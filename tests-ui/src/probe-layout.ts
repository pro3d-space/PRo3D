// Ad-hoc probe of the Golden Layout main page: tabs, close buttons, layout menu.
//   npx tsx src/probe-layout.ts
import { chromium } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import * as os from "os";
import { launchPro3d } from "./pro3d";

(async () => {
    const layoutDir = fs.mkdtempSync(path.join(os.tmpdir(), "pro3d-layout-probe-"));
    const app = await launchPro3d(undefined, { PRO3D_LAYOUT_DIR: layoutDir });
    const browser = await chromium.launch();
    const context = await browser.newContext({ viewport: { width: 1600, height: 900 } });
    context.on("page", (p) => console.log("[new page]", p.url()));
    const page = await context.newPage();
    page.on("console", (m) => console.log("[console]", m.type(), m.text()));
    page.on("pageerror", (e) => console.log("[pageerror]", e.message));
    try {
        await page.goto(app.url);
        await page.waitForSelector(".lm_tab", { timeout: 60_000 });
        await page.waitForTimeout(5000);

        const dump = await page.evaluate(`(() => {
            const tabs = Array.from(document.querySelectorAll(".lm_tab")).map(t => ({
                title: t.querySelector(".lm_title")?.textContent,
                active: t.classList.contains("lm_active"),
                close: (() => { const c = t.querySelector(".lm_close_tab"); return c ? getComputedStyle(c).display : "none-element"; })()
            }));
            const frames = Array.from(document.querySelectorAll("iframe.gl-aard-component")).map(f => f.getAttribute("src") + " " + f.style.display);
            const stackButtons = Array.from(document.querySelectorAll(".lm_controls > *")).map(c => c.className + " " + getComputedStyle(c).display);
            const menu = document.querySelector("[data-test=layout-menu]")?.outerHTML?.slice(0, 3000);
            const status = Array.from(document.querySelectorAll(".topmenu")).map(e => e.textContent.trim()).filter(t => t.startsWith("Layout")).join(" | ");
            return JSON.stringify({ tabs, frames, stackButtons, status, menu }, null, 2);
        })()`);
        console.log(dump);
        await page.screenshot({ path: path.join(__dirname, "..", "artifacts", "probe-layout.png") });
        console.log("layout dir:", fs.readdirSync(layoutDir));
    } finally {
        await browser.close();
        await app.stop();
    }
})();
