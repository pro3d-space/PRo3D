import { test, expect, Page } from "@playwright/test";
import { fixture, testData } from "../src/pro3d";
import { awaitIdle, clickTool, drawingScene, openDrawingViewer, pick, selectByOption, settled, tool } from "../src/drawing";
import { PNG } from "pngjs";
import { controlSize, mismatch, renderClient, serverShot, shotsFor } from "../src/server-shot";
import * as fs from "fs";
import * as path from "path";

/**
 * The geological map showcase (#843, docs/GeologicalMap.md) on Dimorphos.
 *
 * The contacts and label points come from the showcase generator
 * (`dotnet run --project src/Tests -- --make-geomap-showcase`), which drapes them on this
 * OPC and writes `cases/geologicalMap/dimorphos-geological-map.pro3d.ann` into the test
 * data. Here they are put next to the usual Dimorphos test scene, so the map is drawn over
 * the body exactly as a mapper would see it - and its screenshots are the showcase.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "geological-map");
const annotations = path.join(testData, "cases", "geologicalMap", "dimorphos-geological-map.pro3d.ann");

test.setTimeout(30 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

/** Camera at `distance` from the body centre in direction (lat, lon), looking at the centre,
 *  north up. CameraView is [Sky, Location, Forward, Up, Right]. */
function lookFrom(d: any, latDeg: number, lonDeg: number, distance: number) {
    const lat = (latDeg * Math.PI) / 180;
    const lon = (lonDeg * Math.PI) / 180;
    const dir = [Math.cos(lat) * Math.cos(lon), Math.cos(lat) * Math.sin(lon), Math.sin(lat)];
    const fwd = dir.map((c) => -c);
    const sky = [0, 0, 1];
    const dot = fwd[0] * sky[0] + fwd[1] * sky[1] + fwd[2] * sky[2];
    let up = sky.map((c, i) => c - dot * fwd[i]);
    const ul = Math.hypot(up[0], up[1], up[2]);
    up = up.map((c) => c / ul);
    const right = [
        fwd[1] * up[2] - fwd[2] * up[1],
        fwd[2] * up[0] - fwd[0] * up[2],
        fwd[0] * up[1] - fwd[1] * up[0],
    ];
    const v = (a: number[]) => `[${a.join(", ")}]`;
    d.cameraView.view = [v(sky), v(dir.map((c) => c * distance)), v(fwd), v(up), v(right)];
}

/** The Dimorphos test scene, looking from (lat, lon), with the showcase annotations beside it. */
function mapScene(name: string, lat: number, lon: number): string {
    const scene = drawingScene(artifacts, name, (d) => lookFrom(d, lat, lon, 260));
    fs.copyFileSync(annotations, scene + ".ann");
    return scene;
}

/** Mean colour of a small square around (fx, fy), given as fractions of the frame. */
function meanColor(png: Buffer, fx: number, fy: number, r = 6) {
    const p = PNG.sync.read(png);
    const cx = Math.round(fx * p.width);
    const cy = Math.round(fy * p.height);
    let s = [0, 0, 0];
    let n = 0;
    for (let y = cy - r; y <= cy + r; y++)
        for (let x = cx - r; x <= cx + r; x++) {
            const i = (y * p.width + x) * 4;
            s = [s[0] + p.data[i], s[1] + p.data[i + 1], s[2] + p.data[i + 2]];
            n++;
        }
    return s.map((c) => c / n);
}

/** Fraction of pixels in the central half of the frame that differ between two frames. */
function changedFraction(a: Buffer, b: Buffer, threshold = 24) {
    const pa = PNG.sync.read(a);
    const pb = PNG.sync.read(b);
    let changed = 0;
    let total = 0;
    for (let y = Math.floor(pa.height * 0.25); y < Math.floor(pa.height * 0.75); y++)
        for (let x = Math.floor(pa.width * 0.25); x < Math.floor(pa.width * 0.75); x++) {
            const i = (y * pa.width + x) * 4;
            const d =
                Math.abs(pa.data[i] - pb.data[i]) +
                Math.abs(pa.data[i + 1] - pb.data[i + 1]) +
                Math.abs(pa.data[i + 2] - pb.data[i + 2]);
            if (d > threshold) changed++;
            total++;
        }
    return changed / total;
}

