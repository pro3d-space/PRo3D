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
    settled,
    tool,
} from "../src/drawing";
import { crop, EllipseFit, fitOutline } from "../src/ellipse-fit";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

/**
 * Editing a finished AxisEllipse by its axis ends, on the Dimorphos OPC, in the real viewer.
 * docs/EllipseAnnotations.md, "Editing an ellipse".
 *
 * The geometry of an edit (which end stays, which length is kept, no drift, undo, refused
 * drops) is proven in src/Tests/VertexEditingTests.fs against a synthetic sampler. What only
 * the app can show is the part a user touches:
 *
 *  - the five handles are drawn, at the ends of the axes and the centre, as soon as Edit
 *    annotation is on;
 *  - Ctrl+click on one picks it up (it turns green), Ctrl+click on the surface drops it;
 *  - the outline on screen is rebuilt and draped: the major end moved, the opposite end
 *    stayed, the minor width kept -- and for a minor end the other way round;
 *  - Ctrl+Z brings the drawn ellipse back;
 *  - the white centre handle moves the whole ellipse, and its crater profiles move with it.
 *
 * Every check reads the render, so a handle that is drawn but cannot be grabbed, or an edit
 * that reaches the model but not the screen, fails here. The measures are relative to the
 * ellipse as drawn, so perspective and terrain, which shift absolute pixel sizes, cancel.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "ellipse-editing");

const EDIT_TOOL = "Edit annotation - move the control points of the selected annotation";
const SELECT_TOOL = "Select annotation";

/** Sampled outline length: `EllipticAnnotations.planeSampleNumber` + 1. */
const OUTLINE_POINTS = 201;

test.setTimeout(30 * 60_000);

type Px = { x: number; y: number };

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

const dist = (a: Px, b: Px) => Math.hypot(a.x - b.x, a.y - b.y);

/** Moves the cursor below the body, where the pick ray hits nothing and no marker is drawn. */
async function parkCursor(render: Page, vp: { width: number; height: number }) {
    await render.mouse.move(Math.round(vp.width / 2), vp.height - 40);
    await render.waitForTimeout(1500);
}

/** "#Points: 201 | taken on: ..." per annotation on the annotations page (Drawing.UI.fs). */
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

type HandleColor = "major" | "minor" | "centre" | "hover";

/**
 * Which handle colour a pixel is, if any (VertexHandleShader / EllipseEditColors): the major
 * axis ends magenta C4b(240, 80, 240), the minor cyan C4b(40, 200, 255), the centre white, and hover yellow
 * V4f(1, 0.85, 0.1) - the hover is only updated by moves over the annotations, so the last
 * handle touched can stay yellow after the cursor has left.
 */
function handleColor(r: number, g: number, b: number): HandleColor | undefined {
    if (r > 180 && b > 180 && g < 150) return "major";
    if (b > 200 && g > 150 && r < 120) return "minor";
    if (r > 230 && g > 180 && b < 90) return "hover";
    if (r > 225 && g > 225 && b > 225) return "centre";
    return undefined;
}

/**
 * The edit handles in `withHandles`: blobs of a handle colour that are not in `without`, the
 * same view before Edit annotation was switched on. The difference is what makes this safe on
 * a bright terrain: a saturated pixel of the texture is in both frames.
 *
 * The axis guides are drawn in the same colours, 2 px wide, and run into their handles. An
 * erosion by 2 px removes them and keeps the discs (radius thickness + 6 px).
 */
