# Loader artwork

Generates [`src/PRo3D.Viewer/resources/pro3d-loader.svg`](../../src/PRo3D.Viewer/resources/pro3d-loader.svg),
the image on the render control's boot screen. What it is and how it is used:
[docs/LoadingScreen.md](../../docs/LoadingScreen.md).

The SVG is checked in, so you only need this when the artwork itself changes.
**Do not hand-edit the SVG** — it is mostly base64.

```bash
npm install
npm run generate          # rewrites the resource in place
```

`npm install` also pulls a Playwright browser (`npx playwright install chromium`
if it is missing); the script uses it to crop the logo and encode it as JPEG.

## What it builds from

| Input | Used for |
|-------|----------|
| `data/logo_pro3d_big.png` | the photo (a 1024x560 band of it) and the "PRo3D" lettering, which is cut out as a mask |
| `data/fonts/Roboto Mono/RobotoMono-Regular.ttf` | "POWERED BY AARDVARK", converted to outlines |
| `aardvark-mark.json` | the aardvark silhouette and its transform |

Everything is embedded, because an SVG used as a CSS `background-image` may not
load anything from outside — no linked bitmaps, no web fonts.

Layout constants (canvas size, crop band, vignette, credit line) sit at the top
of `generate-loader.mjs`. If you swap in a differently framed logo, the
lettering box `BOX` in the mask step has to follow.

## Refreshing the aardvark silhouette

`aardvark-mark.json` is extracted from Aardvark.UI's own `aardvark-light.svg`,
which ships as an embedded resource, not as a file. From an aardvark.media
checkout:

```bash
git show 5.7.3:src/Aardvark.UI/resources/aardvark-light.svg > aardvark-light.svg
node extract-aardvark-mark.mjs aardvark-light.svg
```
