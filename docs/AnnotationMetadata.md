# Annotation metadata

Each annotation carries four pieces of provenance and quality information
([#887](https://github.com/pro3d-space/PRo3D/issues/887)):

| field | set by | what it holds |
|---|---|---|
| **Confidence** | you: keys **1–4**, or the Properties panel | 1 (lowest) to 4 (highest), from the identification criteria. Unrated until set. |
| **Comment** | you: key **C**, or the Properties panel | free text, any length, new lines allowed |
| **Shape model** | PRo3D, when the annotation is finished | the shape model of the surface it was drawn on |
| **Images used** | PRo3D, when the annotation is finished | the projected images that were painted under its points |

All four show in the annotation's **Properties** panel and can be exported (see
[Export](#export)).

## Confidence: keys 1–4

Select one or more annotations with a tool other than *Draw annotation* (e.g. *Select
annotation*), then press **1**, **2**, **3** or **4** in the 3D view.
All selected annotations get that rating as one undo step (Ctrl+Z takes it back), and a
short message top right says what happened, e.g. *Confidence 3 set on 2 annotations*.

The keys only rate while annotations are selected **and the draw tool is not active**. With
*Draw annotation* active, **0–3** set the semantic of the next annotation as before, even
though finishing an annotation selects it. With Ctrl held, 1–4 and C do neither. The **–**
button in Properties removes a rating.

### Confidence labels

Rated annotations show their rating next to them in 3D, as `C3`, or appended to the
annotation's own label: `Boulder A [C3]`. The label shows even when *Show Text* is off. It
is drawn like the annotation's text, at its *TextSize* (Properties): at the default of 0.05
it is small from a distance, so raise TextSize to read it from far away.
Switch the labels off for all annotations at once in the **Annotations** panel under
*Confidence & Comments → Confidence labels*. The switch is not saved with the scene; labels
are on in every new session.

## Comment: key C

Select **one** annotation (draw tool not active) and press **C** in the 3D view. A small
dialog opens next to the
annotation, so it stays in view while you type. The dialog follows the annotation when the
camera moves.

- The text field has focus straight away: just start typing.
- **Enter** saves and closes the dialog (the same as *OK*). The keys go straight back to the
  3D view, so the next **1–4** or **C** works without moving the mouse.
- **Shift+Enter** starts a new line.
- **Esc** or *Cancel* closes it without changing anything, and so does Esc in the 3D view.
- A blank comment removes the comment; trailing blanks and line breaks are dropped.

The comment can also be edited in Properties, in a multi-line field that saves when you
leave it.

With no annotation selected, or with the draw tool active, **C** still re-centres the orbit
on the body centre. With several selected, it shows a hint instead.

## Shape model

When an annotation is finished, PRo3D reads the `*.opc.json` sidecar of the surface it was
drawn on and stores the shape model in the annotation. Replacing the surface's OPC later
therefore does not change what an old annotation reports.

| sidecar contains | Shape model shows | example |
|---|---|---|
| a FITS header (`fits2mbi.fits_header`), as the HERA V4 global OPCs built from a FITS DTM do | `DATASRCV`, `MAP_VER` and the product `PRODNAME` | `dalyErnst_dimorphos230530 004 (dimorphos_g_0243mm_spc_dtm_0000n00000_v004)` |
| no FITS header, e.g. OPCs built from an OBJ such as the V4 *Dimorphos_Local* | the OPC file name and its `_vNNN` | `dimorphos_l_0050mm_spc_obj_0897s26313_v004 v004, version from file name` |
| neither | the OPC file name only | |

Annotations drawn before this feature, or on surfaces that are not OPCs, show *not
recorded*.

## Credits

Shape models are years of someone's work, so PRo3D shows who made the model wherever it
shows the model:
- the surface's **Source & Credits** section (below)
- the annotation's *Shape Model* row in Properties
- the `shapeModelCredit` export column

The credit is captured with the shape model when an annotation is finished.

It is taken from the first of these that exists:

1. **What the product states.** In order: `product_information.credit` in the `*.opc.json`
   sidecar, a top-level `"credit"`, or the FITS `AUTHOR`, `CREDIT` or `REFERENC` keyword. None
   of the products seen so far has one.
2. **The citation of the archive the model comes from**, from a table of citations checked
   against the archive (`ShapeModelCredits.citations` in
   [Annotation-Model.fs](../src/PRo3D.Base/Annotation/Annotation-Model.fs)). An entry matches a
   product family by the product name, the OPC file name or the SPC model name (`DATASRCV`).
   Today it holds the final pre-impact Dimorphos model, which covers the global DTM and the
   local models of v004 alike:

   > Daly, T., Barnouin, O., Ernst, C., Nair, H., Espiritu, R., Waller, D. (2023). Derived data
   > products for DART shapemodel: dimorphos_model_v004. NASA Planetary Data System.
   > doi:10.26007/0nss-vd15

   The citation is that of the PDS collection
   (`urn:nasa:pds:dart_shapemodel:data_derived_dimorphos_model_v004`, PDS Small Bodies Node).
   The FITS header carries no authors, and neither does the PDS4 label of the global DTM.
   Add an entry per model family once its citation has been checked against its archive.

Models matched by neither show *No credit known for this model*.

### Asking producers for a credit

The FITS header of a delivered shape model cannot be changed. The place for a credit is the
sidecar the OPC producer writes (JOANNEUM's ExportGpc, schema
`https://www.joanneum.at/jim/product_information.schema.json`). It needs one string in
`product_information`:

```json
"product_information": {
    "product_type": "opc",
    ...
    "credit": "Daly, T., Barnouin, O., Ernst, C., Nair, H., Espiritu, R., Waller, D. (2023). Derived data products for DART shapemodel: dimorphos_model_v004. NASA Planetary Data System. doi:10.26007/0nss-vd15"
}
```

PRo3D shows it as it is, ahead of the table, for OPCs with a FITS header and for those built
from an OBJ alike. When the source is a PDS product, its PDS4 label's `Citation_Information`,
or the collection's citation and DOI, is what belongs there. This has been requested from
JOANNEUM.

## Source & Credits (Surfaces panel)

*Surfaces → Source & Credits* (closed by default) shows the selected surface's origin, read from
its `*.opc.json` once per session:

- **the credit**, bold, on top
- from the FITS header, when the sidecar has one: shape model (`DATASRCV`), version
  (`MAP_VER`), model date, method (SPC), product, map type, grid spacing, mission, target,
  origin, software, production date
- without a header: the product and the version from the file name, saying so
- the OPC itself: created when and by which tool, the DEM reference model, the DSK body and
  frame for BDS-derived OPCs, and the sidecar's path
- *FITS header (n keys)*, folded away: every keyword with its value

Surfaces that are not OPCs say so. Nothing here is saved in the scene.

## Images used

When an annotation is finished, PRo3D looks at each of its control points in turn. It takes
the topmost image of the [projection stack](MultiImageProjection.md) that contains the point
in its footprint and from which the point is visible. Every image found this way is listed
once, in the order it first appears along the annotation.

- **Same footprint as the renderer.** The point must lie inside the image's view volume,
  exactly as the shader tests it. The hover preview is not counted, only the stack.
- **Visibility replaces the shader's facing test.** PRo3D casts a ray from the image's
  projection centre towards the point. The point counts only if it is the first terrain the
  ray meets (within 5 cm, or 1 cm per km of distance). Unlike the renderer, this also excludes
  terrain hidden from the instrument, which the projection still paints
  ([hidden terrain is painted too](MultiImageProjection.md#known-limitation-hidden-terrain-is-painted-too)).
  So behind a ridge, an annotation can list a lower image than the one you saw painted
  there. The listed one is the image that actually saw the point.
- The list is captured once, when the annotation is finished. Moving its points later does
  not update it.
- The points are placed with the same surface the projection is computed on (the first
  surface bound to a SPICE body, as in the [Image Inspector](ImageInspector.md)). Annotations
  on a second OPC with its own transformation are tested in that surface's frame.
- The projection stack itself is not saved with the scene; this list is.

Each entry stores the image's file name, instrument and observation time (UTC).

## Export

The annotation export ([AnnotationExport.md](AnnotationExport.md)) has six columns for
this. They are part of the default selection, so a default export has six more columns than
before:

| column | content |
|---|---|
| `confidence` | 1–4, empty when unrated |
| `comment` | the comment |
| `shapeModel` | SPC model name, else the OPC product |
| `shapeModelVersion` | `MAP_VER`, else the `vNNN` of the file name |
| `shapeModelCredit` | who made the shape model ([Credits](#credits)) |
| `projectedImages` | the image file names, separated by `; ` |

## Annotations made other ways

Shape model and images are captured only for annotations drawn in the 3D view. Annotations
from a CSV polygon import, supporting profiles and geological-map label points start
without them, and so do annotations drawn before this feature. A boolean operation's result
keeps the metadata of the first annotation it was made from.

## Scene files

The metadata is saved under four keys of its own on each annotation: `confidence`,
`comment`, `shapeModel` and `projectedImagesUsed`. A key is only written when it has a value.
The annotation version stays 5, so scenes saved with this feature still open in older
releases, which skip the keys. Damaged entries are dropped when a scene is read, rather
than failing the scene.

## Implementation

| What | Where |
|---|---|
| `AnnotationMetadata`, `ShapeModelRef`, `ProjectedImageRef`, JSON | [Annotation-Model.fs](../src/PRo3D.Base/Annotation/Annotation-Model.fs) |
| FITS header, credit and shape model from the sidecar (`OpcMetadata.shapeModelOf`, `sourceRows`) | [OpcMetadata.fs](../src/PRo3D.Core/OpcMetadata.fs) |
| archive citations (`ShapeModelCredits.citations`) | [Annotation-Model.fs](../src/PRo3D.Base/Annotation/Annotation-Model.fs) |
| Source & Credits section (`viewSource`) | [SurfaceApp.fs](../src/PRo3D.Core/Surface/SurfaceApp.fs) |
| sub-app: keys, comment dialog, capture at finish, settings | [AnnotationMetadataApp.fs](../src/PRo3D.Viewer/Viewer/AnnotationMetadataApp.fs), model in [AnnotationMetadata-Model.fs](../src/PRo3D.Viewer/AnnotationMetadata-Model.fs) |
| per-image projector, any library image (`ImageInspector.tryContextFor`) | [ImageInspector.fs](../src/PRo3D.Viewer/Viewer/ImageInspector.fs) |
| key routing (`metadataKeyAction`), capture hook (`withAnnotationMetadata`) | [Viewer.fs](../src/PRo3D.Viewer/Viewer/Viewer.fs) |
| Properties rows | [Drawing-Properties.fs](../src/PRo3D.Core/Drawing/Drawing-Properties.fs) |
| confidence in the 3D label (`labelText`) | [Drawing-App.fs](../src/PRo3D.Core/Drawing/Drawing-App.fs) |
| export columns | [AnnotationFields.fs](../src/PRo3D.Base/Annotation/Exporters/AnnotationFields.fs) |
| tests | [Section23_AnnotationMetadata.fs](../src/Tests/Features/Section23_AnnotationMetadata.fs); `PRO3D_V4_OPCS` points the shape-model test at the HERA V4 OPCs |
| end to end | [annotation-metadata.spec.ts](../tests-ui/tests/annotation-metadata.spec.ts): an AFC frame on the stack, a Sky line in its footprint, 2 and C in the 3D view, then Properties and the CSV export; [probe-annotation-metadata.ts](../tests-ui/src/probe-annotation-metadata.ts) screenshots the same plus *Source & Credits* (`PRO3D_PROBE_OPC` draws on another OPC, e.g. the V4 DRACO one) |

The dialog's text stays in the browser while you type and reaches the model only on Enter.
Only the dialog's position follows the camera, so a camera move never rebuilds the text
field. The position is the annotation's centre projected with the camera, in percent of the
view, so it needs no pixel size. While a text field has focus, the 3D view no longer takes
focus when the cursor enters it.
