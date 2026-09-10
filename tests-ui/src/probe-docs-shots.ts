// Capture the documentation screenshot set for docs/MultiImageProjection.md.
// Run: npx tsx src/probe-docs-shots.ts  (PRO3D_IMAGE_DIR must point at a COP date folder)
import { chromium, Page } from "@playwright/test";
import { launchPro3d, config } from "./pro3d";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

const outDir = path.resolve(__dirname, "..", "..", "docs", "images");

function litCenter(buf: Buffer): number {
    const png = PNG.sync.read(buf);
    let lit = 0, n = 0;
    for (let y = Math.floor(png.height * 0.3); y < Math.floor(png.height * 0.95); y++)
        for (let x = Math.floor(png.width * 0.3); x < Math.floor(png.width * 0.9); x++) {
            const o = (y * png.width + x) * 4;
            n++;
            if (png.data[o] > 70 || png.data[o + 1] > 70 || png.data[o + 2] > 70) lit++;
        }
    return lit / n;
}

async function waitForBody(page: Page, minLit = 0.003) {
    const t0 = Date.now();
    while (Date.now() - t0 < 600_000) {
        const lit = litCenter(await page.screenshot());
        if (lit > minLit) return;
        await page.waitForTimeout(3000);
    }
    throw new Error("body did not appear");
}

const img2317 = "HERA_AFC_2317_20270301_040000_COP.png";
const img2333 = "HERA_AFC_2333_20270301_080000_COP.png";
const img2341 = "HERA_AFC_2341_20270301_100000_COP.png";

async function clickRowIcon(gis: Page, name: string, icon: string) {
    const r = await gis.evaluate(
        `(() => {
            const matches = Array.from(document.querySelectorAll("*")).filter(e => (e.textContent || "").trim() === ${JSON.stringify(name)});
            const deepest = matches.filter(e => !Array.from(e.children).some(c => matches.includes(c)));
            if (deepest.length === 0) return "header not found";
            let el = deepest[0];
            while (el) {
                const row = el.nextElementSibling;
                const box = row ? row.querySelector("i.${icon}.icon") : null;
                if (box) { box.click(); return "clicked"; }
                el = el.parentElement;
            }
            return "no icon";
        })()`
    );
    if (r !== "clicked") throw new Error(`${icon} on ${name}: ${r}`);
}

async function hoverRow(gis: Page, name: string, type: string) {
    await gis.evaluate(
        `(() => {
            const matches = Array.from(document.querySelectorAll("*")).filter(e => (e.textContent || "").trim() === ${JSON.stringify(name)});
            const deepest = matches.filter(e => !Array.from(e.children).some(c => matches.includes(c)));
            let el = deepest[0];
            while (el && !(el.style && el.style.border.length > 0)) el = el.parentElement;
            if (el) el.dispatchEvent(new MouseEvent(${JSON.stringify(type)}, { bubbles: false }));
        })()`
    );
}

(async () => {
    fs.mkdirSync(outDir, { recursive: true });
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const context = await browser.newContext();

    const render = await context.newPage();
    await render.setViewportSize({ width: 1280, height: 800 });
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });

    const gis = await context.newPage();
    await gis.setViewportSize({ width: 1100, height: 1000 });
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await gis.locator("text=Import Directory").first().waitFor({ timeout: 60_000 });
    const dir = config.imageDir.replace(/\\/g, "/");
    await gis.evaluate(
        `(() => { window.aardvark = window.aardvark || {}; window.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(dir)}] }) }; })()`
    );
    await gis.locator("text=Import Directory").first().click();
    await gis.locator("text=/HERA_AFC_\\d+_\\d+_\\d+_COP\\.png/").first().waitFor({ timeout: 120_000 });
    await waitForBody(render);

    // 1: plain body, nothing stacked
    await render.waitForTimeout(3000);
    await render.screenshot({ path: path.join(outDir, "multiProjection-surface.png") });

    // 2: hover an image -> preview + footprint + wireframe
    await hoverRow(gis, img2333, "mouseenter");
    await render.waitForTimeout(5000);
    await render.screenshot({ path: path.join(outDir, "multiProjection-hover.png") });
    await hoverRow(gis, img2333, "mouseleave");
    await render.waitForTimeout(2000);

    // 3: stack two images
    await clickRowIcon(gis, img2333, "plus");
    await gis.waitForTimeout(1500);
    await clickRowIcon(gis, img2317, "plus");
    await gis.waitForTimeout(3000);
    await render.waitForTimeout(8000);
    await waitForBody(render);
    await render.screenshot({ path: path.join(outDir, "multiProjection-stack.png") });

    // 4: GIS tab with populated stack
    await gis.screenshot({ path: path.join(outDir, "multiProjection-gisTab.png") });

    // 5: fly-to 2317 via its STACK row (the fly icon sits inside that row, so
    // matching the row that CONTAINS the icon is unambiguous)
    const fly = await gis.evaluate(
        `(() => {
            const rows = Array.from(document.querySelectorAll("div")).filter(d =>
                d.querySelector("i.location.icon") && (d.textContent || "").trim() === ${JSON.stringify(img2317)});
            if (rows.length === 0) return "no stack row";
            rows[rows.length - 1].querySelector("i.location.icon").click();
            return "clicked";
        })()`
    );
    if (fly !== "clicked") throw new Error("fly-to: " + fly);
    await render.waitForTimeout(9000);
    await render.screenshot({ path: path.join(outDir, "multiProjection-flyTo.png") });

    // 6: coverage view
    const cov = await gis.evaluate(
        `(() => {
            const sels = Array.from(document.querySelectorAll("select"));
            const vis = sels.find(s => Array.from(s.options).some(o => o.textContent.trim() === "RelativeCount"));
            if (!vis) return "no visibility select";
            vis.value = Array.from(vis.options).find(o => o.textContent.trim() === "RelativeCount").value;
            vis.dispatchEvent(new Event("change", { bubbles: true }));
            return "set";
        })()`
    );
    console.log("coverage dropdown:", cov);
    await render.waitForTimeout(6000);
    await render.screenshot({ path: path.join(outDir, "multiProjection-coverage.png") });

    console.log("screenshots written to", outDir);
    await browser.close();
    await app.stop();
    process.exit(0);
})();
