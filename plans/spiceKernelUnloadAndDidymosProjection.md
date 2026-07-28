# Handover: SPICE kernel unload + Didymos/ASPECT projection investigation

## Where this started

User report: projecting an ASPECT image onto Didymos in the GUI gives
`[SPICE] failed to transform body (body = Didymos, bodyFrame = IAU_DIDYMOS, observer = Didymos, observerFrame = J2000, time = 03/03/2027 03:05:00.`
Switching the reference-frame dropdown to J2000 gives the same error shape again and "I see something" but still wrong.

## What's fixed and committed-ready in pro3d-6 (uncommitted, working tree)

1. **ASPECT support added** to `InstrumentProjection.fs` (instrument name/camera-source maps, `specialTrafos` entry) and `Visualization.fs` (real NIR1 FOV from `hera_milani_aspect_v02.ti`: 6.7°×5.4°).
2. **GUI projection-method toggle**: `ProjectionMethod = Spice | MbiBased` added to `ProjectedImageListModel` (`PRo3D.Core/ProjectedImageList-Model.fs` + regenerated `.g.fs` via `dotnet fsi src/PRo3D.Core/RunAdaptify.fsx` -- never hand-edit `.g.fs`), wired through `ProjectedImageListApp.fs` (new dropdown) and `Visualization.fs`'s `projectDirect`.
3. **Real bug fixed**: `src/PRo3D.Base/SpiceInterfacing.fs`'s `getRotationTrafo` used to return `Some Trafo3d.Identity` instead of `None` on failure (sibling `getRelState` correctly returns `None`). This silently masked every SPICE frame-transform failure as a bogus identity rotation throughout the whole app. **Fixed to return `None`.** This alone may fix a good chunk of the user's real-world "wrongness" complaints -- worth testing in the GUI once everything else here settles.
4. **Real concurrency bug fixed**: `InstrumentProjection.fs`'s `getLookAt`/`getLookAtQuat`/`projectOnto`/`projectOntoQuat` now lock each call as one unit (`spiceCallLock`) -- previously each native call was locked individually, so unrelated threads' SPICE calls could interleave between them and corrupt CSPICE's internal state (reproduced: parallel HSH+ASPECT test runs gave a silently wrong HSH angle, 122.98° vs the correct 104.23°).
5. **Dead-code bug found, not yet fixed**: `InstrumentProjection.getLookAt` (line ~56-65) computes a real attitude-based `CameraView` from the SPICE relative-state rotation, then immediately discards it and returns `CameraView.lookAt` pointed at the frame origin instead. Same pattern independently found in `Visualization.fs`'s `projectDirect`/`project` (compute `t` from measured quaternion, discard it, always return `spice`). Not fixed because the GUI toggle (item 2) now lets the user choose `MbiBased` to get the real quaternion-based orientation instead -- decide whether `getLookAt`'s dead code is worth resurrecting once there's real Didymos data to compare against.
6. **Test files added**: `src/Tests/ProjectedImageMetadataTest.fs`, `InstrumentProjectionComparisonTest.fs`, `DidymosProjectionSpiceTest.fs`, plus real fixture data copied into `src/Tests/data/` (HSH, AFC2, ASPECT mbi sidecars + minimal per-band json/placeholder tif). These call the real production functions (not reimplementations) and include a ray-cast sanity check (unproject the image center pixel through the real trafo, check it hits a round-body sphere) for HSH/Mars, which passes with real data.
7. **`InstrumentMetadata.fs` mbi lookup fixed** (separate earlier bug, already solid): multi-band exports (ASPECT) share one `.mbi.json` across many per-band images with no fixed filename relationship -- lookup is now content-driven (`buildMbiIndex`, matches by declared band list) instead of filename-guessing, which used to make every ASPECT image's date show as `DateTime.MinValue`.

## The kernel problem (why the Didymos investigation stalled)

