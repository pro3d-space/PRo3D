// Dump the visible text of one PRo3D UI page, so a selector can be written against
// what is actually in the DOM rather than against a guess.
//   PRO3D_DUMP_PAGE   render | surfaces | gis | config   (default: surfaces)
//   PRO3D_DUMP_GREP   only print lines containing this (case-insensitive)
//
// Note: no nested function declarations inside page.evaluate -- the TypeScript
// loader rewrites them with a __name helper that does not exist in the page.
import { chromium } from "@playwright/test";
import { launchPro3d } from "./pro3d";

(async () => {
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const page = await (await browser.newContext()).newPage();
    const which = process.env.PRO3D_DUMP_PAGE ?? "surfaces";
    await page.goto(app.url + "?page=" + which);
    await page.waitForLoadState("networkidle");
    await page.waitForTimeout(6000);
    const lines: string[] = await page.evaluate(`
        (() => {
            const out = [];
            const els = document.querySelectorAll("*");
            for (const el of els) {
                let own = "";
                for (const n of el.childNodes)
                    if (n.nodeType === 3 && (n.textContent || "").trim())
                        own += (n.textContent || "").trim() + " ";
                const tag = el.tagName.toLowerCase();
                const cls = (el.getAttribute("class") || "").slice(0, 44);
                let opts = "";
                if (tag === "select") {
                    const o = [];
                    for (const x of el.options) o.push((x.textContent || "").trim());
                    opts = " OPTIONS[" + o.join("|") + "]";
                }
                if (own.trim() || tag === "select" || tag === "input" || /icon|checkbox/.test(cls))
                    out.push("<" + tag + (cls ? " ." + cls : "") + "> " + own.trim() + opts);
            }
            return out;
        })()
    `);
    const grep = (process.env.PRO3D_DUMP_GREP ?? "").toLowerCase();
    const shown = grep ? lines.filter((l) => l.toLowerCase().includes(grep)) : lines;
    console.log(shown.join("\n").slice(0, 14000));
    await browser.close();
    await app.stop();
})();
