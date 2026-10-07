/**
 * Shared machinery for the documentation probes.
 *
 * Every helper here existed in three or four separately-tuned copies across the probes
 * before this module; each copy had drifted, and the drift was where the bugs lived.
 */
import type { BrowserContext, Page } from "@playwright/test";
import { PNG } from "pngjs";
import { diffPng } from "./image";
import { config } from "./pro3d";
import * as fs from "fs";
import * as path from "path";

// ---------------------------------------------------------------- classification

/**
 * The viewer's clear colour, `#222222` (`src/PRo3D.Viewer/Config.fs`, `backgroundColor`).
 *
 * This MUST be a constant. "Body" means *not the clear colour*, never *bright*: a region
 * of a texture mosaic that no image ever observed renders **pure black (0)**, which is a
 * perfectly good body pixel and nothing like the background.
 *
 * Every attempt to infer the background from the picture fails on exactly the frames that
 * matter. Sampling a corner breaks the moment the black region reaches that corner; taking
 * the most common value breaks too, because when most of the visible body is unobserved,
 * black *is* the most common value. Both then classify the body as background and report
 * it missing — which is how a correctly rendering scene once read as "bodyPixels=9501,
 * essentially an empty frame" and cost a session chasing a rendering bug that did not
 * exist. See ai/TESTING.md.
 */
export const BG = 34;

export interface Stats {
    /** fraction of the frame that is body */
    coverage: number;
    /** fraction of the BODY that is unobserved (pure black) */
    darkOfBody: number;
    /** pure-green pixels, for the projector frustum wireframe */
    greenPixels: number;
    /** fraction of the frame at the clear colour; a sanity check on BG itself */
    bgFraction: number;
    bodyPixels: number;
    /** horizontal centroid of the lit part, 0 when nothing is lit */
    litCentroidX: number;
    /** fraction of the BODY that is lit (brightness > 70) */
    litOfBody: number;
}

export function stats(img: PNG): Stats {
    let n = 0, body = 0, dark = 0, green = 0, atBg = 0, litN = 0, litSx = 0;
    for (let y = 0; y < img.height; y++)
        for (let x = 0; x < img.width; x++) {
            const i = (img.width * y + x) * 4;
            const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
            if (g > 120 && r < 90 && b < 90) green++;
            n++;
            const v = (r + g + b) / 3;
            if (Math.abs(v - BG) <= 6) { atBg++; continue; }
            body++;
            if (v < 25) dark++;
            if (v > 70) { litN++; litSx += x; }
        }
    return {
        coverage: n ? body / n : 0,
        darkOfBody: body ? dark / body : 0,
        greenPixels: green,
        bgFraction: n ? atBg / n : 0,
        bodyPixels: body,
        litCentroidX: litN ? litSx / litN : 0,
        litOfBody: body ? litN / body : 0,
    };
}

export const measure = (buf: Buffer): Stats => stats(PNG.sync.read(buf));

/**
 * Throw unless the frame really is against the clear colour we assume.
 *
 * `backgroundColor` is a CLI argument, so BG is a default rather than a law. Without this
 * check a changed background turns every number above into a plausible-looking lie.
 */
export function assertBackground(s: Stats, what: string) {
    if (s.bgFraction < 0.01)
        throw new Error(
            `${what}: only ${(s.bgFraction * 100).toFixed(2)}% of the frame sits at the ` +
            `assumed clear colour ${BG} -- every measurement of this frame would be meaningless`
        );
}

// ---------------------------------------------------------------- frames

/**
 * Wait until the viewer is really drawing the surface, then until two consecutive frames
 * agree, and return that frame.
 *
 * It THROWS on timeout. The previous copies of this loop all fell through to
 * `page.screenshot()` after the wait expired, so a probe that never saw the body emitted
 * a loading splash or an empty frame — and that frame was then written into docs/images/
 * as a documentation figure.
 */
