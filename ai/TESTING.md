# Testing PRo3D

Two complementary mechanisms. Use **both** when a change touches viewer
behavior: Expecto proves the model/math, Playwright proves the pixels.

## Expecto (`src/Tests`)

Unit and integration tests over models, update functions, SPICE math, parsers,
exporters. Run from the Release build the FAKE script produces:

```
dotnet bin/Release/net9.0/Tests.dll                       # whole suite (~10 s + fixtures)
dotnet bin/Release/net9.0/Tests.dll --filter-test-list <substring>
```

- The suite is **sequenced** (SPICE kernel state is process-global); new test
  lists are registered in `src/Tests/Program.fs` — read its ordering comments
  before placing anything that loads kernels.
- Kernel-, GPU- or fixture-dependent tests **self-skip** when prerequisites
  are missing; pure model tests always run.
- Pattern for update-function tests: build models directly
  (`{ SomeApp.initial with … }`), call `update`, assert on the record — see
  `ProjectedImageStackTest.fs`.

## Playwright (`tests-ui/`) — drive the real app

End-to-end tests that launch the real viewer (`--server` mode), operate its
browser UI, and verify results with **screenshots of the rendered 3D view**.
**Do not be shy about using this.** A green build plus theory is not evidence
that a viewer change works; a Playwright run is. It is the fastest way to
answer "does the surface still render", "does my UI action reach the model",
"does this effect actually draw" — and failures come with screenshots you can
look at instead of speculating.

Read **[../tests-ui/README.md](../tests-ui/README.md)** before writing or
debugging a spec — it documents the mechanics that are NOT guessable:

- server mode exits when stdin closes (the launcher keeps a pipe open);
- every docking panel is its own page (`?page=gis`, `?page=render`);
- Electron file dialogs must be stubbed on `window.aardvark.dialog`;
- click via single-shot DOM `evaluate` (Playwright's actionability loop
  starves against the incremental UI);
- screenshot gates: the loading splash is rendered *into* the stream
  (`streamLive`), overlays pollute naive brightness checks (`litFraction`),
  and an empty view is perfectly "stable";
- **a changed surface shader recompiles for minutes on first start, during
  which surfaces are absent with no log output** — budget for it, and do not
  misdiagnose it as "my shader broke rendering".

For quick one-off questions (what does this page's DOM look like? what does
the view show right now?), write a **probe** (`tests-ui/src/probe-*.ts`,
`npx tsx src/<probe>.ts`) instead of a spec — same launcher, no test
ceremony. Screenshots land in `tests-ui/artifacts/`; read the images yourself
before drawing conclusions from pixel statistics.

Tests are machine-local (GPU + local datasets, `PRO3D_*` env vars); they are
not run in CI, which makes running them locally the only line of defense.
