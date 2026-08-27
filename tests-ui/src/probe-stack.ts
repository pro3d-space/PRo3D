// Add one image to the stack, then watch the render view for minutes:
// does the body come back (slow path) or stay gone (hard fault)?
import { chromium } from "@playwright/test";
import { launchPro3d, config } from "./pro3d";
import { PNG } from "pngjs";

function litFractionRight(buf: Buffer): number {
    // central region only: excludes the legend (left), HUD text (top-left)
    // and the center marker is small enough not to matter
    const png = PNG.sync.read(buf);
    let lit = 0, n = 0;
    const x0 = Math.floor(png.width * 0.3), x1 = Math.floor(png.width * 0.9);
    const y0 = Math.floor(png.height * 0.3), y1 = Math.floor(png.height * 0.95);
    for (let y = y0; y < y1; y++) {
        for (let x = x0; x < x1; x++) {
            const o = (y * png.width + x) * 4;
            n++;
            if (png.data[o] > 70 || png.data[o + 1] > 70 || png.data[o + 2] > 70) lit++;
        }
    }
    return lit / n;
}

(async () => {
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const context = await browser.newContext();
    const render = await context.newPage();
    await render.goto(app.url + "?page=render");

    // wait for the body first
    let t0 = Date.now();
    while (Date.now() - t0 < 240_000) {
        await render.waitForTimeout(3000);
        const lit = litFractionRight(await render.screenshot());
        if (lit > 0.003) { console.log(`baseline body after ${Math.round((Date.now()-t0)/1000)}s (lit ${(lit*100).toFixed(2)}%)`); break; }
    }

    // GIS: import + add first image to stack
    const gis = await context.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await gis.locator("text=Import Directory").first().waitFor({ timeout: 60_000 });
    const dir = config.imageDir.replace(/\\/g, "/");
    await gis.evaluate(
        `(() => { window.aardvark = window.aardvark || {}; window.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(dir)}] }) }; })()`
    );
    await gis.locator("text=Import Directory").first().click();
    await gis.locator("text=/HERA_AFC_\\d+_\\d+_\\d+_COP\\.png/").first().waitFor({ timeout: 120_000 });
    const added = await gis.evaluate(
        `(() => { const p = document.querySelector("i.plus.icon"); if (!p) return "no plus"; p.click(); return "clicked"; })()`
    );
    console.log("add to stack:", added);

    // watch the render view
    t0 = Date.now();
    while (Date.now() - t0 < 240_000) {
        await render.waitForTimeout(10_000);
        const shot = await render.screenshot();
        const lit = litFractionRight(shot);
        console.log(`t=${Math.round((Date.now()-t0)/1000)}s lit=${(lit*100).toFixed(2)}%`);
        if (lit > 0.003) {
            await render.screenshot({ path: "artifacts/probe-stack.png" });
            console.log("BODY VISIBLE with stack");
            break;
        }
    }
    await render.screenshot({ path: "artifacts/probe-stack-final.png" });
    await browser.close();
    await app.stop();
    process.exit(0);
})();
