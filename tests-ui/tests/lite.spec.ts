// PRo3D Lite (docs/PRo3DLite.md): the minimal viewer composed from PRo3D.Core and
// PRo3D.Composition, driven in a browser.
//
// What it proves: an OPC given on the command line renders; Ctrl+click with the draw tool
// puts annotation points on the terrain (picking -> PickRouting -> DrawingApp) and they
// render; the annotations save as a .pro3d.ann; "Save scene as" writes a .pro3d plus its
// annotation sidecar, and a second Lite opens that scene and renders the same surface.
import { test, expect, Page } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { launchLite, liteOpc, LiteApp } from "../src/lite";
import { launchPro3d } from "../src/pro3d";
import { diffPng, litFraction, streamLive } from "../src/image";

const artifacts = path.join(__dirname, "..", "artifacts", "lite");
const W = 1200;
const H = 800;

test.describe.configure({ mode: "serial" });
test.setTimeout(15 * 60_000);

/** screenshot until the stream is live, the surface covers the view and two frames agree */
async function settle(page: Page, name: string, minLit = 0.1): Promise<Buffer> {
    const until = Date.now() + 10 * 60_000;
    let last: Buffer | undefined;
    for (;;) {
        await page.waitForTimeout(1000);
        const shot = await page.locator("img.rendercontrol").screenshot();
        const ready = streamLive(shot) && litFraction(shot, 40) >= minLit;
        if (ready && last && diffPng(last, shot).changedFraction < 0.001) {
            fs.writeFileSync(path.join(artifacts, `${name}.png`), shot);
            return shot;
        }
        last = ready ? shot : undefined;
        if (Date.now() > until) {
            fs.writeFileSync(path.join(artifacts, `${name}-timeout.png`), shot);
            throw new Error(`${name}: the render view did not settle (lit ${litFraction(shot, 40).toFixed(2)})`);
        }
    }
}

/** stubs the Electron dialogs (plain Chromium has none) to answer with `file` */
async function stubDialogs(page: Page, file: string) {
    const f = JSON.stringify(file.replace(/\\/g, "/"));
    await page.evaluate(`(() => {
        window.aardvark = window.aardvark || {};
        window.aardvark.dialog = {
            showSaveDialog: () => Promise.resolve({ canceled: false, filePath: ${f} }),
            showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${f}] })
        };
    })()`);
}

/** clicks the menu item with exactly this label (single-shot DOM click, see README) */
async function clickMenu(page: Page, label: string) {
    const found = await page.evaluate(`(() => {
        const item = Array.from(document.querySelectorAll('.item')).find(e => e.textContent.trim() === ${JSON.stringify(label)});
        if (!item) return false;
        item.click();
        return true;
    })()`);
    expect(found, `menu item "${label}"`).toBe(true);
}

async function openRender(app: { url: string }, page: Page) {
    await page.setViewportSize({ width: W, height: H });
    await page.goto(app.url + "?page=render");
    await page.waitForSelector("img.rendercontrol", { timeout: 120_000 });
}