/** The panel is experimental: a notice until opened, and no map is drawn before that. */
async function openMapsPanel(panel: Page, url: string) {
    await panel.goto(url + "?page=geomaps");
    await panel.locator("button", { hasText: "Open anyway" }).click({ timeout: 120_000 });
}

async function panelText(panel: Page) {
    return panel.evaluate(() => document.body.innerText);
}

/** Clicks the checkbox icon in the panel row labelled `label`. */
async function clickRowIcon(panel: Page, label: string) {
    const r = await panel.evaluate((l) => {
        const cell = Array.from(document.querySelectorAll("td")).find((td) => (td.textContent ?? "").trim() === l);
        const icon = cell?.parentElement?.querySelector("td:nth-child(2) i") as HTMLElement | null;
        if (!icon) return "no icon";
        icon.click();
        return "clicked";
    }, label);
    expect(r).toBe("clicked");
}

/** Clicks the icon in the legend row whose code box holds `code`, in column `column`. */
async function clickUnitIcon(panel: Page, code: string, column: number) {
    const r = await panel.evaluate(
        ({ c, col }) => {
            const row = Array.from(document.querySelectorAll("tr")).find((tr) =>
                Array.from(tr.querySelectorAll("input")).some((i) => (i as HTMLInputElement).value === c)
            );
            const icon = row?.querySelector(`td:nth-child(${col}) i`) as HTMLElement | null;
            if (!icon) return "no icon";
            icon.click();
            return "clicked";
        },
        { c: code, col: column }
    );
    expect(r).toBe("clicked");
}

/** Clicks the active-map icon of the (only) map. */
async function activateMap(panel: Page) {
    const r = await panel.evaluate(() => {
        const cell = Array.from(document.querySelectorAll("td")).find((td) => (td.textContent ?? "").trim() === "Map:");
        const icon = cell?.parentElement?.querySelector("td:nth-child(2) i") as HTMLElement | null;
        if (!icon) return "no icon";
        icon.click();
        return "clicked";
    });
    expect(r).toBe("clicked");
}

test("a label point drawn into the map gives its unit the active legend entry", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate) || !fs.existsSync(annotations),
        "set PRO3D_TEST_DATA and run --make-geomap-showcase");

    const scene = mapScene("geomap-assign", 35, 40);
    const { app, context, render } = await openDrawingViewer(browser, scene, "assign-1-before.png", save);
    try {
        const panel = await context.newPage();
        await panel.setViewportSize({ width: 520, height: 900 });
        await openMapsPanel(panel, app.url);
        await panel.waitForLoadState("domcontentloaded");
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("1 without a label");

        // the unlabelled part of the crater, south of the equatorial contact
        const spot = { x: 0.45, y: 0.74 };
        // colour sampled beside the click: the new, selected point is drawn on the spot itself
        const probe = { x: 0.40, y: 0.73 };
        const before = await settled(render, "assign-1-before.png", save);
        const grey = meanColor(before, probe.x, probe.y);

        // active map -> its group takes what is drawn; Cr -> what is drawn is a Cr label
        await activateMap(panel);
        await clickUnitIcon(panel, "Cr", 4);
        await panel.waitForTimeout(500);
        save("assign-panel.png", await panel.screenshot());

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");
        await clickTool(render, "Draw annotation");
        await expect.poll(async () => (await tool(render, "Draw annotation")).active, { timeout: 30_000 }).toBe(true);
        await selectByOption(main, "Point", "Point");
        await main.waitForTimeout(800);

        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        // the first pick on a cold start loads the surface's KdTree and PRo3D drops input while
        // it intersects (drawing.ts, awaitIdle): pick again if no point arrived
        for (let attempt = 0; attempt < 3; attempt++) {
            await pick(render, spot.x * vp.width, spot.y * vp.height);
            await awaitIdle(render, app);
            if ((await panelText(panel)).includes("6 label points")) break;
        }

        await expect.poll(() => panelText(panel), { timeout: 60_000 }).not.toContain("without a label");
        expect(await panelText(panel)).toContain("5 contacts, 6 label points");

        const after = await settled(render, "assign-2-after.png", save);
        const crater = meanColor(after, probe.x, probe.y);
        console.log(`[geomap] unlabelled unit ${grey.map(Math.round)} -> ${crater.map(Math.round)}`);
        // orange: red well above blue, where the grey had them about equal
        expect(crater[0] - crater[2]).toBeGreaterThan(grey[0] - grey[2] + 30);
    } finally {
        await context.close();
        await app.stop();
    }
});

