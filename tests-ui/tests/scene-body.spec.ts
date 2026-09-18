import { test, expect, Page, Browser } from "@playwright/test";
import { launchPro3d, Pro3d, fixture, derivedScene as deriveScene } from "../src/pro3d";
import { diffPng, litFraction, streamLive } from "../src/image";
import { overlayPlanet } from "../src/viewer";
import * as fs from "fs";
import * as path from "path";

/**
 * The scene body (#758, docs/SceneBody.md): the planet in the top bar and the GIS
 * observed body + reference frame are one setting.
 *
 * Every case derives a scene from the test-data projection scene (PRO3D_TEST_DATA),
 * which is itself the legacy case: observed body Dimorphos in J2000, surface bound
 * explicitly, planet None.
 *
 *  - a scene set up in the GIS view only (body-fixed, no surface binding, no planet):
 *    loading fills in the planet, map view is available and navigates, and the
 *    unbound surface inherits the scene body;
 *  - the J2000 scene loads unchanged, says why map view is off, and switches to the
 *    body-fixed frame on request;
 *  - picking the planet in the top bar sets up the GIS observation.
 *
 * Image projection with the scene body is covered by projection-e2e (the scene-body
 * cases there project a generated frame back through an unbound surface).
 */

const artifacts = path.join(__dirname, "..", "artifacts", "scene-body");
const derivedScene = (name: string, edit: (d: any) => void) => deriveScene(name, edit, artifacts);

test.setTimeout(15 * 60_000);
test.describe.configure({ mode: "serial" });

/** the template, re-pointed at this machine's data and edited by `edit` */
/** content on screen (not the splash), then two near-identical frames in a row */
async function settled(page: Page, name: string): Promise<Buffer> {
    const started = Date.now();
    let shot = await page.screenshot();
    while ((!streamLive(shot) || litFraction(shot) < 0.003) && Date.now() - started < 600_000) {
        await page.waitForTimeout(3000);
        shot = await page.screenshot();
    }
    expect(
        litFraction(shot),
        `render view still empty after ${Math.round((Date.now() - started) / 1000)}s (${name})`
    ).toBeGreaterThan(0.003);
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
    fs.writeFileSync(path.join(artifacts, name), prev);
    return prev;
}

async function openRender(browser: Browser, app: Pro3d) {
    const context = await browser.newContext();
    context.on("weberror", (e) => console.log("[page error]", e.error()));
    const render = await context.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    return { context, render };
}

async function openGis(context: import("@playwright/test").BrowserContext, app: Pro3d) {
    const gis = await context.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    return gis;
}

/** the map view button of the tool strip: "" when enabled, "disabled" otherwise,
 *  "active" when it is the current mode */
async function mapTool(render: Page): Promise<{ found: boolean; enabled: boolean; active: boolean }> {
    return render.evaluate(() => {
        const icon = Array.from(document.querySelectorAll("i")).find(
            (i) => i.className.trim() === "map icon"
        );
        const tool = icon?.closest(".pro3d-tool");
        if (!tool) return { found: false, enabled: false, active: false };
        return {
            found: true,
            enabled: !tool.classList.contains("disabled"),
            active: tool.classList.contains("active"),
        };
    });
}

async function clickMapTool(render: Page) {
    const r = await render.evaluate(() => {
        const icon = Array.from(document.querySelectorAll("i")).find(
            (i) => i.className.trim() === "map icon"
        );
        const tool = icon?.closest(".pro3d-tool") as HTMLElement | null;
        if (!tool) return "no map tool";
        tool.click();
        return "clicked";
    });
    expect(r).toBe("clicked");
}

/** text of the cell next to a label in the GIS panel's observation table */
async function besideLabel(gis: Page, label: string): Promise<string> {
    return gis.evaluate((l) => {
        const el = Array.from(document.querySelectorAll("td, div, span")).find(
            (e) => (e.textContent ?? "").trim() === l
        );
        const row = el?.closest("tr");
        const cells = row ? Array.from(row.querySelectorAll("td")) : [];
        return cells.length > 1 ? (cells[cells.length - 1].textContent ?? "").trim() : "";
    }, label);
}

