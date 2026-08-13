namespace PRo3D.Core.Surface

open System.Collections.Generic
open Aardvark.Base
open Aardvark.Geometry

module TriangleSet =

    /// Builds triangle indices for a 2D grid of the given size, skipping
    /// any quad where at least one vertex position is NaN.
    /// Each grid cell (x,y) produces two triangles (6 indices).
    /// Replaces the combination of getInvalidIndices3f + ComputeIndexArray.
    let computeGridIndices (size : V2i) (positions : V3f[]) =
        let w = size.X
        let h = size.Y
        let maxQuads = (w - 1) * (h - 1)
        let result = List<int>(maxQuads * 6)

        for y in 0 .. h - 2 do
            for x in 0 .. w - 2 do
                let a = y * w + x
                let b = (y + 1) * w + x
                let c = y * w + x + 1
                let d = (y + 1) * w + x + 1

                if not (positions.[a].AnyNaN || positions.[b].AnyNaN ||
                        positions.[c].AnyNaN || positions.[d].AnyNaN) then
                    result.Add(a); result.Add(b); result.Add(c)
                    result.Add(c); result.Add(b); result.Add(d)

        result.ToArray()

    /// Top-left grid index of every quad `computeGridIndices` emits triangles for, in the
    /// same order - so triangle `i` belongs to quad `i / 2`.
    ///
    /// This is the compact form of the triangle-to-grid mapping needed to look up
    /// per-vertex data at a picked triangle: one int per quad instead of six, and no
    /// jagged per-triangle arrays. For a 1032x1032 patch that is 4 MB rather than the
    /// ~100 MB a `int[3]` per triangle costs, which matters because the mapping is cached
    /// per patch and rebuilt whenever the 3D cursor moves onto a new patch.
    let computeValidQuadStarts (size : V2i) (positions : V3f[]) =
        let w = size.X
        let h = size.Y
        let result = List<int>((w - 1) * (h - 1))

        for y in 0 .. h - 2 do
            for x in 0 .. w - 2 do
                let a = y * w + x
                let b = a + w

                if not (positions.[a].AnyNaN || positions.[b].AnyNaN ||
                        positions.[a + 1].AnyNaN || positions.[b + 1].AnyNaN) then
                    result.Add a

        result.ToArray()


    let getInvalidIndices3f (positions : V3f[]) =
        let result = System.Collections.Generic.List<int>(64)
        for i in 0 .. positions.Length - 1 do
            if positions.[i].AnyNaN then
                result.Add(i)
        result.ToArray()

    let getTriangleSet (indices : int[]) (vertices : V3d[]) =
        let mutable count = 0
        let triangles = Array.zeroCreate (indices.Length / 3)
        let mutable i = 0
        while i + 2 < indices.Length do
            let t = Triangle3d(vertices.[indices.[i]], vertices.[indices.[i+1]], vertices.[indices.[i+2]])
            if not (t.P0.AnyNaN || t.P1.AnyNaN || t.P2.AnyNaN) then
                triangles.[count] <- t
                count <- count + 1
            i <- i + 3
        if count < triangles.Length then
            Array.sub triangles 0 count |> TriangleSet
        else
            triangles |> TriangleSet

    let getTriangleSet3f (vertices : V3f[]) =
        let mutable count = 0
        let triangles = Array.zeroCreate (vertices.Length / 3)
        let mutable i = 0
        while i + 2 < vertices.Length do
            let p0 = vertices.[i].ToV3d()
            let p1 = vertices.[i+1].ToV3d()
            let p2 = vertices.[i+2].ToV3d()
            if not (p0.AnyNaN || p1.AnyNaN || p2.AnyNaN) then
                triangles.[count] <- Triangle3d(p0, p1, p2)
                count <- count + 1
            i <- i + 3
        if count < triangles.Length then
            Array.sub triangles 0 count |> TriangleSet
        else
            triangles |> TriangleSet