/** The legend row whose code is `code` shows it as the active unit. */
async function isActiveUnit(panel: Page, code: string) {
    return panel.evaluate((c) => {
        const row = Array.from(document.querySelectorAll("tr")).find((tr) =>
            Array.from(tr.querySelectorAll("input")).some((i) => (i as HTMLInputElement).value === c)
        );
        return !!row?.querySelector("td:nth-child(4) i.check");
    }, code);
}

/** A tool gesture with Ctrl held (the classic scheme), optionally Alt too, dragging from a to b. */
async function gesture(render: Page, a: { x: number; y: number }, b = a, alt = false) {
    await render.bringToFront();
    await render.mouse.move(a.x, a.y);
    await render.waitForTimeout(400);
    await render.keyboard.down("Control");
    if (alt) await render.keyboard.down("Alt");
    await render.waitForTimeout(200);
    await render.mouse.down();
    for (let i = 1; i <= 10; i++) {
        await render.mouse.move(a.x + ((b.x - a.x) * i) / 10, a.y + ((b.y - a.y) * i) / 10);
        await render.waitForTimeout(60);
    }
    await render.mouse.up();
    await render.waitForTimeout(200);
    if (alt) await render.keyboard.up("Alt");
    await render.keyboard.up("Control");
}

test("the Assign unit tool: click, Alt+click and drag across units", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate) || !fs.existsSync(annotations),
        "set PRO3D_TEST_DATA and run --make-geomap-showcase");

    const scene = mapScene("geomap-tool", 35, 40);
    const { app, context, render } = await openDrawingViewer(browser, scene, "tool-1-before.png", save);
    try {
        const panel = await context.newPage();
        await panel.setViewportSize({ width: 520, height: 900 });
        await openMapsPanel(panel, app.url);
        await panel.waitForLoadState("domcontentloaded");
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("1 without a label");
        await activateMap(panel);
        await clickUnitIcon(panel, "Cr", 4);

        const title = "Assign unit - click or drag across units of the active geological map";
        await clickTool(render, title);
        await expect.poll(async () => (await tool(render, title)).active, { timeout: 30_000 }).toBe(true);

        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const at = (fx: number, fy: number) => ({ x: fx * vp.width, y: fy * vp.height });
        const unlabelled = { x: 0.45, y: 0.74 };
        const probe = { x: 0.40, y: 0.73 };
        const crater = { x: 0.47, y: 0.42 };
        const plains = { x: 0.27, y: 0.42 };

        const before = await settled(render, "tool-1-before.png", save);

        // click: the unlabelled unit gets Cr - a label point is placed where it was clicked
        await gesture(render, at(unlabelled.x, unlabelled.y));
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).not.toContain("without a label");
        expect(await panelText(panel)).toContain("6 label points");
        const assigned = await settled(render, "tool-2-assigned.png", save);
        const g = meanColor(before, probe.x, probe.y);
        const o = meanColor(assigned, probe.x, probe.y);
        console.log(`[geomap] click: ${g.map(Math.round)} -> ${o.map(Math.round)}`);
        expect(o[0] - o[2]).toBeGreaterThan(g[0] - g[2] + 30);

        // Alt+click: the northern plains' unit becomes the active one
        await gesture(render, at(plains.x, plains.y), at(plains.x, plains.y), true);
        await expect.poll(() => isActiveUnit(panel, "Np"), { timeout: 30_000 }).toBe(true);

        // drag from the plains into the crater: the crater takes Np
        const c0 = meanColor(assigned, crater.x, crater.y);
        await gesture(render, at(plains.x, plains.y), at(crater.x, crater.y));
        await render.waitForTimeout(1500);
        const painted = await settled(render, "tool-3-painted.png", save);
        const c1 = meanColor(painted, crater.x, crater.y);
        console.log(`[geomap] drag: crater ${c0.map(Math.round)} -> ${c1.map(Math.round)}`);
        // orange (red over blue) -> teal (blue/green over red)
        expect(c1[2] - c1[0]).toBeGreaterThan(c0[2] - c0[0] + 30);
    } finally {
        await context.close();
        await app.stop();
    }
});