/** the selected option of the <select> next to a label */
async function selectedBeside(gis: Page, label: string): Promise<string> {
    return gis.evaluate((l) => {
        const el = Array.from(document.querySelectorAll("td, div, span")).find(
            (e) => (e.textContent ?? "").trim() === l
        );
        const sel = el?.closest("tr")?.querySelector("select") as HTMLSelectElement | null;
        if (!sel) return `no select beside ${l}`;
        return (sel.options[sel.selectedIndex]?.textContent ?? "").trim();
    }, label);
}

/** a drag across the middle of the render view */
async function drag(render: Page, dx: number, dy: number) {
    const vp = render.viewportSize() ?? { width: 1600, height: 900 };
    const x = vp.width / 2, y = vp.height / 2;
    await render.mouse.move(x, y);
    await render.mouse.down();
    for (let i = 1; i <= 10; i++) await render.mouse.move(x + (dx * i) / 10, y + (dy * i) / 10);
    await render.mouse.up();
}

test("a scene set up in the GIS view only gets its planet on load, and map view works", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");
    const scene = derivedScene("gis-only", (d) => {
        d.gisApp.gisSurfaces = [];
        d.gisApp.defaultObservationInfo.observer = { EntitySpiceName: "Dimorphos" };
        d.gisApp.defaultObservationInfo.referenceFrame = { FrameSpiceName: "DIMORPHOS_FIXED" };
        d.referenceSystem.planet = 2; // Planet.None, as saved by a GIS-only setup
        d.navigationMode = 0;         // FreeFly
    });
    const app = await launchPro3d(scene);
    try {
        const { context, render } = await openRender(browser, app);
        const loaded = await settled(render, "gis-only-loaded.png");

        // loading filled in the planet from the body-fixed observation
        await expect.poll(() => overlayPlanet(render), { timeout: 30_000 }).toBe("Dimorphos");
        expect(fs.readFileSync(app.logFile, "utf-8")).toContain("[SceneBodySync] scene observes");
        const tool = await mapTool(render);
        expect(tool.found, "the tool strip has a map view button").toBe(true);
        expect(tool.enabled, "map view needs a planet and now has one").toBe(true);

        // the GIS view shows the frame as the body's fixed frame, and the unbound
        // surface as belonging to the scene body
        const gis = await openGis(context, app);
        await expect.poll(() => besideLabel(gis, "Reference Frame:")).toBe("DIMORPHOS_FIXED (body-fixed)");
        await gis.locator("text=Surfaces").first().click();
        await expect(gis.locator("option", { hasText: "Scene body (Dimorphos)" }).first()).toBeAttached({ timeout: 30_000 });
        await gis.waitForTimeout(1500); // the accordion animates open
        await gis.screenshot({ path: path.join(artifacts, "gis-only-gis.png"), fullPage: true });

        // map view: switch, the body stays in view, a drag moves over it
        await clickMapTool(render);
        await expect.poll(async () => (await mapTool(render)).active, { timeout: 30_000 }).toBe(true);
        const map = await settled(render, "gis-only-mapview.png");
        expect(litFraction(map), "the body is in view in map view").toBeGreaterThan(0.003);
        await drag(render, 250, 0);
        const dragged = await settled(render, "gis-only-mapview-dragged.png");
        const d = diffPng(map, dragged);
        console.log(`map view drag: ${(d.changedFraction * 100).toFixed(2)}% pixels changed`);
        expect(d.changedFraction, "a drag in map view moves the view").toBeGreaterThan(0.01);
        expect(litFraction(dragged), "the body is still in view after the drag").toBeGreaterThan(0.003);
        expect(diffPng(loaded, map).changedFraction, "map view re-aimed the camera").toBeGreaterThan(0.001);
        await context.close();
    } finally {
        await app.stop();
    }
});

