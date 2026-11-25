// Probe: does a Map Projection panel that sits in the layout but is NOT the selected tab do any
// work? This is the whole "no regressions when the view is not open" question (#772): the panel is
// the last tab of a stack whose first tab is selected, so in the default layout it is a background
// tab, and Golden Layout decides whether that instantiates its iframe.
//
//   npx tsx src/probe-map-background-tab.ts
//
// Two detectors: the iframes the layout created (their src carries ?page=<id>), and the "[map]"
// lines the panel logs when its inputs are evaluated. The run proves the detectors work by
// selecting the tab afterwards: silent before, loud after.
import { chromium, Page } from "@playwright/test";
import { launchPro3d } from "./pro3d";
import * as fs from "fs";

const scene = process.env.MARS_SCENE ?? "C:/pro3ddata/JezeroRGB/traversePriorities.pro3d";

const mapLines = (logFile: string) =>
    (fs.readFileSync(logFile, "utf8").match(/\[map\]/g) ?? []).length;

const pages = (page: Page) => page.evaluate(() =>
    Array.from(document.querySelectorAll("iframe"))
        .map((f) => { const m = (f.getAttribute("src") ?? "").match(/page=([^&]*)/); return m ? m[1] : "(none)"; })
        .sort());

(async () => {
    if (!fs.existsSync(scene)) { console.log(`no scene at ${scene}`); return; }
    const app = await launchPro3d(scene);
    const browser = await chromium.launch();
    try {
        const page = await (await browser.newContext({ viewport: { width: 1600, height: 900 } })).newPage();
        await page.goto(app.url);
        await page.waitForSelector(".lm_tab", { timeout: 180_000 });
        await page.waitForTimeout(45_000);   // the scene has 58 surfaces; let it settle

        const titles = await page.evaluate(() =>
            Array.from(document.querySelectorAll(".lm_tab .lm_title")).map((t) => (t.textContent ?? "").trim()));
        console.log(`tabs: ${titles.join(", ")}`);
        console.log(`untouched: iframes = [${(await pages(page)).join(", ")}], [map] log lines = ${mapLines(app.logFile)}`);

        // control: select the tab, the panel must come alive
        await page.evaluate(() => {
            const tab = Array.from(document.querySelectorAll(".lm_tab")).find(
                (e) => (e.querySelector(".lm_title")?.textContent ?? "").trim() === "Map Projection");
            (tab as HTMLElement | null)?.click();
        });
        await page.waitForTimeout(40_000);
        console.log(`after selecting: iframes = [${(await pages(page)).join(", ")}], [map] log lines = ${mapLines(app.logFile)}`);
    } finally {
        await browser.close();
        await app.stop();
    }
})();
