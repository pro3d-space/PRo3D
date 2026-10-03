// The full viewer through its real UI: empty start, import the MSL OPC through the menu,
// wait for the surface, Ctrl+click a line onto it. Screenshots in artifacts/viewer-probe.
// npx tsx src/probe-viewer-draw.ts
import { chromium, Page } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { launchPro3d } from "./pro3d";
const liteOpc = path.join(__dirname, "..", "..", "src", "Tests", "resources", "1087_004779_MSLMST_0011");
import { diffPng, litFraction, streamLive } from "./image";

const out = path.join(__dirname, "..", "artifacts", "viewer-probe");

async function settle(page: Page, name: string): Promise<Buffer> {
    const until = Date.now() + 10 * 60_000;
    let last: Buffer | undefined;
    for (;;) {
        await page.waitForTimeout(1000);
        const shot = await page.locator("img.rendercontrol").screenshot();
        const ready = streamLive(shot) && litFraction(shot, 40) >= 0.1;
        if (ready && last && diffPng(last, shot).changedFraction < 0.001) {
            fs.writeFileSync(path.join(out, `${name}.png`), shot);
            return shot;
        }
        last = ready ? shot : undefined;
        if (Date.now() > until) {
            fs.writeFileSync(path.join(out, `${name}-timeout.png`), shot);
            throw new Error(`${name}: did not settle`);
        }
    }
}

(async () => {
    fs.mkdirSync(out, { recursive: true });
    const app = await launchPro3d("");
    const browser = await chromium.launch();
    try {
        const body = await browser.newPage({ viewport: { width: 1400, height: 900 } });
        await body.goto(app.url);
        await body.waitForSelector(".pro3d-topbar", { timeout: 120_000 });
        await body.waitForTimeout(5000);
        const opc = JSON.stringify(liteOpc.replace(/\\/g, "/"));
        await body.evaluate(`(() => {
            window.aardvark = window.aardvark || {};
            window.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${opc}] }) };
            const item = Array.from(document.querySelectorAll('.item')).find(e => e.textContent.trim() === 'Import OPCs');
            if (!item) throw new Error('no Import OPCs item');
            item.click();
        })()`);
        console.log("import requested");

        const render = await browser.newPage({ viewport: { width: 1200, height: 800 } });
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 120_000 });
        const before = await settle(render, "01-imported");
        console.log("surface visible");

        const box = (await render.locator("img.rendercontrol").boundingBox())!;
        await render.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await render.keyboard.down("Control");
        await render.waitForTimeout(500);
        for (const [fx, fy] of [[0.5, 0.45], [0.5, 0.6]]) {
            await render.mouse.click(box.x + box.width * fx, box.y + box.height * fy);
            await render.waitForTimeout(500);
        }
        await render.keyboard.up("Control");
        await render.mouse.move(box.x + 20, box.y + box.height - 20);
        await render.waitForTimeout(1500);
        const after = await settle(render, "02-drawn");
        console.log("changed fraction after drawing", diffPng(before, after).changedFraction.toFixed(4));
    } finally {
        await browser.close();
        await app.stop();
    }
})();
