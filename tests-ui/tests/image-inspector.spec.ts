import { test, expect, Page } from "@playwright/test";
import { launchPro3d, Pro3d, config, imageRow, surfaceShadersReady, firstImage } from "../src/pro3d";
import { diffPng, litFraction, streamLive, registration } from "../src/image";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

/**
 * Image Inspector (docs/ImageInspector.md, docs/dev/shadowEstimation.md milestone 1):
 *
 * 1. the panel shows the selected image the way the projection samples it -- the panel's
 *    render reproduces the source frame, and the identity beats every mirror/rotation;
 * 2. hovering a bright (on-body) pixel reports a surface hit and draws the 3D marker; a dark
 *    corner (space) reports no hit; leaving the panel removes the marker;
 * 3. Ctrl-hovering the 3D view puts the cyan crosshair into the panel.
 */

let app: Pro3d;
const artifacts = path.join(__dirname, "..", "artifacts");

test.setTimeout(15 * 60_000);

test.beforeAll(async () => {
    fs.mkdirSync(artifacts, { recursive: true });
    app = await launchPro3d();
});

test.afterAll(async () => {
    await app?.stop();
});

async function stable(page: Page, shoot: () => Promise<Buffer>, name: string): Promise<Buffer> {
    const started = Date.now();
    let shot = await shoot();
    while ((!streamLive(shot) || litFraction(shot) < 0.003) && Date.now() - started < 600_000) {
        await page.waitForTimeout(3000);
        shot = await shoot();
    }
    let prev = shot;
    for (let i = 0; i < 60; i++) {
        await page.waitForTimeout(1000);
        const cur = await shoot();
        if (diffPng(prev, cur).changedFraction < 0.001) { prev = cur; break; }
        prev = cur;
    }
    fs.writeFileSync(path.join(artifacts, name), prev);
    return prev;
}

/** centroid of the source's bright pixels, as a fraction of the image (x right, y down) */
function brightCentroid(png: Buffer): [number, number] {
    const p = PNG.sync.read(png);
    let sx = 0, sy = 0, n = 0;
    for (let y = 0; y < p.height; y++)
        for (let x = 0; x < p.width; x++) {
            const o = (y * p.width + x) * 4;
            if ((p.data[o] + p.data[o + 1] + p.data[o + 2]) / 3 > 60) { sx += x; sy += y; n++; }
        }
    return [(sx / n + 0.5) / p.width, (sy / n + 0.5) / p.height];
}

