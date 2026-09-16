import { test, expect, Page, Browser, BrowserContext } from "@playwright/test";
import { launchPro3d, Pro3d, fixture, sceneFor, surfaceShadersReady } from "../src/pro3d";
import { diffPng, litFraction, streamLive } from "../src/image";
import { overlayPlanet } from "../src/viewer";
import * as fs from "fs";
import * as path from "path";

/**
 * Multi-attribute profile export -- docs/MultiAttributeProfile.md.
 *
 * Draws a Line annotation with Projection.Sky across the Dimorphos OPC and exports it
 * through Annotations -> Export... as a CSV with one row per point and the OPC's layers
 * sampled under each of them.
 *
 * Worth an end-to-end test rather than an Expecto case because every step is a different
 * subsystem meeting the others: Sky drapes the line by ray casting against the KdTrees,
 * the export re-picks every one of those points to read the .aara layers, and the schema
 * is assembled from settings spread over three widgets. An exporter unit test proves the
 * writer; only this proves that drawing on a real surface yields a file with real numbers.
 *
 * The old "selected as multi-attribute profile" menu command is gone. It is now the
 * Profile preset (CSV + one record per point + scope Selected) plus the Surface
 * properties checkbox -- see docs/AnnotationExport.md.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "profile-export");
/** PRO3D_DOC_SHOTS=1 also refreshes the images used by docs/MultiAttributeProfile.md. */
const docImages = path.join(__dirname, "..", "..", "docs", "images");
const writeDocShots = process.env.PRO3D_DOC_SHOTS === "1";

/** Layers this OPC ships as .aara, i.e. surface_ columns the export must produce. */
const EXPECTED_LAYERS = ["Elevation", "Gravity", "Slope"];

/** Camera distance from the body centre, chosen together with the frustum below.
 *  With PRo3D's normal 60 deg camera an ~160 m body spans about 160/dist radians, so 350 m
 *  puts it across roughly 700 px of a 1600 px view -- enough to pick two points well apart
 *  on it without magnifying the 1.96 m/px texture into a blur. */
const CAMERA_DISTANCE = 350;

test.setTimeout(20 * 60_000);
test.describe.configure({ mode: "serial" });

/** the projection test scene, re-pointed at this machine's data and edited by `edit` */
function derivedScene(name: string, edit: (d: any) => void): string {
    fs.mkdirSync(artifacts, { recursive: true });
    const out = path.join(artifacts, `${name}.pro3d`);
    sceneFor(fixture.sceneTemplate, fixture.opc, out);
    const d = JSON.parse(fs.readFileSync(out, "utf-8"));
    edit(d);
    fs.writeFileSync(out, JSON.stringify(d, null, 2));
    return out;
}

/** Saves into artifacts/, and into docs/images as well under PRO3D_DOC_SHOTS=1. */
function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
    if (writeDocShots) {
        fs.mkdirSync(docImages, { recursive: true });
        fs.writeFileSync(path.join(docImages, `multiAttributeProfile-${name}`), png);
    }
}

/**
 * Fraction of the frame taken by the AARDVARK loading banner's yellow-green.
 *
 * The shared gate (`streamLive` + `litFraction`) does not reject that splash here:
 * `litFraction` measures the centre of the frame, which is exactly where the bright
 * banner sits, and the tool strip down the right edge keeps a corner pixel above
 * `streamLive`'s threshold. So an unloaded view scored as "lit and stable" and the test
 * went on to read a model that had not finished loading. Rejecting the splash by its own
 * colour is what makes "the surface is up" mean that.
 */
