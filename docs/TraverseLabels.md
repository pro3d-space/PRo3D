### Traverse Sol Labels

Every traverse can display its sol numbers next to the waypoints (*Show Text* in the traverse
properties). Two render paths exist, selected per traverse with the *Fast Text* checkbox:

| Mode | Path | Trade-off |
| --- | --- | --- |
| *Fast Text* on (default) | `drawSolTextsFast`, one batched billboard draw (`Sg.textsWithConfig`) | fast for long traverses, but the world-space trafo is applied in `float32` in the shader, so labels jitter at planetary scale |
| *Fast Text* off | `drawSolText` → `PRo3D.Base.Sg.text`, one stable-trafo label each | numerically stable, no jitter, slower |

### Text size

*Textsize* is **not** a size in meters: labels keep a constant size on screen, and the value is a
fraction of the viewport. Both paths use the same convention, `PRo3D.Base.Sg.invariantScale`:

```
scale = tan(hfov / 2) · size · distance-to-camera
```

This is the same convention used by annotation labels, scale bars, the reference system and
surface name labels — a *Textsize* of `0.05` (the default) means the same thing everywhere in
PRo3D. Both paths are fed the **actual** horizontal field of view, so labels keep their size when
the focal length changes.

Do not reimplement this formula. It exists once, in `PRo3D.Base.Sg.invariantScale`; the batched
path calls it on the CPU per label, the stable path through `invariantScaleTrafo`.

### Migration of stored sizes

Before this convention was shared, the batched path scaled with `size · 2 · dist · tan(hfov)` —
a factor of **6** larger than the stable path at the 60° reference field of view, which is why
saved scenes contain unusually small values (e.g. `0.005`).

Stored values are converted when a scene is loaded (`Traverse.migrateTextSize`), so labels keep
the size the user picked:

- scenes written with the current convention carry a `tTextSizeConvention` marker and are left alone,
- scenes without the marker have their `tTextSize` multiplied by 6 (clamped to the input's `max`),
  unless the traverse explicitly stored `fastText = false` — the stable path formula did not change.

The marker is an **additional field** read with `Json.tryRead`, so the scene file version was
deliberately *not* bumped and older PRo3D versions still load the files. The one asymmetry: an
older PRo3D version reading a migrated scene renders the labels 6× too large.
