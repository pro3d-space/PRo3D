# Tool Strip

The tool strip is the vertical icon rail overlaid on the **right edge of the main
render view**. It carries the two selections that used to sit in the top toolbar as
dropdowns: the **navigation mode** and the **interaction** (the "tool").

Both are enum-valued fields on the model (`navigation.navigationMode` and
`interaction`), so "nothing selected" cannot happen: **exactly one navigation icon
and exactly one tool icon is highlighted at all times**. The active one is drawn as a
filled chip in its group colour; idle icons are the same colour, dimmed.

Hovering an icon shows a tooltip on its left. The ctrl-click hint for the selected
tool ("CTRL+click to pick point on surface", …) stays on the top toolbar row, where a
full sentence fits.

## Groups

Groups are separated by a horizontal divider and share an accent colour.

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

## Where the code lives

| Piece | Location |
|---|---|
| Strip markup, groups, icons, colours | `Gui.ToolStrip` in [ViewerGUI.fs](../src/PRo3D.Viewer/Viewer/ViewerGUI.fs) |
| Mounted as a render-view overlay | `Gui.Pages.pageRouting`, the `"render"` branch |
| Layout, chips, dividers | `.pro3d-toolstrip` in [semui-overrides.css](../src/PRo3D.Viewer/resources/semui-overrides.css) |

The strip stops mouse events from reaching the render body underneath — that body
starts a camera drag on mousedown and opens the context menu on right click.

Keyboard shortcuts still set the interaction directly (F1 orbit centre, F2 draw
annotation, F3 select annotation, F4 coordinate cross); the strip follows, because it
renders straight off `m.interaction`.
