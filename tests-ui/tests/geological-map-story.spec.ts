import { test, expect, Page } from "@playwright/test";
import { fixture, testData } from "../src/pro3d";
import { awaitIdle, clickTool, drawingScene, openDrawingViewer, pick, selectByOption, settled, tool } from "../src/drawing";
import { diffPng } from "../src/image";
import * as fs from "fs";
import * as path from "path";

/**
 * The geological map story (#843, docs/GeologicalMap-story.md): a map made from scratch on
 * Dimorphos through the UI, one screenshot per step. Starts from an empty annotation group.
 *
 * Not a correctness test first - the screenshots are the documentation - but every step checks
 * the panel, so a broken step fails instead of producing a misleading picture.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "geological-map-story");
const showcaseAnn = path.join(testData, "cases", "geologicalMap", "dimorphos-geological-map.pro3d.ann");

test.setTimeout(30 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

function lookFrom(d: any, latDeg: number, lonDeg: number, distance: number) {
    const lat = (latDeg * Math.PI) / 180;
    const lon = (lonDeg * Math.PI) / 180;
    const dir = [Math.cos(lat) * Math.cos(lon), Math.cos(lat) * Math.sin(lon), Math.sin(lat)];
    const fwd = dir.map((c) => -c);
    const sky = [0, 0, 1];
    const dot = fwd[0] * sky[0] + fwd[1] * sky[1] + fwd[2] * sky[2];
    let up = sky.map((c, i) => c - dot * fwd[i]);
    const ul = Math.hypot(up[0], up[1], up[2]);
    up = up.map((c) => c / ul);
    const right = [fwd[1] * up[2] - fwd[2] * up[1], fwd[2] * up[0] - fwd[0] * up[2], fwd[0] * up[1] - fwd[1] * up[0]];
    const v = (a: number[]) => `[${a.join(", ")}]`;
    d.cameraView.view = [v(sky), v(dir.map((c) => c * distance)), v(fwd), v(up), v(right)];
}

/** The showcase's annotation file reduced to one empty group "Dimorphos geology" and no map. */
function emptyGroupAnnotations(target: string) {
    const a = JSON.parse(fs.readFileSync(showcaseAnn, "utf-8").replace(/^﻿/, ""));
    a.annotations.flat = [];
    a.annotations.rootGroup.leaves = [];
    for (const g of a.annotations.rootGroup.subNodes) g.leaves = [];
    delete a.geologicalMaps;
    fs.writeFileSync(target, JSON.stringify(a, null, 2));
}

async function panelText(panel: Page) {
    return panel.evaluate(() => document.body.innerText);
}

async function clickButton(page: Page, label: string) {
    await page.locator("button", { hasText: label }).first().click({ timeout: 30_000 });
}

/** Types into the legend row `row` (0-based), input `column` (0 code, 1 name). */
async function setUnitText(panel: Page, row: number, column: number, value: string) {
    // legend rows are the ones with a remove icon - the map table above has inputs too
    const input = panel.locator("tr:has(i.trash)").nth(row).locator("input:visible").nth(column);
    await input.fill(value, { timeout: 30_000 });
    await input.press("Enter", { timeout: 30_000 });
    await panel.waitForTimeout(400);
}

async function activeUnitIcon(panel: Page, code: string) {
    await panel.evaluate((c) => {
        const row = Array.from(document.querySelectorAll("tr")).find((tr) =>
            Array.from(tr.querySelectorAll("input")).some((i) => (i as HTMLInputElement).value === c));
        (row?.querySelector("td:nth-child(4) i") as HTMLElement | null)?.click();
    }, code);
    await panel.waitForTimeout(400);
}

/** A tool gesture with Ctrl held (the classic scheme), optionally dragging to `to`. */
async function gesture(render: Page, at: { x: number; y: number }, to = at) {
    await render.bringToFront();
    await render.mouse.move(at.x, at.y);
    await render.waitForTimeout(300);
    await render.keyboard.down("Control");
    await render.waitForTimeout(150);
    await render.mouse.down();
    for (let i = 1; i <= 8; i++) {
        await render.mouse.move(at.x + ((to.x - at.x) * i) / 8, at.y + ((to.y - at.y) * i) / 8);
        await render.waitForTimeout(60);
    }
    await render.mouse.up();
    await render.waitForTimeout(150);
    await render.keyboard.up("Control");
}

/** Hover with the tool armed (Ctrl), long enough for the cursor's surface pick. */
async function hover(render: Page, at: { x: number; y: number }) {
    await render.bringToFront();
    await render.mouse.move(at.x, at.y);
    await render.keyboard.down("Control");
    for (let i = 0; i < 8; i++) {
        await render.mouse.move(at.x + (i % 2), at.y);
        await render.waitForTimeout(400);
    }
}

