// Poll the render view for up to 5 minutes; report when the body appears
// (first-run shader compilation of a new effect can take minutes).
import { chromium } from "@playwright/test";
import { launchPro3d } from "./pro3d";
import { PNG } from "pngjs";

function litFraction(buf: Buffer): number {
    const png = PNG.sync.read(buf);
    let lit = 0;
    const n = png.width * png.height;
    for (let i = 0; i < n; i++) {
        const o = i * 4;
        // background is ~#2A2A2A; count clearly brighter pixels
        if (png.data[o] > 70 || png.data[o + 1] > 70 || png.data[o + 2] > 70) lit++;
    }
    return lit / n;
}

(async () => {
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1200, height: 700 } });
    await page.goto(app.url + "?page=render");

    const started = Date.now();
    let appeared = false;
    while (Date.now() - started < 300_000) {
        await page.waitForTimeout(15_000);
        const shot = await page.screenshot();
        const lit = litFraction(shot);
        const secs = Math.round((Date.now() - started) / 1000);
        console.log(`t=${secs}s lit=${(lit * 100).toFixed(2)}%`);
        if (lit > 0.005) {
            console.log(`BODY VISIBLE after ~${secs}s`);
            await page.screenshot({ path: "artifacts/probe-slow.png" });
            appeared = true;
            break;
        }
    }
    if (!appeared) console.log("body did NOT appear within 5 minutes");
    await browser.close();
    await app.stop();
    process.exit(0);
})();
