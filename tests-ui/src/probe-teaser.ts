/**
 * Teaser collage for docs/MultiImageProjection.md.
 *
 * Four panels, all of the same body at the same distance:
 *   1. the DRACO mosaic alone, from a direction where most of it is unobserved (black)
 *   2. the same view with the AFC stack projected, filling it
 *   3. a second direction, stack projected
 *   4. the hovered image's projector frustum (green wireframe) over the terrain
 *
 * The camera for each panel is a BOOKMARK of the scene, written verbatim into the
 * scene's top-level cameraView. A bookmark stores the whole [Sky, Location, Forward,
 * Up, Right] view, so this reproduces a viewpoint the user actually chose. Do not
 * rebuild the view as "position + look at the origin": that discards the saved
 * orientation and reframes the shot.
 *
 * Distance matters. These bookmarks sit 198.8 m from the centre of a body whose
 * spherical radius is 77.2 m, so it subtends ~46 deg and fits inside the scene's
 * 60 deg fov (focal 10.25). An earlier version of this probe shot from 136.6 m, where
 * the body subtends ~69 deg and overflows the frame -- that is what made those panels
 * extreme close-ups of smeared terrain.
 *
 * Run: SCENE=<path to .pro3d with bookmarks> npx tsx src/probe-teaser.ts
 */
import { chromium, Page } from "@playwright/test";
import { launchPro3d, config, surfaceShadersReady } from "./pro3d";
import { litFraction, streamLive, diffPng } from "./image";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

const outDir = path.resolve(__dirname, "..", "..", "docs", "images");
const work = path.resolve(__dirname, "..", "artifacts", "teaser");

const IMAGES = (process.env.PRO3D_TEASER_IMAGES ??
    "AFC1_DRACO2_20270321_140000.png,AFC1_DRACO2_20270321_170000.png," +
    "AFC1_DRACO2_20270321_200000.png,AFC1_DRACO2_20270321_230000.png").split(",");

// ---------------------------------------------------------------- measurement

/** Background is the render div's #222222 (34), NOT pure black -- which is the only
 *  reason an unobserved (black) body region can be told apart from empty space.
 *
 *  It has to be this fixed constant. Deriving it from the frame fails in exactly the
 *  panel this teaser exists for: sampling a corner breaks once the black body covers
 *  that corner, and taking the most common value breaks too, because when most of the
 *  body is unobserved, black IS the most common value -- both then classify the black
 *  body as background and report the body as absent. That is what produced the
 *  "bodyPixels=9501, essentially empty" reading that sent an earlier session chasing a
 *  rendering bug that was never there. */
const BG = 34;

function stats(img: PNG, left: number, right: number) {
    const bg = BG;
    let n = 0, body = 0, dark = 0, green = 0, atBg = 0;
    for (let y = 0; y < img.height; y++)
        for (let x = left; x < img.width - right; x++) {
            const i = (img.width * y + x) * 4;
            const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
            if (g > 120 && r < 90 && b < 90) green++;
            n++;
            const v = (r + g + b) / 3;
            if (Math.abs(v - bg) <= 6) { atBg++; continue; }
            body++;
            if (v < 25) dark++;
        }
    return {
        bg,
        coverage: n ? body / n : 0,
        darkOfBody: body ? dark / body : 0,
        greenPixels: green,
        /** share of the frame actually sitting at #222222. If this is ~0 the constant
         *  is wrong for this build and every other number here is meaningless, so the
         *  caller checks it rather than trusting a plausible-looking coverage. */
        bgFraction: n ? atBg / n : 0,
    };
}

function measure(buf: Buffer) { return stats(PNG.sync.read(buf), 0, 0); }

// ---------------------------------------------------------------- page helpers

/** Hide the DOM overlays (HUD, tool strip, colour bar) so a panel is just the rendered
 *  frame. They are ordinary absolutely-positioned DOM sitting over img.rendercontrol,
 *  anywhere in the document -- walking up only from the img's parent misses the HUD,
 *  so sweep the whole body and keep just the ancestors of the render control. */
async function hideChrome(page: Page) {
    const hidden = await page.evaluate(
        `(function(){
            var img = document.querySelector("img.rendercontrol");
            if (!img) return -1;
            var n = 0;
            Array.prototype.forEach.call(document.body.querySelectorAll("*"), function(e){
                if (e === img || e.contains(img)) return;
                var cs = getComputedStyle(e);
                if (cs.position === "absolute" || cs.position === "fixed") {
                    e.style.display = "none";
                    n++;
                }
            });
            return n;
        })()`
    );
    if (hidden < 0) throw new Error("hideChrome: no img.rendercontrol");
    return hidden;
}

