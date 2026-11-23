// Probe: does the map projection work for a planet? Runs the standalone map on real Jezero
// OPC tiles with --planet Mars, and reports what is on screen at zoom 1 (the data is far below
// a pixel there, so only the footprint boxes and the camera marker can show it), after
// "Zoom to data", and how many pixels of actual surface arrive.
//
//   npx tsx src/probe-mars-map.ts
//
// MARS_OPCS overrides the tiles; they are not part of the repository.
import { chromium, Page } from "@playwright/test";
import { launchMap } from "./mapprojection";
import * as path from "path";
import * as fs from "fs";

const tiles = (process.env.MARS_OPCS ?? [
    "C:/pro3ddata/JezeroRGB/Jezero1/Jezero_05_04",
    "C:/pro3ddata/JezeroRGB/Jezero1/Jezero_05_05",
    "C:/pro3ddata/JezeroRGB/Jezero1/Jezero_06_04",
    "C:/pro3ddata/JezeroRGB/Jezero1/Jezero_06_05",
].join(";")).split(";").filter((t) => fs.existsSync(t));

// a camera a few hundred metres above the first tile's centre (body-fixed, from its Patch.xml)
const camera = process.env.MARS_CAMERA ?? "700586.97,3140941.06,1077437.94";

/** Counts what the rendered frame is made of, by colour family. */
async function frameStats(page: Page) {
    return await page.evaluate(async () => {
        const img = document.querySelector("img.rendercontrol") as HTMLImageElement;
        const canvas = document.createElement("canvas");
        canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
        const ctx = canvas.getContext("2d")!;
        ctx.drawImage(img, 0, 0);
        const { data } = ctx.getImageData(0, 0, canvas.width, canvas.height);
        let surface = 0, footprint = 0, camera = 0, grid = 0, background = 0;
        for (let i = 0; i < data.length; i += 4) {
            const r = data[i], g = data[i + 1], b = data[i + 2];
            if (r < 45 && g < 45 && b < 45) background++;
            else if (b > 150 && b > r + 40 && g > r) footprint++;          // 120,200,255
            else if (r > 180 && g > 90 && g < 200 && b < 110) camera++;    // 255,150,40
            else if (Math.abs(r - g) < 25 && Math.abs(g - b) < 25) grid++; // grey graticule
            else surface++;
        }
        return { total: canvas.width * canvas.height, surface, footprint, camera, grid, background,
                 width: canvas.width, height: canvas.height };
    });
}

(async () => {
    if (tiles.length === 0) { console.log("no Jezero tiles found - set MARS_OPCS"); return; }
    console.log(`tiles: ${tiles.length}`);
    const app = await launchMap(tiles, ["--planet", "Mars", "--camera", camera]);
    const browser = await chromium.launch();
    const out = path.join(__dirname, "..", "artifacts");
    fs.mkdirSync(out, { recursive: true });
    try {
        const page = await (await browser.newContext({ viewport: { width: 1024, height: 620 } })).newPage();
        page.on("console", (m) => { if (m.text().includes("[map]")) console.log("  page:", m.text()); });
        await page.goto(app.url);
        await page.waitForSelector("img.rendercontrol", { timeout: 120_000 });
        await page.waitForTimeout(12_000);

        console.log("zoom 1 (whole Mars):", JSON.stringify(await frameStats(page)));
        await page.screenshot({ path: path.join(out, "mars-map-zoom1.png") });

        await page.click("text=Zoom to data");
        await page.waitForTimeout(20_000);
        console.log("after Zoom to data:", JSON.stringify(await frameStats(page)));
        await page.screenshot({ path: path.join(out, "mars-map-fitted.png") });

        await page.click("text=Polar north");
        await page.waitForTimeout(8_000);
        console.log("polar north:", JSON.stringify(await frameStats(page)));
        await page.screenshot({ path: path.join(out, "mars-map-polar.png") });
        console.log(`screenshots in ${out}`);
    } finally {
        await browser.close();
        await app.stop();
    }
})();
