import { test, expect, BrowserContext, Page } from "@playwright/test";
import { fixture, Pro3d } from "../src/pro3d";
import { overlayPlanet } from "../src/viewer";
import {
    awaitIdle,
    clickTool,
    drawingScene,
    openDrawingViewer,
    pick,
    selectByOption,
    tool,
} from "../src/drawing";
import { diffPng } from "../src/image";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

/**
 * Adding points to a finished annotation (#639), on the Dimorphos OPC, in the real viewer.
 * docs/AnnotationVertexEditing.md, "Adding points".
 *
 * The index arithmetic (which segment splits, ring closing segments, the samples kept exactly,
 * undo, ESC) is proven in src/Tests/VertexEditingTests.fs against a synthetic sampler. What
 * only the app can show is the part a user touches, all read from the render:
 *
 *  - in Edit annotation, walking the cursor along a Sky polyline with Ctrl held, a terrain
 *    sample turns blue when the cursor is within a few pixels of it, and nowhere between;
 *  - Ctrl+click on the line between two dots picks a new point up (the green rubber band runs
 *    from its neighbours to the cursor), Ctrl+click on the surface puts it down: one more
 *    control point, and the line runs through the drop;
 *  - ESC after picking a new point up adds nothing; Ctrl+Z takes an added point back;
 *  - Ctrl+click on a blue sample makes it a control point where it is: one
 *    more handle, on the dot, and the drawn line does not move.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "vertex-insertion");

const EDIT_TOOL = "Edit annotation - move the control points of the selected annotation";
const SELECT_TOOL = "Select annotation";

/** Metres between terrain samples: ~40 px at the default camera distance, well over the 16 px
 *  (DrawingApp.sampleMinSpacingPixels) below which dots are not shown, so a click midway
 *  between two is ~20 px from either - outside the 8 px in which it would convert one. */
const SAMPLING_M = "16";

test.setTimeout(30 * 60_000);

type Px = { x: number; y: number };
type Blob = Px & { area: number };

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

const dist = (a: Px, b: Px) => Math.hypot(a.x - b.x, a.y - b.y);
const mid = (a: Px, b: Px): Px => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });

/** "#Points: 3 | taken on: ..." per annotation on the annotations page (Drawing.UI.fs). */
async function annotationPointCounts(context: BrowserContext, app: Pro3d): Promise<number[]> {
    const page = await context.newPage();
    try {
        await page.goto(app.url + "?page=annotations");
        await page.waitForLoadState("networkidle");
        const text = (await page.evaluate(() => document.body.innerText)) as string;
        return Array.from(text.matchAll(/#Points:\s*(\d+)/g)).map((m) => Number(m[1]));
    } finally {
        await page.close();
    }
}

/** Polyline + Sky in the toolbar, checked to have taken (as supporting-profiles.spec.ts). */
async function selectSkyPolyline(main: Page) {
    const projectionShown = () =>
        main.evaluate(() => {
            const sel = Array.from(document.querySelectorAll("select")).find((s) =>
                Array.from(s.options).some((o) => (o.textContent ?? "").trim() === "Viewpoint")
            );
            return sel ? sel.value : "(none)";
        });
    await selectByOption(main, "Polyline", "Polyline");
    await main.waitForTimeout(800);
    await selectByOption(main, "Sky", "Sky");
    await expect.poll(projectionShown, { timeout: 30_000, intervals: [500] }).toBe("Sky");
}

/**
 * Blobs of pixels passing `is` that are not in `without` (the same view before), after an
 * erosion by `erode` px, at least `minArea` px large. The difference keeps bright terrain out.
 */
function blobs(
    without: Buffer,
    withIt: Buffer,
    is: (r: number, g: number, b: number) => boolean,
    erode: number,
    minArea: number,
    threshold = 60
): Blob[] {
    const a = PNG.sync.read(without);
    const b = PNG.sync.read(withIt);
    const w = b.width, h = b.height;
    const raw = new Uint8Array(w * h);
    for (let i = 0; i < w * h; i++) {
        const j = i * 4;
        const changed =
            Math.abs(b.data[j] - a.data[j]) + Math.abs(b.data[j + 1] - a.data[j + 1]) +
            Math.abs(b.data[j + 2] - a.data[j + 2]) > threshold;
        if (changed && is(b.data[j], b.data[j + 1], b.data[j + 2])) raw[i] = 1;
    }
    const mask = new Uint8Array(w * h);
    for (let y = erode; y < h - erode; y++)
        for (let x = erode; x < w - erode; x++) {
            if (!raw[y * w + x]) continue;
            let all = true;
            for (let dy = -erode; dy <= erode && all; dy++)
                for (let dx = -erode; dx <= erode && all; dx++)
                    if (!raw[(y + dy) * w + x + dx]) all = false;
            if (all) mask[y * w + x] = 1;
        }
    const out: Blob[] = [];
    const stack: number[] = [];
    for (let start = 0; start < w * h; start++) {
        if (!mask[start]) continue;
        let sx = 0, sy = 0, n = 0;
        mask[start] = 0;
        stack.push(start);
        while (stack.length) {
            const i = stack.pop()!;
            const x = i % w, y = (i - x) / w;
            sx += x; sy += y; n++;
            for (let dy = -1; dy <= 1; dy++)
                for (let dx = -1; dx <= 1; dx++) {
                    const nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    const k = ny * w + nx;
                    if (mask[k]) { mask[k] = 0; stack.push(k); }
                }
        }
        if (n >= minArea) out.push({ x: sx / n, y: sy / n, area: n });
    }
    return out;
}

/**
 * The white control-point handles (radius thickness + 6 px, ~14 px across here), as places
 * where a 9x9 window holds at least 45 white pixels that are not in `without`. Not by blob
 * erosion: compression ringing inside a disc breaks it up, and the selected line's 1-2 px
 * white core reaches ~18 in such a window at most. Peaks within 10 px are one handle.
 */
function whiteHandles(without: Buffer, withIt: Buffer): Blob[] {
    const a = PNG.sync.read(without);
    const b = PNG.sync.read(withIt);
    const w = b.width, h = b.height;
    const white = new Uint8Array(w * h);
    for (let i = 0; i < w * h; i++) {
        const j = i * 4;
        const changed =
            Math.abs(b.data[j] - a.data[j]) + Math.abs(b.data[j + 1] - a.data[j + 1]) +
            Math.abs(b.data[j + 2] - a.data[j + 2]) > 60;
        if (changed && b.data[j] > 235 && b.data[j + 1] > 235 && b.data[j + 2] > 235) white[i] = 1;
    }
    const peaks: Blob[] = [];
    for (let y = 4; y < h - 4; y++)
        for (let x = 4; x < w - 4; x++) {
            if (!white[y * w + x]) continue;
            let n = 0;
            for (let dy = -4; dy <= 4; dy++)
                for (let dx = -4; dx <= 4; dx++) n += white[(y + dy) * w + x + dx];
            if (n >= 45) peaks.push({ x, y, area: 1 });
        }
    return merged(peaks, 10);
}

/** Joins blobs closer than `radius`: a handle over bright terrain differs from the frame
 *  without it only in parts, and falls apart into fragments. */
function merged(parts: Blob[], radius: number): Blob[] {
    const out: Blob[] = [];
    for (const p of parts) {
        const near = out.find((o) => dist(o, p) < radius);
        if (!near) { out.push({ ...p }); continue; }
        const n = near.area + p.area;
        near.x = (near.x * near.area + p.x * p.area) / n;
        near.y = (near.y * near.area + p.y * p.area) / n;
        near.area = n;
    }
    return out;
}

/** Whether most pixels along `p`-`q` (ends left out) are rubber-band C4b.VRVisGreen (a
 *  yellow-green, ~(178, 210, 0)), give or take 3 px. */
function greenAlong(png: Buffer, p: Px, q: Px): boolean {
    const img = PNG.sync.read(png);
    let hits = 0, samples = 0;
    for (let k = 3; k <= 17; k++) {
        const t = k / 20;
        const cx = p.x + t * (q.x - p.x), cy = p.y + t * (q.y - p.y);
        samples++;
        search: for (let dy = -3; dy <= 3; dy++)
            for (let dx = -3; dx <= 3; dx++) {
                const x = Math.round(cx) + dx, y = Math.round(cy) + dy;
                if (x < 0 || y < 0 || x >= img.width || y >= img.height) continue;
                const i = (img.width * y + x) * 4;
                const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
                if (r > 130 && r < 220 && g > 180 && b < 80) { hits++; break search; }
            }
    }
    return hits >= 0.8 * samples;
}

/** The annotation's line: its red, or the yellow-green (C4b.VRVisGreen) it is drawn in while
 *  selected. Not the hover yellow, V4f(1, 0.85, 0.1), whose red is far higher. */
const isRed = (r: number, g: number, b: number) =>
    (r > 60 && r - g > 30 && r - b > 30) || (r > 140 && r < 215 && g > 185 && b < 70);

/** The annotation-red pixel nearest `p`, within `radius`. */
function nearestRed(png: Buffer, p: Px, radius = 30): Px | undefined {
    const img = PNG.sync.read(png);
    let best: Px | undefined, bestD = Infinity;
    for (let dy = -radius; dy <= radius; dy++)
        for (let dx = -radius; dx <= radius; dx++) {
            const x = Math.round(p.x) + dx, y = Math.round(p.y) + dy;
            if (x < 0 || y < 0 || x >= img.width || y >= img.height) continue;
            const i = (img.width * y + x) * 4;
            if (isRed(img.data[i], img.data[i + 1], img.data[i + 2]) && Math.hypot(dx, dy) < bestD) {
                bestD = Math.hypot(dx, dy);
                best = { x, y };
            }
        }
    return best;
}

/**
 * How much of the line in `a` has no line within 2 px in `b`, relative to its length in
 * pixels. By the selected line's yellow-green band (C4b.VRVisGreen), which both frames have:
 * its 1 px core is white while the line is hovered and red otherwise, and the hover sticks
 * from whichever mouse move came last - a change of colour, not of place.
 */
function lineMoved(a: Buffer, b: Buffer): number {
    const A = PNG.sync.read(a), B = PNG.sync.read(b);
    const w = A.width, h = A.height;
    const band = (img: any, i: number) => {
        const r = img.data[i], g = img.data[i + 1], bl = img.data[i + 2];
        return r > 130 && r < 220 && g > 180 && bl < 80;
    };
    let line = 0, moved = 0;
    for (let y = 2; y < h - 2; y++)
        for (let x = 2; x < w - 2; x++) {
            if (!band(A, (y * w + x) * 4)) continue;
            line++;
            let near = false;
            for (let dy = -2; dy <= 2 && !near; dy++)
                for (let dx = -2; dx <= 2 && !near; dx++)
                    near = band(B, ((y + dy) * w + x + dx) * 4);
            if (!near) moved++;
        }
    return moved / Math.max(1, line);
}

/**
 * Two near-identical frames in a row, saved as `name`. Not `settled`: its loading-splash gate
 * counts the splash's yellow-green, which is also the colour of the selected annotation - and
 * the edited annotation is selected throughout. The surface is known to be up (openDrawingViewer).
 */
async function stable(page: Page, name: string): Promise<Buffer> {
    let prev = await page.screenshot();
    for (let i = 0; i < 60; i++) {
        await page.waitForTimeout(1000);
        const cur = await page.screenshot();
        const same = diffPng(prev, cur).changedFraction < 0.001;
        prev = cur;
        if (same) break;
    }
    save(name, prev);
    return prev;
}

/** Moves the cursor below the body, where the pick ray hits nothing. */
async function parkCursor(render: Page, vp: { width: number; height: number }) {
    await render.mouse.move(Math.round(vp.width / 2), vp.height - 40);
    await render.waitForTimeout(1500);
}

/**
 * Brings the cursor onto `to` with Ctrl held, in steps - the hover readback (which handle,
 * and where the sample dots show) runs on mouse moves, and only while the tool is armed.
 * Leaves Ctrl down; the caller releases it.
 */
async function ctrlHover(render: Page, from: Px, to: Px) {
    await render.bringToFront();
    await render.mouse.move(Math.round(from.x), Math.round(from.y));
    await render.waitForTimeout(400);
    await render.keyboard.down("Control");
    await render.waitForTimeout(300);
    const steps = 8;
    for (let s = 1; s <= steps; s++) {
        const t = s / steps;
        await render.mouse.move(Math.round(from.x + (to.x - from.x) * t), Math.round(from.y + (to.y - from.y) * t));
        await render.waitForTimeout(150);
    }
    await render.waitForTimeout(800);
}

/**
 * Walks the cursor along the line from `t0` to `t1` of the way from `from` to `to` (each step
 * snapped onto the line in `lineFrame`), Ctrl held, and records at every stop whether a blue
 * dot is under it - the dot a click there would convert (sampleSnapPixels, shown only where the
 * samples are told apart). Returns the sample centres (middles of blue runs) and the widest
 * stretch without one (where a click adds a point).
 *
 * This steers by what the user sees rather than by picking grey dots out of the lossy stream:
 * blue under the cursor is exactly the contract between the shader and segmentUnderCursor.
 */
async function scanLine(render: Page, app: Pro3d, lineFrame: Buffer, from: Px, to: Px, name: string,
                        t0 = 0.2, t1 = 0.8) {
    await awaitIdle(render, app, 2000);
    const steps = Math.max(10, Math.round((dist(from, to) * (t1 - t0)) / 3));
    const stops: Array<{ p: Px; kind: Blue }> = [];
    const at = (k: number): Px => {
        const t = t0 + ((t1 - t0) * k) / steps;
        const p = { x: from.x + (to.x - from.x) * t, y: from.y + (to.y - from.y) * t };
        return nearestRed(lineFrame, p, 8) ?? p;
    };
    const first = at(0);
    await ctrlHover(render, { x: first.x - 20, y: first.y - 20 }, first);
    try {
        for (let k = 0; k <= steps; k++) {
            const p = at(k);
            await render.mouse.move(Math.round(p.x), Math.round(p.y));
            await render.waitForTimeout(250);
            stops.push({ p, kind: await blueNear(render, p) });
        }
        save(`${name}.png`, await render.screenshot());
    } finally {
        await render.keyboard.up("Control");
    }
    const runs = (want: Blue) => {
        const out: Px[][] = [];
        let cur: Px[] = [];
        for (const s of stops) {
            if (s.kind === want) cur.push(s.p);
            else { if (cur.length) out.push(cur); cur = []; }
        }
        if (cur.length) out.push(cur);
        return out;
    };
    const samples = runs("sample").map((r) => r[Math.floor(r.length / 2)]);
    // between samples the click adds a point, and the preview dot says so
    const gaps = runs("preview").filter((r) => r.length >= 3).sort((a, b) => b.length - a.length);
    const gap = gaps.length ? gaps[0][Math.floor(gaps[0].length / 2)] : undefined;
    console.log(`${name}: ${stops.map((s) => ({ sample: "S", preview: "P", none: "." })[s.kind]).join("")} samples ${JSON.stringify(samples)} gap ${JSON.stringify(gap)}`);
    return { samples, gap };
}

type Blue = "sample" | "preview" | "none";

/** What is blue, V4f(0.15, 0.55, 1), within 10 px of `p` (nothing else near the line is blue;
 *  the terrain is grey): the sample a click converts, a disc ~8 px across (~50 px), or the
 *  preview of the point a click adds, ~14 px across (~150 px). */
async function blueNear(render: Page, p: Px): Promise<Blue> {
    const clip = { x: Math.round(p.x) - 10, y: Math.round(p.y) - 10, width: 21, height: 21 };
    const img = PNG.sync.read(await render.screenshot({ clip }));
    let n = 0;
    for (let i = 0; i < img.width * img.height * 4; i += 4)
        if (img.data[i + 2] > 190 && img.data[i] < 120 && img.data[i + 2] - img.data[i] > 100) n++;
    return n < 4 ? "none" : n < 100 ? "sample" : "preview";
}

/** Ctrl+click at `at`, coming onto it with Ctrl already down. */
async function ctrlClick(render: Page, at: Px) {
    await ctrlHover(render, { x: at.x - 20, y: at.y - 20 }, at);
    try {
        await render.mouse.click(Math.round(at.x), Math.round(at.y));
        await render.waitForTimeout(200);
    } finally {
        await render.keyboard.up("Control");
    }
}

/** With a point in hand, Ctrl-moves from `from` to `to` in steps and clicks there to drop it. */
async function ctrlDrop(render: Page, app: Pro3d, from: Px, to: Px, name: string) {
    await render.bringToFront();
    await render.keyboard.down("Control");
    try {
        await render.waitForTimeout(300);
        const steps = 12;
        for (let s = 1; s <= steps; s++) {
            const t = s / steps;
            await render.mouse.move(Math.round(from.x + (to.x - from.x) * t), Math.round(from.y + (to.y - from.y) * t));
            await render.waitForTimeout(150);
        }
        // the last preview pick has to land before the click
        await awaitIdle(render, app, 2000);
        save(`${name}-before-drop.png`, await render.screenshot());
        await render.mouse.click(Math.round(to.x), Math.round(to.y));
        await render.waitForTimeout(200);
    } finally {
        await render.keyboard.up("Control");
    }
    await awaitIdle(render, app);
}

test("points are added on a segment and from terrain samples; ESC and Ctrl+Z take them back", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    const scene = drawingScene(artifacts, "vertex-insertion");
    const { app, context, render } = await openDrawingViewer(browser, scene, "00-loaded.png", save);
    try {
        await expect
            .poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("Dimorphos");

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        // --- a Sky polyline with sparse samples ---------------------------------------------
        await clickTool(render, "Draw annotation");
        await selectSkyPolyline(main);
        const thickness = main.locator(".pro3d-secondary-toolbar input[type=number]").first();
        // thin, as supporting-profiles draws its lines
        await thickness.fill("1");
        await thickness.press("Enter");
        const sampling = main.locator('.pro3d-secondary-toolbar [title^="Sampling amount"] input').first();
        await sampling.fill(SAMPLING_M);
        await sampling.press("Enter");
        await main.waitForTimeout(800);

        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);
        const A: Px = { x: cx - 160, y: cy + 30 };
        const B: Px = { x: cx, y: cy + 60 };
        const C: Px = { x: cx + 160, y: cy + 10 };
        for (const p of [A, B, C]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await awaitIdle(render, app);
        await render.bringToFront();
        await render.mouse.move(cx, cy + 150);
        await render.keyboard.press("Enter");
        await awaitIdle(render, app);
        await expect
            .poll(() => annotationPointCounts(context, app), { timeout: 180_000, intervals: [5000] })
            .toEqual([3]);

        // One render view only: the main page's render control would report its own size and
        // squeeze the pick pass (see ellipse-editing.spec.ts).
        await main.close();
        await render.setViewportSize({ width: 1599, height: 900 });
        await render.waitForTimeout(500);
        await render.setViewportSize({ width: 1600, height: 900 });
        await render.waitForTimeout(1000);

        await clickTool(render, SELECT_TOOL);
        await parkCursor(render, vp);
        const drawn = await stable(render, "01-drawn.png");
        for (const p of [A, B, C])
            expect(nearestRed(drawn, p, 8), `the line runs through the pick ${JSON.stringify(p)}`).toBeDefined();

        const editMode = async () => {
            await clickTool(render, EDIT_TOOL);
            await expect.poll(async () => (await tool(render, EDIT_TOOL)).active, { timeout: 30_000 }).toBe(true);
        };
        const handlesNow = async (reference: Buffer, name: string) => {
            await parkCursor(render, vp);
            const shot = await stable(render, `${name}.png`);
            const h = whiteHandles(reference, shot);
            console.log(`${name}: handles ${JSON.stringify(h)}`);
            return { shot, handles: h };
        };

        await editMode();
        const idle = await handlesNow(drawn, "02-handles");
        expect(idle.handles.length, "three handles").toBe(3);

        // --- 1. along A-B the samples light up under the cursor, with gaps between -----------
        const ab = await scanLine(render, app, drawn, A, B, "03-scan-ab");
        expect(ab.samples.length, "a sample turns blue under the cursor").toBeGreaterThanOrEqual(1);
        expect(ab.gap, "and line between them").toBeDefined();

        // --- 2. a click between two samples picks a new point up; the drop adds it ------------
        const q = ab.gap!;
        await ctrlClick(render, q);
        await awaitIdle(render, app, 2000);
        // picked up, not written: a click that landed on a sample would already show 4
        expect(await annotationPointCounts(context, app), "the segment click writes nothing yet").toEqual([3]);
        const drop: Px = { x: q.x, y: q.y + 60 };
        await ctrlDrop(render, app, q, drop, "04-insert");
        const inHand = PNG.sync.read(fs.readFileSync(path.join(artifacts, "04-insert-before-drop.png")));
        const inHandBuf = PNG.sync.write(inHand);
        expect(greenAlong(inHandBuf, A, drop), "rubber band from the previous point to the cursor").toBe(true);
        expect(greenAlong(inHandBuf, drop, B), "and on to the next").toBe(true);
        await expect
            .poll(() => annotationPointCounts(context, app), { timeout: 60_000, intervals: [2000] })
            .toEqual([4]);

        await clickTool(render, SELECT_TOOL);
        await parkCursor(render, vp);
        const inserted = await stable(render, "05-inserted.png");
        expect(nearestRed(inserted, drop, 10), "the line runs through the added point").toBeDefined();
        expect(nearestRed(inserted, q, 6), "and no longer along the old segment").toBeUndefined();
        await editMode();
        expect((await handlesNow(drawn, "06-handles-inserted")).handles.length, "four handles").toBe(4);

        // --- 3. ESC: a point picked up and abandoned adds nothing -----------------------------
        const bc = await scanLine(render, app, inserted, B, C, "07-scan-bc");
        expect(bc.gap, "line between samples on B-C").toBeDefined();
        const q2 = bc.gap!;
        await ctrlClick(render, q2);
        await awaitIdle(render, app, 2000);
        await ctrlHover(render, q2, { x: q2.x, y: q2.y + 40 });
        await render.keyboard.up("Control");
        await render.keyboard.press("Escape");
        await awaitIdle(render, app, 2000);
        expect(await annotationPointCounts(context, app), "ESC: still four points").toEqual([4]);

        // --- 4. Ctrl+Z takes the added point back ------------------------------------------
        await render.bringToFront();
        await render.mouse.move(cx, vp.height - 40);
        await render.keyboard.press("Control+z");
        await awaitIdle(render, app, 2000);
        await expect
            .poll(() => annotationPointCounts(context, app), { timeout: 60_000, intervals: [2000] })
            .toEqual([3]);

        // --- 5. a click on a dot makes it a control point where it is -----------------------
        const again = await scanLine(render, app, drawn, B, C, "08-scan-bc");
        const dot = again.samples[Math.floor(again.samples.length / 2)];
        expect(dot, "a sample to convert").toBeDefined();
        await ctrlHover(render, { x: dot.x - 20, y: dot.y - 20 }, dot);
        save("09-on-dot.png", await render.screenshot());
        expect(await blueNear(render, dot), "the sample under the cursor is blue").toBe("sample");
        try {
            await render.mouse.click(Math.round(dot.x), Math.round(dot.y));
            await render.waitForTimeout(200);
        } finally {
            await render.keyboard.up("Control");
        }
        await awaitIdle(render, app);
        await expect
            .poll(() => annotationPointCounts(context, app), { timeout: 60_000, intervals: [2000] })
            .toEqual([4]);

        const converted = await handlesNow(drawn, "10-handles-converted");
        expect(converted.handles.length, "four handles").toBe(4);
        const nearestHandle = converted.handles.reduce((b, h) => (dist(h, dot) < dist(b, dot) ? h : b));
        expect(dist(nearestHandle, dot), "the new handle sits on the dot").toBeLessThan(6);

        await clickTool(render, SELECT_TOOL);
        await parkCursor(render, vp);
        const unchanged = await stable(render, "11-converted.png");
        const change = lineMoved(drawn, unchanged);
        console.log(`converted: line moved ${(100 * change).toFixed(2)} %`);
        expect(change, "the drawn line did not move").toBeLessThan(0.05);
    } finally {
        await context.close();
        await app.stop();
    }
});