export async function settled(
    page: Page,
    file?: string,
    timeoutMs = 420_000
): Promise<Buffer> {
    const t0 = Date.now();
    let shot = await page.screenshot();
    while (!drawingSurface(shot)) {
        if (Date.now() - t0 > timeoutMs) {
            // dump what it actually saw -- a failure you cannot look at is a failure you
            // will theorise about instead of diagnosing
            const dump = file ? file.replace(/\.png$/, "-TIMEOUT.png") : path.join(process.cwd(), "settled-TIMEOUT.png");
            fs.writeFileSync(dump, shot);
            const s = measure(shot);
            throw new Error(
                `settled: the viewer never drew a surface within ${Math.round(timeoutMs / 1000)} s` +
                (file ? ` (wanted ${path.basename(file)})` : "") +
                `; last frame: bg=${(s.bgFraction * 100).toFixed(1)}% body=${s.bodyPixels}px ` +
                `dark=${(s.darkOfBody * 100).toFixed(1)}% -- written to ${dump}`
            );
        }
        await page.waitForTimeout(3000);
        shot = await page.screenshot();
    }
    let prev = shot;
    let stable = false;
    for (let i = 0; i < 40; i++) {
        await page.waitForTimeout(900);
        const cur = await page.screenshot();
        if (diffPng(prev, cur).changedFraction < 0.001) { prev = cur; stable = true; break; }
        prev = cur;
    }
    if (!stable)
        throw new Error(`settled: the frame never stopped changing${file ? ` (${path.basename(file)})` : ""}`);
    if (file) fs.writeFileSync(file, prev);
    return prev;
}

/**
 * Is the viewer drawing a surface yet?
 *
 * Deliberately not `litFraction` (brightness-based): a body whose texture is mostly
 * unobserved is almost entirely black, and a brightness gate can never be satisfied for
 * it — the probe would wait out its whole timeout on a frame that was correct all along.
 * Ask instead whether anything at all differs from the clear colour.
 */
export function drawingSurface(buf: Buffer): boolean {
    const s = measure(buf);
    // NOT "some of the frame is background". A body can legitimately fill the whole
    // frame -- requiring visible background here reported "the viewer never drew a
    // surface" for a frame that was 99.4% terrain, which is the same misclassification
    // this module exists to prevent, one level up.
    return s.bodyPixels > 2000;
}

// ---------------------------------------------------------------- page chrome

/**
 * Hide the DOM overlays (HUD, tool strip, colour bar) so a screenshot is just the render.
 *
 * They are absolutely-positioned DOM over `img.rendercontrol` but not siblings of it, so
 * walking up from the render control's parent misses the HUD. Sweep the whole body.
 * Anything drawn by the scene graph itself survives this.
 */
export async function hideChrome(page: Page): Promise<number> {
    const hidden = (await page.evaluate(
        `(function(){
            var img = document.querySelector("img.rendercontrol");
            if (!img) return -1;
            var n = 0;
            Array.prototype.forEach.call(document.body.querySelectorAll("*"), function(e){
                if (e === img || e.contains(img)) return;
                var cs = getComputedStyle(e);
                if (cs.position === "absolute" || cs.position === "fixed") { e.style.display = "none"; n++; }
            });
            return n;
        })()`
    )) as number;
    if (hidden < 0) throw new Error("hideChrome: no img.rendercontrol on the page");
    return hidden;
}

// ---------------------------------------------------------------- GIS controls

/** `icon` is a Semantic UI icon name such as "plus" or "location arrow"; spaces are
 *  class separators, so they become dots in the selector (`i.location.arrow.icon`). */
