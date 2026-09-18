// Refreshes aardvark-mark.json, the aardvark silhouette used in the boot
// screen's credit line, from Aardvark.UI's own logo file:
//
//   node extract-aardvark-mark.mjs <path to aardvark-light.svg>
//
// That file is an embedded resource of Aardvark.UI, so it is not on disk here;
// take it from an aardvark.media checkout, e.g.
//   git show 5.7.3:src/Aardvark.UI/resources/aardvark-light.svg > aardvark-light.svg
//
// The path sits under a chain of Inkscape group transforms, so we let the
// browser flatten them (getCTM) instead of multiplying matrices by hand, and
// store the result next to the path data.
import { chromium } from "playwright";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const here = path.dirname(fileURLToPath(import.meta.url));
const input = process.argv[2];
if (!input) {
    console.error("usage: node extract-aardvark-mark.mjs <aardvark-light.svg>");
    process.exit(1);
}

const browser = await chromium.launch();
const page = await browser.newPage();
await page.goto("file:///" + path.resolve(input).replace(/\\/g, "/"));

const info = await page.evaluate(() => {
    const el = document.getElementById("path4173-9"); // the silhouette
    const root = document.querySelector("svg");
    const m = root.getScreenCTM().inverse().multiply(el.getScreenCTM()); // path -> root user space
    const b = el.getBBox();
    return {
        d: el.getAttribute("d"),
        matrix: [m.a, m.b, m.c, m.d, m.e, m.f],
        bbox: { x: b.x, y: b.y, width: b.width, height: b.height },
        viewBox: root.getAttribute("viewBox"),
    };
});
await browser.close();

const out = path.join(here, "aardvark-mark.json");
fs.writeFileSync(out, JSON.stringify(info, null, 2));
console.log(`wrote ${out}`);
