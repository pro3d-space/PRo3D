import { test, expect, Frame, Page } from "@playwright/test";
import { launchPro3d, config, imageRow, surfaceShadersReady, firstImage } from "../src/pro3d";
import { Video } from "../src/video";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

/**
 * End-user video of the shadow measurement (docs/ShadowMeasurement.md) -- not a test, run only
 * with PRO3D_VIDEO=1. Same scene and frame as shadow-measurement-docshots.spec.ts:
 *
 *   PRO3D_SCENE      = C:\pro3ddata\HERA\workshop3\didyShadow.pro3d
 *   PRO3D_IMAGE_DIR  = C:\pro3ddata\HERA\workshop3\shadowDocShots
 *   FFMPEG           = ...\ffmpeg.exe
 *
 * The story: full PRo3D (3D view, GIS View, Image Inspector), fly to the image, zoom into the
 * Image Inspector (maximised), measure the shadow of a ridge (demonstration) with Snap on (the default), twice, back to 3D.
 *
 * Result: tests-ui/artifacts/shadow-measurement-video/shadow-measurement.mp4
 */

const out = path.join(__dirname, "..", "artifacts", "shadow-measurement-video");
const enabled = process.env.PRO3D_VIDEO === "1";

/** the crater: rim and shadow end in image pixels (0-based, origin top-left) */
const RIM = { x: 400, y: 580 };
const TIP = { x: 400, y: 611 };
const ZOOM_STEPS = 3; // 2^3 = 8x
const W = 1020, H = 1020;
type Box = { x: number; y: number; width: number; height: number };

test.setTimeout(30 * 60_000);

/** 3D view left; GIS View above the Image Inspector right (docs/WindowLayouts.md). */
function storyLayout(dir: string) {
    const component = (title: string, componentType: string) => ({
        type: "component", title, componentType, isClosable: componentType !== "render", header: { show: "top" },
        size: 1, componentState: { keepAlive: true }, sizeUnit: "fr",
    });
    const stack = (size: number, c: object) => ({ type: "stack", header: { show: "top" }, size, sizeUnit: "%", content: [c] });
    const root = {
        type: "row", size: 1, sizeUnit: "fr", content: [
            stack(58, component("Main View", "render")),
            { type: "column", size: 42, sizeUnit: "%", content: [
                stack(40, component("GIS View", "gis")),
                stack(60, component("Image Inspector", "imageinspector")),
            ] },
        ],
    };
    const file = { format: "pro3d-layout", version: 1, name: "Shadow measurement video", layout: { root, openPopouts: [] } };
    fs.writeFileSync(path.join(dir, "current.json"), JSON.stringify(file, null, 2));
}

async function frameOf(main: Page, page: string): Promise<Frame> {
    let frame: Frame | undefined;
    await expect.poll(() => (frame = main.frames().find((f) => f.url().includes(`page=${page}`))) !== undefined,
        { timeout: 120_000 }).toBe(true);
    return frame!;
}

