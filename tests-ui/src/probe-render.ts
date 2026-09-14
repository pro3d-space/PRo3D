// Screenshot the render view of the loaded scene. Run: npx tsx src/probe-render.ts
import { chromium } from "@playwright/test";
import { launchPro3d } from "./pro3d";

(async () => {
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });

    await page.goto(app.url + "?page=render");
    await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await page.waitForTimeout(20_000); // let OPC LoD streaming settle
    await page.screenshot({ path: "artifacts/render-after-entity-fix.png" });
    console.log("saved artifacts/render-after-entity-fix.png");

    await browser.close();
    await app.stop();
    process.exit(0);
})();