function handleBlobs(without: Buffer, withHandles: Buffer): Array<Px & { area: number; color: HandleColor }> {
    const a = PNG.sync.read(without);
    const b = PNG.sync.read(withHandles);
    const w = b.width, h = b.height;
    const COLORS: HandleColor[] = ["major", "minor", "centre", "hover"];
    const raw = new Uint8Array(w * h); // 0 = none, else 1 + index into COLORS
    for (let i = 0; i < w * h; i++) {
        const j = i * 4;
        const c = handleColor(b.data[j], b.data[j + 1], b.data[j + 2]);
        const changed =
            Math.abs(b.data[j] - a.data[j]) + Math.abs(b.data[j + 1] - a.data[j + 1]) +
            Math.abs(b.data[j + 2] - a.data[j + 2]) > 60;
        if (c && changed) raw[i] = 1 + COLORS.indexOf(c);
    }
    const R = 2;
    const mask = new Uint8Array(w * h);
    for (let y = R; y < h - R; y++)
        for (let x = R; x < w - R; x++) {
            const v = raw[y * w + x];
            if (!v) continue;
            let all = true;
            for (let dy = -R; dy <= R && all; dy++)
                for (let dx = -R; dx <= R && all; dx++)
                    if (!raw[(y + dy) * w + x + dx]) all = false;
            if (all) mask[y * w + x] = v;
        }
    const blobs: Array<Px & { area: number; color: HandleColor }> = [];
    const stack: number[] = [];
    for (let start = 0; start < w * h; start++) {
        const v = mask[start];
        if (!v) continue;
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
                    if (mask[k] === v) { mask[k] = 0; stack.push(k); }
                }
        }
        // an eroded disc is ~100 px; anything far smaller is a highlight, not a handle
        if (n >= 30) blobs.push({ x: sx / n, y: sy / n, area: n, color: COLORS[v - 1] });
    }
    return blobs;
}

/** Whether most of the pixels along `p`-`q` (ends left out) have handle colour `color`, give
 *  or take 2 px: an axis guide is drawn there. */
function guideAlong(png: Buffer, p: Px, q: Px, color: HandleColor): boolean {
    const img = PNG.sync.read(png);
    let hits = 0, samples = 0;
    for (let k = 3; k <= 17; k++) {
        const t = k / 20;
        const cx = p.x + t * (q.x - p.x), cy = p.y + t * (q.y - p.y);
        samples++;
        search: for (let dy = -2; dy <= 2; dy++)
            for (let dx = -2; dx <= 2; dx++) {
                const x = Math.round(cx) + dx, y = Math.round(cy) + dy;
                if (x < 0 || y < 0 || x >= img.width || y >= img.height) continue;
                const i = (img.width * y + x) * 4;
                if (handleColor(img.data[i], img.data[i + 1], img.data[i + 2]) === color) { hits++; break search; }
            }
    }
    return hits >= 0.8 * samples;
}

/** Whether the pixel at `p` (or one within 3 px) is handle-grabbed green, V4f(0.2, 0.8, 0.2). */
function greenAt(png: Buffer, p: Px): boolean {
    const img = PNG.sync.read(png);
    for (let dy = -3; dy <= 3; dy++)
        for (let dx = -3; dx <= 3; dx++) {
            const x = Math.round(p.x) + dx, y = Math.round(p.y) + dy;
            if (x < 0 || y < 0 || x >= img.width || y >= img.height) continue;
            const i = (img.width * y + x) * 4;
            const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
            if (g > 150 && g - r > 80 && g - b > 80) return true;
        }
    return false;
}

/** Distance from `p` to the nearest outline (annotation red) pixel, up to `radius`; Infinity
 *  when there is none that close. Same colour test as fitOutline. */
function redDistance(png: Buffer, p: Px, radius = 30): number {
    const img = PNG.sync.read(png);
    let best = Infinity;
    for (let dy = -radius; dy <= radius; dy++)
        for (let dx = -radius; dx <= radius; dx++) {
            const x = Math.round(p.x) + dx, y = Math.round(p.y) + dy;
            if (x < 0 || y < 0 || x >= img.width || y >= img.height) continue;
            const i = (img.width * y + x) * 4;
            const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
            if (r > 60 && r - g > 30 && r - b > 30) best = Math.min(best, Math.hypot(dx, dy));
        }
    return best;
}

/**
 * The third click of an AxisEllipse. It was lost now and then with nothing in the log: it comes
 * during the burst of surface extraction that follows the first picks. Repeated while no
 * annotation has appeared - a click too many after a finished ellipse would only start a new one.
 */
async function closeEllipse(render: Page, app: Pro3d, context: BrowserContext, p: Px) {
    for (let attempt = 0; attempt < 3; attempt++) {
        await awaitIdle(render, app);
        await pick(render, p.x, p.y);
        await awaitIdle(render, app);
        // finishing may generate profiles too: give it time before calling the click lost
        const finished = await expect
            .poll(async () => (await annotationPointCounts(context, app)).length, { timeout: 45_000, intervals: [3000] })
            .toBeGreaterThan(0)
            .then(() => true, () => false);
        if (finished) return;
        console.log(`closing pick ${attempt + 1} did not finish the ellipse, again`);
    }
}

