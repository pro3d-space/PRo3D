// Probe: frame rate while panning the standalone map app (no PRo3D model around it).
import { chromium } from "@playwright/test";
import { launchMap } from "./mapprojection";
import { fixture } from "./pro3d";

(async () => {
    const app = await launchMap([fixture.opc]);
    const browser = await chromium.launch();
    try {
        for (const [w, h] of [[478, 393], [1200, 600]]) {
            const page = await (await browser.newContext({ viewport: { width: w, height: h } })).newPage();
            await page.goto(app.url);
            await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
            await page.waitForTimeout(8000);
            await page.evaluate(() => {
                const win = window as any;
                win.__frames = [];
                const img = document.querySelector("img.rendercontrol");
                if (img) new MutationObserver(() => win.__frames.push(performance.now())).observe(img, { attributes: true, attributeFilter: ["src"] });
            });
            const cx = w / 2, cy = h / 2, r = Math.min(w, h) * 0.2;
            await page.mouse.move(cx + r, cy);
            await page.mouse.down();
            const start = Date.now();
            let a = 0, moves = 0;
            while (Date.now() - start < 10000) {
                a += 0.08;
                await page.mouse.move(cx + r * Math.cos(a), cy + r * Math.sin(a));
                moves++;
                await page.waitForTimeout(Number(process.env.MOVE_WAIT_MS ?? 16));
            }
            await page.mouse.up();
            const t: number[] = await page.evaluate(() => (window as any).__frames);
            let gap = 0;
            for (let i = 1; i < t.length; i++) gap = Math.max(gap, t[i] - t[i - 1]);
            console.log(`standalone ${w}x${h}: ${t.length} frames in 10 s = ${(t.length / 10).toFixed(1)} fps, ${moves} mouse moves (${(moves / 10).toFixed(1)}/s), longest gap ${Math.round(gap)} ms`);
            await page.context().close();
        }
    } finally {
        await browser.close();
        await app.stop();
    }
})();