function splashFraction(buf: Buffer): number {
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

/** the surface on screen (not the splash), then two near-identical frames in a row */
async function settled(page: Page, name: string): Promise<Buffer> {
    const started = Date.now();
    const loading = (b: Buffer) =>
        !streamLive(b) || litFraction(b) < 0.003 || splashFraction(b) > 0.002;
    let shot = await page.screenshot();
    while (loading(shot) && Date.now() - started < 900_000) {
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
async function tool(render: Page, title: string) {
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

async function clickTool(render: Page, title: string) {
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
async function selectByOption(page: Page, option: string, value: string, root = "") {
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

/** The Electron save dialog cannot be driven from Chromium, so it is stubbed to answer
 *  with `filePath` -- the same shape the import specs use for showOpenDialog. */
async function stubSaveDialog(main: Page, filePath: string) {
    await main.evaluate((p) => {
        const w = window as any;
        w.aardvark = w.aardvark ?? {};
        w.aardvark.dialog = { showSaveDialog: (_o: unknown) => Promise.resolve({ filePath: p }) };
    }, filePath.replace(/\\/g, "/"));
}

/** Opens a top-menu dropdown and clicks one of its items. */
async function menu(main: Page, dropdown: string, item: string) {
    const r = await main.evaluate(
        ({ d, i }) => {
            const dd = Array.from(document.querySelectorAll(".ui.dropdown.item")).find((e) =>
                (e.textContent ?? "").startsWith(d)
            ) as HTMLElement | null;
            if (!dd) return `no ${d} dropdown`;
            dd.click();
            const entry = Array.from(dd.querySelectorAll(".item")).find(
                (e) => (e.textContent ?? "").trim() === i
            ) as HTMLElement | null;
            if (!entry) return `no ${i} item`;
            entry.click();
            return "clicked";
        },
        { d: dropdown, i: item }
    );
    expect(r, `${dropdown} -> ${item}`).toBe("clicked");
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
async function awaitIdle(page: Page, app: Pro3d, quietMs = 4000, timeoutMs = 300_000) {
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
async function pick(render: Page, x: number, y: number) {
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
async function annotationCount(context: BrowserContext, app: Pro3d): Promise<number> {
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

test("a sky-projected line exports as a multi-attribute profile CSV", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    // Dimorphos as the scene body. Without one the export still writes, but lat/lon/alt
    // and groundDistance come out empty (Planet.None has no geographic frame) -- and
    // groundDistance is exactly the x-axis of a profile, so the file would be half a one.
    //
    // Setting referenceSystem.planet alone does NOT survive the load: the template
    // observes Dimorphos in J2000, which is not a body-fixed frame, so the scene-body
    // sync puts the planet back to None (scene-body.spec.ts covers that case). Giving it
    // the body-fixed observation is what makes the load derive the planet.
    const scene = derivedScene("profile", (d) => {
        d.gisApp.gisSurfaces = [];
        d.gisApp.defaultObservationInfo.observer = { EntitySpiceName: "Dimorphos" };
        d.gisApp.defaultObservationInfo.referenceFrame = { FrameSpiceName: "DIMORPHOS_FIXED" };
        d.referenceSystem.planet = 2; // Planet.None, filled in on load
        d.navigationMode = 0; // FreeFly
        // The template is a projection-test scene, so it carries the HERA AFC INSTRUMENT
        // frustum (hfov 5.53 deg, focal 122.563) rather than a viewing camera. Keep it and
        // the annotation selection outline is drawn ~10x too large, because
        // Utilities.drawSpheresFast converts pixels to world units as dist/viewportWidth,
        // omitting the field of view -- correct only near hfov 53 deg (issue #770). A profile workflow is not an instrument
        // simulation, so use PRo3D's normal 60 deg frustum and the harness then renders what
        // the viewer renders.
        // toggleFocal must go off as well: with it on, PRo3D recomputes the frustum from
        // `focal` (122.563 mm, the AFC lens) on load and overwrites whatever frustum the
        // scene stores.
        const fm = d.config?.frustumModel;
        if (fm?.frustumOld) {
            fm.frustum = { ...fm.frustumOld, far: fm.frustum.far };
            fm.toggleFocal = false;
            fm.focal = 10.25; // FrustumModel.focal default -> hfov 2*atan(11.84/(2*focal)) = 60 deg
        }
        // cameraView.view is [Sky, Location, Forward, Up, Right]; Forward already points
        // at the body centre, so scaling Location moves in without re-aiming.
        const loc = JSON.parse(d.cameraView.view[1]) as number[];
        const len = Math.hypot(loc[0], loc[1], loc[2]);
        d.cameraView.view[1] = JSON.stringify(loc.map((c) => (c / len) * CAMERA_DISTANCE));
    });

    const csv = path.join(artifacts, "dimorphos-profile.csv");
    fs.rmSync(csv, { force: true });

    const app = await launchPro3d(scene);
    let context: BrowserContext | undefined;
    try {
        context = await browser.newContext();
        context.on("weberror", (e) => console.log("[page error]", e.error()));

        const render = await context.newPage();
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        // the OPC surface effect links before anything can be drawn; on a cold shader
        // cache this is minutes of silence rather than a failure (ai/TESTING.md)
        await surfaceShadersReady(render);
        await settled(render, "1-loaded.png");

        // the geographic columns depend on this, so fail here rather than on empty cells
        await expect
            .poll(() => overlayPlanet(render), { timeout: 300_000, intervals: [5000] })
            .toBe("Dimorphos");

        // the main page carries the top menu, the annotation toolbar and the export window
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        // --- draw a sky-projected line ------------------------------------------------
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
        const toolbar = main.locator(".pro3d-secondary-toolbar").first();
        if (await toolbar.count()) save("2-toolbar.png", await toolbar.screenshot());

        // Two ctrl+clicks: Geometry.Line finishes itself at the second point
        // (Drawing-App.fs), so there is no Enter to press. Each pick ray-casts the
        // KdTrees for seconds and input is dropped meanwhile, hence the wait between.
        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);
        await awaitIdle(render, app);
        await pick(render, cx - 110, cy);
        await awaitIdle(render, app);
        await pick(render, cx + 110, cy);
        await awaitIdle(render, app);

        await expect
            .poll(() => annotationCount(context!, app), { timeout: 180_000, intervals: [5000] })
            .toBe(1);
        await settled(render, "3-annotation.png");

        // --- export -------------------------------------------------------------------
        await stubSaveDialog(main, csv);
        await menu(main, "Annotations", "Export...");
        const dimmer = ".annotation-export-dimmer";
        const panel = main.locator(`${dimmer} .ui.inverted.segment`).first();
        await expect(panel).toBeVisible({ timeout: 30_000 });

        // Profile preset: CSV, one record per point, scope Selected (the annotation just
        // drawn is the single-selected one), sampled points on, every point attribute.
        // It leaves Surface properties off -- that is the setting that makes the profile
        // multi-attribute, and it is never preset because it re-picks every point.
        await selectByOption(main, "Profile", "Profile", dimmer);
        await main.waitForTimeout(1000);

        // the Point attributes accordion only exists once the granularity is per-point,
        // which the preset has just set
        const opened = await main.evaluate((d) => {
            const title = Array.from(document.querySelectorAll(`${d} .title`)).find((t) =>
                Array.from(t.querySelectorAll("span")).some(
                    (s) => (s.textContent ?? "").trim() === "Point attributes"
                )
            ) as HTMLElement | null;
            if (!title) return "no Point attributes accordion";
            title.click();
            return "clicked";
        }, dimmer);
        expect(opened, "the Point attributes accordion").toBe("clicked");
        await main.waitForTimeout(800);

        // the checkbox is an <i> icon followed by a <span> label (AnnotationExportApp.checkBox)
        const ticked = await main.evaluate((d) => {
            const span = Array.from(document.querySelectorAll(`${d} span`)).find(
                (s) => (s.textContent ?? "").trim() === "Surface properties"
            );
            const icon = span?.previousElementSibling as HTMLElement | null;
            if (!icon) return "no Surface properties checkbox";
            const before = icon.className;
            icon.click();
            return `clicked (${before})`;
        }, dimmer);
        expect(ticked, "the Surface properties checkbox").toContain("clicked");
        await main.waitForTimeout(1000);
        save("4-export-window.png", await panel.screenshot());

        // The settings are a scrolling middle between a fixed header and footer, and
        // "Surface properties" sits at the very bottom of it -- the one setting this whole
        // page is about, and off screen in the shot above. Scroll it into view for a second
        // image rather than leaving the documentation to describe a control it never shows.
        await main.evaluate((d) => {
            const scroller = Array.from(document.querySelectorAll(`${d} div`)).find(
                (e) => (e as HTMLElement).scrollHeight > (e as HTMLElement).clientHeight + 20
            ) as HTMLElement | null;
            if (scroller) scroller.scrollTop = scroller.scrollHeight;
        }, dimmer);
        await main.waitForTimeout(600);
        save("5-surface-properties.png", await panel.screenshot());

        await main.locator(`${dimmer} .ui.primary.button`).first().click();

        // the export re-picks every sampled point against the surface, so it is seconds
        // rather than milliseconds, and PRo3D is unresponsive while it runs
        try {
            await expect
                .poll(() => fs.existsSync(csv), { timeout: 300_000, intervals: [1000] })
                .toBe(true);
        } catch (e) {
            // the window stays open with a warning when it refuses to write -- that text
            // is the diagnosis, so surface it instead of just "the file never appeared"
            const warning = await main
                .locator(`${dimmer} .warning.message`)
                .first()
                .textContent()
                .catch(() => null);
            throw new Error(`no CSV was written. Export window warning: ${warning ?? "(none)"}`);
        }

        // --- what is actually in the file ---------------------------------------------
        const lines = fs.readFileSync(csv, "utf-8").trim().split(/\r?\n/);
        const header = lines[0].split(",");
        const rows = lines.slice(1);
        console.log(`profile CSV: ${rows.length} rows, ${header.length} columns`);
        console.log(`header: ${lines[0]}`);

        for (const c of ["pointIndex", "x", "y", "z", "lat", "lon", "alt", "distance", "groundDistance"])
            expect(header, `the profile schema has ${c}`).toContain(c);

        // Sky sampling drapes the line over the surface between the two picked points.
        // Two rows would mean the projection silently fell back to a straight chord.
        expect(rows.length, "the sky projection sampled the span, not just its endpoints")
            .toBeGreaterThan(10);

        const surfaceCols = header.filter((c) => c.startsWith("surface_"));
        console.log(`surface layers sampled: ${surfaceCols.join(", ")}`);
        for (const layer of EXPECTED_LAYERS)
            expect(surfaceCols, `the OPC ships a ${layer} layer`).toContain(`surface_${layer}`);

        // A column of empty cells would satisfy "the column exists" while proving nothing:
        // the sampling is what this test is for, so require values to have arrived.
        const cell = (row: string, col: string) => row.split(",")[header.indexOf(col)] ?? "";
        for (const layer of EXPECTED_LAYERS) {
            const filled = rows.filter((r) => cell(r, `surface_${layer}`).trim() !== "").length;
            expect(filled, `surface_${layer} was sampled under the points, not left empty`)
                .toBeGreaterThan(rows.length / 2);
        }

        // geographic columns resolved through SPICE: Dimorphos is planetocentric (reclat)
        expect(cell(rows[0], "latLonAltSource"), "lat/lon came from SPICE").toBe("spice_reclat");
        expect(cell(rows[0], "body")).toBe("Dimorphos");

        // distance runs through 3D space, groundDistance has the height removed, so the
        // former can never be shorter -- and both must actually advance along the line
        const last = rows[rows.length - 1];
        const d = Number(cell(last, "distance"));
        const g = Number(cell(last, "groundDistance"));
        expect(d, "the profile has a length").toBeGreaterThan(0);
        expect(d, "distance includes the climb groundDistance drops").toBeGreaterThanOrEqual(g - 1e-6);

        // KNOWN DEFECT, asserted as it currently behaves so that fixing it trips this line.
        //
        // groundDistance is 0 on EVERY row for Dimorphos, and 0 is not "missing" -- it is a
        // plausible number that silently collapses the x-axis of any profile plotted against
        // it. Cause: AnnotationExport.flatten drops the height by setting altitude = 0 and
        // transforming back, but Dimorphos uses the Spherical convention
        // (CooTransformation.getConvention -> Spherical 77.1667) where altitude IS the radial
        // distance from the body centre, and xyzFromLatLonAltOnSphere takes `r = sc.altitude`
        // while ignoring the mean radius. Altitude 0 therefore maps every point onto the body
        // centre, so consecutive flattened points coincide and nothing accumulates. Flattening
        // such a body needs altitude = meanRadius, not 0.
        //
        // Applies to every Spherical-convention body; Planetographic ones (Mars, Earth, Moon,
        // Phobos, Deimos, Didymos) measure a real horizontal run. See docs/MultiAttributeProfile.md.
        expect(g, "groundDistance collapses to 0 on a Spherical-convention body").toBe(0);

        // a stub of the real file, for the documentation
        fs.writeFileSync(
            path.join(artifacts, "dimorphos-profile.stub.csv"),
            [lines[0], ...rows.slice(0, 5)].join("\n") + "\n"
        );
    } finally {
        await context?.close();
        await app.stop();
    }
});