/**
 * Picks the handle at `from` up and puts it down at `to`, as a user does.
 *
 * The pick-up is an ordinary Ctrl+click. The drop is not: a grab only becomes droppable once a
 * preview hit has arrived after it (ArmVertexGrab, "a grab needs a mouse move" in
 * docs/AnnotationVertexEditing.md), and preview picks only run while Ctrl is held. So Ctrl goes
 * down first, the cursor travels to `to` in steps, and only then is the click sent.
 * Returns the frame taken while the handle was in hand.
 */
async function moveHandle(render: Page, app: Pro3d, from: Px, to: Px, label: string): Promise<Buffer> {
    await awaitIdle(render, app, 2000);
    // Which handle is under the cursor is read back on mouse *move*, and only while the tool
    // is armed (Ctrl held, allowAnnotationPicking). `pick` moves first and presses Ctrl after,
    // so the click would find nothing hovered: come onto the handle with Ctrl already down.
    await render.bringToFront();
    await render.mouse.move(Math.round(from.x) - 12, Math.round(from.y) - 12);
    await render.waitForTimeout(400);
    await render.keyboard.down("Control");
    try {
        await render.waitForTimeout(300);
        for (const d of [8, 4, 0]) {
            await render.mouse.move(Math.round(from.x) - d, Math.round(from.y) - d);
            await render.waitForTimeout(250);
        }
        await render.waitForTimeout(500);
        save(`${label}-hover.png`, await render.screenshot());
        await render.mouse.click(Math.round(from.x), Math.round(from.y));
        await render.waitForTimeout(200);
    } finally {
        await render.keyboard.up("Control");
    }
    await awaitIdle(render, app, 2000);
    const inHand = await render.screenshot();
    save(`${label}-in-hand.png`, inHand);

    await render.bringToFront();
    await render.keyboard.down("Control");
    try {
        await render.waitForTimeout(300);
        const steps = 12;
        for (let s = 1; s <= steps; s++) {
            const t = s / steps;
            await render.mouse.move(
                Math.round(from.x + (to.x - from.x) * t),
                Math.round(from.y + (to.y - from.y) * t)
            );
            await render.waitForTimeout(150);
        }
        // the last preview pick has to land before the click
        await awaitIdle(render, app, 2000);
        save(`${label}-before-drop.png`, await render.screenshot());
        await render.mouse.click(Math.round(to.x), Math.round(to.y));
        await render.waitForTimeout(200);
    } finally {
        await render.keyboard.up("Control");
    }
    await awaitIdle(render, app);
    return inHand;
}

