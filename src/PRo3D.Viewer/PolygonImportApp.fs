namespace PRo3D.Viewer

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Base.Gis
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Core.Gis

/// The polygon CSV import window (docs/PolygonCsvImport.md). Session-only, like annotationExport.
type PolygonImportModel = {
    isOpen  : bool
    options : PolygonCsvImporter.ImportOptions
}

module PolygonImportModel =
    let initial = { isOpen = false; options = PolygonCsvImporter.ImportOptions.initial }

type PolygonImportAction =
    | ShowImportWindow
    | HideImportWindow
    | ToggleSnapVertices
    | ToggleDrapeEdges
    | ToggleDipAndStrike
    /// non-finite values (an unparsable entry) are ignored
    | SetSpacing of float
    /// files chosen in the window; imports them with its options and closes it
    | ImportFiles of list<string>

/// The polygon CSV import as a sub-app: window state, the import itself and the window.
/// The parser, snapping and annotation building are `PolygonCsvImporter` (PRo3D.Core); this
/// adds what needs the scene: surfaces to cast against, the GIS frame, the annotation tree.
module PolygonImportApp =

    /// Window and option actions. `ImportFiles` needs the scene and goes through `importFiles`.
    let update (m : PolygonImportModel) (action : PolygonImportAction) : PolygonImportModel =
        let o = m.options
        match action with
        | ShowImportWindow   -> { m with isOpen = true }
        | HideImportWindow   -> { m with isOpen = false }
        | ToggleSnapVertices -> { m with options = { o with snapVertices = not o.snapVertices } }
        | ToggleDrapeEdges   -> { m with options = { o with drapeEdges = not o.drapeEdges } }
        | ToggleDipAndStrike -> { m with options = { o with dipAndStrike = not o.dipAndStrike } }
        | SetSpacing spacing when Double.IsFinite spacing ->
            { m with options = { o with spacing = max PolygonCsvImporter.ImportOptions.minSpacing spacing } }
        | SetSpacing _       -> m
        | ImportFiles _      -> { m with isOpen = false }

    /// What the import reads from the scene.
    type SceneContext = {
        surfaces          : SurfaceModel
        referenceSystem   : ReferenceSystem
        gis               : GisApp
        diagnosticTimings : bool
    }

    /// Imports `paths` into `drawing` (one undo step for all of them). Returns the drawing,
    /// the KdTree cache after the casts (the viewer's `Picking.cache`) and the notice text.
    let importFiles
        (scene   : SceneContext)
        cache // the KdTree cache, HashMap<string, ConcreteKdIntersectionTree>, as doKdTreeIntersection takes it
        (options : PolygonCsvImporter.ImportOptions)
        (paths   : list<string>)
        (drawing : DrawingModel) =

        let rs = scene.referenceSystem
        let observerSystem = GisApp.getObserverSystem scene.gis
        let observedSystem (v : SurfaceId) = GisApp.getSpiceReferenceSystem scene.gis v
        let isTarget (l : Leaf) = l.active && l.visible

        // The CSV is in the body frame of the surfaces. With a GIS observer set, surfaces are
        // placed in the observer's frame by a SPICE transform; drawn annotations carry the
        // surface's body frame (`referenceSystem`) and are placed the same way. Imported
        // polygons do too: the body frame of the active, visible surfaces (the first, if they
        // disagree), rays cast through the transform as picking does (`toLocal`).
        let frame, frameNote =
            match observerSystem with
            | None -> None, ""
            | Some _ ->
                let frames =
                    scene.surfaces.surfaces.flat
                    |> HashMap.toList
                    |> List.choose (fun (id, l) -> if isTarget l then observedSystem id else None)
                    |> List.distinct
                match frames with
                | [] -> None, ""
                | [ f ] -> Some f, ""
                | f :: _ ->
                    let (EntitySpiceName n) = f.body
                    Some f, sprintf " (surfaces in %d body frames; polygons placed in %s's, only its surfaces hit)" frames.Length n
        let spiceTrafo =
            match frame, observerSystem with
            | Some observed, Some observer ->
                CooTransformation.transformBody observed.body (Some observed.referenceFrame) observer.body observer.referenceFrame observer.time
                |> Option.map (fun t -> t.Trafo)
                |> Option.defaultValue Trafo3d.Identity
            | _ -> Trafo3d.Identity

        let cache = ref cache

        /// ray in the body frame -> hit in the body frame
        let cast =
            let filter (id : Guid) (l : Leaf) (_ : SgSurface) =
                isTarget l && (frame.IsNone || observedSystem id = frame)
            fun (origin : V3d) (dir : V3d) ->
                let ray = FastRay3d(spiceTrafo.Forward.TransformPos origin, spiceTrafo.Forward.TransformDir dir)
                let hit, c =
                    SurfaceIntersection.doKdTreeIntersection
                        scene.surfaces rs observedSystem observerSystem ray filter cache.Value scene.diagnosticTimings
                cache.Value <- c
                hit |> Option.map (fun h -> ray.Ray.GetPointOnRay h.hit.RayHit.T |> spiceTrafo.Backward.TransformPos)

        let sky = PolygonCsvImporter.Sky.ofPlanet rs.planet

        let importOne (annotations : GroupsModel) (path : string) =
            let fileName = IO.Path.GetFileName path
            try
                match PolygonCsvImporter.parseFile path with
                | Result.Error e ->
                    Log.error "[PolygonImport] %s: %s" fileName e
                    annotations, sprintf "%s: not imported (%s)" fileName e
                | Result.Ok parsed ->
                    for w in parsed.warnings do Log.warn "[PolygonImport] %s: %s" fileName w
                    let built =
                        PolygonCsvImporter.build rs.up.value rs.north.value rs.planet frame cast sky options parsed.polygons
                    match built.annotations with
                    | [] ->
                        annotations, sprintf "%s: no polygons (%d rows skipped, see log)" fileName parsed.warnings.Length
                    | imported ->
                        let group, flat, lookup = PolygonCsvImporter.toGroup fileName imported
                        let annotations =
                            { annotations with
                                rootGroup    = { annotations.rootGroup with subNodes = annotations.rootGroup.subNodes |> IndexList.add group }
                                groupsLookup = HashMap.union annotations.groupsLookup lookup
                                flat         = HashMap.union annotations.flat flat }
                        let text =
                            sprintf "%s: %d polygons%s%s%s%s%s" fileName imported.Length frameNote
                                (if parsed.warnings.IsEmpty then "" else sprintf ", %d rows/polygons skipped (see log)" parsed.warnings.Length)
                                (if built.missedVertices > 0 then sprintf ", %d vertices missed the surface and were left in place" built.missedVertices else "")
                                (if built.missedSamples > 0 then sprintf ", %d in-between points missed the surface and were left out" built.missedSamples else "")
                                (if built.coarsenedEdges > 0 then sprintf ", %d long edges sampled coarser (at most %d points per edge)" built.coarsenedEdges PolygonCsvImporter.ImportOptions.maxSamplesPerEdge else "")
                        Log.line "[PolygonImport] %s" text
                        annotations, text
            with e ->
                Log.error "[PolygonImport] %s: %A" fileName e
                annotations, sprintf "%s: not imported (%s)" fileName e.Message

        let before = drawing.annotations
        let after, messages =
            paths |> List.fold (fun (a, msgs) path -> let a, t = importOne a path in a, t :: msgs) (before, [])
        let drawing =
            if obj.ReferenceEquals(before, after) then drawing
            else { drawing with annotations = after } |> DrawingApp.pushUndo (SnapshotDelta(before, after))
        drawing, cache.Value, (messages |> List.rev |> String.concat "; ")

    // ---------------------------------------------------------------- view

    let private jsOpenPolygonCsv =
        "top.aardvark.dialog.showOpenDialog({title:'Import polygons (CSV)', filters: [{ name: 'Polygon tables', extensions: ['csv', 'txt', 'tsv']}, { name: 'All files', extensions: ['*']}], properties: ['openFile', 'multiSelections']}).then(result => {top.aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

    /// Tick box and label on one line, with a tooltip on the label. The label is a plain
    /// span (iconCheckBox is an icon, not an <input>, so an HTML <label> cannot target it);
    /// it gets the same click action, so clicking the text toggles too.
    let private checkBox (label : string) (tip : string) (isSet : aval<bool>) (action : PolygonImportAction) =
        div [ style "display:flex; align-items:center; gap:6px; padding:4px 0" ] [
            GuiEx.iconCheckBox isSet action
            span [ style "line-height:1.2; cursor:pointer; user-select:none"; attribute "title" tip
                   onClick (fun _ -> action) ] [ text label ]
        ]

    let private spacingInput (model : aval<PolygonImportModel>) =
        // only what the input shows: ticking the other boxes must not touch it
        let state = model |> AVal.map (fun p -> p.options.spacing, p.options.drapeEdges)
        let attributes =
            amap {
                let! spacing, drape = state
                yield attribute "type" "number"
                yield attribute "min" (string PolygonCsvImporter.ImportOptions.minSpacing)
                yield attribute "step" "0.1"
                yield attribute "value" (spacing.ToString(Globalization.CultureInfo.InvariantCulture))
                yield style "width:90px; color:black"
                if not drape then yield attribute "disabled" "disabled"
                yield onChange (fun v ->
                    match Double.TryParse(v, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                    | true, f -> SetSpacing f
                    | _ -> SetSpacing nan)
            } |> AttributeMap.ofAMap
        Incremental.input attributes

    let private window (model : aval<PolygonImportModel>) =
        let snap  = model |> AVal.map (fun p -> p.options.snapVertices)
        let drape = model |> AVal.map (fun p -> p.options.drapeEdges)
        let dns   = model |> AVal.map (fun p -> p.options.dipAndStrike)
        require GuiEx.semui (
            div [ clazz "ui inverted segment"; style "min-width:420px; max-width:520px; padding:0; margin:0" ] [
                div [ clazz "ui inverted header"
                      style "margin:0; padding:12px; border-bottom:1px solid rgba(255,255,255,0.15)" ] [
                    text "Import polygons (CSV)"
                ]
                div [ style "padding:10px 12px" ] [
                    div [ style "opacity:0.8; padding-bottom:8px" ] [
                        text "One row per vertex: polygon_id, x, y, z in metres, body-fixed frame of the scene. See docs/PolygonCsvImport.md."
                    ]
                    checkBox "Snap vertices to the surface"
                        "Move every vertex onto the active surfaces along the sky (local up, as the Sky projection): nearer hit of the ray down and the ray up."
                        snap ToggleSnapVertices
                    checkBox "Sky-project in-between points along the edges"
                        "Add points along every edge and drop them onto the surface, like a polygon drawn with sky projection. Without this, edges are straight lines."
                        drape ToggleDrapeEdges
                    div [ style "display:flex; align-items:center; gap:8px; padding:4px 0 0 24px" ] [
                        span [] [ text "spacing" ]
                        spacingInput model
                        span [] [ text (sprintf "m (at least %g)" PolygonCsvImporter.ImportOptions.minSpacing) ]
                    ]
                    checkBox "Compute dip & strike"
                        "Fit a plane to each polygon's vertices, as for a drawn polygon (dip & strike panel, colour by dip). A regression per polygon: slow for many thousands."
                        dns ToggleDipAndStrike
                ]
                div [ style "display:flex; gap:8px; justify-content:flex-end; padding:10px 12px; border-top:1px solid rgba(255,255,255,0.15)" ] [
                    button [ clazz "ui button"; onClick (fun _ -> HideImportWindow) ] [ text "Cancel" ]
                    button [ clazz "ui primary button"
                             Dialogs.onChooseFiles ImportFiles
                             clientEvent "onclick" jsOpenPolygonCsv ] [ text "Choose files..." ]
                ]
            ]
        )

    /// Centred overlay like the export window; absent from the DOM while closed. Bound to
    /// `isOpen` only, so changing an option updates the window instead of rebuilding it.
    let view (model : aval<PolygonImportModel>) : DomNode<PolygonImportAction> =
        let isOpen = model |> AVal.map (fun p -> p.isOpen)
        Incremental.div AttributeMap.empty (
            alist {
                let! isOpen = isOpen
                if isOpen then
                    yield div [
                        style "position:fixed; inset:0; background:rgba(0,0,0,0.55); z-index:20000; display:flex; align-items:center; justify-content:center"
                        onClick (fun _ -> HideImportWindow)
                    ] [
                        onBoot "$('#__ID__').on('click', function(e) { e.stopPropagation(); });" (
                            div [ style "display:flex" ] [ window model ])
                    ]
            })
