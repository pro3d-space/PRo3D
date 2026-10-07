/**
 * Compose captioned figures for the docs.
 *
 * The panels are laid out as HTML and screenshotted rather than blitted together with a
 * hand-rolled bitmap font: tests-ui ships no font package, and a browser is already
 * running to drive the viewer, so this costs nothing and gives real type.
 */
import type { BrowserContext } from "@playwright/test";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

/** the viewer's clear colour; see ai/TESTING.md on why this must not be inferred */
const BG = 34;

/**
 * Crop every frame to one common box around the body, with `pad` px of margin.
 *
 * The box is the union over all frames, so a filmstrip does not jitter as the lit
 * region moves. Frames are rewritten in place next to the originals, with `-crop`
 * appended, and the new paths returned along with the common size.
 */
export function cropToContent(files: string[], pad = 40) {
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    const imgs = files.map((f) => PNG.sync.read(fs.readFileSync(f)));
    for (const img of imgs)
        for (let y = 0; y < img.height; y++)
            for (let x = 0; x < img.width; x++) {
                const i = (img.width * y + x) * 4;
                const v = (img.data[i] + img.data[i + 1] + img.data[i + 2]) / 3;
                if (Math.abs(v - BG) <= 6) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
    if (!isFinite(x0)) throw new Error("cropToContent: no body pixels in any frame");

    const w0 = imgs[0].width, h0 = imgs[0].height;
    x0 = Math.max(0, x0 - pad); y0 = Math.max(0, y0 - pad);
    x1 = Math.min(w0 - 1, x1 + pad); y1 = Math.min(h0 - 1, y1 + pad);
    const w = x1 - x0 + 1, h = y1 - y0 + 1;

    const out = files.map((f, k) => {
        const src = imgs[k];
        const dst = new PNG({ width: w, height: h });
        for (let y = 0; y < h; y++)
            for (let x = 0; x < w; x++) {
                const s = ((y + y0) * src.width + (x + x0)) * 4, t = (y * w + x) * 4;
                dst.data[t] = src.data[s];
                dst.data[t + 1] = src.data[s + 1];
                dst.data[t + 2] = src.data[s + 2];
                dst.data[t + 3] = 255;
            }
        const p = f.replace(/\.png$/, "-crop.png");
        fs.writeFileSync(p, PNG.sync.write(dst));
        return p;
    });
    return { files: out, width: w, height: h };
}

export interface Panel {
    /** PNG on disk; it must sit in `dir` */
    file: string;
    caption: string;
    /** right-aligned, for a measured value */
    note?: string;
}

export interface FigureOptions {
    cols: number;
    /** panel size in px; panels are assumed uniform */
    width: number;
    height: number;
    /** where the panel PNGs live and the html is written */
    dir: string;
    gap?: number;
}

export async function compose(
    ctx: BrowserContext,
    panels: Panel[],
    opts: FigureOptions
): Promise<Buffer> {
    const { cols, width: w, height: h, dir } = opts;
    const gap = opts.gap ?? 10;
    const rows = Math.ceil(panels.length / cols);

    for (const p of panels)
        if (!fs.existsSync(p.file)) throw new Error(`figure panel missing: ${p.file}`);

    const html = `<!doctype html><meta charset="utf-8">
<style>
  :root { color-scheme: dark }
  * { margin: 0; padding: 0; box-sizing: border-box }
  body { background: #111 }
  .grid {
    display: grid; grid-template-columns: repeat(${cols}, ${w}px);
    gap: ${gap}px; padding: ${gap}px; width: max-content;
  }
  figure { position: relative; width: ${w}px; height: ${h}px; overflow: hidden }
  img { display: block; width: ${w}px; height: ${h}px }
  figcaption {
    position: absolute; left: 0; right: 0; bottom: 0;
    padding: 14px 18px 13px;
    background: linear-gradient(to top, rgba(0,0,0,.85), rgba(0,0,0,.55) 60%, transparent);
    font: 500 19px/1.35 "Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif;
    color: #fff; display: flex; align-items: baseline; gap: .6em;
  }
  .note {
    margin-left: auto; opacity: .72; font-size: 16px; font-weight: 400;
    font-variant-numeric: tabular-nums;
  }
</style>
<div class="grid">
${panels
    .map(
        (p) => `  <figure>
    <img src="${path.basename(p.file)}">
    <figcaption>${p.caption}${p.note ? `<span class="note">${p.note}</span>` : ""}</figcaption>
  </figure>`
    )
    .join("\n")}
</div>`;

    const file = path.join(dir, "figure.html");
    fs.writeFileSync(file, html);

    const page = await ctx.newPage();
    await page.setViewportSize({
        width: w * cols + gap * (cols + 1),
        height: h * rows + gap * (rows + 1),
    });
    await page.goto("file:///" + file.replace(/\\/g, "/"));
    await page.waitForLoadState("networkidle");
    const broken = (await page.evaluate(
        `Array.from(document.images).filter(function(i){ return !i.complete || i.naturalWidth === 0; })
             .map(function(i){ return i.getAttribute("src"); })`
    )) as string[];
    if (broken.length) throw new Error(`figure images failed to load: ${broken.join(", ")}`);
    await page.evaluate("document.fonts.ready");
    const buf = await page.locator(".grid").screenshot();
    await page.close();
    return buf;
}
