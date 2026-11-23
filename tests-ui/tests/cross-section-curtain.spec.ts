import { test, expect, Page, BrowserContext } from "@playwright/test";
import { PNG } from "pngjs";
import { launchPro3d, fixture, surfaceShadersReady } from "../src/pro3d";
import { overlayPlanet } from "../src/viewer";
import { diffPng } from "../src/image";
import { drawingScene, drawSkyLine, settled } from "../src/drawing";
import * as fs from "fs";
import * as path from "path";

/**
 * Cross section + curtain -- docs/CrossSections.md.
 *
 * Draws a sky-projected line across the Dimorphos OPC, turns it into a cross section
 * (Annotation properties -> Cross Section: Create), and sets up the curtain through the
 * Curtain Settings panel the way a user does:
 *
 *   1. Create clips the surface between the line and the camera.
 *   2. Curtain on WITHOUT an image draws it in the base color. It used to draw nothing
 *      until an image was chosen.
 *   3. The folder button opens the image dialog (stubbed: Chromium has no Electron
 *      dialog) and the chosen image lands on the curtain.
 *   4. Cancelling the dialog keeps that image. It used to clear it.
 *   5. Remove drops the image, back to the base color.
 *   6. A path typed into the Image field works too -- the fallback when the native dialog
 *      does not open (reported on macOS).
 *
 * Every step is judged on the rendered frame, not only on the model: the curtain is
 * drawn by a geometry shader from CPU-built geometry, and "the path is set" says
 * nothing about whether it renders.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "cross-section");

test.setTimeout(30 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

/** A flat-colour PNG. Magenta and cyan occur nowhere on the grey body or in the HUD, so
 *  counting them measures the curtain image and nothing else. */
function solidPng(file: string, rgb: [number, number, number]): string {
    const png = new PNG({ width: 256, height: 128 });
    for (let i = 0; i < png.width * png.height; i++) {
        png.data[i * 4] = rgb[0];
        png.data[i * 4 + 1] = rgb[1];
        png.data[i * 4 + 2] = rgb[2];
        png.data[i * 4 + 3] = 255;
    }
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, PNG.sync.write(png));
    return file;
}

/** Fraction of the frame whose colour is close to `rgb`. */
function colourFraction(buf: Buffer, rgb: [number, number, number], tol = 40): number {
    const img = PNG.sync.read(buf);
    let hits = 0;
    const n = img.width * img.height;
    for (let i = 0; i < n; i++) {
        const o = i * 4;
        if (
            Math.abs(img.data[o] - rgb[0]) <= tol &&
            Math.abs(img.data[o + 1] - rgb[1]) <= tol &&
            Math.abs(img.data[o + 2] - rgb[2]) <= tol
        )
            hits++;
    }
    return hits / n;
}

/**
 * Fraction of the frame that changed from `before` to `after` AND is now the curtain's
 * default base colour (C4b.Gray = 128,128,128, unlit). The body is grey too, so a plain
 * colour count would mostly measure the surface; the change is what isolates the curtain.
 */
function newBaseColourFraction(before: Buffer, after: Buffer): number {
    const a = PNG.sync.read(before);
    const b = PNG.sync.read(after);
    let hits = 0;
    const n = b.width * b.height;
    for (let i = 0; i < n; i++) {
        const o = i * 4;
        const changed =
            Math.max(
                Math.abs(a.data[o] - b.data[o]),
                Math.abs(a.data[o + 1] - b.data[o + 1]),
                Math.abs(a.data[o + 2] - b.data[o + 2])
            ) > 12;
        const gray =
            Math.abs(b.data[o] - 128) <= 3 &&
            Math.abs(b.data[o + 1] - 128) <= 3 &&
            Math.abs(b.data[o + 2] - 128) <= 3;
        if (changed && gray) hits++;
    }
    return hits / n;
}

/** The <td> holding the controls of the Html.row labelled `label`, as a CSS-free
 *  handle: rows are <tr><td>label</td><td>controls</td></tr>. Returns an evaluate-able
 *  snippet so every caller finds the row the same way. */
const rowScript = (label: string) => `(() => {
    const tds = Array.from(document.querySelectorAll("td"));
    const l = tds.find((t) => (t.textContent || "").trim() === ${JSON.stringify(label)});
    return l ? l.nextElementSibling : null;
})()`;

