import { test, expect, Page } from "@playwright/test";
import { clickTool, drawingScene, openDrawingViewer, pick, settled } from "../src/drawing";
import { PNG } from "pngjs";
import { randomUUID } from "crypto";
import * as fs from "fs";
import * as path from "path";

/**
 * The annotations list with thousands of annotations (#900, docs/AnnotationList.md).
 *
 * The list is windowed: only the rows in and around the visible part of a group's scroll
 * box are in the DOM. Before, 5000 annotations took the browser 2.5-4.5 minutes to lay out.
 * This spec loads PRO3D_LIST_COUNT (default 5000) polylines into one group and checks that
 * the list is up within seconds, that the DOM holds a screenful of rows, that scrolling
 * reaches the last annotation, and that a row still selects on click.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "annotation-list");
const COUNT = Number(process.env.PRO3D_LIST_COUNT ?? "5000");

test.setTimeout(15 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

const vec = (v: number[]) => `[${v.join(", ")}]`;

/** a short polyline beside the body, `i` spreading them out on a ring; where they lie does
 *  not matter to the list */
function polyline(i: number) {
    const a = (i / COUNT) * 2 * Math.PI;
    const pts = [0, 1, 2].map((k) => [120 * Math.cos(a + k * 0.001), 120 * Math.sin(a + k * 0.001), 10]);
    return polylineThrough(i, pts, [255, 160, 0]);
}

/** polyline `N<i>` through `pts` (body-fixed metres) */
function polylineThrough(i: number, pts: number[][], rgb: number[]) {
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
            key: randomUUID(),
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
            text: `N${i}`,
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

function sceneWithAnnotations(): string {
    const scene = drawingScene(artifacts, "list");
    const template = JSON.parse(
        fs.readFileSync(path.join(__dirname, "..", "fixtures", "map-projection-annotations.pro3d.ann"), "utf-8").replace(/^﻿/, "")
    );
    const flat = Array.from({ length: COUNT }, (_, i) => polyline(i));
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
        colorByCategory: template.colorByCategory,
        dnsColorLegend: template.dnsColorLegend,
        version: template.version,
    };
    fs.writeFileSync(scene + ".ann", JSON.stringify(ann));
    return scene;
}

/** names of the annotation rows in the DOM, in order */
async function rowNames(page: Page): Promise<string[]> {
    return page.evaluate(() =>
        Array.from(document.querySelectorAll(".item"))
            .filter((it) => it.firstElementChild?.matches("i.cube.icon"))
            .map((it) => (it.querySelector(".header")?.textContent ?? "").trim().split(" ")[0])
    );
}

/** whether the row named `name` is in the DOM and inside its scroll box's visible part */
async function inView(page: Page, name: string): Promise<boolean> {
    return page.evaluate((name) => {
        const row = Array.from(document.querySelectorAll(".item")).find(
            (it) => it.firstElementChild?.matches("i.cube.icon") && (it.querySelector(".header")?.textContent ?? "").trim().split(" ")[0] === name
        );
        const box = row?.parentElement;
        if (!row || !box) return false;
        const r = row.getBoundingClientRect();
        const b = box.getBoundingClientRect();
        return r.top >= b.top - 1 && r.bottom <= b.bottom + 1 && b.bottom > 0 && b.top < window.innerHeight;
    }, name);
}

/** the scroll box holding the annotation rows */
function scrollBox(page: Page) {
    return page.locator("div.list", { has: page.locator(":scope > .item > i.cube.icon") }).first();
}