The ASPECT mbi sidecar itself declares `SPICE_MK: "hera_plan_v180_20250616_001"` -- a **planning** meta-kernel, not `hera_ops.tm` (**operational**, what the app actually loads at startup per `PRo3D.Viewer/Properties/launchSettings.json`'s `--defaultSpiceKernel`). `hera_ops.tm` only carries Milani's cruise-phase kernels; `hera_plan.tm`/`hera_plan_v180_...` additionally loads `hera_milani_mlp_270130_270417_v01.bsp` (Milani's Didymos-proximity ephemeris, which covers the exact fixture epoch).

Tried and **abandoned**:
- **Layering both meta-kernels** (load ops, then load plan on top): breaks things. Two snapshots of the same mission define conflicting CK segments for the same frame -- confirmed empirically, `HERA_HSH` orientation broke when `hera_ops_v172` was loaded on top of the already-loaded `hera_ops.tm` (v182).
- **`DeInit()`+`Init()`+reload to "switch" kernels**: **does not work**. Confirmed by reading `PRo3D-Extensions/CooTransformation/src/CooTransformation.cpp`: `DeInit()` only does `s_logfile.reset()` (closes the log file). `Init()` calls `DeInit()` then only sets up logging/error-handling. **Neither ever calls CSPICE's `kclear_c()`.** Kernels accumulate forever in the process; there is no working unload path via the current native API. (An earlier claim in this session that DeInit+Init "empirically works" was wrong -- a coincidental new conflict from reloading `hera_ops.tm` a third time was misread as a clean reset. Apologies baked into the session; don't repeat this mistake.)
- **Calling `cspice.dll`'s `unload_c` directly** (bypassing `CooTransformation.dll`): doesn't reach the right state. `dumpbin /dependents` on the deployed Windows `CooTransformation.dll` shows **no dependency on `cspice.dll`** -- CSPICE is statically linked into `CooTransformation.dll` on Windows. The separate `cspice.dll` shipped alongside it (also used by `PRo3D.SPICE.Tests`'s `CSpiceDirect` P/Invoke tests) is a **completely separate, independent CSPICE instance** with its own kernel pool. Proven empirically: called `cspice.dll`'s `str2et_c` after `CooTransformation.Init()` had already set SPICE's error action to non-aborting "RETURN" -- if state were shared, the call would fail gracefully; instead it **crashed**, proving `cspice.dll`'s copy still has default (crash-on-error) behavior, i.e. untouched/separate state.

## Second critical bug found (2026-07-07, after writing the first version of this doc): sticky CSPICE error state

`CooTransformation.cpp`'s **`GetPositionTransformationMatrix` (line 538-584) never checks `SpiceHasFailed()` and never calls `reset_c()`** after `pxform_c` -- unlike every other function in the same file (`Xyz2LatLonRad`, `Xyz2LatLonAlt`, `LatLonAlt2Xyz`, `GetRelState`, `Str2Et` all do this correctly). Confirmed via log: `pxform_c("IAU_DIDYMOS", "J2000", ...)` fails internally (no frame data at a future date) and returns silently with `rot` full of zeros (which the F# wrapper now correctly reports as `None`, see item 3 above) -- but CSPICE's *global* error flag is left set. The very next SPICE call, even a trivial `Str2Et` re-parsing an already-valid date string, then fails too (`SPICE(FRAMEDATANOTFOUND)`), and keeps failing until some *other* function that does call `reset_c()` happens to run. This explains most of the "same query sometimes works, sometimes doesn't depending on what ran before it" confusion throughout this whole investigation -- it's not primarily about kernel layering (that's also real, see below), it's this.

**Fix**: add the same `SpiceHasFailed()` / `getmsg_c` / `reset_c()` / `Log(WARNING, ...)` / `return -N` pattern already used in `AddSpiceKernel`/`GetRelState` to `GetPositionTransformationMatrix`, right after the `pxform_c` call. Do this in the same native rebuild pass as the `UnloadSpiceKernel` addition below.

## Update (later in the same session): stopgap that actually works for this one test file

`DidymosProjectionSpiceTest.fs` doesn't need Mars/HSH at all -- it's only about Didymos/ASPECT. So instead of routing through `HeraSpiceTests`'s (broken) kernel-switching machinery, it now just **additively loads `hera_plan_v180_20250616_001.tm` directly** at module-load time (`do` block right after `reportedTime`, before `tests()`), on top of whatever `HeraSpiceTests.tests()`'s eager init already loaded (`hera_ops.tm`). This is layering two *different* kernel families (ops + plan), not two snapshots of the *same* family (ops v172 + ops v182) -- the latter is what was proven to conflict (CK segment clashes for the same frame); the former hasn't shown that failure mode.

Result: **`getRelState MILANI/SUN/DIDYMOS/ECLIPJ2000` now resolves with real data** (proximity-phase position, e.g. `pos = (-1412.3, -10050.4, -1110.0)` km). Test count went from 2/8 to 3/8 passing after flipping that one assertion from `Expect.isNone` to `Expect.isSome`. This is real, working proof that "load the mbi-specified plan kernel" is the correct fix direction -- it isn't blocked on the native unload work to produce *some* correct results, just reliable/clean ones.

Remaining failures in that file, reclassified:
- `getRotationTrafo IAU_DIDYMOS -> J2000`: **genuine failure, not carry-over poisoning** -- it's the first call to `GetPositionTransformationMatrix` in the run, immediately after a clean success on an unrelated function, so there's nothing to have poisoned it. `pxform_c` really can't compute this at 2027-03-03, even though the *identical* query succeeds at 2025-03-12 with the *same* kernel file. Open question, not yet resolved: `hera_didymos_v06.tpc`'s pole constants are keyed to body ID `-658030` (a Hera-mission-specific ID), not the standard Didymos ID (`65803`/`20065803`) -- whether CSPICE's auto-generated `IAU_DIDYMOS` frame (which should be tied to the standard ID) is even reading from that entry, versus some other date-bounded source, is unconfirmed. Investigate once the C++ tooling pass is happening anyway (e.g. add a temporary `Log` of `bodc2n_c`/`bods2c_c` results, or just try a few more dates to see if there's a coverage boundary).
- `getRotationTrafo J2000 -> J2000`, both `transformBody` variants, `getRotationTrafo ECLIPJ2000 -> J2000`: these run *after* the `IAU_DIDYMOS -> J2000` failure in test order, so they're plausibly poisoned by it (sticky error state, see above) rather than independently broken. Won't know for sure which are genuine vs. poisoned until the `reset_c()` fix lands.

