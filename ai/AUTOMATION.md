# Automation: Command Line, Remote API, Snapshots & Provenance

PRo3D can run beyond the interactive window: launched with arguments, driven over HTTP, rendered headlessly in batch, and recorded for reproducibility. These features are what make PRo3D usable from scripts and notebooks (see `provex/` and `notebooks/`).

---

## Command Line

Arguments are parsed at startup in `src/PRo3D.Viewer/Program.fs` via the parsers under `src/PRo3D.Viewer/CommandLine.fs` and `src/PRo3D.Viewer/CommandLine/CommandLine.fs`, producing the `StartupArgs` carried on the root `Model`. Use `--help` (or `--h`) to print the authoritative list. Common flags (verify against the source — flags evolve):

| Flag | Purpose |
|------|---------|
| `--opc <path[;path…]>` / `--opcs <path[;…]>` | Load one / many OPC datasets |
| `--obj <path[;…]>` | Load OBJ meshes |
| `--scene <path>` | Load a `.pro3d` scene |
| `--snap <path>` | Load camera views (snapshot file) |
| `--asnap <path>` | Load an animation snapshot (camera sequence) |
| `--out <folder>` | Output directory for screenshots/snapshots |
| `--port <n>` | Web server port |
| `--samples <n>` | MSAA sample count |
| `--backgroundColor <c>` | Background color |
| `--defaultSpiceKernel <path>` | SPICE kernel to load at startup |
| `--excentre`, `--refsystem` | Show exploration centre / reference system |
| `--verbose` | Verbose logging |
| `--gui <mode>` | Select a dashboard/dock layout |

Startup then optionally pipelines scene/data loading into the initial model (see [ARCHITECTURE.md](ARCHITECTURE.md#app-startup--hosting)). Headless/server operation (no Aardium window) is selected via startup args and is the basis for batch rendering.

---

## Remote API

When enabled, an HTTP API is mounted by the Giraffe server alongside the UI (`src/PRo3D.Viewer/Program.fs:375`, `http.subRoute "/api" remoteApi`). It is implemented in `src/PRo3D.Viewer/RemoteApi.fs` (plus `QueriesRemoteApi.fs`), and turns messages into the running app's update loop via the `messagingMailbox`. This is how external Python/notebook clients control a live PRo3D instance (see `provex/PROVEX.MD`).

Route groups under `/api`:

| Route | Purpose |
|-------|---------|
| `/api/loadScene` | Load a scene file |
| `/api/importOpc` | Import an OPC dataset |
| `/api/saveScene` | Save the current scene |
| `/api/discoverSurfaces` | Enumerate loadable surfaces under a path |
| `/api/v2/captureSnapshot`, `/api/v2/activateSnapshot` | Trigger / activate batch snapshots |
| `/api/v2/getProvenanceGraph`, `/api/v2/provenanceGraph`, `/api/v2/provenanceGraphChanges` | Read the provenance graph (and live changes) |
| `/api/v2/importAnnotations`, `/api/v2/importAnnotationsFromGraph`, `/api/v2/getFullStateFor` | Annotation/state import & retrieval |
| `/api/integration/geojson_latlon`, `/api/integration/ws/geojson_xyz` | Export annotations as GeoJSON (lat/lon, or world-XYZ over a WebSocket) |
| `/api/queries/findAnnotation`, `/queryAnnotationAsJson`, `/queryAnnotationAsObj` | Run [queries](DOMAIN.md#queries) against surfaces and return JSON/OBJ |
| `/api/annotations/%s/points` | Per-annotation point data |

The viewer also serves `/websocket` (UI updates), `/crash.txt`, and `/minilog.txt`. A separate **remote-control** app (`RemoteControlApp.fs`) exposes `POST /shots` and `POST /platformshots` for screenshot/platform-shot capture (`Program.fs:446`).

---

## Snapshots & Headless Rendering

Two related mechanisms produce images without interactive use:

- **In-viewer snapshots** — `Snapshot-Model.fs` (+ `ScreenshotModel`, `snapshotThreads` on the root model) define camera/surface state for batch capture. Sequenced bookmarks (see [DOMAIN.md](DOMAIN.md#bookmarks--sequenced-bookmarks)) can drive a sequence of snapshots / a panorama, rendering RGB and depth. Triggered interactively, via `--snap`/`--asnap`, or via the `/api/v2/*Snapshot` endpoints.
- **`PRo3D.Snapshots`** (`src/PRo3D.Snapshots/`) — a standalone headless tool that reads snapshot JSON and renders many viewpoints without the full interactive UI. **`PRo3D.SimulatedViews`** supplies the realistic rover/instrument camera models (e.g. mission instruments) used to define those viewpoints, and underpins view planning.

The snapshot file formats mirror the camera/surface model state, so they round-trip with the same Chiron codecs as scenes.

---

## Provenance Tracking

PRo3D can record a reproducible graph of user interactions — essential for scientific workflows. See `docs/ProvenanceTracking.md`.

| File | Role |
|------|------|
| `src/PRo3D.Viewer/ProvenanceModel.fs` | `ProvenanceModel`, `PNode`/`PEdge`, the tracked `PMessage` set |
| `src/PRo3D.Viewer/ProvenanceApp.fs` | Recording logic; `updateWithProvenanceTracking` wraps the viewer update |

- The root `update` is `updateWithProvenanceTracking`, which (when enabled) records the message and a model snapshot around each `updateInternal` step (see [ARCHITECTURE.md](ARCHITECTURE.md#sub-app-composition)).
- **`ProvenanceModel`** is a graph: `nodes` (versioned snapshots) and `edges` (message transitions), with the current trail of recent messages.
- Only a curated set of interactions is tracked (`PMessage`: camera changes, finishing annotations, drawing actions, branch/create-node, scene loads) — not every internal message.
- The graph is exposed over the remote API (`/api/v2/getProvenanceGraph`, `provenanceGraphChanges`) so external tools can observe and replay sessions.

Provenance is opt-in (enabled together with the remote API at startup) since recording snapshots has a cost.

---

## See Also

- [ARCHITECTURE.md](ARCHITECTURE.md) — the update loop, `messagingMailbox`, and how remote messages enter it
- [DOMAIN.md](DOMAIN.md#queries) — what the query endpoints compute
- `../provex/PROVEX.MD`, `../provex/*.ipynb` — Python/notebook remote-control examples
- `../docs/ProvenanceTracking.md`
