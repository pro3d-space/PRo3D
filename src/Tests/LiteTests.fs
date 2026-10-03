/// PRo3D Lite (docs/PRo3DLite.md): its update loop, driven headless. Picking and rendering in
/// the browser are covered by tests-ui/tests/lite.spec.ts; these pin the model side.
module PRo3D.Tests.LiteTests

open System
open System.Collections.Concurrent
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Animation.Deprecated
open FSharp.Data.Adaptive
open Expecto

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Composition
open PRo3D.Lite

let private sendQueue = new BlockingCollection<string>()

let private fresh () = LiteApp.initial UserPreferences.initial

let private annotationCount (m : LiteModel) = m.drawing.annotations.flat |> HashMap.count

/// an AddPointAdv as a pick on `p` would send it (identity re-projection)
let private click (p : V3d) = DrawingMsg (DrawingAction.AddPointAdv(p, Some, None, "test-surface", None))

let private modelTests =
    // no GL needed: the update only touches the runtime when it loads surfaces
    let update (m : LiteModel) (msg : LiteAction) =
        LiteApp.update Unchecked.defaultof<IRuntime> Unchecked.defaultof<IFramebufferSignature> sendQueue m msg

    testList "model" [

        test "starts empty, drawing" {
            let m = fresh ()
            Expect.isTrue (HashMap.isEmpty m.surfaces.surfaces.flat) "no surfaces"
            Expect.equal m.interaction Interactions.DrawAnnotation "draw tool"
            Expect.isNone m.scenePath "unsaved"
        }

        test "Ctrl arms the tools while it is held" {
            let m = update (fresh ()) (KeyDownMsg Keys.LeftCtrl)
            Expect.isTrue (LiteApp.toolArmed m) "armed"
            Expect.isFalse (LiteApp.toolArmed (update m (KeyUpMsg Keys.LeftCtrl))) "released"
        }

        test "every offered tool can be selected" {
            let m = { fresh () with drawing = Draw.startTool Draw.refSystemFlat Geometry.Point }
            for interaction, _, _ in LiteApp.tools do
                let m' = update m (SetInteraction interaction)
                Expect.equal m'.interaction interaction "selected"
        }

        test "a line drawn, undone with Ctrl+Z and redone with Ctrl+Y" {
            let m = { fresh () with refSystem = Draw.refSystemFlat }
            let m = update m (DrawingMsg (DrawingAction.SetGeometry Geometry.Line))
            let m = update m (click (V3d(0.0, 0.0, 0.0)))
            let m = update m (click (V3d(3.0, 4.0, 0.0)))
            Expect.equal (annotationCount m) 1 "a line auto-finishes after two points"

            let m = update m (KeyDownMsg Keys.LeftCtrl)
            let undone = update m (KeyDownMsg Keys.Z)
            Expect.equal (annotationCount undone) 0 "undone"
            let redone = update undone (KeyDownMsg Keys.Y)
            Expect.equal (annotationCount redone) 1 "redone"
        }

        test "Escape discards a half-drawn annotation, Backspace removes its last point" {
            let m = { fresh () with refSystem = Draw.refSystemFlat }
            let m = update m (DrawingMsg (DrawingAction.SetGeometry Geometry.Polyline))
            let m = update m (click V3d.Zero)
            let m = update m (click V3d.IOO)
            let m = update m (click V3d.IIO)
            let pointsOf (m : LiteModel) = m.drawing.working |> Option.map (fun a -> a.points.Count) |> Option.defaultValue 0
            Expect.equal (pointsOf m) 3 "three working points"
            Expect.equal (pointsOf (update m (KeyDownMsg Keys.Back))) 2 "backspace"
            Expect.isNone (update m (KeyDownMsg Keys.Escape)).drawing.working "escape"
            Expect.equal (annotationCount (update m (KeyDownMsg Keys.Enter))) 1 "enter finishes"
        }

        test "hovering arms a grabbed control point, so the next click drops it" {
            let m = { fresh () with refSystem = Draw.refSystemFlat; interaction = Interactions.EditAnnotation }
            let m = update m (DrawingMsg (DrawingAction.SetGeometry Geometry.Line))
            let m = update m (click V3d.Zero)
            let m = update m (click V3d.IOO)
            let id = m.drawing.annotations.flat |> HashMap.keys |> Seq.head
            let grabbed = update m (DrawingMsg (DrawingAction.GrabVertex(id, 1)))
            Expect.equal (grabbed.drawing.vertexGrab |> Option.map (fun g -> g.movedSinceGrab)) (Some false) "grabbed, not yet armed"

            let armed = update grabbed SurfaceHover
            Expect.equal (armed.drawing.vertexGrab |> Option.map (fun g -> g.movedSinceGrab)) (Some true) "armed by the hover"
            Expect.isTrue (obj.ReferenceEquals((update m SurfaceHover).drawing, m.drawing)) "no grab: hover changes nothing"

            // the drop itself is the shared pick routing (PickRouting.routePick, EditAnnotation)
            let target = V3d(5.0, 5.0, 0.0)
            let ctx : PickRouting.PickContext =
                { interaction = Interactions.EditAnnotation; standardMode = true; view = armed.navigation.camera.view
                  shiftFlag = false; mouseScheme = Navigation.MouseScheme.full; sendQueue = sendQueue
                  point = target; surface = makeSurface "test-surface"; hitF = Some; observed = None }
            let inputs : PickRouting.PickInputs =
                { drawing = armed.drawing; surfaces = armed.surfaces; refSystem = armed.refSystem
                  navigation = armed.navigation; config = armed.config; userPrefs = armed.userPrefs; scenePath = None }
            match PickRouting.routePick ctx inputs with
            | PickRouting.DrawingChanged (drawing, _) ->
                match drawing.annotations.flat |> HashMap.tryFind id with
                | Some (Leaf.Annotations a) -> Expect.equal (a.points |> IndexList.toList |> List.last) target "vertex moved"
                | _ -> failtest "the annotation is gone"
            | other -> failtestf "expected a drop, got %A" other
        }

        test "fly to annotation animates the camera" {
            let m = { fresh () with refSystem = Draw.refSystemFlat }
            let m = update m (DrawingMsg (DrawingAction.SetGeometry Geometry.Line))
            let m = update m (click V3d.Zero)
            let m = update m (click V3d.IOO)
            let id = m.drawing.annotations.flat |> HashMap.keys |> Seq.head
            let flying = update m (DrawingMsg (DrawingAction.FlyToAnnotation id))
            Expect.isTrue (AnimationApp.shouldAnimate flying.animations) "a fly-to animation runs"
        }

        test "removing an annotation through the actions buttons is undoable" {
            let m = { fresh () with refSystem = Draw.refSystemFlat }
            let m = update m (DrawingMsg (DrawingAction.SetGeometry Geometry.Line))
            let m = update m (click V3d.Zero)
            let m = update m (click V3d.IOO)
            let id = m.drawing.annotations.flat |> HashMap.keys |> Seq.head
            let removed = update m (DrawingMsg (DrawingAction.GroupsMessage (GroupsAppAction.RemoveLeaf(id, []))))
            Expect.equal (annotationCount removed) 0 "removed"
            Expect.equal (annotationCount (update removed (DrawingMsg DrawingAction.Undo))) 1 "undone"
        }

        test "Page Up / Page Down step the navigation sensitivity and say so" {
            let m = fresh ()
            let s0 = m.config.navigationSensitivity.value
            let up = update m (KeyDownMsg Keys.PageUp)
            Expect.floatClose Accuracy.high up.config.navigationSensitivity.value (s0 + ConfigProperties.sensitivityStep) "up"
            Expect.stringContains up.status "Navigation sensitivity" "status"
            let down = update (update up (KeyDownMsg Keys.PageDown)) (KeyDownMsg Keys.PageDown)
            Expect.floatClose Accuracy.high down.config.navigationSensitivity.value (s0 - ConfigProperties.sensitivityStep) "down"
        }

        test "the rendered frustum follows the configured clip planes" {
            let m = fresh ()
            Expect.floatClose Accuracy.medium m.config.frustumModel.frustum.near m.config.nearPlane.value "consistent from the start"
            let m' = update m (ConfigMsg (ConfigProperties.Action.SetNearPlane (Numeric.Action.SetValue 2.5)))
            Expect.floatClose Accuracy.medium m'.config.frustumModel.frustum.near 2.5 "near plane"
            let m'' = update m' (ConfigMsg (ConfigProperties.Action.SetFarPlane (Numeric.Action.SetValue 12345.0)))
            Expect.floatClose Accuracy.medium m''.config.frustumModel.frustum.far 12345.0 "far plane"
            Expect.floatClose Accuracy.medium
                (Frustum.horizontalFieldOfViewInDegrees m''.config.frustumModel.frustum)
                (Frustum.horizontalFieldOfViewInDegrees m.config.frustumModel.frustum) "field of view kept"
        }

        test "a planet change recomputes the measurements" {
            GeoJsonRework.Tests.init ()
            let at (x : float) (y : float) = V3d(3396190.0, x, y)
            let m = { fresh () with refSystem = Draw.refSystemMars }
            let m = update m (DrawingMsg (DrawingAction.SetGeometry Geometry.Line))
            let m = update m (click (at 0.0 0.0))
            let m = update m (click (at 30.0 40.0))
            let resultsOf (m : LiteModel) =
                m.drawing.annotations.flat |> HashMap.toList |> List.choose (fun (_, l) -> match l with Leaf.Annotations a -> a.results | _ -> None)
            let before = resultsOf m
            let m' = update m (RefSystemMsg (ReferenceSystemAction.SetPlanet Planet.None))
            Expect.equal m'.refSystem.planet Planet.None "planet"
            Expect.notEqual (resultsOf m') before "results recomputed for the new frame"
        }

        test "saving annotations writes a file full PRo3D reads" {
            let m = { fresh () with refSystem = Draw.refSystemMars }
            GeoJsonRework.Tests.init ()
            let at (x : float) (y : float) = V3d(3396190.0, x, y)
            let m = update m (DrawingMsg (DrawingAction.SetGeometry Geometry.Line))
            let m = update m (click (at 0.0 0.0))
            let m = update m (click (at 30.0 40.0))
            let path = Path.Combine(Path.GetTempPath(), sprintf "lite-%s.pro3d.ann" (Guid.NewGuid().ToString "N"))
            try
                let _ = update m (SaveAnnotations path)
                match AnnotationFiles.tryLoad path with
                | Ok annotations -> Expect.equal (annotations.annotations.flat |> HashMap.count) 1 "one annotation"
                | Result.Error e -> failtestf "unreadable: %s" e
                let loaded = update (fresh ()) (LoadAnnotations [ path ])
                Expect.equal (annotationCount loaded) 1 "and Lite loads it back"
            finally
                if File.Exists path then File.Delete path
        }
    ]

/// with the real MSL OPC: import, framing, scene save and reopen
let private dataTests =
    match Render.skipReason () with
    | Some reason -> testList "with data" [ test "skipped" { skiptest reason } ]
    | None ->
        let runtime, signature = Render.context.Value |> Option.get
        let update = LiteApp.update runtime signature sendQueue

        testSequenced <| testList "with data" [

            test "importing an OPC frames it" {
                let m = update (fresh ()) (ImportOpcs [ Render.opcSurfaceDir ])
                Expect.equal (m.surfaces.surfaces.flat |> HashMap.count) 1 "one surface"
                Expect.notEqual m.navigation.camera.view.Location (fresh ()).navigation.camera.view.Location "camera moved onto the data"
                match LiteApp.homeView m with
                | Some home -> Expect.equal m.navigation.camera.view.Location home.Location "the first import looks from home"
                | None -> failtest "home view of an imported surface"
            }

            test "Home flies back after looking away" {
                let m = update (fresh ()) (ImportOpcs [ Render.opcSurfaceDir ])
                let away = update m (NavigationMsg (Navigation.Action.SetNavigationMode NavigationMode.FreeFly))
                let flying = update away Home
                Expect.isTrue (AnimationApp.shouldAnimate flying.animations) "a fly-to animation runs"
            }

            test "save scene as, then open it in a fresh Lite" {
                let m = update (fresh ()) (ImportOpcs [ Render.opcSurfaceDir ])
                let m = { m with refSystem = { m.refSystem with planet = Planet.Mars } }
                let path = Path.Combine(Path.GetTempPath(), sprintf "lite-%s.pro3d" (Guid.NewGuid().ToString "N"))
                try
                    let saved = update m (SaveSceneAs path)
                    Expect.equal saved.scenePath (Some path) "scene path set"
                    Expect.isTrue (File.Exists (AnnotationFiles.sidecarOf path)) "annotation sidecar written"

                    let reopened = update (fresh ()) (OpenScene [ path ])
                    Expect.equal (reopened.surfaces.surfaces.flat |> HashMap.count) 1 "the surface"
                    Expect.equal (reopened.surfaces.sgSurfaces |> HashMap.count) 1 "with its scene graph"
                    Expect.equal reopened.navigation.camera.view.Location m.navigation.camera.view.Location "the camera"
                    Expect.equal reopened.scenePath (Some path) "the path"
                finally
                    for f in [ path; AnnotationFiles.sidecarOf path ] do
                        if File.Exists f then File.Delete f
            }

            test "a missing scene leaves the model and says so" {
                let m = fresh ()
                let m' = update m (OpenScene [ Path.Combine(Path.GetTempPath(), "no-such-scene.pro3d") ])
                Expect.isTrue (HashMap.isEmpty m'.surfaces.surfaces.flat) "nothing loaded"
                Expect.stringContains m'.status "Cannot open" "status"
            }
        ]

let tests () =
    testList "lite" [
        modelTests
        dataTests
    ]
