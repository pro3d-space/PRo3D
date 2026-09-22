# Build.fs FAKE target graph — redundant compile cleanup (pending)

While adding Apple Silicon support we noticed the release builds recompile the
solution more than necessary. This is the analysis + a (deferred) cleanup plan.

## Confirmed redundancy

FAKE dedups the dependency *graph*, so each target runs once per invocation — the
duplicate `==>` declarations do **not** cause re-runs. The redundancy is that
several targets each launch their own `dotnet build`/`publish` and none reuse the
others' output.

Build/publish actions in `Build.fs`:
- **Compile** (`:66`) — `dotnet build src/PRo3D.sln` Release/AnyCPU → builds all
  solution projects once.
- **CompileInstruments** (`:613`) — builds `JR.Wrappers.sln` **twice**: Debug
  (`:636`) then Release (`:648`).
- **CopyToElectron** (`:365`) — 1× `dotnet publish PRo3D.Viewer` for the host RID.
- **Publish** (`:451`) — 4× `dotnet publish`: viewer `win-x64` (`:468`), snapshots
  `win-x64` (`:482`), viewer `osx-x64` (`:497`), viewer `osx-arm64` (`:513`).

Entry points (deduped graph):
- **`PublishToElectron`** (CI deploy jobs) pulls in `Compile`,
  `CompileInstruments`, `CopyToElectron`, etc. ⇒ shared F# graph compiled in
  `Compile` (AnyCPU) **and again** in the RID publish = 2× full compile, plus
  JR.Wrappers 2×.
- **`Publish`** (→ `GithubRelease`) ⇒ shared graph compiled AnyCPU + `win-x64` +
  `osx-x64` + `osx-arm64` = ~4× full compile, plus JR.Wrappers 2×.

## Realistic ceiling

The **3 per-RID publishes are irreducible** — releasing for win-x64 + osx-x64 +
osx-arm64 must compile the IL once per RID. The only redundant *full F# solution*
compile is the AnyCPU **`Compile`**. So the realistic win is **one full solution
compile removed per release**, plus small/cosmetic cleanups.

## Cleanup steps (when picked up)

1. **Drop the throwaway AnyCPU `Compile` from the publish/electron chains** — the
   one full build we can spare. `Compile`'s output is not consumed by the
   publishes (they recompile per-RID; natives come from
   `extractNativeDependenciesInFolder`, not the resource-embedded `bin/Release`
   assemblies). Remove the `AddNativeResources`-via-`Compile` prerequisite from
   the publish/electron chains; keep `CompileInstruments → CopyJRWrapper →
   Credits` (the viewer references `lib/JR.Wrappers.dll`). Keep
   `Compile`/`AddNativeResources`/`Pack` intact for the nupkg/`Pack` path.
   *Verify the publish output (viewer + GLVM/Vulkan/FreeImage/DevIL natives, win
   vcruntime) is unchanged.*
2. **Drop the redundant Release build in `CompileInstruments`** (`:648-658`) —
   only the Debug output feeds `CopyJRWrapper` → `lib/JR.Wrappers.dll`. Small
   C++-wrapper sln, minor saving. *Verify nothing reads
   `bin/Release/.../JR.Wrappers.*`.*
3. **Cosmetic:** delete the duplicate edge declarations at `:818-819` (identical
   to `:669-670`); declare each edge once.
4. **Fewer restores (not compiles):** the `win-x64` snapshots publish (`:482`)
   shares its RID with the viewer publish above and can use `NoRestore=true`.

Net: one full F# solution compile saved (1), one small JR.Wrappers build saved
(2), plus tidy-up. Per-RID publishes stay.

## Side note (already done elsewhere)

arm64 native deps were verified present in the locked packages — GL
(`mac/ARM64/libglvm.dylib`), Vulkan (`libvkvm`/`libvulkan-1`), FreeImage 5.3.8
(`libFreeImage`), DevILSharp 0.2.20 (DevIL/ILU/ILUT + codecs). Only JR.Wrappers
`libCooTransformation.dylib` lacks an arm64 build (separate task; build is
crash-safe without it).
