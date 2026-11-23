# Image Search (STAC)

Find the instrument images that show a point of the surface, and project one
with a click. The images come from a STAC (SpatioTemporal Asset Catalog)
server, e.g. a HERA mission catalog. No server is built in: set the
`PRO3D_STAC_URL` environment variable to the catalog's API root (the URL
`/search` is appended to), or type it into the *Catalog* field. The tool is
the **magnifier** (*Image Search*) in the toolbar; settings and results are in
the GIS tab under *Projected Images → Image Search (STAC)*.

# Workflow

1. Set the scene up as for [Multi-Image Projection](MultiImageProjection.md):
   an observed body, the surface bound to its SPICE body and frame (e.g.
   `MARS` / `IAU_MARS`), and a SPICE kernel covering the observation epochs.
2. Set the filters in the panel's **Filters** section (see below). They are the
   Image Library's: one set for both.
3. Select the **Image Search** tool in the toolbar. This connects to the
   catalog, and asks you to sign in if it wants that (see *Signing in*). Then either
   - **click** the surface: that point is searched, or
   - hold **Ctrl** and **move** over the surface: each point the cursor rests on
     for a quarter second is searched, continuously. Moving on abandons the
     check in progress.

   Only while this tool is active does the cursor search; with any other tool,
   Ctrl + move does what it always does.
4. The status line is short (e.g. *checking 12/240 …*, *8 images · 2 red*);
   hover it for the whole story: the point (planetocentric, east-positive), the
   counts, why images could not be checked. Each image that shows the point
   appears in the list as soon as it is checked; once all are checked, the
   list is sorted by pixel size at the point (finest first) and cut to the best
   **100**. The results are in the section's **Images** fold, whose header says
   *showing x of y* (rows shown of the items the catalog matched).
   Up to 1000 catalog items are checked per point, newest first (the log says
   when the catalog matched more; narrow the time range then). Moving the cursor
   back and forth on the spot does not restart a running search.

   The list has the Image Library's columns, so both read the same. Above each
   row is the product name (red when its pointing could not be resolved). Click it
   to **select** the image: it is downloaded if needed and shown under *Selected
   Image*, where its display settings can be changed as for a library image. It is
   **not added to the Image Library**, nor to the stack. The selected row is
   highlighted blue, as in the library; the same image's row in the Projection
   Stack is highlighted too, and selecting it there highlights this row. A second
   click deselects it.

   | column | meaning |
   |---|---|
   | Preview | the catalog's overview image |
   | Add | **+**: download the image, select it and put it on top of the projection stack (not into the Image Library). Once the image is on the stack, the cell shows the stack icon, as in the library; click it to take the image off the stack |
   | Go to | the arrow flies to the image's camera (once the row is checked: it needs the downloaded metadata) and moves the scene time to the image's; the clock only sets the GIS time to the image's observation time (the catalog's, no download needed), the camera stays |
   | **Instr.** | instrument, from the image's metadata (the catalog's name before the check) |
   | **Distance** | distance to the target's centre, from the metadata (`TRG_POS`); for a sidecar without it, the spacecraft–target range from SPICE at the observation time, to its `TARGET` or, when SPICE cannot place that (e.g. a barycenter the kernels lack), to the surface's body, as the projection does (the loaded kernel must cover it). The library computes it the same way, except that it has no surface body to fall back to, so there such an image shows *no TRG_POS*. The Distance filter compares this value |
   | **Date (UTC)** | observation time, to the minute (the full time in the tooltip) |
   | **Res.** | resolution: ground size of one pixel at the target's centre, from the metadata — as in the library and the Resolution filter |

   Instr., Distance, Date and Res. sort the list; a second click sorts the other
   way round, and the header shows the direction. What the check measured at
   the point itself (range to the surface point, pixel size and sun incidence
   there) is in the
   row's tooltip.

   **Hover a row** to see, in the 3D view, the image's frustum and its footprint
   outline on the surface, the same as for a hovered library image. They come
   from the sidecar the check downloaded, so they appear at once. Rest on the row
   for 0.3 s and the image itself is downloaded (a spinner in the + cell) and
   **projected as a preview**, on top of the stack, like a hovered library
   image. The preview is not added to the library or the stack, and it goes
   when the mouse leaves the row. Sweeping quickly over the list downloads
   nothing. An image downloaded once is previewed at once the next time. A red
   row has no frustum and no preview. *Projection Settings → Projection* decides
   whether the preview shows over the stack or alone
   ([MultiImageProjection.md](MultiImageProjection.md), *Projection Settings*).

