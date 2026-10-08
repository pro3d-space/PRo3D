/// PRo3D.Composition - the host glue the Viewer and PRo3D.Lite share. These pin the pieces
/// that were lifted out of the Viewer so both hosts keep the same behaviour.
module PRo3D.Tests.CompositionTests

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open Expecto

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.Composition

let private tempFile (ext : string) =
    Path.Combine(Path.GetTempPath(), sprintf "pro3d-composition-%s%s" (Guid.NewGuid().ToString("N")) ext)

let private pointsOf (m : DrawingModel) =
    Draw.annotations m
    |> List.map (fun a -> a.key, a.points |> IndexList.toList)
    |> List.sortBy fst

let annotationFiles =
    testList "AnnotationFiles" [

        test "the sidecar of a scene is <scene>.pro3d.ann" {
            let scene = Path.Combine("data", "scene.pro3d")
            Expect.equal (AnnotationFiles.sidecarOf scene) (Path.Combine("data", "scene.pro3d.ann")) "sidecar path"
        }

        test "save then load reproduces the annotations" {
            // a polyline in an exactly horizontal plane has an undefined strike, a NaN the
            // dip-and-strike codec cannot write - so the points sit on the curved Mars sphere
            GeoJsonRework.Tests.init ()
            let refSys = Draw.refSystemMars
            // points on the Mars reference sphere, a few metres apart
            let at (x : float) (y : float) = V3d(3396190.0, x, y)
            let drawn =
                Draw.drawFull refSys Geometry.Line true [ at 0.0 0.0; at 3.0 4.0 ]
                |> fun m -> [ at 1.0 1.0; at 2.0 1.0; at 2.0 2.0 ]
                            |> List.fold (fun m p -> Draw.click refSys p m) (Draw.run refSys m (DrawingAction.SetGeometry Geometry.Polyline))
                |> fun m -> Draw.run refSys m DrawingAction.Finish

            Expect.equal (Draw.annotations drawn |> List.length) 2 "two annotations drawn"

            let path = tempFile ".pro3d.ann"
            try
                Expect.isOk (AnnotationFiles.save path drawn) "save succeeds"
                match AnnotationFiles.tryLoad path with
                | Ok loaded ->
                    let restored = AnnotationFiles.applyTo DrawingModel.initialdrawing loaded
                    Expect.equal (pointsOf restored) (pointsOf drawn) "same keys and points"
                | Result.Error e -> failtestf "load failed: %s" e
            finally
                if File.Exists path then File.Delete path
        }

        test "a malformed file is an Error, not an exception" {
            let path = tempFile ".pro3d.ann"
            try
                File.WriteAllText(path, "{ this is not json")
                Expect.isError (AnnotationFiles.tryLoad path) "malformed"
            finally
                if File.Exists path then File.Delete path
        }

        test "a missing file is an Error" {
            Expect.isError (AnnotationFiles.tryLoad (tempFile ".pro3d.ann")) "missing"
        }
    ]

