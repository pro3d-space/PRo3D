import { test, expect, Page } from "@playwright/test";
import { launchPro3d, Pro3d, config, imageRow, surfaceShadersReady, firstImage } from "../src/pro3d";
import * as fs from "fs";
import * as path from "path";

/**
 * Screenshots for docs/ShadowMeasurement.md -- not a test, run only with PRO3D_DOC_SHOTS=1.
 *
 * Needs the Didymos scene and a frame rendered from the same OPC (the crater at pixel column
 * ~400, rim row ~581, shadow end ~610):
 *
 *   PRO3D_SCENE      = C:\pro3ddata\HERA\workshop3\didyShadow.pro3d
 *   PRO3D_IMAGE_DIR  = C:\pro3ddata\HERA\workshop3\shadowDocShots
 *
 * The frame: pro3d-tool simulate-image --opc ...\Didymos_SK_OPC__texture\Didymos_ASPECT_texture
 *   --mbi ...\shadowProjectionTest\AFC1_COP_20270205_170000.mbi.json --body DIDYMOS
 *   --frame DIDYMOS_FIXED --kernel ...\spice2\kernels\mk\hera_plan.tm --micro-amplitude 0
 *   --gain 3.690 --shadow-bias 0.006 --write-mbi
 */

const docImages = path.join(__dirname, "..", "..", "docs", "images");
const artifacts = path.join(__dirname, "..", "artifacts", "shadow-docshots");
const enabled = process.env.PRO3D_DOC_SHOTS === "1";

/** the crater: rim and shadow end in image pixels (0-based, origin top-left) */
const RIM = { x: 400, y: 580 };
const TIP = { x: 400, y: 611 };
const ZOOM_STEPS = 3; // 2^3 = 8x

let app: Pro3d;

test.setTimeout(20 * 60_000);

test.beforeAll(async () => {
    if (!enabled) return;
    fs.mkdirSync(artifacts, { recursive: true });
    fs.mkdirSync(docImages, { recursive: true });
    app = await launchPro3d();
});

test.afterAll(async () => {
    await app?.stop();
});

async function save(name: string, png: Buffer) {
    fs.writeFileSync(path.join(artifacts, name), png);
    fs.writeFileSync(path.join(docImages, `shadowMeasurement-${name}`), png);
}

test("shadow measurement: documentation screenshots", async ({ browser }) => {
    test.skip(!enabled, "set PRO3D_DOC_SHOTS=1 (and the Didymos scene/frame, see the header)");
    const context = await browser.newContext();

    const render = await context.newPage();
    await render.setViewportSize({ width: 1200, height: 800 });
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await surfaceShadersReady(render);

    // import the frame; LoadImagesDir selects the first
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

    // fly to the image: the 3D markers are depth-tested, seen best from where the camera looked
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
    await render.waitForTimeout(9000);

    // the panel, past its experimental notice
    const inspector = await context.newPage();
    await inspector.setViewportSize({ width: 900, height: 934 });
    await inspector.goto(app.url + "?page=imageinspector");
    const open = inspector.locator("button", { hasText: "Open anyway" });
    await expect(open).toBeVisible({ timeout: 120_000 });

    await open.click();
    await expect(inspector.locator("text=/^wheel zooms/")).toBeVisible({ timeout: 120_000 });
    const control = inspector.locator("img.rendercontrol");
    await expect(control).toBeVisible({ timeout: 60_000 });
    await inspector.bringToFront();
    await inspector.waitForTimeout(3000);

    const box = (await control.boundingBox())!;
    const W = 1020, H = 1020;
    // unzoomed: image pixel -> screen
    const at = (p: { x: number; y: number }) => ({ x: box.x + (p.x + 0.5) / W * box.width, y: box.y + (p.y + 0.5) / H * box.height });

    // zoom about the middle of the crater: that point stays put, everything scales around it
    const mid = { x: RIM.x, y: (RIM.y + TIP.y) / 2 };
    const fixed = at(mid);
    await inspector.mouse.move(fixed.x, fixed.y);
    for (let i = 0; i < ZOOM_STEPS; i++) {
        await inspector.mouse.wheel(0, -200);
        await inspector.waitForTimeout(400);
    }
    const scale = Math.pow(2, ZOOM_STEPS);
    const zoomedAt = (p: { x: number; y: number }) => ({
        x: fixed.x + (p.x - mid.x) / W * box.width * scale,
        y: fixed.y + (p.y - mid.y) / H * box.height * scale,
    });

    // measure
    await inspector.locator("button", { hasText: "Measure shadow" }).click();
    await expect(inspector.locator("text=/click the crater rim/")).toBeVisible({ timeout: 30_000 });
    const rim = zoomedAt(RIM);
    await inspector.mouse.move(rim.x, rim.y);
    await inspector.mouse.click(rim.x, rim.y);
    await expect(inspector.locator("text=/click the end of the shadow/")).toBeVisible({ timeout: 30_000 });
    await inspector.waitForTimeout(800);
    await save("first-click.png", await inspector.screenshot());

    const tip = zoomedAt(TIP);
    await inspector.mouse.move(tip.x, tip.y);
    await inspector.mouse.click(tip.x, tip.y);
    const result = inspector.locator("text=/start over$/");
    await expect(result).toBeVisible({ timeout: 30_000 });
    console.log(`measurement: ${await result.innerText()}`);
    // move off the panel so no hover crosshair sits in the picture
    await inspector.mouse.move(5, box.y + box.height + 20);
    await inspector.waitForTimeout(1500);
    await save("crater.png", await inspector.screenshot());
});