export async function clickRowIcon(gis: Page, name: string, icon: string) {
    const sel = "i." + icon.trim().split(/\s+/).join(".") + ".icon";
    const r = await gis.evaluate(
        `(function(){
            var matches = Array.from(document.querySelectorAll("*")).filter(function(e){
                return (e.textContent || "").trim() === ${JSON.stringify(name)}; });
            var deepest = matches.filter(function(e){
                return !Array.from(e.children).some(function(c){ return matches.indexOf(c) >= 0; }); });
            if (deepest.length === 0) return "row not found";
            var el = deepest[0];
            while (el) {
                var row = el.nextElementSibling;
                var box = row ? row.querySelector(${JSON.stringify(sel)}) : null;
                if (box) { box.click(); return "clicked"; }
                el = el.parentElement;
            }
            return "no " + ${JSON.stringify(sel)} + " in row";
        })()`
    );
    if (r !== "clicked") throw new Error(`${icon} on ${name}: ${r}`);
}

export async function selectBeside(gis: Page, label: string, option: string) {
    const r = await gis.evaluate(
        `(function(){
            var el = Array.from(document.querySelectorAll("*")).find(function(e){
                return (e.textContent || "").trim() === ${JSON.stringify(label)}; });
            var sel = el && el.parentElement ? el.parentElement.querySelector("select") : null;
            if (!sel) return "no select beside " + ${JSON.stringify(label)};
            var opt = Array.from(sel.options).find(function(x){
                return x.textContent.trim() === ${JSON.stringify(option)}; });
            if (!opt) return "no option " + ${JSON.stringify(option)} + "; have " +
                Array.from(sel.options).map(function(o){ return o.textContent.trim(); }).join("|");
            sel.value = opt.value;
            sel.dispatchEvent(new Event("change", { bubbles: true }));
            return "ok";
        })()`
    );
    if (r !== "ok") throw new Error(`${label} -> ${option}: ${r}`);
}

/** click the checkbox beside a label (Transfer Function, ...) */
export async function toggleBeside(gis: Page, label: string) {
    const r = await gis.evaluate(
        `(function(){
            var el = Array.from(document.querySelectorAll("*")).find(function(e){
                return (e.textContent || "").trim() === ${JSON.stringify(label)}; });
            var box = el && el.parentElement ? el.parentElement.querySelector("i") : null;
            if (!box) return "no checkbox beside " + ${JSON.stringify(label)};
            box.click();
            return "ok";
        })()`
    );
    if (r !== "ok") throw new Error(`toggle ${label}: ${r}`);
}

/**
 * Hover one library/stack row, with a REAL pointer crossing.
 *
 * Two independent traps, both found by failure:
 *  - `new MouseEvent("mouseenter", { bubbles: false })` never reaches aardvark.media's
 *    delegated listener, so nothing happens.
 *  - a row is the full width of the panel (~1092 px), so nudging the mouse from `x - 40`
 *    to `x` never leaves it and no `mouseenter` fires. Leave the row first.
 *
 * The row is identified by containing its OWN name and no other row's. Resolving it as
 * "nearest ancestor with an inline border" instead picks the container shared by every
 * row, and then every image hovers the same element — which showed up as four
 * byte-identical frames.
 */
export async function hoverRow(gis: Page, name: string, others: string[]): Promise<void> {
    const box = (await gis.evaluate(
        `(function(){
            var name = ${JSON.stringify(name)}, others = ${JSON.stringify(others)};
            var matches = Array.from(document.querySelectorAll("*")).filter(function(e){
                return (e.textContent || "").trim() === name; });
            var deepest = matches.filter(function(e){
                return !Array.from(e.children).some(function(c){ return matches.indexOf(c) >= 0; }); });
            for (var i = 0; i < deepest.length; i++) {
                var el = deepest[i];
                while (el && el !== document.body) {
                    var txt = el.textContent || "";
                    var styled = el.style && el.style.cssText.indexOf("border") >= 0;
                    var unique = txt.indexOf(name) >= 0 && !others.some(function(o){
                        return o !== name && txt.indexOf(o) >= 0; });
                    if (styled && unique && el.getBoundingClientRect().height > 8) {
                        el.scrollIntoView({ block: "center" });
                        var r = el.getBoundingClientRect();
                        return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
                    }
                    if (!unique) break;
                    el = el.parentElement;
                }
            }
            return null;
        })()`
    )) as { x: number; y: number } | null;
    if (!box) throw new Error(`hoverRow: no unique row for ${name}`);
    await gis.mouse.move(5, 5);
    await gis.waitForTimeout(500);
    await gis.mouse.move(box.x, box.y, { steps: 6 });
    await gis.waitForTimeout(900);
}

