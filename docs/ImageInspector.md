# Image Inspector (experimental)

Shows the image selected in *GIS View → projected images* in its own panel, linked to the 3D view,
and hosts the [shadow measurement](ShadowMeasurement.md) tool. Design:
[dev/shadowEstimation.md](dev/shadowEstimation.md).

## Use

Open **Image Inspector** from the **GIS** window layout or add it to any layout. It starts behind
an *experimental feature* notice and does nothing until you click **Open anyway**; **Close**
returns to the notice. Every session starts there.

- **Hover the image** — marks where that pixel's camera ray meets the surface in the 3D view
  (axis cross plus the yellow ray); the bottom bar shows the pixel (0-based, top-left origin).
- **Ctrl-hover the 3D view** — a cyan crosshair marks that surface point in the image.
- **Wheel** zooms about the pointer (up to 64×), **drag** pans, **double-click** or **Reset**
  shows the whole image. Zoomed in, pixel borders show the precision of a click.
- **Measure shadow** — see [ShadowMeasurement.md](ShadowMeasurement.md).

If the panel names something missing, select an image, set the observed body, or bind a surface
to a SPICE body.

## Notes

- The panel draws the image the way the projection samples it, so it checks that the two agree
  (orientation, flips), not that the projection matches reality.
- Nothing is saved with the scene except scale bars created from a measurement.
- The projection surface is the first surface bound to a SPICE body (as for *fly to image*).
- The first hovers can lag while kd-trees load.
