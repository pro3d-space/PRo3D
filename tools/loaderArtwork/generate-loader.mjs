// Builds src/PRo3D.Viewer/resources/pro3d-loader.svg, the artwork of the render
// control boot screen (docs/LoadingScreen.md).
//
// Everything is baked into that one SVG because it is used as a CSS
// background-image, and such an SVG may not fetch anything from outside: no
// linked bitmaps, no web fonts. So the photo goes in as a data URI, the
// lettering as a mask, and the credit text as outlines.
//
//   node generate-loader.mjs [outfile]
//
// Inputs (all in the repository):
//   data/logo_pro3d_big.png            the logo, 1024x1024
//   data/fonts/Roboto Mono/*.ttf       the credit type
//   tools/loaderArtwork/aardvark-mark.json  the aardvark silhouette
//                                      (refresh with extract-aardvark-mark.mjs)
import opentype from "opentype.js";
import { PNG } from "pngjs";
import jpeg from "jpeg-js";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, "..", "..");
const out = process.argv[2] ?? path.join(repo, "src", "PRo3D.Viewer", "resources", "pro3d-loader.svg");

// ---- canvas ---------------------------------------------------------------
const W = 1160, H = 752;
const PHOTO = { x: 68, y: 36, w: 1024, h: 560 };
const CROP_Y = 300;    // the band of logo_pro3d_big.png the photo is taken from
const INSET = 46;      // how far the visible area is inset from the photo edge
const BLUR = 44;       // softness of that edge
const JPEG_QUALITY = 82;

const logo = path.join(repo, "data", "logo_pro3d_big.png");
const source = PNG.sync.read(fs.readFileSync(logo));

// ---- photo: crop the band and re-encode as JPEG ---------------------------
const photo = (() => {
    const band = Buffer.alloc(PHOTO.w * PHOTO.h * 4);
    for (let y = 0; y < PHOTO.h; y++) {
        const from = ((y + CROP_Y) * source.width) * 4;
        source.data.copy(band, y * PHOTO.w * 4, from, from + PHOTO.w * 4);
    }
    const encoded = jpeg.encode({ data: band, width: PHOTO.w, height: PHOTO.h }, JPEG_QUALITY);
    return encoded.data.toString("base64");
})();

// ---- lettering: cut the white "PRo3D" out of the same picture --------------
// it is painted again in pure white on top of the artwork, so the vignette
// cannot grey it out
const letters = (() => {
    const BOX = { x0: 70, x1: 925, y0: 125, y1: 420 }; // lettering, in crop coordinates
    const MIN_AREA = 500;                              // anything smaller is a rock highlight

    const mask = new PNG({ width: PHOTO.w, height: PHOTO.h });

    for (let y = 0; y < PHOTO.h; y++) {
        for (let x = 0; x < PHOTO.w; x++) {
            const s = ((y + CROP_Y) * source.width + x) * 4;
            const r = source.data[s], g = source.data[s + 1], b = source.data[s + 2];
            let a = 0;
            if (x >= BOX.x0 && x <= BOX.x1 && y >= BOX.y0 && y <= BOX.y1) {
                const lum = 0.299 * r + 0.587 * g + 0.114 * b;
                const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
                const sat = mx > 0 ? (mx - mn) / mx : 0;
                // the lettering is neutral white; the rocks are warm, the annotations saturated
                if (sat < 0.1) a = Math.min(1, Math.max(0, (lum - 215) / 30));
            }
            const v = Math.round(a * 255);
            const d = (y * PHOTO.w + x) * 4;
            mask.data[d] = mask.data[d + 1] = mask.data[d + 2] = v;
            mask.data[d + 3] = 255;
        }
    }

    // drop specks: a bright highlight that survived the threshold is tiny next
    // to a letter stroke
    const seen = new Uint8Array(PHOTO.w * PHOTO.h);
    const alphaAt = (i) => mask.data[i * 4];
    for (let start = 0; start < seen.length; start++) {
        if (seen[start] || alphaAt(start) === 0) continue;
        const comp = [];
        const stack = [start];
        seen[start] = 1;
        while (stack.length) {
            const p = stack.pop();
            comp.push(p);
            const px = p % PHOTO.w, py = (p - px) / PHOTO.w;
            for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
                const nx = px + dx, ny = py + dy;
                if (nx < 0 || ny < 0 || nx >= PHOTO.w || ny >= PHOTO.h) continue;
                const n = ny * PHOTO.w + nx;
                if (!seen[n] && alphaAt(n) > 0) { seen[n] = 1; stack.push(n); }
            }
        }
        if (comp.length < MIN_AREA)
            for (const p of comp) mask.data[p * 4] = mask.data[p * 4 + 1] = mask.data[p * 4 + 2] = 0;
    }

    return PNG.sync.write(mask).toString("base64");
})();

