import { test, expect, Page } from "@playwright/test";
import { spawn, ChildProcess } from "child_process";
import { launchPro3d, Pro3d, fixture, imageRow, surfaceShadersReady, testData } from "../src/pro3d";
import { clickTool, pick, tool } from "../src/drawing";
import { diffPng, litFraction, streamLive } from "../src/image";
import * as fs from "fs";
import * as path from "path";
import * as readline from "readline";

/**
 * Image Search against a catalog that asks to sign in (docs/StacImageSearch.md, "Signing
 * in" and "Testing"). No real catalog: the Expecto project's fake STAC catalog
 * (src/Tests/FakeStacCatalog.fs) serves the simulated AFC-1 frames of the test data,
 * behind HTTP Basic auth with made-up credentials, and reports every request it gets.
 *
 * 1. the catalog is off at start; selecting the Image Search tool connects, and the
 *    sign-in dialog opens over the main window;
 * 2. a wrong password keeps the dialog open with a message; the right one connects;
 * 3. a click on Dimorphos finds frames by pointing (they have no footprints); their
 *    thumbnails come through PRo3D's own route;
 * 4. hovering a row draws its frustum; clicking it projects the frame;
 * 5. the catalog saw nothing unsigned after the sign-in, and only the probe before it.
 *
 *   PRO3D_TEST_DATA   PRo3D.Resources.TestData checkout (the frames, the OPC, the scene)
 *   PRO3D_EXE         the viewer; its folder must also hold Tests.dll (a Debug build does)
 *   PRO3D_SPICE_KERNELS  kernels covering 2027-03-21, as for the projection specs
 */

const user = "tester";
const password = "not-a-secret";
/** the tool strip button, by its tooltip (ViewerGUI.ToolStrip) */
const toolTitle = "Image search - click the surface, or hold Ctrl and move over it, to find catalog images of that point (GIS tab: Image Search)";
const shots = path.join(__dirname, "..", "artifacts", "stac-signin");

type Logged = { method: string; path: string; status: number; signed: boolean };

/** Tests.exe --fake-stac: the API root, and the requests it reports */
async function startCatalog(exe: string): Promise<{ url: string; requests: Logged[]; proc: ChildProcess }> {
    const testsDll = path.join(path.dirname(exe), "Tests.dll");
    if (!fs.existsSync(testsDll)) throw new Error(`Tests.dll not found next to ${exe}: build src/Tests`);
    const proc = spawn("dotnet", [testsDll, "--fake-stac", fixture.frames, "--user", user, "--password", password], {
        stdio: ["pipe", "pipe", "pipe"],
    });
    const requests: Logged[] = [];
    const lines = readline.createInterface({ input: proc.stdout! });
    const url = await new Promise<string>((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error("the fake catalog did not start")), 60_000);
        proc.on("exit", (code) => reject(new Error(`the fake catalog exited (${code})`)));
        lines.on("line", (line) => {
            const m = /^\[request\] (\S+) (\S+) (\d+) (signed|unsigned)$/.exec(line);
            if (m) requests.push({ method: m[1], path: m[2], status: Number(m[3]), signed: m[4] === "signed" });
            else if (/^http:\/\/127\.0\.0\.1:\d+\/api$/.test(line)) {
                clearTimeout(timer);
                resolve(line);
            }
        });
    });
    return { url, requests, proc };
}

let app: Pro3d;
let catalog: { url: string; requests: Logged[]; proc: ChildProcess };

test.setTimeout(15 * 60_000);

test.beforeAll(async () => {
    if (!testData) return;
    fs.mkdirSync(shots, { recursive: true });
    const exe = process.env.PRO3D_EXE ?? path.join(__dirname, "..", "..", "bin", "Release", "net9.0", "PRo3D.Viewer.exe");
    catalog = await startCatalog(exe);
    // the remembered catalog in a file of its own: not the developer's %APPDATA%Pro3Dstac.json
    app = await launchPro3d(undefined, { PRO3D_STAC_URL: catalog.url, PRO3D_STAC_REMEMBERED: path.join(shots, "stac.json") });
});

test.afterAll(async () => {
    await app?.stop();
    catalog?.proc.stdin?.end();
    catalog?.proc.kill();
});

/** the main view once it shows the body and has stopped changing */
async function settled(render: Page, name: string): Promise<Buffer> {
    const shoot = () => render.screenshot();
    const started = Date.now();
    let shot = await shoot();
    while ((!streamLive(shot) || litFraction(shot) < 0.003) && Date.now() - started < 300_000) {
        await render.waitForTimeout(2000);
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
        const re = /^(searching|checking|\d+ images|error|use the Image Search tool)/;
        const hits = Array.from(document.querySelectorAll("body *"))
            .filter((e) => re.test((e.textContent ?? "").trim()));
        const line = hits.find((e) => !Array.from(e.children).some((c) => hits.includes(c)));
        return (line?.textContent ?? "").trim();
    });
}

