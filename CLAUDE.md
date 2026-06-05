# PRo3D — Instructions for AI Assistants

PRo3D (Planetary Robotics 3D Viewer) is an F# application built on the **Aardvark Platform** / **aardvark.media** ELM-style framework.

**Read [AGENTS.md](AGENTS.md) first** for build commands, dependency management (Paket), code generation (Adaptify), and project structure. Then route to the detailed docs via **[ai/README.md](ai/README.md)**:

- [ai/ARCHITECTURE.md](ai/ARCHITECTURE.md) — root model, sub-app composition, hosting, scene versioning
- [ai/DOMAIN.md](ai/DOMAIN.md) — surfaces, annotations, reference systems/SPICE, GIS, bookmarks, …
- [ai/RENDERING.md](ai/RENDERING.md) — OPC data, scene graph, shaders, KdTree picking
- [ai/CONVENTIONS.md](ai/CONVENTIONS.md) — **must-read** adaptive/F# coding conventions
- [ai/AUTOMATION.md](ai/AUTOMATION.md) — command line, remote API, snapshots, provenance

The base framework (App/Unpersist/DomNode/controllers/animation/ThreadPool) is documented upstream at https://github.com/aardvark-platform/aardvark.media/tree/main/ai — the PRo3D `ai/` docs cover only what is specific to PRo3D.

## Hard rules

- **Paket only** — never `dotnet add package` or edit `<PackageReference>`. Edit `paket.dependencies` + `paket.references`.
- **Never edit `*.g.fs`** — they are Adaptify output. Change the `*-Model.fs` source and run `adapt.cmd` / `adapt.sh`.
- **Models are immutable `[<ModelType>]` records**; updates return new models.
- **Persisted state is versioned** — *adding* a field needs no version bump if read with `Json.tryRead` + a default; bump `Scene.current` / add a `readN` only for breaking changes (rename/remove/retype/changed meaning).
- **Never `AVal.force` inside an adaptive computation** (`AVal.map`/`AList.collect`/CEs) — bind with `let!`. Forcing is OK only in imperative/UI callbacks; inside a custom `Sg`/`RenderObject` use the provided token (`x.GetValue(t)`). See [ai/CONVENTIONS.md](ai/CONVENTIONS.md).
- **Total functions only** — no `List.find`, `List.head`, `Option.get`, `Map.find`, etc. Use `tryFind`/pattern matching/`Option`.
- **Prefix generic syntax** — `Option<int>`, `IndexList<T>`, `HashMap<K,V>`; never postfix `int option`.
- **Mind data structures & complexity** — no `Seq`/LINQ or O(n) ops (`List.length`, `List.append`) on large/hot data; use `array` loops / `HashMap`. Fuse pipelines (`choose` over `map`+`filter`, `collect` over `map`+`concat`) — also for adaptive ops. Large-map type choice (`Map` vs `HashMap` vs mutable `Dictionary`) is a trade-off — discuss; discuss asymptotically slow choices first.
- **Choosing a model collection type is a design decision** — `IndexList`/`HashSet`/`HashMap` give per-element adaptive tracking; a plain field gives `aval<whole>`. Ask when unsure.
- **Precision (planetary scale) — never transform world coordinates in a `float32` shader.** Geometry lives in **local space** (OPC patch-local; annotations relative to their first point), placed with `Sg.trafo`; the MVP is composed on the **CPU in `double`** (`stableTrafo`) and the shader goes **local → view space** directly. Do **lighting** and **large-triangle filtering** in **view space**, not world space.
- **Scene graph (Aardvark.Rendering)** — instance repeated geometry (`Sg.instanced`), cache/reuse composed effects, update the graph rather than rebuilding it, and **discuss shader-vs-CPU trade-offs** with the user. See [ai/CONVENTIONS.md](ai/CONVENTIONS.md#scene-graph--rendering-aardvarkrendering).
- **Every feature gets a `docs/<Feature>.md` page** — add/update it in the same change (`docs/` = human docs, `ai/` = agent docs).
- **Contribute via PRs** — work on a `features/[issue#]_name` or `bugs/[issue#]_name` branch, never commit straight to `main`/`develop`. See [CONTRIBUTING.md](CONTRIBUTING.md).