let surfacePicking =
    testList "SurfacePicking" [

        test "sky rays cast down from above the point (#628)" {
            let p = V3d(1.0, 2.0, 3.0)
            let ray = SurfacePicking.skyRay Planet.None p
            Expect.equal ray.Ray.Origin (p + V3d.OOI * 100.0) "origin 100 m above, flat frame up = +Z"
            Expect.equal ray.Ray.Direction (-V3d.OOI) "straight down"
        }

        test "Mars sky rays start 1000 km up" {
            GeoJsonRework.Tests.init ()
            let p = V3d(3396190.0, 0.0, 0.0)
            let ray = SurfacePicking.skyRay Planet.Mars p
            Expect.floatClose Accuracy.medium (Vec.distance ray.Ray.Origin p) 1000000.0 "distance"
            Expect.isGreaterThan (Vec.dot ray.Ray.Direction (-p.Normalized)) 0.99 "towards the planet"
        }

        test "viewpoint projection casts from the camera through the point" {
            let cam = V3d(0.0, 0.0, 10.0)
            let p   = V3d(3.0, 4.0, 0.0)
            match SurfacePicking.projectionRay Projection.Viewpoint (fun _ -> Planet.None) Guid.Empty cam p with
            | Some ray ->
                Expect.equal ray.Ray.Origin cam "from the camera"
                Expect.floatClose Accuracy.high (Vec.dot ray.Ray.Direction (p - cam).Normalized) 1.0 "through p"
            | None -> failtest "viewpoint has a ray"
        }

        test "sky projection uses the surface's own body" {
            let p = V3d(1.0, 2.0, 3.0)
            match SurfacePicking.projectionRay Projection.Sky (fun _ -> Planet.None) Guid.Empty V3d.Zero p with
            | Some ray -> Expect.equal ray (SurfacePicking.skyRay Planet.None p) "sky ray of planetOf"
            | None -> failtest "sky has a ray"
        }

        test "linear projection has no re-projection ray" {
            Expect.isNone
                (SurfacePicking.projectionRay Projection.Linear (fun _ -> Planet.None) Guid.Empty V3d.Zero V3d.III)
                "no ray"
        }

        test "an empty surface model has nothing to hit" {
            let hit =
                SurfacePicking.pickSurface
                    (SurfacePicking.KdTreeCache()) SurfaceModel.initial Draw.refSystemFlat SurfacePicking.Observation.none
                    SurfacePicking.onlyActive Projection.Viewpoint (fun _ -> Planet.None) V3d.Zero (FastRay3d(V3d.Zero, V3d.OOI))
            Expect.isNone hit "no surfaces"
        }

        // the real MSL OPC: SurfaceLoading builds the scene graph + KdTrees, SurfacePicking hits it
        match Render.skipReason () with
        | Some reason -> test "picks the imported MSL OPC (skipped)" { skiptest reason }
        | None ->
            test "picks the imported MSL OPC" {
                let runtime, signature = Render.context.Value |> Option.get
                let refSys = Draw.refSystemFlat
                let surfaces =
                    SurfaceModel.initial
                    |> SurfaceLoading.importSurfaces runtime signature None
                            (SurfaceLoading.discoverOpcSurfaces 100.0 [ Render.opcSurfaceDir ])

                let sg =
                    match surfaces.sgSurfaces |> HashMap.toList with
                    | [ (_, sg) ] -> sg
                    | other -> failtestf "expected one scene-graph surface, got %d" (List.length other)

                // the viewer's initial camera: from the bounding box corner at its centre
                let bb = sg.globalBB
                let ray = FastRay3d(bb.Max, (bb.Center - bb.Max).Normalized)
                let cache = SurfacePicking.KdTreeCache()

                match SurfacePicking.pickSurface cache surfaces refSys SurfacePicking.Observation.none
                        SurfacePicking.onlyActive Projection.Viewpoint (fun _ -> Planet.None) bb.Max ray with
                | Some pick ->
                    Expect.isTrue (bb.EnlargedBy(1e-3).Contains pick.hit) "hit inside the surface's box"
                    Expect.isFalse (HashMap.isEmpty cache.Trees) "the KdTree cache grew"
                    // re-projecting the hit along the same viewpoint ray finds it again
                    match pick.hitF pick.hit with
                    | Some again -> Expect.isLessThan (Vec.distance again pick.hit) 1e-3 "re-projection is stable"
                    | None -> failtest "re-projection missed"
                | None -> failtest "the ray through the box centre should hit the OPC"
            }
    ]

/// A fresh viewer model without a GL runtime - matchPickingInteraction never renders.
let private viewerModel () =
    let cts     = new System.Threading.CancellationTokenSource()
    let mailbox = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)
    let m =
        Viewer.initial mailbox StartupArgs.initArgs "" 1 Render.dataDir ViewerLenses._animator "tests"
    { m with scene = { m.scene with referenceSystem = Draw.refSystemFlat } }

let private pick (interaction : Interactions) (p : V3d) (surf : Surface) (m : Model) =
    ViewerApp.matchPickingInteraction Draw.bc p None Draw.identityHit surf { m with interaction = interaction }

