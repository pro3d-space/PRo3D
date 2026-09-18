import { expect, Page, BrowserContext } from "@playwright/test";
import { Pro3d, derivedScene } from "./pro3d";
import { diffPng, litFraction, streamLive } from "./image";
import * as fs from "fs";

/**
 * Shared steps for specs that draw annotations on the Dimorphos OPC:
 * scene setup, render gates, the tool strip, and ctrl+click picking.
 * The reasoning behind each is in ai/TESTING.md ("Mechanics that cost hours here").
 */

/** Camera distance from the body centre, chosen together with the frustum below.
 *  With PRo3D's normal 60 deg camera an ~160 m body spans about 160/dist radians, so 350 m
 *  puts it across roughly 700 px of a 1600 px view -- enough to pick two points well apart
 *  on it without magnifying the 1.96 m/px texture into a blur. */
export const CAMERA_DISTANCE = 350;

/**
 * The projection test scene, re-pointed at this machine's data, set up for drawing on
 * Dimorphos and then edited by `edit`. Written to `<dir>/<name>.pro3d`.
 */
export function drawingScene(dir: string, name: string, edit: (d: any) => void = () => {}): string {
    return derivedScene(name, (d) => {
        // Dimorphos as the scene body. Without one lat/lon/alt, elevations and the local up
        // vector are all undefined (Planet.None has no geographic frame).
        //
        // Setting referenceSystem.planet alone does NOT survive the load: the template
        // observes Dimorphos in J2000, which is not a body-fixed frame, so the scene-body
        // sync puts the planet back to None (scene-body.spec.ts covers that case). Giving it
        // the body-fixed observation is what makes the load derive the planet.
        d.gisApp.gisSurfaces = [];
        d.gisApp.defaultObservationInfo.observer = { EntitySpiceName: "Dimorphos" };
        d.gisApp.defaultObservationInfo.referenceFrame = { FrameSpiceName: "DIMORPHOS_FIXED" };
        d.referenceSystem.planet = 2; // Planet.None, filled in on load
        d.navigationMode = 0; // FreeFly
        // The template is a projection-test scene, so it carries the HERA AFC INSTRUMENT
        // frustum (hfov 5.53 deg, focal 122.563) rather than a viewing camera. Keep it and
        // the annotation selection outline is drawn ~10x too large, because
        // Utilities.drawSpheresFast converts pixels to world units as dist/viewportWidth,
        // omitting the field of view -- correct only near hfov 53 deg (issue #770).
        // Only `focal` matters: Scene.applyScene recomputes the frustum unconditionally as
        // calculateFrustum'(focal, nearPlane, farPlane, aspect) (Scene.fs:305-310, :393), so
        // the frustum stored in the scene -- and `toggleFocal`, which is only read by the
        // interactive FrustumMessage handler -- are both ignored at load.
        // 10.25 mm is FrustumModel's default: hfov = 2*atan(11.84/(2*focal)) = 60 deg.
        if (d.config?.frustumModel) d.config.frustumModel.focal = 10.25;
        // cameraView.view is [Sky, Location, Forward, Up, Right]; Forward already points
        // at the body centre, so scaling Location moves in without re-aiming.
        const loc = JSON.parse(d.cameraView.view[1]) as number[];
        const len = Math.hypot(loc[0], loc[1], loc[2]);
        d.cameraView.view[1] = JSON.stringify(loc.map((c) => (c / len) * CAMERA_DISTANCE));
        edit(d);
    }, dir);
}

/**
 * Fraction of the frame taken by the AARDVARK loading banner's yellow-green.
 *
 * The shared gate (`streamLive` + `litFraction`) does not reject that splash here.
 * `litFraction` measures the centre of the frame, which is exactly where the bright banner
 * sits. And `streamLive` samples three corners expecting the splash to be pure black, but
 * the render div's own CSS background is #222222 (Viewer.fs:2530) = 34, over its threshold
 * of 15, so it reports "live" as soon as the DOM exists. An unloaded view therefore scored
 * as lit and stable, and the test read a model that had not finished loading. Rejecting the
 * splash by its own colour is what makes "the surface is up" mean that.
 */
