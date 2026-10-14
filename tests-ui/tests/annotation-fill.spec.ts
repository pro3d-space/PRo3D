import { test, expect, Page, BrowserContext } from "@playwright/test";
import { fixture } from "../src/pro3d";
import { awaitIdle, annotationCount, clickTool, drawingScene, openDrawingViewer, pick, selectByOption, settled, tool } from "../src/drawing";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

/**
 * Filled annotations lie on the terrain -- docs/AnnotationFill.md, #841.
 *
 * Draws a sky-projected, filled Polygon on the Dimorphos OPC through the UI and compares the
 * frame against the one before. Dimorphos is a ~160 m body and the polygon spans tens of
 * metres of it, so the surface inside the outline bulges well above the plane through its
 * corners: the old fill, a flat sheet between the outline points, lay *under* that bulge and
 * was hidden by it everywhere but near the rim. The stencil fill tints every terrain pixel
 * inside the outline, so the interior of the polygon must change almost everywhere.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "annotation-fill");

test.setTimeout(30 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

/** Ticks "fill new annotations" in the annotation toolbar (the square icon after "Fill/Alpha:"). */
async function enableFillForNewAnnotations(main: Page) {
    const find = () =>
        main.evaluate(() => {
            const label = Array.from(document.querySelectorAll("div")).find(
                (d) => (d.textContent ?? "").trim() === "Fill/Alpha:"
            );
            if (!label) return "no label";
            let box: Element | null = label.parentElement;
            while (box && !box.nextElementSibling) box = box.parentElement;
            const icon = box?.nextElementSibling?.querySelector("i.square.outline.icon") as HTMLElement | null;
            if (!icon) return "no checkbox";
            if (icon.classList.contains("check")) return "already on";
            icon.click();
            return "clicked";
        });
    await expect.poll(find, { timeout: 60_000, intervals: [500] }).toMatch(/clicked|already on/);
}

/** Fraction of pixels in the rectangle whose colour differs between two frames. */
function changedFraction(a: PNG, b: PNG, x0: number, y0: number, x1: number, y1: number, threshold = 24) {
    let changed = 0;
    let total = 0;
    for (let y = y0; y < y1; y++)
        for (let x = x0; x < x1; x++) {
            const i = (y * a.width + x) * 4;
            const d =
                Math.abs(a.data[i] - b.data[i]) + Math.abs(a.data[i + 1] - b.data[i + 1]) + Math.abs(a.data[i + 2] - b.data[i + 2]);
            if (d > threshold) changed++;
            total++;
        }
    return changed / total;
}

/**
 * Server screenshots (/rendering/screenshot) render the scene again at the requested size. The
 * surfaces' offscreen targets must follow it (ViewerUtils.offscreenScene builds them from
 * RenderClientValues): at another aspect ratio a control-sized surface texture would be
 * stretched under correctly projected outlines.
 *
 * Only the viewer's sample count (4, what PRo3D's own screenshot clients send): with another
 * the render server compiles a scene of its own, whose single frame comes out without
 * surfaces - on develop as much as here.
 */
type Shot = { name: string; w: number; h: number; samples: number };

function shotsFor(W: number, H: number): Shot[] {
    return [
        { name: "wide", w: 2 * W, h: H, samples: 4 },
        { name: "large", w: Math.round(1.5 * W), h: Math.round(1.5 * H), samples: 4 },
    ];
}

/**
 * Fraction of pixels in the live-frame rectangle whose colour differs between the live frame
 * and the server shot `s`, sampled where the live pixel lands in it: the standard render
 * control config keeps the horizontal field of view (Frustum.withAspect), so the shot is the
 * live view scaled by w / W about the centre.
 */
function mismatch(live: PNG, shot: PNG, x0: number, y0: number, x1: number, y1: number, threshold = 60) {
    const k = shot.width / live.width;
    let changed = 0;
    let total = 0;
    for (let y = y0; y < y1; y++)
        for (let x = x0; x < x1; x++) {
            const sx = Math.round(shot.width / 2 + (x + 0.5 - live.width / 2) * k - 0.5);
            const sy = Math.round(shot.height / 2 + (y + 0.5 - live.height / 2) * k - 0.5);
            const i = (y * live.width + x) * 4;
            const j = (sy * shot.width + sx) * 4;
            const d =
                Math.abs(live.data[i] - shot.data[j]) + Math.abs(live.data[i + 1] - shot.data[j + 1]) + Math.abs(live.data[i + 2] - shot.data[j + 2]);
            if (d > threshold) changed++;
            total++;
        }
    return changed / total;
}

/**
 * The render page's client, as the render server names it: its control's element id.
 *
 * Not looked up in /rendering/stats.json: requesting it while the page renders stopped the
 * live view from updating (aardvark.media 5.7.3), on this branch and before it.
 */
async function renderClient(render: Page) {
    return render.evaluate(() => document.querySelector("img.rendercontrol")?.closest("[id]")?.id ?? "");
}

async function serverShot(context: BrowserContext, app: { url: string }, client: string, s: Shot, extra = "") {
    const res = await context.request.get(
        new URL(`/rendering/screenshot/${client}?w=${s.w}&h=${s.h}&samples=${s.samples}&fmt=png${extra}`, app.url).href
    );
    expect(res.ok(), `screenshot ${s.name}: ${res.status()}`).toBe(true);
    return Buffer.from(await res.body());
}

