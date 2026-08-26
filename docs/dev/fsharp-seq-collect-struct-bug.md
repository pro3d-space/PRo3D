# F# codegen bug: `Seq.collect` over a struct `IEnumerable` emits invalid IL under `--optimize+`

Hit while implementing annotation boolean operations. Every test touching
`AnnotationRegionOps.commonChart` threw `System.InvalidProgramException` — **in Release only**,
Debug was fine — which is why it passed locally and failed in CI (`runTests.sh` uses
`-c Release`).

This page records the analysis so the workaround in `AnnotationRegionOps.fs` is not "cleaned
up" by someone later, and so the report can be filed upstream.

## Summary

When `Seq.collect`'s lambda returns a **value type that implements `IEnumerable<'T>`**, and the
result is consumed by a **materializing** function (`Seq.toArray` / `Seq.toList`), the optimizer
fuses the two into an `ArrayCollector`/`ListCollector` loop and emits IL that is not type-safe.
The JIT rejects the enclosing method at run time.

`FSharp.Data.Adaptive.IndexList<'T>` is such a struct, and `Annotation.points` is an
`IndexList<V3d>` — so `annotations |> Seq.collect (fun a -> a.points) |> Seq.toArray` compiled
to an unverifiable method.

## Minimal repro (no external dependencies)

```fsharp
module Repro

open System.Collections
open System.Collections.Generic

[<Struct>]
type StructSeq =
    val Items : int[]
    new (items : int[]) = { Items = items }
    interface IEnumerable<int> with
        member this.GetEnumerator() : IEnumerator<int> =
            (this.Items :> IEnumerable<int>).GetEnumerator()
    interface IEnumerable with
        member this.GetEnumerator() : IEnumerator =
            this.Items.GetEnumerator() :> IEnumerator

type Holder = { points : StructSeq }

let collectThenToArray (hs : seq<Holder>) =
    hs |> Seq.collect (fun h -> h.points) |> Seq.toArray

[<EntryPoint>]
let main _ =
    let data = [ { points = StructSeq [| 1; 2 |] }; { points = StructSeq [| 3 |] } ]
    printfn "%A" (collectThenToArray data)
    0
```

- `dotnet run -c Debug` → `[|1; 2; 3|]`
- `dotnet run -c Release` → `System.InvalidProgramException: Common Language Runtime detected an invalid program.` at `Repro.collectThenToArray`

## The invalid IL

Release IL for `collectThenToArray` (via `ilspycmd -il`), with the two violations marked:

```
.locals init (
    [0] valuetype [FSharp.Core]Microsoft.FSharp.Core.CompilerServices.ArrayCollector`1<int32>,
    [1] class [System.Runtime]System.Collections.Generic.IEnumerator`1<class Repro/Holder>,
    [2] valuetype Repro/StructSeq,          // <-- struct-typed local
    ...
)
    ...
    IL_0011: ldloca.s 0
    IL_0013: ldloc.3
    IL_0014: ldfld valuetype Repro/StructSeq Repro/Holder::points@
    IL_0019: call instance void ArrayCollector`1<int32>::AddMany(class IEnumerable`1<!0>)
    //       ^^^ (1) an unboxed struct is passed where IEnumerable<int> is expected: no `box`
    ...
    IL_0027: ldnull
    IL_0028: stloc.2
    //       ^^^ (2) a null object reference is stored into the struct-typed local [2]
```

Both are type-safety violations: a value type is used as an interface reference without boxing,
and `ldnull` is stored into a value-type local. Either alone is enough for the JIT to refuse the
method.

The same shape appeared in `PRo3D.Base.dll` with `IndexList<V3d>` in place of `StructSeq`.

## Trigger matrix

Same struct and data, six shapes, Debug vs Release:

| # | Expression | Debug | Release |
|---|---|---|---|
| A | `Seq.collect (fun h -> h.points) >> Seq.toArray` | OK | **InvalidProgramException** |
| B | `Seq.collect (fun h -> h.points) >> Seq.toList` | OK | **InvalidProgramException** |
| C | `Seq.collect (fun h -> h.points) >> Seq.length` | OK | OK |
| D | `Seq.collect (fun h -> h.points :> IEnumerable<int>) >> Seq.toArray` | OK | OK |
| E | `[| for h in hs do yield! h.points |]` | OK | OK |
| F | `Seq.collect id >> Seq.toArray` (over `seq<StructSeq>`) | OK | **InvalidProgramException** |

So it needs all three of: a struct implementing `IEnumerable<'T>`, `Seq.collect`, and a
materializing consumer that the optimizer fuses with it. Non-fusing consumers (C) are unaffected,
and an explicit upcast in the lambda (D) avoids it.

## Workaround

Upcast the lambda's result so the fused code sees a reference type:

```fsharp
annotations |> Seq.collect (fun a -> a.points :> seq<V3d>) |> Seq.toArray
```

The comprehension form (E) works too. The upcast looks redundant — and the compiler even warns
`FS0066: This upcast is unnecessary` in the standalone repro — but it is what keeps the emitted
IL valid, so **do not remove it**.

## Environment

- .NET SDK 9.0.309 (repo pins `9.0.100` with `rollForward: latestFeature`)
- F# compiler 13.9.303-beta.25361.1, FSharp.Core 9.0.303-beta.25361.1
- Reproduced on Windows locally and on ubuntu-latest / macos-15 / windows-latest in CI, so it is
  platform-independent.

## Status

Not yet filed upstream. Worth reporting to [dotnet/fsharp](https://github.com/dotnet/fsharp) —
the repro above is self-contained and the failure is silent at compile time (no warning, no
error; only a run-time `InvalidProgramException` in optimized builds).
