// Probe: frame rate of the main view while the camera moves, with and without the map panel,
// and of the map panel itself while panning. Reads aardvark.js' per-render-control fps counter
// (span.fps, updated once per second while frames arrive; hidden unless showFPS="true").
import { chromium, Page, Frame } from "@playwright/test";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { launchPro3d, derivedScene } from "./pro3d";

const artifacts = path.join(__dirname, "..", "artifacts");

function frameOf(page: Page, pageName: string): Frame | undefined {
    return page.frames().find((f) => f.url().includes(`page=${pageName}`));
}

async function box(page: Page, pageName: string) {
    return page.evaluate((p) => {
        const f = document.querySelector(`iframe[src*="page=${p}"]`);
        const r = f?.getBoundingClientRect();
        return r ? { x: r.x, y: r.y, width: r.width, height: r.height } : null;
    }, pageName);
}

/** drag in a circle inside `b` for `ms`, sampling the fps counter of `frame` once per second */
async function dragAndSample(page: Page, b: { x: number; y: number; width: number; height: number }, frame: Frame, ms: number) {
    const cx = b.x + b.width / 2, cy = b.y + b.height / 2, r = Math.min(b.width, b.height) * 0.2;
    const samples: string[] = [];
    await page.mouse.move(cx + r, cy);
    await page.mouse.down();
    const start = Date.now();
    let nextSample = start + 1500;
    let a = 0;
    while (Date.now() - start < ms) {
        a += 0.08;
        await page.mouse.move(cx + r * Math.cos(a), cy + r * Math.sin(a));
        await page.waitForTimeout(16);
        if (Date.now() > nextSample) {
            nextSample += 1000;
            samples.push(await frame.evaluate(() => (document.querySelector("span.fps") as HTMLElement | null)?.innerText ?? "?"));
        }
    }
    await page.mouse.up();
    return samples;
}

async function reopenMap(page: Page) {
    const entry = '[data-test="layout-reopen"] [data-panel="mapprojection"]';
    for (let i = 0; i < 120; i++) {
        const titles = await page.evaluate(() => Array.from(document.querySelectorAll(".lm_tab .lm_title")).map((t) => (t.textContent ?? "").trim()));
        if (titles.includes("Map Projection")) return;
        await page.evaluate((sel) => (document.querySelector(sel) as HTMLElement | null)?.click(), entry);
        await page.waitForTimeout(1000);
    }
    throw new Error("map panel did not open");
}

async function selectMapTab(page: Page) {
    await page.evaluate(() => {
        const tab = Array.from(document.querySelectorAll(".lm_tab")).find(
            (e) => (e.querySelector(".lm_title")?.textContent ?? "").trim() === "Map Projection") as HTMLElement | undefined;
        for (const type of ["mousedown", "mouseup", "click"])
            tab?.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, button: 0, buttons: 1 }));
    });
}

(async () => {
    const scene = derivedScene("smoothness-body-fixed", (d) => {
        d.gisApp.gisSurfaces = [];
        d.gisApp.defaultObservationInfo.observer = { EntitySpiceName: "Dimorphos" };
        d.gisApp.defaultObservationInfo.referenceFrame = { FrameSpiceName: "DIMORPHOS_FIXED" };
        d.referenceSystem.planet = 2;
    });
    const layouts = fs.mkdtempSync(path.join(os.tmpdir(), "pro3d-smooth-"));
    const app = await launchPro3d(scene, { PRO3D_LAYOUT_DIR: layouts });
    const browser = await chromium.launch();
    const report: Record<string, string[]> = {};
    try {
        const page = await (await browser.newContext({ viewport: { width: 1600, height: 900 } })).newPage();
        await page.goto(app.url);
        await page.waitForSelector(".lm_tab", { timeout: 120_000 });
        await page.waitForTimeout(15000); // surfaces, shaders

        const renderBox = await box(page, "render");
        const render = frameOf(page, "render");
        if (!renderBox || !render) throw new Error("no main view");

        // timestamp every frame a render control receives (its img src is replaced per frame)
        const installCounter = (f: Frame) => f.evaluate(() => {
            const w = window as any;
            if (w.__frames) return;
            w.__frames = [] as number[];
            const img = document.querySelector("img.rendercontrol");
            if (img) new MutationObserver(() => w.__frames.push(performance.now())).observe(img, { attributes: true, attributeFilter: ["src"] });
        });
        const takeFrames = (f: Frame) => f.evaluate(() => {
            const w = window as any;
            const t: number[] = w.__frames ?? [];
            w.__frames = [];
            return { now: performance.now(), t };
        });
        const summarize = (name: string, r: { now: number; t: number[] }, from: number) => {
            const t = r.t;
            let maxGap = 0;
            for (let i = 1; i < t.length; i++) maxGap = Math.max(maxGap, t[i] - t[i - 1]);
            const span = (r.now - from) / 1000;
            report[name] = [`${t.length} frames in ${span.toFixed(1)} s = ${(t.length / span).toFixed(1)} fps, longest gap ${Math.round(maxGap)} ms`];
        };

        await installCounter(render);
        let t0 = await render.evaluate(() => performance.now());
        await dragAndSample(page, renderBox, render, 8000);
        summarize("A main view drag, no map panel", await takeFrames(render), t0);

        await reopenMap(page);
        await page.waitForTimeout(3000);
        await takeFrames(render);
        t0 = await render.evaluate(() => performance.now());
        await dragAndSample(page, renderBox, render, 8000);
        summarize("B main view drag, map tab present but not selected", await takeFrames(render), t0);

        // select the map tab and keep dragging the main view through the map's loading
        await takeFrames(render);
        t0 = await render.evaluate(() => performance.now());
        await selectMapTab(page);
        let last = t0;
        for (let s = 0; s < 6; s++) {
            await dragAndSample(page, renderBox, render, 15000);
            const r = await takeFrames(render);
            summarize(`C${s} main view drag while map panel loads/shows, segment ${s} (15 s)`, r, last);
            last = r.now;
        }
        await page.screenshot({ path: path.join(artifacts, "smoothness-after-load.png") });

        const mapBox = await box(page, "mapprojection");
        const map = frameOf(page, "mapprojection");
        if (mapBox && map && mapBox.width > 100) {
            await installCounter(map);
            await takeFrames(map);
            await takeFrames(render);
            const m0 = await map.evaluate(() => performance.now());
            await dragAndSample(page, mapBox, map, 10000);
            summarize(`D map panel pan (${Math.round(mapBox.width)}x${Math.round(mapBox.height)})`, await takeFrames(map), m0);
            summarize("D' main view frames meanwhile (not moved)", await takeFrames(render), m0);
        } else {
            report["D map panel"] = [`not visible: ${JSON.stringify(mapBox)}`];
        }
        await page.screenshot({ path: path.join(artifacts, "smoothness-end.png") });
    } finally {
        console.log(JSON.stringify(report, null, 2));
        await browser.close();
        await app.stop();
    }
})();