/** Fraction of pixels in the rectangle within `tol` of the colour. */
function colourFraction(p: PNG, rgb: [number, number, number], x0: number, y0: number, x1: number, y1: number, tol = 6) {
    let n = 0;
    let total = 0;
    for (let y = y0; y < y1; y++)
        for (let x = x0; x < x1; x++) {
            const i = (y * p.width + x) * 4;
            if (Math.abs(p.data[i] - rgb[0]) <= tol && Math.abs(p.data[i + 1] - rgb[1]) <= tol && Math.abs(p.data[i + 2] - rgb[2]) <= tol) n++;
            total++;
        }
    return n / total;
}

test("a filled polygon tints the terrain inside its outline, bulge included", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    const scene = drawingScene(artifacts, "fill");
    const { app, context, render } = await openDrawingViewer(browser, scene, "1-loaded.png", save);
    try {
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        await clickTool(render, "Draw annotation");
        await expect.poll(async () => (await tool(render, "Draw annotation")).active, { timeout: 30_000 }).toBe(true);
        await selectByOption(main, "Polygon", "Polygon");
        await selectByOption(main, "Sky", "Sky");
        await enableFillForNewAnnotations(main);
        await main.waitForTimeout(800);

        const before = await settled(render, "2-before.png", save);

        const vp = render.viewportSize() ?? { width: 1600, height: 900 };

        // a square of picks around the centre of the view, then Enter closes the polygon
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);
        const r = 120;
        for (const [x, y] of [[cx - r, cy - r], [cx + r, cy - r], [cx + r, cy + r], [cx - r, cy + r]]) {
            await awaitIdle(render, app);
            await pick(render, x, y);
        }
        await awaitIdle(render, app);
        await render.keyboard.press("Enter");
        await awaitIdle(render, app);
        await expect.poll(() => annotationCount(context, app), { timeout: 120_000, intervals: [5000] }).toBe(1);

        // leave drawing mode so no working annotation or cursor is in the frame
        await clickTool(render, "Draw annotation");
        const after = await settled(render, "3-filled.png", save);

        const a = PNG.sync.read(before);
        const b = PNG.sync.read(after);
        // well inside the outline, clear of the outline's own line width
        const inner = r - 30;
        const inside = changedFraction(a, b, cx - inner, cy - inner, cx + inner, cy + inner);
        console.log(`inside the outline: ${(inside * 100).toFixed(1)}% of pixels tinted`);
        expect(inside, "the terrain inside the outline is filled, its bulge included").toBeGreaterThan(0.9);

        // a band outside the outline, still on the body
        const outside = changedFraction(a, b, cx + r + 30, cy - 40, cx + r + 90, cy + 40);
        console.log(`outside the outline: ${(outside * 100).toFixed(1)}% of pixels changed`);
        expect(outside, "nothing outside the outline is tinted").toBeLessThan(0.05);

        // Server screenshots of other sizes and aspect ratios show the same picture: surfaces,
        // fill and outline where the live view has them. Taken last: after a server
        // screenshot at another size the live view stops showing model changes until the
        // camera moves (aardvark.media 5.7.3; on develop as much as here).
        const client = await renderClient(render);
        expect(client, "the render control's element id").not.toBe("");
        const control = await render.evaluate(() => {
            const rc = document.querySelector("img.rendercontrol")!.getBoundingClientRect();
            return { width: Math.round(rc.width), height: Math.round(rc.height) };
        });
        for (const s of shotsFor(control.width, control.height)) {
            const png = await serverShot(context, app, client, s);
            save(`4-server-${s.name}.png`, png);
            const shot = PNG.sync.read(png);
            expect([shot.width, shot.height], `screenshot ${s.name} has the requested size`).toEqual([s.w, s.h]);
            const sIn = mismatch(b, shot, cx - inner, cy - inner, cx + inner, cy + inner);
            const sOut = mismatch(b, shot, cx + r + 30, cy - 40, cx + r + 90, cy + 40);
            console.log(`server ${s.name} ${s.w}x${s.h}: ${(sIn * 100).toFixed(1)}% of the fill and ${(sOut * 100).toFixed(1)}% of the band outside differ from the live view`);
            expect(sIn, `${s.name}: the fill as in the live view`).toBeLessThan(0.1);
            expect(sOut, `${s.name}: outside the outline as in the live view`).toBeLessThan(0.1);
        }

        // a screenshot's background is the requested one wherever there is no surface - the
        // offscreen copy blends over the target's clear colour instead of replacing it
        {
            const s: Shot = { name: "background", w: control.width, h: control.height, samples: 4 };
            const plain = PNG.sync.read(await serverShot(context, app, client, s));
            const png = await serverShot(context, app, client, s, "&background=[1.0,0.0,1.0,1.0]");
            save(`6-server-background.png`, png);
            const magenta = PNG.sync.read(png);
            const sky = colourFraction(plain, [0, 0, 0], 0, 0, s.w, s.h, 2);
            const pink = colourFraction(magenta, [255, 0, 255], 0, 0, s.w, s.h);
            console.log(`server background: ${(sky * 100).toFixed(1)}% black by default, ${(pink * 100).toFixed(1)}% magenta when asked`);
            expect(sky, "a screenshot with the default background has black sky, not the viewer's #222222").toBeGreaterThan(0.01);
            expect(Math.abs(pink - sky), "the requested background shows where the default one did").toBeLessThan(0.01);
        }

    } finally {
        await context.close();
        await app.stop();
    }
});
