# Tool Strip

The tool strip is the vertical icon rail overlaid on the **right edge of the main
render view**. It carries the two selections that used to sit in the top toolbar as
dropdowns: the **navigation mode** and the **interaction** (the "tool").

The navigation modes themselves are described in
[Camera & Navigation](Navigation.md).

Both are enum-valued fields on the model (`navigation.navigationMode` and
`interaction`), so "nothing selected" cannot happen: **exactly one navigation icon
and exactly one tool icon is highlighted at all times**. The active one is drawn as a
filled chip in its group colour; idle icons are the same colour, dimmed.

Hovering an icon shows a tooltip on its left. The ctrl-click hint for the selected
tool ("CTRL+click to pick point on surface", …) sits on the **second toolbar row**,
where a full sentence fits, following straight on after that tool's own controls
(annotation geometry, rover selector, …). The row is labelled with the **selected
tool's name** ("Draw Annotation", "Place Rover", …; `interactionName` in `ViewerGUI.fs`) —
white on a full-height chip in the group colour of whichever tool is currently lit in
the strip.

## Groups

The **Navigation** group sits in its own panel, separated from the tool panel by a
small gap (`.pro3d-toolstrip-group` in [semui-overrides.css](../src/PRo3D.Viewer/resources/semui-overrides.css)).
Within the tool panel, groups are separated by a horizontal divider. Every group
shares an accent colour.

| Group | Colour | Icons |
|---|---|---|
| **Navigation** | blue | Free fly, ArcBall, Map view |
| **Annotation** | green | Draw annotation, Select annotation, Cut annotation, Edit annotation |
| **Selection** | amber | Select surface, Select area |
| **Placement** | violet | Place rover, Place distance point, Place scene object, Place scale bar, Pick pivot point |
| **Reference & camera** | teal | Place coordinate cross, Place surface reference system, Pick ArcBall orbit centre |

**Map view** needs a reference body: with `Planet.None` it is greyed out and not
clickable, the same rule the old navigation dropdown applied.

## Adding an interaction

The strip lists every `Interactions` case that is *not* in `Interactions.hideSet`
([Model.fs](../src/PRo3D.Core/Model.fs)). The two are not derived from one another, so
when un-hiding an interaction, add it to a group in `Gui.ToolStrip.view`
([ViewerGUI.fs](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs)) as well — otherwise it is
unreachable from the UI, and if something else selects it (an F-key, say) no tool icon
appears active.

Icon names must exist in **Semantic UI 2.2.6** (loaded from CDN, FontAwesome 4-era
set); newer FontAwesome 5 names silently render as an empty box.

A glyph the font does not carry can still be added as an SVG: give the button a
custom icon name (e.g. `pro3d-tire`) and, in [semui-overrides.css](../src/PRo3D.Viewer/resources/semui-overrides.css),
match `.pro3d-toolstrip .pro3d-tool i.icon.<name>` with a `mask` data-URI plus
`background-color: currentColor`. Painting through `currentColor` makes the SVG
pick up `--tool-color` and the active-chip knockout like the font icons.
`pro3d-tire` (Place rover) uses Phosphor's MIT-licensed "tire".

## Where the code lives

| Piece | Location |
|---|---|
| Group colours + which group a tool is in | `Gui.ToolColors` in [ViewerGUI.fs](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs) |
| Strip markup, groups, icons | `Gui.ToolStrip` in [ViewerGUI.fs](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs) |
| Selected-tool label (`interactionName`) + hint row | `Gui.TopMenu.secondaryToolbarRow`, same file |
| Mounted as a render-view overlay | `Gui.Pages.pageRouting`, the `"render"` branch |
| Layout, chips, dividers | `.pro3d-toolstrip` in [semui-overrides.css](../src/PRo3D.Viewer/resources/semui-overrides.css) |

The strip stops mouse events from reaching the render body underneath — that body
starts a camera drag on mousedown and opens the context menu on right click.

Keyboard shortcuts still set the interaction directly (F1 orbit centre, F2 draw
annotation, F3 select annotation, F4 coordinate cross); the strip follows, because it
renders straight off `m.interaction`.