/** move the pointer off every row, so nothing is hovered */
export async function unhover(gis: Page) {
    await gis.mouse.move(5, 5);
    await gis.waitForTimeout(700);
}

/**
 * Open the GIS page, import an image folder and put Projection Settings into the state
 * the documentation prescribes: Orientation Source MbiBased, Transfer Function off.
 *
 * Playwright cannot drive a native file dialog, hence the `window.aardvark.dialog` stub.
 */
export async function setUpProjection(
    ctx: BrowserContext,
    url: string,
    firstImage: string,
    imageDir: string = config.imageDir
): Promise<Page> {
    const gis = await ctx.newPage();
    await gis.setViewportSize({ width: 1200, height: 1100 });
    await gis.goto(url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await gis.locator("text=Import Directory").first().waitFor({ timeout: 60_000 });
    await gis.evaluate(
        `(function(){ window.aardvark = window.aardvark || {};
           window.aardvark.dialog = { showOpenDialog: function(){
             return Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(imageDir.replace(/\\/g, "/"))}] }); } }; })()`
    );
    await gis.locator("text=Import Directory").first().click();
    await gis.locator(`text=${firstImage}`).first().waitFor({ timeout: 120_000 });
    await gis.locator("text=Projection Settings").first().click();
    await gis.waitForTimeout(1200);
    // the enum case is MbiBased -- that is the literal the dropdown renders
    await selectBeside(gis, "Orientation Source:", "MbiBased");
    await toggleBeside(gis, "Transfer Function:");
    await gis.waitForTimeout(1500);
    return gis;
}

// ---------------------------------------------------------------- cameras

export function parseVec(s: string): number[] {
    return s.replace(/[[\]]/g, "").split(",").map((x) => Number(x.trim()));
}

/** a saved camera: [Sky, Location, Forward, Up, Right] */
export type View = string[];

export interface Bookmark {
    name: string;
    view: View;
    navigationMode: number;
}

/**
 * Bookmarks of a scene, sorted by name.
 *
 * A bookmark carries the whole five-vector view, so writing it into the scene's top-level
 * `cameraView` reproduces a viewpoint exactly. Never rebuild a view as "position, looking
 * at the origin": that discards the saved orientation and reframes the shot.
 */
export function bookmarksOf(scene: any): Bookmark[] {
    return ((scene.bookmarks?.flat ?? []) as any[])
        .map((e) => ({
            name: e.Bookmarks.name as string,
            view: e.Bookmarks.cameraView.view as View,
            navigationMode: e.Bookmarks.navigationMode as number,
        }))
        .sort((a, b) => a.name.localeCompare(b.name));
}

export const distanceOf = (view: View) => Math.hypot(...parseVec(view[1]));

/** how much of the frame a sphere of radius `r` covers, seen from `d`, in degrees */
export const subtends = (r: number, d: number) =>
    (2 * Math.asin(Math.min(1, r / d)) * 180) / Math.PI;

/**
 * The same view, `k` times further from the origin.
 *
 * Valid only for a radial view (Forward = -Location/|Location|), where scaling Location
 * leaves all five vectors pointing exactly where they did. Asserted, not assumed.
 */
export function pullBack(view: View, k: number): View {
    const loc = parseVec(view[1]);
    const fwd = parseVec(view[2]);
    const d = Math.hypot(...loc);
    const off = Math.hypot(fwd[0] + loc[0] / d, fwd[1] + loc[1] / d, fwd[2] + loc[2] / d);
    if (off > 1e-4)
        throw new Error(`pullBack: view is not radial (|forward + loc/|loc|| = ${off.toExponential(2)})`);
    const out = view.slice();
    out[1] = `[${loc.map((c) => c * k).join(", ")}]`;
    return out;
}

/**
 * Clear the GIS "Camera source Body".
 *
 * While it is set, the camera is PLACED AT THAT ENTITY and aimed at the observed body, so
 * the scene's own `cameraView` has no effect whatsoever. PRo3D.Resources.TestData's
 * ProjectionTest.pro3d ships with it set to HERA, ~8 km out, which leaves the body a few
 * pixels across and makes every attempt to position the camera look like it was ignored —
 * because it was. Any probe that sets a camera must call this first.
 */
export function withViewerLens(scene: any, focal: number = VIEWER_FOCAL) {
    if (!scene.config?.frustumModel)
        throw new Error("scene has no config.frustumModel; cannot set the lens");
    scene.config.frustumModel.focal = focal;
    return scene;
}

/**
 * Select a surface texture by label.
 *
 * Which mosaic is selected decides what the body looks like, and the test-data template
 * selects DRACO_2 -- which covers the body completely. DRACO_1 is the partial mosaic with
 * an unobserved region, and that region is the entire subject of the projection figures.
 * Picking the wrong one produces a perfectly good screenshot of the wrong thing.
 */
export function withPrimaryTexture(scene: any, label: string) {
    const surfaces = scene.surfaceModel?.surfaces?.flat ?? [];
    let found = false;
    for (const entry of surfaces) {
        const s = entry.Surfaces;
        if (!s?.textureLayers) continue;
        const layer = s.textureLayers.find((l: any) => l.label === label);
        if (!layer) continue;
        s.selectedTexture = { index: layer.index, label: layer.label, version: 0 };
        found = true;
    }
    if (!found)
        throw new Error(
            `withPrimaryTexture: no texture layer "${label}"; have ` +
            surfaces.flatMap((e: any) => (e.Surfaces?.textureLayers ?? []).map((l: any) => l.label)).join(", ")
        );
    return scene;
}

export function withFreeCamera(scene: any) {
    if (scene.gisApp?.defaultObservationInfo) scene.gisApp.defaultObservationInfo.target = null;
    return scene;
}

/** write `scene` to `file` with this camera, and return the path */
export function sceneWithCamera(scene: any, view: View, navigationMode: number, file: string): string {
    const d = withFreeCamera(JSON.parse(JSON.stringify(scene)));
    const focal = d.config?.frustumModel?.focal;
    if (focal !== undefined && Math.abs(focal - VIEWER_FOCAL) > 1e-6)
        throw new Error(
            `sceneWithCamera: focal is ${focal} (fov ${(2 * Math.atan(11.84 / (2 * focal)) * 180 / Math.PI).toFixed(2)} deg), ` +
            `but the saved viewpoints assume ${VIEWER_FOCAL} (60 deg). Call withViewerLens first.`
        );
    d.cameraView = { view };
    d.navigationMode = navigationMode;
    d.scenePath = file;
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, JSON.stringify(d, null, 2));
    return file;
}

