// Screenshot of the whole PRo3D Lite window (Golden Layout with all panels), and of each panel
// page on its own. npx tsx src/probe-lite-ui.ts
import { chromium } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { launchLite, liteOpc } from "./lite";

(async () => {
    const out = path.join(__dirname, "..", "artifacts", "lite-probe");
    fs.mkdirSync(out, { recursive: true });
    const app = await launchLite(["--opc", liteOpc]);
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage({ viewport: { width: 1500, height: 900 } });
        await page.goto(app.url);
        await page.waitForTimeout(15000);
        await page.screenshot({ path: path.join(out, "ui-body.png") });

        // the hamburger menu opens on hover
        await page.hover(".menu-bar .ui.dropdown.item");
        await page.waitForTimeout(800);
        await page.screenshot({ path: path.join(out, "ui-menu-open.png"), clip: { x: 0, y: 0, width: 600, height: 300 } });
        try {
            await page.hover(".menu-bar .ui.menu .ui.dropdown.item:has-text('Scene')", { timeout: 5000 });
            await page.waitForTimeout(800);
        } catch (e) { console.log("submenu hover failed"); }
        await page.screenshot({ path: path.join(out, "ui-menu.png"), clip: { x: 0, y: 0, width: 600, height: 300 } });
        await page.mouse.move(700, 500);

        // a strip click switches the tool; the coordinate cross brings its settings onto row 2
        const render = page.frameLocator("iframe").first();
        await page.waitForTimeout(500);
        const clicked = await page.evaluate(`(() => {
            for (const f of Array.from(document.querySelectorAll('iframe'))) {
                const d = f.contentDocument; if (!d) continue;
                const tools = d.querySelectorAll('.pro3d-toolstrip .pro3d-tool');
                if (tools.length) { tools[tools.length - 2].click(); return tools.length; }
            }
            return 0;
        })()`);
        console.log("strip buttons", clicked);
        await page.waitForTimeout(1500);
        await page.screenshot({ path: path.join(out, "ui-tool-switched.png") });
        for (const p of ["annotations", "surfaces", "scene", "properties"]) {
            const panel = await browser.newPage({ viewport: { width: 420, height: 900 } });
            await panel.goto(app.url + "?page=" + p);
            await panel.waitForTimeout(3000);
            await panel.screenshot({ path: path.join(out, `ui-${p}.png`) });
            await panel.close();
        }
    } finally {
        await browser.close();
        await app.stop();
    }
})();