/** Ctrl+clicks at the given fractions of the render view */
async function ctrlClicks(page: Page, points: Array<[number, number]>) {
    const box = (await page.locator("img.rendercontrol").boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.keyboard.down("Control");
    // let the key reach the app before the first click (a user holds Ctrl first, too)
    await page.waitForTimeout(500);
    for (const [fx, fy] of points) {
        await page.mouse.click(box.x + box.width * fx, box.y + box.height * fy);
        await page.waitForTimeout(400);
    }
    await page.keyboard.up("Control");
}

const annotationCount = (file: string) => {
    const json = JSON.parse(fs.readFileSync(file, "utf8"));
    // { version, annotations: GroupsModel { flat: [...] , ...}, ... }
    const flat = json.annotations?.flat;
    return Array.isArray(flat) ? flat.length : Object.keys(flat ?? {}).length;
};

test.describe("PRo3D Lite", () => {
    let app: LiteApp;
    const scene = path.join(artifacts, "lite-scene.pro3d");
    const annotationsFile = path.join(artifacts, "lite-annotations.pro3d.ann");

    test.beforeAll(async () => {
        expect(liteOpc, "MSL OPC: set PRO3D_TEST_DATA or init the src/Tests/resources submodule").not.toBe("");
        fs.mkdirSync(artifacts, { recursive: true });
        for (const f of [scene, scene + ".ann", annotationsFile]) if (fs.existsSync(f)) fs.unlinkSync(f);
        app = await launchLite(["--opc", liteOpc]);
    });

    test.afterAll(async () => {
        await app?.stop();
    });

    test("an OPC given on the command line renders", async ({ page }) => {
        await openRender(app, page);
        await settle(page, "01-opc");
    });

    test("ctrl+click with the draw tool puts annotations on the terrain", async ({ page, context }) => {
        await openRender(app, page);
        const before = await settle(page, "02-before-draw");

        // the default geometry is a line: two clicks each, along the middle of the (narrow,
        // upright) MSL outcrop strip
        await ctrlClicks(page, [[0.5, 0.25], [0.5, 0.45]]);
        await ctrlClicks(page, [[0.5, 0.6], [0.52, 0.8]]);

        await page.waitForTimeout(1500);
        const after = await settle(page, "03-after-draw");
        const d = diffPng(before, after);
        expect(d.changedFraction, "the annotations render").toBeGreaterThan(0.0005);

        // save them through the menu; the file is the format full PRo3D reads
        const body = await context.newPage();
        await body.setViewportSize({ width: W, height: H });
        await body.goto(app.url);
        await body.waitForSelector(".pro3d-topbar");
        await stubDialogs(body, annotationsFile);
        await clickMenu(body, "Save annotations");
        await expect.poll(() => fs.existsSync(annotationsFile), { timeout: 30_000 }).toBe(true);
        expect(annotationCount(annotationsFile), "two annotations saved").toBe(2);
        await body.close();
    });

    test("save scene as writes the scene and its annotation sidecar, and it reopens", async ({ page, context }) => {
        const body = await context.newPage();
        await body.setViewportSize({ width: W, height: H });
        await body.goto(app.url);
        await body.waitForSelector(".pro3d-topbar");
        await stubDialogs(body, scene);
        await clickMenu(body, "Save scene as");
        await expect.poll(() => fs.existsSync(scene) && fs.existsSync(scene + ".ann"), { timeout: 30_000 }).toBe(true);

        const doc = JSON.parse(fs.readFileSync(scene, "utf8"));
        expect(doc.version).toBe(3);
        for (const key of ["cameraView", "surfaceModel", "config", "referenceSystem"]) expect(doc[key], key).toBeTruthy();
        expect(annotationCount(scene + ".ann"), "sidecar holds the annotations").toBe(2);
        await body.close();

        // a fresh Lite on the saved scene shows the surface (and the annotations) again
        const second = await launchLite(["--scene", scene]);
        try {
            await openRender(second, page);
            await settle(page, "04-reopened");
        } finally {
            await second.stop();
        }
    });

    // the other direction, and the Viewer's own render + pick path (now PRo3D.Composition):
    // full PRo3D opens the scene Lite wrote, shows surface and annotations, and Ctrl+click
    // draws on it
    test("the full viewer opens Lite's scene and draws on it", async ({ page }) => {
        expect(fs.existsSync(scene), "the scene from the previous case").toBe(true);
        const viewer = await launchPro3d(scene);
        try {
            await openRender(viewer, page);
            const before = await settle(page, "05-viewer-opened");
            await ctrlClicks(page, [[0.5, 0.3], [0.5, 0.42]]);
            await page.waitForTimeout(1500);
            const after = await settle(page, "06-viewer-drawn");
            expect(diffPng(before, after).changedFraction, "the viewer draws on Lite's scene").toBeGreaterThan(0.0005);
        } finally {
            await viewer.stop();
        }
    });
});