export function splashFraction(buf: Buffer): number {
    const { PNG } = require("pngjs");
    const img = PNG.sync.read(buf);
    let hits = 0;
    let total = 0;
    for (let y = Math.floor(img.height * 0.3); y < Math.floor(img.height * 0.8); y++)
        for (let x = Math.floor(img.width * 0.3); x < Math.floor(img.width * 0.75); x++) {
            const i = (img.width * y + x) * 4;
            const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
            total++;
            if (r > 150 && g > 170 && b < 130 && g > b + 60) hits++;
        }
    return total === 0 ? 0 : hits / total;
}

/** The surface on screen (not the splash), then two near-identical frames in a row.
 *  The settled frame is handed to `save` under `name`. */
export async function settled(
    page: Page,
    name: string,
    save: (name: string, png: Buffer) => void
): Promise<Buffer> {
    const started = Date.now();
    const loading = (b: Buffer) =>
        !streamLive(b) || litFraction(b) < 0.003 || splashFraction(b) > 0.002;
    let shot = await page.screenshot();
    while (loading(shot) && Date.now() - started < 420_000) {
        await page.waitForTimeout(3000);
        shot = await page.screenshot();
    }
    const waited = Math.round((Date.now() - started) / 1000);
    expect(splashFraction(shot), `still on the loading splash after ${waited}s (${name})`)
        .toBeLessThanOrEqual(0.002);
    expect(litFraction(shot), `render view still empty after ${waited}s (${name})`)
        .toBeGreaterThan(0.003);
    let prev = shot;
    for (let i = 0; i < 60; i++) {
        await page.waitForTimeout(1000);
        const cur = await page.screenshot();
        if (diffPng(prev, cur).changedFraction < 0.001) {
            prev = cur;
            break;
        }
        prev = cur;
    }
    save(name, prev);
    return prev;
}

/**
 * A tool-strip button, addressed by its tooltip rather than its icon: `wrapToolTip` puts
 * the tooltip on the button div itself (`title=`), and the icons are NOT unique --
 * "mouse pointer" is both Select annotation and Select surface (ViewerGUI.ToolStrip).
 */
export async function tool(render: Page, title: string) {
    return render.evaluate((t) => {
        const el = document.querySelector(`.pro3d-tool[title="${t}"]`);
        if (!el) return { found: false, active: false, enabled: false };
        return {
            found: true,
            active: el.classList.contains("active"),
            enabled: !el.classList.contains("disabled"),
        };
    }, title);
}

export async function clickTool(render: Page, title: string) {
    const r = await render.evaluate((t) => {
        const el = document.querySelector(`.pro3d-tool[title="${t}"]`) as HTMLElement | null;
        if (!el) return "not found";
        if (el.classList.contains("disabled")) return "disabled";
        el.click();
        return "clicked";
    }, title);
    expect(r, `tool strip button "${title}"`).toBe("clicked");
}

/**
 * Sets the <select> offering `option`, within `root`. The toolbar's dropdowns carry no
 * label of their own and the export window's differ only by content, so an option only
 * they have is the identity.
 *
 * The index is resolved in the page and the value then set through selectOption. Two
 * reasons for the detour: `filter({ has: 'option:text-is(...)' })` matches nothing,
 * because Playwright's text engine does not see the text of an <option> inside a
 * <select>; and a hand-dispatched "change" is what ai/TESTING.md records as silently
 * ignored by one of PRo3D's dropdown helpers, so the real event sequence is worth keeping.
 */
export async function selectByOption(page: Page, option: string, value: string, root = "") {
    const find = () =>
        page.evaluate(
            ({ o, r }) => {
                const scope = r ? document.querySelector(r) : document;
                if (!scope) return -1;
                const selects = Array.from(scope.querySelectorAll("select"));
                return selects.findIndex((s) =>
                    Array.from(s.options).some((x) => (x.textContent ?? "").trim() === o)
                );
            },
            { o: option, r: root }
        );
    // The toolbar is rendered into this page over the incremental DOM channel once the
    // interaction changes on the render page, so it arrives after the click, not with it.
    await expect.poll(find, { timeout: 60_000, intervals: [500] }).toBeGreaterThanOrEqual(0);
    const sel = root ? `${root} select` : "select";
    await page.locator(sel).nth(await find()).selectOption(value);
}

/**
 * Waits until PRo3D stops writing to its log for `quietMs`, i.e. the work the last input
 * started has finished.
 *
 * A fixed sleep cannot do this job: the first pick on a surface loads that patch's
 * KdTree from disk (~4.5 s each, six of them here) while later picks hit the warm cache
 * and cost milliseconds. PRo3D ignores input while it intersects, so a click sent too
 * early is silently dropped and the annotation simply never completes.
 */
