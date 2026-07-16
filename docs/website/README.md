# PRo3D website

Static marketing site for PRo3D. No build step, no dependencies — plain HTML, CSS
and a small progressive-enhancement script.

```
website/
├── index.html      the whole page
├── styles.css
├── script.js       mobile nav, scroll reveal, scrollspy (site works without it)
├── .nojekyll       stops GitHub Pages running the files through Jekyll
└── logos/          images + generated favicon.png
```

## Preview locally

Open `index.html` directly, or serve it:

```sh
python -m http.server 8000 --directory website
# → http://localhost:8000
```

## Publishing to GitHub Pages

GitHub Pages can only serve from a repository **root** or a **`/docs`** folder, so
a folder named `/website` cannot be selected in the Pages settings dropdown.
Pick one of:

1. **GitHub Actions (keeps the `/website` folder).** Settings → Pages → Source →
   *GitHub Actions*, then add a workflow that uploads `./website` as the Pages
   artifact via `actions/upload-pages-artifact` and deploys it with
   `actions/deploy-pages`.
2. **Rename `website/` to `docs/`.** Settings → Pages → Source → *Deploy from a
   branch* → branch `main`, folder `/docs`. Simplest option, zero CI.
3. **Publish the folder as the root of a `gh-pages` branch**, e.g. with
   `git subtree push --prefix website origin gh-pages`.

## Still to fill in

Search the HTML for `TODO`:

- The four example-dataset download links (`href="#"`).
- The Discord invite URL (`href="#"`).
- `#tutorials` holds three placeholder cards. A commented-out `<article class="video-card">`
  block above the grid shows the markup for a real embedded video with a description.
