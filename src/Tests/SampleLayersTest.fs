module SampleLayersTest

open System
open System.IO

open Expecto

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Geometry
open Aardvark.PixImage.LibTiff

open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Tool
open PRo3D.Tool.SampleLayersVerb

/// `pro3d-tool sample-layers`: the pieces that need no SPICE kernels. The camera in the
/// shape-model case is made up rather than read from a sidecar -- what is under test is
/// that visibility and pixel placement agree with an independent ray cast, not the pointing.
module private Fixtures =

    let testData =
        [
            Environment.GetEnvironmentVariable "PRO3D_TESTDATA"
            Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "PRo3D.Resources.TestData")
        ]
        |> List.tryFind (fun p -> not (String.IsNullOrWhiteSpace p) && Directory.Exists p)
        |> Option.map Path.GetFullPath

    /// Dimorphos with kd-trees, per-vertex normals and a Slope layer.
    let dimorphosOpc =
        testData
        |> Option.map (fun root -> Path.Combine(root, "HERA", "Dimorphos_opc", "Dimorphos_DRACO1_DRACO2_Earth", "Dimorphos"))
        |> Option.filter Directory.Exists

    let scratch () =
        let d = Path.Combine(Path.GetTempPath(), "pro3d-sample-layers-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory d |> ignore
        d

    let writeJson (path : string) (content : string) = File.WriteAllText(path, content)

let private bandTests =
    testList "bands" [

        test "the nearest pixel of a coordinate: integers are pixel centres" {
            let size = V2i(4, 3)
            Expect.equal (nearestPixel size (V2d(0.0, 0.0))) (Some (V2i(0, 0))) "centre of the first pixel"
            Expect.equal (nearestPixel size (V2d(-0.49, 0.49))) (Some (V2i(0, 0))) "still inside the first pixel"
            Expect.equal (nearestPixel size (V2d(0.51, 0.0))) (Some (V2i(1, 0))) "past the half: the next pixel"
            Expect.equal (nearestPixel size (V2d(3.49, 2.49))) (Some (V2i(3, 2))) "inside the last pixel"
            Expect.equal (nearestPixel size (V2d(3.5, 0.0))) None "beyond the right edge"
            Expect.equal (nearestPixel size (V2d(0.0, -0.51))) None "above the top edge"
        }

        test "a stacked TIFF is read back plane by plane, with its wavelengths" {
            let dir = Fixtures.scratch ()
            let path = Path.Combine(dir, "HSH_TEST_Stacked.tif")
            let w, h = 5, 3
            // plane k, pixel i holds 100k + i: any swap of planes or axes shows as a wrong value
            let planes = Array.init 4 (fun k -> Array.init (w * h) (fun i -> float32 (100 * k + i)))
            match Float32Writer.writeBands path w h planes with
            | Result.Error e -> failtest e
            | Ok () -> ()
            Fixtures.writeJson (path + ".json") """{ "wavelengths": [ 661, 670, 688, 702 ] }"""

            match readBandFile path (Some "Stacked") None with
            | Result.Error e -> failtest e
            | Ok read ->
                Expect.equal (read |> List.map (fun p -> p.column)) [ "Stacked_0"; "Stacked_1"; "Stacked_2"; "Stacked_3" ] "one column per plane"
                Expect.equal (read |> List.map (fun p -> p.wavelength)) [ Some 661.0; Some 670.0; Some 688.0; Some 702.0 ] "wavelengths from the .tif.json"
                for p in read do
                    Expect.equal p.size (V2i(w, h)) "size"
                    Expect.equal p.values planes.[p.plane] (sprintf "plane %d round trips" p.plane)
        }

        test "an 8-bit frame is read as raw DN, row 0 at the top" {
            let dir = Fixtures.scratch ()
            let path = Path.Combine(dir, "AFC1_TEST.png")
            let img = PixImage<byte>(Col.Format.Gray, V2i(3, 2))
            let mutable m = img.GetChannel Col.Channel.Gray
            for y in 0 .. 1 do
                for x in 0 .. 2 do m.[x, y] <- byte (10 * y + x)
            img.Save path
            match readBandFile path None None with
            | Result.Error e -> failtest e
            | Ok [ p ] ->
                Expect.equal p.column "AFC1_TEST" "the file stem names an unlabelled band"
                Expect.equal p.values [| 0.0f; 1.0f; 2.0f; 10.0f; 11.0f; 12.0f |] "row-major, not normalised"
            | Ok ps -> failtestf "expected one plane, got %d" ps.Length
        }

        test "observations are found by their sidecars, in every layout" {
            let dir = Fixtures.scratch ()
            // ASPECT-like: mbi_bands with labels and wavelengths
            Fixtures.writeJson (Path.Combine(dir, "ASP.mbi.json"))
                """{ "mbi_bands": [ { "file_path": "ASP_Vis_0.tif", "label": "Vis_0", "wavelength": 675.0 },
                                    { "file_path": "ASP_Vis_1.tif", "label": "Vis_1", "wavelength": 690.0 } ] }"""
            // COP-like: no usable file_path, the image is named like the sidecar
            Fixtures.writeJson (Path.Combine(dir, "COP.mbi.json")) """{ "bands": [ { "file_path": "" } ] }"""
            File.WriteAllBytes(Path.Combine(dir, "COP.png"), [||])
            // a sidecar with nothing behind it is skipped, not fatal
            Fixtures.writeJson (Path.Combine(dir, "ORPHAN.mbi.json")) """{ "bands": [] }"""

            let found = discoverObservations dir |> List.map (fun o -> observationStem o.sidecar, o.bands)
            match found with
            | [ ("ASP", asp); ("COP", cop) ] ->
                Expect.equal (asp |> List.map (fun (p, l, w) -> Path.GetFileName p, l, w))
                    [ "ASP_Vis_0.tif", Some "Vis_0", Some 675.0; "ASP_Vis_1.tif", Some "Vis_1", Some 690.0 ] "declared bands, in order"
                Expect.equal (cop |> List.map (fun (p, _, _) -> Path.GetFileName p)) [ "COP.png" ] "naming-convention fallback"
            | other -> failtestf "unexpected observations %A" (other |> List.map fst)
        }
    ]

let private surfaceTests =
    testList "against the shape model" [

        test "every vertex a camera sees is the first surface along its own pixel's ray" {
            match Fixtures.dimorphosOpc with
            | None -> skiptest "no Dimorphos OPC test data (set PRO3D_TESTDATA)"
            | Some opc ->

            HeadlessPicking.init ()
            let hierarchies = HeadlessPicking.loadHierarchies opc
            let kdTreeMap = HeadlessPicking.loadKdTreeMap hierarchies
            let vertices = collectVertices hierarchies [ "Slope" ]
            let occluders = loadOccluders kdTreeMap

            let n = vertices.positions.Length
            Expect.isGreaterThan n 100000 "the finest level is loaded"
            let keys =
                vertices.positions
                |> Array.map (fun p -> int64 (Math.Round(p.X * 1000.0)), int64 (Math.Round(p.Y * 1000.0)), int64 (Math.Round(p.Z * 1000.0)))
            Expect.equal (Array.distinct keys).Length n "no two ids share a position (the pole row and the seam are merged)"
            Expect.isTrue (vertices.normals |> Array.forall (fun v -> v = V3d.Zero || abs (v.Length - 1.0) < 1e-6)) "normals are unit or absent"

            match vertices.attributes with
            | [ slope ] ->
                Expect.equal slope.name "Slope" "the requested layer"
                Expect.equal slope.values.Length n "one value per vertex"
            | other -> failtestf "expected the Slope layer, got %A" (other |> List.map (fun a -> a.name))

            // a made-up camera 2 km out on +X, looking at the centre
            let eye = V3d(2000.0, 300.0, 200.0)
            let view = CameraView.lookAt eye V3d.Zero V3d.OOI |> CameraView.viewTrafo
            let proj = Frustum.perspective 8.0 10.0 4000.0 1.0 |> Frustum.projTrafo
            let cam : ProjectorCamera =
                { view = view; proj = proj; full = view * proj; distance = eye.Length; near = 10.0; far = 4000.0 }
            let size = V2i(512, 512)
            let sun = V3d(1.0, 1.0, 0.0).Normalized

            let samples, funnel = sampleVisibility vertices occluders cam size sun 0.05
            let seen = samples |> Array.choose id
            Expect.equal seen.Length funnel.seen "the funnel counts what is returned"
            Expect.isTrue (funnel.seen <= funnel.facing && funnel.facing <= funnel.inFrame) "each stage narrows"
            Expect.isGreaterThan funnel.seen (n / 4) "roughly the facing hemisphere is seen"
            Expect.isLessThan funnel.seen funnel.facing "on a rugged body, occlusion removes some facing vertices"

            // The far side is never seen. From 2 km the limb of a ~85 m body lies R^2/d ~ 4 m in
            // front of the centre plane; an irregular limb can reach a little behind it, 20 m
            // behind cannot.
            for i in 0 .. n - 1 do
                if samples.[i].IsSome then
                    let p = vertices.positions.[i]
                    Expect.isGreaterThan (Vec.dot p eye.Normalized) -20.0 "a seen vertex is not on the far side"

            // Independent check: along the ray through the reported pixel, nothing lies in
            // front of the vertex. The ray passes exactly through a vertex, i.e. along the
            // edges of its triangles, and can slip through between them -- its first hit is then
            // terrain BEHIND the vertex, which does not contradict visibility; a hit in front
            // would. A strided subset keeps it quick.
            let mutable cache = FSharp.Data.Adaptive.HashMap.empty
            let mutable checkedCount = 0
            let mutable atVertex = 0
            for i in 0 .. 997 .. n - 1 do
                match samples.[i] with
                | Some s ->
                    let ray = InstrumentObservation.pixelRay cam size InstrumentObservation.PixelConvention.Image s.pixel
                    let hit, c = HeadlessPicking.intersectAll kdTreeMap cache (FastRay3d ray)
                    cache <- c
                    let vertexRange = Vec.length (vertices.positions.[i] - eye)
                    match hit with
                    | None -> ()
                    | Some h ->
                        let hitRange = Vec.length (h.position - eye)
                        Expect.isGreaterThan hitRange (vertexRange - 0.25)
                            (sprintf "vertex %d: surface %.3f m in front of it along its pixel's ray" i (vertexRange - hitRange))
                        if abs (hitRange - vertexRange) < 0.05 then atVertex <- atVertex + 1
                    checkedCount <- checkedCount + 1
                    Expect.isTrue (Double.IsFinite s.incidence && Double.IsFinite s.emission) "angles from the vertex normal"
                    Expect.isLessThan s.emission (Math.PI / 2.0) "a seen vertex has emission below 90 deg"
                | None -> ()
            // slipping through must stay the exception, or the check above proves nothing
            Expect.isGreaterThan (float atVertex) (0.9 * float checkedCount) "most pixel rays stop at their vertex"
            Expect.isGreaterThan checkedCount 100 "enough vertices checked"
        }
    ]

let tests () =
    testList "sample-layers" [ bandTests; surfaceTests ]
