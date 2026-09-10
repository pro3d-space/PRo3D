# SPICE kernels for the test suite

Part of the suite drives real SPICE geometry — HERA spacecraft states, instrument frames,
the Didymos-system frames the mbi fixtures in `src/Tests/data` were generated against.
That needs ESA's HERA mission kernels: public, but too large to commit.

| | |
|---|---|
| Source | `https://spiftp.esac.esa.int/data/SPICE/HERA/kernels/` (public, no credentials) |
| Full dataset | ~11 GB — do **not** mirror it |
| What the suite needs | 119 files, 1.23 GiB — the closure of four meta-kernels |
| Fetched by | [`scripts/fetch-spice-kernels.sh`](../../scripts/fetch-spice-kernels.sh) |
| Pinned in | [`scripts/spice-kernels.pins`](../../scripts/spice-kernels.pins) |
| Found via | `$PRO3D_SPICE_KERNELS`, read by `HeraSpiceTests.kernelsDir` |

## Getting them

```bash
scripts/fetch-spice-kernels.sh spice        # or scripts\fetch-spice-kernels.cmd
PRO3D_SPICE_KERNELS=$PWD/spice ./runAllTests.sh
```

`PRO3D_SPICE_KERNELS` is the variable [`pro3d-tool`](../Pro3DTool.md) reads, and equally
tolerant: the dataset root or `kernels/` itself. Without it the tests fall back to a
`spice` directory next to the PRo3D clone, so an existing full mirror keeps working.

With kernels at neither place the kernel-backed tests skip. `runTests.sh` / `.cmd` pass
`--skip-hera` to force that even where they are present; `runAllTests.sh` / `.cmd` run
everything.

## Fetching, not cloning

ESA publishes the dataset as a git repo too (`https://spiftp.esac.esa.int/git/hera.git`),
whose working tree is ~11 GB: every version of every CK, SPK and DSK ever released. The
tests need one percent of it, and a meta-kernel's `KERNELS_TO_LOAD` already names exactly
which files SPICE will open. So the script reads the pinned meta-kernels and fetches
their closure over HTTPS — resumable (the server honours range requests), 8 at a time,
about 90 s on a CI runner.

## The pins

The tests open `hera_ops.tm` and `hera_plan.tm`. On the server those unversioned names
are copies of whatever release is newest, and ESA ships a new SKD every few weeks, moving
the superseded file from `mk/` into `mk/former_versions/`. Depending on them directly
means testing against kernels nobody chose, changing on ESA's schedule.

So the pins name versioned files, and the script writes the aliased ones into `mk/` under
the plain names:

```
hera_ops_v182_20260527_001.tm -> hera_ops.tm
hera_plan_v182_20260527_001.tm -> hera_plan.tm
hera_ops_v172_20250312_001.tm
hera_plan_v180_20250616_001.tm
```

The last two are not a choice. An `.mbi.json` sidecar's `SPICE_MK` field names the exact
meta-kernel its geometry was produced against, and `HeraSpiceTests.loadKernelForMbiContent`
honours it — checking a projection against a fixture only means anything under the kernels
that fixture came from. They change when the fixtures are regenerated.

Each pin is looked up in `mk/` first, `mk/former_versions/` second, so it survives the
release that archives it. Archiving rewrites exactly one line — `PATH_VALUES` from `'..'`
to `'../..'`, the file having moved a directory deeper — which the script puts back when
it copies an archived meta-kernel into `mk/` under its plain name.

**To bump:** edit the version in `spice-kernels.pins`, run the script, run
`runAllTests.sh`. Expect numeric differences in the projection tests if the new release
revised a CK or SPK; reviewing that delta is the point. CI refetches on its own — the
pins file is the cache key.

## Loading a kernel

Go through `PRo3D.Base.CooTransformation.switchKernel`, under
`InstrumentProjection.withSpiceLock`. Two rules, both learned the hard way:

**Never depend on the working directory.** SPICE stores the file names a meta-kernel
produces as they read at furnsh time, and DAF re-opens binary kernels lazily, by the
stored name, long after the load. ESA's meta-kernels name them relatively, so those names
resolve only while the process still sits where it sat at load time. `switchKernel` calls
`materializeMetaKernel`, which rewrites `PATH_VALUES` to the absolute directory it always
meant. Nothing needs to `chdir` — and nothing may, since a process-global directory
cannot be saved and restored around a call that races with other loads.

**Hold the projection lock.** Switching is `DeInit` + `Init` + furnsh: it empties the
kernel pool. Doing that while another thread is inside a SPICE call answers that call from
two kernel sets, or crashes it.

Get either wrong and the symptom lands nowhere near the cause: a failed lazy re-open, then
`SPICE(BADSUBSCRIPT)` in `dafah` on the next `DeInit`, then every lookup returning
`SPICE(DAFNOSUCHHANDLE)` — which reads exactly like a kernel with no coverage at the epoch
you asked for.

Only one meta-kernel is loaded at a time; there is no per-kernel unload, and layering them
corrupts state (see `plans/archive/spiceKernelUnloadAndDidymosProjection.md`). Anything
needing two has to serialise, and anything running work concurrently has to stay on one.

## What CI does

`.github/workflows/build.yml` runs the kernel-free suite across the four-OS matrix, and
the kernel-backed suite in a separate `spice-tests` job:

1. `actions/cache` on `spice/`, keyed by `hashFiles('scripts/spice-kernels.pins')` — an
   ESA release does not invalidate it, a pin bump does. `restore-keys` hands a bumped run
   the previous tree, so it refetches one closure instead of four. Note the two hashes
   are not the same: `hashFiles` covers the whole file, while the script's completeness
   check hashes only the pin lines — so editing a comment there costs a new 1.2 GB cache
   entry, but no downloads.
2. `bash scripts/fetch-spice-kernels.sh spice`, cache hit or not.
3. `PRO3D_SPICE_KERNELS=$GITHUB_WORKSPACE/spice bash ./runAllTests.sh`.

Linux only: kernels are platform-independent data, and a 1.3 GB cache entry per OS would
push the paket caches out of the repository's 10 GB budget. The platform-specific risk
sits in the kernel-free suite, which still runs everywhere.

Step 2 runs on a hit because asking the script is the cheapest way to know a restored tree
is usable. It verifies the tree offline against the manifest it wrote
(`kernels/.pro3d-kernels-manifest`: one `path<TAB>size` per file, plus the pins checksum
it was written for) and exits in about a second; anything missing or short is refetched.
A half-restored cache repairs itself instead of surfacing later as a puzzling
`SPICE(FILEOPENFAIL)` inside CSPICE.

The script never deletes, since `dest` may be a developer's own mirror — so a CI tree
carried across pin bumps accumulates superseded kernels. Bounded and cheap (cache entries
expire after a week unused), but if an entry gets uncomfortably large, delete it and let
the next run refetch.

## Notes

- `spice/` is gitignored.
- No credentials or tokens anywhere: this is ESA public data.
- The parser cannot join a path wrapped across two quoted strings. No HERA meta-kernel
  does that, and the script counts quoted entries against parsed paths so it would fail
  loudly rather than fetch a subset.

## Related

- [`docs/spice.md`](../spice.md) — how SPICE is wired into PRo3D at runtime
- [`docs/tests/TestData.md`](TestData.md) — the OPC fixtures and the other test data
- [`docs/Pro3DTool.md`](../Pro3DTool.md) — `PRO3D_SPICE_KERNELS` for the command-line tool
