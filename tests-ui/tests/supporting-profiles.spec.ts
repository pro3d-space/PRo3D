import { test, expect, BrowserContext, Page } from "@playwright/test";
import { fixture, Pro3d } from "../src/pro3d";
import { overlayPlanet } from "../src/viewer";
import {
    annotationCount,
    awaitIdle,
    clickTool,
    drawingScene,
    openDrawingViewer,
    pick,
    selectByOption,
    settled,
} from "../src/drawing";
import { clickInExportWindow, exportDimmer, menu, readCsv, runExport, stubSaveDialog } from "../src/export";
import * as fs from "fs";
import * as path from "path";

/**
 * Supporting profiles (#833) -- docs/SupportingProfiles.md.
 *
 * On the Dimorphos OPC, with Dimorphos as the reference body, as a user does it:
 *
 *  1. draw an AxisEllipse (a crater), open Properties -> Supporting Profiles, Generate:
 *     four compass profiles through it;
 *  2. draw a Sky polyline, set spacing and length in the same panel, Generate: one profile
 *     every `spacing` metres;
 *  3. export with the *Craters* preset: the crater row and its four profiles, per point, in
 *     a second file, joined by parentKey;
 *  4. export with the *Lineaments* preset: the polyline's row (not its profiles, which are
 *     lines too), and its profiles per point in a second file, with parent and station.
 *
 * The geometry itself (perpendicularity, rim intersection, all-or-nothing) is proven exactly in
 * Section22_SupportingProfiles.fs on a synthetic plane; this spec proves the workflow and the
 * files on real terrain.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "supporting-profiles");

const SPACING = 10;
const LENGTH = 8;

test.setTimeout(40 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

/** Opens the accordion titled `title` on `page` and checks its content really shows: a click
 *  on a title that did not open it would otherwise pass silently (ai/TESTING.md). */
async function openAccordion(page: Page, title: string, content: string) {
    const r = await page.evaluate((t) => {
        const el = Array.from(document.querySelectorAll(".title")).find(
            (e) => (e.textContent ?? "").trim() === t
        ) as HTMLElement | undefined;
        if (!el) return "no title";
        if (!el.classList.contains("active")) el.click();
        return "clicked";
    }, title);
    expect(r, `accordion ${title}`).toBe("clicked");
    await expect(page.locator(content).first()).toBeVisible({ timeout: 30_000 });
}

/** The supporting-profiles panel's last message (Drawing.UI.viewSupportingProfiles). */
async function panelMessage(props: Page): Promise<string> {
    return props.evaluate(
        () => (document.querySelector(".pro3d-profiles-message")?.textContent ?? "").trim()
    );
}

/** Clicks Generate in the panel and returns how many profiles it reports. Waits for the
 *  message to *change*: the previous run's message is still standing when the next starts.
 *  Then waits for the annotation list to have grown by that many. */
async function generate(context: BrowserContext, app: Pro3d, props: Page, render: Page): Promise<number> {
    const button = props.locator(".pro3d-generate-profiles");
    await expect(button, "the panel offers Generate for the selected annotation").toBeVisible({ timeout: 60_000 });
    const before = await annotationCount(context, app);
    const previous = await panelMessage(props);
    await button.click();
    await awaitIdle(render, app);
    await expect
        .poll(() => panelMessage(props), { timeout: 120_000, intervals: [1000] })
        .not.toBe(previous);
    const message = await panelMessage(props);
    console.log(`panel: ${message}`);
    expect(message, "generation succeeded").not.toContain("No profiles generated");
    const count = Number(/(\d+) profiles generated/.exec(message)?.[1] ?? "0");
    await expect
        .poll(() => annotationCount(context, app), { timeout: 120_000, intervals: [2000] })
        .toBe(before + count);
    return count;
}

/** Sets the n-th number input of the supporting-profiles panel. */
async function setPanelNumber(props: Page, n: number, value: number) {
    const input = props.locator(".pro3d-supporting-profiles input[type=number]").nth(n);
    await input.fill(String(value));
    await input.press("Enter");
    await props.waitForTimeout(800);
}

/** Polyline + Sky in the toolbar, and a check that Sky took: a change sent while the toolbar is
 *  still re-rendering for the new geometry can be lost (one run drew its polylines Linear). */
async function selectSkyPolyline(main: Page) {
    const projectionShown = () =>
        main.evaluate(() => {
            const sel = Array.from(document.querySelectorAll("select")).find((s) =>
                Array.from(s.options).some((o) => (o.textContent ?? "").trim() === "Viewpoint")
            );
            return sel ? sel.value : "(none)";
        });
    await selectByOption(main, "Polyline", "Polyline");
    await main.waitForTimeout(800);
    await selectByOption(main, "Sky", "Sky");
    await expect.poll(projectionShown, { timeout: 30_000, intervals: [500] }).toBe("Sky");
}

