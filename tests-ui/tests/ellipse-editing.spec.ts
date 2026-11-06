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
 *  - the four handles are drawn, at the ends of the axes, as soon as Edit annotation is on;
 *  - Ctrl+click on one picks it up (it turns green), Ctrl+click on the surface drops it;
 *  - the outline on screen is rebuilt and draped: the major end moved, the opposite end
 *    stayed, the minor width kept -- and for a minor end the other way round;
 *  - Ctrl+Z brings the drawn ellipse back.
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

/**
 * The edit handles in `withHandles`: blobs of handle white (VertexHandleShader: pure white
 * when neither hovered nor grabbed) that are not in `without`, the same view before Edit
 * annotation was switched on. The difference is what makes this safe on a bright terrain:
 * a saturated pixel of the texture is in both frames.
 */
function handleBlobs(without: Buffer, withHandles: Buffer): Array<Px & { area: number }> {
    const a = PNG.sync.read(without);
    const b = PNG.sync.read(withHandles);
    const w = b.width, h = b.height;
    const mask = new Uint8Array(w * h);
    for (let i = 0; i < w * h; i++) {
        const j = i * 4;
        const r = b.data[j], g = b.data[j + 1], bl = b.data[j + 2];
        // white, or hover yellow V4f(1, 0.85, 0.1): the hover is only updated by moves over the
        // annotations, so the last handle touched can stay yellow after the cursor has left
        const white = (r > 230 && g > 230 && bl > 230) || (r > 230 && g > 180 && bl < 90);
        const changed =
            Math.abs(b.data[j] - a.data[j]) + Math.abs(b.data[j + 1] - a.data[j + 1]) +
            Math.abs(b.data[j + 2] - a.data[j + 2]) > 60;
        if (white && changed) mask[i] = 1;
    }
    const blobs: Array<Px & { area: number }> = [];
    const stack: number[] = [];
    for (let start = 0; start < w * h; start++) {
        if (mask[start] !== 1) continue;
        let sx = 0, sy = 0, n = 0;
        mask[start] = 2;
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
                    if (mask[k] === 1) { mask[k] = 2; stack.push(k); }
                }
        }
        if (n >= 10) blobs.push({ x: sx / n, y: sy / n, area: n });
    }
    // The selected outline's white core runs through every handle and is white in both
    // frames, so the difference cuts each disc into two halves. Pieces closer than a handle's
    // radius (thickness + 6 px) belong to the same disc.
    const merged: Array<Px & { area: number }> = [];
    for (const b of blobs.sort((p, q) => q.area - p.area)) {
        const m = merged.find((x) => Math.hypot(x.x - b.x, x.y - b.y) < 10);
        if (m) {
            const n = m.area + b.area;
            m.x = (m.x * m.area + b.x * b.area) / n;
            m.y = (m.y * m.area + b.y * b.area) / n;
            m.area = n;
        } else merged.push({ ...b });
    }
    // a whole handle is ~150 px; anything far smaller is a highlight, not a handle
    return merged.filter((b) => b.area >= 60);
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
        for (const p of [majorA, majorB, minorEnd]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await awaitIdle(render, app);
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
            return blobs;
        };

        const handles = await findHandles(drawn.shot, "02-handles");
        expect(handles.length, "four handles: two ends per axis").toBe(4);
        const nearest = (p: Px) =>
            handles.reduce((best, h) => (dist(h, p) < dist(best, p) ? h : best), handles[0]);
        // the major ends are where they were clicked (the drape lands the plane point on the
        // picked surface point); the minor ends are wherever the plane construction put them
        expect(dist(nearest(majorA), majorA), "a handle on the first major end").toBeLessThan(8);
        expect(dist(nearest(majorB), majorB), "a handle on the second major end").toBeLessThan(8);
        const minorHandles = handles.filter((h) => dist(h, majorA) > 20 && dist(h, majorB) > 20);
        expect(minorHandles.length, "the other two sit off the major axis").toBe(2);

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
        const again = await findHandles(undone.shot, "06-handles-again");
        expect(again.length, "four handles again after the undo").toBe(4);
        const centre: Px = { x: (majorA.x + majorB.x) / 2, y: (majorA.y + majorB.y) / 2 };
        const minorGrab = again
            .filter((h) => dist(h, majorA) > 20 && dist(h, majorB) > 20)
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
