import { test, expect, Page } from "@playwright/test";
import { Pro3d } from "../src/pro3d";
import { awaitIdle, clickTool, drawingScene, openDrawingViewer } from "../src/drawing";
import { PNG } from "pngjs";
import { randomUUID } from "crypto";
import * as http from "http";
import * as fs from "fs";
import * as path from "path";

/**
 * Box selection of annotations (docs/AnnotationBoxSelection.md) on the Dimorphos OPC, with
 * thousands of synthetic polylines whose visibility is known by construction.
 *
 * Section 22 in src/Tests covers the hit test and the selection modes against synthetic
 * terrain. This spec covers what only the running app has: occlusion against the real
 * KdTrees at scale, its cost, and the gestures through real pointer and key events.
 *
 * Three sets around the camera axis (the body is ~177 x 174 x 116 m, semi-axes >= 58 m):
 *  - **A, inside**: on a sphere of 30 m, deep inside the body. Never visible.
 *  - **B, in front**: on a sphere of 120 m, within 40 deg of the camera direction, floating
 *    between the camera and the body. Always visible. Magenta.
 *  - **C, behind**: on a sphere of 120 m, within 20 deg of the far side. Every line of
 *    sight to them crosses the body's centre plane within ~31 m of the centre, so the body
 *    hides them all.
 * A Ctrl+drag over the whole view must select exactly B. A hidden annotation is tested at
 * every sample up to the ray cap (32) before it counts as hidden, so the 4000 hidden ones are
 * the expensive part; the viewer logs the cost (`[BoxSelection] ... visibility rays, ... ms`)
 * and the spec prints it.
 *
 * PRO3D_BOX_SCALE multiplies the set sizes (default 1: 2000 + 1000 + 2000 polylines). The
 * selection is read per set from the annotations list, which also has to open at that size.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "box-selection");
const scale = Number(process.env.PRO3D_BOX_SCALE ?? "1");
function setsFor(scale: number) {
    return {
        A: { count: Math.round(2000 * scale), radius: 30, side: 1, cone: 180 },
        B: { count: Math.round(1000 * scale), radius: 120, side: 1, cone: 40 },
        C: { count: Math.round(2000 * scale), radius: 120, side: -1, cone: 20 },
    } as const;
}
type Sets = ReturnType<typeof setsFor>;
type SetName = keyof Sets;
/** the scale test's sets (PRO3D_BOX_SCALE) */
const SETS = setsFor(scale);
/** the modifier-key test's sets: 100 annotations, so every page stays responsive */
const SMALL = setsFor(0.02);
const POINTS_PER_LINE = 12;
/** angular length of one polyline, degrees */
const LINE_SPAN = 3;

