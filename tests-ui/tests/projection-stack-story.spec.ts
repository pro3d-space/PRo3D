import { test, expect, BrowserContext, ElementHandle, Frame, Page } from "@playwright/test";
import { config, derivedScene, fixture, launchPro3d, Pro3d, surfaceShadersReady } from "../src/pro3d";
import { diffPng, litFraction, streamLive } from "../src/image";
import { Video } from "../src/video";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

/**
 * The projection stack story (docs/MultiImageProjection.md): four AFC-1 frames of Dimorphos in
 * the projection stack, reordered by drag & drop. Shows - and checks - the feedback while doing
 * so: the hovered or dragged image stays at full strength while the others fade, and while
 * dragging, the 3D view already shows the order the drop would give.
 *
 * Drives a real mouse drag (the browser's native HTML5 drag & drop), not dispatched events.
 * Writes the reorder upload timings ("[ProjectedStack] uploaded ...") of the drag to
 * artifacts/projection-stack-story/upload-timings.txt.
 *
 * PRO3D_VIDEO=1 runs the same steps in the full UI (render view and GIS panel docked in one
 * page) and records a captioned video instead of the pictures (docs/dev/StoryVideo.md).
 */

const artifacts = path.join(__dirname, "..", "artifacts", "projection-stack-story");
const videoMode = process.env.PRO3D_VIDEO === "1";

// Default data: the test-data Dimorphos scene and its AFC-1 frames. PRO3D_STACK_STORY_SCENE and
// PRO3D_STACK_STORY_IMAGES run the story on another scene and image folder (e.g. the Mars
// swing-by with HyperScout frames). That scene is copied without its .layout sidecar, so no
// layout question interrupts, and the checks are soft: a video is not thrown away because a
// threshold tuned on the test data does not fit other data.
const customScene = process.env.PRO3D_STACK_STORY_SCENE;
const imageDir = process.env.PRO3D_STACK_STORY_IMAGES ?? config.imageDir;
const check = customScene ? expect.soft : expect;

function storyScene(): string {
    if (!customScene) return derivedScene(videoMode ? "stack-story-video" : "stack-story", () => {}, artifacts);
    fs.mkdirSync(artifacts, { recursive: true });
    const out = path.join(artifacts, "stack-story-custom.pro3d");
    const d = JSON.parse(fs.readFileSync(customScene, "utf-8").replace(/^﻿/, ""));
    d.scenePath = out;
    fs.writeFileSync(out, JSON.stringify(d, null, 2));
    if (fs.existsSync(customScene + ".ann")) fs.copyFileSync(customScene + ".ann", out + ".ann");
    return out;
}

test.setTimeout(30 * 60_000);

type Doc = Page | Frame;
type Box = { x: number; y: number; width: number; height: number };

/** Where the steps act. Pictures: a render page and a GIS page. Video: one page with both docked
 *  as iframes; `input` (mouse) is that page and `view` the render view's place in it. */
interface Ui {
    render: Doc;
    gis: Doc;
    input: Page;
    view: Box;
    video?: Video;
}

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

async function say(ui: Ui, text: string, hold = 2.5) {
    await ui.video?.caption(text, ui.view, hold);
}

/** A wait, cut from the video but for its first moment. */
async function until<T>(ui: Ui, wait: () => Promise<T>): Promise<T> {
    return ui.video ? ui.video.cut(wait) : wait();
}

async function viewShot(ui: Ui): Promise<Buffer> {
    return ui.video ? ui.input.screenshot({ clip: ui.view }) : (ui.render as Page).screenshot();
}

/** The render view once two consecutive frames agree (the stack re-uploads, LoD streams). */
async function settledView(ui: Ui): Promise<Buffer> {
    return until(ui, async () => {
        let prev = await viewShot(ui);
        for (let i = 0; i < 40; i++) {
            await ui.input.waitForTimeout(400);
            const cur = await viewShot(ui);
            if (diffPng(prev, cur).changedFraction < 0.0005) return cur;
            prev = cur;
        }
        return prev;
    });
}

/** A result: the picture is saved (pictures) or held (video). Returns the settled view. */
async function shot(ui: Ui, name: string): Promise<Buffer> {
    const png = await settledView(ui);
    if (ui.video) await ui.input.waitForTimeout(1500);
    else save(name, png);
    return png;
}

async function move(ui: Ui, x: number, y: number) {
    if (ui.video) await ui.video.move(x, y);
    else await ui.input.mouse.move(x, y, { steps: 8 });
}

function centre(b: Box) {
    return { x: b.x + b.width / 2, y: b.y + b.height / 2 };
}

/** The file names in the stack panel, top first (the rows carry a remove icon). */
async function stackEntries(gis: Doc): Promise<string[]> {
    return gis.evaluate(() =>
        Array.from(document.querySelectorAll("i.remove.icon"))
            .map((i) => (i.parentElement?.textContent ?? "").trim())
            .filter((t) => t.length > 0)
    );
}

