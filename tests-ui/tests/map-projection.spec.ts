// Map projection view (#772), rung 5 of its testing ladder: the real app in a browser.
//
// The headless Expecto render test proves the projection pixel by pixel; this proves the
// panel: it serves, renders through the render-control stream, the projection buttons and
// the mouse reach the model, and PRo3D shows the same panel as a page.
//
// The signals are the graticule's two coloured lines, because they survive the JPEG stream
// and their position is known exactly: on an equirectangular map filling the view the prime
// meridian (red) is the middle column and the equator (yellow) the middle row; on the north
// polar map the prime meridian runs from the centre straight down. The texture itself is no
// signal here -- the test OPC's default layer is a camera frame, mostly black sky.
import { test, expect, Page } from "@playwright/test";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";
import { launchMap } from "../src/mapprojection";
import { launchPro3d, fixture, derivedScene } from "../src/pro3d";
import { diffPng } from "../src/image";

const artifacts = path.join(__dirname, "..", "artifacts");
const W = 1200;
const H = 600;

type Img = { width: number; height: number; data: Buffer };
const isRed = (d: Buffer, o: number) => d[o] > 150 && d[o + 1] < 110 && d[o + 2] < 110;
const isYellow = (d: Buffer, o: number) => d[o] > 150 && d[o + 1] > 150 && d[o + 2] < 110;

/** fraction of rows y0..y1 with a red pixel within +-2 px of column x */
function redColumn(img: Img, x: number, y0: number, y1: number) {
    let hits = 0;
    for (let y = y0; y < y1; y++) {
        let hit = false;
        for (let dx = -2; dx <= 2 && !hit; dx++) hit = isRed(img.data, (y * img.width + x + dx) * 4);
        if (hit) hits++;
    }
    return hits / (y1 - y0);
}

/** fraction of columns x0..x1 with a yellow pixel within +-2 px of row y */
function yellowRow(img: Img, y: number, x0: number, x1: number) {
    let hits = 0;
    for (let x = x0; x < x1; x++) {
        let hit = false;
        for (let dy = -2; dy <= 2 && !hit; dy++) hit = isYellow(img.data, ((y + dy) * img.width + x) * 4);
        if (hit) hits++;
    }
    return hits / (x1 - x0);
}

/** fraction of pixels showing surface texture: mid-grey (the grid is ~200, the clear colour and
 *  the sky of the test layer are dark) */
function textured(img: Img) {
    let n = 0;
    for (let o = 0; o < img.data.length; o += 4) {
        const r = img.data[o], g = img.data[o + 1], b = img.data[o + 2];
        if (r > 70 && r < 185 && Math.abs(r - g) < 12 && Math.abs(r - b) < 12) n++;
    }
    return n / (img.width * img.height);
}

/** the column with the most red pixels in rows y0..y1 */
function reddestColumn(img: Img, y0: number, y1: number) {
    let best = -1;
    let bestCount = -1;
    for (let x = 0; x < img.width; x++) {
        let n = 0;
        for (let y = y0; y < y1; y++) if (isRed(img.data, (y * img.width + x) * 4)) n++;
        if (n > bestCount) {
            best = x;
            bestCount = n;
        }
    }
    return best;
}

/** screenshot until two consecutive shots agree, the graticule is there and the surface
 *  has streamed in: the grid alone renders first and is "stable" long before the patches
 *  arrive (asynchronous loading), which once made an empty map look settled */
async function settle(page: Page, name: string, minTextured = 0.05): Promise<Img> {
    const until = Date.now() + 180_000;
    let shot = await page.screenshot();
    for (;;) {
        await page.waitForTimeout(1500);
        const cur = await page.screenshot();
        const stable = diffPng(shot, cur).changedFraction < 0.002;
        shot = cur;
        const img = PNG.sync.read(cur);
        let anyRed = false;
        for (let o = 0; o < img.data.length && !anyRed; o += 4) anyRed = isRed(img.data, o);
        if ((stable && anyRed && textured(img) > minTextured) || Date.now() > until) break;
    }
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), shot);
    return PNG.sync.read(shot);
}

async function clickButton(page: Page, label: string) {
    // single-shot DOM click: Playwright's actionability loop starves against the incremental UI
    const r = await page.evaluate((l) => {
        const b = Array.from(document.querySelectorAll("button")).find((e) => (e.textContent ?? "").trim() === l);
        if (!b) return "no button " + l;
        (b as HTMLElement).click();
        return "clicked";
    }, label);
    expect(r).toBe("clicked");
}

/** The test-data Dimorphos scene, observed in DIMORPHOS_FIXED: world coordinates are body-fixed
 *  and loading sets the planet (#758). Unbound surfaces inherit the scene body, like the
 *  scene-body spec's GIS-only scene. */