## Why "just don't let anything fail" doesn't avoid needing the reset_c() fix

Asked directly: wouldn't a clean run (nothing ever fails) sidestep the sticky-error-state bug entirely, making the C++ fix unnecessary? No:

1. **The failure is often the thing being observed.** "Does this instrument/time/frame have SPICE coverage" is a query that's *supposed* to be able to fail -- that's why it returns `Option`/`None` everywhere in this codebase. An app that has to avoid ever triggering a real failure to stay usable is broken by construction; you can't know which of a user's images have coverage without querying.
2. **The bug isn't "a failure happened," it's that one function forgot the pattern its siblings already use.** `GetPositionTransformationMatrix` is the only function in `CooTransformation.cpp` that skips `SpiceHasFailed()` + `reset_c()` -- `AddSpiceKernel`, `GetRelState`, `Xyz2LatLonAlt`, `LatLonAlt2Xyz`, `Str2Et` all do this correctly already. It's a copy-paste omission, not a deeper architectural necessity.
3. Once fixed, a failing query stays local (returns `None`, like `getRelState` already does today) instead of contaminating whatever unrelated SPICE call happens to run next.

## What actually needs to happen (per-file)

The fix has to go **inside** `CooTransformation.cpp`'s statically-linked CSPICE instance, since that's the one the whole app actually uses.

1. **`C:\Users\haral\Desktop\pro3d\PRo3D-Extensions\CooTransformation\include\CooTransformation\CooTransformation.hpp`**: add a new exported function, e.g.
   ```cpp
   /**
    * @brief Unload a previously loaded SPICE kernel (or meta-kernel -- unloads
    * everything that meta-kernel caused to be loaded, per CSPICE's unload_c semantics).
    * @param[in] pcSpiceKernelFile Path to the kernel file, exactly as passed to AddSpiceKernel.
    * @return 0 success, -1 nullptr argument.
    */
   JR_PRO3D_EXTENSIONS_COOTRANSFORMATION_EXPORT
   int UnloadSpiceKernel(const char *pcSpiceKernelFile);
   ```
2. **`...\CooTransformation\src\CooTransformation.cpp`**: implement it, mirroring `AddSpiceKernel`'s error-handling shape:
   ```cpp
   JR_PRO3D_EXTENSIONS_COOTRANSFORMATION_EXPORT
   int UnloadSpiceKernel(const char* pcKernelPath)
   {
       if (!pcKernelPath) { Log(LogLevel::ERROR, "UnloadSpiceKernel() called with nullptr arguments."); return -1; }
       std::string sPath = pcKernelPath;
       Log(LogLevel::TRACE, "UnloadSpiceKernel() called with spice kernel path = \"" + sPath + "\".");
       unload_c(sPath.c_str());
       if (SpiceHasFailed()) { /* getmsg_c + reset_c + Log, same pattern as AddSpiceKernel */ return -2; }
       Log(LogLevel::TRACE, "UnloadSpiceKernel() finished.");
       return 0;
   }
   ```
   (`unload_c` is a standard CSPICE C API function -- confirmed exported from `cspice.lib`/`cspice.dll`, just needs to be called from inside the already-statically-linked copy here, not from the outside.)