test("a scene saved in J2000 loads unchanged and switches to body-fixed on request", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");
    // the template as it is: Dimorphos observed in J2000, surface bound, planet None
    const scene = derivedScene("j2000", (d) => {
        d.navigationMode = 0;
    });
    const app = await launchPro3d(scene);
    try {
        const { context, render } = await openRender(browser, app);
        const before = await settled(render, "j2000-loaded.png");

        // unchanged: no planet, so no map view -- world is J2000, not body-fixed
        await expect.poll(() => overlayPlanet(render), { timeout: 30_000 }).toBe("None xyz");
        expect((await mapTool(render)).enabled, "map view stays off for a J2000 world").toBe(false);
        expect(fs.readFileSync(app.logFile, "utf-8")).not.toContain("[SceneBodySync] scene observes");

        // the GIS view says so and offers the switch
        const gis = await openGis(context, app);
        await expect(gis.locator("text=/J2000 is not the body-fixed frame of Dimorphos/").first()).toBeVisible({ timeout: 30_000 });
        await gis.screenshot({ path: path.join(artifacts, "j2000-gis.png"), fullPage: true });
        await gis.locator("button", { hasText: "Use DIMORPHOS_FIXED" }).first().click();

        // now body-fixed: the planet follows, map view is on
        await expect.poll(() => besideLabel(gis, "Reference Frame:"), { timeout: 30_000 }).toBe("DIMORPHOS_FIXED (body-fixed)");
        await expect.poll(() => overlayPlanet(render), { timeout: 30_000 }).toBe("Dimorphos");
        expect((await mapTool(render)).enabled, "map view is available once body-fixed").toBe(true);
        expect(fs.readFileSync(app.logFile, "utf-8")).toContain("[SceneBodySync] GIS observation");

        // the world turned from J2000 into the body-fixed frame under the camera
        const after = await settled(render, "j2000-switched.png");
        const d = diffPng(before, after);
        console.log(`J2000 -> DIMORPHOS_FIXED: ${(d.changedFraction * 100).toFixed(2)}% pixels changed`);
        expect(d.changedFraction, "the surface is placed in the new frame").toBeGreaterThan(0.001);
        await context.close();
    } finally {
        await app.stop();
    }
});

test("picking the planet in the top bar sets up the GIS observation", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");
    // a scene that never touched the GIS view: planet Mars (the default), no observation
    const scene = derivedScene("planet-only", (d) => {
        d.gisApp.gisSurfaces = [];
        d.gisApp.defaultObservationInfo.observer = null;
        d.gisApp.defaultObservationInfo.referenceFrame = null;
        d.gisApp.defaultObservationInfo.target = null;
        d.referenceSystem.planet = 1; // Planet.Mars
        d.navigationMode = 0;
    });
    const app = await launchPro3d(scene);
    try {
        const { context, render } = await openRender(browser, app);
        await settled(render, "planet-only-loaded.png");
        await expect.poll(() => overlayPlanet(render), { timeout: 30_000 }).toContain("Mars");

        // a planet with the GIS observing nothing is not a scene body yet: the GIS panel
        // offers the one click that makes it one (and the scene stays as it is otherwise)
        const gisBefore = await openGis(context, app);
        await expect(
            gisBefore.locator("button", { hasText: "Observe Mars as scene body (IAU_MARS)" }).first()
        ).toBeVisible({ timeout: 30_000 });
        await gisBefore.screenshot({ path: path.join(artifacts, "planet-only-offer.png"), fullPage: true });
        await gisBefore.close();

        // the main page carries the top bar with the reference-system dropdown
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");
        await expect
            .poll(
                () =>
                    main.evaluate(() => {
                        const sel = Array.from(document.querySelectorAll("select")).find((s) =>
                            Array.from(s.options).some((o) => (o.textContent ?? "").trim() === "Dimorphos")
                        );
                        if (!sel) return "no planet select";
                        const opt = Array.from(sel.options).find((o) => (o.textContent ?? "").trim() === "Dimorphos")!;
                        sel.value = opt.value;
                        sel.dispatchEvent(new Event("change", { bubbles: true }));
                        return "ok";
                    }),
                { timeout: 60_000 }
            )
            .toBe("ok");

        await expect.poll(() => overlayPlanet(render), { timeout: 30_000 }).toBe("Dimorphos");

        // the GIS observation followed: Dimorphos, in its fixed frame
        const gis = await openGis(context, app);
        await expect.poll(() => selectedBeside(gis, "Observed body:"), { timeout: 30_000 }).toBe("Dimorphos");
        await expect.poll(() => besideLabel(gis, "Reference Frame:")).toBe("DIMORPHOS_FIXED (body-fixed)");
        await expect(gis.locator("button", { hasText: "as scene body" })).toHaveCount(0);
        await gis.locator("text=Surfaces").first().click();
        await expect(gis.locator("option", { hasText: "Scene body (Dimorphos)" }).first()).toBeAttached({ timeout: 30_000 });
        await gis.waitForTimeout(1500); // the accordion animates open
        await gis.screenshot({ path: path.join(artifacts, "planet-only-gis.png"), fullPage: true });
        expect((await mapTool(render)).enabled).toBe(true);
        await context.close();
    } finally {
        await app.stop();
    }
});
