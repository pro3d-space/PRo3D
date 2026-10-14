// Live render view vs server screenshots (/rendering/screenshot) of the same client (#842).
// Run: npx tsx src/probe-server-shot.ts <scene.pro3d> [outDir]
import { chromium } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { launchPro3d, surfaceShadersReady } from "./pro3d";

(async () => {
    const scene = process.argv[2];
    const out = process.argv[3] ?? "artifacts/server-shot";
    fs.mkdirSync(out, { recursive: true });
    const app = await launchPro3d(scene);
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
    try {
        await page.goto(app.url + "?page=render");
        await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(page);
        await page.waitForTimeout(20_000); // OPC LoD streaming
        await page.screenshot({ path: path.join(out, "live-before.png") });
        const id = await page.evaluate(() => document.querySelector("img.rendercontrol")?.closest("[id]")?.id ?? "");
        for (const [name, q] of [
            ["same", "w=1600&h=900&samples=4"],
            ["samples1", "w=1600&h=900&samples=1"],
            ["wide", "w=3200&h=900&samples=4"],
            ["samples8", "w=2400&h=1350&samples=8"],
        ]) {
            const res = await page.request.get(new URL(`/rendering/screenshot/${id}?${q}&fmt=png`, app.url).href);
            fs.writeFileSync(path.join(out, `server-${name}.png`), await res.body());
            console.log(`${name}: ${res.status()}`);
        }
        // does the live view still update? move the camera with the mouse wheel
        await page.mouse.move(800, 450);
        await page.mouse.wheel(0, -400);
        await page.waitForTimeout(5_000);
        await page.screenshot({ path: path.join(out, "live-after-zoom.png") });
        console.log(`saved to ${out}`);
    } finally {
        await browser.close();
        await app.stop();
        process.exit(0);
    }
})();