/** the surface on screen and two near-identical frames in a row */
async function settled(page: Page, name: string): Promise<Buffer> {
    const t0 = Date.now();
    let shot = await page.screenshot();
    while ((!streamLive(shot) || litFraction(shot) < 0.003) && Date.now() - t0 < 420_000) {
        await page.waitForTimeout(3000);
        shot = await page.screenshot();
    }
    let prev = shot;
    for (let i = 0; i < 40; i++) {
        await page.waitForTimeout(900);
        const cur = await page.screenshot();
        if (diffPng(prev, cur).changedFraction < 0.001) { prev = cur; break; }
        prev = cur;
    }
    fs.writeFileSync(path.join(work, name), prev);
    return prev;
}

async function clickRowIcon(gis: Page, name: string, icon: string) {
    const r = await gis.evaluate(
        `(function(){
            var matches = Array.from(document.querySelectorAll("*")).filter(function(e){
                return (e.textContent || "").trim() === ${JSON.stringify(name)}; });
            var deepest = matches.filter(function(e){
                return !Array.from(e.children).some(function(c){ return matches.indexOf(c) >= 0; }); });
            if (deepest.length === 0) return "row not found";
            var el = deepest[0];
            while (el) {
                var row = el.nextElementSibling;
                var box = row ? row.querySelector("i.${icon}.icon") : null;
                if (box) { box.click(); return "clicked"; }
                el = el.parentElement;
            }
            return "no i.${icon}.icon in row";
        })()`
    );
    if (r !== "clicked") throw new Error(`${icon} on ${name}: ${r}`);
}

/** Hover a library row with a REAL mouse move, starting from OUTSIDE it.
 *
 *  Both halves matter. The original dispatched `new MouseEvent("mouseenter",
 *  { bubbles: false })`, which never reaches aardvark.media's delegated listener. The
 *  obvious fix -- move the mouse to the row -- still fails on its own, because a row is
 *  the full width of the panel (~1092 px): nudging from `x - 40` to `x` never leaves it,
 *  so no `mouseenter` fires and the frustum silently stays off. Leave the row first. */
async function hoverRow(gis: Page, name: string, others: string[]): Promise<boolean> {
    // The row element is the one carrying onMouseEnter. Finding it by "nearest ancestor
    // with an inline border" picks the wrong node: a stack row sets only border-bottom,
    // so the walk climbs past it to the container shared by ALL rows, and then every
    // image hovers the same element -- four byte-identical frames, which is how this was
    // caught. Require instead that the row contains its own name and no other image's.
    const box = await gis.evaluate(
        `(function(){
            var name = ${JSON.stringify(name)}, others = ${JSON.stringify(others)};
            var matches = Array.from(document.querySelectorAll("*")).filter(function(e){
                return (e.textContent || "").trim() === name; });
            var deepest = matches.filter(function(e){
                return !Array.from(e.children).some(function(c){ return matches.indexOf(c) >= 0; }); });
            for (var i = 0; i < deepest.length; i++) {
                var el = deepest[i];
                while (el && el !== document.body) {
                    var txt = el.textContent || "";
                    var hasStyle = el.style && el.style.cssText.indexOf("border") >= 0;
                    var unique = txt.indexOf(name) >= 0 && !others.some(function(o){
                        return o !== name && txt.indexOf(o) >= 0; });
                    if (hasStyle && unique && el.getBoundingClientRect().height > 8) {
                        el.scrollIntoView({ block: "center" });
                        var r = el.getBoundingClientRect();
                        return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
                    }
                    if (!unique) break;   // climbed into a shared container: try next match
                    el = el.parentElement;
                }
            }
            return null;
        })()`
    ) as { x: number; y: number } | null;
    if (!box) return false;
    await gis.mouse.move(5, 5);                            // leave whatever row we were on
    await gis.waitForTimeout(500);
    await gis.mouse.move(box.x, box.y, { steps: 6 });      // ... and enter this one
    await gis.waitForTimeout(900);
    return true;
}

async function selectBeside(gis: Page, label: string, option: string) {
    const r = await gis.evaluate(
        `(function(){
            var el = Array.from(document.querySelectorAll("*")).find(function(e){
                return (e.textContent || "").trim() === ${JSON.stringify(label)}; });
            var sel = el && el.parentElement ? el.parentElement.querySelector("select") : null;
            if (!sel) return "no select beside ${label}";
            var opt = Array.from(sel.options).find(function(x){
                return x.textContent.trim() === ${JSON.stringify(option)}; });
            if (!opt) return "no option";
            sel.value = opt.value;
            sel.dispatchEvent(new Event("change", { bubbles: true }));
            return "ok";
        })()`
    );
    if (r !== "ok") throw new Error(`${label} -> ${option}: ${r}`);
}