3. **Build**: `cmake` and MSVC (VS2022) are already installed on this machine. Missing piece: NAIF's SPICE C toolkit needs to be downloaded and extracted into `PRo3D-Extensions/cspice/` (headers in `cspice/include/`, `cspice.lib` in `cspice/lib/`) before `cmake --preset windows-configure` + `cmake --build --preset windows-build` will work (see `PRo3D-Extensions/README.md`). Typical NAIF URL shape: `https://naif.jpl.nasa.gov/pub/naif/toolkit/C/PC_Windows_VisualC_64bit/packages/cspice.zip` (unverified in this session -- confirm the exact current URL/toolkit variant needed, e.g. 64-bit MSVC, before downloading).
4. **Redeploy into `PRo3D.SPICE-2`**: replace `PRo3D.SPICE-2/lib/Native/PRo3D.SPICE/windows/AMD64/CooTransformation.dll` with the freshly built one. Add the P/Invoke binding in `PRo3D.SPICE-2/src/PRo3D.SPICE/CooTransformation.fs` (mirror `AddSpiceKernel`'s `[<DllImport>]` shape) and the `Option`-returning wrapper in `CooTransformation.FSharp.fs` if one exists there (check the file -- not yet inspected in this session).
5. **Version bump + publish**: `PRo3D.SPICE-2` publishes via `.github/workflows/publish.yml` on push/tag (not inspected in detail -- check what triggers it). **This publishes to the public nuget.org feed** (pro3d-6's `paket.dependencies` line 70: `nuget PRo3D.SPICE ~> 1.0.6`, source `https://api.nuget.org/v3/index.json`) -- confirm with the user before actually pushing/tagging, since that's a hard-to-reverse, publicly-visible action, distinct from ordinary local commits.
6. **Pull into pro3d-6**: bump the `paket.dependencies` version constraint, `paket update PRo3D.SPICE` (or equivalent), then replace this session's `HeraSpiceTests.fs` `ensureKernelAt`/`activeKernel`/DeInit-based scaffolding (**currently broken, do not ship as-is** -- it relies on DeInit+Init actually clearing state, which it doesn't) with real `UnloadSpiceKernel` calls: unload whatever kernel was previously active before loading the one a given mbi fixture needs.

## Also worth doing once unload exists

- `src/Tests/HeraSpiceTests.fs` currently has `ensureKernelAt`/`activeKernel`/`ensureOpsKernel`/`loadKernelForMbiContent` scaffolding (added earlier this session) that assumes `DeInit()`+`Init()` clears the kernel pool. **It does not, and this code is misleading/broken as it stands** -- it doesn't crash, but the "switching" it appears to do is illusory (kernels just keep accumulating underneath it). Replace it with real `UnloadSpiceKernel` calls once that exists: unload whatever kernel was previously active before loading the one a given mbi fixture needs. Until then, prefer the simpler pattern used in `DidymosProjectionSpiceTest.fs` (additively load the one extra kernel a file's tests need, accept that it's not cleanly isolated) for any *new* test file, rather than extending the broken `ensureKernelAt` machinery further.
- Re-run `DidymosProjectionSpiceTest.fs` and `InstrumentProjectionComparisonTest.fs`'s ASPECT cases for real with `hera_plan` properly isolated via unload (not layered) -- expect real Didymos/Milani data to resolve cleanly (no sticky-state uncertainty), replace the remaining `Expect.isNone`-documents-a-gap assertions with real angle/ray-hit comparisons (mirroring the HSH ones).
- Add the ray-cast center-pixel-hits-the-body test for ASPECT/Didymos once data resolves reliably (radius ~409.5m, from `CooTransformation`'s own `LatLonAlt2Xyz("didymos", 0,0,0)`; body sits at the observer-relative frame's origin by construction, same as the working HSH/Mars version).
- Add the **reverse** check the user asked for and wasn't yet built: project the body's known center-point (barycenter, `V3d.Zero` in the observer-relative convention) forward through the trafo and check it lands near the image's center pixel (NDC ≈ (0,0)) -- complements the existing "unproject center ray -> hits body" test.
- Investigate the `IAU_DIDYMOS -> J2000` genuine-failure-at-2027-but-not-2025 question (see above) -- likely needs looking at what's really backing the `IAU_DIDYMOS` frame (body ID mismatch vs. a date-bounded binary PCK).
- Decide whether `getLookAt`'s dead code (item 5 above) should be resurrected now that the GUI can pick `MbiBased` explicitly, or left alone.
