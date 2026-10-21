// Probe: what does re-centring the map cost? Follow-cursor would move the map centre on every
// preview pick, which is the same work a pan does: only `center` changes, so the view-projection
// and the LoD decider are re-evaluated while zoom, footprints and markers stay put.
//
//   npx tsx src/probe-map-recenter.ts [surfaceCount]
//
// Reports, per surface count: frames delivered while idle, and while the centre moves as fast as
// the app accepts, with the longest gap between frames. MARS_OPC_ROOT overrides the data root;
// the tiles are not part of the repository.
import { chromium, Page } from "@playwright/test";
import { launchMap } from "./mapprojection";
import * as fs from "fs";
import * as path from "path";

const root = process.env.MARS_OPC_ROOT ?? "C:/pro3ddata/JezeroRGB";
const camera = "700586.97,3140941.06,1077437.94";

/** Jezero tiles that exist here, as OPC directories. */
function tiles(limit: number): string[] {
    const found: string[] = [];
    for (const group of ["Jezero1", "Jezero2", "Jezero3", "Jezero"]) {
        const dir = path.join(root, group);
        if (!fs.existsSync(dir)) continue;
        for (const name of fs.readdirSync(dir)) {
            const full = path.join(dir, name);
            if (!fs.statSync(full).isDirectory()) continue;
            const hasHierarchy = fs.readdirSync(full).some((sub) =>
                fs.existsSync(path.join(full, sub, "Patches", "patchhierarchy.xml")));
            if (hasHierarchy) found.push(full);
            if (found.length >= limit) return found;
        }
    }
    return found;
}

async function countFrames(page: Page, seconds: number, move?: (t: number) => Promise<void>) {
    await page.evaluate(() => {
        const win = window as any;
        win.__frames = [];
        const img = document.querySelector("img.rendercontrol");
        if (img) {
            win.__obs?.disconnect();
            win.__obs = new MutationObserver(() => win.__frames.push(performance.now()));
            win.__obs.observe(img, { attributes: true, attributeFilter: ["src"] });
        }
    });
    const start = Date.now();
    let steps = 0;
    while (Date.now() - start < seconds * 1000) {
        if (move) { await move((Date.now() - start) / 1000); steps++; }
        await page.waitForTimeout(16);
    }
    const t: number[] = await page.evaluate(() => (window as any).__frames);
    let gap = 0;
    for (let i = 1; i < t.length; i++) gap = Math.max(gap, t[i] - t[i - 1]);
    return { fps: t.length / seconds, frames: t.length, gap: Math.round(gap), steps };
}

(async () => {
    const counts = (process.argv[2] ? [Number(process.argv[2])] : [4, 24, 58]);
    for (const n of counts) {
        const opcs = tiles(n);
        if (opcs.length === 0) { console.log("no tiles found under " + root); return; }
        const app = await launchMap(opcs, ["--planet", "Mars", "--camera", camera]);
        const browser = await chromium.launch();
        try {
            const page = await (await browser.newContext({ viewport: { width: 1200, height: 620 } })).newPage();
            await page.goto(app.url);
            await page.waitForSelector("img.rendercontrol", { timeout: 180_000 });
            await page.click("text=Zoom to data");
            await page.waitForTimeout(25_000);   // let the patches stream in at that zoom

            const idle = await countFrames(page, 6);
            // the centre moves the way follow-cursor would move it: a new centre per event,
            // as fast as the app takes them. A drag is the same update path.
            const cx = 600, cy = 310, r = 140;
            await page.mouse.move(cx + r, cy);
            await page.mouse.down();
            const moving = await countFrames(page, 10, async (t) => {
                const a = t * 2.0;
                await page.mouse.move(cx + r * Math.cos(a), cy + r * Math.sin(a));
            });
            await page.mouse.up();

            console.log(`${opcs.length} surfaces: idle ${idle.fps.toFixed(1)} fps (gap ${idle.gap} ms) | ` +
                        `re-centring ${moving.fps.toFixed(1)} fps over ${moving.steps} moves (gap ${moving.gap} ms)`);
        } finally {
            await browser.close();
            await app.stop();
        }
    }
})();
