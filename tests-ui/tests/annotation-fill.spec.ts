import { test, expect, Page } from "@playwright/test";
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

        // a square of picks around the centre of the view, then Enter closes the polygon
        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
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
    } finally {
        await context.close();
        await app.stop();
    }
});
