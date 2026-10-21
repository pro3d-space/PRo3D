import { test, expect, BrowserContext, Frame, Page } from "@playwright/test";
import { fixture, launchPro3d, Pro3d, surfaceShadersReady, testData } from "../src/pro3d";
import { awaitIdle, clickTool, drawingScene, openDrawingViewer, selectByOption, settled, tool } from "../src/drawing";
import { diffPng } from "../src/image";
import { Video } from "../src/video";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

/**
 * The geological map story (#843, docs/GeologicalMap-story.md): a map made from scratch on
 * Dimorphos through the UI, one screenshot per step. Starts from an empty annotation group.
 *
 * Not a correctness test first - the screenshots are the documentation - but every step checks
 * the panel, so a broken step fails instead of producing a misleading picture.
 *
 * PRO3D_VIDEO=1 runs the same steps in the full UI (render view and panels docked in one page)
 * and records a captioned video instead of the pictures (docs/dev/StoryVideo.md).
 */

const artifacts = path.join(__dirname, "..", "artifacts", "geological-map-story");
const showcaseAnn = path.join(testData, "cases", "geologicalMap", "dimorphos-geological-map.pro3d.ann");
const videoMode = process.env.PRO3D_VIDEO === "1";

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

type Doc = Page | Frame;
type Box = { x: number; y: number; width: number; height: number };
type Point = { x: number; y: number };

/**
 * Where the story's steps act. Pictures: separate pages for the render view, the panels and the
 * toolbar. Video: one page with the panels docked as iframes; `input` (mouse, keyboard) is that
 * page and `view` the render view's place in it.
 */
interface Ui {
    main: Page; // the toolbar (geometry dropdown)
    render: Doc; // the render view: tool strip
    panel: Doc; // Geological Maps
    annotations: Doc;
    input: Page;
    view: Box;
    video?: Video;
}

/** A point of the render view, in fractions of the 1600 x 900 view the story was laid out in.
 *  The field of view is horizontal, so another view size scales about the centre by width. */
function at(ui: Ui, fx: number, fy: number): Point {
    const s = ui.view.width / 1600;
    return {
        x: ui.view.x + ui.view.width / 2 + (fx - 0.5) * 1600 * s,
        y: ui.view.y + ui.view.height / 2 + (fy - 0.5) * 900 * s,
    };
}

async function move(ui: Ui, p: Point) {
    if (ui.video) await ui.video.move(p.x, p.y);
    else await ui.input.mouse.move(p.x, p.y);
}

/** Video only: the pointer goes to `box` and shows a click there. */
async function pointTo(ui: Ui, box: Box | null) {
    if (!ui.video || !box) return;
    await ui.video.move(box.x + box.width / 2, box.y + box.height / 2);
    await ui.video.ripple();
}

async function ctrl(ui: Ui, down: boolean) {
    if (down) await ui.input.keyboard.down("Control");
    else await ui.input.keyboard.up("Control");
    await ui.video?.keys(down ? "Ctrl" : null, ui.view);
}

async function press(ui: Ui, key: string) {
    if (ui.video) {
        await ui.video.keys(key, ui.view);
        await ui.input.waitForTimeout(300);
    }
    await ui.input.keyboard.press(key);
    if (ui.video) {
        await ui.input.waitForTimeout(250);
        await ui.video.keys(null, ui.view);
    }
}

async function say(ui: Ui, text: string, hold = 2.5) {
    await ui.video?.caption(text, ui.view, hold);
}

/** A wait, cut from the video but for its first moment. */
async function until<T>(ui: Ui, wait: () => Promise<T>): Promise<T> {
    return ui.video ? ui.video.cut(wait) : wait();
}

async function idle(ui: Ui, app: Pro3d) {
    await until(ui, () => awaitIdle(ui.input, app));
}

async function panelText(panel: Doc) {
    return panel.evaluate(() => document.body.innerText);
}

