// Diagnostic probe for PRo3D Lite: launch with the given args, screenshot the render page
// every few seconds, report whether the stream is live. npx tsx src/probe-lite.ts [-- args]
import { chromium } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { launchLite } from "./lite";
import { streamLive, litFraction } from "./image";

(async () => {
    const args = process.argv.slice(2);
    const out = path.join(__dirname, "..", "artifacts", "lite-probe");
    fs.mkdirSync(out, { recursive: true });
    const app = await launchLite(args);
    console.log("log", app.logFile);
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage({ viewport: { width: 1000, height: 700 } });
        await page.goto(app.url + "?page=render");
        await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        for (let i = 0; i < 24; i++) {
            await page.waitForTimeout(5000);
            const shot = await page.locator("img.rendercontrol").screenshot();
            fs.writeFileSync(path.join(out, `shot-${i}.png`), shot);
            console.log(i, "live", streamLive(shot), "lit", litFraction(shot, 40).toFixed(3));
            if (streamLive(shot) && i > 2) break;
        }
    } finally {
        await browser.close();
        await app.stop();
    }
})();
