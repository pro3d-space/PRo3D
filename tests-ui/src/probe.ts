// Ad-hoc DOM probe: launch PRo3D, open pages, dump structure. Run with:
//   npx tsx src/probe.ts
import { chromium } from "@playwright/test";
import { launchPro3d } from "./pro3d";

(async () => {
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });

    await page.goto(app.url + "?page=render");
    await page.waitForTimeout(15000);

    // plain JS strings: tsx's esbuild transform injects helpers (__name) that
    // don't exist inside the page when passing functions to evaluate
    const dump = await page.evaluate(`(() => {
        const walk = (el, depth) => {
            if (depth > 4) return "";
            const id = el.id ? "#" + el.id : "";
            const cls = (typeof el.className === "string" && el.className)
                ? "." + el.className.split(" ").filter(Boolean).slice(0, 3).join(".")
                : "";
            let s = "  ".repeat(depth) + el.tagName.toLowerCase() + id + cls + "\\n";
            for (const c of Array.from(el.children)) s += walk(c, depth + 1);
            return s;
        };
        return walk(document.body, 0);
    })()`);
    console.log("=== ?page=render DOM ===");
    console.log(dump);

    await page.goto(app.url + "?page=gis");
    await page.waitForTimeout(8000);
    const dumpGis = await page.evaluate(`(() => {
        const texts = [];
        document.querySelectorAll("button, .title, .item, th").forEach((el) => {
            const t = el.innerText ? el.innerText.trim().replace(/\\s+/g, " ") : "";
            if (t && t.length < 80) texts.push(el.tagName.toLowerCase() + ": " + t);
        });
        return texts.join("\\n");
    })()`);
    console.log("=== ?page=gis clickables ===");
    console.log(dumpGis);

    await page.screenshot({ path: "artifacts/probe-gis.png" });
    await browser.close();
    await app.stop();
    process.exit(0);
})();