/** Clicks the element matching `selector` inside the row labelled `label`. */
async function clickInRow(page: Page, label: string, selector: string) {
    const r = await page.evaluate(
        `(() => {
            const cell = ${rowScript(label)};
            if (!cell) return "no row";
            const el = cell.querySelector(${JSON.stringify(selector)});
            if (!el) return "no " + ${JSON.stringify(selector)};
            el.click();
            return "clicked";
        })()`
    );
    expect(r, `${label} ${selector}`).toBe("clicked");
}

/** The Cross Section row: "none (...)" until one exists, then a remove button. */
async function crossSectionDefined(page: Page): Promise<boolean> {
    return (await page.evaluate(
        `(() => {
            const cell = ${rowScript("Cross Section:")};
            return !!(cell && cell.querySelector(".remove.icon"));
        })()`
    )) as boolean;
}

async function imageField(page: Page): Promise<string> {
    return (await page.evaluate(
        `(() => {
            const cell = ${rowScript("Image:")};
            const i = cell && cell.querySelector("input");
            return i ? i.value : "<no input>";
        })()`
    )) as string;
}

/**
 * Stubs Electron's open dialog to answer `answer`, and records the options it was called
 * with in `window.__dialogCalls` -- so the test can tell "the button opened the dialog"
 * apart from "the path got there some other way".
 */
async function stubOpenDialog(page: Page, answer: { canceled: boolean; filePaths: string[] }) {
    await page.evaluate((a) => {
        const w = window as any;
        w.__dialogCalls = [];
        w.aardvark = w.aardvark ?? {};
        w.aardvark.dialog = {
            showOpenDialog: (opts: unknown) => {
                w.__dialogCalls.push(opts);
                return Promise.resolve(a);
            },
        };
    }, answer);
}

async function dialogCalls(page: Page): Promise<any[]> {
    return page.evaluate(() => (window as any).__dialogCalls ?? []);
}

/** Opens the Curtain Settings accordion by clicking its title, and checks it opened. */
async function openCurtainSettings(page: Page) {
    const title = () =>
        page.evaluate(() => {
            const t = Array.from(document.querySelectorAll(".title")).find(
                (e) => (e.textContent ?? "").trim() === "Curtain Settings"
            ) as HTMLElement | undefined;
            if (!t) return { found: false, open: false };
            return { found: true, open: t.classList.contains("active") };
        });
    await expect.poll(async () => (await title()).found, { timeout: 60_000 }).toBe(true);
    if (!(await title()).open)
        await page.evaluate(() => {
            const t = Array.from(document.querySelectorAll(".title")).find(
                (e) => (e.textContent ?? "").trim() === "Curtain Settings"
            ) as HTMLElement | undefined;
            t?.click();
        });
    await expect.poll(async () => (await title()).open, { timeout: 10_000 }).toBe(true);
}