export async function awaitIdle(page: Page, app: Pro3d, quietMs = 4000, timeoutMs = 120_000) {
    const size = () => {
        try {
            return fs.statSync(app.logFile).size;
        } catch {
            return -1;
        }
    };
    const started = Date.now();
    let last = size();
    let quietSince = Date.now();
    while (Date.now() - started < timeoutMs) {
        await page.waitForTimeout(500);
        const now = size();
        if (now !== last) {
            last = now;
            quietSince = Date.now();
        } else if (Date.now() - quietSince >= quietMs) {
            return;
        }
    }
    console.log(`[awaitIdle] still busy after ${Math.round(timeoutMs / 1000)}s`);
}

/** One ctrl+click on the surface. Raw page.mouse, never locator.click(): the render
 *  control's parent div intercepts pointer events, so Playwright's actionability check
 *  never settles and the click retries forever (there is no default action timeout). */
export async function pick(render: Page, x: number, y: number) {
    // The render control focuses itself on mouseenter (`onmouseenter="this.focus()"`), and
    // PRo3D reads the ctrl modifier from KEY events on the focused element, not from
    // MouseEvent.ctrlKey. Pressing Control in the same tick as the move sends the keydown
    // to whatever was focused before -- usually the other page -- and the click is then
    // taken as navigation, leaving no pick and no error anywhere.
    await render.bringToFront();
    await render.mouse.move(x, y);
    await render.waitForTimeout(400);
    await render.keyboard.down("Control");
    await render.waitForTimeout(200);
    await render.mouse.click(x, y);
    await render.waitForTimeout(200);
    await render.keyboard.up("Control");
}

/** Entries listed on the annotations page. */
export async function annotationCount(context: BrowserContext, app: Pro3d): Promise<number> {
    const page = await context.newPage();
    try {
        await page.goto(app.url + "?page=annotations");
        await page.waitForLoadState("networkidle");
        const text = (await page.evaluate(() => document.body.innerText)) as string;
        return (text.match(/#Points:/g) ?? []).length;
    } finally {
        await page.close();
    }
}

/**
 * Draws a sky-projected Line through the two picks `(cx - dx, cy + dy)` and
 * `(cx + dx, cy + dy)` of the render view, `(cx, cy)` being its centre, the way a user does: Draw annotation on the tool strip, Line and Sky
 * in the toolbar on `main`, then two ctrl+clicks. Leaves it as the selected annotation.
 * `onToolbar` runs once the toolbar is set up, before the first pick (e.g. to screenshot it).
 */
export async function drawSkyLine(
    context: BrowserContext,
    app: Pro3d,
    render: Page,
    main: Page,
    onToolbar: () => Promise<void> = async () => {},
    dx = 110,
    dy = 0
) {
    // The interaction selector is the tool strip on the right edge of the render
    // view; it replaced the top-menu dropdown.
    expect((await tool(render, "Draw annotation")).found, "the tool strip is present").toBe(true);
    await clickTool(render, "Draw annotation");
    await expect
        .poll(async () => (await tool(render, "Draw annotation")).active, { timeout: 30_000 })
        .toBe(true);

    // Line, then Sky. Both orders work (SetGeometry keeps a projection the geometry
    // allows, and Line allows all three) but this is the order a user follows.
    await selectByOption(main, "Polyline", "Line");
    await selectByOption(main, "Sky", "Sky");
    // the default thickness of 3 draws as a capsule wider than the profile is long
    const thickness = main.locator(".pro3d-secondary-toolbar input[type=number]").first();
    await thickness.fill("1");
    await thickness.press("Enter");
    await main.waitForTimeout(800);
    await onToolbar();

    // Two ctrl+clicks: Geometry.Line finishes itself at the second point
    // (Drawing-App.fs), so there is no Enter to press. Each pick ray-casts the
    // KdTrees for seconds and input is dropped meanwhile, hence the wait between.
    const vp = render.viewportSize() ?? { width: 1600, height: 900 };
    const cx = Math.round(vp.width / 2);
    const cy = Math.round(vp.height / 2);
    await awaitIdle(render, app);
    await pick(render, cx - dx, cy + dy);
    await awaitIdle(render, app);
    await pick(render, cx + dx, cy + dy);
    await awaitIdle(render, app);

    await expect
        .poll(() => annotationCount(context, app), { timeout: 120_000, intervals: [5000] })
        .toBe(1);
}