/** Count of strongly green pixels (the snap / attached marker) within `r` pixels of (x, y). */
function greenNear(png: Buffer, x: number, y: number, r: number) {
    const p = PNG.sync.read(png);
    let n = 0;
    for (let yy = Math.max(0, y - r); yy < Math.min(p.height, y + r); yy++)
        for (let xx = Math.max(0, x - r); xx < Math.min(p.width, x + r); xx++) {
            const i = (yy * p.width + xx) * 4;
            if (p.data[i + 1] > 170 && p.data[i] < 90 && p.data[i + 2] < 120) n++;
        }
    return n;
}

/** First near-black pixel (a contact line) walking from (fx, fy) by (dx, dy) pixels, as a pixel. */
function findLine(png: Buffer, fx: number, fy: number, dx: number, dy: number) {
    const p = PNG.sync.read(png);
    let x = Math.round(fx * p.width);
    let y = Math.round(fy * p.height);
    for (let n = 0; n < 2000 && x > 0 && y > 0 && x < p.width && y < p.height; n++, x += dx, y += dy) {
        const i = (y * p.width + x) * 4;
        if (p.data[i] + p.data[i + 1] + p.data[i + 2] < 120) return { x, y };
    }
    throw new Error(`no contact line from ${fx},${fy}`);
}

test("a contact drawn to end near other contacts snaps onto them and cuts a unit", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate) || !fs.existsSync(annotations),
        "set PRO3D_TEST_DATA and run --make-geomap-showcase");

    const scene = mapScene("geomap-snap", 35, 40);
    const { app, context, render } = await openDrawingViewer(browser, scene, "snap-1-before.png", save);
    try {
        const panel = await context.newPage();
        await panel.setViewportSize({ width: 520, height: 900 });
        await openMapsPanel(panel, app.url);
        await panel.waitForLoadState("domcontentloaded");
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("6 units");
        // the active map's group takes what is drawn next
        await activateMap(panel);

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");
        await clickTool(render, "Draw annotation");
        await expect.poll(async () => (await tool(render, "Draw annotation")).active, { timeout: 30_000 }).toBe(true);
        await selectByOption(main, "Polyline", "Polyline");
        await main.waitForTimeout(800);

        // from the plains, ending 4 px short of the crater rim and of the equatorial contact
        const before = await settled(render, "snap-1-before.png", save);
        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const sx = vp.width / PNG.sync.read(before).width;
        const rim = findLine(before, 0.24, 0.48, 1, 0);       // walking right onto the crater rim
        const equator = findLine(before, 0.25, 0.55, 0, 1);   // walking down onto the wavy contact
        const start = { x: (rim.x - 4) * sx, y: rim.y * sx };
        const end = { x: equator.x * sx, y: (equator.y - 4) * sx };
        const mid = { x: (start.x + end.x) / 2 - 20, y: (start.y + end.y) / 2 };

        // before clicking: hovering near the rim shows where the click would snap to (green).
        // The cursor's surface point is picked while the tool is armed: Ctrl held (classic scheme)
        await render.bringToFront();
        await render.mouse.move(start.x, start.y);
        await render.waitForTimeout(400);
        await render.keyboard.down("Control");
        // the first preview pick loads the surface's KdTree (seconds); keep the cursor alive
        let hover = await render.screenshot();
        for (let i = 0; i < 20; i++) {
            await render.mouse.move(start.x + (i % 2), start.y);
            await render.waitForTimeout(500);
            hover = await render.screenshot();
            if (greenNear(hover, rim.x, rim.y, 14) > 8) break;
        }
        await render.keyboard.up("Control");
        save("snap-0-hover.png", hover);
        expect(greenNear(hover, rim.x, rim.y, 14), "a green snap marker at the rim under the cursor").toBeGreaterThan(8);
        for (const p of [start, mid, end]) {
            await pick(render, p.x, p.y);
            await awaitIdle(render, app);
        }

        // split preview, before the contact is finished: the plains it runs through light up
        const plains = { x: 0.62, y: 0.20 };
        const drawing = await settled(render, "snap-1b-drawing.png", save);
        const p0 = meanColor(before, plains.x, plains.y);
        const p1 = meanColor(drawing, plains.x, plains.y);
        console.log(`[geomap] preview: plains ${p0.map(Math.round)} -> ${p1.map(Math.round)}`);
        expect(p1[0] + p1[1] + p1[2]).toBeGreaterThan(p0[0] + p0[1] + p0[2] + 40);

        await render.keyboard.press("Enter");
        await awaitIdle(render, app);

        // snapped at both ends: one more unit (the plains split in two, one half unlabelled),
        // and no open end added
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("7 units");
        const text = await panelText(panel);
        expect(text).toContain("6 contacts");
        expect(text).toContain("2 without a label");
        expect(text).toContain("4 open contact ends");
        save("snap-panel.png", await panel.screenshot());
        const after = await settled(render, "snap-2-after.png", save);
        expect(fs.readFileSync(app.logFile, "utf-8")).toContain("snapped onto a contact");

        // N: the camera goes to the first problem
        await render.mouse.move(vp.width / 2, vp.height / 2);
        await render.keyboard.press("n");
        await render.waitForTimeout(2500);
        const flown = await settled(render, "snap-3-next-problem.png", save);
        expect(changedFraction(after, flown)).toBeGreaterThan(0.2);
    } finally {
        await context.close();
        await app.stop();
    }
});