async function panelHas(ui: Ui, text: string, timeout: number, not = false) {
    await until(ui, async () => {
        const p = expect.poll(() => panelText(ui.panel), { timeout });
        await (not ? p.not.toContain(text) : p.toContain(text));
    });
}

/** A tool-strip button, by its tooltip. */
async function useTool(ui: Ui, title: string) {
    await pointTo(ui, await ui.render.locator(`.pro3d-tool[title="${title}"]`).boundingBox());
    await clickTool(ui.render as Page, title);
}

/** Selects a geometry in the toolbar and waits until the viewer shows it selected. */
async function geometry(ui: Ui, name: string) {
    const index = () => ui.main.evaluate((n) => Array.from(document.querySelectorAll("select")).findIndex((s) =>
        Array.from(s.options).some((o) => (o.textContent ?? "").trim() === n)), name);
    await expect.poll(index, { timeout: 60_000 }).toBeGreaterThanOrEqual(0);
    await pointTo(ui, await ui.main.locator("select").nth(await index()).boundingBox());
    await selectByOption(ui.main, name, name);
    await expect.poll(() => ui.main.evaluate((n) => Array.from(document.querySelectorAll("select")).some((s) =>
        Array.from(s.options).some((o) => (o.textContent ?? "").trim() === n) &&
        (s.options[s.selectedIndex]?.textContent ?? "").trim() === n), name), { timeout: 30_000 }).toBe(true);
    await ui.main.waitForTimeout(800);
}

async function clickButton(ui: Ui, doc: Doc, label: string) {
    const button = doc.locator("button", { hasText: label }).first();
    await pointTo(ui, await button.boundingBox());
    await button.click({ timeout: 30_000 });
}

/** Types into the legend row `row` (0-based), input `column` (0 code, 1 name). */
async function setUnitText(ui: Ui, row: number, column: number, value: string) {
    // legend rows are the ones with a remove icon - the map table above has inputs too
    const input = ui.panel.locator("tr:has(i.trash)").nth(row).locator("input:visible").nth(column);
    await pointTo(ui, await input.boundingBox());
    if (ui.video) {
        await input.fill("", { timeout: 30_000 });
        await input.pressSequentially(value, { delay: 50 });
    } else await input.fill(value, { timeout: 30_000 });
    await input.press("Enter", { timeout: 30_000 });
    await ui.input.waitForTimeout(400);
}