/** Groups CSV rows by the value of `col`, keeping row order. */
function groupBy(rows: string[][], key: (r: string[]) => string) {
    const groups = new Map<string, string[][]>();
    for (const r of rows) {
        const k = key(r);
        groups.set(k, [...(groups.get(k) ?? []), r]);
    }
    return groups;
}

test("profiles generated from a crater and a polyline, exported with the Craters preset", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    const scene = drawingScene(artifacts, "supporting-profiles");
    const cratersCsv = path.join(artifacts, "craters.csv");
    const profilesCsv = path.join(artifacts, "craters_profiles.csv");
    const lineamentsCsv = path.join(artifacts, "lineaments.csv");
    const lineamentProfilesCsv = path.join(artifacts, "lineaments_profiles.csv");
    for (const f of [cratersCsv, profilesCsv, lineamentsCsv, lineamentProfilesCsv]) fs.rmSync(f, { force: true });

    const { app, context, render } = await openDrawingViewer(browser, scene, "1-loaded.png", save);
    try {
        // compass directions and lat/lon need the body
        await expect
            .poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("Dimorphos");

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        const props = await context.newPage();
        await props.goto(app.url + "?page=properties");
        await props.waitForLoadState("domcontentloaded");

        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);

        // --- 1. a crater -----------------------------------------------------------------
        await clickTool(render, "Draw annotation");
        await selectByOption(main, "AxisEllipse", "AxisEllipse");
        const thickness = main.locator(".pro3d-secondary-toolbar input[type=number]").first();
        await thickness.fill("1");
        await thickness.press("Enter");
        await main.waitForTimeout(800);

        // ends of the major axis, then the semi-minor; centred above the view's middle so
        // the polyline below has room (the body spans ~700 px at CAMERA_DISTANCE)
        const ey = cy - 90;
        for (const p of [
            { x: cx - 95, y: ey + 40 },
            { x: cx + 95, y: ey - 40 },
            { x: cx + 25, y: ey + 60 },
        ]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await awaitIdle(render, app);
        await expect
            .poll(() => annotationCount(context, app), { timeout: 180_000, intervals: [5000] })
            .toBe(1);

        // the finished ellipse is the selected annotation; the panel offers its directions
        await openAccordion(props, "Supporting Profiles", ".pro3d-supporting-profiles");
        await expect(props.locator(".pro3d-supporting-profiles select")).toBeVisible({ timeout: 60_000 });
        save("2-panel-ellipse.png", await props.locator(".pro3d-supporting-profiles").screenshot());
        expect(await generate(context, app, props, render), "four compass profiles").toBe(4);
        await settled(render, "3-crater-profiles.png", save);

        // --- 2. a polyline ---------------------------------------------------------------
        await selectSkyPolyline(main);
        const py = cy + 150;
        for (const p of [
            { x: cx - 130, y: py },
            { x: cx - 20, y: py + 30 },
            { x: cx + 100, y: py - 10 },
        ]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await awaitIdle(render, app);
        // Enter finishes a polyline (Viewer.getDrawingActionForKey); the render control
        // holds the focus after the picks
        await render.bringToFront();
        await render.mouse.move(cx, py);
        await render.keyboard.press("Enter");
        await awaitIdle(render, app);
        await expect
            .poll(() => annotationCount(context, app), { timeout: 120_000, intervals: [2000] })
            .toBe(6);

        // the panel follows the selection: now spacing and length
        await expect(props.locator(".pro3d-supporting-profiles input[type=number]")).toHaveCount(2, {
            timeout: 60_000,
        });
        await setPanelNumber(props, 0, SPACING);
        await setPanelNumber(props, 1, LENGTH);
        const polyCount = await generate(context, app, props, render);
        // after generating, so the message under the button is about this polyline
        save("4-panel-polyline.png", await props.locator(".pro3d-supporting-profiles").screenshot());
        expect(polyCount, "at least start, one step and the end").toBeGreaterThanOrEqual(3);
        await render.mouse.move(cx, vp.height - 40);
        await settled(render, "5-all-profiles.png", save);

        // --- 3. Craters export -----------------------------------------------------------
        await stubSaveDialog(main, cratersCsv);
        await menu(main, "Annotations", "Export...");
        const panel = main.locator(`${exportDimmer} .ui.inverted.segment`).first();
        await expect(panel).toBeVisible({ timeout: 30_000 });
        await selectByOption(main, "Craters (ellipses + profiles)", "Craters (ellipses + profiles)", exportDimmer);
        await main.waitForTimeout(1000);
        // the preset as it comes, before the checkbox below turns it into Custom
        save("6-export-window.png", await panel.screenshot());
        // opt-in, as in a Profile export: the OPC layers under every profile point
        expect(
            await clickInExportWindow(main, "checkbox", "Surface properties in <file>_profiles.csv (re-picks every point, slower)")
        ).toBe("clicked");
        await main.waitForTimeout(1000);
        await runExport(main, [cratersCsv, profilesCsv], fs.existsSync);

        const craters = readCsv(fs.readFileSync(cratersCsv, "utf-8"));
        console.log(`craters: ${craters.rows.length} rows; header ${craters.header.join(",")}`);
        expect(craters.rows.length, "one crater: the profiles are lines, not ellipses").toBe(1);
        const craterKey = craters.cell(craters.rows[0], "key");
        expect(Number(craters.cell(craters.rows[0], "semiMajorAxis")), "Boulders columns").toBeGreaterThan(0);

        const prof = readCsv(fs.readFileSync(profilesCsv, "utf-8"));
        console.log(`profiles: ${prof.rows.length} rows; header ${prof.header.join(",")}`);
        for (const c of ["key", "parentKey", "profileIndex", "profileDirection", "profileAzimuth",
                         "profileStation", "pointIndex", "lat", "lon", "alt", "distance"])
            expect(prof.header, `the profiles file has ${c}`).toContain(c);
        for (const r of prof.rows) expect(r.length, "every row has every column").toBe(prof.header.length);

        // only the crater's profiles: the polyline's belong to no exported crater
        for (const r of prof.rows) expect(prof.cell(r, "parentKey"), "joined to the crater").toBe(craterKey);
        const byProfile = groupBy(prof.rows, (r) => prof.cell(r, "key"));
        expect(byProfile.size, "four profiles").toBe(4);

        const expected: Record<string, number> = { "N-S": 0, "NE-SW": 45, "E-W": 90, "NW-SE": 315 };
        const seen = new Set<string>();
        for (const [key, rows] of byProfile) {
            const dir = prof.cell(rows[0], "profileDirection");
            seen.add(dir);
            expect(Object.keys(expected), `${key}: a compass label`).toContain(dir);
            for (const r of rows) {
                expect(prof.cell(r, "profileDirection"), "one direction per profile").toBe(dir);
                expect(prof.cell(r, "profileStation"), "ellipse profiles have no station").toBe("");
            }
            // the bearing from the centre to the start end is the label's
            const az = Number(prof.cell(rows[0], "profileAzimuth"));
            const diff = Math.abs(((az - expected[dir] + 540) % 360) - 180);
            expect(diff, `${dir}: azimuth ${az}`).toBeLessThan(1);
            // a profile across the crater, sampled at the drawing tool's spacing
            expect(rows.length, `${dir}: sampled, not just its two ends`).toBeGreaterThan(5);
            expect(Number(prof.cell(rows[0], "pointIndex")), `${dir} starts at point 0`).toBe(0);
            expect(Number(prof.cell(rows[0], "distance")), `${dir} starts at distance 0`).toBe(0);
            const distances = rows.map((r) => Number(prof.cell(r, "distance")));
            for (let i = 1; i < distances.length; i++)
                expect(distances[i], `${dir}: distance never decreases`).toBeGreaterThanOrEqual(distances[i - 1]);
            const alts = new Set(rows.map((r) => prof.cell(r, "alt")));
            expect(alts.size, `${dir}: the terrain shows in alt`).toBeGreaterThan(rows.length / 2);
            expect(prof.cell(rows[0], "body")).toBe("Dimorphos");
            // surface properties: filled on every point and varying along the profile
            for (const layer of ["Elevation", "Slope"]) {
                const col = `surface_${layer}`;
                expect(prof.header, `the profiles file has ${col}`).toContain(col);
                const values = rows.map((r) => prof.cell(r, col));
                expect(values.filter((v) => v.trim() !== "").length, `${dir}: ${col} on every point`).toBe(rows.length);
                expect(new Set(values).size, `${dir}: ${col} varies`).toBeGreaterThan(rows.length / 2);
            }
        }
        expect([...seen].sort(), "all four directions").toEqual(Object.keys(expected).sort());

        // the start ends: N-S starts north of where it ends, E-W east of where it ends
        const ends = (dir: string) => {
            const rows = [...byProfile.values()].find((rs) => prof.cell(rs[0], "profileDirection") === dir)!;
            const first = rows[0], last = rows[rows.length - 1];
            return {
                dLat: Number(prof.cell(first, "lat")) - Number(prof.cell(last, "lat")),
                dLon: Number(prof.cell(first, "lon")) - Number(prof.cell(last, "lon")),
            };
        };
        console.log(`N-S ends ${JSON.stringify(ends("N-S"))}, E-W ends ${JSON.stringify(ends("E-W"))}`);
        expect(ends("N-S").dLat, "N-S starts at the northern rim").toBeGreaterThan(0);

        // --- 4. Lineaments export: the polyline, and its profiles beside it --------------
        await stubSaveDialog(main, lineamentsCsv);
        await menu(main, "Annotations", "Export...");
        await expect(panel).toBeVisible({ timeout: 30_000 });
        await selectByOption(main, "Lineaments (polylines + profiles)", "Lineaments (polylines + profiles)", exportDimmer);
        await main.waitForTimeout(1000);
        save("7-export-lineaments.png", await panel.screenshot());
        await runExport(main, [lineamentsCsv, lineamentProfilesCsv], fs.existsSync);

        const lineaments = readCsv(fs.readFileSync(lineamentsCsv, "utf-8"));
        console.log(`lineaments: ${lineaments.rows.length} rows; header ${lineaments.header.join(",")}`);
        // the generated profiles are lines too, but not lineaments; the ellipse is not a line
        expect(lineaments.rows.length, "one lineament: the polyline").toBe(1);
        const parentKey = lineaments.cell(lineaments.rows[0], "key");
        expect(Number(lineaments.cell(lineaments.rows[0], "wayLength")), "a measured polyline").toBeGreaterThan(SPACING);

        const poly = readCsv(fs.readFileSync(lineamentProfilesCsv, "utf-8"));
        for (const r of poly.rows) expect(poly.cell(r, "parentKey"), "joined to the lineament").toBe(parentKey);
        const polyProfiles = groupBy(poly.rows, (r) => poly.cell(r, "key"));
        expect(polyProfiles.size, "every polyline profile is in the file").toBe(polyCount);
        const stations = [...polyProfiles.values()]
            .map((rs) => Number(poly.cell(rs[0], "profileStation")))
            .sort((a, b) => a - b);
        console.log(`polyline stations: ${stations.map((s) => s.toFixed(2)).join(", ")}`);
        expect(stations[0], "the first profile at the start").toBe(0);
        for (let i = 1; i < stations.length - 1; i++)
            expect(stations[i] - stations[i - 1], "one every spacing").toBeCloseTo(SPACING, 6);
        expect(stations[stations.length - 1] - stations[stations.length - 2], "the last one at the end")
            .toBeLessThanOrEqual(SPACING + 1e-6);
        for (const [, rows] of polyProfiles) {
            expect(poly.cell(rows[0], "profileDirection"), "polyline profiles have no compass label").toBe("");
            const length = Number(poly.cell(rows[rows.length - 1], "distance"));
            // draped over the terrain, so a little longer than the straight 8 m
            expect(length, "about the requested length").toBeGreaterThan(LENGTH * 0.95);
            expect(length).toBeLessThan(LENGTH * 1.6);
        }
    } finally {
        await context.close();
        await app.stop();
    }
});