/** Import the AFC folder and put the projection settings in the state the doc
 *  prescribes: Orientation Source MBI, Transfer Function off (it false-colours the
 *  body and hides exactly the contrast this teaser is about). */
async function setUpProjection(ctx: import("@playwright/test").BrowserContext, url: string): Promise<Page> {
    const gis = await ctx.newPage();
    await gis.setViewportSize({ width: 1100, height: 1000 });
    await gis.goto(url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await gis.locator("text=Import Directory").first().waitFor({ timeout: 60_000 });
    await gis.evaluate(
        `(function(){ window.aardvark = window.aardvark || {};
           window.aardvark.dialog = { showOpenDialog: function(){
             return Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(config.imageDir.replace(/\\/g, "/"))}] }); } }; })()`
    );
    await gis.locator("text=Import Directory").first().click();
    await gis.locator(`text=${IMAGES[0]}`).first().waitFor({ timeout: 120_000 });
    await gis.locator("text=Projection Settings").first().click();
    await gis.waitForTimeout(1200);
    await selectBeside(gis, "Orientation Source:", "MbiBased");
    await gis.evaluate(
        `(function(){
            var el = Array.from(document.querySelectorAll("*")).find(function(e){
                return (e.textContent || "").trim() === "Transfer Function:"; });
            var box = el && el.parentElement ? el.parentElement.querySelector("i") : null;
            if (box) box.click();
            return "ok";
        })()`
    );
    await gis.waitForTimeout(1500);
    return gis;
}

// ---------------------------------------------------------------- collage

/** Compose the four panels with real type.
 *
 *  The panels are laid out as HTML and screenshotted, rather than blitted together
 *  with a hand-rolled bitmap font: there is no font package in tests-ui, and the
 *  browser that renders the panels is already running. */
async function collageHtml(
    ctx: import("@playwright/test").BrowserContext,
    panels: Array<{ file: string; caption: string; note: string }>,
    w: number,
    h: number
): Promise<Buffer> {
    const html = `<!doctype html><meta charset="utf-8">
<style>
  :root { color-scheme: dark }
  * { margin: 0; padding: 0; box-sizing: border-box }
  body { background: #111; }
  .grid {
    display: grid; grid-template-columns: ${w}px ${w}px; gap: 10px;
    padding: 10px; width: max-content;
  }
  figure { position: relative; width: ${w}px; height: ${h}px; overflow: hidden; }
  img { display: block; width: ${w}px; height: ${h}px; }
  figcaption {
    position: absolute; left: 0; right: 0; bottom: 0;
    padding: 14px 18px 13px;
    background: linear-gradient(to top, rgba(0,0,0,.85), rgba(0,0,0,.55) 60%, transparent);
    font: 500 19px/1.35 "Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif;
    color: #fff; display: flex; align-items: baseline; gap: .6em;
  }
  .n {
    font-variant-numeric: tabular-nums; font-weight: 600;
    color: #fff; opacity: .55; font-size: 17px;
  }
  .note { margin-left: auto; opacity: .72; font-size: 16px; font-weight: 400; }
</style>
<div class="grid">
${panels.map((p, i) => `  <figure>
    <img src="${path.basename(p.file)}">
    <figcaption><span class="n">${i + 1}</span>${p.caption}<span class="note">${p.note}</span></figcaption>
  </figure>`).join("\n")}
</div>`;
    const file = path.join(work, "collage.html");
    fs.writeFileSync(file, html);

    const page = await ctx.newPage();
    await page.setViewportSize({ width: w * 2 + 30, height: h * 2 + 30 });
    await page.goto("file:///" + file.replace(/\\/g, "/"));
    await page.waitForLoadState("networkidle");
    await page.evaluate("document.fonts.ready");
    const buf = await page.locator(".grid").screenshot();
    await page.close();
    return buf;
}

// ---------------------------------------------------------------- main

function parseVec(s: string): number[] {
    return s.replace(/[[\]]/g, "").split(",").map((x) => Number(x.trim()));
}

/** The same bookmark, `k` times further out.
 *
 *  Safe only because these bookmarks are radial: Forward is -Location/|Location|, so
 *  scaling Location by k > 0 leaves Forward, Up, Right and Sky pointing exactly where
 *  they did and the camera still looks at the body centre. It is NOT the same thing as
 *  rebuilding the view as "new position, look at the origin", which throws the saved
 *  orientation away. Asserted below rather than assumed. */
function pullBack(view: string[], k: number): string[] {
    const loc = parseVec(view[1]);
    const fwd = parseVec(view[2]);
    const d = Math.hypot(...loc);
    const radial = Math.hypot(fwd[0] + loc[0] / d, fwd[1] + loc[1] / d, fwd[2] + loc[2] / d);
    if (radial > 1e-6)
        throw new Error(`bookmark is not radial (|forward + loc/|loc|| = ${radial}); cannot pull back safely`);
    const out = view.slice();
    out[1] = `[${loc.map((c) => c * k).join(", ")}]`;
    return out;
}

async function shoot(
    scenePath: string,
    raw: string,
    view: string[],
    navigationMode: number,
    slug: string,
    withStack: boolean,
    hover: boolean
) {
    const out = path.join(work, `scene-${slug}.pro3d`);
    const d = JSON.parse(raw);
    d.cameraView = { view };
    d.navigationMode = navigationMode;
    d.scenePath = out;
    fs.writeFileSync(out, JSON.stringify(d, null, 2));

    const app = await launchPro3d(out);
    const browser = await chromium.launch();
    try {
        const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
        const render = await ctx.newPage();
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(render);
        await hideChrome(render);

        const shots: Record<string, Buffer> = {};
        if (!withStack && !hover) {
            shots.bare = await settled(render, `${slug}-bare.png`);
            return shots;
        }

        const gis = await setUpProjection(ctx, app.url);
        if (!hover) shots.bare = await settled(render, `${slug}-bare.png`);

        for (const img of IMAGES) {
            await clickRowIcon(gis, img, "plus");
            await gis.waitForTimeout(1500);
        }
        shots.stacked = await settled(render, `${slug}-stacked.png`);

        if (hover) {
            // Green from the frustum only: the scale bar's axis cross is green too and is
            // in every frame, so score each hover against the un-hovered frame.
            const baseGreen = measure(shots.stacked).greenPixels;
            let best: { name: string; green: number } | null = null;
            for (const img of IMAGES) {
                if (!(await hoverRow(gis, img, IMAGES))) { console.log(`  hover ${img}: row not found`); continue; }
                const g = measure(await settled(render, `${slug}-hover-${img}`)).greenPixels - baseGreen;
                console.log(`  hover ${img}: frustum green=${g}`);
                if (!best || g > best.green) best = { name: img, green: g };
            }
            if (best && best.green > 20) {
                await hoverRow(gis, best.name, IMAGES);
                shots.frustum = await settled(render, `${slug}-frustum.png`);
                console.log(`  frustum from ${best.name} (+${best.green} green px over baseline ${baseGreen})`);
            } else {
                console.log(`  NO frustum visible from this viewpoint (best +${best?.green ?? 0} px)`);
            }
        }
        return shots;
    } finally {
        await browser.close();
        await app.stop();
    }
}

async function main() {
    const scenePath = process.env.SCENE;
    if (!scenePath) throw new Error("set SCENE to the .pro3d file (needs bookmarks)");
    fs.mkdirSync(work, { recursive: true });
    fs.mkdirSync(outDir, { recursive: true });

    const raw = fs.readFileSync(scenePath, "utf-8").replace(/^﻿/, "");
    const base = JSON.parse(raw);
    const marks = (base.bookmarks?.flat ?? [])
        .map((e: any) => ({
            name: e.Bookmarks.name as string,
            view: e.Bookmarks.cameraView.view as string[],
            navigationMode: e.Bookmarks.navigationMode as number,
        }))
        .sort((a: any, b: any) => a.name.localeCompare(b.name));
    if (marks.length < 3) throw new Error(`need 3 bookmarks, found ${marks.length}`);

    const r = 77.2; // spherical convention radius, from the HUD
    for (const m of marks) {
        const dist = Math.hypot(...parseVec(m.view[1]));
        const subtends = 2 * Math.asin(Math.min(1, r / dist)) * 180 / Math.PI;
        console.log(`${m.name}: ${dist.toFixed(2)} m -> body subtends ${subtends.toFixed(1)} deg`);
    }

    const [b0, b1, b2] = marks;

    console.log(`\n=== ${b0.name}: unobserved side, bare + stacked ===`);
    const s0 = await shoot(scenePath, raw, b0.view, b0.navigationMode, "b0", true, false);
    // This launch does double duty: its stacked frame is teaser panel 3, and hovering a
    // row in it -- at the bookmark's own 198.8 m, where the body is large -- gives the
    // hover screenshot for the docs. Cheaper than a fourth launch for one image.
    console.log(`\n=== ${b2.name}: second side, stacked + hover (docs) ===`);
    const s2 = await shoot(scenePath, raw, b2.view, b2.navigationMode, "b2", true, true);
    // The frustum's apex is at the projector, ~8 km out, and only its far rectangle is
    // near the body -- from 198 m just two edges cross the frame. Pull straight back
    // along the same radial direction so the cone reads as a cone.
    const k = Number(process.env.PRO3D_TEASER_FRUSTUM_K ?? 4);
    console.log(`\n=== ${b1.name}: frustum, pulled back ${k}x (${(198.77 * k).toFixed(0)} m) ===`);
    const s1 = await shoot(scenePath, raw, pullBack(b1.view, k), b1.navigationMode, "b1", true, true);

    const pick = (b: Buffer | undefined, what: string) => {
        if (!b) throw new Error(`missing panel: ${what}`);
        return PNG.sync.read(b);
    };
    const p1 = pick(s0.bare, "b0 bare");
    const p2 = pick(s0.stacked, "b0 stacked");
    const p3 = pick(s2.stacked, "b2 stacked");
    const p4 = pick(s1.frustum ?? s1.stacked, "b1 frustum");

    const m1 = stats(p1, 0, 0), m2 = stats(p2, 0, 0), m3 = stats(p3, 0, 0), m4 = stats(p4, 0, 0);
    console.log("\n--- panels ---");
    for (const [n, m] of [["1 DRACO only", m1], ["2 AFC projected", m2], ["3 second side", m3], ["4 frustum", m4]] as const)
        if (m.bgFraction < 0.01)
            throw new Error(`${n}: only ${(m.bgFraction * 100).toFixed(2)}% of the frame is at the assumed background ${BG} -- the constant is wrong, numbers would be meaningless`);
    console.log(`1 DRACO only     coverage=${(m1.coverage * 100).toFixed(1)}%  unobserved=${(m1.darkOfBody * 100).toFixed(1)}% of body`);
    console.log(`2 AFC projected  coverage=${(m2.coverage * 100).toFixed(1)}%  unobserved=${(m2.darkOfBody * 100).toFixed(1)}% of body`);
    console.log(`3 second side    coverage=${(m3.coverage * 100).toFixed(1)}%  unobserved=${(m3.darkOfBody * 100).toFixed(1)}% of body`);
    console.log(`4 frustum        greenPixels=${m4.greenPixels}  coverage=${(m4.coverage * 100).toFixed(1)}%`);

    // captions carry the measured numbers, so they cannot drift from the panels
    const pct = (x: number) => `${Math.round(x * 100)}% unobserved`;
    const browser = await chromium.launch();
    let buf: Buffer;
    try {
        buf = await collageHtml(
            await browser.newContext(),
            [
                { file: path.join(work, "b0-bare.png"), caption: "DRACO mosaic only", note: pct(m1.darkOfBody) },
                { file: path.join(work, "b0-stacked.png"), caption: "Plus four AFC images", note: pct(m2.darkOfBody) },
                { file: path.join(work, "b2-stacked.png"), caption: "A second direction", note: pct(m3.darkOfBody) },
                { file: path.join(work, "b1-frustum.png"), caption: "Projector frustum of a hovered image", note: `camera ${(198.77 * k).toFixed(0)} m` },
            ],
            p1.width,
            p1.height
        );
    } finally {
        await browser.close();
    }
    fs.writeFileSync(path.join(work, "multiProjection-teaser.png"), buf);
    fs.writeFileSync(path.join(outDir, "multiProjection-teaser.png"), buf);
    console.log(`\nteaser -> ${path.join(outDir, "multiProjection-teaser.png")}`);

    // The committed multiProjection-hover.png was shot on an unbound scene -- its HUD
    // reads "None xyz" / "conversion failed (set planet)", which is the broken setup
    // section 1 of the doc tells you to fix. Replace it with a hover on a bound scene.
    if (s2.frustum) {
        fs.writeFileSync(path.join(outDir, "multiProjection-hover.png"), s2.frustum);
        console.log(`hover  -> ${path.join(outDir, "multiProjection-hover.png")}`);
    } else {
        console.log("hover  -> NOT regenerated (no frustum visible from that bookmark)");
    }
}

main().catch((e) => { console.error(e); process.exit(1); });
