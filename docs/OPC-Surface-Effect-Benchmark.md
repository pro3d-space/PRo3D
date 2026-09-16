# OPC surface effect benchmark (`Tests.dll --bench`)

Measures the frame-time cost of the four OPC surface-effect variants (#719) through the
viewer's own scene graph, on macOS/Apple Silicon.

It is a **tool, not a test.** It does not run under Expecto and is not part of the
correctness suite: a frame-time threshold on a developer machine is a flaky test, and the
GL context has to be created on the main thread (see "macOS" below).

```bash
dotnet bin/Release/net9.0/Tests.dll --bench
```

## What it measures

Four arms, one per pool variant, each rendering the **same geometry to the same pixels** so
that any difference is the shader stage and not the work:

| arm | how it is forced | composed |
|-----|------------------|----------|
| `lean` | the default model | neither |
| `geometryStage` | `filterByTriangleSize` with `triangleSize = 1e9` — a maximum no triangle exceeds, so the filter runs but drops nothing | `triangleSizeFilter` + `generateNormal` |
| `crossSection` | a small cross-section polygon placed ~1e6 m from the terrain | `crossSectionClip` |
| `both` | both of the above | both |

Every arm asserts, before its timing is believed, that it draws the OPC, draws the same
pixels as `lean`, and submits the same draw calls. Images are written to
`benchmark-output/` for inspection — a fast render of the wrong thing is worthless.

### Method

- Arms are **interleaved** (a,b,c,d,a,b,c,d…), not run in blocks, so drift over the run hits
  every arm equally instead of landing on whichever ran last. `lean` is reported first-vs-last
  as a drift check.
- Warm-up batches after each program swap are **discarded**. Measured directly, one `lean`
  configuration drifted 4.383 → 0.979 ms/frame — larger than the effect being measured.
- Batches submit every frame and sync **once** at the end: GL queues commands, so timing a
  single `task.Run` measures CPU submission, and a per-frame readback would stall the pipeline.
- Settling renders until two consecutive frames are byte-identical, with a cap.

### Options

`PRO3D_BENCH_OPC` (OPC directory), `PRO3D_BENCH_EYE` / `PRO3D_BENCH_FWD` (camera, as PRo3D's
readout prints it), `PRO3D_BENCH_SIZES`, `PRO3D_BENCH_FRAMES`, `PRO3D_BENCH_REPEATS`,
`PRO3D_BENCH_ROUNDS`, `PRO3D_BENCH_WARMUP`, `PRO3D_BENCH_SETTLE_CAP`. Without
`PRO3D_BENCH_OPC` it uses `PRO3D_TEST_DATA`.

## Results (Apple M1, 4x MSAA, ms/frame, 9 batches per arm)

**HiRISE_VictoriaCrater**, near-surface camera
(`--eye 3376474.17 -324507.68 -121181.49`, fwd `-0.2756 -0.4158 -0.8667`):

| resolution | lean | geometryStage | crossSection | both |
|---|---|---|---|---|
| 320×240   | 4.568 | 81.969 (**17.94×**) | 5.566 (1.22×) | 88.361 (19.34×) |
| 1024×768  | 6.106 | 83.882 (**13.74×**) | 7.608 (1.25×) | 92.645 (15.17×) |
| 1920×1200 | 6.920 | 87.787 (**12.69×**) | 8.990 (1.30×) | 95.846 (13.85×) |

**1087_004779_MSLMST_0011** (small test fixture), bounding-box camera:

| resolution | lean | geometryStage | crossSection | both |
|---|---|---|---|---|
| 320×240   | 0.257 | 0.993 (3.86×) | 0.399 (1.55×) | 1.150 (4.47×) |
| 1024×768  | 0.655 | 1.697 (2.59×) | 1.037 (1.58×) | 1.817 (2.77×) |
| 1920×1200 | 1.501 | 3.033 (2.02×) | 1.998 (1.33×) | 3.113 (2.07×) |

## Other modes

- `PRO3D_BENCH_MODE=anatomy` — decomposes the geometry stage into existence, emission and
  computation by varying what the shader emits and which filter it runs. Arms that
  deliberately change the output are exempt from the same-pixels check.
- `--normal-cost` (with `PRO3D_BENCH_OPC`) — loads every patch of a hierarchy and times
  per-face and per-vertex normal generation against the geometry load, with memory totals.
- `--effect-inputs` — prints `Effect.Inputs` for a normal reader composed with and without a
  writer, to check whether `LocalNormal` becomes a vertex attribute.

## Findings

- **#719's 13.9× reproduces on the viewer's own path**: 13.74× at 1024×768 on Victoria.
  Until this run the figure came from the standalone OpcViewer harness only.
- **The camera decides the answer.** Framing Victoria from its bounding box — the whole
  dataset from above, at a coarse LoD — measures the geometry stage at **1.13×**. The same
  dataset from a near-surface camera measures **13.74×**. Any future benchmark must pin a
  realistic camera or it will report a flattering number.
- On Victoria the geometry-stage delta is **flat** across a 30× pixel range
  (+77.4 / +77.8 / +80.9 ms), i.e. vertex-bound, matching #719. On the small fixture it
  *scales* with area instead — that dataset is too small to measure this stage with.
- **`crossSectionClip` costs 1.22–1.30×.** Real and consistent, but ~60× smaller than the
  geometry stage. The discard axis earns its variant; it is not a headline effect.
- **GPU timer queries return 0 on this driver** (`ITimeQuery` via `IQueryRuntime`), so all
  numbers are wall clock. `RenderToken.TotalInstructions` is also 0; `DrawCallCount` works.
- **macOS: a GL context must be created on the main thread.** GLFW enters
  `-[NSApplication run]`, so `Render.context` forced from an Expecto worker thread deadlocks
  instead of skipping (observed as a 50-minute hang, not an error). `--gl-init-main` forces
  the lazy on the main thread before Expecto starts, which is what makes the GL-dependent
  Expecto tests runnable here at all.
- **The cross-section polygon marks what is cut away, not what is kept.** `Surface.Sg` writes
  `if poly.Contains q2 then -d else d` and `crossSectionClip` discards on `< 0`. A polygon
  containing the terrain discards all of it — which first showed up here as a fully black
  frame that still measured "faster than lean".
- **The geometry stage's cost is existence + emission; the arithmetic inside it is free.**
  A GS that consumes every primitive and emits nothing still costs +15.9 ms (3.6x) over no
  GS; emitting every triangle adds a further +64.9 ms. The size filter and the distance
  filter cost the same to within 0.1%, and running both costs the same again. Optimising the
  shader body cannot help; only removing the stage can. See issue #763.
- **CPU normal generation is cheap in time, expensive in memory**: 82 ms / 191 MB (per-face)
  or 98 ms / 96 MB (per-vertex) for all of Victoria's 16.7 M triangles; ~4x less with
  oct-encoding. Time is negligible next to texture loading; memory is the deciding factor.
- **macOS: the benchmark needs an awake display.** With the screen asleep GLFW's primary
  monitor is NULL and `glfwGetVideoMode` segfaults during GL init (`EXC_BAD_ACCESS at 0x100`).
  Run under `caffeinate -dis`.
- `crossSectionClip` does **not** need a curtain texture: `curtainEnabled` is independent of
  `clippingEnabled`.