function bodyFixedScene() {
    return derivedScene("map-projection-body-fixed", (d) => {
        d.gisApp.gisSurfaces = [];
        d.gisApp.defaultObservationInfo.observer = { EntitySpiceName: "Dimorphos" };
        d.gisApp.defaultObservationInfo.referenceFrame = { FrameSpiceName: "DIMORPHOS_FIXED" };
        d.referenceSystem.planet = 2; // Planet.None, as saved by a GIS-only setup
    });
}

async function poll<T>(what: string, f: () => Promise<T>, ok: (v: T) => boolean, timeoutMs = 60_000): Promise<T> {
    const until = Date.now() + timeoutMs;
    let v = await f();
    while (!ok(v)) {
        if (Date.now() > until) throw new Error(`timed out: ${what} (last: ${JSON.stringify(v)})`);
        await page_wait(500);
        v = await f();
    }
    return v;
}
const page_wait = (ms: number) => new Promise((r) => setTimeout(r, ms));

/** A fresh per-user layout directory: window layouts persist per user, so a panel opened by an
 *  earlier run would no longer be offered under Reopen Panel -- and the tests must not touch the
 *  developer's own layouts. */
function freshLayouts() {
    return { PRO3D_LAYOUT_DIR: fs.mkdtempSync(path.join(require("os").tmpdir(), "pro3d-map-layouts-")) };
}