test("story: a geological map of Dimorphos from scratch", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate) || !fs.existsSync(showcaseAnn),
        "set PRO3D_TEST_DATA and run --make-geomap-showcase");

    const scene = drawingScene(artifacts, "story", (d) => lookFrom(d, 35, 40, 260));
    emptyGroupAnnotations(scene + ".ann");
    const { app, context, render } = await openDrawingViewer(browser, scene, "01-dimorphos.png", save);
    const vp = render.viewportSize() ?? { width: 1600, height: 900 };
    const at = (fx: number, fy: number) => ({ x: fx * vp.width, y: fy * vp.height });
    // settled() rejects yellow-green in the middle of the frame as the loading splash - but a
    // freshly drawn, selected annotation is exactly that colour. The view is up by now: wait
    // for two matching frames only
    const shot = async (name: string) => {
        await render.waitForTimeout(1200);
        let prev = await render.screenshot();
        for (let i = 0; i < 30; i++) {
            await render.waitForTimeout(700);
            const cur = await render.screenshot();
            const same = diffPng(prev, cur).changedFraction < 0.001;
            prev = cur;
            if (same) break;
        }
        save(name, prev);
        return prev;
    };

    try {
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        // 1. the group the map is made of: active
        const annotations = await context.newPage();
        await annotations.setViewportSize({ width: 520, height: 700 });
        await annotations.goto(app.url + "?page=annotations");
        await annotations.waitForLoadState("domcontentloaded");
        await expect.poll(() => annotations.evaluate(() => document.body.innerText), { timeout: 60_000 }).toContain("Dimorphos geology");
        await annotations.evaluate(() => {
            const label = Array.from(document.querySelectorAll("div")).find((d) => (d.firstChild?.textContent ?? "").trim() === "Dimorphos geology");
            (label?.querySelector('i[title="Set active"]') as HTMLElement | null)?.dispatchEvent(new MouseEvent("click", { bubbles: true }));
        });
        await annotations.waitForTimeout(800);

        // 2. a new map over it
        const panel = await context.newPage();
        await panel.setViewportSize({ width: 520, height: 900 });
        await panel.goto(app.url + "?page=geomaps");
        await panel.waitForLoadState("domcontentloaded");
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("New map from group 'Dimorphos geology'");
        await clickButton(panel, "New map from group");
        await expect.poll(() => panelText(panel), { timeout: 30_000 }).toContain("0 contacts");
        await panel.locator("input").first().fill("Dimorphos units", { timeout: 30_000 });
        await panel.locator("input").first().press("Enter", { timeout: 30_000 });
        save("02-new-map.png", await panel.screenshot());

        // 3. a first contact: the outline of a lowland, closed
        await clickTool(render, "Draw annotation");
        await expect.poll(async () => (await tool(render, "Draw annotation")).active, { timeout: 30_000 }).toBe(true);
        await selectByOption(main, "Polygon", "Polygon");
        await main.waitForTimeout(800);
        const loop = [at(0.33, 0.36), at(0.50, 0.27), at(0.66, 0.36), at(0.66, 0.60), at(0.50, 0.70), at(0.33, 0.60)];
        for (const p of loop) {
            await pick(render, p.x, p.y);
            await awaitIdle(render, app);
        }
        await render.keyboard.press("Enter");
        await awaitIdle(render, app);
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("2 units");
        await shot("03-first-contact.png");

        // 4. the legend
        await clickButton(panel, "New unit");
        await clickButton(panel, "New unit");
        await setUnitText(panel, 0, 0, "Lo");
        await setUnitText(panel, 0, 1, "Smooth lowland");
        await setUnitText(panel, 1, 0, "Hi");
        await setUnitText(panel, 1, 1, "Rough highland");
        save("04-legend.png", await panel.screenshot());

        // 5. assigning: one click per unit
        const brush = "Assign unit - click or drag across units of the active geological map";
        await clickTool(render, brush);
        await expect.poll(async () => (await tool(render, brush)).active, { timeout: 30_000 }).toBe(true);
        await activeUnitIcon(panel, "Lo");
        await gesture(render, at(0.50, 0.48));
        await activeUnitIcon(panel, "Hi");
        await gesture(render, at(0.75, 0.50));
        await expect.poll(() => panelText(panel), { timeout: 30_000 }).not.toContain("without a label");
        await shot("05-assigned.png");

        // 6. refining: a contact across the lowland, ending on its outline - snapped
        await clickTool(render, "Draw annotation");
        await selectByOption(main, "Polyline", "Polyline");
        await main.waitForTimeout(800);
        // the middles of the lowland outline's upper-left and lower-left edges, a few pixels outside
        const start = at(0.415, 0.315 - 0.007);
        const end = at(0.415, 0.65 + 0.007);
        await hover(render, start);
        save("06-hover-snap.png", await render.screenshot());
        await render.keyboard.up("Control");
        await pick(render, start.x, start.y);
        await awaitIdle(render, app);
        const mid = at(0.44, 0.48);
        await pick(render, mid.x, mid.y);
        await awaitIdle(render, app);
        await hover(render, end);
        save("07-preview.png", await render.screenshot());
        await render.keyboard.up("Control");
        await pick(render, end.x, end.y);
        await awaitIdle(render, app);
        await render.keyboard.press("Enter");
        await awaitIdle(render, app);
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("3 units");
        expect(await panelText(panel)).toContain("1 without a label");
        await shot("08-split.png");

        // 7. a third unit for the new part
        await clickButton(panel, "New unit");
        await setUnitText(panel, 2, 0, "Ej");
        await setUnitText(panel, 2, 1, "Ejecta blanket");
        await clickTool(render, brush);
        await activeUnitIcon(panel, "Ej");
        await gesture(render, at(0.38, 0.48));
        await expect.poll(() => panelText(panel), { timeout: 30_000 }).not.toContain("without a label");
        await shot("09-third-unit.png");

        // 8. a contact that ends in nothing: an open end, listed as a problem
        await clickTool(render, "Draw annotation");
        await main.waitForTimeout(500);
        for (const p of [at(0.74, 0.30), at(0.77, 0.42)]) {
            await pick(render, p.x, p.y);
            await awaitIdle(render, app);
        }
        await render.keyboard.press("Enter");
        await awaitIdle(render, app);
        await expect.poll(() => panelText(panel), { timeout: 60_000 }).toContain("2 open contact ends");
        await shot("10-open-end.png");
        save("11-problems.png", await panel.screenshot());
    } finally {
        await context.close();
        await app.stop();
    }
});
