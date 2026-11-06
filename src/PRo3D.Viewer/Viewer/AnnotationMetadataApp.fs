namespace PRo3D.Viewer

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.ImageMapping

/// Annotation metadata (#887, docs/AnnotationMetadata.md): confidence 1-4, a comment, the
/// shape model and the projected images an annotation was traced on.
///
/// Confidence and comment are set by the user (keys 1-4 and C in the 3D view, or the
/// Properties panel). Shape model and projected images are captured once, when an annotation
/// is finished (`capture`).
module AnnotationMetadataApp =

    // ---------------------------------------------------------------------------------------
    // selection

    /// The green multi-selection, else the single selection; only annotation leaves that exist.
    let selectedAnnotations (drawing : DrawingModel) : list<Annotation> =
        let ids =
            match drawing.annotations.selectedLeaves |> HashSet.toList |> List.map (fun ts -> ts.id) with
            | [] -> drawing.annotations.singleSelectLeaf |> Option.toList
            | multi -> multi
        ids
        |> List.choose (fun id ->
            match HashMap.tryFind id drawing.annotations.flat with
            | Some (Leaf.Annotations a) -> Some a
            | _ -> None)

    /// Whether keys 1-4 and C belong to this app: annotations are selected and none is being
    /// drawn. Otherwise 0-3 keep setting the drawing semantic and C re-centres the orbit.
    let takesKeys (drawing : DrawingModel) =
        drawing.working.IsNone && not (selectedAnnotations drawing).IsEmpty

    /// Replaces the given annotations in one undo step.
    let private replaceAll (drawing : DrawingModel) (updated : list<Annotation>) =
        match updated with
        | [] -> drawing
        | _ ->
            let before = drawing.annotations
            let after =
                updated
                |> List.fold (fun groups a -> GroupsApp.replaceLeaf (Leaf.Annotations a) groups) before
            { drawing with annotations = after }
            |> DrawingApp.pushUndo (SnapshotDelta(before, after))

    let private plural (n : int) = if n = 1 then "annotation" else "annotations"

    /// The centre of an annotation's points, where its comment dialog is anchored.
    let anchorOf (a : Annotation) : Option<V3d> =
        match a.points |> IndexList.toList with
        | [] -> None
        | pts -> Some ((pts |> List.fold (+) V3d.Zero) / float pts.Length)

    // ---------------------------------------------------------------------------------------
    // update

    /// Returns the drawing model, this app's model and an optional short message for the
    /// 3D view.
    let update
        (drawing : DrawingModel)
        (model : AnnotationMetadataModel)
        (msg : AnnotationMetadataAction)
        : DrawingModel * AnnotationMetadataModel * Option<string> =

        match msg with
        | SetConfidence c ->
            match AnnotationMetadata.validConfidence c, selectedAnnotations drawing with
            | None, _ -> drawing, model, None
            | _, [] -> drawing, model, None
            | Some c, selected ->
                let updated =
                    selected |> List.map (fun a -> { a with metadata = { a.metadata with confidence = Some c } })
                replaceAll drawing updated, model,
                Some (sprintf "Confidence %d set on %d %s" c updated.Length (plural updated.Length))

        | ClearConfidence ->
            match selectedAnnotations drawing with
            | [] -> drawing, model, None
            | selected ->
                let updated =
                    selected |> List.map (fun a -> { a with metadata = { a.metadata with confidence = None } })
                replaceAll drawing updated, model,
                Some (sprintf "Confidence cleared on %d %s" updated.Length (plural updated.Length))

        | OpenComment ->
            match selectedAnnotations drawing with
            | [ a ] ->
                match anchorOf a with
                | Some anchor ->
                    drawing, { model with comment = Some { annotation = a.key; initial = a.metadata.comment; anchor = anchor } }, None
                | None -> drawing, model, None
            | [] -> drawing, model, None
            | _ -> drawing, model, Some "Select a single annotation to comment on it"

        | CommitComment text ->
            match model.comment with
            | None -> drawing, model, None
            | Some editor ->
                let model = { model with comment = None }
                // trailing blanks and new lines dropped; blank means no comment
                let text = text.TrimEnd()
                match HashMap.tryFind editor.annotation drawing.annotations.flat with
                | Some (Leaf.Annotations a) when a.metadata.comment <> text ->
                    let a = { a with metadata = { a.metadata with comment = text } }
                    replaceAll drawing [ a ], model,
                    Some (if String.IsNullOrWhiteSpace text then "Comment removed" else "Comment saved")
                | _ -> drawing, model, None

        | CancelComment ->
            drawing, { model with comment = None }, None

        | ToggleConfidenceLabels ->
            drawing, { model with showConfidenceLabels = not model.showConfidenceLabels }, None

    // ---------------------------------------------------------------------------------------
    // capture at finish: shape model and projected images

    /// The shape model of the surface the annotation was drawn on, by name as the annotation
    /// records it. None when the surface is unknown or not an OPC.
    let shapeModelOf (surfaces : SurfaceModel) (surfaceName : string) : Option<ShapeModelRef> =
        surfaces.surfaces.flat
        |> HashMap.toSeq
        |> Seq.tryPick (fun (_, leaf) ->
            match leaf with
            | Leaf.Surfaces s when s.name = surfaceName -> s.opcxPath
            | _ -> None)
        |> Option.map (fun opcx -> OpcMetadata.shapeModelOf opcx (OpcMetadata.tryReadForOpcxCached opcx))

    let private imageRef (image : ProjectedImageModel) : ProjectedImageRef =
        {
            image      = IO.Path.GetFileName image.texture
            instrument = image.instrument
            time       = (Calendar.toUtc image.time).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", Globalization.CultureInfo.InvariantCulture)
        }

    /// How far (m) the first terrain hit seen from a projector may lie from the annotation point
    /// and still count as that point: annotation points are kd-tree picks on the same terrain,
    /// so a visible point is hit within millimetres; the relative part covers long ranges.
    let private visibilityTolerance (distance : float) = max 0.05 (distance * 1.0e-5)

    /// The images painted under an annotation's points: per point, the top image of the
    /// projection stack whose footprint contains the point and from which the point is visible,
    /// each image listed once in order of first appearance.
    ///
    /// The footprint test is the shader's (projector NDC x, y, z all in [-1, 1]). Instead of the
    /// shader's facing test, which depends on the OPC's triangle winding, a ray is cast from the
    /// image's projection centre through the point: the point counts when it is the first
    /// terrain the ray meets. That also excludes terrain the instrument could not see, which the
    /// projection still paints (docs/MultiImageProjection.md, "hidden terrain is painted too").
    ///
    /// `loadedKdTrees` is the viewer's picking cache; the updated cache is returned.
    let projectedImagesUnder
        (gis : Gis.GisApp)
        (surfaces : SurfaceModel)
        (refSystem : ReferenceSystem)
        (loadedKdTrees : HashMap<string, Aardvark.Geometry.ConcreteKdIntersectionTree>)
        (points : list<V3d>)
        : list<ProjectedImageRef> * HashMap<string, Aardvark.Geometry.ConcreteKdIntersectionTree> =

        let list = gis.projectedImageList
        // top first; the hover preview is not part of what is projected
        let stack =
            list.stack
            |> IndexList.toList
            |> List.rev
            |> List.choose (fun id -> ProjectedImageListModel.tryFind id list)

        match stack, points with
        | [], _ | _, [] -> [], loadedKdTrees
        | _ ->
            let inFootprint (ctx : ImageInspector.Context) (p : V3d) =
                let b = ctx.toWorld.Backward.TransformPos p
                let h = ctx.full.Forward.Transform(V4d(b.X, b.Y, b.Z, 1.0))
                if h.W = 0.0 || not (Double.IsFinite h.W) then false
                else
                    let n = h.XYZ / h.W
                    abs n.X <= 1.0 && abs n.Y <= 1.0 && abs n.Z <= 1.0

            // The projection centre, render space. A perspective projector maps its centre to
            // clip w = 0, so every clip point (0, 0, z, 0) maps back to it; None for a degenerate
            // (non-perspective) matrix.
            let projectionCentre (ctx : ImageInspector.Context) =
                let e = ctx.full.Backward.Transform(V4d(0.0, 0.0, 1.0, 0.0))
                if abs e.W < 1.0e-12 || not (Double.IsFinite e.W) then None
                else Some (ctx.toWorld.Forward.TransformPos (e.XYZ / e.W))

            // per image once: its projector and centre. Images whose footprint holds none of the
            // points are dropped before any ray is cast.
            let contexts =
                stack
                |> List.choose (fun image ->
                    match ImageInspector.tryContextFor gis surfaces refSystem image with
                    | Ok ctx when points |> List.exists (inFootprint ctx) ->
                        projectionCentre ctx |> Option.map (fun centre -> image, ctx, centre)
                    | Ok _ -> None
                    | Result.Error e ->
                        Log.warn "[AnnotationMetadata] %s: no projector (%s)" (IO.Path.GetFileName image.texture) e
                        None)

            let observedSystem = Gis.GisApp.getSpiceReferenceSystem gis
            let observerSystem = Gis.GisApp.getObserverSystem gis
            let visibleSurface = fun _ (leaf : Leaf) _ ->
                match leaf with
                | Leaf.Surfaces s -> s.isActive && s.isVisible
                | _ -> false

            // the picking cache, extended by the kd-trees this loads
            let cache = ref loadedKdTrees

            let visibleFrom (origin : V3d) (p : V3d) =
                    let toP = p - origin
                    let distance = Vec.length toP
                    if distance <= 0.0 then false
                    else
                        let r = FastRay3d(Ray3d(origin, toP / distance))
                        let hit, c =
                            SurfaceIntersection.doKdTreeIntersection surfaces refSystem observedSystem observerSystem r visibleSurface cache.Value false
                        cache.Value <- c
                        match hit with
                        | Some info ->
                            let hitPoint = r.Ray.GetPointOnRay info.hit.RayHit.T
                            Vec.distance hitPoint p <= visibilityTolerance distance
                        | None -> false

            let used =
                points
                |> List.choose (fun p ->
                    contexts
                    |> List.tryPick (fun (image, ctx, centre) ->
                        if inFootprint ctx p && visibleFrom centre p then Some image else None))
                |> List.distinctBy (fun image -> image.id)
                |> List.map imageRef

            used, cache.Value

    /// Fills in shape model and projected images of an annotation just finished. Confidence and
    /// comment are left as they are.
    let capture
        (gis : Gis.GisApp)
        (surfaces : SurfaceModel)
        (refSystem : ReferenceSystem)
        (loadedKdTrees : HashMap<string, Aardvark.Geometry.ConcreteKdIntersectionTree>)
        (a : Annotation)
        : Annotation * HashMap<string, Aardvark.Geometry.ConcreteKdIntersectionTree> =
        let images, cache =
            try
                projectedImagesUnder gis surfaces refSystem loadedKdTrees (a.points |> IndexList.toList)
            with e ->
                Log.warn "[AnnotationMetadata] projected images could not be determined: %s" e.Message
                [], loadedKdTrees
        let shapeModel =
            try shapeModelOf surfaces a.surfaceName
            with e ->
                Log.warn "[AnnotationMetadata] shape model could not be read: %s" e.Message
                None
        { a with metadata = { a.metadata with shapeModel = shapeModel; projectedImagesUsed = images } }, cache

    /// Applies `capture` to the annotation `finished` names, in place in the drawing model,
    /// and in the undo entry that added it, so redo brings it back with its metadata.
    let captureFinished
        (gis : Gis.GisApp)
        (surfaces : SurfaceModel)
        (refSystem : ReferenceSystem)
        (loadedKdTrees : HashMap<string, Aardvark.Geometry.ConcreteKdIntersectionTree>)
        (finished : Option<Annotation>)
        (drawing : DrawingModel)
        : DrawingModel * HashMap<string, Aardvark.Geometry.ConcreteKdIntersectionTree> =
        match finished |> Option.bind (fun f -> HashMap.tryFind f.key drawing.annotations.flat) with
        | Some (Leaf.Annotations a) ->
            let a, cache = capture gis surfaces refSystem loadedKdTrees a
            let leaf = Leaf.Annotations a
            let undoStack =
                match drawing.undoStack with
                | LeafAdded (l, path) :: rest when l.id = a.key -> LeafAdded (leaf, path) :: rest
                | stack -> stack
            { drawing with
                annotations = GroupsApp.replaceLeaf leaf drawing.annotations
                undoStack   = undoStack }, cache
        | _ -> drawing, loadedKdTrees

    // ---------------------------------------------------------------------------------------
    // view

    /// The global switch for confidence labels, and the keys (#887).
    let viewSettings (model : aval<AnnotationMetadataModel>) : DomNode<AnnotationMetadataAction> =
        let showLabels = model |> AVal.map (fun m -> m.showConfidenceLabels)
        div [] [
            Aardvark.UI.Primitives.Html.table [
                Aardvark.UI.Primitives.Html.row "Confidence labels:" [ GuiEx.iconCheckBox showLabels ToggleConfidenceLabels ]
            ]
            div [ style "opacity:0.8; margin-top:4px" ] [
                text "With annotations selected in the 3D view: 1-4 rate them, C comments on one. While drawing, 0-3 set the semantic as before."
            ]
        ]

    /// Where a render-space point appears in the 3D view, as fractions of its width and height
    /// (0, 0 top left; 1, 1 bottom right); None behind the camera. Fractions rather than
    /// pixels: the dialog is placed in percent of the view, so the view's pixel size, which
    /// the model keeps per page instance, is not needed.
    let screenPosition (view : CameraView) (frustum : Frustum) (p : V3d) : Option<V2d> =
        let vp = CameraView.viewTrafo view * Frustum.projTrafo frustum
        let h = vp.Forward.Transform(V4d(p.X, p.Y, p.Z, 1.0))
        if h.W <= 0.0 then None
        else
            let ndc = h.XY / h.W
            Some (V2d(ndc.X * 0.5 + 0.5, 0.5 - ndc.Y * 0.5))

    /// Client side: Enter commits the input's value, Escape cancels; the keys do not travel on
    /// to the page (shortcuts) or the 3D view.
    /// Other keys bubble on as usual: the page's shortcut handlers leave text fields alone.
    let private keyHandler (commit : string -> 'msg) (cancel : 'msg) =
        // the dialog is about to go; give the keys back to the 3D view, so 1-4 and C work on
        // without moving the mouse out and in again
        let refocus = "var rc = document.querySelector('.mainrendercontrol'); if (rc) setTimeout(function () { rc.focus(); }, 0);"
        "onkeydown",
        AttributeValue.Event {
            clientSide = fun send id ->
                String.concat "" [
                    "if(event.key==='Enter'&&!event.shiftKey){event.preventDefault();event.stopPropagation();"
                    send id [ "'enter'"; "this.value" ]
                    refocus
                    "}else if(event.key==='Escape'){event.preventDefault();event.stopPropagation();"
                    send id [ "'escape'"; "''" ]
                    refocus
                    "}"
                ]
            serverSide = fun _ _ args ->
                match args with
                | key :: value :: _ ->
                    match Pickler.unpickleOfJson<string> key with
                    | "enter"  -> Seq.delay (fun () -> Seq.singleton (commit (Pickler.unpickleOfJson<string> value)))
                    | "escape" -> Seq.singleton cancel
                    | _ -> Seq.empty
                | _ -> Seq.empty
        }

    /// The comment dialog, floating next to the annotation in the 3D view. Only its position
    /// follows the camera (`position`, fractions of the view, see screenPosition); the DOM is
    /// built once per opening, so the text being typed survives camera moves.
    let commentDialog
        (editor : aval<Option<CommentEditor>>)
        (position : aval<Option<V2d>>)
        (lift : AnnotationMetadataAction -> 'msg)
        : DomNode<'msg> =
        let boxStyle =
            position |> AVal.map (fun pos ->
                // 24 px right of the annotation's centre, clamped into the view
                let left, top =
                    match pos with
                    | Some p ->
                        sprintf "min(calc(%.3f%% + 24px), calc(100%% - 340px))" (100.0 * p.X),
                        sprintf "max(0px, calc(%.3f%% - 20px))" (100.0 * p.Y)
                    | None   -> "40%", "40%"
                sprintf "position:absolute; left:%s; top:%s; z-index:20; width:320px; background:rgba(30,30,30,0.95); border:1px solid #888; border-radius:4px; padding:6px; color:white; font-size:small" left top)

        let dialog (ed : CommentEditor) =
            let attributes =
                AttributeMap.ofAMap (amap {
                    yield clazz "annotation-comment-dialog"
                    let! s = boxStyle
                    yield style s
                })
            let box =
                Incremental.div attributes (AList.ofList [
                    div [ style "margin-bottom:4px; opacity:0.8" ] [ text "Comment - Enter saves, Shift+Enter new line, Esc cancels" ]
                    textarea [
                        attribute "rows" "3"
                        style "width:100%; resize:vertical; background:#222; color:white; border:1px solid #666; font-family:inherit"
                        keyHandler (CommitComment >> lift) (lift CancelComment)
                    ] ed.initial
                    |> onBoot "var el = document.getElementById('__ID__'); el.focus(); el.setSelectionRange(el.value.length, el.value.length);"
                    div [ style "margin-top:4px; text-align:right" ] [
                        button [ clazz "ui mini button"; onClick (fun _ -> lift CancelComment) ] [ text "Cancel" ]
                        // the textarea's value lives on the client: OK presses Enter in it
                        button [ clazz "ui mini primary button"
                                 clientEvent "onclick" "var t = $(this).closest('.annotation-comment-dialog').find('textarea')[0]; t.dispatchEvent(new KeyboardEvent('keydown', {key:'Enter', bubbles:true}));" ] [ text "OK" ]
                    ]
                ])
            // clicks and the wheel on the dialog must not reach the 3D view
            box |> onBoot "$('#__ID__').on('mousedown mouseup click dblclick contextmenu wheel', e => e.stopPropagation())"

        let content =
            editor |> AVal.map (fun e ->
                match e with
                | None    -> IndexList.empty
                | Some ed -> IndexList.single (dialog ed))
        Incremental.div AttributeMap.empty (AList.ofAVal content)