/** The stack panel row of `name`. */
async function stackRow(gis: Doc, name: string): Promise<ElementHandle<Element>> {
    const el = (await gis.evaluateHandle((n) =>
        Array.from(document.querySelectorAll('div[draggable="true"]')).find((d) => (d.textContent ?? "").trim() === n)
    , name)).asElement();
    if (!el) throw new Error(`stack row ${name} not found`);
    return el;
}

/** The add-to-stack icon in the library row of `name` (see stack-ui.spec.ts clickRowIcon). */
async function plusIcon(gis: Doc, name: string): Promise<ElementHandle<Element>> {
    const el = (await gis.evaluateHandle((n) => {
        const matches = Array.from(document.querySelectorAll("*")).filter((e) => (e.textContent ?? "").trim() === n);
        const deepest = matches.filter((e) => !Array.from(e.children).some((c) => matches.includes(c)));
        let el: Element | null = deepest[0] ?? null;
        while (el) {
            const box = el.nextElementSibling?.querySelector("i.plus.icon");
            if (box) return box;
            el = el.parentElement;
        }
        return null;
    }, name)).asElement();
    if (!el) throw new Error(`no add icon for ${name}`);
    return el;
}

/** Clicks an icon the app listens on directly (dispatched; the incremental list re-renders too
 *  often for Playwright's actionability loop). The video pointer goes there first. */