5. **Add** (the row's + cell): its files are downloaded to a folder of its own under
   `%TEMP%\PRo3D\stac\`, the image is selected and put on top of the
   projection stack. It is **not added to the Image Library**: the library holds
   the imported folder only. An image already downloaded (previewed or selected)
   is not downloaded again. A downloaded image stays available while its result
   is listed, or while it is on the stack or selected: clearing the results,
   disconnecting or a new search keeps the images on the stack and the selected
   one. Added images are **not saved with the scene**: they are not in the
   library's imported folder, so reopening the scene leaves them out of the
   stack.

Rows in red have no usable pointing. The message says why, e.g. the sidecar
carries no attitude. Such an image would not project either; the status line
counts them apart from the images found.

The **eraser** clears the results and their previews, and stops a running
search; images on the stack or selected stay. The **bin** deletes the
downloaded files (`%TEMP%\PRo3D\stac`) and removes the images downloaded from
the catalog from the stack and the selection. The next search downloads what it needs again. Nothing else
ever removes the files.

Downloads: one file is downloaded once, however many searches and clicks ask
for it at the same time; a download that does not complete within five minutes
fails (a red row, or counted as not checkable). A search downloads the sidecars
of the next 16 images while it checks the current one; abandoning it leaves at
most those behind, finishing into the cache.

# Signing in

The catalog is **off** when PRo3D starts: nothing is sent to it until you
connect, with *Connect* in the panel's *Connection* row or by selecting the
Image Search tool. Connecting asks the catalog for one item:

- it answers: connected, the row shows *connected* (as whom, after a sign-in);
- it wants a sign-in (HTTP 401): the **sign-in dialog** opens over the whole
  window, with the catalog's host. *Sign in* (or Enter) tries the user name
  and password; a wrong one keeps the dialog open and says so. *Cancel* (or
  Escape) leaves the catalog off;
- anything else (unreachable, 403 for this user, not a STAC catalog): the row
  shows *failed*, the reason in its tooltip, and the catalog stays off.
  *Retry* tries again.

*Disconnect* stops searching, forgets the sign-in and clears the results (their
thumbnails are no longer served). Changing the *Catalog* URL disconnects too:
the sign-in was for the old one. If the catalog refuses
the session later (the password changed, the account was closed), the dialog
opens again.

The sign-in is HTTP Basic, as an nginx in front of a catalog asks for it. What
PRo3D does with it:

- the password is kept **in memory, for this session only**. Not in the scene,
  not in the log, not on disk. The dialog's field is cleared in the same step
  that reads it on *Sign in*, so it does not linger in the page (developer
  tools);
- PRo3D remembers the last catalog URL that connected and the last user name
  that signed in (`%APPDATA%\Pro3D\stac.json`): the *Catalog* field and the
  dialog start with them next time. Never a password. `PRO3D_STAC_URL`, when
  set, still wins for the URL;
- it is sent only to the catalog's own host (scheme, host and port): an asset
  link to another host, or a redirect, never gets it;
- only over **https** (or to this computer, for a local test catalog): over
  plain http the dialog refuses, since Basic auth would travel unencrypted. A
  catalog URL with a user name or password in it is refused as well: PRo3D
  asks for them;
- in code it is a `Secret`, which prints as `***`; `Secret.reveal` is the one
  way to read it, so every place the plain text exists is found by searching
  for that;
- the thumbnails come through PRo3D's own web server
  (`/stac-overview/...`), which downloads them with the sign-in: the GUI's
  browser has none. Only listed results' thumbnails are served.

# Filters

The search uses the panel's shared **Filters** section — Phase Angle,
Instrument, Resolution, Distance, Date — the same filters as the Image Library
([MultiImageProjection.md](MultiImageProjection.md#filters)). Changing a filter
searches the last point again.

Every result is checked on its **downloaded metadata** by the library's own rules
(`ImageFilter.passesValues`), before its pointing is checked: a catalog image
passes exactly when it would pass in the library. Dropped images are counted in
the status line (*filtered*). Some filters are also sent to the server, only to
make the answer smaller:

| filter | sent to the server as | checked by PRo3D |
|---|---|---|
| **Date** | `datetime` interval (UTC), always: the selected dates, by default the whole range 2026-11-01 to today, so the footprint-less fallback checks that window and not the catalog's newest 1000 of any time | yes, while the filter is set |
| **Distance** | no: a catalog whose items lack `hera:target_distance` would then list nothing at all, whatever the range. PRo3D computes the distance itself (`TRG_POS`, else SPICE; see the Distance column above) | yes |
| **Instrument** | not yet: the catalog's `instruments` spellings still need mapping to the metadata's `INSTRUME` names | yes |
| **Phase Angle**, **Resolution** | no: the catalog has neither | yes |

Consequences of filtering after download:

- The catalog lists at most 1000 items per point, newest first, *before* PRo3D's
  own check. Strict phase-angle, resolution or distance filters can leave fewer
  results than the catalog has; narrowing the Date range brings older ones in.
- Each listed item's metadata is downloaded once (cached), also when the filters
  then drop it. With a Distance filter that is every listed item (the catalog no
  longer narrows by distance), plus a SPICE call for each sidecar without `TRG_POS`.
- An item whose metadata could not be downloaded cannot be filtered: it is listed
  red (with the reason) whatever the Phase Angle, Resolution or Distance filter
  says, rather than silently hidden.

## Sun incidence

**Sun incidence** (in the Image Search section, below the catalog) keeps the
images whose sun incidence at the picked point lies in the range, both ends
included; the default 0°–90° keeps the images taken in daylight. The two ends
stay ordered: raising the lower end above the upper one moves the upper end
along, and vice versa. The incidence is
the angle at the picked point between the sun and the surface normal. Changing the
range searches the last point again. Each result's incidence is in its tooltip,
next to the range and pixel size; the list has no column for it, because it shares
its columns with the Image Library.

Why incidence, and not sun elevation and azimuth: incidence is what planetary
image searches filter on (PDS ODE, for instance, together with emission and
phase angle). It is the sun's elevation above the *local* surface
(incidence = 90° − elevation), so it already says how the point is lit, while
azimuth only says from which side. As a guide:

- **0°–40°**: little shadow; for albedo and colour.
- **55°–80°**: long shadows; for relief, boulders and crater rims.
- Matching the azimuth matters when two images are to be compared (change
  detection, photoclinometry); that is not a filter yet.

The incidence is measured against the loaded surface's normal at the point
(the OPC triangle, in the body-fixed frame), with the sun position from the
image's sidecar. On rough terrain that is the local facet, not a smooth body.
An image without a sun position in its sidecar, or a point without a normal,
passes the filter: it cannot tell. Images dropped by the filter are counted in
the status line (*off incidence*).

No ray is cast for it: the normal is the one the pick already found, and only
the sun direction differs per image. The incidence is therefore the one *at the
picked point*, not at the image's centre or averaged over the image (what
earlier versions measured with a boresight ray). Whether the point lies in a
shadow cast by other terrain is not evaluated either.

# What "shows the point" means

The catalog finds images by footprint: a lon/lat polygon per image, about a
dozen vertices. That is coarse, so every hit is checked again in PRo3D with
the image's own pointing, the same projector the projection uses (the
*Orientation Source* and the per-instrument settings under *Instrument Settings*):

- the point must project into the image (4 pixels of slack for the known AFC
  field-of-view error, #801), and
- the camera must be above the point's horizon: on the outer side of the plane
  through the point perpendicular to the direction from the body's centre. This
  removes the far side of the body, which otherwise lands inside every
  full-disc image. The picked triangle's normal is not used for this: on rough
  terrain a facet tilted away from an oblique camera would drop an image that
  does show the point.

No rays are cast: range and pixel size are measured from the camera to the
picked point, the incidence from the picked normal and the sidecar's sun
position (see [Sun incidence](#sun-incidence)). Terrain that hides a point above the horizon (a crater
rim, a boulder at a grazing view) is not detected. Earlier versions cast an
occlusion ray and a boresight ray per image; on a large OPC those rays loaded
KdTree patches all over the body, more than the KdTree memory budget holds, so
the search spent its time dropping and reloading patches.

The checks run one after another on a worker thread; the sidecar downloads run
in parallel. A running check shows as *image search* in the busy indicator.

# Images without footprint

The search first asks for items whose footprint (`geometry`) contains the
point and whose `ssys:targets` include the body. A catalog may not have
computed footprints yet, e.g. for a newly visited body. When no footprint
contains the point, the search falls back to the items **without any
footprint** in the time range that name the body or no target at all, newest
first, at most 1000. These are checked by their pointing alone; the status line
says *by pointing*. The first search downloads their sidecars (cached
afterwards), so a narrow time range makes it much faster. Images whose pointing
does not resolve are not listed in red here, only counted in the status line
with an example reason: nothing says they show the point.

Every check needs SPICE: with no kernel loaded, the search does not start and the
status line says *no SPICE kernel*. Images whose time the loaded kernel does not
cover are counted as *no SPICE coverage*, apart from the other uncheckable ones;
a kernel that covers them makes them checkable.

The pointing check needs an *Orientation Source* that works for these sidecars:

- *MbiBased* needs the sidecar's `SC_QUAT` and `TRG_POS`;
- *MbiAttitudeSpiceRange* needs `SC_QUAT` only, the range comes
  from SPICE;
- *Spice* (the default) and *SpiceAimed* need neither: position and attitude come from the
  kernels, which must cover the epoch. *Spice* also needs the frames kernel and is
  not aimed, so the image can miss the target; *SpiceAimed* always lands on it.

The test *STAC catalog coverage* says whether a catalog has footprints for
Didymos and Dimorphos, and which part is missing.

# Images of other targets

An image the catalog lists for another body can still show this one, e.g. a
frame aimed at Didymos with Dimorphos next to it, or a model of a body that was
never a target itself. **Other targets** (checkbox in the Image Search section,
on by default) checks these images too. The footprint search cannot find
them: a footprint is a lon/lat polygon on the image's *own* target, so this
body's lon/lat says nothing about it.

With the checkbox on, the search also asks for the items of the time range
regardless of point and target (one more query; when no footprint contains the
point, it is the same query as the fallback above, asked once). Those naming
other bodies only, and those naming none but having a footprint (on an unknown
body), are checked **by their pointing alone**, after the footprint hits and
the footprint-less ones:

- as for images without footprint, at most the newest 1000 of the time range,
  sidecars downloaded first (cached afterwards): a narrow Date range is much
  faster. The 1000 are of *all* items of the range, this body's own included, so
  in a range full of this body's images few others may be left; the log warns
  when the range holds more;
- the check places the camera relative to this body through SPICE. The loaded
  kernel must place the image's target (its sidecar `TARGET`) relative to this
  body at the image's time; an image it cannot place is counted as *no SPICE
  coverage*, not listed. (Images listed for this body are taken to be of it
  when SPICE cannot place their target, e.g. a barycentre the kernels lack;
  for another body that would put the camera in the wrong place.) A sidecar
  without `TARGET` cannot be placed either: it is counted as *uncheckable*;
- unresolved ones are counted (*uncheckable*), not listed in red;
- the Distance and Res. columns (and the Distance, Resolution and Phase Angle
  filters) stay the metadata's, i.e. measured to the image's *own* target, as in
  the library; the range and pixel size *at the picked point* are in the tooltip.
  For two bodies as close as Didymos and Dimorphos the difference is small, for
  a planet and its moon it is not;
- a result shows the catalog's target after its name (grey) and in its tooltip;
  the status line's tooltip counts them apart (*… and N image(s) of other
  targets by pointing*).

Off, the search is what it was before: footprint hits for this body, or the
footprint-less fallback.

# Coordinates

The footprints are **planetocentric, east-positive** lon/lat in the body-fixed
frame. This was checked against the spacecraft positions of the images: read
east-positive, every footprint vertex lies on the hemisphere the spacecraft
saw; read west-positive, many do not. PRo3D converts the
picked point the same way (`atan2`/`asin` of its body-fixed position). It does
not go through `CooTransformation.tryGetLatLonAlt`, which is planetographic for
Mars and therefore west-positive with the default kernels.

# Notes

- The client only calls `<server>/search` and the asset links. Use an https
  URL that answers directly; a redirect to plain http is refused.
- Nothing of the search is saved with the scene, including the images put on
  the stack from it. Downloaded images never enter the Image Library. Downloaded files stay in the temp
  folder until it is cleaned.

# Testing

No real catalog is needed. `src/Tests/FakeStacCatalog.fs` is a small STAC
catalog of its own: it serves every `<name>.mbi.json` with a `<name>.png` in a
folder as an item, answers `/search` (time range, paging) and the file
downloads, optionally behind HTTP Basic auth, and logs whether each request
carried credentials. Run it on its own to try the feature:

    dotnet bin/Debug/net9.0/Tests.dll --fake-stac <PRo3D.Resources.TestData>/HERA/Dimorphos_opc/AFC_2027-03-21 --user tester --password not-a-secret

It prints its API root (`http://127.0.0.1:<port>/api`), to put into the
*Catalog* field or `PRO3D_STAC_URL`, and serves until its input closes. The
test data's simulated AFC-1 frames of Dimorphos have no footprints, so PRo3D
finds them by pointing; open `ProjectionTest.pro3d` from the same folder.
`--footprints` gives every item a world-wide footprint instead.

- Expecto, `src/Tests/StacSearchTests.fs`: the sign-in cases against the fake
  catalog (always run); the live cases need the real catalog
  (`PRO3D_STAC_URL`; for one that asks to sign in also `PRO3D_STAC_USER` and
  `PRO3D_STAC_PASSWORD`, else they skip and say so), the Mars case also the
  HERA kernels (`PRO3D_SPICE_KERNELS`), and skip without them.
- Playwright, `tests-ui/tests/stac-signin.spec.ts`: the fake catalog with a
  password and the test data's scene, from the dialog to the projection, and
  that nothing went to the catalog unsigned after the sign-in. Needs
  `PRO3D_TEST_DATA`, `PRO3D_SPICE_KERNELS`, and `PRO3D_EXE` in a folder that
  also has `Tests.dll` (a Debug build).
- Playwright, `tests-ui/tests/stac-search.spec.ts`: against a real catalog
  (`PRO3D_STAC_URL`, a scene in `PRO3D_STAC_SCENE`, and `PRO3D_STAC_USER` /
  `PRO3D_STAC_PASSWORD` if it asks to sign in).

The UI specs set `PRO3D_STAC_REMEMBERED` to a file of their own, so they do not
overwrite your remembered catalog in `%APPDATA%Pro3Dstac.json`.

Implementation: `src/PRo3D.GIS/StacSearch.fs` (client, sign-in, geometry,
filters, worker), `ProjectedImageListApp.fs` (panel, sign-in dialog),
`Viewer.fs` (`searchStacAt`, called from the preview pick and from a click
with the `PickStacPoint` tool; the connection), `ViewerGUI.fs` (the toolbar
button, the dialog on the main page), `Program.fs` (the thumbnail route).
