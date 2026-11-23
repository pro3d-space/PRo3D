// Dumps the top bar's elements with computed colours, to find what still renders light.
//   PRO3D_PORT=54377 npx tsx src/probe-topbar-dom.ts
import { chromium } from "@playwright/test";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { launchPro3d } from "./pro3d";

(async () => {
    const layoutDir = fs.mkdtempSync(path.join(os.tmpdir(), "pro3d-topbar-probe-"));
    const app = await launchPro3d(undefined, { PRO3D_LAYOUT_DIR: layoutDir });
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
    try {
        await page.goto(app.url);
        await page.waitForSelector(".lm_tab", { timeout: 120_000 });
        await page.waitForTimeout(4000);
        const lines = (await page.evaluate(`(() => {
            const out = [];
            const walk = (el, d) => {
                const cs = getComputedStyle(el);
                const cls = typeof el.className === "string" ? el.className : "";
                const own = Array.from(el.childNodes).filter(n => n.nodeType === 3).map(n => n.textContent.trim()).join("").slice(0, 30);
                out.push("  ".repeat(d) + el.tagName.toLowerCase() + "." + cls.split(" ").join(".") + " bg=" + cs.backgroundColor + " col=" + cs.color + " op=" + cs.opacity + (el.disabled ? " DISABLED" : "") + (own ? " [" + own + "]" : ""));
                if (d < 9) for (const c of el.children) walk(c, d + 1);
            };
            walk(document.querySelector(".pro3d-topbar"), 0);
            return out;
        })()`)) as string[];
        console.log(lines.filter((l) => !l.includes(".menu.") || l.includes("[")).join("\n"));
    } finally {
        await browser.close();
        await app.stop();
    }
})();