test("an AxisEllipse is reshaped by dragging its axis ends, and Ctrl+Z restores it", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    const scene = drawingScene(artifacts, "ellipse-editing");
    const { app, context, render } = await openDrawingViewer(browser, scene, "00-loaded.png", save);
    try {
        await expect
            .poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("Dimorphos");

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        // --- draw the ellipse, as ellipse-annotation.spec.ts does -------------------------
        await clickTool(render, "Draw annotation");
        await selectByOption(main, "AxisEllipse", "AxisEllipse");
        const thickness = main.locator(".pro3d-secondary-toolbar input[type=number]").first();
        await thickness.fill("2");
        await thickness.press("Enter");
        await main.waitForTimeout(800);

        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);
        const ax = 95, ay = 55, minor = 65;
        const len = Math.hypot(2 * ax, 2 * ay);
        const majorA: Px = { x: cx - ax, y: cy + ay };
        const majorB: Px = { x: cx + ax, y: cy - ay };
        const minorEnd: Px = {
            x: Math.round(cx + ((2 * ay) / len) * minor),
            y: Math.round(cy + ((2 * ax) / len) * minor),
        };
        for (const p of [majorA, majorB]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await closeEllipse(render, app, context, minorEnd);
        await expect
            .poll(() => annotationPointCounts(context, app), { timeout: 180_000, intervals: [5000] })
            .toEqual([OUTLINE_POINTS]);

        // The main page carries a render control of its own, and its ResizeSensor reports its
        // size whenever its layout changes (a tool switch changes the toolbar row). The model
        // has ONE frustum, whose aspect is the last reported size (Viewer.updateFrustumAspect),
        // and the annotation pick pass projects with it: on this 1600x900 page with the main
        // page's 887x812 it drew everything squeezed to ~60 % in height, so only the horizontal
        // centre line was pickable. A pre-existing bug of multiple render views, not of this
        // feature. The toolbar is no longer needed, so the main page goes, and a resize makes
        // the render page the last one reported.
        await main.close();
        await render.setViewportSize({ width: 1599, height: 900 });
        await render.waitForTimeout(500);
        await render.setViewportSize({ width: 1600, height: 900 });
        await render.waitForTimeout(1000);

        // The window the outline is measured in: generous enough for the major axis to grow
        // by a third, and clear of the tool strip on the right edge.
        const win = { x: cx - ax - 190, y: cy - ay - 190, w: 2 * ax + 380, h: 2 * ay + 380 };
        const measure = async (name: string): Promise<{ shot: Buffer; fit: EllipseFit }> => {
            await parkCursor(render, vp);
            const shot = await settled(render, `${name}.png`, save);
            const fit = fitOutline(crop(shot, win.x, win.y, win.w, win.h));
            console.log(`${name}: ${JSON.stringify(fit)}`);
            expect(fit, `${name}: the outline is in the render`).not.toBeNull();
            expect(fit!.emptyBins, `${name}: the outline is closed`).toBe(0);
            // centroid back in screen coordinates
            return { shot, fit: { ...fit!, cx: fit!.cx + win.x, cy: fit!.cy + win.y } };
        };

        // Select annotation: the ellipse stays selected (finishing selects it) but no
        // handles are drawn, so this frame is the reference for finding them.
        await clickTool(render, SELECT_TOOL);
        const drawn = await measure("01-drawn");
        expect(redDistance(drawn.shot, majorB), "the outline runs through the picked major end")
            .toBeLessThan(8);

        // --- the handles ------------------------------------------------------------------
        const findHandles = async (reference: Buffer, name: string) => {
            await clickTool(render, EDIT_TOOL);
            await expect.poll(async () => (await tool(render, EDIT_TOOL)).active, { timeout: 30_000 })
                .toBe(true);
            await parkCursor(render, vp);
            const shot = await settled(render, `${name}.png`, save);
            const blobs = handleBlobs(reference, shot);
            console.log(`${name}: handles ${JSON.stringify(blobs)}`);
            return { blobs, shot };
        };

        const found = await findHandles(drawn.shot, "02-handles");
        const handles = found.blobs;
        const handlesFrame = found.shot;
        expect(handles.length, "five handles: two ends per axis and the centre").toBe(5);
        const nearest = (p: Px) =>
            handles.reduce((best, h) => (dist(h, p) < dist(best, p) ? h : best), handles[0]);
        // the major ends are where they were clicked (the drape lands the plane point on the
        // picked surface point); the minor ends are wherever the plane construction put them
        expect(dist(nearest(majorA), majorA), "a handle on the first major end").toBeLessThan(8);
        expect(dist(nearest(majorB), majorB), "a handle on the second major end").toBeLessThan(8);
        const middle: Px = { x: (majorA.x + majorB.x) / 2, y: (majorA.y + majorB.y) / 2 };
        const centreHandle = handles.reduce((best, h) => (dist(h, middle) < dist(best, middle) ? h : best), handles[0]);
        expect(dist(centreHandle, middle), "a handle at the centre, between the major ends").toBeLessThan(15);
        expect(centreHandle.color, "the centre handle is white").toBe("centre");
        const minorHandles = handles.filter((h) => dist(h, majorA) > 20 && dist(h, majorB) > 20 && dist(h, middle) > 20);
        expect(minorHandles.length, "the other two sit off the major axis").toBe(2);
        // the two axes tell themselves apart: handles and guides in their axis colour
        const majorHandles = [nearest(majorA), nearest(majorB)];
        for (const h of majorHandles)
            expect(["major", "hover"], "a major end is magenta").toContain(h.color);
        for (const h of minorHandles)
            expect(["minor", "hover"], "a minor end is cyan").toContain(h.color);
        expect(guideAlong(handlesFrame, majorHandles[0], majorHandles[1], "major"),
            "a magenta guide runs along the major axis").toBe(true);
        expect(guideAlong(handlesFrame, minorHandles[0], minorHandles[1], "minor"),
            "a cyan guide runs along the minor axis").toBe(true);

        // --- 1. pull the second major end out by 35 % of the axis -------------------------
        const grab = nearest(majorB);
        const outward: Px = {
            x: majorB.x + 0.35 * (majorB.x - majorA.x),
            y: majorB.y + 0.35 * (majorB.y - majorA.y),
        };
        const inHand = await moveHandle(render, app, grab, outward, "03-major");
        expect(greenAt(inHand, grab), "the grabbed handle turns green").toBe(true);
        expect(await annotationPointCounts(context, app), "still one ellipse, re-sampled")
            .toEqual([OUTLINE_POINTS]);

        await clickTool(render, SELECT_TOOL);
        const stretched = await measure("04-major-moved");
        const aRatio = stretched.fit.a / drawn.fit.a;
        const bRatio = stretched.fit.b / drawn.fit.b;
        console.log(`major drag: a x${aRatio.toFixed(3)}, b x${bRatio.toFixed(3)}`);
        expect(aRatio, "the major axis grew by about the 35 % it was pulled").toBeGreaterThan(1.15);
        expect(aRatio).toBeLessThan(1.6);
        expect(bRatio, "the minor width is kept").toBeGreaterThan(0.85);
        expect(bRatio).toBeLessThan(1.15);
        expect(redDistance(stretched.shot, majorA), "the opposite end stayed put").toBeLessThan(8);
        // Not onto the pixel: the drop is a terrain point, projected onto the frozen construction
        // plane, and towards the limb the body curves away below that plane. Measured 10 px
        // short here (axis x1.28 for a 35 % pull); a wrong end or no move is 60+ px off.
        expect(redDistance(stretched.shot, outward), "the outline reaches towards the drop").toBeLessThan(20);
        expect(redDistance(stretched.shot, majorB), "and no longer passes the old end").toBeGreaterThan(15);

        // --- 2. Ctrl+Z, with the 3D view focused -------------------------------------------
        // The render control focuses itself on mouseenter, so leave it over the tool strip (an
        // overlay, not part of the control) and come back, as keyboard-undo.spec.ts does.
        await render.bringToFront();
        await render.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
        const strip = await render.locator(".pro3d-tool").first().boundingBox();
        expect(strip, "the tool strip is on the render page").not.toBeNull();
        await render.mouse.move(Math.round(strip!.x + strip!.width / 2), Math.round(strip!.y + strip!.height / 2));
        await render.waitForTimeout(300);
        await render.mouse.move(cx, vp.height - 40);
        await expect
            .poll(() => render.evaluate(() => (document.activeElement as HTMLElement | null)?.className ?? ""),
                  { timeout: 10_000, message: "the render control has focus" })
            .toContain("mainrendercontrol");
        await render.keyboard.press("Control+z");
        await awaitIdle(render, app, 2000);
        const undone = await measure("05-undone");
        expect(undone.fit.a / drawn.fit.a, "undo: major axis as drawn").toBeGreaterThan(0.95);
        expect(undone.fit.a / drawn.fit.a).toBeLessThan(1.05);
        expect(redDistance(undone.shot, majorB), "undo: through the picked end again").toBeLessThan(8);

        // --- 3. widen it by a minor end ----------------------------------------------------
        const again = (await findHandles(undone.shot, "06-handles-again")).blobs;
        expect(again.length, "five handles again after the undo").toBe(5);
        const centre: Px = { x: (majorA.x + majorB.x) / 2, y: (majorA.y + majorB.y) / 2 };
        const minorGrab = again
            .filter((h) => dist(h, majorA) > 20 && dist(h, majorB) > 20 && dist(h, centre) > 20)
            .reduce((best, h) => (dist(h, minorEnd) < dist(best, minorEnd) ? h : best));
        const wider: Px = {
            x: centre.x + 1.5 * (minorGrab.x - centre.x),
            y: centre.y + 1.5 * (minorGrab.y - centre.y),
        };
        await moveHandle(render, app, minorGrab, wider, "07-minor");

        await clickTool(render, SELECT_TOOL);
        const widened = await measure("08-minor-moved");
        const a2 = widened.fit.a / drawn.fit.a;
        const b2 = widened.fit.b / drawn.fit.b;
        console.log(`minor drag: a x${a2.toFixed(3)}, b x${b2.toFixed(3)}`);
        expect(b2, "the minor axis grew by about the 50 % it was pulled").toBeGreaterThan(1.25);
        expect(b2).toBeLessThan(1.8);
        expect(a2, "the major axis is kept").toBeGreaterThan(0.9);
        expect(a2).toBeLessThan(1.1);
        expect(redDistance(widened.shot, majorA), "both major ends stay").toBeLessThan(8);
        expect(redDistance(widened.shot, majorB)).toBeLessThan(8);
    } finally {
        await context.close();
        await app.stop();
    }
});