test("sign in to a protected catalog, search, hover and project", async ({ browser }) => {
    test.skip(!testData, "needs PRO3D_TEST_DATA");
    const context = await browser.newContext();

    // the main page: the sign-in dialog lives here, over every docked panel
    const main = await context.newPage();
    main.on("pageerror", (e) => console.log(`[main pageerror] ${e.message}`));
    await main.goto(app.url);

    const render = await context.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await surfaceShadersReady(render);
    const before = await settled(render, "1-before.png");

    const gis = await context.newPage();
    gis.on("pageerror", (e) => console.log(`[gis pageerror] ${e.message}`));
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await gis.locator("text=Image Search (STAC)").first().click();
    await expect(gis.locator("text=Connection:")).toBeVisible({ timeout: 60_000 });

    // --- off at start: nothing sent -------------------------------------------------
    await expect(gis.locator("button:has-text('Connect')")).toBeVisible();
    expect(catalog.requests, "nothing sent before connecting").toHaveLength(0);

    // --- the tool connects; the catalog wants a sign-in ------------------------------
    await clickTool(render, toolTitle);
    await expect.poll(async () => (await tool(render, toolTitle)).active, { timeout: 30_000 }).toBe(true);
    const dialog = main.locator(".stac-signin-dimmer");
    await expect(dialog).toBeVisible({ timeout: 30_000 });
    await expect(dialog).toContainText("127.0.0.1");
    await main.screenshot({ path: path.join(shots, "2-dialog.png") });

    // a wrong password: the dialog stays, says so, and the password field is empty again
    await main.locator(".stac-signin-dimmer [data-stac=user]").fill(user);
    await main.locator(".stac-signin-dimmer [data-stac=password]").fill("wrong");
    await main.locator(".stac-signin-dimmer [data-stac=password]").press("Enter");
    await expect(dialog).toContainText("Wrong user name or password", { timeout: 30_000 });
    await expect(main.locator(".stac-signin-dimmer [data-stac=password]")).toHaveValue("");
    await expect(main.locator(".stac-signin-dimmer [data-stac=user]")).toHaveValue(user);

    // the right one
    await main.locator(".stac-signin-dimmer [data-stac=password]").fill(password);
    await main.locator(".stac-signin-dimmer [data-stac=submit]").click();
    await expect(dialog).toBeHidden({ timeout: 30_000 });
    await expect(gis.locator(`text=connected as ${user}`)).toBeVisible({ timeout: 30_000 });
    const signedInAt = catalog.requests.length;

    // --- search at the body's centre -------------------------------------------------
    const vp = render.viewportSize()!;
    await pick(render, vp.width * 0.5, vp.height * 0.5);
    let last = "";
    await expect.poll(async () => {
        last = await status(gis);
        return /^\d+ images|^error/.test(last) ? last : "";
    }, { timeout: 5 * 60_000, intervals: [1000] }).not.toBe("");
    console.log(`[stac-signin] ${last}`);
    expect(last, "the search must not fail").not.toMatch(/^error/);
    expect(Number(/^(\d+) images/.exec(last)?.[1] ?? "0"), `frames showing the point: ${last}`).toBeGreaterThan(0);

    // thumbnails: through PRo3D's route, loaded (the browser itself has no sign-in)
    const rows = gis.locator('div[title^="click: download and project"]');
    await expect(rows.first()).toBeVisible();
    const thumb = rows.first().locator("img");
    expect(await thumb.getAttribute("src")).toMatch(/^\/stac-overview\/[0-9a-f]+$/);
    await expect.poll(() => thumb.evaluate((i) => (i as HTMLImageElement).naturalWidth), { timeout: 30_000 }).toBeGreaterThan(0);
    await gis.screenshot({ path: path.join(shots, "3-results.png"), fullPage: true });

    // --- hover: the frustum and footprint ----------------------------------------------
    await rows.first().hover();
    await render.waitForTimeout(1500);
    const hovered = await render.screenshot();
    fs.writeFileSync(path.join(shots, "4-hover.png"), hovered);
    const hoverChange = diffPng(before, hovered).changedFraction;
    console.log(`[stac-signin] hover changed the render by ${(hoverChange * 100).toFixed(2)} %`);
    expect(hoverChange, "the hovered frame's frustum and footprint show").toBeGreaterThan(0.0005);

    // --- click: download (signed in), library, projection ------------------------------
    const libraryBefore = await gis.locator(imageRow).count();
    await rows.first().click();
    await expect.poll(() => gis.locator(imageRow).count(), { timeout: 5 * 60_000, intervals: [1000] })
        .toBeGreaterThan(libraryBefore);
    await gis.mouse.move(0, 0);
    await render.bringToFront();
    const after = await settled(render, "5-projected.png");
    const changed = diffPng(before, after).changedFraction;
    console.log(`[stac-signin] projection changed the render by ${(changed * 100).toFixed(1)} %`);
    expect(changed, "the projected frame shows on the body").toBeGreaterThan(0.005);

    // --- where the credentials went ---------------------------------------------------
    const beforeSignIn = catalog.requests.slice(0, signedInAt);
    const afterSignIn = catalog.requests.slice(signedInAt);
    console.log(`[stac-signin] catalog requests: ${beforeSignIn.length} before signing in, ${afterSignIn.length} after`);
    expect(beforeSignIn.filter((r) => r.status === 200), "nothing answered before the sign-in").toHaveLength(1);
    expect(afterSignIn.length, "the search, thumbnails and download went to the catalog").toBeGreaterThan(0);
    expect(afterSignIn.filter((r) => !r.signed), "nothing unsigned after the sign-in").toHaveLength(0);
    expect(afterSignIn.filter((r) => r.path.startsWith("/data/") && r.path.endsWith(".png")).length, "thumbnails and the image, signed in")
        .toBeGreaterThan(0);
});
