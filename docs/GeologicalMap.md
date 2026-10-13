# Geological maps

A geological map in PRo3D is built the way geologists build one in GIS with the *contacts and
points* method (QGIS plugin Mappy): you draw the **contacts** - the lines between units - and
place a **label point** in each unit saying which unit it is. The units themselves, polygons
with holes, are **derived**: PRo3D computes them from the contacts and shows them as coloured
areas on the terrain. Refining a map means drawing another contact; every shared boundary is
drawn once, and changing a contact updates the units on both its sides.

A map can cover a whole small body such as Dimorphos - there is no map projection involved, no
pole or date-line special case.

![Units of the showcase map on Dimorphos, northern view](images/geological-map/units-north.png)

*The showcase map: contacts (black lines) cut Dimorphos into units, coloured by the legend.
The grey area is a unit without a label point yet.*

Status: first version (#843). What is not there yet is listed [below](#not-yet).

## Concepts

| | what it is | how you make it |
|---|---|---|
| **map** | a set of units over one annotation group | *Geological Maps* panel → *New map from group* |
| **contact** | a line between units | any **polyline**, **polygon** or **line** annotation in the map's group |
| **label point** | names the unit it lies in | a **point** annotation in the map's group with a *Geo Unit* |
| **unit** | an area bounded by contacts | derived - never edited directly |
| **legend** | the units' codes, names and colours | the map's legend in the panel |

The map is **immutable**: you cannot edit, split or merge a unit itself. Every change goes
through the contacts and the label points, and the units follow.

## Making a map

1. Create an annotation group for the map (Annotations panel) and make it the active group.
2. Open the **Geological Maps** panel (main menu → *Layout* → *Reopen Panel*) and press **New map from group '…'**.
   The map is created over the active group and becomes the **active map**.
3. Draw contacts into the group with the ordinary annotation tools (polyline, Sky
   projection on small bodies). A closed region needs closed contacts: let contacts
   **overshoot** the contact they end on - the overhanging piece is trimmed automatically.
4. Add units to the legend with **New unit** and give each a code (`Cr`), a name (*Crater
   floor*) and a colour.
5. Label the units: make a legend entry **active** (the circle in its row), then draw a
   **point** annotation in a unit - while its map is active, a new point in the map's group
   takes the active unit. Alternatively select point annotations and press **Assign to selected
   points**, or type the code into *Geo Unit* in the annotation properties.

![Before](images/geological-map/assign-before.png) ![After](images/geological-map/assign-after.png)

*A label point with the active unit `Cr` placed in the grey, unlabelled unit: the unit takes the
crater colour.*

Making a map active also makes its group the active annotation group, so whatever you draw next
lands in the map.

## The panel

![The Geological Maps panel](images/geological-map/panel.png)

Per map:

- **Map** - the active map (green dot) and its name.
- **Group** - the annotation group it is built from.
- **Visible**, **Opacity** - the units are drawn semi-transparent (default 50 %), so the
  terrain stays readable underneath.
- **Status** - what the map currently consists of, and what needs attention:
  - *without a label* - units with no label point: drawn grey;
  - *conflicting labels* - a unit holding label points of different units, usually a missing
    contact between them: drawn magenta;
  - *open contact ends* - ends of contacts that reach no other contact. Their dangling pieces
    do not bound anything; an overshoot you drew on purpose shows up here too;
  - *topology check failed* - the contacts could not be turned into units consistently.
- **Legend** - colour, code, name, the active unit, remove. Renaming a code renames it on the
  map's label points too, so units keep their colour.
- **Remove map** removes the map only; its contacts and label points stay as annotations.

A scene can have several maps (two interpretations of the same area, units vs. morphology);
one is active at a time.

## How the units are drawn

Units are drawn like [filled annotations](AnnotationFill.md): every terrain pixel inside a unit
is tinted, at every level of detail and on its full relief - a boulder or a crater wall inside
a unit is coloured completely. The boundary between two units is exactly where their contact
runs, seen from the body's centre. The [fill reach](AnnotationFill.md#fill-reach) bounds how far
above and below the contacts the colouring reaches.

![Units of the showcase map on Dimorphos, southern view](images/geological-map/units-south.png)

*The southern side: a meridian contact splits the south into two units; a small closed contact
is a boulder field inside one of them (a unit with a hole).*

Unlike outlines, the units are only drawn in views that render their surfaces offscreen (main
and instrument view); [Snapshots](../ai/AUTOMATION.md) do not show them yet.

## Where it is stored

Maps are saved with the annotations, in the scene's `.pro3d.ann` file, under new keys
(`geologicalMaps`, and `geoUnit` on label points). The derived units are not stored - they are
recomputed on load and on every change. Older PRo3D versions open such scenes and ignore the
maps; contacts and label points stay ordinary annotations there.

## Limits

- **Bodies**: units are computed and drawn as seen from the body's centre, which is exact on
  a sphere and correct on any body where each ray from the centre meets the surface once
  (Dimorphos, Didymos, Mars). Bodies with overhanging shapes (67P) are not supported.
- Map scenes need the scene body set (annotations in body-fixed coordinates).

## Not yet

These are planned (see `docs/dev/GeologicalMap-plan.md`):

- an *Assign unit* tool: click or drag across units to assign, without placing points by hand;
- a *Draw contact* tool with end snapping and a preview of the split;
- hatching for unlabelled units, unit codes written on the terrain;
- jumping to problems from the panel;
- export to GeoPackage.

## Showcase

The scene in the screenshots is generated from the Dimorphos test OPC:

```
set PRO3D_TEST_DATA=<PRo3D.Resources.TestData checkout>
dotnet run --project src/Tests -c Release -- --make-geomap-showcase
```

writes `cases/geologicalMap/dimorphos-geological-map.pro3d` (+ `.pro3d.ann`) into the test
data; `tests-ui/tests/geological-map.spec.ts` opens it and drives the panel.
