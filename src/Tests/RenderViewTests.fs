/// pro3d-tool render-view (#878): job files, cameras, and Dimorphos rendered from HERA's
/// AFC-1 against simulate-image's frame of the same observation (silhouette, centroid,
/// lit region), plus the sun override, OPC transformations and the fixed LoD. Job and
/// reference are fixtures (src/Tests/data/render-view, see its README): no SPICE needed.
/// The rendering tests need a GL context and PRO3D_TEST_DATA; they skip otherwise.
module PRo3D.Tests.RenderViewTests

open System
open System.IO

open Expecto

open Aardvark.Base
open Aardvark.Rendering

open PRo3D
open PRo3D.SceneRender

open PRo3D.Tests

let private fixtureDir = Path.Combine(__SOURCE_DIRECTORY__, "data", "render-view")
let private fixtureJob = Path.Combine(fixtureDir, "afc1_20270321T170000.json")
let private reference  = Path.Combine(fixtureDir, "simulate-image_afc1_20270321T170000.png")


/// relative OPC paths resolve against an existing directory: "." is one
let private parse (json : string) = ViewJob.parse (Path.GetTempPath()) json

/// a job file under a rooted path on every OS (C:\jobs is a relative path on Linux)
let private jobFile = Path.Combine(Path.GetTempPath(), "jobs", "job.json")

let private resolveText (json : string) =
    parse json |> Result.bind (ViewJob.resolve ViewJob.noOverrides jobFile)

let private camera : ViewCamera = { location = V3d(0.0, -10.0, 0.0); forward = V3d.OIO; up = V3d.OOI; fov = 30.0 }

// ---- image measures --------------------------------------------------------------------

let private mask (threshold : int) (image : PixImage<byte>) : bool[,] =
    let m = image.GetMatrix<C4b>()
    Array2D.init (int m.SY) (int m.SX) (fun y x ->
        let c = m.[int64 x, int64 y]
        int (max c.R (max c.G c.B)) >= threshold)

let private litMask (fraction : float) (image : PixImage<byte>) : bool[,] =
    let m = image.GetMatrix<C4b>()
    let mutable top = 0uy
    m.ForeachIndex(fun i -> let c = m.[i] in top <- max top (max c.R (max c.G c.B)))
    mask (max 1 (int (fraction * float top))) image

let private count (m : bool[,]) =
    let mutable n = 0
    m |> Array2D.iter (fun b -> if b then n <- n + 1)
    n

let private iou (a : bool[,]) (b : bool[,]) =
    let mutable i = 0
    let mutable u = 0
    for y in 0 .. Array2D.length1 a - 1 do
        for x in 0 .. Array2D.length2 a - 1 do
            if a.[y, x] && b.[y, x] then i <- i + 1
            if a.[y, x] || b.[y, x] then u <- u + 1
    if u = 0 then 1.0 else float i / float u

let private centroid (m : bool[,]) =
    let mutable sx, sy, n = 0.0, 0.0, 0
    m |> Array2D.iteri (fun y x b -> if b then (sx <- sx + float x; sy <- sy + float y; n <- n + 1))
    if n = 0 then V2d.NaN else V2d(sx / float n, sy / float n)

let private differs (a : PixImage<byte>) (b : PixImage<byte>) =
    let ma, mb = a.GetMatrix<C4b>(), b.GetMatrix<C4b>()
    let mutable changed = 0L
    ma.ForeachIndex(fun i ->
        let x, y = ma.[i], mb.[i]
        if max (abs (int x.R - int y.R)) (max (abs (int x.G - int y.G)) (abs (int x.B - int y.B))) > 16 then
            changed <- changed + 1L)
    float changed / float (ma.SX * ma.SY)

// ---- tests without a GPU -----------------------------------------------------------------