/** Ticks a toolbar checkbox, found by its tooltip, as supporting-profiles.spec.ts does. */
async function tickToolbar(main: Page, tooltip: string) {
    const iconClass = () =>
        main.evaluate((t) => document.querySelector(`[title="${t}"] i`)?.className ?? "missing", tooltip);
    await expect.poll(iconClass, { timeout: 60_000, intervals: [500] }).toContain("square outline icon");
    await main.evaluate((t) => (document.querySelector(`[title="${t}"] i`) as HTMLElement).click(), tooltip);
    await expect.poll(iconClass, { timeout: 30_000, intervals: [500] }).toContain("check square");
}

/** Pixels of `frame` that differ from `bare`, the same view before anything was drawn: the
 *  annotations, inside the window `win`. */
function annotationMask(bare: Buffer, frame: Buffer, win: { x: number; y: number; w: number; h: number }): Uint8Array {
    const a = PNG.sync.read(bare);
    const b = PNG.sync.read(frame);
    const mask = new Uint8Array(win.w * win.h);
    for (let y = 0; y < win.h; y++)
        for (let x = 0; x < win.w; x++) {
            const j = ((win.y + y) * b.width + win.x + x) * 4;
            const d = Math.abs(b.data[j] - a.data[j]) + Math.abs(b.data[j + 1] - a.data[j + 1]) +
                Math.abs(b.data[j + 2] - a.data[j + 2]);
            if (d > 90) mask[y * win.w + x] = 1;
        }
    return mask;
}

