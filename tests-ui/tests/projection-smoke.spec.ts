import { test, expect, Page } from "@playwright/test";
import { launchPro3d, Pro3d, config } from "../src/pro3d";
import { diffPng } from "../src/image";
import * as fs from "fs";
import * as path from "path";

/**
 * Smoke test for the (single-)image projection pipeline with the HERA COP
 * synthetic data: loads the Dimorphos projection scene, imports one date
 * folder of simulated AFC png images through the GIS tab, and asserts that
 * selecting an image visibly changes the rendered surface (i.e. the projection
 * actually lands on the OPC).
 *
 * Machine-local by design (real GPU, local OPC + image data); configure via
 * PRO3D_EXE / PRO3D_SCENE / PRO3D_IMAGE_DIR / PRO3D_PORT.
 */

let app: Pro3d;

const artifacts = path.join(__dirname, "..", "artifacts");

test.beforeAll(async () => {
    fs.mkdirSync(artifacts, { recursive: true });
    app = await launchPro3d();
});

test.afterAll(async () => {
    await app?.stop();
});

/** screenshot the page repeatedly until two consecutive frames are (nearly)
 *  identical, so OPC/LoD streaming has settled and diffs mean something */
async function stableScreenshot(page: Page, name: string): Promise<Buffer> {
    let prev = await page.screenshot();
    for (let i = 0; i < 60; i++) {
        await page.waitForTimeout(1000);
        const cur = await page.screenshot();
        const d = diffPng(prev, cur);
        if (d.changedFraction < 0.001) {
            fs.writeFileSync(path.join(artifacts, name), cur);
            return cur;
        }
        prev = cur;
    }
    fs.writeFileSync(path.join(artifacts, name), prev);
    return prev;
}

test("COP image projects onto the Dimorphos OPC", async ({ browser }) => {
    const context = await browser.newContext();
    context.on("weberror", (e) => console.log("[page error]", e.error()));

    // --- render view: baseline ------------------------------------------------
    const render = await context.newPage();
    await render.goto(app.url + "?page=render");
    // the render control streams frames into an img (mapping mode), no canvas
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    const baseline = await stableScreenshot(render, "baseline.png");

    // --- GIS tab: import the COP folder --------------------------------------
    const gis = await context.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");

    // the projected-images UI sits in a collapsed GisApp accordion
    await gis.locator("text=Projected Images").first().click();
    await expect(gis.locator("text=Import Directory").first()).toBeVisible({
        timeout: 60_000,
    });

    // stand in for the Electron directory picker: PRo3D asks
    // top.aardvark.dialog.showOpenDialog and feeds filePaths back into
    // aardvark.processEvent(_, 'onchoose', ...)
    const imageDirUnix = config.imageDir.replace(/\\/g, "/");
    await gis.evaluate((dir) => {
        const w = window as any;
        w.aardvark = w.aardvark ?? {};
        w.aardvark.dialog = {
            showOpenDialog: (_opts: unknown) =>
                Promise.resolve({ canceled: false, filePaths: [dir] }),
        };
    }, imageDirUnix);

    await gis.locator("text=Import Directory").first().click();

    // import lists the folder's pngs; rows carry the file names
    const anyRow = gis.locator("text=/HERA_AFC_\\d+_\\d+_\\d+_COP\\.png/").first();
    await expect(anyRow).toBeVisible({ timeout: 120_000 });

    // optionally select a specific image (e.g. a Dimorphos-pointed frame)
    // instead of the auto-selected first one
    const wanted = process.env.PRO3D_SELECT_IMAGE;
    if (wanted) {
        // single-shot DOM click: the incremental list re-renders often enough
        // that Playwright's actionability loop (scroll/stability checks) can
        // starve against it. Row layout (ProjectedImageListApp.view): header
        // div with the file name, next sibling holds the cells, first cell is
        // Select and contains the checkbox <i>.
        const result = await gis.evaluate((name) => {
            // deepest element whose text is exactly the file name (the text
            // may sit in a wrapper inside the header div)
            const matches = Array.from(document.querySelectorAll("*")).filter(
                (e) => (e.textContent ?? "").trim() === name
            );
            const deepest = matches.filter(
                (e) => !Array.from(e.children).some((c) => matches.includes(c))
            );
            if (deepest.length === 0) return "header not found";
            // climb until an ancestor whose next sibling holds the row cells
            // (recognizable by the checkbox <i> in its first cell)
            let el: Element | null = deepest[0];
            while (el) {
                const row = el.nextElementSibling;
                const box = row?.children[0]?.querySelector("i");
                if (box) {
                    (box as HTMLElement).click();
                    return "clicked";
                }
                el = el.parentElement;
            }
            return "checkbox not found";
        }, wanted);
        expect(result, `selecting ${wanted}`).toBe("clicked");
        await gis.waitForTimeout(2000);
    }

    // LoadImagesDir auto-selects the first image; give SPICE + texture load a
    // moment, then verify the render view changed where it matters
    const after = await stableScreenshot(render, "projected.png");

    const d = diffPng(baseline, after);
    console.log(
        `render diff after projection: ${(d.changedFraction * 100).toFixed(2)}% pixels, mean delta ${d.meanDelta.toFixed(2)}`
    );
    expect(
        d.changedFraction,
        "selecting a COP image should visibly change the rendered surface " +
            "(projection landing on the OPC)"
    ).toBeGreaterThan(0.001);
});
