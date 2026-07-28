module TriangleSetTests

open System.IO
open Expecto
open Aardvark.Base
open Aardvark.Geometry
open Aardvark.Data.Opc
open PRo3DCompability
open PRo3D.Core.Surface

let private triangleCount (ts : TriangleSet) = ts.Position3dList.Count / 3

let private extractTriangles (ts : TriangleSet) =
    [| for i in 0 .. triangleCount ts - 1 do
        let pi = i * 3
        yield Triangle3d(ts.Position3dList.[pi], ts.Position3dList.[pi+1], ts.Position3dList.[pi+2]) |]

let tests () =
    Aardvark.Init()
    testList "TriangleSet" [

        test "getInvalidIndices3f matches legacy" {
            let positions = [|
                V3f(1.0f, 2.0f, 3.0f)
                V3f.NaN
                V3f(4.0f, 5.0f, 6.0f)
                V3f(System.Single.NaN, 0.0f, 0.0f)
                V3f(7.0f, 8.0f, 9.0f)
            |]

            let legacy = DebugKdTreesX.getInvalidIndices3f positions
            let optimized = TriangleSet.getInvalidIndices3f positions

            Expect.equal (Array.ofList legacy) optimized "should produce same invalid indices"
        }

        test "getInvalidIndices3f empty on clean data" {
            let positions = [| V3f(1.0f, 2.0f, 3.0f); V3f(4.0f, 5.0f, 6.0f) |]
            let result = TriangleSet.getInvalidIndices3f positions
            Expect.equal result [||] "no invalid indices expected"
        }

        test "getInvalidIndices3f all NaN" {
            let positions = [| V3f.NaN; V3f.NaN; V3f.NaN |]
            let legacy = DebugKdTreesX.getInvalidIndices3f positions
            let optimized = TriangleSet.getInvalidIndices3f positions
            Expect.equal (Array.ofList legacy) optimized "all should be invalid"
        }

        test "getTriangleSet matches legacy" {
            let vertices = [|
                V3d(0.0, 0.0, 0.0)
                V3d(1.0, 0.0, 0.0)
                V3d(0.0, 1.0, 0.0)
                V3d(2.0, 0.0, 0.0)
                V3d(3.0, 0.0, 0.0)
                V3d(2.0, 1.0, 0.0)
            |]
            let indices = [| 0; 1; 2; 3; 4; 5 |]

            let legacy = DebugKdTreesX.getTriangleSet indices vertices
            let optimized = TriangleSet.getTriangleSet indices vertices

            let legacyTris = extractTriangles legacy
            let optimizedTris = extractTriangles optimized
            Expect.equal optimizedTris.Length legacyTris.Length "same triangle count"
            for i in 0 .. legacyTris.Length - 1 do
                Expect.equal optimizedTris.[i] legacyTris.[i] (sprintf "triangle %d should match" i)
        }

        test "getTriangleSet filters NaN triangles" {
            let vertices = [|
                V3d(0.0, 0.0, 0.0); V3d(1.0, 0.0, 0.0); V3d(0.0, 1.0, 0.0) // valid
                V3d.NaN;             V3d(1.0, 0.0, 0.0); V3d(0.0, 1.0, 0.0) // invalid
                V3d(2.0, 0.0, 0.0); V3d(3.0, 0.0, 0.0); V3d(2.0, 1.0, 0.0) // valid
            |]
            let indices = [| 0; 1; 2; 3; 4; 5; 6; 7; 8 |]

            let legacy = DebugKdTreesX.getTriangleSet indices vertices
            let optimized = TriangleSet.getTriangleSet indices vertices

            let legacyTris = extractTriangles legacy
            let optimizedTris = extractTriangles optimized
            Expect.equal optimizedTris.Length 2 "should have 2 valid triangles"
            Expect.equal optimizedTris.Length legacyTris.Length "same count as legacy"
        }

        test "getTriangleSet3f matches legacy" {
            let vertices = [|
                V3f(0.0f, 0.0f, 0.0f); V3f(1.0f, 0.0f, 0.0f); V3f(0.0f, 1.0f, 0.0f)
                V3f.NaN;               V3f(1.0f, 0.0f, 0.0f); V3f(0.0f, 1.0f, 0.0f)
                V3f(2.0f, 0.0f, 0.0f); V3f(3.0f, 0.0f, 0.0f); V3f(2.0f, 1.0f, 0.0f)
            |]

            let legacy = DebugKdTreesX.getTriangleSet3f vertices
            let optimized = TriangleSet.getTriangleSet3f vertices

            let legacyTris = extractTriangles legacy
            let optimizedTris = extractTriangles optimized
            Expect.equal optimizedTris.Length legacyTris.Length "same triangle count"
            for i in 0 .. legacyTris.Length - 1 do
                Expect.equal optimizedTris.[i] legacyTris.[i] (sprintf "triangle %d should match" i)
        }

        // Real-world test: loads XYZ_Local.aara from a patch and runs both legacy
        // and optimized paths through the full pipeline:
        //   Aara.fromFile<V3f> → getInvalidIndices3f → ComputeIndexArray → map V3f→V3d → getTriangleSet
        // This is the hot path in DebugKdTreesX.loadTriangles' (Surface.fs:107-120).
        // Patch 0_0_1 has ~2M triangles — a meaningful size for correctness and perf comparison.
        test "real-world loadTriangles pipeline matches legacy" {
            let araPath =
                Path.Combine(
                    "C:\\pro3ddata\\testdata", "Dimorphos_DRACO1", "Dimorphos_DRACO1",
                    "g_01960mm_spc_dtm_dimo_0000n00000_v003_0_0", "Patches", "0_0_1", "XYZ_Local.aara")
            if not (File.Exists araPath) then
                skiptest "OPC test data not available"

            let positions = araPath |> Aara.fromFile<V3f>
            let size = positions.Size.XY.ToV2i()
            let affine = Trafo3d.Identity
            let sw = System.Diagnostics.Stopwatch()

            // legacy path
            sw.Restart()
            let legacyInvalids = DebugKdTreesX.getInvalidIndices3f positions.Data |> List.toArray
            let legacyInvalidsMs = sw.Elapsed.TotalMilliseconds

            let legacyIndices = PRo3DCSharp.ComputeIndexArray(size, legacyInvalids)
            let vertices = positions.Data |> Array.map (fun x -> x.ToV3d() |> affine.Forward.TransformPos)

            sw.Restart()
            let legacySet = DebugKdTreesX.getTriangleSet legacyIndices vertices
            let legacyTriSetMs = sw.Elapsed.TotalMilliseconds

            // optimized path
            sw.Restart()
            let optInvalids = TriangleSet.getInvalidIndices3f positions.Data
            let optInvalidsMs = sw.Elapsed.TotalMilliseconds

            let optIndices = PRo3DCSharp.ComputeIndexArray(size, optInvalids)

            sw.Restart()
            let optSet = TriangleSet.getTriangleSet optIndices vertices
            let optTriSetMs = sw.Elapsed.TotalMilliseconds

            Log.line "[Test] getInvalidIndices3f: legacy=%.2fms optimized=%.2fms (%.1fx)" legacyInvalidsMs optInvalidsMs (legacyInvalidsMs / optInvalidsMs)
            Log.line "[Test] getTriangleSet:      legacy=%.2fms optimized=%.2fms (%.1fx)" legacyTriSetMs optTriSetMs (legacyTriSetMs / optTriSetMs)

            Expect.equal optInvalids.Length legacyInvalids.Length "same number of invalid indices"
            Expect.equal optInvalids legacyInvalids "invalid indices should match"
            Expect.equal (triangleCount optSet) (triangleCount legacySet) "same triangle count"

            // spot check first, middle, last triangles
            let checkTriangle label i =
                let pi = i * 3
                let lt = Triangle3d(legacySet.Position3dList.[pi], legacySet.Position3dList.[pi+1], legacySet.Position3dList.[pi+2])
                let ot = Triangle3d(optSet.Position3dList.[pi], optSet.Position3dList.[pi+1], optSet.Position3dList.[pi+2])
                Expect.equal ot lt (sprintf "%s triangle %d should match" label i)

            checkTriangle "first" 0
            checkTriangle "middle" (triangleCount optSet / 2)
            checkTriangle "last" (triangleCount optSet - 1)

            Log.line "[Test] real-world: %d positions, %d invalids, %d triangles" positions.Data.Length optInvalids.Length (triangleCount optSet)
        }

        test "computeGridIndices skips quads with NaN vertices" {
            // 3x3 grid with one NaN vertex at position 4 (center)
            //  0--1--2
            //  |  |  |
            //  3--4--5    (4 = NaN)
            //  |  |  |
            //  6--7--8
            let positions = [|
                V3f(0.0f, 0.0f, 0.0f); V3f(1.0f, 0.0f, 0.0f); V3f(2.0f, 0.0f, 0.0f)
                V3f(0.0f, 1.0f, 0.0f); V3f.NaN;                V3f(2.0f, 1.0f, 0.0f)
                V3f(0.0f, 2.0f, 0.0f); V3f(1.0f, 2.0f, 0.0f); V3f(2.0f, 2.0f, 0.0f)
            |]
            let size = V2i(3, 3)

            let indices = TriangleSet.computeGridIndices size positions

            // all 4 quads touch center NaN vertex → 0 indices emitted
            Expect.equal indices.Length 0 "all quads touch NaN center, no indices"
        }

        test "computeGridIndices correct on partially invalid grid" {
            // 4x2 grid, vertex 1 is NaN
            //  0--1--2--3
            //  |  |  |  |
            //  4--5--6--7
            let positions = [|
                V3f(0.0f, 0.0f, 0.0f); V3f.NaN;                V3f(2.0f, 0.0f, 0.0f); V3f(3.0f, 0.0f, 0.0f)
                V3f(0.0f, 1.0f, 0.0f); V3f(1.0f, 1.0f, 0.0f); V3f(2.0f, 1.0f, 0.0f); V3f(3.0f, 1.0f, 0.0f)
            |]
            let size = V2i(4, 2)

            let indices = TriangleSet.computeGridIndices size positions

            // quad(0,0): 0,4,1,5 → touches 1 (NaN) → skip
            // quad(1,0): 1,5,2,6 → touches 1 (NaN) → skip
            // quad(2,0): 2,6,3,7 → all valid → 6 indices
            Expect.equal indices.Length 6 "only 1 valid quad"
            // check the indices: tri1 = 2,6,3  tri2 = 3,6,7
            Expect.equal indices.[0] 2 "a"
            Expect.equal indices.[1] 6 "b"
            Expect.equal indices.[2] 3 "c"
            Expect.equal indices.[3] 3 "d"
            Expect.equal indices.[4] 6 "e"
            Expect.equal indices.[5] 7 "f"
        }

        test "computeGridIndices matches legacy on clean grid" {
            // 3x2 grid, no NaN → 2 quads → 4 triangles
            let positions = [|
                V3f(0.0f, 0.0f, 0.0f); V3f(1.0f, 0.0f, 0.0f); V3f(2.0f, 0.0f, 0.0f)
                V3f(0.0f, 1.0f, 0.0f); V3f(1.0f, 1.0f, 0.0f); V3f(2.0f, 1.0f, 0.0f)
            |]
            let size = V2i(3, 2)

            let legacyInvalids = DebugKdTreesX.getInvalidIndices3f positions |> List.toArray
            let legacyIndices = PRo3DCSharp.ComputeIndexArray(size, legacyInvalids)

            let newIndices = TriangleSet.computeGridIndices size positions

            // legacy pads with zeros for the full allocation, new only emits valid
            // but the actual index values for valid quads should match
            Expect.equal newIndices.Length 12 "2 quads * 6 indices"
            // compare index-by-index
            for i in 0 .. newIndices.Length - 1 do
                Expect.equal newIndices.[i] legacyIndices.[i] (sprintf "index %d should match" i)
        }

        test "real-world computeGridIndices vs legacy pipeline" {
            let araPath =
                Path.Combine(
                    "C:\\pro3ddata\\testdata", "Dimorphos_DRACO1", "Dimorphos_DRACO1",
                    "g_01960mm_spc_dtm_dimo_0000n00000_v003_0_0", "Patches", "0_0_1", "XYZ_Local.aara")
            if not (File.Exists araPath) then
                skiptest "OPC test data not available"

            let positions = araPath |> Aara.fromFile<V3f>
            let size = positions.Size.XY.ToV2i()
            let affine = Trafo3d.Identity
            let sw = System.Diagnostics.Stopwatch()

            // legacy pipeline: getInvalidIndices3f → ComputeIndexArray → getTriangleSet
            sw.Restart()
            let legacyInvalids = DebugKdTreesX.getInvalidIndices3f positions.Data |> List.toArray
            let legacyIndices = PRo3DCSharp.ComputeIndexArray(size, legacyInvalids)
            let legacyPipelineMs = sw.Elapsed.TotalMilliseconds

            let vertices = positions.Data |> Array.map (fun x -> x.ToV3d() |> affine.Forward.TransformPos)

            sw.Restart()
            let legacySet = DebugKdTreesX.getTriangleSet legacyIndices vertices
            let legacyTriSetMs = sw.Elapsed.TotalMilliseconds

            // new pipeline: computeGridIndices → getTriangleSet
            sw.Restart()
            let newIndices = TriangleSet.computeGridIndices size positions.Data
            let newPipelineMs = sw.Elapsed.TotalMilliseconds

            sw.Restart()
            let newSet = TriangleSet.getTriangleSet newIndices vertices
            let newTriSetMs = sw.Elapsed.TotalMilliseconds

            Log.line "[Test] index computation:  legacy=%.2fms (invalids+ComputeIndexArray) new=%.2fms (computeGridIndices) (%.1fx)" legacyPipelineMs newPipelineMs (legacyPipelineMs / newPipelineMs)
            Log.line "[Test] triangle building:  legacy=%.2fms new=%.2fms (%.1fx)" legacyTriSetMs newTriSetMs (legacyTriSetMs / newTriSetMs)
            Log.line "[Test] total:              legacy=%.2fms new=%.2fms (%.1fx)" (legacyPipelineMs + legacyTriSetMs) (newPipelineMs + newTriSetMs) ((legacyPipelineMs + legacyTriSetMs) / (newPipelineMs + newTriSetMs))
            Log.line "[Test] legacy indices: %d (padded), new indices: %d (compact)" legacyIndices.Length newIndices.Length

            Expect.equal (triangleCount newSet) (triangleCount legacySet) "same triangle count"

            // spot check triangles
            let legacyTris = extractTriangles legacySet
            let newTris = extractTriangles newSet
            Expect.equal newTris.[0] legacyTris.[0] "first triangle"
            Expect.equal newTris.[newTris.Length / 2] legacyTris.[legacyTris.Length / 2] "middle triangle"
            Expect.equal newTris.[newTris.Length - 1] legacyTris.[legacyTris.Length - 1] "last triangle"

            Log.line "[Test] %d triangles match across legacy and new pipeline" (triangleCount newSet)
        }

        test "getTriangleSet handles incomplete trailing chunk" {
            let vertices = [|
                V3d(0.0, 0.0, 0.0); V3d(1.0, 0.0, 0.0); V3d(0.0, 1.0, 0.0)
                V3d(2.0, 0.0, 0.0); V3d(3.0, 0.0, 0.0) // only 2 vertices - incomplete
            |]
            let indices = [| 0; 1; 2; 3; 4 |]

            let optimized = TriangleSet.getTriangleSet indices vertices
            let tris = extractTriangles optimized
            Expect.equal tris.Length 1 "incomplete chunk should be dropped"
        }
    ]
