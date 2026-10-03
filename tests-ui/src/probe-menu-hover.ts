// Does the hamburger menu open on hover? Runs against PRo3D Lite, or the Viewer with
// `viewer` as argument. npx tsx src/probe-menu-hover.ts [viewer]
import { chromium } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { launchLite } from "./lite";
import { launchPro3d } from "./pro3d";

(async () => {
    const viewer = process.argv[2] === "viewer";
    const out = path.join(__dirname, "..", "artifacts", "lite-probe");
    fs.mkdirSync(out, { recursive: true });
    const app = viewer ? await launchPro3d("") : await launchLite([]);
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage({ viewport: { width: 1500, height: 900 } });
        await page.goto(app.url);
        await page.waitForTimeout(viewer ? 20000 : 8000);
        await page.hover(".menu-bar .ui.dropdown.item");
        await page.mouse.move(19, 17, { steps: 5 });
        await page.click(".menu-bar .ui.dropdown.item i.sidebar");
        await page.waitForTimeout(1000);
        const state = await page.evaluate(`(() => {
            const m = document.querySelector('.menu-bar .ui.dropdown.item > .ui.menu');
            if (!m) return 'no menu element';
            const s = getComputedStyle(m);
            return 'display=' + s.display + ' visibility=' + s.visibility + ' classes=' + m.className + ' jquery=' + (typeof $) + ' dropdownFn=' + (typeof ($ && $.fn && $.fn.dropdown));
        })()`);
        console.log(viewer ? "viewer:" : "lite:", state);
        await page.screenshot({ path: path.join(out, viewer ? "menu-viewer.png" : "menu-lite.png"), clip: { x: 0, y: 0, width: 600, height: 400 } });
    } finally {
        await browser.close();
        await app.stop();
    }
})();
