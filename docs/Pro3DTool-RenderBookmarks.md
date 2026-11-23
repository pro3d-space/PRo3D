# pro3d-tool render-bookmarks

Opens a `.pro3d` scene without a window and renders the camera bookmarks prepared in
it: one PNG per bookmark, as the viewer shows that bookmark in a window of the requested
size. Surfaces, annotations and overlays are drawn by the same scene graph that
`PRo3D.Snapshots` uses.

You can also fix the OPC **level of detail** to one level everywhere. This is a test
hook: renders of the same bookmark at different levels can be compared directly.

The same rendering is available as a library, **`PRo3D.SceneRender`**, for tests and
scripts that run in-process ([below](#in-process-pro3dscenerender)).

> Phase 1 (#871) renders **plain bookmarks** (the bookmark panel). Sequenced bookmarks,
> which also carry scene state, frustum and SPICE observation time, come in phase 2.

## Usage

```
pro3d-tool render-bookmarks --scene site.pro3d                      # every bookmark, 1920x1080
pro3d-tool render-bookmarks --scene site.pro3d --list               # name and GUID of each bookmark
pro3d-tool render-bookmarks --scene site.pro3d --bookmarks "Crater rim" "Outcrop*" --width 1024 --height 768
pro3d-tool render-bookmarks --scene site.pro3d --bookmarks "*" --lod-level 3 --out lod3
pro3d-tool render-bookmarks --job render.json
```

| Option | Default | Meaning |
|---|---|---|
| `--scene <file>` | — | The scene to open. Required unless the job file names it. |
| `--bookmarks <p> …` | `*` | Bookmarks to render, separated by spaces: names, names with `*` / `?` wildcards, or GUIDs. Case-sensitive. **Quote** patterns in a shell, otherwise the shell expands `*` itself. |
| `--out <dir>` | `<scene>-renders` beside the scene | Output directory. |
| `--width`, `--height` | 1920, 1080 | Image size. The scene's field of view is kept and the aspect ratio follows the size, as in a viewer window of that size. |
| `--lod-level <n>` | the viewer's | Fix the OPC level of detail ([below](#fixed-level-of-detail)). |
| `--job <file>` | — | JSON job file ([below](#job-files)). Options given on the command line override its top level. |
| `--list` | — | Only list the scene's bookmarks and exit. |

Every bookmark that a pattern selects is rendered once, in the order of the bookmark
panel. A pattern that selects nothing is an error, and the error message lists the
available bookmarks. This is deliberate: a typo should not quietly produce fewer images.

### Output

- `<bookmark name><suffix>.png` for each bookmark and pass. Characters that a file system
  rejects, and whitespace, become `_`. When two bookmarks would get the same file name,
  the first 8 characters of each bookmark's GUID are appended.
- `renders.json`, a manifest of the images written:

```json
{
  "scene": "C:\\data\\site.pro3d",
  "images": [
    { "bookmark": "Crater rim", "key": "…", "file": "Crater_rim_lod3.png", "width": 1920, "height": 1080, "lodLevel": 3 }
  ]
}
```

The exit code is 0 on success and 1 on any error: a scene that won't load, a bookmark
pattern that matches nothing, or a bad job file.

## Job files

A job file holds the same settings as the command line, and optionally several
**passes**. Each pass renders its own set of bookmarks with its own size and level.

```jsonc
{
  "scene": "site.pro3d",          // relative paths are relative to the job file
  "out": "renders",
  "bookmarks": [ "*" ],           // a single string is fine too
  "width": 1280,
  "height": 720,
  "passes": [
    { "lodLevel": 0 },
    { "lodLevel": 2 },
    { "lodLevel": 4, "bookmarks": [ "Crater rim" ], "width": 3840, "height": 2160 },
    { "suffix": "_viewer" }       // no lodLevel: the viewer's level of detail
  ]
}
```

- **Precedence:** command line, then the job file's top level, then the defaults. Each
  pass inherits the top level and can override `bookmarks`, `width`, `height`,
  `lodLevel` and `suffix`.
- **`suffix`** is appended to every file name of the pass. It defaults to `_lod<n>` when
  the pass fixes a level, and to nothing otherwise. If two passes would write the same
  file, the job is refused before anything is rendered.
- Comments and trailing commas are allowed. Unknown keys are errors, so a misspelt
  `lodlevel` cannot silently fall back to the default.
- Without `passes`, the top level is a single pass.

## Fixed level of detail

OPC surfaces are quadtrees of patches. Level 0 is the root patch, and every refinement
adds one level. Normally the viewer chooses, patch by patch, how deep to go from the
patch's distance and screen size. `--lod-level n` replaces that choice: every hierarchy
is refined to exactly level `n` everywhere. Branches that are shallower than `n` end at
their leaves.

Typical uses:
- Testing a rendering override at a known, reproducible patch set, independent of the
  camera.
- Comparing levels: render the same bookmarks in several passes, one per level.

Two caveats:
- The fixed level ignores visibility. A deep level loads the *whole* surface at that
  resolution, including patches that are out of view. Large OPCs at their deepest levels
  can need a lot of memory and time.
- Patches load synchronously, so each image is complete when it is written. There is no
  settling time, and rendering the same bookmark twice gives the same image.

Implementation: `LodOverride` in `src/PRo3D.Core/Surface/Surface.Sg.fs` wraps the
viewer's level-of-detail decider. The wrapper reads the override with the adaptive
token, so switching levels re-filters the patch trees without rebuilding the scene graph.

## In process: PRo3D.SceneRender

```fsharp
open PRo3D.SceneRender

Aardvark.Init()
use app = new OpenGlApplication()                 // the host owns the GL runtime
use renderer = new SceneRenderer(app.Runtime, "site.pro3d")

for b in renderer.Bookmarks do
    let image = renderer.Render(b, { size = V2i(1024, 768); lodLevel = Some 2 })
    image.Save(sprintf "%s.png" b.name)

// or a whole job, exactly what the verb does
JobFile.load "render.json"
|> Result.bind JobFile.toJob
|> Result.bind (RenderJob.run (printfn "%s") renderer)
```

- `SceneRenderer(runtime, signature, scene)` loads the scene through the viewer's own
  update function. `Render` puts the camera where clicking the bookmark in the bookmark
  panel ends up (the viewer flies there with an animation, which only a window drives),
  then renders one frame offscreen. The two-argument constructor uses the
  framebuffer signature of `PRo3D.Snapshots` (RGBA8, depth-stencil, 8× MSAA). Tests pass
  their own runtime and signature (`Render.context`).
- `Bookmarks.ofGroups`, `Bookmarks.select` and `Bookmarks.fileNames` list a scene's
  bookmarks, select them by pattern, and name their files. `JobFile` parses and merges
  job files. `RenderJob.plan` and `RenderJob.run` resolve and render a job.
- Render from the thread that owns the GL runtime. The level-of-detail override is
  process-wide. `Render` sets it for every image, and disposing the renderer resets it.

Tests: `src/Tests/SceneRenderTests.fs`. They need `PRO3D_TEST_DATA` (the Dimorphos
projection scene) and a GL context.

## Relation to PRo3D.Snapshots

`PRo3D.Snapshots.exe --asnap` also renders bookmarks, but only from a separate batch file
of copied bookmark records. It runs only as an executable, starts a web server, and waits
for a key press at the end. `render-bookmarks` reads the bookmarks from the scene itself,
and its library can be called in-process.

Phase 2 is meant to move the snapshot tool's bookmark path, and the tests that run
`PRo3D.Snapshots.exe` (`SnapshotSunLightingTest`), onto `PRo3D.SceneRender`.
