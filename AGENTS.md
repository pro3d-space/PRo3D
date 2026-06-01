# AI Agent Entry Point

This file provides AI coding assistants with essential context for working with the **PRo3D** codebase — the **P**lanetary **Ro**botics **3D** Viewer, an F# application for interactive visualization of high-resolution 3D reconstructions of planetary surfaces (Mars, Moon, asteroids).

For detailed documentation, see [ai/README.md](ai/README.md).

PRo3D is built on top of the [Aardvark Platform](https://github.com/aardvark-platform) and uses the **aardvark.media** ELM-style UI/rendering framework. The base framework (DomNode, App, camera controllers, animation, RenderControl, ThreadPool) is documented in aardvark.media's own AI docs — see [Relationship to aardvark.media](#relationship-to-aardvarkmedia) below. **These PRo3D docs only cover what is specific to PRo3D.**

## Quick Reference

| Command | Purpose |
|---------|---------|
| `dotnet tool restore` | Restore .NET tools (Paket, Adaptify, Aardpack, Fantomas) |
| `dotnet paket restore` | Restore NuGet dependencies via Paket |
| `build.cmd` / `build.sh` | Restore tools+deps, then run the FAKE build (`dotnet run`) |
| `dotnet run` | Run the FAKE build script (`Build.fs`) — default target compiles the solution + adds native resources |
| `adapt.cmd` / `adapt.sh` | Run Adaptify code generation for all projects (regenerates `*.g.fs`) |
| `dotnet run --project src/PRo3D.Viewer` | Run the main viewer directly |
| `dotnet fantomas src/` | Format F# source |

## Technology Stack

| Component | Version | Purpose |
|-----------|---------|---------|
| .NET SDK | 9.0.0 (`global.json`, `rollForward: latestFeature`) | Runtime/SDK |
| F# | All projects | Language (one C# helper project: `CSharpUtils`) |
| Paket | 9.0.2 | Dependency management |
| Adaptify | 1.3.7 | Code generation for adaptive models (`*.g.fs`) |
| Aardpack | 2.0.5 | NuGet packaging |
| Fantomas | 6.2.3 | F# code formatter |
| FAKE | 6.1.4 | Build orchestration (`Build.fs`) |
| Aardvark.Rendering / UI | ~> 5.6.0 | Rendering + aardvark.media UI |
| Aardvark.Base / Geometry | ~> 5.3.2 / ~> 5.5.0 | Math, geometry, intersection |
| Aardvark.Data.Opc | ~> 0.11.0 | OPC terrain data |
| Aardvark.GeoSpatial.Opc | ~> 5.13.0-prerelease | OPC level-of-detail rendering |
| PRo3D.SPICE | ~> 1.0.6 | Planetary ephemeris / coordinate transforms |

## Dependency Management: Paket

**DO NOT** use `dotnet add package` or edit `<PackageReference>` in `.fsproj` files.

Dependencies are declared centrally in [`paket.dependencies`](paket.dependencies); per-project subsets live in `paket.references` files. Locked versions are in [`paket.lock`](paket.lock) (commit this).

| Task | Command |
|------|---------|
| Restore all | `dotnet paket restore` |
| Add dependency | edit `paket.dependencies` + the project's `paket.references`, then `dotnet paket install` |
| Update dependency | `dotnet paket update <package>` |

## Code Generation: Adaptify

Adaptify generates adaptive (`A...`) counterparts for model record types annotated with `[<ModelType>]`. This is how the immutable model is bridged to the incremental/adaptive model the `view` function consumes (see [ai/ARCHITECTURE.md](ai/ARCHITECTURE.md)).

- Generated files: `*.g.fs` (e.g. `Surface-Model.g.fs` next to `Surface-Model.fs`).
- Triggered automatically by the build (`Adaptify.MSBuild`), or manually via `adapt.cmd` / `adapt.sh` (each project has a `RunAdaptify.fsx`).
- **DO NOT edit `*.g.fs` files** — change the source `.fs` model and regenerate.
- Convention: model files are named `*-Model.fs` (or `*Model.fs`), e.g. `Viewer-Model.fs`, `Surface-Model.fs`.

## Project Structure

The solution is [`src/PRo3D.sln`](src/PRo3D.sln). Key projects:

```
src/
├── PRo3D.Base/            # Foundations: annotations, coordinate transforms (SPICE),
│                          #   serialization, KdTrees, shaders/effects, GIS models, dialogs
├── PRo3D.Core/            # Domain sub-apps: surfaces, drawing, groups, bookmarks,
│                          #   reference systems, scale bars, traverses, GIS, transformations
├── PRo3D.Viewer/          # Main executable: root model/update/view, hosting, command line,
│                          #   remote API, provenance, scene persistence
├── PRo3D.SimulatedViews/  # Simulated instrument/rover camera views, view planning, screenshots
├── PRo3D.Snapshots/       # Headless batch rendering tool (camera/animation snapshots)
├── PRo3D.Lite/            # Lightweight orbit-camera viewer variant
├── PRo3D.GIS/             # Geospatial tooling (image projection, SPICE-backed entities/frames)
├── PRo3D.CorrelationPanels/ # Geologic correlation visualization
├── OpcViewer/             # Shared OPC viewing base functionality
├── opc-tool/              # CLI: validate OPC datasets, build/convert textures + KdTrees
├── ModelViewer/           # Standalone mesh model viewer
├── CSharpUtils/           # C# helper utilities (netstandard2.1)
└── Tests/                 # NUnit/FsUnit tests + notebooks
```

Native code wrappers (instruments) live under `src/InstrumentPlatforms` (built into `lib/JR.Wrappers.dll`). See [ai/RENDERING.md](ai/RENDERING.md) and [ai/AUTOMATION.md](ai/AUTOMATION.md).

## Native Dependencies

- Native libraries are embedded as `native.zip` resources in assemblies and extracted at build time by [`UnpackNativeDependencies.fs`](UnpackNativeDependencies.fs) (uses Mono.Cecil), matched per `os/arch`.
- SPICE coordinate-transform config ships as an embedded resource in `PRo3D.Base` and is initialized at startup via `CooTransformation.initCooTrafo` (see [ai/DOMAIN.md](ai/DOMAIN.md#reference-systems--spice)).
- `JR.Wrappers` native instrument libs are placed under `lib/Native/JR.Wrappers/<platform>/`.

## Framework Rules

1. **F# first** — only `CSharpUtils` and native-wrapper projects are non-F#.
2. **Paket only** — never bypass Paket for dependencies.
3. **Never edit `*.g.fs`** — they are Adaptify output.
4. **Models are immutable records** annotated `[<ModelType>]`; updates return new models. Don't mutate adaptive values directly outside `transact`.
5. **Sub-app composition** — each domain (surfaces, drawing, …) is an ELM sub-app with its own Model/Update/View, composed by the viewer's root update via lenses. See [ai/ARCHITECTURE.md](ai/ARCHITECTURE.md).
6. **Serialization is versioned** — `Scene` carries a `version` int with per-version readers. **Adding a field needs no version bump** if the reader uses `Json.tryRead` + a default; bump `current` and add a `readN` only for breaking changes (rename/remove/retype/changed semantics). See [ai/ARCHITECTURE.md](ai/ARCHITECTURE.md#scene-persistence--versioning).
7. **Document every feature** — each feature ships with a technical doc page under [`docs/`](docs/) (one `.md` per feature). Add/update it as part of the same change. (`docs/` = human feature docs; `ai/` = agent docs.)
8. **Follow [ai/CONVENTIONS.md](ai/CONVENTIONS.md)** — adaptive usage (never `AVal.force` inside an adaptive computation; choose model collection types deliberately), **total functions only** (no `List.find`/`Option.get`/etc.), prefix generic syntax (`Option<int>`, not `int option`), and performance rules (no `Seq`/LINQ or O(n) ops like `List.length`/`List.append` on large/hot data — discuss asymptotically slow choices first).

## Contribution Workflow

PRo3D uses an **issue + feature-branch + PR** workflow (full details in [CONTRIBUTING.md](CONTRIBUTING.md)). When making changes:

- **Never commit directly to `main`/`develop`.** Work on a branch named `features/[issue#]_name` or `bugs/[issue#]_name`, and open a Pull Request for review.
- A PR is expected to include the corresponding [`docs/`](docs/) page for any feature it adds or changes (see rule 7 above).
- Releases are driven by editing `PRODUCT_RELEASE_NOTES.md` / `aardium/package.json` (CI builds and tags). See [docs/Build-Deploy-System.md](docs/Build-Deploy-System.md).

## Common Failures

| Symptom | Cause | Fix |
|---------|-------|-----|
| Build fails: missing packages | Paket restore not run | `dotnet paket restore` |
| Build fails: missing tools | Tools not restored | `dotnet tool restore` |
| Type errors referencing `Adaptive*` types | Stale/missing `*.g.fs` | Rebuild, or run `adapt.cmd` |
| Old scene fails to load | New field read with `Json.read` instead of `Json.tryRead` (no default), or a breaking change without a version bump | Use `Json.tryRead`+default for additive fields; add a `readN` + bump `current` for breaking changes |
| SPICE / coordinate calls return garbage | `CooTransformation` not initialized | Ensure `initCooTrafo` ran with valid kernels at startup |
| Picking does nothing | No KdTrees for the surface | Build KdTrees via `opc-tool` (see [ai/RENDERING.md](ai/RENDERING.md#picking)) |

## Relationship to aardvark.media

PRo3D is an aardvark.media application. For the **base framework** — `App<'model,'mmodel,'msg>`, `Unpersist`, `[<ModelType>]`, `DomNode`, attributes/events, `RenderControl`, camera controllers (`FreeFly`/`ArcBall`/`Orbit`), the animation system, `ThreadPool`/`proclist`, and Suave/`MutableApp.toWebPart` hosting — consult the upstream docs:

- https://github.com/aardvark-platform/aardvark.media/tree/main/ai
  - `ARCHITECTURE.md` — ELM pattern, App type, Unpersist, ThreadPool
  - `UI.md` — DomNode, attributes, events, RenderControl
  - `RENDERING.md` — RenderTask pipeline, server setup
  - `PRIMITIVES.md` — camera controllers, animation, layout
  - `ADVANCED.md` — JS interop, multi-app, custom scene graphs

The PRo3D `ai/` docs assume that knowledge and focus on PRo3D's own architecture and planetary domain.

## Tips for AI Agents

- Read [ai/README.md](ai/README.md) first to route to the right detailed doc.
- Check the longer-form human docs in [`docs/`](docs/) for feature deep-dives (e.g. `ProvenanceTracking.md`, `KdTrees.md`, `spice.md`, `Transformations.md`).
- When changing a model, remember the `*.g.fs` regeneration step.
- When changing persisted state, bump/extend `Scene` versioning.
- Prefer editing the relevant sub-app over the monolithic viewer update where possible.
