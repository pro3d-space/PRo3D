# Color Legends

The color legends of the main render view sit side by side in one **legend dock** in the
**bottom-left corner**, just above the [navigation gizmo](NavigationGizmo.md). Each legend
is a tile with a header, so you can see which colormap belongs to what. You can close a
legend straight from the view and rearrange the tiles.

## The legends

| Legend | Header | Shown when | Turn it back on in |
|---|---|---|---|
| Dip & Strike | `Dip angle [°]` | *show legend* in the Dip&Strike ColorLegend panel | *Dip&Strike ColorLegend* panel → *show legend* |
| [Color by Category](ColorByCategory.md) | attribute and unit, or the surface layer name | Color by Category enabled, *show legend* ticked, attribute numeric or cyclic | *Color by Category* panel → *show legend* |
| Surface scalar | `<scalar layer> — <surface>` | a single surface is selected, it has a selected scalar layer, and that layer's *show legend* is ticked | *Scalars ColorLegend* panel of the surface → *show legend* |
| Surface color map | `Color Map — <surface>` | a single surface is selected, its Transfer Function is *Ramp*, and *show legend* is ticked | surface's *Properties* panel, Color Map section → *show legend* |
| Projected image | `<image file> · <channel>` | a projected image is selected and its legend toggle is on | projected image's edit panel → *Show color legend* |
| [Surface comparison](SurfaceComparison.md) | `Distance [m] — <area>` | a comparison area with statistics is selected | Surface Comparison → paint brush icon in the area's row |

The depth legend of the instrument view is not part of the dock. Its *show legend* also
switches the depth coloring on and off, so it has no close button.

## Closing a legend

Click the **×** in a tile's header. This hides **only the legend**: the colors on the
surface or annotations stay as they are. The legend's *show legend* toggle in its panel is
cleared at the same time, so tick it there to bring the legend back.

The surface scalar legend belongs to a scalar layer. Closing it hides it for that layer
only; selecting another surface whose legend is on shows that one.

## Arranging the legends

- Tiles are laid out left to right in a **saved order**. The **⇄** button between two
  neighbouring tiles swaps them.
- A closed legend keeps its place in the order. When it is shown again it returns to that
  place, not to the end of the row.
- When a legend closes, the tiles on either side become neighbours and get a ⇄ between
  them. Tiles to its right slide left; their order does not change.
- The order is saved with the scene (`config.colorLegendOrder`). Scenes saved before it
  existed load with the default order: Dip & Strike, Color by Category, Surface scalar,
  Surface color map, Projected image, Surface comparison.
- If the row gets wider than the view, the dock scrolls horizontally instead of wrapping
  to a second row (that would halve the ramps' height). Scroll it with Shift + mouse wheel
  over a tile header.

Dragging over a legend's ramp still navigates the camera. Only the tile headers and the ⇄
buttons take mouse input.

## Implementation

- Order and swap: `ColorLegendKind` and `ViewConfigModel.colorLegendOrder` in
  [`ViewConfigModel.fs`](../src/PRo3D.Core/ViewConfigModel.fs); the swap action is
  `ConfigProperties.SwapColorLegends` in
  [`ViewconfigApp.fs`](../src/PRo3D.Viewer/ViewconfigApp.fs). The field is read with
  `Json.tryRead` and passed through `ColorLegendKind.normalize`, so the scene version is
  unchanged.
- Dock and tiles: `Gui.colorLegendDock` in
  [`ViewerGUI.fs`](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs). There is one `LegendTile` per
  legend. Its `header` is `None` while the legend is hidden, otherwise it holds the title
  and the close message. Tile DOM nodes are built once; the dock only re-lists the
  visible ones in `colorLegendOrder` and puts swap buttons between them.
- Close messages set the visibility explicitly rather than toggling it, so a double click
  cannot show the legend again: `FalseColorLegendApp.SetUseFalseColors`,
  `SurfaceProperties.SetColorMapLegend` (surface color map),
  `ImageMessage.SetFalseColorLegend` (projected images) and
  `AreaSelectionAction.SetLegendVisible` (comparison areas). A comparison area's hidden
  legend stays hidden when its statistics are recomputed.
- The surface color map legend has no `FalseColorsModel` of its own — its range and LUT
  name live only on `Surface.transferFunction.tf` (`ColorMaps.TF.Ramp(min,max,name)`, set
  via the *Min*/*Max*/*Color Map* controls), so there is nothing to keep in sync. Its
  gradient is drawn by `FalseColorLegendApp.Draw.createColorMapLegendBasics`, which shares
  the named-image rendering path of `createFalseColorLegendBasics` via the private
  `legendBody` helper.
- Each ramp's SVG gradient needs a document-unique id: `DnsLegend`,
  `ColorByCategoryLegend`, `ScalarLegend`, `ColorMapLegend`, `ProjectedLegend`,
  `ComparisonLegend`.
- Styles: `.pro3d-legend-*` in
  [`semui-overrides.css`](../src/PRo3D.Viewer/resources/semui-overrides.css).
- Tests: [`ColorLegendDockTest.fs`](../src/Tests/ColorLegendDockTest.fs).
