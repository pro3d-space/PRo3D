// Screenshots of the top bar (closed and with menus open), for judging its styling.
//   PRO3D_PORT=54377 npx tsx src/probe-topbar.ts
import { chromium } from "@playwright/test";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { launchPro3d } from "./pro3d";

(async () => {
    const out = path.join(__dirname, "..", "artifacts", "topbar");
    fs.mkdirSync(out, { recursive: true });
    const layoutDir = fs.mkdtempSync(path.join(os.tmpdir(), "pro3d-topbar-probe-"));
    const app = await launchPro3d(undefined, { PRO3D_LAYOUT_DIR: layoutDir });
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
    try {
        await page.goto(app.url);
        await page.waitForSelector(".lm_tab", { timeout: 120_000 });
        await page.waitForTimeout(6000);
        const clip = { x: 0, y: 0, width: 1600, height: 110 };
        await page.screenshot({ path: path.join(out, "topbar.png"), clip });

        // open the main menu and its Layout submenu the way hovering does
        await page.evaluate(`(() => {
            const main = document.querySelector(".pro3d-topbar .ui.dropdown");
            $(main).dropdown("show");
        })()`);
        await page.waitForTimeout(800);
        await page.evaluate(`(() => {
            const layout = Array.from(document.querySelectorAll(".pro3d-topbar .ui.dropdown.item"))
                .find(e => (e.firstChild && e.firstChild.textContent || "").trim() === "Layout");
            if (layout) $(layout).dropdown("show");
        })()`);
        await page.waitForTimeout(800);
        await page.screenshot({ path: path.join(out, "menu-open.png"), clip: { x: 0, y: 0, width: 900, height: 620 } });

    } finally {
        await browser.close();
        await app.stop();
    }
})();