test("annotation list: thousands of annotations are listed within seconds", async ({ browser }) => {
    const { app, context } = await openDrawingViewer(browser, sceneWithAnnotations(), "loaded.png", save);
    try {
        const panel = await context.newPage();
        await panel.setViewportSize({ width: 500, height: 900 });
        const t0 = Date.now();
        await panel.goto(app.url + "?page=annotations");
        await expect.poll(async () => (await rowNames(panel))[0], { timeout: 120_000, message: "first row listed" }).toBe("N0");
        const tList = Date.now() - t0;
        const rows = await rowNames(panel);
        console.log(`[annotation-list] ${COUNT} annotations: first rows after ${tList} ms, ${rows.length} rows in the DOM`);
        save("top.png", await panel.screenshot());
        expect(tList, "list up").toBeLessThan(30_000);
        expect(rows.length, "rows in the DOM").toBeLessThan(150);
        // windowing needs every row at the same fixed height: nothing may be cut off
        const clipped = await panel.evaluate(() =>
            Array.from(document.querySelectorAll(".item"))
                .filter((it) => it.firstElementChild?.matches("i.cube.icon"))
                .filter((it) => it.scrollHeight > it.clientHeight + 1)
                .map((it) => `${(it.querySelector(".header")?.textContent ?? "").trim()}: ${it.scrollHeight} > ${it.clientHeight}`)
        );
        expect(clipped, "clipped rows").toEqual([]);

        // the scroll box spans all rows: scrolling to its end shows the last annotation
        const box = scrollBox(panel);
        await box.evaluate((el) => (el.scrollTop = el.scrollHeight));
        await expect
            .poll(async () => (await rowNames(panel)).at(-1), { timeout: 30_000, message: "last row listed" })
            .toBe(`N${COUNT - 1}`);
        save("bottom.png", await panel.screenshot());

        // a row in the middle, reached by scrolling, selects on click (header turns green)
        const mid = Math.floor(COUNT / 2);
        await box.evaluate((el, f) => (el.scrollTop = el.scrollHeight * f), mid / COUNT);
        const header = panel.locator(".item .header > span", { hasText: new RegExp(`^N${mid} `) });
        await expect(header).toBeVisible({ timeout: 30_000 });
        await header.click();
        await expect
            .poll(async () => header.evaluate((el) => (el.parentElement as HTMLElement).style.color), { timeout: 30_000 })
            .not.toBe("rgb(128, 128, 128)");
        save("selected.png", await panel.screenshot());

        // A selection made elsewhere scrolls the list to it. To a second annotations page, a
        // click on the first is such a selection, the same path as a pick in 3D.
        const other = await context.newPage();
        await other.setViewportSize({ width: 500, height: 900 });
        await other.goto(app.url + "?page=annotations");
        // opened with N<mid> selected: it shows N<mid>
        await expect.poll(() => inView(other, `N${mid}`), { timeout: 60_000, message: "opens at the selection" }).toBe(true);
        await scrollBox(other).evaluate((el) => (el.scrollTop = 0));
        await expect.poll(() => inView(other, "N0"), { timeout: 30_000 }).toBe(true);

        const target = Math.floor(COUNT * 0.8);
        await box.evaluate((el, f) => (el.scrollTop = el.scrollHeight * f), target / COUNT);
        const targetHeader = panel.locator(".item .header > span", { hasText: new RegExp(`^N${target} `) });
        await expect(targetHeader).toBeVisible({ timeout: 30_000 });
        await targetHeader.click();
        // a background tab gets no animation frames, and the list reports its window in one
        await other.bringToFront();
        await expect.poll(() => inView(other, `N${target}`), { timeout: 30_000, message: "follows the selection" }).toBe(true);
        save("followed.png", await other.screenshot());
    } finally {
        await context.close();
        await app.stop();
    }
});

// ---- a single pick in 3D opens the folder and scrolls the list to the annotation --------

/** Dimorphos' SPC shape model (body-fixed, km), the source of the surface annotations */
const SHAPE_MODEL =
    process.env.PRO3D_DIMORPHOS_OBJ ?? "C:\\pro3ddata\\shapemodels\\dimorphos_g_1960mm_spc_obj_0000n00000_v003.obj";
const FOLDERS = 5;

type V3 = [number, number, number];
const sub = (a: V3, b: V3): V3 => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const dot = (a: V3, b: V3) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a: V3, b: V3): V3 => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = (a: V3): V3 => {
    const l = Math.hypot(...a) || 1;
    return [a[0] / l, a[1] / l, a[2] / l];
};

interface Face {
    m: V3;
    n: V3;
    c: V3[];
}

/** surface triangles of the shape model in metres: centroid, outward normal, corners */
function shapeModelFaces(): Face[] {
    const v: V3[] = [];
    const faces: Face[] = [];
    for (const line of fs.readFileSync(SHAPE_MODEL, "utf-8").split(/\r?\n/)) {
        const t = line.trim().split(/\s+/);
        if (t[0] === "v") v.push([1000 * +t[1], 1000 * +t[2], 1000 * +t[3]]);
        else if (t[0] === "f") {
            const c = t.slice(1, 4).map((s) => v[parseInt(s, 10) - 1]);
            if (c.some((p) => !p)) continue;
            const m: V3 = [0, 1, 2].map((j) => (c[0][j] + c[1][j] + c[2][j]) / 3) as V3;
            let n = norm(cross(sub(c[1], c[0]), sub(c[2], c[0])));
            if (dot(n, m) < 0) n = [-n[0], -n[1], -n[2]];
            faces.push({ m, n, c });
        }
    }
    return faces;
}

/**
 * COUNT surface triangles as polylines (each enlarged 2.5x about its centroid, lifted 0.5 m)
 * in FOLDERS closed folders under an open root. The scene body is Dimorphos (drawingScene).
 * One of them, `target`, is magenta and lies on the face nearest the camera close to the view
 * axis, so it is in view and not hidden by the body.
 */
