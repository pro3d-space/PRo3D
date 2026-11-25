import { test, expect, Frame, Page } from "@playwright/test";
import { fixture, launchPro3d, surfaceShadersReady } from "../src/pro3d";
import { drawingScene, settled } from "../src/drawing";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

/**
 * The Screenshots panel captures the main view -- docs/Screenshots.md.
 *
 * The panel asks the render server for a screenshot of one render control. The server lists
 * every live control (the main view, the instrument view, each page and iframe showing one)
 * in no particular order; the panel used to take the first, so with several views open it
 * could save any of them. Now the main view reports its scene name when it boots and the
 * panel asks for that one.
 *
 * Clicks the real camera button with several render controls live, and compares the saved
 * file with a server screenshot of a main view at the same size.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "screenshot");
const shotDir = path.join(artifacts, "shots");

test.setTimeout(20 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

/** The render server's live controls (/rendering/stats.json is JSON inside a JSON string). */
async function liveScenes(page: Page, url: string): Promise<string[]> {
    const res = await page.request.get(new URL("/rendering/stats.json", url).href);
    let body: any = await res.json();
    if (typeof body === "string") body = JSON.parse(body);
    return (body as { name: string }[]).map((s) => s.name);
}

/** The frame (golden-layout panes are iframes) whose Screenshots panel is in the DOM. */
async function screenshotPanel(main: Page): Promise<Frame> {
    let found: Frame | undefined;
    await expect
        .poll(
            async () => {
                for (const f of main.frames()) {
                    const has = await f
                        .evaluate(() =>
                            Array.from(document.querySelectorAll(".title")).some(
                                (t) => (t.textContent ?? "").trim() === "Screenshots"
                            )
                        )
                        .catch(() => false);
                    if (has) {
                        found = f;
                        return true;
                    }
                }
                return false;
            },
            { message: "the Screenshots panel (Config tab)", timeout: 120_000, intervals: [1000] }
        )
        .toBe(true);
    return found!;
}

/** Fraction of pixels whose colour differs by more than `threshold` (summed over RGB). */
function mismatch(a: PNG, b: PNG, threshold = 24) {
    let n = 0;
    for (let i = 0; i < a.data.length; i += 4) {
        const d =
            Math.abs(a.data[i] - b.data[i]) + Math.abs(a.data[i + 1] - b.data[i + 1]) + Math.abs(a.data[i + 2] - b.data[i + 2]);
        if (d > threshold) n++;
    }
    return n / (a.width * a.height);
}

/** Fraction of pixels that are not the (white) screenshot background. */
function nonWhite(p: PNG) {
    let n = 0;
    for (let i = 0; i < p.data.length; i += 4)
        if (255 * 3 - p.data[i] - p.data[i + 1] - p.data[i + 2] > 30) n++;
    return n / (p.width * p.height);
}

test("the Screenshots panel saves the main view", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    fs.rmSync(shotDir, { recursive: true, force: true });
    fs.mkdirSync(shotDir, { recursive: true });

    const scene = drawingScene(artifacts, "screenshot");
    const app = await launchPro3d(scene, undefined, ["--screenshotDirectory", shotDir]);
    const context = await browser.newContext();
    try {
        // a main view on a page of its own ...
        const render = await context.newPage();
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(render);
        await settled(render, "1-render-page.png", save);
        const renderPageScene = await render.evaluate(
            () => document.querySelector("img.rendercontrol")?.closest("[id]")?.id ?? ""
        );
        expect(renderPageScene, "the render page's control id").not.toBe("");

        // ... and the full GUI, with its main view (booting last, so the one captured) and the
        // instrument view - a control that must never be captured in place of the main view
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");
        const panel = await screenshotPanel(main);
        await main.locator(".lm_title", { hasText: "Instrument View" }).first().click();
        await expect
            .poll(async () => (await liveScenes(main, app.url)).length, {
                message: "render page, main view and instrument view live",
                timeout: 120_000,
                intervals: [2000],
            })
            .toBeGreaterThan(2);
        await main.waitForTimeout(3000);
        const live = await liveScenes(main, app.url);
        console.log(`live render controls: ${live.join(", ")}`);

        // the panel's defaults: 500 x 500 px, white background, PNG
        const clicked = await panel.evaluate(() => {
            const title = Array.from(document.querySelectorAll(".title")).find(
                (t) => (t.textContent ?? "").trim() === "Screenshots"
            );
            const button = title?.parentElement?.querySelector("i.camera.icon")?.closest("button") as HTMLElement | null;
            if (!button) return false;
            button.click();
            return true;
        });
        expect(clicked, "the camera button in the Screenshots panel").toBe(true);

        let saved = "";
        await expect
            .poll(
                () => {
                    saved = fs.readdirSync(shotDir).find((f) => /^img\d+\.png$/.test(f)) ?? "";
                    return saved;
                },
                { message: `a screenshot in ${shotDir} (see ${app.logFile})`, timeout: 120_000, intervals: [1000] }
            )
            .not.toBe("");
        // written in one go, but give the file system a moment
        await main.waitForTimeout(1000);
        const png = fs.readFileSync(path.join(shotDir, saved));
        save("2-panel-shot.png", png);
        const shot = PNG.sync.read(png);
        expect([shot.width, shot.height], "the panel's size").toEqual([500, 500]);

        const body = nonWhite(shot);
        console.log(`panel shot: ${(body * 100).toFixed(1)}% of pixels not background`);
        expect(body, "the surface is in the screenshot").toBeGreaterThan(0.02);

        // the same picture as the main view: both main views share the camera, so a server
        // screenshot of the render page's control at the panel's settings must match
        const res = await context.request.get(
            new URL(
                `/rendering/screenshot/${renderPageScene}?w=500&h=500&samples=4&fmt=png&background=[1.0,1.0,1.0,1.0]`,
                app.url
            ).href
        );
        expect(res.ok(), `reference screenshot: ${res.status()}`).toBe(true);
        const refPng = Buffer.from(await res.body());
        save("3-main-view-reference.png", refPng);
        const diff = mismatch(shot, PNG.sync.read(refPng));
        console.log(`panel shot vs main view: ${(diff * 100).toFixed(2)}% of pixels differ`);
        expect(diff, "the panel saved the main view").toBeLessThan(0.02);

        // and the choice mattered: some live control shows another picture, which the panel
        // could have saved by taking whichever control the server happens to list first
        const others: number[] = [];
        for (const scene of live) {
            const r = await context.request.get(
                new URL(`/rendering/screenshot/${scene}?w=500&h=500&samples=4&fmt=png&background=[1.0,1.0,1.0,1.0]`, app.url).href
            );
            if (!r.ok()) continue;
            const buf = Buffer.from(await r.body());
            save(`4-live-${scene}.png`, buf);
            others.push(mismatch(shot, PNG.sync.read(buf)));
        }
        console.log(`live controls vs panel shot: ${others.map((d) => (d * 100).toFixed(1) + "%").join(", ")}`);
        expect(Math.max(...others), "another live control renders a different picture").toBeGreaterThan(0.05);
    } finally {
        await context.close();
        await app.stop();
    }
});
