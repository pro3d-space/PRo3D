/// `TriangleFilter.partitionByEdgeLength` replaces the `triangleSizeFilter` geometry shader
/// on the CPU (#763). These pin the three properties the replacement depends on: it selects
/// exactly the triangles the shader would keep, it preserves every triangle (the filter is a
/// reordering, so the checkbox can toggle by draw count), and it allocates nothing.
module PRo3D.Tests.TriangleFilterTests

open Expecto

open Aardvark.Base

open PRo3D.Base

/// A grid mesh with a handful of deliberately over-long triangles, like the stretched
/// triangles that bridge data gaps in a real OPC.
let private mesh (n : int) =
    let positions = Array.init (n * n) (fun i -> V3f(float32 (i % n), float32 (i / n), 0.0f))
    let index = ResizeArray<int>()
    for y in 0 .. n - 2 do
        for x in 0 .. n - 2 do
            let a = y * n + x
            let b = a + 1
            let c = a + n
            let d = c + 1
            index.AddRange [ a; b; c ]
            index.AddRange [ b; d; c ]
    positions, index.ToArray()

/// Longest edge of triangle `t`, computed independently of the implementation under test.
let private longestEdge (positions : V3f[]) (index : int[]) (t : int) =
    let a = positions.[index.[3 * t]]
    let b = positions.[index.[3 * t + 1]]
    let c = positions.[index.[3 * t + 2]]
    max (Vec.Distance(a, b)) (max (Vec.Distance(b, c)) (Vec.Distance(c, a)))

/// Multiset of triangles as sorted vertex triples, so a reordering compares equal.
let private triangleSet (index : int[]) =
    Array.init (index.Length / 3) (fun t ->
        let tri = [| index.[3 * t]; index.[3 * t + 1]; index.[3 * t + 2] |]
        Array.sortInPlace tri
        tri.[0], tri.[1], tri.[2])
    |> Array.sort

let tests () =
    testList "triangle size filter (#763)" [

        test "the prefix is exactly the triangles below the threshold" {
            let positions, index = mesh 24
            let triangleCount = index.Length / 3
            // stretch one corner vertex far away, so every triangle touching it is over-long
            positions.[0] <- V3f(500.0f, 500.0f, 0.0f)
            let threshold = 2.0f

            let expected =
                Array.init triangleCount (fun t -> longestEdge positions index t < threshold)
                |> Array.filter id
                |> Array.length

            let split = TriangleFilter.partitionByEdgeLength positions index triangleCount threshold

            Expect.equal split expected "the split counts the triangles the shader would keep"
            Expect.isGreaterThan split 0 "some triangles survive"
            Expect.isLessThan split triangleCount "and some are filtered, or the case is vacuous"

            for t in 0 .. split - 1 do
                Expect.isLessThan (longestEdge positions index t) threshold
                    (sprintf "triangle %d is in the kept prefix" t)
            for t in split .. triangleCount - 1 do
                Expect.isGreaterThanOrEqual (longestEdge positions index t) threshold
                    (sprintf "triangle %d is in the filtered tail" t)
        }

        test "no triangle is lost, so the toggle can be a draw count" {
            let positions, index = mesh 16
            positions.[0] <- V3f(500.0f, 500.0f, 0.0f)
            let before = triangleSet index

            TriangleFilter.partitionByEdgeLength positions index (index.Length / 3) 2.0f |> ignore

            Expect.equal (triangleSet index) before
                "the buffer still holds exactly the same triangles, only reordered"
        }

        test "everything passes an infinite threshold, nothing passes a zero one" {
            let positions, index = mesh 12
            let triangleCount = index.Length / 3
            Expect.equal (TriangleFilter.partitionByEdgeLength positions index triangleCount infinityf)
                triangleCount "an unreachable threshold keeps every triangle"
            Expect.equal (TriangleFilter.partitionByEdgeLength positions index triangleCount 0.0f)
                0 "a zero threshold keeps none"
        }

        test "the partition allocates nothing" {
            let positions, index = mesh 64
            let triangleCount = index.Length / 3
            positions.[0] <- V3f(500.0f, 500.0f, 0.0f)
            // one warm-up so JIT compilation is not counted as allocation
            TriangleFilter.partitionByEdgeLength positions index triangleCount 2.0f |> ignore

            let before = System.GC.GetAllocatedBytesForCurrentThread()
            TriangleFilter.partitionByEdgeLength positions index triangleCount 2.0f |> ignore
            let allocated = System.GC.GetAllocatedBytesForCurrentThread() - before

            Expect.equal allocated 0L
                "partitioning in place must not allocate: it runs per patch on the loader thread"
        }
    ]
