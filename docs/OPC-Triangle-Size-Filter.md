# OPC triangle-size filter

Surfaces can hide triangles whose edges are longer than `TriangleSize` — the stretched
triangles that bridge gaps in OPC data. It is off by default, toggled per surface by the
**TriangleFilter** checkbox in the surface properties, with the threshold in the
**TriangleSize** box next to it.

Today the test runs in a geometry shader (`triangleSizeFilter`). This page describes the CPU
replacement being built in [#763](https://github.com/pro3d-space/PRo3D/issues/763);
`PRo3D.Base.TriangleFilter` is the first part of it.

## Why it moved off the GPU

On Apple Silicon the geometry-shader stage costs about **+78 ms/frame** on a near-surface
Victoria view — 13.7× the frame time without it. The cost is the stage *existing and
emitting*, not the filtering: a geometry shader that consumes every primitive and emits
nothing still costs +15.9 ms, and the size and distance filters cost the same as each other to
within 0.1 %. Optimising the shader body cannot help; only removing the stage can.

## Why the CPU can do it at all

The test looks camera-dependent and is not. It measures edge lengths in **view** space, the
view transform is rigid and so preserves distance, and the model transform does not depend on
the camera either: a view-space edge length is the patch-local length times the surface scale.
The predicate is therefore a **static property of the mesh** and can be evaluated once, when a
patch is loaded.

## How

`TriangleFilter.partitionByEdgeLength` **partitions** a patch's index buffer in place so the
triangles below the threshold come first, and returns how many. It does not delete anything:

- filter on → draw the first *n* triangles
- filter off → draw all of them

so the checkbox is a **draw count**, not a re-uploaded buffer. Only a change of `TriangleSize`
itself requires partitioning a patch again, which is cheap (below).

It is allocation-free: index triples are swapped inside the buffer rather than copied into a
second one, and edge lengths are compared squared so there is no `sqrt` per triangle. Each
triangle is classified exactly once, so there is no separate measure-then-filter pass.

Measured over all 16,675,578 triangles of HiRISE_VictoriaCrater:

| | time | allocation |
|---|---|---|
| compacting filter, with a separate max-edge pass | 203.6 ms | new 166 MB index buffer |
| partition, copying each `V3f` | 75.0 ms | 0 bytes |
| **partition, reading through `inref`** | **58.6 ms** | **0 bytes** |

That is the whole hierarchy; a single patch is well under a millisecond, on the patch loader
thread.

## Caveats

- **It mutates the index buffer it is given.** Only pass geometry the caller owns, never a
  shared or cached buffer.
- **`maxEdgeLength` must be pre-divided by the surface scale**, since the shader compares in
  view space.
- The partition is **unstable**. Safe here — the surface is opaque and depth-tested, so
  triangle order is not observable, and nothing in the OPC path reads `gl_PrimitiveID`.

## Not yet wired up

The shader still does the filtering. Connecting the partition needs control of a patch's index
buffer or draw range, and `PatchNode` currently exposes hooks only for **textures** and
**vertex attributes** (`Surface.Sg.fs`), so it needs an addition in
`Aardvark.GeoSpatial.Opc`. See [#763](https://github.com/pro3d-space/PRo3D/issues/763).