/** Share of pixels within `r` px of `p` that are white, as the centre handle is. */
function whiteShare(png: Buffer, p: Px, r: number): number {
    const img = PNG.sync.read(png);
    let white = 0, all = 0;
    for (let dy = -r; dy <= r; dy++)
        for (let dx = -r; dx <= r; dx++) {
            if (dx * dx + dy * dy > r * r) continue;
            const i = (img.width * (Math.round(p.y) + dy) + Math.round(p.x) + dx) * 4;
            all++;
            if (img.data[i] > 225 && img.data[i + 1] > 225 && img.data[i + 2] > 225) white++;
        }
    return white / all;
}

/** Overlap (intersection over union) of mask `a` moved by (dx, dy) with mask `b`. */
function overlap(a: Uint8Array, b: Uint8Array, w: number, h: number, dx: number, dy: number): number {
    let inter = 0, union = 0;
    for (let y = 0; y < h; y++)
        for (let x = 0; x < w; x++) {
            const sx = x - dx, sy = y - dy;
            const av = sx >= 0 && sy >= 0 && sx < w && sy < h ? a[sy * w + sx] : 0;
            const bv = b[y * w + x];
            if (av && bv) inter++;
            if (av || bv) union++;
        }
    return union === 0 ? 0 : inter / union;
}

/** The shift within +-`r` px of (dx, dy) under which `a` overlaps `b` best. */
function bestShift(a: Uint8Array, b: Uint8Array, w: number, h: number, dx: number, dy: number, r: number) {
    let best = { dx, dy, iou: -1 };
    for (let y = dy - r; y <= dy + r; y += 2)
        for (let x = dx - r; x <= dx + r; x += 2) {
            const iou = overlap(a, b, w, h, x, y);
            if (iou > best.iou) best = { dx: x, dy: y, iou };
        }
    return best;
}