/**
 * Put a scene in the body's own fixed frame, as picking the body under *Reference System*
 * does in the GUI. Without this the scene stays in an inertial frame, the body does not
 * turn with it, and nothing that depends on the body's rotation is visible.
 */
export function withBodyFixedFrame(scene: any, planet: number, body: string, frame: string, time?: string) {
    scene.referenceSystem = { ...(scene.referenceSystem ?? {}), planet };
    scene.gisApp = scene.gisApp ?? {};
    scene.gisApp.defaultObservationInfo = {
        ...(scene.gisApp.defaultObservationInfo ?? {}),
        observer: { EntitySpiceName: body },
        referenceFrame: { FrameSpiceName: frame },
        ...(time ? { time } : {}),
    };
    return scene;
}

export function readScene(file: string): any {
    return JSON.parse(fs.readFileSync(file, "utf-8").replace(/^﻿/, ""));
}

/** spherical-convention radius for Dimorphos, src/PRo3D.Base/CooTransformation.fs */
export const DIMORPHOS_RADIUS = 77.2;

/**
 * Three viewpoints on Dimorphos, so the figures can be regenerated from the SHIPPED test
 * data. They were chosen by hand in the viewer and saved as bookmarks of a scene that is
 * not in any repository; PRo3D.Resources.TestData's ProjectionTest.pro3d has no bookmarks
 * of its own, and its saved camera leaves the body a few hundred pixels across.
 *
 * All three are radial at 198.77 m, where the body subtends ~46 deg inside the scene's
 * 60 deg fov (focal 10.25). Forward is exactly -Location/|Location|, which is what lets
 * `pullBack` dolly straight out along them.
 *
 * They only mean anything at DIMORPHOS_VIEWPOINT_EPOCH. In a body-fixed frame the
 * observation time IS the body's orientation, so the same camera at another epoch looks
 * at a rotated body -- or, two years out, at empty space. Set both together.
 */
