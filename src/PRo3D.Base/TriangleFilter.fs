namespace PRo3D.Base

open Aardvark.Base

/// CPU-side replacement for the `triangleSizeFilter` geometry shader (#763).
///
/// The shader drops triangles whose view-space edge lengths exceed `MaxTriangleSize`. On
/// Apple Silicon that geometry-shader stage costs ~+78 ms/frame on a near-surface Victoria
/// view -- and measurably so even when it emits nothing, because the cost is the stage
/// existing and emitting, not the filtering arithmetic (see #763).
///
/// The filter does not need to be on the GPU at all: it measures edge lengths in **view**
/// space, the view transform is rigid and therefore preserves distance, and the model
/// transform does not depend on the camera. The predicate is a **static property of the
/// mesh**, so it can be evaluated once when a patch is loaded.
module TriangleFilter =

    /// Module level rather than local: a local `let inline` cannot take a byref parameter
    /// (FS0412). Reading through `inref` rather than copying each V3f out of the array
    /// measured 22% faster over 16.7 M triangles (75.0 ms -> 58.6 ms).
    let inline private lengthSq (a : inref<V3f>) (b : inref<V3f>) =
        let dx = b.X - a.X
        let dy = b.Y - a.Y
        let dz = b.Z - a.Z
        dx * dx + dy * dy + dz * dz

    /// Partitions a triangle-list index buffer **in place** so that every triangle whose
    /// longest edge is shorter than `maxEdgeLength` comes first, and returns how many those
    /// are.
    ///
    /// Drawing that many triangles is the filtered surface; drawing all of them is the
    /// unfiltered one. So the `TriangleFilter` checkbox becomes a draw count rather than a
    /// re-uploaded index buffer, and only a change of `triangleSize` itself needs the patch
    /// partitioned again.
    ///
    /// Allocation-free: index triples are swapped in the buffer given, never copied into a
    /// second one. Edge lengths are compared squared, so there is no `sqrt` per triangle, and
    /// each triangle is classified exactly once because every iteration removes one from the
    /// unclassified range. Measured at 58.6 ms for all 16,675,578 triangles of
    /// HiRISE_VictoriaCrater, allocating 0 bytes.
    ///
    /// The partition is unstable. That is safe here: the surface is opaque and depth-tested,
    /// so triangle order is not observable, and nothing in the OPC path reads
    /// `gl_PrimitiveID`.
    ///
    /// `positions` are patch-local, so `maxEdgeLength` must already be divided by the
    /// surface's scale -- the shader compares in view space, and view-space edge length is
    /// the local length times that scale.
    ///
    /// **Mutates `index`.** Only pass a buffer owned by the caller (freshly loaded patch
    /// geometry), never a shared or cached one.
    let partitionByEdgeLength (positions : V3f[]) (index : int[]) (triangleCount : int) (maxEdgeLength : float32) : int =
        let limit = maxEdgeLength * maxEdgeLength

        let inline isSmall (t : int) =
            let i = 3 * t
            let ia = index.[i]
            let ib = index.[i + 1]
            let ic = index.[i + 2]
            lengthSq &positions.[ia] &positions.[ib] < limit
            && lengthSq &positions.[ib] &positions.[ic] < limit
            && lengthSq &positions.[ic] &positions.[ia] < limit

        let inline swap (x : int) (y : int) =
            let ix = 3 * x
            let iy = 3 * y
            let t0 = index.[ix]
            let t1 = index.[ix + 1]
            let t2 = index.[ix + 2]
            index.[ix]     <- index.[iy]
            index.[ix + 1] <- index.[iy + 1]
            index.[ix + 2] <- index.[iy + 2]
            index.[iy]     <- t0
            index.[iy + 1] <- t1
            index.[iy + 2] <- t2

        let mutable lo = 0
        let mutable hi = triangleCount - 1
        while lo <= hi do
            if isSmall lo then
                lo <- lo + 1
            else
                swap lo hi
                hi <- hi - 1
        lo
