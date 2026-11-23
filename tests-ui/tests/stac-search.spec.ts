import { test, expect, Page } from "@playwright/test";
import { launchPro3d, Pro3d, imageRow, surfaceShadersReady } from "../src/pro3d";
import { clickTool, pick, tool } from "../src/drawing";
import { diffPng, litFraction, streamLive } from "../src/image";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

/**
 * STAC image search end to end (docs/StacImageSearch.md): a body whose catalog items have
 * no footprint, searched at the cursor, checked by pointing, one hit projected.
 *
 * 1. the time range is set in the shared Filters section (Date, UTC; the STAC panel has
 *    no filters of its own); with the Image Search tool, a ctrl+click on the body
 *    searches the catalog; the status line reports images found *by pointing* (the
 *    footprint-less fallback) within the time range;
 * 2. the best row's Add cell downloads the image and puts it on the stack (not into the
 *    library);
 * 3. the render changes where the image is projected.
 *
 * Nothing about the catalog or the scene is in the repo: it needs
 *   PRO3D_STAC_URL     the catalog
 *   PRO3D_STAC_SCENE   a .pro3d with the body's surface, its SPICE binding and a kernel
 *                      covering the time range
 *   PRO3D_STAC_FROM / PRO3D_STAC_TO   the time range, YYYY-MM-DD (optional)
 *   PRO3D_STAC_USER / PRO3D_STAC_PASSWORD   the sign-in, when the catalog asks for one
 * and skips without the first two. Screenshots go to PRO3D_STAC_SHOTS (default: the OS
 * temp folder), never to artifacts/: they show catalog images.
 */

const catalog = process.env.PRO3D_STAC_URL ?? "";
const scene = process.env.PRO3D_STAC_SCENE ?? "";
const from = process.env.PRO3D_STAC_FROM ?? "";
const to = process.env.PRO3D_STAC_TO ?? "";
const shots = process.env.PRO3D_STAC_SHOTS ?? path.join(os.tmpdir(), "pro3d-stac-e2e");
/** for a catalog that asks to sign in (docs/StacImageSearch.md, Signing in); never in the repo */
const user = process.env.PRO3D_STAC_USER ?? "";
const password = process.env.PRO3D_STAC_PASSWORD ?? "";
/** the tool strip button, by its tooltip (ViewerGUI.ToolStrip) */
const toolTitle = "Image search - click the surface, or hold Ctrl and move over it, to find catalog images of that point (GIS tab: Image Search)";

let app: Pro3d;

test.setTimeout(20 * 60_000);
// no trace, screenshot or video on failure: they would show catalog content and land
// in test-results/ and playwright-report/, which CI uploads
test.use({ trace: "off", screenshot: "off", video: "off" });

test.beforeAll(async () => {
    if (!catalog || !scene) return;
    fs.mkdirSync(shots, { recursive: true });
    // the remembered catalog in a file of its own: not the developer's %APPDATA%Pro3Dstac.json
    app = await launchPro3d(scene, { PRO3D_STAC_URL: catalog, PRO3D_STAC_REMEMBERED: path.join(shots, "stac.json") });
});

test.afterAll(async () => {
    await app?.stop();
});

/** the main view once it shows the body and has stopped changing */
async function settled(render: Page, name: string): Promise<Buffer> {
    const shoot = () => render.screenshot();
    const started = Date.now();
    let shot = await shoot();
    while ((!streamLive(shot) || litFraction(shot) < 0.003) && Date.now() - started < 600_000) {
        await render.waitForTimeout(3000);
        shot = await shoot();
    }
    let prev = shot;
    for (let i = 0; i < 60; i++) {
        await render.waitForTimeout(1000);
        const cur = await shoot();
        if (diffPng(prev, cur).changedFraction < 0.001) { prev = cur; break; }
        prev = cur;
    }
    fs.writeFileSync(path.join(shots, name), prev);
    return prev;
}

/** the STAC panel's status line */
async function status(gis: Page): Promise<string> {
    return gis.evaluate(() => {
        // the innermost element holding the line (it sits in a span under its div)
        const re = /^(searching|checking|\d+ images|error|use the Image Search tool)/;
        const hits = Array.from(document.querySelectorAll("body *"))
            .filter((e) => re.test((e.textContent ?? "").trim()));
        const line = hits.find((e) => !Array.from(e.children).some((c) => hits.includes(c)));
        return (line?.textContent ?? "").trim();
    });
}