test("image inspector: orientation and 2D <-> 3D hover", async ({ browser }) => {
    const context = await browser.newContext();

    const render = await context.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await surfaceShadersReady(render);

    // --- import the frames; LoadImagesDir selects the first --------------------
    const gis = await context.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    const imageDirUnix = config.imageDir.replace(/\\/g, "/");
    await gis.evaluate((dir) => {
        const w = window as any;
        w.aardvark = w.aardvark ?? {};
        w.aardvark.dialog = { showOpenDialog: (_: unknown) => Promise.resolve({ canceled: false, filePaths: [dir] }) };
    }, imageDirUnix);
    await gis.locator("text=Import Directory").first().click();
    await expect(gis.locator(imageRow).first()).toBeVisible({ timeout: 120_000 });
    const sourceName = firstImage(config.imageDir);
    const source = fs.readFileSync(path.join(config.imageDir, sourceName));

    // look from the image's own viewpoint: the 3D markers are depth-tested real geometry, so
    // they are only visible where the instrument looked
    const flown = await gis.evaluate((n) => {
        const all = Array.from(document.querySelectorAll("*")).filter((e) => (e.textContent ?? "").trim() === n);
        const deepest = all.filter((e) => !Array.from(e.children).some((c) => all.includes(c)));
        let el: Element | null = deepest[0] ?? null;
        while (el) {
            const icon = el.nextElementSibling?.querySelector("i.location.icon");
            if (icon) { (icon as HTMLElement).click(); return "clicked"; }
            el = el.parentElement;
        }
        return "no fly-to icon in the row";
    }, sourceName);
    expect(flown).toBe("clicked");
    await render.waitForTimeout(9000); // the animation runs 3.5 s

    // --- the panel --------------------------------------------------------------
    const inspector = await context.newPage();
    // a script error in the panel's page stops it sending events: surface it
    inspector.on("pageerror", (e) => console.log(`[inspector pageerror] ${e.message}
${e.stack ?? ""}`));
    inspector.on("console", (m) => { if (m.type() === "error" || m.type() === "warning") console.log(`[inspector console.${m.type()}] ${m.text()}`); });
    await inspector.setViewportSize({ width: 900, height: 900 });
    await inspector.goto(app.url + "?page=imageinspector");
    await expect(inspector.locator("text=/^wheel zooms/")).toBeVisible({ timeout: 120_000 });
    const control = inspector.locator("img.rendercontrol");
    await expect(control).toBeVisible({ timeout: 60_000 });

    // The panel must be the foreground page: Chromium delivers mouse moves aligned to
    // animation frames, and a background page with nothing to redraw (e.g. right after a
    // reset) may not get one -- the move then never reaches the page, with no error anywhere.
    await inspector.bringToFront();

    // 1. orientation: the panel reproduces the source frame
    const panelShot = await stable(inspector, () => control.screenshot(), "inspector-panel.png");
    const reg = registration(source, panelShot);
    console.log(`panel vs ${sourceName}: zeroShift ${reg.zeroShift.toFixed(3)}, best ${reg.best.toFixed(3)} at ${reg.bestShift}, symmetries ${reg.symmetries.map((s) => s.toFixed(2)).join(" ")}`);
    expect(reg.zeroShift, "panel must reproduce the source image").toBeGreaterThan(0.9);
    const bestSym = reg.symmetries.indexOf(Math.max(...reg.symmetries));
    expect(bestSym, "the identity must beat every mirror/rotation").toBe(0);

    // 2. hover -> 3D marker
    const renderBefore = await stable(render, () => render.screenshot(), "inspector-render-before.png");
    const box = (await control.boundingBox())!;
    const [fx, fy] = brightCentroid(source);
    await inspector.mouse.move(box.x + fx * box.width, box.y + fy * box.height);
    await expect(inspector.locator("text=/on the surface$/")).toBeVisible({ timeout: 60_000 });
    const pixelAt = async () => (await inspector.locator("text=/on the surface$/").first().innerText()).match(/pixel ([\d.]+), ([\d.]+)/)!.slice(1).map(Number);
    const before = await pixelAt();
    await render.waitForTimeout(1500);
    const renderHover = await render.screenshot();
    fs.writeFileSync(path.join(artifacts, "inspector-render-hover.png"), renderHover);
    const dHover = diffPng(renderBefore, renderHover);
    console.log(`marker diff: ${(dHover.changedFraction * 100).toFixed(3)}%`);
    expect(dHover.changedFraction, "the hover marker should appear in 3D").toBeGreaterThan(0.0002);

    // zoom about the pointer: the pixel under it stays put, the view magnifies
    for (let i = 0; i < 2; i++) { await inspector.mouse.wheel(0, -200); await inspector.waitForTimeout(300); }
    await expect(inspector.locator("text=/zoom 4.0x/")).toBeVisible({ timeout: 30_000 });
    await inspector.mouse.move(box.x + fx * box.width + 1, box.y + fy * box.height);
    await inspector.waitForTimeout(200); // past the client's 33 ms move throttle
    await inspector.mouse.move(box.x + fx * box.width, box.y + fy * box.height);
    await expect(inspector.locator("text=/on the surface$/")).toBeVisible({ timeout: 30_000 });
    const after = await pixelAt();
    console.log(`pixel before zoom ${before}, after ${after}`);
    expect(Math.hypot(after[0] - before[0], after[1] - before[1]), "zoom must keep the pixel under the pointer").toBeLessThan(1.0);
    // drag pans: dragging left by a tenth of the panel brings image content from the right
    // under the pointer -- at 4x that is 1020 / 4 / 10 ~ 25 px
    const cx = box.x + fx * box.width, cy = box.y + fy * box.height;
    await inspector.mouse.down();
    await inspector.mouse.move(cx - 0.1 * box.width, cy, { steps: 8 });
    await inspector.mouse.up();
    await inspector.mouse.move(cx + 1, cy);
    await inspector.waitForTimeout(200);
    await inspector.mouse.move(cx, cy);
    await inspector.waitForTimeout(500);
    const panned = await pixelAt();
    console.log(`pixel after drag ${panned}`);
    expect(panned[0] - after[0], "drag left must move the image left under the pointer").toBeGreaterThan(15);
    expect(Math.abs(panned[1] - after[1]), "horizontal drag must not move vertically").toBeLessThan(2);
    const zoomed = await control.screenshot();
    fs.writeFileSync(path.join(artifacts, "inspector-zoomed.png"), zoomed);
    await inspector.mouse.dblclick(box.x + fx * box.width, box.y + fy * box.height);
    await expect(inspector.locator("text=/zoom 4.0x/")).toHaveCount(0, { timeout: 30_000 });

    await inspector.mouse.move(box.x + 0.02 * box.width, box.y + 0.02 * box.height);
    await expect(inspector.locator("text=/off the surface$/")).toBeVisible({ timeout: 30_000 });

    // leaving the panel clears the hover
    await inspector.mouse.move(box.x + box.width + 50, box.y + box.height + 50);
    await expect(inspector.locator("text=/^wheel zooms/")).toBeVisible({ timeout: 30_000 });

    // 3. Ctrl-hover in 3D -> cyan crosshair in the panel
    const vp = render.viewportSize()!;
    // the render control focuses itself on mouseenter and reads Ctrl from key events on the
    // focused element: move in first, then press (see pick in src/drawing.ts)
    await render.bringToFront();
    await render.mouse.move(vp.width * 0.5, vp.height * 0.5);
    await render.waitForTimeout(400);
    await render.keyboard.down("Control");
    await render.waitForTimeout(200);
    let found = false;
    for (const [px, py] of [[0.51, 0.5], [0.45, 0.5], [0.55, 0.5], [0.5, 0.45], [0.5, 0.55]]) {
        await render.mouse.move(vp.width * px, vp.height * py, { steps: 5 });
        await render.waitForTimeout(1500);
        const n = await inspector.locator('div[style*="#00e5ff"]').count();
        if (n > 0) { found = true; break; }
    }
    await render.keyboard.up("Control");
    await inspector.screenshot({ path: path.join(artifacts, "inspector-crosshair.png") });
    expect(found, "a 3D surface hover should show the cyan crosshair in the panel").toBe(true);

    // 4. shadow measurement: rim on the body, tip along the drawn sun line
    await inspector.bringToFront();
    await inspector.locator("button", { hasText: "Measure shadow" }).click();
    await expect(inspector.locator("text=/click the crater rim/")).toBeVisible({ timeout: 30_000 });
    await inspector.mouse.click(box.x + fx * box.width, box.y + fy * box.height);
    await expect(inspector.locator("text=/click the end of the shadow/")).toBeVisible({ timeout: 30_000 });
    // the dashed sun line, in percent of the panel
    const sunLine = inspector.locator("svg line");
    await expect(sunLine).toHaveCount(1, { timeout: 30_000 });
    const [x1, y1, x2, y2] = await Promise.all(["x1", "y1", "x2", "y2"].map(async (a) => Number(await sunLine.getAttribute(a))));
    const len = Math.hypot(x2 - x1, y2 - y1);
    // 3 % of the panel along the line, plus a little off it: the click must snap
    const tx = x1 + (x2 - x1) / len * 3 + 0.3, ty = y1 + (y2 - y1) / len * 3 + 0.3;
    await inspector.mouse.click(box.x + tx / 100 * box.width, box.y + ty / 100 * box.height);
    const result = inspector.locator("text=/start over$/");
    await expect(result).toBeVisible({ timeout: 30_000 });
    const resultText = await result.innerText();
    console.log(`measurement: ${resultText}`);
    expect(resultText, "measurement result").toMatch(/^depth -?[\d.]+ m/);
    await inspector.screenshot({ path: path.join(artifacts, "inspector-measure.png") });
    await render.waitForTimeout(1500);
    await render.screenshot({ path: path.join(artifacts, "inspector-measure-3d.png") });

    // the details are in the result box
    await expect(inspector.locator("text=/^Depth [0-9.]+ m$/")).toBeVisible({ timeout: 10_000 });
    await expect(inspector.locator("text=/^phase$/")).toBeVisible({ timeout: 10_000 });
    await inspector.locator("button", { hasText: "Create scale bar" }).click();

    // Up: plane -- the result waits for two more plane points, then comes back
    await inspector.bringToFront();
    await inspector.locator("button", { hasText: "Up: local" }).click();
    await inspector.locator("button", { hasText: "Up: radial" }).click();
    await expect(inspector.locator("text=/click 2 more points/")).toBeVisible({ timeout: 30_000 });
    await inspector.mouse.click(box.x + (fx - 0.04) * box.width, box.y + fy * box.height);
    await expect(inspector.locator("text=/click 1 more point /")).toBeVisible({ timeout: 30_000 });
    await inspector.mouse.click(box.x + (fx + 0.04) * box.width, box.y + (fy - 0.02) * box.height);
    await expect(inspector.locator("text=/^Depth [0-9.]+ m$/")).toBeVisible({ timeout: 30_000 });
    await expect(inspector.locator("text=/plane, 3 pts/")).toBeVisible({ timeout: 10_000 });
    await inspector.locator("button", { hasText: "Up: plane" }).click();   // back to local
    const bars = await context.newPage();
    await bars.goto(app.url + "?page=scalebars");
    // one bar at each end, along the camera's sky
    // (list entries only: the selected bar's name also appears in its properties)
    await expect(bars.locator(".item .header", { hasText: "Sky_cam" })).toHaveCount(2, { timeout: 30_000 });

    // 5. boulder height: anchor on the ground, the top triangulated towards the sun
    await inspector.bringToFront();
    await inspector.locator("button", { hasText: "Crater depth" }).click();
    await expect(inspector.locator("text=/boulder's shadow, on the ground/")).toBeVisible({ timeout: 30_000 });
    await inspector.mouse.click(box.x + fx * box.width, box.y + fy * box.height);
    await expect(inspector.locator("text=/boulder's top edge/")).toBeVisible({ timeout: 30_000 });
    const line2 = inspector.locator("svg line");
    await expect(line2).toHaveCount(1, { timeout: 30_000 });
    const [bx1, by1, bx2, by2] = await Promise.all(["x1", "y1", "x2", "y2"].map(async (a) => Number(await line2.getAttribute(a))));
    const blen = Math.hypot(bx2 - bx1, by2 - by1);
    await inspector.mouse.click(box.x + (bx1 + (bx2 - bx1) / blen * 3) / 100 * box.width, box.y + (by1 + (by2 - by1) / blen * 3) / 100 * box.height);
    const height = inspector.locator("text=/start over$/");
    await expect(height).toBeVisible({ timeout: 30_000 });
    const heightText = await height.innerText();
    console.log(`boulder: ${heightText}`);
    expect(heightText, "boulder result").toMatch(/^height [\d.]+ m/);
    await render.waitForTimeout(1500);
    await render.screenshot({ path: path.join(artifacts, "inspector-boulder-3d.png") });
});