test("the centre handle moves the whole ellipse, its crater profiles follow, and Ctrl+Z puts it back", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    const scene = drawingScene(artifacts, "ellipse-moving");
    const { app, context, render } = await openDrawingViewer(browser, scene, "m00-loaded.png", save);
    try {
        await expect
            .poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("Dimorphos");

        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);
        // nothing drawn yet, the render page the only view: what the annotations are told from
        await parkCursor(render, vp);
        const bare = await settled(render, "m01-bare.png", save);

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        // --- an ellipse with its four crater profiles, generated on finishing it ----------
        await clickTool(render, "Draw annotation");
        await selectByOption(main, "AxisEllipse", "AxisEllipse");
        const thickness = main.locator(".pro3d-secondary-toolbar input[type=number]").first();
        await thickness.fill("2");
        await thickness.press("Enter");
        await main.waitForTimeout(800);
        await tickToolbar(main, "Generate 4 profiles through the centre, rim to rim, when the ellipse is finished");

        const ax = 80, ay = 45, minor = 55;
        const len = Math.hypot(2 * ax, 2 * ay);
        const majorA: Px = { x: cx - ax, y: cy + ay };
        const majorB: Px = { x: cx + ax, y: cy - ay };
        const minorEnd: Px = {
            x: Math.round(cx + ((2 * ay) / len) * minor),
            y: Math.round(cy + ((2 * ax) / len) * minor),
        };
        for (const p of [majorA, majorB]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await closeEllipse(render, app, context, minorEnd);
        await expect
            .poll(async () => (await annotationPointCounts(context, app)).length, { timeout: 180_000, intervals: [5000] })
            .toBe(5);

        // the shared-frustum workaround, as in the test above
        await main.close();
        await render.setViewportSize({ width: 1599, height: 900 });
        await render.waitForTimeout(500);
        await render.setViewportSize({ width: 1600, height: 900 });
        await render.waitForTimeout(1000);

        const win = { x: cx - 300, y: cy - 260, w: 600, h: 520 };
        const frame = async (name: string) => {
            await parkCursor(render, vp);
            return settled(render, `${name}.png`, save);
        };

        await clickTool(render, SELECT_TOOL);
        const before = await frame("m02-drawn");
        const maskBefore = annotationMask(bare, before, win);

        // --- the centre handle ------------------------------------------------------------
        await clickTool(render, EDIT_TOOL);
        await expect.poll(async () => (await tool(render, EDIT_TOOL)).active, { timeout: 30_000 }).toBe(true);
        const withHandles = await frame("m03-handles");
        const handles = handleBlobs(before, withHandles);
        console.log(`axis handles ${JSON.stringify(handles)}`);
        expect(handles.filter((h) => h.color !== "centre").length, "four axis-end handles").toBe(4);
        // The white profiles cross where the white centre handle sits, so the difference to
        // `before` cuts the disc apart, and the crossing itself is nearly solid white. Found as
        // the whitest spot near the middle; that it is a handle shows on hover below.
        let centre: Px = { x: cx, y: cy };
        let solid = 0;
        for (let y = cy - 15; y <= cy + 15; y++)
            for (let x = cx - 15; x <= cx + 15; x++) {
                const w = whiteShare(withHandles, { x, y }, 4);
                if (w > solid) { solid = w; centre = { x, y }; }
            }
        console.log(`centre handle at ${JSON.stringify(centre)}: white ${solid.toFixed(2)}`);
        expect(solid, "a solid white handle in the middle").toBeGreaterThan(0.95);

        // --- move it --------------------------------------------------------------------------
        const shift = { x: 70, y: 40 };
        const target: Px = { x: centre.x + shift.x, y: centre.y + shift.y };
        const inHand = await moveHandle(render, app, centre, target, "m04-centre");
        // with Ctrl held over it, it lit up as a hovered handle: a solid yellow disc
        const hover = PNG.sync.read(fs.readFileSync(path.join(artifacts, "m04-centre-hover.png")));
        let yellow = 0, all = 0;
        for (let dy = -4; dy <= 4; dy++)
            for (let dx = -4; dx <= 4; dx++) {
                if (dx * dx + dy * dy > 16) continue;
                const i = (hover.width * (Math.round(centre.y) + dy) + Math.round(centre.x) + dx) * 4;
                all++;
                if (handleColor(hover.data[i], hover.data[i + 1], hover.data[i + 2]) === "hover") yellow++;
            }
        expect(yellow / all, "the centre is a handle: it turns yellow under the cursor").toBeGreaterThan(0.8);
        expect(greenAt(inHand, centre), "the grabbed centre turns green").toBe(true);
        const preview = PNG.sync.read(fs.readFileSync(path.join(artifacts, "m04-centre-before-drop.png")));
        const greenNear = (p: Px) => {
            for (let dy = -6; dy <= 6; dy++)
                for (let dx = -6; dx <= 6; dx++) {
                    const i = (preview.width * (Math.round(p.y) + dy) + Math.round(p.x) + dx) * 4;
                    const r = preview.data[i], g = preview.data[i + 1], b = preview.data[i + 2];
                    if (g > 150 && g - r > 40 && g - b > 80) return true;
                }
            return false;
        };
        expect(greenNear({ x: majorB.x + shift.x, y: majorB.y + shift.y }) || greenNear({ x: majorA.x + shift.x, y: majorA.y + shift.y }),
            "while in hand, a green outline previews the moved ellipse").toBe(true);

        await expect
            .poll(async () => (await annotationPointCounts(context, app)).length, { timeout: 60_000, intervals: [2000] })
            .toBe(5);
        expect(fs.readFileSync(app.logFile, "utf-8"), "the four profiles were regenerated, not left behind")
            .toMatch(/\[Drawing\] 4 profiles of .* regenerated/);
        await clickTool(render, SELECT_TOOL);
        const after = await frame("m05-moved");
        const maskAfter = annotationMask(bare, after, win);

        const stayed = overlap(maskBefore, maskAfter, win.w, win.h, 0, 0);
        const moved = bestShift(maskBefore, maskAfter, win.w, win.h, shift.x, shift.y, 16);
        console.log(`centre move: overlap in place ${stayed.toFixed(3)}, best ${JSON.stringify(moved)}`);
        // The whole drawing, shifted by the drag, matches the new one far better than in place.
        // Not near 1: 2 px lines a pixel off under the perspective of the new spot already halve
        // the overlap (measured 0.35, against 0.04 in place).
        expect(moved.iou, "outline and profiles moved, at the same size").toBeGreaterThan(0.25);
        expect(moved.iou, "and it is not where it was").toBeGreaterThan(5 * stayed);
        expect(Math.hypot(moved.dx - shift.x, moved.dy - shift.y), "by about the drag").toBeLessThan(16);

        // The profiles in particular: their dense crossing, eight rays from the centre, went
        // along. One profile may still run past the old centre, but no crossing stays there.
        const density = (mask: Uint8Array, p: Px) => {
            let on = 0, all = 0;
            for (let dy = -10; dy <= 10; dy++)
                for (let dx = -10; dx <= 10; dx++) {
                    if (dx * dx + dy * dy > 100) continue;
                    all++;
                    on += mask[(Math.round(p.y) - win.y + dy) * win.w + Math.round(p.x) - win.x + dx];
                }
            return on / all;
        };
        const newCentre: Px = { x: centre.x + moved.dx, y: centre.y + moved.dy };
        const crossing = { before: density(maskBefore, centre), oldPlace: density(maskAfter, centre), newPlace: density(maskAfter, newCentre) };
        console.log(`profile crossing density ${JSON.stringify(crossing)}`);
        expect(crossing.newPlace, "the profiles cross at the new centre").toBeGreaterThan(0.6 * crossing.before);
        expect(crossing.oldPlace, "and no longer at the old one").toBeLessThan(0.5 * crossing.before);

        // --- Ctrl+Z, with the 3D view focused, as in the test above ------------------------
        await render.bringToFront();
        await render.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
        const strip = await render.locator(".pro3d-tool").first().boundingBox();
        expect(strip, "the tool strip is on the render page").not.toBeNull();
        await render.mouse.move(Math.round(strip!.x + strip!.width / 2), Math.round(strip!.y + strip!.height / 2));
        await render.waitForTimeout(300);
        await render.mouse.move(cx, vp.height - 40);
        await expect
            .poll(() => render.evaluate(() => (document.activeElement as HTMLElement | null)?.className ?? ""),
                  { timeout: 10_000, message: "the render control has focus" })
            .toContain("mainrendercontrol");
        await render.keyboard.press("Control+z");
        await awaitIdle(render, app, 2000);
        const undone = annotationMask(bare, await frame("m06-undone"), win);
        const back = overlap(maskBefore, undone, win.w, win.h, 0, 0);
        console.log(`undo: overlap with the drawn ellipse ${back.toFixed(3)}`);
        expect(back, "undo: ellipse and profiles back where they were drawn").toBeGreaterThan(0.7);
    } finally {
        await context.close();
        await app.stop();
    }
});