test.setTimeout(40 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

type V3 = [number, number, number];
const norm = (v: V3): V3 => {
    const l = Math.hypot(...v);
    return [v[0] / l, v[1] / l, v[2] / l];
};
const cross = (a: V3, b: V3): V3 => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const dot = (a: V3, b: V3) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const add = (a: V3, b: V3, s = 1): V3 => [a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s];
const mul = (a: V3, s: number): V3 => [a[0] * s, a[1] * s, a[2] * s];

/** deterministic PRNG (mulberry32), so every run draws the same annotations */
function rng(seed: number) {
    return () => {
        seed |= 0;
        seed = (seed + 0x6d2b79f5) | 0;
        let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

/** An orthonormal frame around `axis`. */
function frame(axis: V3): [V3, V3] {
    const helper: V3 = Math.abs(axis[2]) < 0.9 ? [0, 0, 1] : [1, 0, 0];
    const u = norm(cross(axis, helper));
    return [u, cross(axis, u)];
}

/** A direction uniformly within `coneDeg` of `axis`. */
function inCone(axis: V3, coneDeg: number, r: () => number): V3 {
    const cosMax = Math.cos((coneDeg * Math.PI) / 180);
    const c = 1 - r() * (1 - cosMax);
    const s = Math.sqrt(Math.max(0, 1 - c * c));
    const phi = 2 * Math.PI * r();
    const [u, v] = frame(axis);
    return norm(add(add(mul(axis, c), u, s * Math.cos(phi)), v, s * Math.sin(phi)));
}

/** A polyline of POINTS_PER_LINE points along a great-circle arc on the sphere `radius`. */
function arc(centre: V3, radius: number, r: () => number): V3[] {
    const [u, v] = frame(centre);
    const phi = 2 * Math.PI * r();
    const t = norm(add(mul(u, Math.cos(phi)), v, Math.sin(phi)));
    const span = (LINE_SPAN * Math.PI) / 180;
    const pts: V3[] = [];
    for (let i = 0; i < POINTS_PER_LINE; i++) {
        const a = span * (i / (POINTS_PER_LINE - 1) - 0.5);
        pts.push(mul(norm(add(mul(centre, Math.cos(a)), t, Math.sin(a))), radius));
    }
    return pts;
}

const vec = (v: number[]) => `[${v.join(", ")}]`;

function polyline(key: string, text: string, pts: V3[], rgb: [number, number, number]) {
    const [x, y, z] = pts[0];
    const color = vec([...rgb, 255]);
    return {
        Annotations: {
            color: { color },
            crossSectionClipping: false,
            dnsResults: null,
            fillAlpha: 0.35,
            fillColor: color,
            geometry: 2, // Polyline
            key,
            manualDipAngle: { format: "{0:0.0}", max: 90, min: 0, step: 0.1, value: null },
            manualDipAzimuth: { format: "{0:0.0}", max: 360, min: 0, step: 0.1, value: null },
            modelTrafo: `[[[1, 0, 0, ${x}], [0, 1, 0, ${y}], [0, 0, 1, ${z}], [0, 0, 0, 1]], [[1, 0, 0, ${-x}], [0, 1, 0, ${-y}], [0, 0, 1, ${-z}], [0, 0, 0, 1]]]`,
            points: { points: pts.map(vec) },
            projection: 0,
            results: null,
            segments: [],
            semantic: 7,
            semanticId: 'SemanticId ""',
            semanticType: 3,
            showDns: false,
            showFill: false,
            showText: false,
            surfaceName: "",
            text,
            textsize: { format: "{0:0.00}", max: 5, min: 0.01, step: 0.01, value: 0.05 },
            thickness: { format: "{0:0}", max: 8, min: 1, step: 1, value: 3 },
            version: 5,
            view: [
                "[0, 0, 1]",
                "[6, 6, 6]",
                "[-0.5773502691896257, -0.5773502691896257, -0.5773502691896257]",
                "[-0.4082482904638631, -0.4082482904638631, 0.8164965809277261]",
                "[-0.7071067811865475, 0.7071067811865475, 0]",
            ],
            visible: true,
        },
    };
}

/** A complete annotation file, for the parts besides the annotations themselves. */
const annotationFileTemplate = JSON.parse(
    fs.readFileSync(path.join(__dirname, "..", "fixtures", "map-projection-annotations.pro3d.ann"), "utf-8").replace(/^﻿/, "")
);

/**
 * The annotation file PRo3D loads with `scene` (`<scene>.ann`): the three sets around
 * `toCamera`, the unit direction from the body centre to the camera.
 */
function writeAnnotations(scene: string, toCamera: V3, sets: Sets) {
    const r = rng(854);
    const flat: any[] = [];
    const colours: Record<SetName, [number, number, number]> = { A: [0, 160, 255], B: [255, 0, 255], C: [255, 160, 0] };
    for (const name of Object.keys(sets) as SetName[]) {
        const s = sets[name];
        const axis = mul(toCamera, s.side) as V3;
        for (let i = 0; i < s.count; i++)
            flat.push(polyline(randomUUID(), `${name}${i}`, arc(inCone(axis, s.cone, r), s.radius, r), colours[name]));
    }
    const ann = {
        annotations: {
            flat,
            groupsLookup: [],
            rootGroup: {
                defaultColor: { color: "[255, 255, 255, 255]" },
                expanded: true,
                key: randomUUID(),
                leaves: flat.map((f) => f.Annotations.key),
                name: "root",
                subNodes: [],
                version: 1,
                visible: true,
            },
            version: 0,
        },
        // the rest of the file as the committed fixture has it
        colorByCategory: annotationFileTemplate.colorByCategory,
        dnsColorLegend: annotationFileTemplate.dnsColorLegend,
        version: annotationFileTemplate.version,
    };
    fs.writeFileSync(scene + ".ann", JSON.stringify(ann));
}

/** The drawing scene (camera CAMERA_DISTANCE from the body centre, looking at it) with the
 *  synthetic annotations beside it. */
function sceneWithAnnotations(name: string, sets: Sets): string {
    let toCamera: V3 = [0, 0, 1];
    const scene = drawingScene(artifacts, name, (d) => {
        toCamera = norm(JSON.parse(d.cameraView.view[1]) as V3);
    });
    writeAnnotations(scene, toCamera, sets);
    return scene;
}

async function renderRect(render: Page) {
    const r = await render.locator("img.rendercontrol").first().boundingBox();
    expect(r, "render control").not.toBeNull();
    return r!;
}

/** Focus the render control first: PRo3D reads Ctrl from key events on the focused element
 *  (drawing.ts:pick has the details). */
async function focusAt(render: Page, x: number, y: number) {
    await render.bringToFront();
    await render.mouse.move(x, y);
    await render.waitForTimeout(400);
}

/**
 * A left drag from `a` to `b` with Ctrl held, through real pointer events. The move handler
 * is attached only once the server has processed the press, hence the pauses. With
 * `releaseCtrlAt` the Ctrl key goes up after that fraction of the drag and `onRelease` runs.
 * Resolves right after the button is released.
 */
async function ctrlDrag(
    render: Page,
    a: [number, number],
    b: [number, number],
    opts: { releaseCtrlAt?: number; onRelease?: () => Promise<void> } = {}
) {
    await focusAt(render, a[0], a[1]);
    await render.keyboard.down("Control");
    await render.waitForTimeout(300);
    await render.mouse.down();
    await render.waitForTimeout(600);
    const steps = 10;
    let ctrl = true;
    for (let i = 1; i <= steps; i++) {
        const t = i / steps;
        await render.mouse.move(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t);
        await render.waitForTimeout(80);
        if (ctrl && opts.releaseCtrlAt !== undefined && t >= opts.releaseCtrlAt) {
            await render.keyboard.up("Control");
            ctrl = false;
            await render.waitForTimeout(500);
            await opts.onRelease?.();
        }
    }
    await render.waitForTimeout(300);
    await render.mouse.up();
    if (ctrl) {
        await render.waitForTimeout(100);
        await render.keyboard.up("Control");
    }
}

async function ctrlClick(render: Page, x: number, y: number) {
    await focusAt(render, x, y);
    await render.keyboard.down("Control");
    await render.waitForTimeout(200);
    await render.mouse.click(x, y);
    await render.waitForTimeout(200);
    await render.keyboard.up("Control");
}

/** Whether the selection rectangle is on screen (ViewerGUI.selectionRectangle). */
async function rectangleShown(page: Page) {
    return page.evaluate(() =>
        Array.from(document.querySelectorAll("rect")).some(
            (r) => (r.getAttribute("style") ?? "").includes("stroke:green") && r.getAttribute("visibility") !== "hidden"
        )
    );
}

interface Row {
    name: string;
    cube: string;
    header: string;
}

/** Per annotation on the annotations page: its name (the set letter and an index), its cube
 *  icon colour (green when in the multi-selection) and its header colour (green when
 *  single-selected). */
async function listState(page: Page): Promise<Row[]> {
    return page.evaluate(() =>
        Array.from(document.querySelectorAll(".item"))
            .filter((it) => it.firstElementChild?.matches("i.cube.icon"))
            .map((it) => {
                const header = it.querySelector(".header") as HTMLElement | null;
                return {
                    name: ((header?.textContent ?? "").trim().split(" ")[0]) ?? "",
                    cube: (it.firstElementChild as HTMLElement).style.color,
                    header: header?.style.color ?? "",
                };
            })
    );
}

interface Selection {
    multi: Record<SetName, number>;
    multiTotal: number;
    single: number;
}

/** Selection relative to the state at load, when nothing was selected. */
async function selection(page: Page, baseline: Map<string, Row>): Promise<Selection> {
    const now = await listState(page);
    const multi: Record<SetName, number> = { A: 0, B: 0, C: 0 };
    let single = 0;
    for (const row of now) {
        const base = baseline.get(row.name);
        if (!base) continue;
        if (row.cube !== base.cube) multi[row.name[0] as SetName]++;
        if (row.header !== base.header) single++;
    }
    return { multi, multiTotal: multi.A + multi.B + multi.C, single };
}

interface Readout {
    /** multi-selected per set */
    perSet: Record<SetName, number>;
    /** multi-selected in all */
    total: number;
    /** something is single-selected */
    single: boolean;
}

interface Reader {
    read(): Promise<Readout>;
}

async function listReader(page: Page): Promise<Reader> {
    const baseline = new Map((await listState(page)).map((r) => [r.name, r] as [string, Row]));
    return {
        read: async () => {
            const s = await selection(page, baseline);
            return { perSet: s.multi, total: s.multiTotal, single: s.single > 0 };
        },
    };
}

/** Polls until `done` holds, returns the readout and how long that took. */
async function readWhen(reader: Reader, page: Page, done: (s: Readout) => boolean, timeoutMs = 120_000) {
    const t0 = Date.now();
    let s = await reader.read();
    while (!done(s) && Date.now() - t0 < timeoutMs) {
        await page.waitForTimeout(250);
        s = await reader.read();
    }
    return { s, ms: Date.now() - t0 };
}

/** Page pixels of the B set's magenta that lie on the edge of a drawn line: magenta with a
 *  non-magenta 4-neighbour. */
function magentaEdges(png: Buffer): Array<[number, number]> {
    const img = PNG.sync.read(png);
    const magenta = (x: number, y: number) => {
        if (x < 0 || y < 0 || x >= img.width || y >= img.height) return false;
        const i = (img.width * y + x) * 4;
        const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
        return r > 170 && b > 170 && g < 90;
    };
    const out: Array<[number, number]> = [];
    for (let y = 0; y < img.height; y++)
        for (let x = 0; x < img.width; x++)
            if (magenta(x, y) && !(magenta(x - 1, y) && magenta(x + 1, y) && magenta(x, y - 1) && magenta(x, y + 1)))
                out.push([x, y]);
    return out;
}

/**
 * Polls the busy indicator's lock-free `/busy` route (docs/BusyIndicator.md) until `stop`, and
 * returns every operation label it reported with the longest time seen for each. The route
 * answers while the update thread holds the app lock, which is the point: the pill is the
 * only feedback the user gets during a long box selection.
 */
function watchBusy(app: Pro3d) {
    const seen = new Map<string, number>();
    let running = true;
    const once = () =>
        new Promise<void>((resolve) => {
            const req = http.get(`${app.url}busy`, (res) => {
                let body = "";
                res.on("data", (c) => (body += c));
                res.on("end", () => {
                    try {
                        const b = JSON.parse(body) as { busy: boolean; op?: string; ms?: number };
                        if (b.busy && b.op) seen.set(b.op, Math.max(seen.get(b.op) ?? 0, b.ms ?? 0));
                    } catch {}
                    resolve();
                });
            });
            req.on("error", () => resolve());
            req.setTimeout(5000, () => req.destroy());
        });
    const loop = (async () => {
        while (running) {
            await once();
            await new Promise((r) => setTimeout(r, 100));
        }
    })();
    return {
        stop: async () => {
            running = false;
            await loop;
            return seen;
        },
    };
}

/**
 * Clicks the "Direct Tool Mode" checkbox on the main page and returns its class afterwards
 * ("check square outline icon" when on). The checkbox is the square-outline icon nearest to
 * that label; the label sits in a top-menu dropdown, so it is located by text, not position.
 */
async function toggleDirectToolMode(main: Page): Promise<string> {
    const find = `(() => {
        const icons = Array.from(document.querySelectorAll("i")).filter((i) => /square outline icon/.test(i.className));
        let best = null, bestLen = 1e9;
        for (const i of icons) {
            let n = i;
            for (let k = 0; k < 4 && n; k++) {
                n = n.parentElement;
                const t = (n && n.textContent) || "";
                if (t.includes("Direct Tool Mode")) { if (t.length < bestLen) { best = i; bestLen = t.length; } break; }
            }
        }
        return best;
    })()`;
    const state = async () => (await main.evaluate(`(() => { const b = ${find}; return b ? b.className : "none"; })()`)) as string;
    const before = await state();
    const clicked = await main.evaluate(`(() => { const b = ${find}; if (b) b.click(); return !!b; })()`);
    expect(clicked, "Direct Tool Mode checkbox").toBe(true);
    // the page reflects the toggle over the incremental DOM channel; with thousands of
    // annotations that takes seconds, so wait for the change rather than a fixed time
    await expect.poll(state, { timeout: 60_000, intervals: [250, 500, 1000] }).not.toBe(before);
    return await state();
}

/** The viewer's own measurement of the last box hit tests. */
function boxSelectionLog(app: Pro3d): string[] {
    try {
        return fs.readFileSync(app.logFile, "utf-8").split(/\r?\n/).filter((l) => l.includes("[BoxSelection]"));
    } catch {
        return [];
    }
}

test("box selection: thousands of annotations, occlusion by the body, gestures", async ({ browser }) => {
    const total = SETS.A.count + SETS.B.count + SETS.C.count;
    const { app, context, render } = await openDrawingViewer(browser, sceneWithAnnotations("box", SETS), "loaded.png", save);
    try {
        const panel = await context.newPage();
        await panel.goto(app.url + "?page=annotations");
        const tList = Date.now();
        await expect
            .poll(async () => (await listState(panel)).length, { timeout: 300_000, message: "annotations listed" })
            .toBe(total);
        console.log(`[box-selection] annotations list of ${total} up after ${Date.now() - tList} ms`);
        await clickTool(render, "Select annotation");
        await awaitIdle(render, app);
        await render.waitForTimeout(2000);
        const reader = await listReader(panel);
        const shot = await render.screenshot();
        save("annotations.png", shot);

        const r = await renderRect(render);
        const at = (fx: number, fy: number): [number, number] => [r.x + r.width * fx, r.y + r.height * fy];

        // 1. Ctrl+click on the edge of a B line selects it, and it stays selected after the
        // release. Polled: the click's occlusion check first loads KdTree patches, which takes
        // seconds without a log line.
        const edges = magentaEdges(shot).filter(
            ([x, y]) => x > r.x + 0.1 * r.width && x < r.x + 0.85 * r.width && y > r.y + 0.1 * r.height && y < r.y + 0.9 * r.height
        );
        expect(edges.length, "B annotations drawn (magenta)").toBeGreaterThan(100);
        const [ex, ey] = edges[Math.floor(edges.length / 2)];
        const clickBusy = watchBusy(app);
        await ctrlClick(render, ex, ey);
        const clicked = await readWhen(reader, panel, (s) => s.single || s.total > 0, 60_000);
        const clickBusySeen = await clickBusy.stop();
        console.log(`[box-selection] busy during the click: ${JSON.stringify([...clickBusySeen])}`);
        // the click's occlusion ray loads cold KdTree patches: seconds, so the pill must say so
        expect(clickBusySeen.has("picking"), "busy indicator reports the click's pick").toBe(true);
        expect(clicked.s.single || clicked.s.total > 0, `Ctrl+click at (${ex},${ey}) on a line edge selects it`).toBe(true);
        await render.waitForTimeout(1500);
        save("1-click-edge.png", await render.screenshot());
        const later = await reader.read();
        expect(later.single || later.total > 0, "and it stays selected after the release").toBe(true);

        // 2. Ctrl+drag over the whole view: exactly the B set, none of the hidden ones. It stops
        // at x 0.88 to stay clear of the tool strip on the right edge; B (within 40 deg of the
        // view axis at 120 m, seen from 350 m) is drawn well inside that, see annotations.png
        const boxBusy = watchBusy(app);
        await ctrlDrag(render, at(0.02, 0.03), at(0.88, 0.97));
        const all = await readWhen(reader, panel, (s) => s.total >= SETS.B.count, 300_000);
        const busy = await boxBusy.stop();
        console.log(`[box-selection] busy during the box: ${JSON.stringify([...busy])}`);
        // the hit test blocks the update thread for seconds; the pill has to say so
        expect(busy.has("selecting annotations"), "busy indicator reports the box selection").toBe(true);
        await awaitIdle(render, app, 2000);
        const allFinal = await reader.read();
        save("2-box-all.png", await render.screenshot());
        console.log(`[box-selection] ${total} annotations: selection on the page ${all.ms} ms after release`);
        for (const l of boxSelectionLog(app)) console.log(`[box-selection] viewer: ${l.trim()}`);
        expect(allFinal.perSet, "box over the view selects exactly the visible set").toEqual({ A: 0, B: SETS.B.count, C: 0 });

        // 3. Ctrl+click on empty space (a sky corner): selection unchanged
        await ctrlClick(render, ...at(0.04, 0.95));
        await awaitIdle(render, app, 2000);
        expect((await reader.read()).total, "click on empty space keeps the selection").toBe(SETS.B.count);

        // 4. the rectangle shows while dragging with Ctrl ...
        await focusAt(render, ...at(0.02, 0.03));
        await render.keyboard.down("Control");
        await render.waitForTimeout(300);
        await render.mouse.down();
        await render.waitForTimeout(600);
        await render.mouse.move(...at(0.1, 0.12), { steps: 5 });
        await render.waitForTimeout(500);
        const shownWhileHeld = await rectangleShown(render);
        save("4-rectangle.png", await render.screenshot());
        await render.keyboard.press("Escape");
        await render.waitForTimeout(300);
        await render.mouse.up();
        await render.keyboard.up("Control");
        expect(shownWhileHeld, "rectangle shown while dragging with Ctrl").toBe(true);

        // ... and goes as soon as Ctrl is released, without a mouse move; the release then
        // changes nothing, though a completed drag here would have cleared the selection
        await focusAt(render, ...at(0.02, 0.03));
        await render.keyboard.down("Control");
        await render.waitForTimeout(300);
        await render.mouse.down();
        await render.waitForTimeout(600);
        await render.mouse.move(...at(0.1, 0.12), { steps: 5 });
        await render.waitForTimeout(500);
        // this very gesture has to be live, or "hidden" and "unchanged" below pass vacuously
        expect(await rectangleShown(render), "rectangle shown before Ctrl is released").toBe(true);
        await render.keyboard.up("Control");
        await render.waitForTimeout(800);
        const shownAfterRelease = await rectangleShown(render);
        await render.mouse.up();
        await awaitIdle(render, app, 2000);
        expect(shownAfterRelease, "rectangle hidden right after Ctrl is released").toBe(false);
        expect((await reader.read()).total, "Ctrl released mid-drag: selection unchanged").toBe(SETS.B.count);

        // 5. a full Ctrl+drag over empty sky replaces the selection with nothing
        await ctrlDrag(render, at(0.02, 0.03), at(0.1, 0.12));
        const cleared = await readWhen(reader, panel, (s) => s.total === 0, 60_000);
        expect(cleared.s.total, "box over empty sky clears").toBe(0);

    } finally {
        await context.close();
        await app.stop();
    }
});

test("modifier keys: Ctrl (Cmd on macOS) arms the tool, Alt toggles, focus loss releases", async ({ browser }) => {
    // Small on purpose: these are gesture checks, and at thousands of annotations the main
    // page (which embeds the annotation list, #900) is too busy to take the Direct Tool Mode
    // click for minutes.
    const sets = SMALL;
    const total = sets.A.count + sets.B.count + sets.C.count;
    const { app, context, render } = await openDrawingViewer(browser, sceneWithAnnotations("modifiers", sets), "modifiers-loaded.png", save);
    try {
        const panel = await context.newPage();
        await panel.goto(app.url + "?page=annotations");
        await expect
            .poll(async () => (await listState(panel)).length, { timeout: 300_000, message: "annotations listed" })
            .toBe(total);
        await clickTool(render, "Select annotation");
        await awaitIdle(render, app);
        await render.waitForTimeout(2000);
        const reader = await listReader(panel);
        const r = await renderRect(render);
        const at = (fx: number, fy: number): [number, number] => [r.x + r.width * fx, r.y + r.height * fy];

        // warm the KdTrees the gestures below hit, so their timing is not a patch load
        await ctrlDrag(render, at(0.02, 0.03), at(0.88, 0.97));
        await readWhen(reader, panel, (s) => s.total >= sets.B.count, 120_000);
        await ctrlDrag(render, at(0.02, 0.03), at(0.1, 0.12));
        const cleared = await readWhen(reader, panel, (s) => s.total === 0, 60_000);
        expect(cleared.s.total, "starts with nothing selected").toBe(0);

        // ---- modifiers (Viewer-Utils.fs `Modifier`): only Ctrl (and Cmd on macOS) arms the
        // tool. Alt used to as well, which broke the Alt toggle below.

        // 6. Alt+click on a B line, no Ctrl: Alt is not the tool modifier, nothing is picked
        const picks = () => (fs.readFileSync(app.logFile, "utf-8").match(/\[DrawingApp\] (single|multi) select/g) ?? []).length;
        const edges2 = magentaEdges(await render.screenshot()).filter(
            ([x, y]) => x > r.x + 0.1 * r.width && x < r.x + 0.85 * r.width && y > r.y + 0.1 * r.height && y < r.y + 0.9 * r.height
        );
        const [ax, ay] = edges2[Math.floor(edges2.length / 3)];
        const picks0 = picks();
        await focusAt(render, ax, ay);
        await render.keyboard.down("Alt");
        await render.waitForTimeout(250);
        await render.mouse.click(ax, ay);
        await render.waitForTimeout(250);
        await render.keyboard.up("Alt");
        await render.waitForTimeout(3000);
        await awaitIdle(render, app, 2000);
        expect(picks() - picks0, "Alt+click without Ctrl picks nothing").toBe(0);
        expect((await reader.read()).total, "and selects nothing").toBe(0);

        // 7. Ctrl+Alt+drag (toggle), Alt released before the mouse: Ctrl still holds the gesture
        await focusAt(render, ...at(0.02, 0.03));
        await render.keyboard.down("Control");
        await render.waitForTimeout(250);
        await render.keyboard.down("Alt");
        await render.waitForTimeout(250);
        await render.mouse.down();
        await render.waitForTimeout(600);
        await render.mouse.move(...at(0.88, 0.97), { steps: 10 });
        await render.waitForTimeout(500);
        await render.keyboard.up("Alt");
        await render.waitForTimeout(600);
        expect(await rectangleShown(render), "releasing Alt keeps the rectangle while Ctrl is held").toBe(true);
        // the toggle reads Alt at the release, so press it again for the release (as a user would
        // hold it); the point above is only that the Alt key-up does not cancel the gesture
        await render.keyboard.down("Alt");
        await render.waitForTimeout(250);
        await render.mouse.up();
        await render.waitForTimeout(150);
        await render.keyboard.up("Alt");
        await render.keyboard.up("Control");
        const toggled = await readWhen(reader, panel, (s) => s.total >= sets.B.count, 120_000);
        expect(toggled.s.perSet, "Ctrl+Alt+drag toggles the visible set on").toEqual({ A: 0, B: sets.B.count, C: 0 });

        // 8. Direct Tool Mode: Alt+drag is the toggle (no Ctrl). Alt used to disarm the tool and
        // orbit the camera instead.
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForTimeout(3000);
        expect(await toggleDirectToolMode(main), "Direct Tool Mode on").toMatch(/check/);
        await render.bringToFront();
        const beforeAltDrag = await render.screenshot();
        await focusAt(render, ...at(0.02, 0.03));
        await render.keyboard.down("Alt");
        await render.waitForTimeout(250);
        await render.mouse.down();
        await render.waitForTimeout(600);
        await render.mouse.move(...at(0.88, 0.97), { steps: 10 });
        await render.waitForTimeout(500);
        const shownDtm = await rectangleShown(render);
        await render.mouse.up();
        await render.waitForTimeout(150);
        await render.keyboard.up("Alt");
        const toggledOff = await readWhen(reader, panel, (s) => s.total === 0, 120_000);
        save("8-dtm-alt-drag.png", await render.screenshot());
        expect(shownDtm, "Direct Tool Mode: Alt+drag draws the rectangle").toBe(true);
        expect(toggledOff.s.perSet, "Direct Tool Mode: Alt+drag toggles the visible set off").toEqual({ A: 0, B: 0, C: 0 });
        const { diffPng } = require("../src/image");
        expect(diffPng(beforeAltDrag, await render.screenshot()).changedFraction, "the camera did not move").toBeLessThan(0.05);
        expect(await toggleDirectToolMode(main), "Direct Tool Mode off").not.toMatch(/check/);
        await render.bringToFront();

        // 9. Losing focus releases Ctrl: a key-up elsewhere (Cmd+Tab, Alt+Tab) must not leave
        // ctrlFlag stuck, which would freeze the camera. Hold Ctrl, move focus away, let go
        // there, then a plain drag must move the camera and draw no rectangle.
        await focusAt(render, ...at(0.5, 0.5));
        await render.keyboard.down("Control");
        await render.waitForTimeout(300);
        await main.bringToFront();
        await main.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
        await render.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
        await render.waitForTimeout(300);
        await main.keyboard.up("Control");
        await render.bringToFront();
        await render.waitForTimeout(800);
        const beforePlain = await render.screenshot();
        await render.mouse.move(...at(0.4, 0.5));
        await render.mouse.down();
        await render.mouse.move(...at(0.6, 0.55), { steps: 10 });
        const shownPlain = await rectangleShown(render);
        await render.mouse.up();
        await render.waitForTimeout(1500);
        const moved = diffPng(beforePlain, await render.screenshot()).changedFraction;
        expect(shownPlain, "after focus loss, a plain drag is not a box").toBe(false);
        expect(moved, "after focus loss, a plain drag moves the camera (Ctrl not stuck)").toBeGreaterThan(0.05);
    } finally {
        await context.close();
        await app.stop();
    }
});
