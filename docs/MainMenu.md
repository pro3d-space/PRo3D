# Main Menu

The main menu sits behind the sidebar icon in the top-left corner of the window. Hovering it
opens the entries *Surfaces*, *Scene*, *Annotations*, *Layout* (see [WindowLayouts.md](WindowLayouts.md)),
*Scene Objects*, *Extras* and *Preferences*; hovering an entry opens its list to the right.

## Long lists

A list never grows past the bottom of the window. When it opens, it is limited to the space
between where it starts and the bottom edge; a longer list (for example *Extras* in a small
window, or *Layout > Reopen Panel* with many closed panels) scrolls, and shows a scrollbar on its
right edge so you can see there is more below (also on macOS, where scrollbars are otherwise
hidden until you scroll).

Lists that themselves hold further submenus or popups (the top-level menu, *Scene* with its
*Recent* popup, *Layout* with *Reopen Panel*) are not limited, because a scrolling list would
cut off the submenus that open out of it. These lists are short.

Implementation: `TopMenu.fitMenusToWindow` in `src/PRo3D.Viewer/Viewer/ViewerGUI.fs`, a single
hover handler on the main-menu button that tags the capped lists with `pro3d-menu-scroll`;
the scrollbar styling is in `src/PRo3D.Viewer/resources/semui-overrides.css`.
