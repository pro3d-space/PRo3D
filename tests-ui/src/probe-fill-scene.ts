// Screenshot a scene's render view after it settles (#841 fill problems).
// Run: npx tsx src/probe-fill-scene.ts <scene.pro3d> [out.png]
import { chromium } from "@playwright/test";
import { launchPro3d, surfaceShadersReady } from "./pro3d";

(async () => {
    const scene = process.argv[2];
    const out = process.argv[3] ?? "artifacts/fill-scene.png";
    const app = await launchPro3d(scene);
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
    try {
        await page.goto(app.url + "?page=render");
        await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(page);
        await page.waitForTimeout(20_000); // OPC LoD streaming
        await page.screenshot({ path: out });
        console.log(`saved ${out}`);
    } finally {
        await browser.close();
        await app.stop();
        process.exit(0);
    }
})();