test.describe("map projection view (#772)", () => {
    test.skip(!fs.existsSync(fixture.opc), `no Dimorphos OPC at ${fixture.opc} (set PRO3D_TEST_DATA)`);

    test("standalone: equirectangular, drag, polar north", async ({ browser }) => {
        const app = await launchMap([fixture.opc]);
        const page = await (await browser.newContext({ viewport: { width: W, height: H } })).newPage();
        try {
            await page.goto(app.url);
            await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });

            // equirectangular at zoom 1 fills a 2:1 view exactly
            let img = await settle(page, "map-standalone-equirect.png");
            expect(textured(img), "the surface texture is on the map").toBeGreaterThan(0.1);
            expect(redColumn(img, W / 2, 60, H - 20), "prime meridian is the middle column").toBeGreaterThan(0.8);
            expect(yellowRow(img, H / 2, 20, W - 20), "equator is the middle row").toBeGreaterThan(0.8);

            // grab and drag: the map follows the pointer
            await page.mouse.move(W / 2 + 50, H / 2 + 100);
            await page.mouse.down();
            for (let i = 1; i <= 10; i++) await page.mouse.move(W / 2 + 50 + 15 * i, H / 2 + 100);
            await page.mouse.up();
            img = await settle(page, "map-standalone-dragged.png");
            const meridian = reddestColumn(img, 60, H - 20);
            expect(Math.abs(meridian - (W / 2 + 150)), `meridian moved with the drag (now at ${meridian})`).toBeLessThan(6);

            // polar north: the prime meridian runs from the centre straight down (USGS convention)
            await clickButton(page, "Polar north");
            img = await settle(page, "map-standalone-polar-north.png");
            const r = H / 2;
            expect(redColumn(img, W / 2, H / 2 + 10, H / 2 + r - 10), "meridian below the pole").toBeGreaterThan(0.8);
            expect(redColumn(img, W / 2, 50, H / 2 - 10), "and not above it").toBeLessThan(0.2);
        } finally {
            await app.stop();
        }
    });

    test("standalone: a planet gets the hint, not a map", async ({ browser }) => {
        const app = await launchMap([fixture.opc], ["--planet", "Mars"], 54332);
        const page = await (await browser.newContext({ viewport: { width: W, height: H } })).newPage();
        try {
            await page.goto(app.url);
            await expect(page.locator("text=available for small bodies")).toBeVisible({ timeout: 60_000 });
            expect(await page.locator("img.rendercontrol").count()).toBe(0);
        } finally {
            await app.stop();
        }
    });

    test("in PRo3D: the mapprojection page shows the same map for a body-fixed scene", async ({ browser }) => {
        const app = await launchPro3d(bodyFixedScene(), freshLayouts());
        const page = await (await browser.newContext({ viewport: { width: W, height: H } })).newPage();
        try {
            await page.goto(app.url + "?page=mapprojection");
            await page.waitForSelector("img.rendercontrol", { timeout: 120_000 });
            const img = await settle(page, "map-pro3d-equirect.png");
            expect(textured(img), "the surface texture is on the map").toBeGreaterThan(0.1);
            expect(redColumn(img, W / 2, 60, H - 20), "prime meridian is the middle column").toBeGreaterThan(0.8);
            expect(yellowRow(img, H / 2, 20, W - 20), "equator is the middle row").toBeGreaterThan(0.8);
        } finally {
            await app.stop();
        }
    });

    test("in PRo3D: Layout > Reopen Panel > Map Projection opens the map as a panel", async ({ browser }) => {
        const app = await launchPro3d(bodyFixedScene(), freshLayouts());
        const context = await browser.newContext({ viewport: { width: 1600, height: 900 } });
        const page = await context.newPage();
        try {
            await page.goto(app.url);
            await page.waitForSelector(".lm_tab", { timeout: 120_000 });

            // the main menu (top-left) -> Layout -> Reopen Panel lists the panel...
            const entry = '[data-test="layout-reopen"] [data-panel="mapprojection"]';
            await poll("Map Projection is offered under Layout > Reopen Panel",
                () => page.evaluate((sel) => document.querySelector(sel)?.textContent?.trim() ?? "", entry),
                (t) => t === "Map Projection", 120_000);

            // ...and clicking it adds the tab. Events clicked before the page's socket is up are
            // lost, so click until the tab is there (single-shot DOM click, see tests-ui/README.md).
            const tabTitles = () => page.evaluate(() =>
                Array.from(document.querySelectorAll(".lm_tab .lm_title")).map((t) => (t.textContent ?? "").trim()));
            await poll("the Map Projection tab appears", async () => {
                const titles = await tabTitles();
                if (!titles.includes("Map Projection"))
                    await page.evaluate((sel) => (document.querySelector(sel) as HTMLElement | null)?.click(), entry);
                return titles;
            }, (t) => t.includes("Map Projection"), 60_000);

            // select the tab and maximise its stack. Dispatched events, not a click at the tab's
            // coordinates: in a narrow stack the new tab sits behind Golden Layout's overflow chevron
            const shown = await page.evaluate(() => {
                const tab = Array.from(document.querySelectorAll(".lm_tab")).find(
                    (e) => (e.querySelector(".lm_title")?.textContent ?? "").trim() === "Map Projection") as HTMLElement | undefined;
                if (!tab) return "no tab";
                for (const type of ["mousedown", "mouseup", "click"])
                    tab.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, button: 0, buttons: 1 }));
                const maximise = tab.closest(".lm_stack")?.querySelector(".lm_controls .lm_maximise") as HTMLElement | null;
                if (!maximise) return "no maximise button";
                maximise.click();
                return "shown";
            });
            expect(shown).toBe("shown");

            // the panel is an iframe on ?page=mapprojection; wait until it is laid out
            const frameBox = await poll("the Map Projection panel is visible", () => page.evaluate(() => {
                const f = document.querySelector('iframe[src*="page=mapprojection"]');
                const r = f?.getBoundingClientRect();
                return r ? { x: r.x, y: r.y, width: r.width, height: r.height } : { x: 0, y: 0, width: 0, height: 0 };
            }), (b) => b.width > 200 && b.height > 150, 60_000);
            const frame = page.frameLocator('iframe[src*="page=mapprojection"]');
            await frame.locator("img.rendercontrol").waitFor({ timeout: 120_000 });

            // the map inside the panel: texture drawn, graticule where the projection puts it.
            // Equirectangular fits the panel with its 2:1 aspect, centred.
            const until = Date.now() + 180_000;
            let img: Img;
            for (;;) {
                const png = await page.screenshot({ clip: frameBox });
                img = PNG.sync.read(png);
                let anyRed = false;
                for (let o = 0; o < img.data.length && !anyRed; o += 4) anyRed = isRed(img.data, o);
                if ((anyRed && textured(img) > 0.05) || Date.now() > until) {
                    fs.mkdirSync(artifacts, { recursive: true });
                    fs.writeFileSync(path.join(artifacts, "map-pro3d-panel.png"), png);
                    break;
                }
                await page.waitForTimeout(1500);
            }
            const w = img.width, h = img.height;
            const mapW = Math.min(w, 2 * h), mapH = mapW / 2;
            const top = Math.round((h - mapH) / 2), left = Math.round((w - mapW) / 2);
            expect(textured(img), "the surface texture is on the map").toBeGreaterThan(0.05);
            expect(redColumn(img, Math.round(w / 2), top + 50, top + mapH - 10), "prime meridian is the middle column").toBeGreaterThan(0.8);
            expect(yellowRow(img, Math.round(h / 2), left + 10, left + mapW - 10), "equator is the middle row").toBeGreaterThan(0.8);
        } finally {
            await context.close();
            await app.stop();
        }
    });

    test("in PRo3D: a scene observed in J2000 has no planet, so no map", async ({ browser }) => {
        // the unmodified test-data scene: Dimorphos observed in J2000, planet None. Its world axes
        // are not body-fixed, so longitude and latitude would be rotated -- MapView refuses it too.
        const app = await launchPro3d(undefined, freshLayouts());
        const page = await (await browser.newContext({ viewport: { width: W, height: H } })).newPage();
        try {
            await page.goto(app.url + "?page=mapprojection");
            await expect(page.locator("text=available for small bodies")).toBeVisible({ timeout: 120_000 });
            expect(await page.locator("img.rendercontrol").count()).toBe(0);
        } finally {
            await app.stop();
        }
    });
});
