// Screenshot the restructured GIS tab after an import. Run: npx tsx src/probe-gis-ui.ts
import { chromium } from "@playwright/test";
import { launchPro3d, config } from "./pro3d";

(async () => {
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const gis = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
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
    // add one to the stack for a fuller picture
    await gis.evaluate(`(() => { const p = document.querySelector("i.plus.icon"); if (p) p.click(); })()`);
    await gis.waitForTimeout(2000);
    await gis.screenshot({ path: "artifacts/gis-restructured.png" });
    console.log("saved artifacts/gis-restructured.png");
    await browser.close();
    await app.stop();
    process.exit(0);
})();