test("STAC search with the Image Search tool finds images by pointing and projects one", async ({ browser }) => {
    test.skip(!catalog || !scene, "needs PRO3D_STAC_URL and PRO3D_STAC_SCENE");
    const context = await browser.newContext();

    const render = await context.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await surfaceShadersReady(render);
    const before = await settled(render, "1-before.png");

    // --- GIS tab: the search panel, its filters --------------------------------------
    const gis = await context.newPage();
    gis.on("pageerror", (e) => console.log(`[gis pageerror] ${e.message}`));
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await gis.locator("text=Image Search (STAC)").first().click();
    await expect(gis.locator("text=Catalog:")).toBeVisible({ timeout: 60_000 });
    // no filters of its own: the shared Filters section narrows the results
    await expect(gis.locator("text=Sun incidence:")).toHaveCount(0);

    // the time range: the shared Date filter (UTC date/time fields, whole range = off)
    await gis.locator("text=Filters").first().click();
    const dates = gis.locator("input.pro3d-time-input");
    await expect(dates).toHaveCount(2, { timeout: 30_000 });
    if (from) { await dates.nth(0).fill(`${from}T00:00:00`); await dates.nth(0).blur(); }
    if (to) { await dates.nth(1).fill(`${to}T23:59:59`); await dates.nth(1).blur(); }
    await expect.poll(() => status(gis), { timeout: 30_000 }).toMatch(/^use the Image Search tool/);

    // --- the Image Search tool, a ctrl+click on the body --------------------------------
    // selecting the tool connects; a catalog that asks to sign in gets PRO3D_STAC_USER / _PASSWORD
    const main = await context.newPage();
    await main.goto(app.url);
    await clickTool(render, toolTitle);
    await expect.poll(async () => (await tool(render, toolTitle)).active, { timeout: 30_000 }).toBe(true);
    const dialog = main.locator(".stac-signin-dimmer");
    const connected = gis.locator("text=/^connected/");
    await expect(dialog.or(connected).first()).toBeVisible({ timeout: 60_000 });
    if (await dialog.isVisible()) {
        test.skip(!user || !password, "the catalog asks to sign in: set PRO3D_STAC_USER and PRO3D_STAC_PASSWORD");
        await main.locator(".stac-signin-dimmer [data-stac=user]").fill(user);
        await main.locator(".stac-signin-dimmer [data-stac=password]").fill(password);
        await main.locator(".stac-signin-dimmer [data-stac=submit]").click();
        await expect(dialog).toBeHidden({ timeout: 60_000 });
    }
    await expect(connected).toBeVisible({ timeout: 60_000 });
    const vp = render.viewportSize()!;
    await pick(render, vp.width * 0.5, vp.height * 0.5);

    // the first search downloads every candidate's sidecar: minutes, not seconds
    let last = "";
    await expect.poll(async () => {
        last = await status(gis);
        return /^\d+ images|^error/.test(last) ? last : "";
    }, { timeout: 15 * 60_000, intervals: [2000] }).not.toBe("");
    console.log(`[stac] ${last}`);
    expect(last, "the search must not fail").not.toMatch(/^error/);
    const kept = Number(/^(\d+) images/.exec(last)?.[1] ?? "0");
    expect(kept, `images found at the cursor: ${last}`).toBeGreaterThan(0);
    await gis.screenshot({ path: path.join(shots, "2-results.png"), fullPage: true });

    // --- click the best row: download, stack (not the library), render ------------------
    // the Add cell (+) of the first result row (the image list's columns: Preview | Add | Fly | ...)
    const rows = gis.locator("[data-stac-item] i.plus.icon");
    await expect(rows.first()).toBeVisible();
    const libraryBefore = await gis.locator(imageRow).count();
    await rows.first().click();
    // the stack panel's rows are the draggable ones
    await expect.poll(() => gis.locator('div[draggable="true"]').count(), { timeout: 10 * 60_000, intervals: [2000] })
        .toBeGreaterThan(0);
    expect(await gis.locator(imageRow).count(), "a download is not added to the library").toBe(libraryBefore);
    expect(await status(gis), "the download must not fail").not.toMatch(/^error/);

    await render.bringToFront();
    const after = await settled(render, "3-projected.png");
    const changed = diffPng(before, after).changedFraction;
    console.log(`[stac] render changed by ${(changed * 100).toFixed(1)} %`);
    expect(changed, "the projected image must show on the body").toBeGreaterThan(0.005);
    console.log(`[stac] screenshots in ${shots}`);
});