/** Clicks the element `find` returns (a dispatched click: icons the app listens on directly). */
async function clickFound(ui: Ui, doc: Doc, find: (arg: string) => Element | null | undefined, arg: string) {
    const el = (await doc.evaluateHandle(find, arg)).asElement();
    await pointTo(ui, el ? await el.boundingBox() : null);
    await el?.evaluate((e) => e.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await ui.input.waitForTimeout(400);
}

async function activeUnitIcon(ui: Ui, code: string) {
    await clickFound(ui, ui.panel, (c) =>
        Array.from(document.querySelectorAll("tr")).find((tr) =>
            Array.from(tr.querySelectorAll("input")).some((i) => (i as HTMLInputElement).value === c))
            ?.querySelector("td:nth-child(4) i"), code);
}

/** One ctrl+click on the surface (see drawing.ts `pick`). */
async function pick(ui: Ui, p: Point) {
    await ui.input.bringToFront();
    await move(ui, p);
    // the render view focuses itself on mouseenter; a video's glide already gave it the time
    await ui.input.waitForTimeout(ui.video ? 150 : 400);
    await ctrl(ui, true);
    await ui.input.waitForTimeout(200);
    await ui.input.mouse.click(p.x, p.y);
    await ui.video?.ripple();
    await ui.input.waitForTimeout(200);
    await ctrl(ui, false);
}

/** A tool gesture with Ctrl held (the classic scheme), optionally dragging to `to`. */
async function gesture(ui: Ui, from: Point, to = from) {
    await ui.input.bringToFront();
    await move(ui, from);
    await ui.input.waitForTimeout(300);
    await ctrl(ui, true);
    await ui.input.waitForTimeout(150);
    await ui.input.mouse.down();
    await ui.video?.ripple();
    for (let i = 1; i <= 8; i++) {
        await ui.input.mouse.move(from.x + ((to.x - from.x) * i) / 8, from.y + ((to.y - from.y) * i) / 8);
        await ui.input.waitForTimeout(60);
    }
    await ui.input.mouse.up();
    await ui.input.waitForTimeout(150);
    await ctrl(ui, false);
}

/** Hover with the tool armed (Ctrl), long enough for the cursor's surface pick. */
async function hover(ui: Ui, p: Point) {
    await ui.input.bringToFront();
    await move(ui, p);
    await ctrl(ui, true);
    for (let i = 0; i < (ui.video ? 5 : 8); i++) {
        await ui.input.mouse.move(p.x + (i % 2), p.y);
        await ui.input.waitForTimeout(400);
    }
}

/** The render view once nothing changes any more: saved (pictures) or held (video).
 *  settled() rejects yellow-green in the middle of the frame as the loading splash - but a
 *  freshly drawn, selected annotation is exactly that colour, so: two matching frames only. */
async function shot(ui: Ui, name: string) {
    const grab = () => (ui.video ? ui.input.screenshot({ clip: ui.view }) : (ui.render as Page).screenshot());
    const frame = await until(ui, async () => {
        await ui.input.waitForTimeout(1200);
        let prev = await grab();
        for (let i = 0; i < 30; i++) {
            await ui.input.waitForTimeout(700);
            const cur = await grab();
            const same = diffPng(prev, cur).changedFraction < 0.001;
            prev = cur;
            if (same) break;
        }
        return prev;
    });
    if (ui.video) await ui.input.waitForTimeout(1500);
    else save(name, frame);
}

/** The render view as it is now (pictures only). */
async function snap(ui: Ui, name: string) {
    if (!ui.video) save(name, await (ui.render as Page).screenshot());
}

async function panelShot(ui: Ui, name: string) {
    if (ui.video) await ui.input.waitForTimeout(800);
    else save(name, await (ui.panel as Page).screenshot());
}

/** Render view left, Geological Maps top right, Annotations below (docs/WindowLayouts.md). */
function storyLayout(dir: string) {
    const component = (title: string, componentType: string) => ({
        type: "component", title, componentType, isClosable: componentType !== "render", header: { show: "top" },
        size: 1, componentState: { keepAlive: true }, sizeUnit: "fr",
    });
    const stack = (size: number, c: object) => ({ type: "stack", header: { show: "top" }, size, sizeUnit: "%", content: [c] });
    const root = {
        type: "row", size: 1, sizeUnit: "fr", content: [
            stack(66, component("Main View", "render")),
            { type: "column", size: 34, sizeUnit: "%", content: [
                stack(62, component("Geological Maps", "geomaps")),
                stack(38, component("Annotations", "annotations")),
            ] },
        ],
    };
    const file = { format: "pro3d-layout", version: 1, name: "Geological map story", layout: { root, openPopouts: [] } };
    fs.writeFileSync(path.join(dir, "current.json"), JSON.stringify(file, null, 2));
}

async function frameOf(main: Page, page: string): Promise<Frame> {
    let frame: Frame | undefined;
    await expect.poll(() => (frame = main.frames().find((f) => f.url().includes(`page=${page}`))) !== undefined,
        { timeout: 120_000 }).toBe(true);
    return frame!;
}

/** The full UI in one 1080p page, recorded. */
async function openVideo(browser: any, scene: string): Promise<{ app: Pro3d; context: BrowserContext; ui: Ui }> {
    const layouts = fs.mkdtempSync(path.join(os.tmpdir(), "pro3d-story-layouts-"));
    storyLayout(layouts);
    const app = await launchPro3d(scene, { PRO3D_LAYOUT_DIR: layouts });
    const context: BrowserContext = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    const main = await context.newPage();
    await main.goto(app.url);
    await main.waitForLoadState("domcontentloaded");
    const render = await frameOf(main, "render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await surfaceShadersReady(render as unknown as Page);
    const view = (await render.locator("img.rendercontrol").boundingBox())!;
    const viewOnly = { screenshot: () => main.screenshot({ clip: view }), waitForTimeout: (ms: number) => main.waitForTimeout(ms) };
    await settled(viewOnly as unknown as Page, "01-dimorphos.png", () => {});
    const ui: Ui = {
        main, render, view, input: main,
        panel: await frameOf(main, "geomaps"),
        annotations: await frameOf(main, "annotations"),
        video: new Video(main, path.join(artifacts, "video-frames")),
    };
    return { app, context, ui };
}

/** Separate pages, as the pictures were taken. */
async function openPictures(browser: any, scene: string): Promise<{ app: Pro3d; context: BrowserContext; ui: Ui }> {
    const { app, context, render } = await openDrawingViewer(browser, scene, "01-dimorphos.png", save);
    const vp = render.viewportSize() ?? { width: 1600, height: 900 };
    const open = async (query: string, size?: { width: number; height: number }) => {
        const page = await context.newPage();
        if (size) await page.setViewportSize(size);
        await page.goto(app.url + query);
        await page.waitForLoadState("domcontentloaded");
        return page;
    };
    const main = await open("");
    const annotations = await open("?page=annotations", { width: 520, height: 700 });
    const panel = await open("?page=geomaps", { width: 520, height: 900 });
    return { app, context, ui: { main, render, panel, annotations, input: render, view: { x: 0, y: 0, ...vp } } };
}

test("story: a geological map of Dimorphos from scratch", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate) || !fs.existsSync(showcaseAnn),
        "set PRO3D_TEST_DATA and run --make-geomap-showcase");

    const scene = drawingScene(artifacts, videoMode ? "story-video" : "story", (d) => lookFrom(d, 35, 40, 260));
    emptyGroupAnnotations(scene + ".ann");
    const { app, context, ui } = await (videoMode ? openVideo : openPictures)(browser, scene);
    const p = (fx: number, fy: number) => at(ui, fx, fy);

    try {
        await expect.poll(() => panelText(ui.annotations), { timeout: 60_000 }).toContain("Dimorphos geology");
        // experimental: the panel opens behind a notice (not part of the story)
        await ui.panel.locator("button", { hasText: "Open anyway" }).click({ timeout: 120_000 });
        await expect.poll(() => panelText(ui.panel), { timeout: 60_000 }).toContain("New map from group");
        if (ui.video) {
            await ui.video.record();
            ui.video.begin();
        }
        await say(ui, "A geological map of Dimorphos, step by step. A made-up example: units A–E are invented to show the workflow.", 5);

        // 1. the group the map is made of: active
        await say(ui, "Contacts and labels go into one annotation group - here 'Dimorphos geology', made active.", 1.5);
        await clickFound(ui, ui.annotations, (name) =>
            Array.from(document.querySelectorAll("div")).find((d) => (d.firstChild?.textContent ?? "").trim() === name)
                ?.querySelector('i[title="Set active"]'), "Dimorphos geology");
        await ui.input.waitForTimeout(800);

        // 2. a new map over it
        await panelHas(ui, "New map from group 'Dimorphos geology'", 60_000);
        await say(ui, "Geological Maps panel: New map from group. What is drawn now belongs to the map.", 1.5);
        await clickButton(ui, ui.panel, "New map from group");
        await panelHas(ui, "0 contacts", 30_000);
        const mapName = ui.panel.locator("input").first();
        await pointTo(ui, await mapName.boundingBox());
        await mapName.fill("Dimorphos units", { timeout: 30_000 });
        await mapName.press("Enter", { timeout: 30_000 });
        await panelShot(ui, "02-new-map.png");

        // 3. a first contact: the outline of a lowland, closed
        await say(ui, "Draw annotation, Polygon: Ctrl+click the points of a contact, Enter to close it.", 1.5);
        await useTool(ui, "Draw annotation");
        await expect.poll(async () => (await tool(ui.render as Page, "Draw annotation")).active, { timeout: 30_000 }).toBe(true);
        await geometry(ui, "Polygon");
        const loop = [p(0.33, 0.36), p(0.50, 0.27), p(0.66, 0.36), p(0.66, 0.60), p(0.50, 0.70), p(0.33, 0.60)];
        for (const q of loop) {
            await pick(ui, q);
            await idle(ui, app);
        }
        await press(ui, "Enter");
        await idle(ui, app);
        await panelHas(ui, "2 units", 60_000);
        await say(ui, "A closed contact divides the body into two units, inside and outside. Hatched: no unit assigned yet.", 4);
        await shot(ui, "03-first-contact.png");

        // 4. the legend
        await say(ui, "The legend: New unit, then a code and a name for each.", 1.5);
        await clickButton(ui, ui.panel, "New unit");
        await clickButton(ui, ui.panel, "New unit");
        await setUnitText(ui, 0, 0, "A");
        await setUnitText(ui, 0, 1, "Unit A");
        await setUnitText(ui, 1, 0, "B");
        await setUnitText(ui, 1, 1, "Unit B");
        await panelShot(ui, "04-legend.png");

        // 5. assigning: one click per unit
        await say(ui, "Paint brush: pick a unit in the legend, click into an area. One click per area.", 1.5);
        const brush = "Assign unit - click or drag across units of the active geological map";
        await useTool(ui, brush);
        await expect.poll(async () => (await tool(ui.render as Page, brush)).active, { timeout: 30_000 }).toBe(true);
        await activeUnitIcon(ui, "A");
        await gesture(ui, p(0.50, 0.48));
        await activeUnitIcon(ui, "B");
        await gesture(ui, p(0.75, 0.50));
        await panelHas(ui, "without a label", 30_000, true);
        await shot(ui, "05-assigned.png");

        // 6. refining: a contact across the lowland, ending on its outline - snapped
        await say(ui, "To divide A, draw a contact across it.", 1.5);
        await useTool(ui, "Draw annotation");
        await geometry(ui, "Polyline");
        // the middles of the lowland outline's upper-left and lower-left edges, a few pixels outside
        const start = p(0.415, 0.315 - 0.007);
        const end = p(0.415, 0.65 + 0.007);
        await say(ui, "Near an existing contact, a green marker shows where the click will land: exactly on it.", 0.5);
        await hover(ui, start);
        await snap(ui, "06-hover-snap.png");
        await ctrl(ui, false);
        await pick(ui, start);
        await idle(ui, app);
        await pick(ui, p(0.44, 0.48));
        await idle(ui, app);
        await say(ui, "While drawing: the unit being cut lights up, attached ends turn green.", 0.5);
        await hover(ui, end);
        await snap(ui, "07-preview.png");
        await ctrl(ui, false);
        await pick(ui, end);
        await idle(ui, app);
        await press(ui, "Enter");
        await idle(ui, app);
        await panelHas(ui, "3 units", 60_000);
        expect(await panelText(ui.panel)).toContain("1 without a label");
        await say(ui, "Enter: A is split, the new part is hatched.", 2);
        await shot(ui, "08-split.png");

        // 7. a third unit for the new part
        await say(ui, "New unit C, one click.", 1);
        await clickButton(ui, ui.panel, "New unit");
        await setUnitText(ui, 2, 0, "C");
        await setUnitText(ui, 2, 1, "Unit C");
        await useTool(ui, brush);
        await activeUnitIcon(ui, "C");
        await gesture(ui, p(0.38, 0.48));
        await panelHas(ui, "without a label", 30_000, true);
        await shot(ui, "09-third-unit.png");

        // 8. a unit inside a unit: an ellipse is a closed contact
        await say(ui, "An ellipse is a closed contact too: three clicks, long axis then width.", 1.5);
        await useTool(ui, "Draw annotation");
        await geometry(ui, "AxisEllipse");
        for (const q of [p(0.515, 0.48), p(0.615, 0.48), p(0.565, 0.545)]) {
            await pick(ui, q);
            await idle(ui, app);
        }
        // an ellipse finishes itself on the third point
        await panelHas(ui, "4 units", 120_000);
        await say(ui, "A new unit inside A; A becomes a ring around it.", 2.5);
        await shot(ui, "10-crater.png");
        await say(ui, "Unit D, one click.", 1);
        await clickButton(ui, ui.panel, "New unit");
        await setUnitText(ui, 3, 0, "D");
        await setUnitText(ui, 3, 1, "Unit D");
        await useTool(ui, brush);
        await activeUnitIcon(ui, "D");
        await gesture(ui, p(0.565, 0.48));
        await panelHas(ui, "without a label", 30_000, true);
        await shot(ui, "11-crater-assigned.png");

        // 9. connecting the ellipse to A's outline: the first line ties it on, the second one
        //    cuts the ring around it in two
        await say(ui, "A line from A's outline to the ellipse only ties the two together: no new unit, no open end.", 1.5);
        await useTool(ui, "Draw annotation");
        await geometry(ui, "Polyline");
        // east edge of the outline (vertical, x 0.66) to the ellipse's east end
        for (const q of [p(0.6625, 0.48), p(0.6175, 0.48)]) {
            await pick(ui, q);
            await idle(ui, app);
        }
        await press(ui, "Enter");
        await idle(ui, app);
        await panelHas(ui, "4 contacts", 60_000);
        expect(await panelText(ui.panel), "tied on: no new unit, no open end").toContain("4 units");
        if (ui.video) await ui.input.waitForTimeout(1500);
        // the upper edge of the outline (0.50,0.27)-(0.66,0.36) to the ellipse's top
        await say(ui, "A second line closes off part of the ring...", 0.5);
        const top = p(0.58, 0.315 - 0.007);
        const rim = p(0.565, 0.415);
        await hover(ui, rim);
        await ctrl(ui, false);
        await pick(ui, top);
        await idle(ui, app);
        await hover(ui, rim);
        await snap(ui, "12-connect-preview.png");
        await ctrl(ui, false);
        await pick(ui, rim);
        await idle(ui, app);
        await press(ui, "Enter");
        await idle(ui, app);
        await panelHas(ui, "5 units", 60_000);
        await say(ui, "...a new unit, hatched.", 1.5);
        await shot(ui, "13-ring-cut.png");
        await say(ui, "Unit E.", 1);
        await clickButton(ui, ui.panel, "New unit");
        await setUnitText(ui, 4, 0, "E");
        await setUnitText(ui, 4, 1, "Unit E");
        await useTool(ui, brush);
        await activeUnitIcon(ui, "E");
        await gesture(ui, p(0.63, 0.40));
        await panelHas(ui, "without a label", 30_000, true);
        await shot(ui, "14-terrace.png");

        // 10. a contact that ends in nothing: an open end, listed as a problem
        await say(ui, "Checking: a contact that reaches no other contact separates nothing...", 1.5);
        await useTool(ui, "Draw annotation");
        await ui.main.waitForTimeout(500);
        for (const q of [p(0.74, 0.30), p(0.77, 0.42)]) {
            await pick(ui, q);
            await idle(ui, app);
        }
        await press(ui, "Enter");
        await idle(ui, app);
        await panelHas(ui, "2 open contact ends", 60_000);
        await say(ui, "...its ends are marked red and listed in the panel. Click an entry to fly there, N for the next one.", 2);
        await shot(ui, "15-open-end.png");
        await pointTo(ui, await ui.panel.getByText("open contact end").first().boundingBox().catch(() => null));
        await panelShot(ui, "16-problems.png");
        await say(ui, "Units are never drawn directly: each boundary is drawn once, as a contact - changing it updates the units on both sides.", 6);

        if (ui.video) await ui.video.encode(path.join(artifacts, "geological-map-story.mp4"));
    } finally {
        await context.close();
        await app.stop();
    }
});
