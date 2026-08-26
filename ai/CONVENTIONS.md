# Coding Conventions

How to write code that fits PRo3D. Two parts: **FSharp.Data.Adaptive usage** (the rules that keep the incremental system correct and fast) and **general F# style** (which mostly defers to the official standard).

> Adaptive basics — `aval`/`aset`/`alist`/`amap`, `AVal.map`/`bind`, the `[<ModelType>]` → adaptive-model bridge — are in [aardvark.media/ai/ARCHITECTURE.md](https://github.com/aardvark-platform/aardvark.media/tree/main/ai) and [ARCHITECTURE.md](ARCHITECTURE.md). This page is about *using them well*.

---

## FSharp.Data.Adaptive Rules

### 1. Never `AVal.force` inside an adaptive computation

Do **not** call `AVal.force` (or `.GetValue()` with a fresh token) inside `AVal.map`/`bind`, `AList.collect`/`map`, `ASet.*`, `AMap.*`, or inside an `aval`/`alist`/`aset`/`amap` computation expression. Forcing there reads a value *without registering a dependency*, so the result will **not** recompute when that value changes — a silent staleness bug.

Bind dependencies instead, with `let!` / `and!` (CE) or the combinators:

```fsharp
// ❌ breaks dependency tracking
let bad = model.count |> AVal.map (fun _ -> AVal.force model.name)

// ✅ both inputs tracked
let good =
    aval {
        let! count = model.count
        let! name  = model.name
        return sprintf "%s: %d" name count
    }
// or: AVal.map2 (fun c n -> ...) model.count model.name
```

### 2. Ask: adaptive collection vs. `aval<collection>`

When a model holds many items, there are two representations, and the choice is a real trade-off — **raise it with the user / reviewer rather than picking silently:**

- **Adaptive collection** (`aset` / `alist` / `amap`, or the Adaptify-generated `Adaptive*` collection): tracks changes **per element**. Adding/removing/updating one item only re-runs work for that item. Best when the collection is large, changes incrementally, and each element maps to its own UI node or scene-graph object. Cost: per-element bookkeeping overhead.
- **`aval<collection>`** (e.g. `aval<HashMap<_,_>>`): tracks the collection as **one value**. Any change invalidates the whole thing and recomputes everything downstream. Best when the collection is small, replaced wholesale, or consumed by an operation that needs all of it at once. Cost: no fine-grained reuse.

Rule of thumb: per-element rendering / large, incrementally-edited data → adaptive collection; small or wholesale-replaced data, or feeding an all-at-once computation → `aval<collection>`. **When it's not obvious, ask and document the decision.**

### 3. `.Current` / `.Content` can beat chains of operators — at a cost

A long pipeline of adaptive operators carries per-operator overhead. For a **complex** computation it is sometimes cheaper to grab the whole current value as a single `aval` and run a plain, non-incremental function over it:

- Adaptify generates a **`.Current`** member on every adaptive model type — the whole value as one `aval<'T>` (e.g. `fcm.Current : aval<FalseColors>`).
- Adaptive collections expose **`.Content`** — the content as one `aval` (e.g. `model.selectedLeaves.Content : aval<HashSet<_>>`).

```fsharp
// Instead of composing many ASet/AList operators, do the heavy work once per change:
adaptive {
    let! content = someAdaptiveSet.Content   // aval<HashSet<_>>
    return expensivePureComputation content
}
```

The trade-off mirrors rule 2: you lose per-element incrementality (the whole thing recomputes on any change) but avoid operator overhead. **This is per-case — measure/reason about it; don't apply it blindly.** Reasoning about
it means knowing how often the underlying record actually changes in a real scene, which
is a load characteristic, not something to infer from the code — see rule 6.

**What `.Current` costs.** Adaptify does not generate a per-field `.Current`. It generates
*one* `AVal.custom` over the whole record, marked outdated whenever `Update` is handed a
shallow-unequal value:

```fsharp
member __.Update(value : 'T) =
    if not (ShallowEquals(value, __value)) then
        __value <- value
        __adaptive.MarkOutdated()      // ← this is .Current, for every field at once
member __.Current = __adaptive
```

So the cost of `.Current` is set by everything *else* in the record, not by the fields you
read out of it. Reading two fields through it still takes a dependency on all of them. The
question is therefore not "how much of this record do I need?" but **"how often does the
busiest field in this record change?"**

That makes `.Current` a good fit for a small record, or one whose fields change together,
and a poor fit for an **aggregate root** — a record that keeps content, selection state and
UI state side by side. There it fires on interactions that have nothing to do with the data
you wanted, and the pipeline you replaced was probably cheaper.

`.Content` on an adaptive collection is the same idea scoped to one collection: narrower
than `.Current`, but it still collapses per-element tracking.

> *Illustration.* `GroupsModel` is the shared group-tree model behind annotations, surfaces
> and bookmarks. One record holds the item map, the current selection, and tree UI state, so
> its `.Current` is marked outdated when a group is expanded or the selection changes — not
> only when an item's data changes. A computation reading two fields of one item through
> `.Current` re-runs on all of that. See `Groups-Model.g.fs`.

### 4. `AVal.force` is fine in imperative callbacks

UI event handlers and similar imperative callbacks (`onClick`, `onMouseMove`, button/menu handlers, command-line/remote-API handlers) run **outside** any adaptive computation — they just need the current value *now*. `AVal.force` is appropriate there.

```fsharp
button [ onClick (fun _ ->
    let current = AVal.force model.value   // ✅ one-shot read in an event callback
    DoSomething current) ] [ text "go" ]
```

**Inside a custom `Sg`/`RenderObject`** you are handed an `AdaptiveToken` — use it: `someAval.GetValue(token)` (e.g. `annoSet.Content.GetValue(t)`). That registers the dependency against the render evaluation, so it is *not* the same mistake as rule 1. Never substitute `AVal.force` for the provided token.

### 5. Model collection types decide the adaptive mapping — choose deliberately

The decision in rules 2–3 is actually made **when you pick the field type in the immutable model**: Adaptify maps each model type to a specific adaptive type in the generated `Adaptive*` record (`*-Model.g.fs`). The collection type you write *is* the per-element-vs-whole-value choice.

| Model field type | Generated adaptive type | Tracking |
|------------------|-------------------------|----------|
| `IndexList<'T>` | `alist<'T>` | per-element (ordered) |
| `HashSet<'T>` | `aset<'T>` | per-element (set) |
| `HashMap<'K,'V>` | `amap<'K,'V>` | per-element (keyed) |
| plain value (`int`, `V3d`, `string`, `Trafo3d`, …) | `aval<'T>` | whole value |
| `Option<'T>` | adaptive option (`aval`-like) | whole value |
| nested `[<ModelType>]` record | its `Adaptive<Record>` | per-field |
| any field marked `[<TreatAsValue>]` | `aval<'T>` (opaque) | whole value, not descended into |
| any field marked `[<NonAdaptive>]` | plain `'T` | excluded from the adaptive model |

Two consequences worth internalizing (both visible in `Groups-Model.g.fs`):

- **Element type matters too.** If the elements of an `IndexList`/`HashMap` are themselves `[<ModelType>]`, Adaptify makes the *elements* adaptive — `IndexList<Node>` → `alist<AdaptiveNode>`, `HashMap<Guid,Leaf>` → `amap<Guid, AdaptiveLeafCase>` — so a change to one field of one element only re-runs that field's dependents. With a plain element type you only get add/remove/replace granularity.
- **Wrapping kills granularity.** `aval<HashMap<_,_>>` (or putting a collection behind `[<TreatAsValue>]`) collapses the whole collection to one value — any change recomputes everything downstream. Use that on purpose (rules 2–3), not by accident.

So: **don't reach for a plain field or `Option<collection>` when you want incremental UI/scene-graph updates, and don't pay for `alist`/`amap`/`aset` element-tracking on a collection you always replace wholesale.** When the right choice isn't obvious, ask (rule 2). After changing a model type, run `adapt.cmd` and check the regenerated `*.g.fs` is what you intended.

### 6. Deriving an aggregate from an adaptive collection

Shape: reduce **a subset of a large adaptive collection to a single value** — a count, a
bounding box, a histogram, a min/max. The result is one value, so the computation
necessarily ends in an `aval`. **That final collapse is not a granularity violation** —
rules 2 and 5 govern how the *model* stores a collection, not the sink of a derived
computation. What matters is what sits between the collection and the collapse.

Throughout: `items : amap<Key, AdaptiveItem>` is the model's collection, large; `keys` is
the subset you care about, small, and fixed for the lifetime of this computation.

#### Method — and what you are not allowed to guess

Two inputs decide the shape, and only one of them is in the code:

1. **The event sets.** Which events *should* re-fire the aggregate (normally: the subset
   changed, or a member of it changed in a field the aggregate reads), and which events each
   candidate *does* re-fire on. The gap between them is the metric. Node count is not — a
   shape with more nodes that fires only when it must beats a three-node shape that fires on
   everything.

2. **The load characteristics.** How large is the collection in a real scene, how large is a
   typical subset, how often does each change, and which of those changes a user actually
   notices. **These are not in the code and must not be invented.** Statements like "the
   collection is much larger than the subset" or "the selection changes more often than the
   data" flip the answer between the shapes below, and both have been guessed wrong here
   before. Ask the people who run the app on real data.

As in rule 2, the resulting trade-off is a **design decision, not an implementation detail:
put the numbers and the choice to the user or reviewer and get agreement rather than picking
silently.** Then record the answer in a comment next to the code, so the next reader inherits
the decision instead of re-deriving it from scratch — and so a later change in scene sizes
can be recognised as invalidating it.

#### Don't call `AMap.tryFind` once per key

This is the shape that looks obvious and is wrong:

```fsharp
// ✗ one lookup per key
keys |> Seq.map (fun k -> AMap.tryFind k items)        // K separate avals
```

`AMap.tryFind` is not an index into the map. The library implements it as

```fsharp
let tryFind key map = map.Content |> AVal.map (HashMap.tryFind key)
```

— so each call is a dependent of the map's **entire content**. K calls hang K dependents off
that one node, and it is marked outdated by *any* change to the map. Insert an item under a
key you don't care about and all K lookups re-run, along with everything downstream of each.

The instinct that misleads is "`HashMap.tryFind` is O(1), so K of them is O(K)". That is true
of the evaluation. The cost is in the dependency graph, not in the lookup. FSharp.Data.Adaptive
says so in its own doc comment: *"this operation should not be used extensively since its
resulting aval will be re-evaluated upon every change of the map."*

One or two fixed keys is fine — it is the fan-out that hurts. For a set of keys:

```fsharp
// ✓ one reader; each change classified once
items |> AMap.filter (fun k _ -> HashSet.contains k keys)
```

`AMap.filter` holds a single reader on `items`. Changes arrive as deltas, the predicate
classifies each once, and deltas for keys outside the subset are dropped without waking
anything downstream.

#### In-place element edits do not move the map

Adaptify maps a `HashMap<K, SomeModelType>` field to a `ChangeableModelMap`. Its `Update`
diffs the **keys**; where a key survives carrying new data it calls `elem.Update(v)`, which
pokes that element's own cvals. **No map delta is produced.**

So a map delta means "an item was added or removed", never "an item changed" — edits travel
through the element's own adaptive fields instead. That is what makes the filter above stay
quiet when an item outside the subset is edited: there is no delta to classify, and this
computation holds no dependency on that element's fields. It is also why the `.Current`
shape is worse here: it re-fires on every edit in the collection, subset or not.

#### The remaining two trade-offs

- **Precise invalidation over N sources costs N edges.** If the result must react to N
  independent adaptive values and to nothing else, something has to depend on all N. There is
  no O(1)-edge formulation with that precision, so the real choice is "N edges" versus "one
  blunt edge that fires too often".

- **Placement matters as much as size.** A per-element layer built *inside* a block bound to
  something volatile (a selection, a mode) is torn down and rebuilt whenever that block
  re-runs. Hoisting it above the block fixes the churn, but it then spans the whole
  collection and pays edges for elements the subset never contains. Which way that falls
  depends entirely on input 2 above — confirm the sizes before choosing, because the answer
  reverses when a typical subset is most of the collection.

> *Illustration.* A dip-direction histogram over a multi-selection of annotations
> (`ViewerGUI.viewBulkAnnotationProperties`): thousands of items in the map, tens to a few
> thousand selected. It filters to the selected keys, reads two fields per selected item,
> collapses, and folds — so editing an unselected item costs nothing, while the per-item
> layer is rebuilt when the selection changes, inside a block that rebuilds the panel anyway.
> Fixtures for measuring this shape: `src/Tests/data/bulk-rose-annotations.pro3d.ann` and the
> larger one in `PRo3D.Resources.TestData`.

### Quick reference

| Context | Reading an `aval` |
|---------|-------------------|
| Inside `AVal.map`/`bind`, `aval`/`alist`/`aset`/`amap` CE | `let!` / `and!` / combinators — **never** `AVal.force` |
| Inside a custom `Sg` / `RenderObject` (has a token `t`) | `x.GetValue(t)` |
| UI event / imperative callback | `AVal.force x` is OK |
| Choosing collection representation | ask: adaptive collection vs `aval<collection>` (rules 2–3) |
| Reducing a collection to one value | one `AMap.filter`, not `AMap.tryFind` per key; sizes and change rates are load characteristics — ask, don't assume (rule 6) |
| Reaching for `.Current` on a big record | check what else lives in that record first — it fires for all of it (rule 3) |

---

## General F# Style

For general formatting/naming, use the **official Microsoft F# style guide** as the baseline — **except where a PRo3D-specific rule below overrides it** (notably generic-type syntax). When something isn't covered here, the guide is the default:

- **F# style guide (index):** https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/
- **Formatting conventions:** https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/formatting
- **Coding conventions:** https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/conventions
- **Component design guidelines** (public API shape): https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/component-design-guidelines

Formatting is enforced by **[Fantomas](https://fsprojects.github.io/fantomas/)** (installed as a dotnet tool, v6.2.3): run `dotnet fantomas src/`. Let Fantomas own whitespace/layout — don't hand-fight it.

### Total functions only (no partial functions)

**Do not use partial functions that throw on missing/empty input.** This is a hard rule. Banned in this codebase: `List.find`, `List.head`, `List.last`, `List.item` / `.[i]` on lists, `Seq.head`/`Seq.find`, `Array.find`, `Map.find`, `HashMap.find` (the throwing variants), `Option.get`, `.Value` on an `Option`, and `failwith`/`raise` used as a lookup-miss path.

Use the total alternative and handle the missing case explicitly:

```fsharp
// ❌ throws if not found / empty
let s = surfaces |> List.find (fun x -> x.id = id)
let v = map |> HashMap.find key
let x = opt.Value

// ✅ total — returns Option, force the caller to handle absence
let s = surfaces |> List.tryFind (fun x -> x.id = id)      // Surface option
match HashMap.tryFind key map with
| Some v -> ...
| None   -> ...                                            // explicit
let x = opt |> Option.defaultValue fallback                // or match
```

Prefer `tryFind`/`tryHead`/`tryItem`/`tryPick`, pattern matching, `Option`/`Result` return types, and `Option.defaultValue`/`Option.map`/`Option.bind`. If absence is genuinely impossible, prove it in the types or make the impossibility obvious — don't rely on a partial function and a comment.

### Naming (per the style guide)

- **PascalCase**: types, modules, members, union cases, the `Option`/`Result`/`List` *modules and types*, namespaces. (`type SurfaceModel`, `module SurfaceApp`, `| SurfaceActions of ...`.)
- **camelCase**: `let`-bound values and functions, parameters, locals. (`let updateViewer m a = ...`.)
- Model files: `*-Model.fs`; generated adaptive types: `*-Model.g.fs` (never edited — see [../AGENTS.md](../AGENTS.md)).

### Generic types: prefix angle-bracket form (PRo3D override)

Write generic types with **explicit angle brackets in prefix form** — `Option<int>`, `list<Surface>`, `IndexList<WayPoint>`, `HashMap<Guid, Leaf>`, `aval<'T>`. **Do not** use the postfix abbreviation form `int option`, `Surface list`, `Guid[]`-style aliases in signatures.

```fsharp
// ✅ PRo3D
surfaceIntersection : Option<SurfaceIntersection>
viewPortSizes       : HashMap<string, V2i>
waypoints           : IndexList<WayPoint>

// ❌ not used here
surfaceIntersection : SurfaceIntersection option
```

This is the Aardvark-ecosystem convention and it **intentionally differs from the Microsoft style guide's default** (which prefers postfix `int option`). In PRo3D the prefix form wins — it reads consistently with `aval<_>`/`alist<_>`/`amap<_>` and the generated adaptive types, which are always angle-bracketed.

### Performance & data structures

Pick the data structure for how it's *used*, and keep large/hot-path data out of slow operations.

- **Model state** uses the immutable/adaptive collections from FSharp.Data.Adaptive: `HashMap`, `HashSet`, `IndexList` (mapped to `amap`/`aset`/`alist` — see [rule 5](#5-model-collection-types-decide-the-adaptive-mapping--choose-deliberately)). Prefer these over BCL mutable collections in models.
- **`Seq` / LINQ-style pipelines are fine for small-to-moderate data** and one-shot/UI code. **Do not run them over large data** (per-vertex/per-patch arrays, point clouds, pixel buffers): the closures and intermediate `IEnumerable`s allocate per element. For large data use **allocation-free `array` loops** (`for`/`while` over arrays, in place where possible). This is the OPC/rendering hot path — see [RENDERING.md](RENDERING.md).
- **`array` over `list` for large or performance-critical buffers** — arrays are contiguous and O(1)-indexed; F# `list` is a singly-linked cons list (O(n) indexing, per-node allocation). `list`/`seq` are fine for small, locally-constructed, ergonomic data.
- **Fuse pipeline operations — one pass, not several.** Each `map`/`filter`/etc. is a full traversal + intermediate allocation; collapse them:
  - `map` + `filter` (in either order) → **`choose`** (`fun x -> if pred x then Some (f x) else None`).
  - `map` + `concat` / nested results → **`collect`**.
  - `filter` + `head`/`tryHead` → **`tryFind`**; `map` + `tryFind` → **`tryPick`**.

  This applies equally to `List`, `Seq`, **and the adaptive operators** (`AList.choose`/`collect`, `ASet.choose`/`collect`, `AMap.choose`) — fusing there also reduces the number of adaptive nodes and their bookkeeping.

  ```fsharp
  // ❌ two passes + an intermediate list
  xs |> List.map f |> List.filter pred
  // ✅ one pass
  xs |> List.choose (fun x -> let y = f x in if pred y then Some y else None)
  ```
- **Map types — for large maps, weigh the trade-offs (and ask):** three reasonable choices, each different:
  - **F# `Map<'K,'V>`** — immutable, balanced-tree, **O(log n)**, ordered by key. Good for small/medium immutable maps where key ordering helps.
  - **`HashMap<'K,'V>`** (FSharp.Data.Adaptive) — immutable/persistent, hash-based, **~O(1)**. Usually the better immutable choice for **large** maps with frequent lookups, and it's what the model layer uses (maps to `amap`, see [rule 5](#5-model-collection-types-decide-the-adaptive-mapping--choose-deliberately)).
  - **`Dictionary<'K,'V>`** (BCL) — mutable, **O(1)**, fastest, but not immutable/persistent.

  Rule: **if you need immutability, stay on an immutable map** (`Map`/`HashMap`; prefer `HashMap` when large). **Hot code should at least consider a mutable `Dictionary`**, possibly only *temporarily* (build mutably in a local, then freeze into an immutable map before it escapes). For large maps this is a real trade-off — **discuss it with the user** rather than defaulting.
- **O(n) operations are a code smell that you picked the wrong structure.** In particular:
  - `List.length` — if you need a count often, you probably want an `array` or to track the count. It's O(n) on a list.
  - `List.append` / `@` — O(n) per call, O(n²) in a loop. Repeated appends mean you want an `array`/`ResizeArray`/`IndexList`, or to build then reverse.
  - `List.item` / indexing into a list — O(n); use `array` or `IndexList`/`HashMap`.
- **Asymptotically slow operations must be discussed with the user before they go in.** Anything super-linear, or linear work inside a hot loop (e.g. an O(n) lookup per element → O(n²)), is a design decision, not a detail — raise it. Reach for the structure with the right complexity (`HashMap`/`HashSet` for membership/lookup, `array` for sequential/indexed access) instead.

### Other

- **Immutability first** — models are immutable records; updates return new records. Reserve `mutable`/`ref`/caches for genuine performance needs (e.g. the KdTree cache), and keep them out of the persisted model.
- **Delete dead code** rather than commenting it out (the codebase has accumulated commented blocks — don't add more).
- Prefer extending the relevant **sub-app** over growing the monolithic viewer `update` (see [ARCHITECTURE.md](ARCHITECTURE.md#sub-app-composition)).

---

## Scene Graph & Rendering (Aardvark.Rendering)

The scene graph (`ISg`) is adaptive, but it is **not free** — each render object is prepared (shaders compiled, buffers uploaded, render state resolved) and every draw call has overhead. Build the graph for efficient GPU submission, and prefer letting the adaptive system *update* an existing graph over *rebuilding* it.

### Numerical precision — read this before touching geometry or shaders

**This is fundamental, not optional.** PRo3D renders planetary-scale data: world coordinates are huge (e.g. a Mars-radius point is ~3.4×10⁶ m). `float32` (GPU) has ~7 significant digits, so transforming *world* coordinates in a shader produces visible jitter / cracks. The whole rendering pipeline is designed around avoiding that:

- **Geometry lives in a local space, never global world space.**
  - **OPC patches** each have their own local coordinate space (patch-local vertices + a patch transform).
  - **Annotations** invent a local space by translating from the origin `(0,0,0)` to the annotation's **first point**; vertices are stored relative to that point.
  - The local geometry is placed with **`Sg.trafo`**.
- **Compose the model→view→projection matrix on the CPU in `double`**, then hand it to the shader (this is what `DefaultSurfaces.stableTrafo` is for). The shader transforms **directly from local space into view space** — it never reconstructs a global float world position. **Never** introduce a shader path that puts world-space coordinates through a `float32` transform.
- **Do lighting in view space.** In PRo3D lighting math runs in *view* space (coordinates are small and near the camera there), **not** world space.
- **Filter / process large triangles in view space.** Operations like culling/filtering big triangles must be done in view space — world space suffers numerical problems at planetary scale.

When you add geometry, a shader, or any per-vertex math: adopt the same discipline — local-space geometry + `Sg.trafo`, double-composed MVP (reuse `stableTrafo` / the existing pattern), and do per-vertex work (lighting, large-triangle filtering, screen-space scaling) in **view space**. If a feature seems to need world-space coordinates on the GPU, that's a red flag — raise it.

### Optimizations

- **Instance repeated geometry — don't emit one `Sg` per copy.** When you draw many copies of the same geometry (markers, scale-bar ticks, points, rover sol markers, preview cones/cylinders), use **`Sg.instanced` / `Sg.instanced'` / `Sg.instancedGeometry`** — one draw call with per-instance attributes (e.g. an `aval<Trafo3d[]>` of transforms) instead of N nodes and N draw calls. PRo3D already does this (`src/PRo3D.Viewer/TraverseApp.fs` sol markers; `src/PRo3D.Core/Drawing/PackedRendering.fs` cones/cylinders). If you find yourself `List.map`-ing geometry into many `Sg` nodes, reach for instancing first.
- **Cache / reuse effects when it's easy.** FShade effect composition + compilation is expensive. Compose an effect (or effect list) **once** — a module-level `let` value or a value cached per surface — and reuse it; don't rebuild the effect array inside a `view`/per-object/per-frame path (constructing fresh effect lists each time defeats the compilation cache). See `src/PRo3D.Base/OutlineEffect.fs` for composed effect lists. Reuse `IndexedGeometry`/buffers similarly rather than recreating per change.
- **Shader vs. CPU is always a trade-off — discuss it.** Work in a shader runs massively parallel on the GPU but **recomputes every frame** and can only see GPU-side inputs; the same work on the CPU can be **computed once and cached** but costs memory, must be recomputed (and re-uploaded) when inputs change, and isn't parallelized the same way. The right side depends on data size, how often inputs change, and whether the result is reused (e.g. per-vertex false-color mapping, transforms, culling). **Don't silently pick one — raise the trade-off with the user** when it's a non-trivial amount of work either way.
- **Update, don't rebuild.** Prefer driving the existing scene graph with adaptive uniforms/attributes/transforms over reconstructing `Sg` nodes when the model changes — rebuilding re-prepares render objects. This is the rendering-side payoff of choosing model collection types for per-element tracking ([rule 5](#5-model-collection-types-decide-the-adaptive-mapping--choose-deliberately)) and of the adaptive rules above. Keep `AVal.force` out of these paths (use the render token; [rule 4](#4-avalforce-is-fine-in-imperative-callbacks)).
- **Minimize render objects / draw calls and per-frame allocations** — group by geometry/render state, avoid allocating arrays or recomposing scene graphs inside hot adaptive callbacks.

See [RENDERING.md](RENDERING.md) for the concrete PRo3D surface/OPC pipeline these rules apply to.

## See Also

- [ARCHITECTURE.md](ARCHITECTURE.md) — the adaptive model bridge and update loop
- [RENDERING.md](RENDERING.md) — where the array-vs-list / `.GetValue(token)` guidance bites
- [../AGENTS.md](../AGENTS.md) — framework rules, Adaptify, Paket
- aardvark.media AI docs — adaptive system fundamentals
