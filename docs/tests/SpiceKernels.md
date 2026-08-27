# SPICE kernels for the test suite

Part of the test suite drives real SPICE geometry: HERA spacecraft states, instrument
frames, the Didymos-system body frames the mbi fixtures in `src/Tests/data` were
generated against. That needs ESA's HERA mission kernels, which are public but far too
large to commit or to carry in a submodule.

[`scripts/fetch-spice-kernels.sh`](../../scripts/fetch-spice-kernels.sh) downloads
exactly the kernels the suite loads — **~120 files, ~1.3 GB** — and CI caches them.

| | |
|---|---|
| Source | `https://spiftp.esac.esa.int/data/SPICE/HERA/kernels/` (public, no credentials) |
| Full dataset | ~11 GB — do **not** mirror it |
| What the suite needs | ~1.3 GB, the closure of four meta-kernels |
| Pinned in | [`scripts/spice-kernels.pins`](../../scripts/spice-kernels.pins) |
| Read by | `HeraSpiceTests.kernelsDir`, via `$PRO3D_SPICE_KERNELS` |

## Getting them

```bash
scripts/fetch-spice-kernels.sh spice        # or scripts\fetch-spice-kernels.cmd
PRO3D_SPICE_KERNELS=$PWD/spice ./runAllTests.sh
```

`PRO3D_SPICE_KERNELS` is the same variable [`pro3d-tool`](../Pro3DTool.md) reads, and it
is equally tolerant: point it at the dataset root (the directory holding `kernels/`) or
straight at `kernels/`. Without the variable the tests fall back to a `spice` directory
next to the PRo3D clone, which is how the developer workstations are laid out — a full
ESA mirror there keeps working untouched.

Without kernels at either place, the kernel-backed tests skip themselves.
`runTests.sh` / `runTests.cmd` pass `--skip-hera` to force that even on a machine that
has them, so the kernel-free subset is deterministic; `runAllTests.sh` / `.cmd` run
everything.

## Why a fetch script and not a clone

ESA also publishes the dataset as a git repository (`https://spiftp.esac.esa.int/git/hera.git`),
and a working tree of it is ~11 GB — every version of every CK, SPK and DSK ever
released. The tests need one percent of that, and a meta-kernel already names precisely
which files SPICE will load, in its `KERNELS_TO_LOAD` block. So the script reads the
pinned meta-kernels and fetches their closure over plain HTTPS: 119 files today, 1.23 GiB,
resumable (the server honours range requests) and downloaded 8 at a time.

## Why the pins, and how to bump them

The tests open `hera_ops.tm` and `hera_plan.tm`. On the server those unversioned names
are *copies of whatever release is newest*, and ESA publishes a new SKD every few weeks
— at which point the previous versioned file moves from `mk/` into `mk/former_versions/`.
Depending on those names directly would mean a CI run testing against kernels nobody
chose, changing under you on ESA's schedule.

So `spice-kernels.pins` names versioned files, and the script writes the aliased ones
into `mk/` under the plain names the tests expect:

```
hera_ops_v182_20260527_001.tm -> hera_ops.tm
hera_plan_v182_20260527_001.tm -> hera_plan.tm
hera_ops_v172_20250312_001.tm
hera_plan_v180_20250616_001.tm
```

The last two are not a choice: the mbi sidecars name them. An `.mbi.json` carries a
`SPICE_MK` field naming the exact meta-kernel its geometry was produced against, and
`HeraSpiceTests.loadKernelForMbiContent` honours it — comparing a projection against a
fixture only means something under the kernels that fixture came from. They change when
the fixtures are regenerated, not otherwise.

Pins are looked up in `mk/` first and `mk/former_versions/` second, so a pin keeps
working across the release that archives it. Archiving rewrites one line — `PATH_VALUES`
goes from `'..'` to `'../..'`, because the file moved one directory deeper — and the
script rewrites it back when it copies an archived meta-kernel into `mk/` under its
plain name. The kernel list itself is untouched by archiving.

**To bump:** change the version in `spice-kernels.pins`, run the script, run
`runAllTests.sh`. Expect numeric differences in the projection tests if the new release
revised a CK or SPK — that is the point of pinning, and reviewing the delta is the work.
CI refetches automatically: the pins file is the cache key.