let private jobTests =
    testList "job file and camera" [

        test "Frustum.perspective takes the horizontal field of view" {
            let f = ViewCamera.frustum { camera with fov = 90.0 } (V2i(200, 100)) (1.0, 10.0)
            Expect.floatClose Accuracy.high f.right 1.0 "tan 45 deg across the width"
            Expect.floatClose Accuracy.high f.top 0.5 "the height follows the aspect"
        }

        test "simulate-image's AFC frusta have the instrument kernel's 5.5 deg field (#801)" {
            // hera_afc_v06.ti: FOV_REF_ANGLE = FOV_CROSS_ANGLE = 2.75 deg half-angles
            let frusta = PRo3D.SPICE.InstrumentProjection.instruments 1.0 10.0
            for name in [ "HERA_AFC-1"; "HERA_AFC-2" ] do
                match Map.tryFind name frusta with
                | None -> failtestf "no frustum for %s" name
                | Some f ->
                    let tan = Math.Tan(Conversion.RadiansFromDegrees 2.75)
                    Expect.floatClose Accuracy.high f.right tan (sprintf "%s: 2.75 deg to the right edge" name)
                    Expect.floatClose Accuracy.high f.top tan (sprintf "%s: 2.75 deg to the top edge" name)
        }

        test "the camera's up is made orthogonal to forward, and odd cameras are refused" {
            match ViewCamera.toCameraView { camera with up = V3d(0.0, 0.1, 1.0) } with
            | Result.Error e -> failtest e
            | Ok (view, turned) ->
                Expect.floatClose Accuracy.medium (Vec.dot view.Up V3d.OIO) 0.0 "up is orthogonal to forward"
                Expect.floatClose Accuracy.medium turned (Conversion.DegreesFromRadians (atan 0.1)) "the turn is reported"
                Expect.isLessThan (Vec.distance view.Right V3d.IOO) 1e-9 "right = forward x up"
            let error c = match ViewCamera.toCameraView c with Ok _ -> failtestf "accepted %A" c | Result.Error e -> e
            Expect.stringContains (error { camera with up = V3d.OIO }) "parallel" "up along forward"
            Expect.stringContains (error { camera with forward = V3d.Zero }) "forward is zero" "no forward"
            Expect.stringContains (error { camera with fov = 180.0 }) "fov" "a flat fov"
        }

        test "near and far enclose the scene" {
            let bounds = Box3d(V3d(-100.0), V3d(100.0))
            let near, far = ViewCamera.nearFar (V3d(0.0, -9000.0, 0.0)) bounds
            Expect.isLessThan near (9000.0 - 174.0) "near in front of the scene"
            Expect.isGreaterThan far (9000.0 + 174.0) "far behind it"
            Expect.isGreaterThan near 1.0 "and not wasted on centimetres"
        }

        test "the sun is a direction from the scene's centre" {
            let bounds = Box3d(V3d(-1.0), V3d(3.0))
            Expect.equal (SunInput.direction bounds { position = V3d(1.0, 1.0, 11.0); shadows = true }) (Some V3d.OOI) "towards the position"
            Expect.equal (SunInput.direction bounds { position = V3d(1.0); shadows = true }) None "a sun at the centre"
        }

        test "the fixture job parses: views, sun, the OPC relative to the job" {
            match ViewJob.load fixtureJob with
            | Result.Error e -> failtest e
            | Ok (f, _) ->
                Expect.equal f.opcs (Some [ { path = Path.Combine(fixtureDir, "Dimorphos"); trafo = None; textured = false } ]) "relative path, untextured"
                Expect.isSome f.coordinatesAreIn "the frame label is kept"
                Expect.equal (f.width, f.height) (Some 1020, Some 1020) "AFC's size"
                match f.views with
                | Some [ v ] ->
                    Expect.equal v.name "afc1_20270321T170000" "the view"
                    Expect.floatClose Accuracy.medium v.fov 5.5 "the IK's field of view"
                    Expect.isTrue (v.sun |> Option.exists (fun s -> s.shadows)) "sun with shadows"
                | vs -> failtestf "expected one view, got %A" vs
        }

        test "trafos are row-major placements; singular ones are refused" {
            let job =
                """{ "opcs": [ { "path": ".", "trafo": [1,0,0,10, 0,1,0,20, 0,0,1,30, 0,0,0,1] } ],
                     "views": [ { "name": "v", "location": [0,0,10], "forward": [0,0,-1], "up": [0,1,0], "fov": 30 } ] }"""
            match parse job with
            | Result.Error e -> failtest e
            | Ok f ->
                match f.opcs with
                | Some [ o ] ->
                    Expect.equal (o.trafo |> Option.map (fun t -> t.Forward.TransformPos V3d.Zero)) (Some (V3d(10.0, 20.0, 30.0))) "the last column translates"
                    Expect.isTrue o.textured "textured by default"
                | other -> failtestf "expected one OPC, got %A" other
            let singular = job.Replace("[1,0,0,10, 0,1,0,20, 0,0,1,30, 0,0,0,1]", "[1,0,0,0, 0,0,0,0, 0,0,1,0, 0,0,0,1]")
            match parse singular with
            | Ok _ -> failtest "a singular trafo is not a placement"
            | Result.Error e -> Expect.stringContains e "not invertible" "says why"
        }

        test "bad jobs fail with the field named, before rendering" {
            let view = """{ "name": "v", "location": [0,0,10], "forward": [0,0,-1], "up": [0,1,0], "fov": 30 }"""
            let job (views : string) = sprintf """{ "opcs": [ { "path": "." } ], "views": [ %s ] }""" views
            let error json = match resolveText json with Ok _ -> failtestf "accepted %s" json | Result.Error e -> e
            Expect.stringContains (error (job (view.Replace("\"fov\": 30", "\"fov\": 30, \"focal\": 1")))) "'focal'" "an unknown key"
            Expect.stringContains (error (job (view.Replace("[0,1,0]", "[0,0,1]")))) "parallel" "up along forward"
            Expect.stringContains (error (job (view.Replace("[0,0,10]", "[0,0]")))) "'location' must be an array of 3 numbers" "a short vector"
            Expect.stringContains (error (job (view.Replace(", \"fov\": 30", "")))) "'fov' is required" "no fov"
            Expect.stringContains (error (job (view + ", " + view))) "two views are named 'v'" "duplicate names"
            Expect.stringContains (error (job (view.Replace("\"v\"", "\"a/b\"")))) "cannot be a file name" "a path as name"
            Expect.stringContains (error (job (view.Replace("}", ", \"sun\": { \"shadows\": true } }")))) "'position' is required" "a sun without position"
            Expect.stringContains (error """{ "views": [] }""") "no OPCs" "no OPCs"
            Expect.stringContains (error """{ "opcs": [ { "path": "no/such/opc" } ], "views": [ { "name": "v", "location": [0,0,10], "forward": [0,0,-1], "up": [0,1,0], "fov": 30 } ] }""") "no OPC directory" "a missing OPC"
        }

        test "the command line overrides the top level; a view's own values win; --views selects" {
            let json =
                """{ "opcs": [ { "path": "." } ], "width": 640, "height": 480, "lodLevel": 1,
                     "views": [
                       { "name": "a1", "location": [0,0,10], "forward": [0,0,-1], "up": [0,1,0], "fov": 30, "width": 100,
                         "sun": { "position": [0,0,1e9] } },
                       { "name": "b1", "location": [0,0,10], "forward": [0,0,-1], "up": [0,1,0], "fov": 30 } ] }"""
            let o = { ViewJob.noOverrides with height = Some 200; lodLevel = Some 3; noSun = true }
            match parse json |> Result.bind (ViewJob.resolve o jobFile) with
            | Result.Error e -> failtest e
            | Ok job ->
                Expect.equal job.out (Path.Combine(Path.GetTempPath(), "jobs", "job-renders")) "default output beside the job"
                Expect.equal (job.views |> List.map (fun v -> v.name, v.settings.size, v.settings.lodLevel, v.sun.IsSome))
                             [ "a1", V2i(100, 200), Some 3, false; "b1", V2i(640, 200), Some 3, false ]
                             "width from the view, height and LoD from the command line, no sun"
            match parse json |> Result.bind (ViewJob.resolve { ViewJob.noOverrides with views = Some [ "b*" ] } jobFile) with
            | Ok job -> Expect.equal (job.views |> List.map (fun v -> v.name)) [ "b1" ] "a wildcard selects views"
            | Result.Error e -> failtest e
            match parse json |> Result.bind (ViewJob.resolve { ViewJob.noOverrides with views = Some [ "c" ] } jobFile) with
            | Ok _ -> failtest "a selection that matches nothing must fail"
            | Result.Error e -> Expect.stringContains e "'c'" "names the pattern"
        }
    ]