/// Characterisation of the Viewer's click -> interaction routing, written against the code
/// before it was lifted into PRo3D.Composition.PickRouting: the lift must not change any of it.
let pickRouting =
    testList "pick routing (viewer)" [

        test "DrawAnnotation adds the point and stashes the resulting drawing" {
            let m = { viewerModel () with drawing = Draw.startTool Draw.refSystemFlat Geometry.Point }
            let p = V3d(1.0, 2.0, 3.0)
            let m' = pick Interactions.DrawAnnotation p (makeSurface "s") m
            let a = Draw.theAnnotation "point" m'.drawing
            Expect.equal (a.points |> IndexList.toList) [ p ] "the picked point"
            Expect.equal a.surfaceName "s" "surface name recorded"
            match m'.past with
            // `stash` runs after the update: the root `past` holds the post-pick drawing
            | Some past -> Expect.isTrue (obj.ReferenceEquals(past, m'.drawing)) "the drawing is stashed"
            | None -> failtest "nothing stashed for undo"
        }

        test "DrawAnnotation on a mesh switches the projection to linear" {
            let m = { viewerModel () with drawing = Draw.startTool Draw.refSystemFlat Geometry.Point }
            let mesh = { makeSurface "mesh" with surfaceType = SurfaceType.Mesh }
            let m' = pick Interactions.DrawAnnotation V3d.III mesh m
            Expect.equal m'.drawing.projection Projection.Linear "mesh -> linear"
        }

        test "EditAnnotation without a grabbed vertex changes nothing" {
            let m = viewerModel ()
            let m' = pick Interactions.EditAnnotation V3d.III (makeSurface "s") m
            Expect.isTrue (obj.ReferenceEquals(m'.drawing, m.drawing)) "drawing untouched"
        }

        test "PickSurface selects the hit surface" {
            let surf = makeSurface "s"
            let m' = pick Interactions.PickSurface V3d.III surf (viewerModel ())
            Expect.equal m'.scene.surfacesModel.surfaces.singleSelectLeaf (Some surf.guid) "selected"
        }

        test "PickExploreCenter sets the orbit centre and switches to ArcBall" {
            let p = V3d(5.0, 6.0, 7.0)
            let m' = pick Interactions.PickExploreCenter p (makeSurface "s") (viewerModel ())
            Expect.equal m'.navigation.exploreCenter p "explore centre"
            Expect.equal m'.navigation.navigationMode NavigationMode.ArcBall "ArcBall"
        }

        test "PlaceCoordinateSystem moves the reference system to the pick" {
            let p = V3d(5.0, 6.0, 7.0)
            let m' = pick Interactions.PlaceCoordinateSystem p (makeSurface "s") (viewerModel ())
            Expect.equal m'.scene.referenceSystem.origin p "origin"
        }

        test "an interaction without a pick action leaves the model alone" {
            let m = viewerModel ()
            let m' = pick Interactions.PickAnnotation V3d.III (makeSurface "s") m
            Expect.isTrue (obj.ReferenceEquals(m'.drawing, m.drawing)) "drawing untouched"
            Expect.isTrue (obj.ReferenceEquals(m'.scene, m.scene)) "scene untouched"
        }

        test "in the instrument view, standard-only picks do nothing" {
            let m = { viewerModel () with viewerMode = ViewerMode.Instrument }
            let m' = pick Interactions.PickSurface V3d.III (makeSurface "s") m
            Expect.equal m'.scene.surfacesModel.surfaces.singleSelectLeaf None "no selection"
        }
    ]

let liteScene =
    let coreOf (s : Scene) : SceneCore =
        {
            cameraView      = s.cameraView
            navigationMode  = s.navigationMode
            exploreCenter   = s.exploreCenter
            surfaceModel    = s.surfacesModel
            config          = s.config
            referenceSystem = s.referenceSystem
        }

    let parseScene (doc : Chiron.Json) : Scene =
        doc |> Chiron.Formatting.Json.format |> Chiron.Parsing.Json.parse |> Chiron.Mapping.Json.deserialize

    let fullSceneJson (s : Scene) : Chiron.Json = Chiron.Mapping.Json.serialize s

    let coreKeys = set [ "cameraView"; "navigationMode"; "exploreCenter"; "surfaceModel"; "config"; "referenceSystem"; "scenePath" ]

    testList "LiteScene" [

        test "a scene created from scratch opens in the full viewer" {
            let initial = (viewerModel ()).scene
            let core =
                { coreOf initial with
                    exploreCenter = V3d(1.0, 2.0, 3.0)
                    cameraView    = CameraView.lookAt (V3d(10.0, 0.0, 5.0)) V3d.Zero V3d.OOI }
            let doc = LiteScene.toJson None (Some "x.pro3d") core
            let scene = parseScene doc
            Expect.equal scene.version 3 "current version"
            Expect.equal scene.exploreCenter core.exploreCenter "explore centre"
            Expect.equal scene.cameraView.Location core.cameraView.Location "camera"
            Expect.equal scene.referenceSystem.planet core.referenceSystem.planet "planet"
            Expect.equal (scene.bookmarks.flat |> HashMap.count) 0 "bookmarks default"
        }

        test "opening and saving a full scene keeps every other key" {
            let full = fullSceneJson (viewerModel ()).scene
            match LiteScene.ofJson (Chiron.Formatting.Json.format full) with
            | Ok (core, extras) ->
                let moved = { core with exploreCenter = V3d(7.0, 8.0, 9.0) }
                let saved = LiteScene.toJson (Some extras) (Some "x.pro3d") moved
                match full, saved with
                | Chiron.Json.Object before, Chiron.Json.Object after ->
                    Expect.equal (after |> Map.toList |> List.map fst |> set) (before |> Map.toList |> List.map fst |> set) "same keys"
                    for KeyValue(k, v) in before do
                        if not (coreKeys.Contains k) then
                            Expect.equal (Map.tryFind k after) (Some v) (sprintf "key %s untouched" k)
                    let scene = parseScene saved
                    Expect.equal scene.exploreCenter moved.exploreCenter "the edit is saved"
                | _ -> failtest "scenes are JSON objects"
            | Result.Error e -> failtestf "could not read a full scene: %s" e
        }

        test "a file that is not a scene is an Error" {
            Expect.isError (LiteScene.ofJson "[1,2,3]") "array"
            Expect.isError (LiteScene.ofJson "{ \"version\": 3 }") "no core keys"
            Expect.isError (LiteScene.ofJson "{ broken") "malformed"
        }
    ]

let tests () =
    testList "composition" [
        annotationFiles
        surfacePicking
        pickRouting
        liteScene
    ]