test("a cross section clips the surface and carries a curtain, with and without an image", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    const scene = drawingScene(artifacts, "cross-section");
    const magenta = solidPng(path.join(artifacts, "curtain-magenta.png"), [255, 0, 255]);
    const cyan = solidPng(path.join(artifacts, "curtain-cyan.png"), [0, 255, 255]);

    const app = await launchPro3d(scene);
    let context: BrowserContext | undefined;
    try {
        context = await browser.newContext();
        context.on("weberror", (e) => console.log("[page error]", e.error()));

        const render = await context.newPage();
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(render);
        await settled(render, "1-loaded.png", save);

        // the curtain's up vector and altitudes come from the scene body
        await expect
            .poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("Dimorphos");

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        // Near the upper limb, not through the middle. The clip region is the polygon of the
        // line plus the camera position, projected along the local up (CrossSectionClipping).
        // A line in the middle of the view has the camera straight above it, so that polygon
        // collapses onto the line and nothing is clipped -- docs/CrossSections.md says to
        // look at the line from the side for the same reason.
        await drawSkyLine(context, app, render, main, async () => {}, 90, -160);
        const drawn = await settled(render, "2-annotation.png", save);

        // --- 1. create the cross section -------------------------------------------------
        const annotations = await context.newPage();
        await annotations.goto(app.url + "?page=annotations");
        await annotations.waitForLoadState("networkidle");
        await openCurtainSettings(annotations);
        expect(await crossSectionDefined(annotations), "no cross section before Create").toBe(false);

        const properties = await context.newPage();
        await properties.goto(app.url + "?page=properties");
        await properties.waitForLoadState("networkidle");
        await expect
            .poll(
                () => properties.evaluate(`!!(${rowScript("Cross Section:")})`),
                { timeout: 60_000, intervals: [500] }
            )
            .toBe(true);
        await clickInRow(properties, "Cross Section:", "button");

        await expect
            .poll(() => crossSectionDefined(annotations), { timeout: 30_000, intervals: [500] })
            .toBe(true);
        await render.bringToFront();
        const clipped = await settled(render, "3-clipped.png", save);
        const clipDiff = diffPng(drawn, clipped).changedFraction;
        console.log(`clipping changed ${(clipDiff * 100).toFixed(2)} % of the frame`);
        expect(clipDiff, "Create clips the surface between the line and the camera").toBeGreaterThan(0.01);

        // --- 2. curtain on, no image: drawn in the base colour ----------------------------
        expect(await imageField(annotations), "no image chosen yet").toBe("");
        await clickInRow(annotations, "Curtain:", "i");
        await render.bringToFront();
        const plain = await settled(render, "4-curtain-base-color.png", save);
        const gray = newBaseColourFraction(clipped, plain);
        console.log(`curtain without image: ${(gray * 100).toFixed(2)} % of the frame in the base colour`);
        expect(gray, "the curtain is drawn in its base colour without an image").toBeGreaterThan(0.002);

        // --- 3. choose an image through the dialog ---------------------------------------
        await stubOpenDialog(annotations, { canceled: false, filePaths: [magenta.replace(/\\/g, "/")] });
        await clickInRow(annotations, "Image:", ".folder.open.icon");
        const calls = await dialogCalls(annotations);
        expect(calls.length, "the folder button opened the dialog").toBe(1);
        expect(calls[0]?.properties, "a single file is asked for").toEqual(["openFile"]);
        expect(calls[0]?.filters?.[0]?.extensions, "images are offered").toContain("png");

        await expect
            .poll(() => imageField(annotations), { timeout: 30_000, intervals: [500] })
            .toBe(path.normalize(magenta));
        await render.bringToFront();
        const textured = await settled(render, "5-curtain-image.png", save);
        const m = colourFraction(textured, [255, 0, 255]);
        console.log(`curtain with image: ${(m * 100).toFixed(2)} % magenta`);
        expect(m, "the chosen image is on the curtain").toBeGreaterThan(0.002);

        // --- 4. cancelling the dialog keeps the image -------------------------------------
        await stubOpenDialog(annotations, { canceled: true, filePaths: [] });
        await clickInRow(annotations, "Image:", ".folder.open.icon");
        expect((await dialogCalls(annotations)).length, "the dialog opened again").toBe(1);
        await annotations.waitForTimeout(1500);
        expect(await imageField(annotations), "cancel keeps the chosen image").toBe(path.normalize(magenta));

        // --- 5. remove the image: back to the base colour ---------------------------------
        await clickInRow(annotations, "Image:", ".remove.icon");
        await expect
            .poll(() => imageField(annotations), { timeout: 30_000, intervals: [500] })
            .toBe("");
        await render.bringToFront();
        const removed = await settled(render, "6-image-removed.png", save);
        expect(colourFraction(removed, [255, 0, 255]), "the image is gone from the curtain")
            .toBeLessThan(0.0002);
        expect(newBaseColourFraction(clipped, removed), "and the curtain is still there")
            .toBeGreaterThan(0.002);

        // --- 6. type the path instead of using the dialog ---------------------------------
        const input = annotations
            .locator("td", { hasText: /^Image:$/ })
            .locator("xpath=following-sibling::td[1]//input");
        await input.fill(cyan);
        await input.press("Enter");
        await render.bringToFront();
        const typed = await settled(render, "7-curtain-typed-path.png", save);
        const c = colourFraction(typed, [0, 255, 255]);
        console.log(`curtain with typed path: ${(c * 100).toFixed(2)} % cyan`);
        expect(c, "a typed path puts that image on the curtain").toBeGreaterThan(0.002);
    } finally {
        await context?.close();
        await app.stop();
    }
});
