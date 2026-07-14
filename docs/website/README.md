# PRo3D website

The public site for PRo3D, served by GitHub Pages from the `docs/` folder.

**The Markdown in `docs/*.md` is the single source of truth.** The feature and
development pages on the site are generated from those files — there is no second
copy of the text to keep in sync. Edit the Markdown, rebuild, done.

## Layout

```
docs/
├── index.html            GENERATED — homepage (do not edit)
├── .nojekyll             stops Pages running the files through Jekyll
├── *.md                  the documentation, and the source of truth
├── images/               images referenced by those .md files
└── website/
    ├── package.json
    ├── build/
    │   ├── manifest.mjs      ← choose which docs appear, and in which section
    │   ├── build.mjs         the generator
    │   └── templates/        index.html + doc.html page shells
    ├── assets/               styles.css, script.js, logos/
    └── pages/            GENERATED — one .html per published document
```

Everything marked GENERATED is produced by the build and committed, because
Pages serves `docs/` as plain static files and cannot run a build itself.

## Build

```sh
cd docs/website
npm ci
npm run build     # regenerate docs/index.html and docs/website/pages/
npm run serve     # preview at http://localhost:8000
```

`npm run check` verifies the committed HTML matches the Markdown without writing
anything. CI runs it on every pull request, so a doc edit that was committed
without a rebuild fails the check.

On pushes to `main`, the `Website` workflow rebuilds and commits the result
automatically.

## Choosing what gets published

Open [`build/manifest.mjs`](build/manifest.mjs). Each section lists the documents
it contains:

```js
{
  id: "features",
  eyebrow: "Features",
  title: "What PRo3D can do",
  layout: "grid",              // "grid" = thumbnails, "compact" = text only
  docs: [
    { file: "CrossSections.md", title: "Cross Sections" },
    { file: "Contour-Lines.md", title: "Contour Lines" },
  ],
}
```

Add a line to publish a document, remove it to unpublish. Adding a section means
appending to the array and dropping a matching placeholder — a section with
`id: "tooling"` is injected at `{{TOOLING_SECTION}}` — into
`build/templates/index.html`.

### What is read from the Markdown automatically

| Tile field | Where it comes from |
| --- | --- |
| title | the document's first heading (override with `title:`) |
| blurb | the first line of the first paragraph, minus a `Synopsis:` / `Summary:` prefix |
| thumbnail | the first image in the document; docs with no image get a monogram tile |

Only override a field when the file cannot give a good answer — `CrossSections.md`
opens with the heading `# Synopsis`, which is a useless tile title, so the
manifest supplies one.

### How the generator rewrites a document

- Heading levels are normalised so the shallowest heading becomes `<h2>` under
  the page's `<h1>`. A leading heading that merely restates the title is dropped.
- `images/foo.png` is rewritten to resolve from `docs/website/pages/`.
- A link to another published doc (`./Feature-Multitexture.md`) becomes a link to
  its generated page.
- A link to anything else — source code, an unpublished doc — becomes a link to
  the file on GitHub, keeping `#L69` line anchors intact.
- Headings get anchor ids and feed the on-page table of contents.

## Known content issues

Pre-existing problems in the Markdown, surfaced as build warnings:

- `GisView.md` references `images/editEntities.png`, which does not exist.
- `CrossSections.md` links to `../profileDrawing/README.md`, which does not exist.

## Still to fill in

Search `build/templates/index.html` for `TODO`:

- the four example-dataset download links (`href="#"`)
- the Discord invite URL (`href="#"`)
- `#tutorials` holds three placeholder cards; a commented-out `video-card` block
  above the grid shows the markup for a real embedded video

## Publishing

GitHub Pages can only serve from a repository root or a `/docs` folder, which is
why the site lives in `docs/`. Settings → Pages → Source → *Deploy from a branch*
→ branch `main`, folder `/docs`.