async function clickIcon(ui: Ui, el: ElementHandle<Element>) {
    const box = await el.boundingBox();
    if (ui.video && box) {
        await ui.video.move(centre(box).x, centre(box).y);
        await ui.video.ripple();
    }
    await el.evaluate((e) => e.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await ui.input.waitForTimeout(300);
}

/** Render view left, GIS View right (docs/WindowLayouts.md). */
function storyLayout(dir: string) {
    const component = (title: string, componentType: string) => ({
        type: "component", title, componentType, isClosable: componentType !== "render", header: { show: "top" },
        size: 1, componentState: { keepAlive: true }, sizeUnit: "fr",
    });
    const stack = (size: number, c: object) => ({ type: "stack", header: { show: "top" }, size, sizeUnit: "%", content: [c] });
    const root = {
        type: "row", size: 1, sizeUnit: "fr", content: [
            stack(64, component("Main View", "render")),
            stack(36, component("GIS View", "gis")),
        ],
    };
    const file = { format: "pro3d-layout", version: 1, name: "Projection stack story", layout: { root, openPopouts: [] } };
    fs.writeFileSync(path.join(dir, "current.json"), JSON.stringify(file, null, 2));
}

async function frameOf(main: Page, page: string): Promise<Frame> {
    let frame: Frame | undefined;
    await expect.poll(() => (frame = main.frames().find((f) => f.url().includes(`page=${page}`))) !== undefined,
        { timeout: 120_000 }).toBe(true);
    return frame!;
}

/** Waits until the render view shows the surface (not the splash, not empty). */
async function surfaceVisible(ui: Ui) {
    await expect.poll(async () => {
        const s = await viewShot(ui);
        return streamLive(s) && litFraction(s) > 0.003;
    }, { timeout: 600_000, intervals: [3000] }).toBe(true);
}

async function openVideo(browser: any, scene: string): Promise<{ app: Pro3d; context: BrowserContext; ui: Ui }> {
    const layouts = fs.mkdtempSync(path.join(os.tmpdir(), "pro3d-stack-story-layouts-"));
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
    const ui: Ui = { render, gis: await frameOf(main, "gis"), input: main, view, video: new Video(main, path.join(artifacts, "video-frames")) };
    return { app, context, ui };
}

async function openPictures(browser: any, scene: string): Promise<{ app: Pro3d; context: BrowserContext; ui: Ui }> {
    const app = await launchPro3d(scene);
    const context: BrowserContext = await browser.newContext();
    const render = await context.newPage();
    await render.setViewportSize({ width: 1280, height: 800 });
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await surfaceShadersReady(render);
    const gis = await context.newPage();
    await gis.setViewportSize({ width: 700, height: 1000 });
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    // the mouse drives the GIS page; the render page is only looked at
    return { app, context, ui: { render, gis, input: gis, view: { x: 0, y: 0, width: 1280, height: 800 } } };
}

function uploadLines(app: Pro3d): string[] {
    try {
        return fs.readFileSync(app.logFile, "utf-8").split(/\r?\n/).filter((l) => l.includes("[ProjectedStack] uploaded"));
    } catch {
        return [];
    }
}

test("story: reordering the projection stack by drag & drop", async ({ browser }) => {
    test.skip(!(customScene ? fs.existsSync(customScene) : fs.existsSync(fixture.sceneTemplate)) || !fs.existsSync(imageDir),
        "set PRO3D_TEST_DATA (or PRO3D_STACK_STORY_SCENE / PRO3D_STACK_STORY_IMAGES)");

    const scene = storyScene();
    const { app, context, ui } = await (videoMode ? openVideo : openPictures)(browser, scene);
    context.on("weberror", (e) => console.log("[page error]", e.error()));

    try {
        await surfaceVisible(ui);

        // --- setup, not part of the story: import the AFC-1 frames --------------------------
        await ui.gis.locator("text=Projected Images").first().click();
        await expect(ui.gis.locator("text=Import Directory").first()).toBeVisible({ timeout: 60_000 });
        // the import calls top.aardvark.dialog: in video mode that is the main page, not the
        // GIS iframe, so the stub goes into both
        for (const doc of [ui.gis, ui.input] as Doc[]) {
            await doc.evaluate((dir) => {
                const w = window as any;
                w.aardvark = w.aardvark ?? {};
                w.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [dir] }) };
            }, imageDir.replace(/\\/g, "/"));
        }
        await ui.gis.locator("text=Import Directory").first().click();
        // four consecutive frames, from the first whose name contains PRO3D_STACK_STORY_FROM
        // (e.g. "T121545"), else from the first of the folder
        const all = fs.readdirSync(imageDir).filter((f) => /\.(png|tiff?)$/i.test(f)).sort();
        const from = process.env.PRO3D_STACK_STORY_FROM;
        const start = from ? Math.max(0, all.findIndex((f) => f.includes(from))) : 0;
        const names = all.slice(start, start + 4);
        expect(names.length, "four frames in the image folder").toBe(4);
        await expect(ui.gis.locator(`text=${names[0]}`).first()).toBeVisible({ timeout: 120_000 });

        if (ui.video) {
            await ui.video.record();
            ui.video.begin();
        }
        await say(ui, customScene ? "Four HyperScout frames go into the projection stack. Higher in the stack wins where they overlap." : "Four AFC-1 frames of Dimorphos go into the projection stack. Higher in the stack wins where they overlap.", 4);

        // 1. the stack: bottom -> top = names[0..3]
        for (const n of names) await clickIcon(ui, await plusIcon(ui.gis, n));
        await until(ui, () => expect.poll(() => stackEntries(ui.gis), { timeout: 30_000 }).toEqual([...names].reverse()));
        const base = await shot(ui, "01-stack.png");

        // 2. hovering a stack row: that image stays, the others fade
        const bottom = names[0];
        const top = names[3];
        await say(ui, "Hovering a stack row: its image stays at full strength, the other layers fade.", 2);
        const bottomBox = (await (await stackRow(ui.gis, bottom)).boundingBox())!;
        await move(ui, centre(bottomBox).x, centre(bottomBox).y);
        const hovered = await shot(ui, "02-hover-fade.png");
        check(diffPng(base, hovered).changedFraction, "hovering fades the other layers").toBeGreaterThan(0.005);

        // 3. dragging it to the top: the 3D view previews the drop, the list keeps its order
        await say(ui, "Drag it onto the top row - the 3D view already shows the new order before the drop.", 2.5);
        const uploadsBefore = uploadLines(app).length;
        await ui.input.mouse.down();
        const topBox = (await (await stackRow(ui.gis, top)).boundingBox())!;
        await move(ui, centre(topBox).x, centre(topBox).y);
        const preview = await shot(ui, "03-drag-preview.png");
        check(await stackEntries(ui.gis), "the panel keeps the committed order while dragging")
            .toEqual([...names].reverse());
        check(diffPng(hovered, preview).changedFraction, "the drag previews the new order in 3D").toBeGreaterThan(0.002);

        // 4. drop: committed
        await say(ui, "Drop: the order is taken over.", 1.5);
        await ui.input.mouse.up();
        await until(ui, () => expect.poll(() => stackEntries(ui.gis), { timeout: 30_000 })
            .toEqual([bottom, names[3], names[2], names[1]]));
        await move(ui, ui.view.x + ui.view.width / 2, ui.view.y + ui.view.height - 40);
        await shot(ui, "04-dropped.png");
        await say(ui, "The arrows are still there for single steps; the stack is saved with the scene.", 3);
        await say(ui, null as unknown as string, 0);

        const timings = uploadLines(app).slice(uploadsBefore);
        fs.mkdirSync(artifacts, { recursive: true });
        fs.writeFileSync(path.join(artifacts, "upload-timings.txt"), timings.join("\n") + "\n");
        console.log(`[stack story] reorder uploads during the drag:\n${timings.join("\n")}`);

        if (ui.video) await ui.video.encode(path.join(artifacts, "projection-stack-story.mp4"));
    } finally {
        await context.close();
        await app.stop();
    }
});