// ---- credit text ----------------------------------------------------------
const font = opentype.parse(
    fs.readFileSync(path.join(repo, "data", "fonts", "Roboto Mono", "RobotoMono-Regular.ttf")).buffer
);
const TEXT = "POWERED BY AARDVARK";
const SIZE = 34;
const TRACK = 4.5;

const advance = (ch) => (font.charToGlyph(ch).advanceWidth * SIZE) / font.unitsPerEm + TRACK;
const textWidth = [...TEXT].reduce((w, ch) => w + advance(ch), 0) - TRACK;

function textPath(x, y) {
    const glyphs = new opentype.Path();
    let cursor = x;
    for (const ch of TEXT) {
        glyphs.extend(font.getPath(ch, cursor, y, SIZE));
        cursor += advance(ch);
    }
    return glyphs.toPathData(2);
}

// ---- aardvark mark --------------------------------------------------------
const mark = JSON.parse(fs.readFileSync(path.join(here, "aardvark-mark.json"), "utf8"));
const m = mark.matrix;
const markRoot = {   // bbox of the path in the root user space of aardvark-light.svg
    x: m[4] + m[0] * mark.bbox.x,
    y: m[5] - m[0] * (mark.bbox.y + mark.bbox.height),
    w: m[0] * mark.bbox.width,
    h: m[0] * mark.bbox.height,
};

const MARK_H = 50;
const markScale = MARK_H / markRoot.h;
const markW = markRoot.w * markScale;

// ---- layout of the credit line --------------------------------------------
const GAP = 22;
const creditW = markW + GAP + textWidth;
const creditX = (W - creditW) / 2;
const baseline = 690;
const markY = baseline - MARK_H + 4;   // sit the animal on the text baseline

// scale the mark into the credit line, on top of its own matrix
const place = [
    markScale * m[0], markScale * m[1],
    markScale * m[2], markScale * m[3],
    creditX + markScale * (m[4] - markRoot.x),
    markY + markScale * (m[5] - markRoot.y),
];

const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <!--
    PRo3D loading screen artwork - generated, do not hand-edit.
    Regenerate with tools/loaderArtwork/generate-loader.mjs; see docs/LoadingScreen.md.

    Photo:  a crop of data/logo_pro3d_big.png (the PRo3D logo), embedded as a
            data URI because an SVG used as a CSS background cannot load files.
    Text:   Roboto Mono (Apache-2.0), data/fonts, converted to outlines.
    Mark:   the aardvark silhouette from Aardvark.UI's resources/aardvark-light.svg
            (aardvark-platform/aardvark.media, Apache-2.0), brand green #BED62F.
  -->
  <defs>
    <filter id="soften" x="-20%" y="-20%" width="140%" height="140%" color-interpolation-filters="sRGB">
      <feGaussianBlur stdDeviation="${BLUR}"/>
    </filter>
    <mask id="fade">
      <rect x="${PHOTO.x + INSET}" y="${PHOTO.y + INSET}" width="${PHOTO.w - 2 * INSET}" height="${PHOTO.h - 2 * INSET}"
            rx="90" fill="#fff" filter="url(#soften)"/>
    </mask>
    <radialGradient id="vignette" cx="50%" cy="50%" r="62%">
      <stop offset="35%" stop-color="#222222" stop-opacity="0"/>
      <stop offset="100%" stop-color="#222222" stop-opacity="0.85"/>
    </radialGradient>
    <mask id="wordmark">
      <image x="${PHOTO.x}" y="${PHOTO.y}" width="${PHOTO.w}" height="${PHOTO.h}"
             href="data:image/png;base64,${letters}"/>
    </mask>
  </defs>

  <!-- photo, calmed down and faded into the render control background -->
  <g mask="url(#fade)">
    <image x="${PHOTO.x}" y="${PHOTO.y}" width="${PHOTO.w}" height="${PHOTO.h}"
           href="data:image/jpeg;base64,${photo}"/>
    <rect x="${PHOTO.x}" y="${PHOTO.y}" width="${PHOTO.w}" height="${PHOTO.h}" fill="#000000" opacity="0.2"/>
    <rect x="${PHOTO.x}" y="${PHOTO.y}" width="${PHOTO.w}" height="${PHOTO.h}" fill="url(#vignette)"/>
  </g>

  <!-- the lettering again, in pure white, so the vignette does not grey it out -->
  <rect x="${PHOTO.x}" y="${PHOTO.y}" width="${PHOTO.w}" height="${PHOTO.h}" fill="#FFFFFF" mask="url(#wordmark)"/>

  <g transform="matrix(${place.map((v) => v.toFixed(5)).join(",")})" fill="#BED62F">
    <path d="${mark.d}"/>
  </g>
  <path fill="#9A9A9A" d="${textPath(creditX + markW + GAP, baseline)}"/>
</svg>
`;

fs.writeFileSync(out, svg);
console.log(`wrote ${out} (${(svg.length / 1024).toFixed(0)} KB)`);
