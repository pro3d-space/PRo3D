# Shadows & Highlights (fullscreen surface effect) — testbed

**Status:** in development. The effect is a placeholder (identity). The pipeline and a testbed are in place, but the main viewer does not use it yet.

## What it is

A fullscreen pass over the **surfaces only** (not annotations, overlays or fills) that adjusts shadows and highlights. It builds on the surfaces' render-to-texture path (`OffscreenSurfaces`, #841):

```
surfaces ──OffscreenSurfaces.render──► colour + depth (MSAA textures)
                                           │
                       ShadowHighlight.apply (fullscreen pass, per sample)
                                           │
                                 processed colour + original depth
                                           │
                    OffscreenSurfaces.blit ──► view  (annotations etc. drawn afterwards)
```

- Code: `src/PRo3D.Core/ShadowHighlight.fs`
  - `Shader.effect` — **the effect goes here** (marked `TODO (shadows & highlights)`)
  - `apply` — the pass, rendered into a texture with the input's size, format (Rgba8) and sample count. Add uniforms or extra passes here.
  - `blit enabled ...` — a drop-in replacement for `OffscreenSurfaces.blit` that switches the pass on and off. While off, the pass's target is released.
- Inputs per sample: premultiplied colour (`color.Read(p, sid)`) and window depth (`depth.Read(p, sid).X`, where 1 means no surface).
- Output: premultiplied colour. Keep alpha, and un-premultiply before any tonal change.
- macOS: the shader must compile on Apple's OpenGL 4.1 core. Pass anything the CPU already knows as a uniform instead of querying it in GLSL (see `ai/RENDERING.md`, *Native & Platform Notes*).

## Testbed: OpcViewer

```
OpcViewer <opc-dir> [<opc-dir> ...] [--shadow-highlight] [--samples <n>]
```

| Option | Meaning |
|---|---|
| `<opc-dir>` | An OPC, or a folder of OPCs (searched up to two levels deep) |
| `--shadow-highlight` | Start with the pass on |
| `--no-shadow-highlight` | Start with the pass off (the default) |
| `--samples <n>` | Multisampling for the window and the surfaces (default 4) |

Keys: **H** toggles the pass. **WASD** and the mouse fly the camera. **PageUp/PageDown** change the flying speed.

Run without arguments, OpcViewer starts its old hard-coded viewers, as before.

Example (Debug build):

```
bin\Debug\net9.0\OpcViewer.exe C:\data\Didymos_SK_OPC__texture --shadow-highlight
```

## Integrating into PRo3D (later)

In `src/PRo3D.Viewer/Viewer/Viewer-Utils.fs`, `offscreenScene` (marked `TODO (shadows & highlights)`), replace `OffscreenSurfaces.blit` with `ShadowHighlight.blit enabled ...`. `enabled` comes from the scene config or UI, which still needs to be added (persisted with `Json.tryRead` and a default). That change is user-visible, so it also needs an entry in `NEXT_RELEASE_NOTES.md`.