## What CI does

`.github/workflows/build.yml` runs the kernel-free suite across the four-OS matrix and
the kernel-backed suite in a separate `spice-tests` job:

1. `actions/cache` on `spice/`, keyed by `hashFiles('scripts/spice-kernels.pins')`.
   A new ESA release does not invalidate it; a pin bump does. `restore-keys` hands a
   bumped run the previous tree so it refetches one closure rather than four.
2. `bash scripts/fetch-spice-kernels.sh spice` — unconditionally, cache hit or not.
3. `PRO3D_SPICE_KERNELS=$GITHUB_WORKSPACE/spice bash ./runAllTests.sh`.

Linux only, deliberately: kernels are platform-independent data, and a 1.3 GB cache
entry per OS would push the paket caches out of the repository's 10 GB budget. What is
covered on Linux is coverage of *this* data — the platform-specific risk lives in the
kernel-free suite, which still runs everywhere.

Step 2 runs on a cache hit as well, because the cheapest way to know a restored tree is
usable is to ask the script: it verifies the tree against the manifest it wrote
(`kernels/.pro3d-kernels-manifest` — one `path<TAB>size` line per file, plus the pins
checksum it was written for) entirely offline, and exits in about a second when
everything matches. When something does not match — a partial restore, or a bumped pin —
it fetches only what is missing or short. A half-restored cache repairs itself rather
than surfacing later as a puzzling `SPICE(FILEOPENFAIL)` deep inside CSPICE.

One consequence worth knowing: the script never deletes anything, because `dest` may be
a developer's own mirror. A CI tree carried across pin bumps therefore accumulates
superseded kernels. That is bounded and cheap — a few hundred MB per bump, and cache
entries expire after a week unused — but if the entry ever gets uncomfortably large,
delete it from the Actions cache and let the next run refetch.

## Loading a kernel, in tests or anywhere else

Go through `PRo3D.Base.CooTransformation.switchKernel`. Two things it gets right that
a direct `AddSpiceKernel` does not:

- **Never depend on the working directory.** SPICE stores the file names a meta-kernel
  produces exactly as they read at furnsh time, and DAF re-opens binary kernels lazily,
  by the stored name, long after the load. ESA's meta-kernels name them relatively
  (`PATH_VALUES` is `'..'` in `mk/`, `'../..'` in `mk/former_versions/`), so those names
  only resolve while the process sits where it sat at load time. `switchKernel` calls
  `materializeMetaKernel`, which rewrites `PATH_VALUES` to the absolute directory it
  always meant. Nothing has to `chdir`, and nothing may — a process-global directory
  cannot be saved and restored around a call that races with other loads.
- **Take `InstrumentProjection.withSpiceLock` around it.** Switching is `DeInit` +
  `Init` + furnsh: it empties the kernel pool. Doing that while another thread is inside
  a SPICE call answers that call from two different kernel sets at best, and takes the
  process down at worst.

Get either wrong and the symptom appears nowhere near the cause: a failed lazy re-open,
`SPICE(BADSUBSCRIPT)` in `dafah` on the next `DeInit`, and from then on every lookup
returning `SPICE(DAFNOSUCHHANDLE)` — which reads exactly like a kernel that has no
coverage at the epoch you asked for.

Only one meta-kernel is loaded at a time (there is no per-kernel unload, and layering
meta-kernels corrupts state — see `plans/archive/spiceKernelUnloadAndDidymosProjection.md`).
Anything wanting two of them has to serialise, and anything running work *concurrently*
has to stay on one.

## Notes

- `spice/` is gitignored.
- Kernels are ESA public data; nothing here needs credentials or a token.
- The one thing the parser cannot handle is a meta-kernel that wraps a long path across
  two quoted strings. None of the HERA meta-kernels do, and the script counts quoted
  entries against parsed paths and fails loudly rather than quietly fetching a subset.

## Related

- [`docs/spice.md`](../spice.md) — how SPICE is wired into PRo3D at runtime
- [`docs/tests/TestData.md`](TestData.md) — the OPC fixtures and where the other test data lives
- [`docs/Pro3DTool.md`](../Pro3DTool.md) — `PRO3D_SPICE_KERNELS` for the command-line tool