// ---- rendering ---------------------------------------------------------------------------

/// The fixture job with the Dimorphos OPC of the test data, and a renderer on it.
let private withDimorphos (run : ViewSpec -> OpcInput -> IRuntime * IFramebufferSignature -> unit) =
    match TestUtils.Roots.dimorphosOpc None, Render.context.Value with
    | None, _ -> skiptest "no Dimorphos OPC: set PRO3D_TEST_DATA"
    | _, None -> skiptest "no OpenGL runtime in this environment"
    | Some opc, Some context ->
        let text = File.ReadAllText fixtureJob
        match ViewJob.parse (Path.GetDirectoryName opc) text
              |> Result.bind (ViewJob.resolve ViewJob.noOverrides fixtureJob) with
        | Result.Error e -> failtest e
        | Ok job ->
            match job.views, job.opcs with
            | [ view ], [ o ] -> run view o context
            | _ -> failtest "the fixture has one view and one OPC"

let private renderTests =
    testList "rendering Dimorphos from HERA's AFC-1" [

        test "render-view matches simulate-image's frame: silhouette, centroid, lit region, FOV" {
            withDimorphos (fun view opc (runtime, signature) ->
            Startup.init ()
            let ref = PixImage.Load(reference).ToPixImage<byte>(Col.Format.RGBA)
            let refSilhouette = mask 1 ref

            use renderer = new OpcRenderer(runtime, signature, [ opc ])
            // the job's field of view is the IK's 5.5 deg, and so is simulate-image's
            Expect.floatClose Accuracy.high view.camera.fov 5.5 "the job carries the IK's AFC-1 field of view"

            // geometry: unlit (the untextured headlight shades the whole disk)
            let unlit = renderer.Render(view.camera, None, view.settings)
            Expect.equal unlit.Size (V2i(1020, 1020)) "AFC's size"
            let silhouette = mask 1 unlit
            Expect.isGreaterThan (iou silhouette refSilhouette) 0.98 "the same silhouette as simulate-image"
            Expect.isLessThan (Vec.distance (centroid silhouette) (centroid refSilhouette)) 1.0 "in the same place, to a pixel"
            // the same size, to a pixel on the radius. Not tighter: render-view's 8x MSAA
            // counts partly covered edge pixels (about half a pixel all round, +1.2% area
            // here) and simulate-image does not, which is as large as a 0.56% field-of-view
            // error at this size. The field of view itself is pinned by the frusta test.
            let radius (n : int) = sqrt (float n / Math.PI)
            Expect.isLessThan (abs (radius (count silhouette) - radius (count refSilhouette))) 1.0
                "the same size as simulate-image's, to a pixel on the radius"

            // sun: the lit region where simulate-image has it (the shading models differ:
            // viewer Lommel-Seeliger vs simulate-image's LS + Lambert with ambient)
            let lit = renderer.Render(view.camera, view.sun, view.settings)
            Expect.isGreaterThan (iou (litMask 0.25 lit) (litMask 0.25 ref)) 0.9 "the lit region matches simulate-image's"
            // the untextured headlight and a low-phase sun look alike, but not the same
            Expect.isGreaterThan (differs unlit lit) 0.005 "sun lighting changes the image"
            )
        }

        test "the sun moves the terminator, shadows matter, and the override does not outlive the renderer" {
            withDimorphos (fun view opc (runtime, signature) ->
            Startup.init ()
            let sun = view.sun |> Option.defaultWith (fun () -> failtest "the fixture has a sun")
            using (new OpcRenderer(runtime, signature, [ opc ])) (fun renderer ->
                let render s = renderer.Render(view.camera, Some s, view.settings)
                let front = render sun
                // the sun mirrored through the body's centre: lights the other hemisphere
                let back = render { sun with position = renderer.Bounds.Center * 2.0 - sun.position }
                let lit image = count (litMask 0.25 image)
                Expect.isLessThan (iou (litMask 0.25 front) (litMask 0.25 back)) 0.2 "the other side lights up"
                Expect.isLessThan (lit back) (lit front) "seen from HERA, most of the lit side faces away"
                // no acne: at the fixture's low phase hardly anything casts a shadow, so the
                // shadow pass must leave the sunlit side alone (a 3 cm bias, under half a
                // shadow-map texel here, darkened 21 % of it by more than 10 %)
                let unshadowed = render { sun with shadows = false }
                Expect.isLessThan (differs front unshadowed) 0.0005 "no shadow acne on the sunlit side"
                // a sun from the image's right at 85 deg phase: Dimorphos' crater rims cast
                // shadows (measured 1.45e-4 of the frame, ~150 px; the body is ~20k px, and
                // it is smooth at this OPC's 1.96 m)
                let forward = Vec.normalize view.camera.forward
                let right = Vec.cross forward view.camera.up |> Vec.normalize
                let phase = Conversion.RadiansFromDegrees 85.0
                let towardsSun = -forward * cos phase + right * sin phase
                let side = { sun with position = renderer.Bounds.Center + towardsSun * Vec.length (sun.position - renderer.Bounds.Center) }
                Expect.isGreaterThan (differs (render side) (render { side with shadows = false })) 0.00005 "cast shadows change the image")
            Expect.equal PRo3D.Core.Gis.SunOverride.direction.Value None "disposing resets the sun override"
            )
        }

        test "OPC transformations place each OPC; LoD still fixable" {
            withDimorphos (fun view opc (runtime, signature) ->
            Startup.init ()
            let single =
                use r = new OpcRenderer(runtime, signature, [ opc ])
                mask 1 (r.Render(view.camera, None, view.settings))
            // a second Dimorphos, moved 300 m along the camera's left: about 300 m / (pixel
            // footprint at the body) to the left in the image
            let right = Vec.cross view.camera.forward view.camera.up |> Vec.normalize
            let shift = -300.0 * right
            let twin = { opc with trafo = Some (Trafo3d.Translation shift) }
            use r = new OpcRenderer(runtime, signature, [ opc; twin ])
            let pair = mask 1 (r.Render(view.camera, None, view.settings))
            Expect.floatClose { absolute = 0.03; relative = 0.0 } (float (count pair) / float (count single)) 2.0 "both bodies are drawn"
            let distance = Vec.distance view.camera.location r.Bounds.Center
            let pixelsPerMetre = 510.0 / (distance * tan (Conversion.RadiansFromDegrees (view.camera.fov / 2.0)))
            let leftOnly = Array2D.init 1020 1020 (fun y x -> pair.[y, x] && not single.[y, x])
            let measured = (centroid single).X - (centroid leftOnly).X
            Expect.floatClose { absolute = 15.0; relative = 0.0 } measured (300.0 * pixelsPerMetre) "the twin sits where its trafo puts it"

            // at 9 km the levels draw the same outline; the LoD itself is tested in #871's
            // close-up (SceneRenderTests) -- here only that it reaches these surfaces too
            let root = r.Render(view.camera, None, { view.settings with lodLevel = Some 0 })
            Expect.isGreaterThan (SurfaceEffectHarness.litFraction root) 0.01 "the root patches draw the bodies"
            )
        }
    ]

let tests () =
    testList "render view (#878)" [ jobTests; renderTests ]
