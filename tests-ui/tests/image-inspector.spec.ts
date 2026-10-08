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

    // --- the panel --------------------------------------------------------------
    const inspector = await context.newPage();
    await inspector.setViewportSize({ width: 900, height: 900 });
    await inspector.goto(app.url + "?page=imageinspector");
    await expect(inspector.locator("text=/hover the image/")).toBeVisible({ timeout: 120_000 });
    const control = inspector.locator("img.rendercontrol");
    await expect(control).toBeVisible({ timeout: 60_000 });

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
    await expect(inspector.locator("text=/-> surface hit/")).toBeVisible({ timeout: 60_000 });
    await render.waitForTimeout(1500);
    const renderHover = await render.screenshot();
    fs.writeFileSync(path.join(artifacts, "inspector-render-hover.png"), renderHover);
    const dHover = diffPng(renderBefore, renderHover);
    console.log(`marker diff: ${(dHover.changedFraction * 100).toFixed(3)}%`);
    expect(dHover.changedFraction, "the hover marker should appear in 3D").toBeGreaterThan(0.0002);

    await inspector.mouse.move(box.x + 0.02 * box.width, box.y + 0.02 * box.height);
    await expect(inspector.locator("text=/-> no surface hit/")).toBeVisible({ timeout: 30_000 });

    // leaving the panel clears the hover
    await inspector.mouse.move(box.x + box.width + 50, box.y + box.height + 50);
    await expect(inspector.locator("text=/hover the image/")).toBeVisible({ timeout: 30_000 });

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
});