/**
 * The lens the viewpoints below assume: focal 10.25 mm = a 60 deg vertical fov
 * (Utilities.fs, `hfov = 2*atan(sensor/(2*focal))`).
 *
 * PRo3D.Resources.TestData's ProjectionTest.pro3d ships with focal 122.563 -- the AFC
 * INSTRUMENT lens, 5.53 deg. A camera is meaningless without its fov: at 198.8 m the
 * body subtends 46 deg, which fits a 60 deg frame and overflows a 5.53 deg one eightfold,
 * filling the screen with blurred terrain. `withViewerLens` sets it; `sceneWithCamera`
 * refuses a scene whose lens would make the saved viewpoints nonsense.
 */
export const VIEWER_FOCAL = 10.25;

/** the epoch the viewpoints below were chosen at; see the note on DIMORPHOS_VIEWPOINTS */
export const DIMORPHOS_VIEWPOINT_EPOCH = "2025-03-10T19:08:12.6000000Z";

export const DIMORPHOS_VIEWPOINTS: Bookmark[] = [
    {
        name: "unobserved side",
        navigationMode: 2,
        view: [
            "[0.3484456513218041, 0.008020947236182676, 0.9372946668366134]",
            "[-186.25830556097827, -4.287520982219715, 69.27947941283063]",
            "[0.9370464365212539, 0.02157007842307351, -0.3485379570715963]",
            "[0.3484456513218041, 0.008020947236182676, 0.9372946668366133]",
            "[0.02301312403267249, -0.9997351629918081, -2.6541269182445153E-16]",
        ],
    },
    {
        name: "textured side",
        navigationMode: 2,
        view: [
            "[0.09757843433911105, 0.34127783223613367, 0.9348838913876597]",
            "[-51.08511584784219, -178.66875723271784, 70.55474202391548]",
            "[0.25700397960951216, 0.8988641971071084, -0.35495367250370413]",
            "[0.09757843433911105, 0.3412778322361337, 0.9348838913876595]",
            "[0.9614714783168565, -0.2749047041852845, 6.938893903907228E-17]",
        ],
    },
    {
        name: "second direction",
        navigationMode: 2,
        view: [
            "[-0.3854447101177167, 0.15148095812013834, 0.910212005397245]",
            "[168.3872725321974, -66.17671668298156, 82.31982818592645]",
            "[-0.8471391018328127, 0.33292827593217356, -0.41414261460362384]",
            "[-0.38544471011771675, 0.15148095812013834, 0.9102120053972451]",
            "[0.36577003374820716, 0.9307052607629525, -8.326672684688674E-17]",
        ],
    },
];