function sceneOnSurface(): { scene: string; target: number } {
    let camera: V3 = [0, 0, 1];
    const scene = drawingScene(artifacts, "surface", (d) => {
        camera = JSON.parse(d.cameraView.view[1]) as V3;
    });
    const faces = shapeModelFaces();
    const toCam = norm(camera);
    // facing the camera, within ~8 deg of the axis; the nearest of those is unoccluded
    const front = faces
        .filter((f) => dot(f.n, norm(sub(camera, f.m))) > 0.7 && dot(norm(f.m), toCam) > 0.99)
        .sort((a, b) => Math.hypot(...sub(a.m, camera)) - Math.hypot(...sub(b.m, camera)));
    expect(front.length, "faces facing the camera near the view axis").toBeGreaterThan(0);
    const targetFace = front[0];

    // deterministic spread over the rest of the model
    let seed = 900;
    const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
    // nothing within 10 m of the target: the enlarged triangles overlap, and a neighbour on
    // top of it took the click (N4756 instead of the target, folder 4 opened)
    const others = faces.filter((f) => Math.hypot(...sub(f.m, targetFace.m)) > 10);
    const chosen: Face[] = [];
    while (chosen.length < COUNT - 1) chosen.push(others[Math.floor(rnd() * others.length)]);
    const target = Math.floor(COUNT * 0.7); // in folder 3 of 5
    chosen.splice(target, 0, targetFace);

    const flat = chosen.map((f, i) => {
        const pts = [...f.c, f.c[0]].map((p) => [0, 1, 2].map((j) => f.m[j] + 2.5 * (p[j] - f.m[j]) + 0.5 * f.n[j]));
        // blue, not orange: the loading-splash check (drawing.ts) takes lit orange for its yellow
        return polylineThrough(i, pts, i === target ? [255, 0, 255] : [0, 160, 255]);
    });
    const per = Math.ceil(COUNT / FOLDERS);
    const group = (name: string, leaves: string[], subNodes: any[], expanded: boolean) => ({
        defaultColor: { color: "[255, 255, 255, 255]" },
        expanded,
        key: randomUUID(),
        leaves,
        name,
        subNodes,
        version: 1,
        visible: true,
    });
    const keys = flat.map((f) => f.Annotations.key);
    const folders = Array.from({ length: FOLDERS }, (_, g) =>
        group(`folder ${g} (N${g * per}-N${Math.min(COUNT, (g + 1) * per) - 1})`, keys.slice(g * per, (g + 1) * per), [], false)
    );
    const template = JSON.parse(
        fs.readFileSync(path.join(__dirname, "..", "fixtures", "map-projection-annotations.pro3d.ann"), "utf-8").replace(/^\uFEFF/, "")
    );
    const ann = {
        annotations: { flat, groupsLookup: [], rootGroup: group("root", [], folders, true), version: 0 },
        colorByCategory: template.colorByCategory,
        dnsColorLegend: template.dnsColorLegend,
        version: template.version,
    };
    fs.writeFileSync(scene + ".ann", JSON.stringify(ann));
    return { scene, target };
}

/** the magenta pixel nearest the centroid of all magenta pixels in `shot` */
function magentaPixel(shot: Buffer): { x: number; y: number; count: number } {
    const png = PNG.sync.read(shot);
    const px: [number, number][] = [];
    for (let y = 0; y < png.height; y++)
        for (let x = 0; x < png.width; x++) {
            const i = 4 * (y * png.width + x);
            if (png.data[i] > 180 && png.data[i + 2] > 180 && png.data[i + 1] < 90) px.push([x, y]);
        }
    if (px.length === 0) return { x: -1, y: -1, count: 0 };
    const cx = px.reduce((s, p) => s + p[0], 0) / px.length;
    const cy = px.reduce((s, p) => s + p[1], 0) / px.length;
    const d = (p: [number, number]) => Math.hypot(p[0] - cx, p[1] - cy);
    const [x, y] = px.reduce((best, p) => (d(p) < d(best) ? p : best));
    return { x, y, count: px.length };
}

test("annotation list: a pick in 3D opens the folder and scrolls to the annotation", async ({ browser }) => {
    test.skip(!fs.existsSync(SHAPE_MODEL), `Dimorphos shape model not found: ${SHAPE_MODEL} (set PRO3D_DIMORPHOS_OBJ)`);
    const { scene, target } = sceneOnSurface();
    const { app, context, render } = await openDrawingViewer(browser, scene, "surface-loaded.png", save);
    try {
        const panel = await context.newPage();
        await panel.setViewportSize({ width: 500, height: 900 });
        await panel.goto(app.url + "?page=annotations");
        // the folders are closed: no annotation rows
        await expect.poll(() => panel.getByText("folder 4").count(), { timeout: 60_000 }).toBeGreaterThan(0);
        expect(await rowNames(panel), "rows while all folders are closed").toEqual([]);

        await clickTool(render, "Select annotation");
        const shot = await settled(render, "surface-armed.png", save);
        const m = magentaPixel(shot);
        console.log(`[annotation-list] target N${target}: ${m.count} magenta pixels, clicking (${m.x}, ${m.y})`);
        expect(m.count, "the target annotation is drawn").toBeGreaterThan(5);

        await pick(render, m.x, m.y);
        await panel.bringToFront();
        await expect
            .poll(() => inView(panel, `N${target}`), { timeout: 60_000, message: "the pick opens its folder and shows its row" })
            .toBe(true);
        const per = Math.ceil(COUNT / FOLDERS);
        const folder = Math.floor(target / per);
        const outside = (await rowNames(panel)).filter((n) => Math.floor(Number(n.slice(1)) / per) !== folder);
        expect(outside, "only the picked annotation's folder is open").toEqual([]);
        save("picked-list.png", await panel.screenshot());
        await render.bringToFront();
        save("picked-3d.png", await render.screenshot());
    } finally {
        await context.close();
        await app.stop();
    }
});
