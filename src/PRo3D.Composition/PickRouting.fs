namespace PRo3D.Composition

open System.Collections.Concurrent

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI.Primitives

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Navigation2
open PRo3D.Base.Gis
open PRo3D.Base.Annotation

/// What a surface pick does in the active interaction - the part of the Viewer's
/// `matchPickingInteraction` every host shares. Works on sub-models; the host writes the
/// outcome back and adds its own bookkeeping (undo stash, on-screen feedback, camera re-up).
module PickRouting =

    type PickContext =
        {
            interaction : Interactions
            /// false in the Viewer's instrument view: placement/selection picks are ignored there
            standardMode : bool
            /// the camera drawing tools record with each annotation
            view        : CameraView
            shiftFlag   : bool
            mouseScheme : Navigation.MouseScheme
            sendQueue   : BlockingCollection<string>
            /// the hit point
            point       : V3d
            surface     : Surface
            hitF        : V3d -> Option<V3d>
            observed    : Option<SpiceReferenceSystem>
        }

    type PickInputs =
        {
            drawing    : DrawingModel
            surfaces   : SurfaceModel
            refSystem  : ReferenceSystem
            navigation : NavigationModel
            config     : ViewConfigModel
            userPrefs  : UserPreferences
            scenePath  : Option<string>
        }

    type PickOutcome =
        /// a drawing tool consumed the pick; `feedback` is a short note for the user
        | DrawingChanged    of DrawingModel * feedback : Option<string>
        | SurfacesChanged   of SurfaceModel
        /// the coordinate system moved: the host re-ups its camera
        | RefSystemChanged  of ReferenceSystem
        | NavigationChanged of NavigationModel * feedback : Option<string>
        /// the interaction took the pick but nothing changes
        | Unchanged
        /// not a shared interaction - the host's own (rover, scale bar, pivot, ...)
        | Unhandled

    let private drawing (ctx : PickContext) (inputs : PickInputs) (drawingModel : DrawingModel) (msg : DrawingAction) =
        DrawingApp.update inputs.refSystem HostConfigs.drawingConfig ctx.observed ctx.sendQueue ctx.view ctx.shiftFlag drawingModel msg

    let routePick (ctx : PickContext) (inputs : PickInputs) : PickOutcome =
        match ctx.interaction with
        | Interactions.DrawAnnotation ->
            let drawingModel =
                match ctx.surface.surfaceType with
                | SurfaceType.Mesh -> { inputs.drawing with projection = Projection.Linear } //TODO LF ... why is this happening?
                | _ -> inputs.drawing

            let msg = DrawingAction.AddPointAdv(ctx.point, ctx.hitF, ctx.observed, ctx.surface.name, None)
            DrawingChanged (drawing ctx inputs drawingModel msg, None)

        | Interactions.CutAnnotation ->
            // the cut stroke is a plain picked polyline: no segment sampling, so the hit
            // function is not needed - straight preview lines suffice and the boolean op
            // works on the control points alone
            DrawingChanged (drawing ctx inputs inputs.drawing (DrawingAction.AddCutStrokePoint ctx.point), None)

        | Interactions.EditAnnotation ->
            // Grabbing a handle is emitted by the annotation scene graph itself, which is the only
            // place that knows *which* control point is under the cursor. Dropping lands here,
            // because a vertex is normally dropped away from the annotation, on bare surface.
            match inputs.drawing.vertexGrab with
            | Some grab when grab.movedSinceGrab ->
                let msg = DrawingAction.MoveVertex(grab.annotation, grab.pointIndex, ctx.point, ctx.hitF)
                let drawingModel = drawing ctx inputs inputs.drawing msg

                // surfaceName is only ever advisory and nothing re-validates it, so a vertex may
                // legitimately end up on a different surface than the annotation was drawn on -
                // but silently is not the way to do it
                let movedAcrossSurfaces =
                    match inputs.drawing.annotations.flat.TryFind grab.annotation with
                    | Some (Leaf.Annotations a) -> a.surfaceName <> ctx.surface.name
                    | _ -> false

                let feedback =
                    if movedAcrossSurfaces then Some (sprintf "vertex moved onto surface \"%s\"" ctx.surface.name)
                    else None
                DrawingChanged (drawingModel, feedback)
            | _ ->
                // Either nothing is grabbed, or this is the very click that grabbed and the cursor
                // has not moved yet. Both mean "not a drop".
                Unchanged

        | Interactions.PlaceCoordinateSystem when ctx.standardMode ->
            let refSystem, _ =
                ReferenceSystemApp.update
                    inputs.config
                    LenseConfigs.referenceSystemConfig
                    inputs.refSystem
                    (ReferenceSystemAction.UpdateUpNorth ctx.point)
            RefSystemChanged refSystem

        | Interactions.PickExploreCenter when ctx.standardMode ->
            let navigation, feedback =
                Navigation.update
                    inputs.config inputs.refSystem HostConfigs.navConf inputs.userPrefs true None inputs.navigation
                    (Navigation.Action.ArcBallAction(ArcBallController.Message.Pick ctx.point)) ctx.mouseScheme
            NavigationChanged (navigation, feedback)

        | Interactions.PickSurface when ctx.standardMode ->
            let action = SurfaceAppAction.GroupsMessage(GroupsAppAction.SingleSelectLeaf(list.Empty, ctx.surface.guid, ""))
            SurfacesChanged (SurfaceApp.update inputs.surfaces action inputs.scenePath inputs.navigation.camera.view inputs.refSystem)

        | _ ->
            Unhandled
