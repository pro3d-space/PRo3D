// Screenshot the instrument view of a scene (#841: surfaces offscreen there too).
// Run: npx tsx src/probe-instrument.ts <scene.pro3d> [out.png]
import { chromium } from "@playwright/test";
import { launchPro3d, surfaceShadersReady } from "./pro3d";

(async () => {
    const scene = process.argv[2];
    const out = process.argv[3] ?? "artifacts/instrument.png";
    const app = await launchPro3d(scene);
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
    try {
        // the main render page first: it loads the shaders the instrument view shares
        await page.goto(app.url + "?page=render");
        await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(page);
        const inst = await browser.newPage({ viewport: { width: 1600, height: 900 } });
        await inst.goto(app.url + "?page=instrumentview");
        await inst.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await inst.waitForTimeout(20_000);
        await inst.screenshot({ path: out });
        console.log(`saved ${out}`);
    } finally {
        await browser.close();
        await app.stop();
        process.exit(0);
    }
})();
