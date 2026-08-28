// Just load the scene and screenshot it. No import, no fly-to, no clicking.
// Its whole job is to answer "does the harness see what the user sees?" -- so that a
// blank frame in a bigger probe can be blamed on the scene rather than on the harness.
import { chromium } from "@playwright/test";
import { launchPro3d } from "./pro3d";
import { litFraction, streamLive, diffPng } from "./image";
import * as fs from "fs";
import * as path from "path";

const label = process.env.PRO3D_PROBE_LABEL ?? "look";
const artifacts = path.join(__dirname, "..", "artifacts");
const settleMs = Number(process.env.PRO3D_PROBE_SETTLE_MS ?? 120_000);

(async () => {
    fs.mkdirSync(artifacts, { recursive: true });
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const [vw, vh] = (process.env.PRO3D_PROBE_VIEWPORT ?? "1600x900").split("x").map(Number);
    const page = await (
        await browser.newContext({ viewport: { width: vw, height: vh } })
    ).newPage();
    await page.goto(app.url + "?page=render");
    await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });

    const started = Date.now();
    let shot = await page.screenshot();
    while ((!streamLive(shot) || litFraction(shot) < 0.003) && Date.now() - started < settleMs) {
        await page.waitForTimeout(3000);
        shot = await page.screenshot();
    }
    let prev = shot;
    for (let i = 0; i < 60; i++) {
        await page.waitForTimeout(1000);
        const cur = await page.screenshot();
        if (diffPng(prev, cur).changedFraction < 0.001) {
            prev = cur;
            break;
        }
        prev = cur;
    }
    const out = path.join(artifacts, `${label}.png`);
    fs.writeFileSync(out, prev);
    console.log(
        `${out}  lit ${(litFraction(prev) * 100).toFixed(2)}%  live ${streamLive(prev)}`
    );
    await browser.close();
    await app.stop();
})();