/** Ticks a toolbar checkbox, found by its tooltip (UI.wrapToolTip puts it in `title`):
 *  that both finds the right one and proves the tooltip is there. */
async function tickToolbar(main: Page, tooltip: string) {
    const iconClass = () =>
        main.evaluate((t) => document.querySelector(`[title="${t}"] i`)?.className ?? "missing", tooltip);
    await expect.poll(iconClass, { timeout: 60_000, intervals: [500] }).toContain("square outline icon");
    expect(await iconClass(), `"${tooltip}" starts unticked`).not.toContain("check");
    await main.evaluate((t) => (document.querySelector(`[title="${t}"] i`) as HTMLElement).click(), tooltip);
    await expect.poll(iconClass, { timeout: 30_000, intervals: [500] }).toContain("check square");
}

test("toolbar: Lineament and Crater profiles are generated as soon as the annotation is finished", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    const scene = drawingScene(artifacts, "supporting-profiles-toolbar");
    const { app, context, render } = await openDrawingViewer(browser, scene, "t1-loaded.png", save);
    try {
        await expect
            .poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("Dimorphos");
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);

        await clickTool(render, "Draw annotation");

        // --- a polyline, with Lineament profiles ticked in the toolbar --------------------
        await selectSkyPolyline(main);
        await tickToolbar(main, "Generate perpendicular profiles when the polyline is finished");
        // ticking it shows spacing and length, with their tooltips
        await expect(main.locator('[title^="Profile spacing (m)"] input')).toBeVisible({ timeout: 30_000 });
        await expect(main.locator('[title^="Profile length (m)"] input')).toBeVisible();
        const toolbar = main.locator(".pro3d-secondary-toolbar").first();
        if (await toolbar.count()) save("t2-toolbar-lineament.png", await toolbar.screenshot());

        const py = cy + 150;
        for (const p of [{ x: cx - 120, y: py }, { x: cx, y: py + 25 }, { x: cx + 110, y: py - 10 }]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await awaitIdle(render, app);
        await render.bringToFront();
        await render.mouse.move(cx, py);
        await render.keyboard.press("Enter");
        await awaitIdle(render, app);
        // the polyline plus its profiles, with no Generate button pressed anywhere
        await expect
            .poll(() => annotationCount(context, app), { timeout: 120_000, intervals: [2000] })
            .toBeGreaterThanOrEqual(1 + 3);
        const afterSpacing = await annotationCount(context, app);
        console.log(`after the spacing polyline: ${afterSpacing} annotations`);

        // the drawn polyline's projection as the model has it (Properties shows it as text)
        const props = await context.newPage();
        await props.goto(app.url + "?page=properties");
        await props.waitForLoadState("domcontentloaded");
        const projectionOfSelected = () =>
            props.evaluate(() => {
                const td = Array.from(document.querySelectorAll("td")).find((t) => (t.textContent ?? "").trim() === "Projection:");
                return (td?.nextElementSibling?.textContent ?? "(none)").trim();
            });
        await expect.poll(projectionOfSelected, { timeout: 60_000, intervals: [1000] }).not.toBe("(none)");
        expect(await projectionOfSelected(), "the polyline was drawn with Sky").toBe("Sky");

        // --- a second polyline by count: 4 profiles over its whole length, none at its end --
        await selectByOption(main, "ByCount", "ByCount");
        const countInput = main.locator('[title^="Number of profiles"] input');
        await expect(countInput, "ByCount shows the count").toBeVisible({ timeout: 30_000 });
        await countInput.fill("4");
        await countInput.press("Enter");
        await main.waitForTimeout(800);
        await main.evaluate(() =>
            (document.querySelector(`[title="A profile at the polyline's last point"] i`) as HTMLElement).click()
        );
        await expect
            .poll(
                () => main.evaluate(() => document.querySelector(`[title="A profile at the polyline's last point"] i`)?.className ?? ""),
                { timeout: 30_000, intervals: [500] }
            )
            .not.toContain("check");
        if (await toolbar.count()) save("t2b-toolbar-count.png", await toolbar.screenshot());

        const qy = cy + 60;
        for (const p of [{ x: cx - 150, y: qy }, { x: cx - 30, y: qy - 20 }, { x: cx + 140, y: qy + 10 }]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await awaitIdle(render, app);
        await render.bringToFront();
        await render.mouse.move(cx, qy);
        await render.keyboard.press("Enter");
        await awaitIdle(render, app);
        await expect
            .poll(() => annotationCount(context, app), { timeout: 120_000, intervals: [2000] })
            .toBe(afterSpacing + 1 + 4);
        await props.waitForTimeout(1500);
        expect(await projectionOfSelected(), "the count polyline was drawn with Sky").toBe("Sky");
        const afterPolyline = await annotationCount(context, app);

        // --- an ellipse, with Crater profiles ticked ------------------------------------
        await selectByOption(main, "AxisEllipse", "AxisEllipse");
        await tickToolbar(main, "Generate 4 profiles through the centre, rim to rim, when the ellipse is finished");
        await expect(main.locator('[title^="Compass: N-S"] select')).toBeVisible({ timeout: 30_000 });
        if (await toolbar.count()) save("t3-toolbar-crater.png", await toolbar.screenshot());

        const ey = cy - 90;
        for (const p of [{ x: cx - 80, y: ey + 30 }, { x: cx + 80, y: ey - 30 }, { x: cx + 20, y: ey + 50 }]) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await awaitIdle(render, app);
        await expect
            .poll(() => annotationCount(context, app), { timeout: 180_000, intervals: [2000] })
            .toBe(afterPolyline + 1 + 4);

        await render.mouse.move(cx, vp.height - 40);
        await settled(render, "t4-auto-profiles.png", save);
    } finally {
        await context.close();
        await app.stop();
    }
});
