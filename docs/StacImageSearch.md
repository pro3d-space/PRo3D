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
   row: the product name (red when its pointing could not be resolved).

   | column | meaning |
   |---|---|
   | Preview | the catalog's overview image |
   | Add | download the image, add it to the library and put it on top of the projection stack |
   | Go to | the arrow flies to the image's camera (once the row is checked: it needs the downloaded metadata) and moves the scene time to the image's; the clock only sets the GIS time to the image's observation time (the catalog's, no download needed), the camera stays |
   | **Instr.** | instrument, from the image's metadata (the catalog's name before the check) |
   | **Distance** | distance to the target's centre, from the metadata — as in the library and the Distance filter |
   | **Date (UTC)** | observation time, to the minute (the full time in the tooltip) |
   | **Res.** | resolution: ground size of one pixel at the target's centre, from the metadata — as in the library and the Resolution filter |

   Instr., Distance, Date and Res. sort the list; a second click sorts the other
   way round, and the header shows the direction. What the check measured at
   the point itself (range to the surface point, pixel size there) is in the
   row's tooltip.

   **Hover a row** to see, in the 3D view, the image's frustum and its footprint outline on the
   surface, the same as for a hovered library image. They come from the
   sidecar the check downloaded, so nothing is fetched; the image itself is
   not projected until the row is clicked. A red row has no frustum.

   When the image's boresight misses the loaded surface (a limb frame, or an
   OPC covering only part of the body), the tooltip's range and pixel size are
   those of the picked point.
5. **Add** (the row's + cell): its files are downloaded to a folder of its own under
   `%TEMP%\PRo3D\stac\`, the image is added to the library and put on top of
   the projection stack. A second click on the same image does not download it
   again. Select it in the Image Library to edit its display settings. Added
   images are **not saved with the scene**: they are not in the library's
   imported folder, so reopening the scene leaves them out of the stack.

Rows in red have no usable pointing. The message says why, e.g. the sidecar
carries no attitude. Such an image would not project either; the status line
counts them apart from the images found.

The **eraser** clears the results and stops a running search. The **bin**
deletes the downloaded files (`%TEMP%\PRo3D\stac`); the next search downloads
what it needs again. Nothing else ever removes them.

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
| **Distance** | `hera:target_distance` (AU), widened by 5 % on each side: the catalog's "observation target" and the metadata's `TRG_POS` need not be the same body (Didymos vs Dimorphos) | yes |
| **Instrument** | not yet: the catalog's `instruments` spellings still need mapping to the metadata's `INSTRUME` names | yes |
| **Phase Angle**, **Resolution** | no: the catalog has neither | yes |

Consequences of filtering after download:

- The catalog lists at most 1000 items per point, newest first, *before* PRo3D's
  own check. Strict phase-angle or resolution filters can leave fewer results
  than the catalog has; narrowing the Date range brings older ones in.
- Each listed item's metadata is downloaded once (cached), also when the filters
  then drop it.
- An item the catalog has no `hera:target_distance` for is not listed while the
  Distance filter is set.

## Sun incidence (disabled)

The sun incidence filter (the angle at the picked point between the sun and the
surface normal) is **disabled** for now: its code is commented out, marked
`SUN INCIDENCE (disabled, see docs/StacImageSearch.md)`, and the model fields
`incidenceMin`/`incidenceMax` are kept, so re-enabling it is uncommenting. The
*inc.* column of the results still shows each image's incidence. What it did:

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
passed the filter: it could not tell. Images dropped by the filter were counted
in the status line.

# What "shows the point" means

The catalog finds images by footprint: a lon/lat polygon per image, about a
dozen vertices. That is coarse, so every hit is checked again in PRo3D with
the image's own pointing, the same projector the projection uses (the
*Orientation Source* and the per-instrument settings under *Instrument Settings*):

- the point must project into the image (4 pixels of slack for the known AFC
  field-of-view error, #801), and
- it must not be hidden: the ray from the camera towards it must not hit the
  surface first. This removes the far side of the body, which otherwise lands
  inside every full-disc image, and terrain that hides the point.

The checks run one after another, never in parallel. Each check casts rays
against the terrain, which loads KdTree patches on first touch. Parallel checks
missed the same cold patch together and each loaded it: one global-MOLA patch
was loaded 64 times, and the viewer's own cursor pick stalled behind them. The
sidecar downloads still run in parallel. A running check shows as *image
search* in the busy indicator.

The checks run on a worker thread, with picks against a snapshot of the scene
taken at the pick. Those picks update the viewer's shared KdTree cache without
a lock: a race there can only lose a cache entry (the patch is loaded again),
not corrupt one.

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
- *MbiAttitudeSpiceRange* (the default) needs `SC_QUAT` only, the range comes
  from SPICE;
- *Spice* needs neither: position and attitude come from the kernels, which
  must cover the epoch.

The test *STAC catalog coverage* says whether a catalog has footprints for
Didymos and Dimorphos, and which part is missing.

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
- Nothing of the search is saved with the scene, including the images added
  from it to the library and the stack. Downloaded files stay in the temp
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