for (const view of [
    { name: "north", lat: 35, lon: 40 },
    { name: "south", lat: -45, lon: 200 },
]) {
    test(`the map's units cover Dimorphos (${view.name})`, async ({ browser }) => {
        test.skip(!fs.existsSync(fixture.sceneTemplate) || !fs.existsSync(annotations),
            "set PRO3D_TEST_DATA and run --make-geomap-showcase");

        const scene = mapScene(`geomap-${view.name}`, view.lat, view.lon);
        const { app, context, render } = await openDrawingViewer(browser, scene, `${view.name}-1-map.png`, save);
        try {
            const panel = await context.newPage();
            await panel.setViewportSize({ width: 520, height: 900 });
            await openMapsPanel(panel, app.url);
            await panel.waitForLoadState("domcontentloaded");

            // the panel reports what the topology made of the group
            await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("6 units");
            const text = await panelText(panel);
            expect(text).toContain("5 contacts, 5 label points");
            expect(text).toContain("1 without a label");
            expect(text).toContain("4 open contact ends");
            expect(text).not.toContain("conflicting");
            expect(text).not.toContain("topology check failed");
            save(`${view.name}-panel.png`, await panel.screenshot());

            const withMap = await settled(render, `${view.name}-1-map.png`, save);

            // server screenshots of other sizes and aspect ratios show the units where the live
            // view has them: the maps are drawn per render client, against its own surface depth
            const client = await renderClient(render);
            expect(client, "the render control's element id").not.toBe("");
            const control = await controlSize(render);
            const live = PNG.sync.read(withMap);
            const box = { x0: Math.round(live.width * 0.3), y0: Math.round(live.height * 0.3), x1: Math.round(live.width * 0.7), y1: Math.round(live.height * 0.7) };
            for (const s of shotsFor(control.width, control.height)) {
                const png = await serverShot(context, app, client, s);
                save(`${view.name}-server-${s.name}.png`, png);
                const shot = PNG.sync.read(png);
                expect([shot.width, shot.height], `screenshot ${s.name} has the requested size`).toEqual([s.w, s.h]);
                const off = mismatch(live, shot, box.x0, box.y0, box.x1, box.y1);
                console.log(`[geomap] ${view.name}: server ${s.name} ${s.w}x${s.h} differs from the live view in ${(off * 100).toFixed(1)} % of the centre`);
                expect(off, `${s.name}: the units as in the live view`).toBeLessThan(0.1);
            }

            // hiding the map takes its colours off the terrain
            await clickRowIcon(panel, "Visible:");
            await render.waitForTimeout(1500);
            const hidden = await settled(render, `${view.name}-2-hidden.png`, save);
            const changed = changedFraction(withMap, hidden);
            console.log(`[geomap] ${view.name}: hiding the map changed ${(changed * 100).toFixed(1)} % of the centre`);
            expect(changed).toBeGreaterThan(0.2);
        } finally {
            await context.close();
            await app.stop();
        }
    });
}