test("shadow measurement: video", async ({ browser }) => {
    test.skip(!enabled, "set PRO3D_VIDEO=1 (and the Didymos scene/frame, see the header)");
    fs.mkdirSync(out, { recursive: true });
    const layouts = fs.mkdtempSync(path.join(os.tmpdir(), "pro3d-shadow-video-layouts-"));
    storyLayout(layouts);
    const app = await launchPro3d(undefined, { PRO3D_LAYOUT_DIR: layouts });
    const context = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    try {
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");
        const step = (s: string) => console.log(`[video] ${new Date().toISOString().slice(11, 19)} ${s}`);
        step("page loaded");
        const render = await frameOf(main, "render");
        step("render frame");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(render as unknown as Page);
        step("shaders ready");
        const gis = await frameOf(main, "gis");
        const insp = await frameOf(main, "imageinspector");
        step(`frames: ${main.frames().map((f) => f.url()).join(" | ")}`);
        const view = (await render.locator("img.rendercontrol").boundingBox({ timeout: 30_000 }))!;
        step(`view ${JSON.stringify(view)}`);

        // --- setup, not part of the video: import the frame (selects it), open the inspector -----
        // the scene brings its own layout: keep ours
        await expect.poll(async () => {
            for (const f of main.frames()) {
                const b = f.locator("[data-test=layout-scene-ignore]");
                if (await b.count().catch(() => 0) > 0) { await b.first().dispatchEvent("click", undefined, { timeout: 10_000 }); return true; }
            }
            return false;
        }, { timeout: 60_000, intervals: [1000] }).toBe(true);
        step("layout dialog dismissed");
        await main.waitForTimeout(1000);
        // dispatched: the accordion title may sit outside the small panel's scroll view
        await gis.locator("text=Projected Images").first().dispatchEvent("click", undefined, { timeout: 60_000 });
        step("projected images opened");
        await expect(gis.locator("text=Import Directory").first()).toBeVisible({ timeout: 60_000 });
        for (const doc of [gis, main] as (Page | Frame)[]) {
            await doc.evaluate((dir) => {
                const w = window as any;
                w.aardvark = w.aardvark ?? {};
                w.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [dir] }) };
            }, config.imageDir.replace(/\\/g, "/"));
        }
        await gis.locator("text=Import Directory").first().click({ timeout: 60_000 });
        await expect(gis.locator(imageRow).first()).toBeVisible({ timeout: 120_000 });
        const sourceName = firstImage(config.imageDir);
        step("imported");
        const openAnyway = insp.locator("button", { hasText: "Open anyway" });
        await expect(openAnyway).toBeVisible({ timeout: 120_000 });
        await openAnyway.click({ timeout: 60_000 });
        step("inspector open");
        await expect(insp.locator("text=/^wheel zooms/")).toBeVisible({ timeout: 120_000 });
        await main.waitForTimeout(20_000); // OPC streaming, the inspector's first frame

        const v = new Video(main, path.join(out, "frames"));
        await v.record();
        await main.waitForTimeout(500);
        v.begin();

        const capIn = (b: Box) => (t: string | null, hold = 3) => v.caption(t, b, hold);
        let cap = capIn(view);
        const clickAt = async (x: number, y: number) => {
            await v.move(x, y);
            await main.waitForTimeout(250);
            await v.ripple();
            await main.mouse.click(x, y);
        };
        const button = async (label: string | RegExp) => {
            const b = insp.locator("button", { hasText: label }).first();
            const bb = (await b.boundingBox())!;
            await clickAt(bb.x + bb.width / 2, bb.y + bb.height / 2);
        };

        // 1. full PRo3D
        await v.move(view.x + view.width * 0.5, view.y + view.height * 0.6);
        await cap("Shadow measurement in PRo3D answers questions like: how deep is a crater, how high is a boulder? From the length of a shadow and the sun's position when the image was taken", 5.5);
        await cap("A demonstration only: a simulated Hera AFC image of Didymos, on a shape model with a mock (Moon) texture -- not real terrain", 5);

        // 2. fly to the image
        const icon = (await gis.evaluateHandle((n) => {
            const all = Array.from(document.querySelectorAll("*")).filter((e) => (e.textContent ?? "").trim() === n);
            const deepest = all.filter((e) => !Array.from(e.children).some((c) => all.includes(c)));
            let el: Element | null = deepest[0] ?? null;
            while (el) {
                const i = el.nextElementSibling?.querySelector("i.location.icon");
                if (i) return i;
                el = el.parentElement;
            }
            return null;
        }, sourceName)).asElement();
        if (!icon) throw new Error("no fly-to icon in the image row");
        await cap("In the GIS View, fly to the image: the 3D view now looks from where the camera was", 3);
        const ib = (await icon.boundingBox())!;
        await v.move(ib.x + ib.width / 2, ib.y + ib.height / 2);
        await v.ripple();
        await icon.evaluate((e) => (e as HTMLElement).click());
        await main.waitForTimeout(9000); // the flight, shown
        await cap("The Image Inspector shows the selected image pixel by pixel -- the measurement happens there", 3.5);
        await cap(null);

        // 3. zoom into the Image Inspector, then maximise it
        const panel = await insp.evaluate(() => ({ x: 0, y: 0, width: window.innerWidth, height: window.innerHeight }));
        const frameEl = (await insp.frameElement()).asElement()!;
        const fb = (await frameEl.boundingBox())!;
        const target = { x: fb.x, y: fb.y - 30, width: panel.width, height: panel.height + 30 }; // with its tab
        await v.move(target.x + target.width / 2, target.y + target.height / 2);
        await main.evaluate((r) => {
            const s = Math.min(window.innerWidth / r.width, window.innerHeight / r.height);
            const tx = window.innerWidth / 2 - s * (r.x + r.width / 2);
            const ty = window.innerHeight / 2 - s * (r.y + r.height / 2);
            const b = document.body;
            document.documentElement.style.background = "#111";
            b.style.transformOrigin = "0 0";
            b.style.transition = "transform 1.8s ease-in-out";
            b.style.transform = `translate(${tx}px, ${ty}px) scale(${s})`;
        }, target);
        await main.waitForTimeout(2000);
        // maximise the inspector's stack (Golden Layout), drop the zoom
        const maximised = await main.evaluate(() => {
            const tabs = Array.from(document.querySelectorAll(".lm_tab"));
            const tab = tabs.find((t) => (t.textContent ?? "").includes("Image Inspector"));
            const stack = tab?.closest(".lm_stack");
            const max = stack?.querySelector(".lm_maximise") as HTMLElement | null;
            const b = document.body;
            b.style.transition = "none";
            b.style.transform = "";
            if (!max) return false;
            max.click();
            return true;
        });
        if (!maximised) console.log("[video] no maximise control on the Image Inspector stack");
        await v.cut(() => main.waitForTimeout(3000), 0);

        const control = insp.locator("img.rendercontrol");
        const box = (await control.boundingBox())!;
        console.log(`[video] inspector image ${JSON.stringify(box)}`);
        cap = capIn(box);

        const at = (p: { x: number; y: number }) => ({ x: box.x + (p.x + 0.5) / W * box.width, y: box.y + (p.y + 0.5) / H * box.height });
        const mid = { x: RIM.x, y: (RIM.y + TIP.y) / 2 };
        const fixed = at(mid);
        await cap("As an example, we measure the shadow of this ridge. Zoom in with the mouse wheel", 2.5);
        await v.move(fixed.x, fixed.y);
        for (let i = 0; i < ZOOM_STEPS; i++) {
            await main.mouse.wheel(0, -200);
            await main.waitForTimeout(500);
        }
        const scale = Math.pow(2, ZOOM_STEPS);
        const zoomedAt = (p: { x: number; y: number }) => ({
            x: fixed.x + (p.x - mid.x) / W * box.width * scale,
            y: fixed.y + (p.y - mid.y) / H * box.height * scale,
        });

        await cap("Click Measure shadow. The mode button next to it switches between crater depth and boulder height", 3.5);
        await button("Measure shadow");
        await v.cut(() => expect(insp.locator("text=/click the crater rim/")).toBeVisible({ timeout: 30_000 }));

        // Snap is on by default; make sure, without showing a toggle
        const snapButton = insp.locator("button", { hasText: /^Snap$/ }).first();
        const snapOn = async () => ((await snapButton.getAttribute("class")) ?? "").split(/\s+/).includes("active");
        if (!(await snapOn())) await snapButton.click();

        const result = insp.locator("text=/start over$/");
        const depthOf = async () => {
            const t = await result.innerText();
            const m = /depth (-?[\d.]+) m/.exec(t);
            return m ? m[1] : t;
        };

        /** one measurement with the clicks `dRim` / `dTip` image pixels off the edges */
        const measure = async (dRim: number, dTip: number, rimText: string, tipText: string) => {
            await cap(rimText, 3.5);
            const r = zoomedAt({ x: RIM.x, y: RIM.y + dRim });
            await clickAt(r.x, r.y);
            await v.cut(() => expect(insp.locator("text=/click the end of the shadow/")).toBeVisible({ timeout: 30_000 }));
            await main.waitForTimeout(1200);
            await cap(tipText, 3.5);
            const t = zoomedAt({ x: TIP.x, y: TIP.y + dTip });
            await clickAt(t.x, t.y);
            await v.cut(() => expect(result).toBeVisible({ timeout: 30_000 }));
            // off the image, so no hover crosshair hides the markers
            await v.move(box.x + box.width + 40, box.y + box.height / 2);
            await main.waitForTimeout(800);
            return depthOf();
        };

        const d1 = await measure(-2, -2,
            "Click the ridge's edge, where the shadow begins. Snap (on by default) moves the click onto the edge -- the orange ring",
            "The yellow line shows where the shadow can lie. Click the end of the shadow -- it snaps onto the shadow's edge");
        const rb = await insp.locator("text=/^Depth /").first().boundingBox();
        if (rb) await v.move(rb.x + rb.width + 30, rb.y + 10);
        await cap(`The shadow drops ${d1} m below the ridge's edge. The box at the top left shows the result: shadow length, sun elevation and the accuracy per pixel`, 5.5);
        await cap("Mesh offset is the quality check: close to 0 px means both clicks sit on the shadow's edges", 4.5);

        const d2 = await measure(2, 2,
            "Click again to start a new measurement. You need not hit the edge exactly ...",
            "... snapping finds it for you");
        await cap(`${d2} m -- the same result`, 3);

        // 4. back to the full view: the measurement in 3D
        await cap("Create scale bar adds the result to the 3D scene", 3);
        await button("Create scale bar");
        await main.waitForTimeout(800);
        await cap(null);
        if (maximised) {
            await main.evaluate(() => {
                const tabs = Array.from(document.querySelectorAll(".lm_tab"));
                const stack = tabs.find((t) => (t.textContent ?? "").includes("Image Inspector"))?.closest(".lm_stack");
                (stack?.querySelector(".lm_maximise") as HTMLElement | null)?.click();
            });
            await v.cut(() => main.waitForTimeout(3000), 0.2);
        }
        cap = capIn(view);
        await v.move(view.x + view.width * 0.5, view.y + view.height * 0.5);
        await cap("Back in the full view: the scale bars show the result in 3D and are saved with the scene", 4.5);
        await cap(null);
        await main.waitForTimeout(800);

        console.log(`depths: ${d1}, ${d2}`);
        await v.encode(path.join(out, "shadow-measurement.mp4"));
    } finally {
        await context.close();
        await app.stop();
    }
});
