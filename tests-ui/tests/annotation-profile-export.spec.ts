import { test, expect, Page } from "@playwright/test";
import { fixture } from "../src/pro3d";
import { overlayPlanet } from "../src/viewer";
import { drawingScene, drawSkyLine, openDrawingViewer, selectByOption, settled } from "../src/drawing";
import * as fs from "fs";
import * as path from "path";

/**
 * Multi-attribute profile export -- docs/MultiAttributeProfile.md.
 *
 * Draws a sky-projected Line across the Dimorphos OPC and exports it through
 * Annotations -> Export... as a CSV with one row per point and the OPC's layers sampled
 * under each of them: the Profile preset (CSV, one record per point, scope Selected) plus
 * the Surface properties checkbox.
 *
 * End to end rather than an Expecto case because every step is a different subsystem
 * meeting the others: Sky drapes the line by ray casting the KdTrees, the export re-picks
 * every one of those points to read the per-vertex layers, and the schema is assembled from
 * settings spread over three widgets. An exporter unit test proves the writer; only this
 * proves that drawing on a real surface yields a file with real numbers in it.
 *
 * PRO3D_DOC_SHOTS=1 also refreshes the images of docs/MultiAttributeProfile.md.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "profile-export");
const docImages = path.join(__dirname, "..", "..", "docs", "images");
const writeDocShots = process.env.PRO3D_DOC_SHOTS === "1";

/** Per-vertex layers of the fixture OPC: `surface_` columns that must be filled on every row. */
const EXPECTED_LAYERS = ["Elevation", "Gravity", "Slope"];

/** Panel of the export window (AnnotationExportApp). */
const dimmer = ".annotation-export-dimmer";

test.setTimeout(30 * 60_000);

/** Saves into artifacts/, and into docs/images as well under PRO3D_DOC_SHOTS=1.
 *  `1-loaded.png` is the bare surface before anything is drawn -- useful when a run fails,
 *  but the documentation does not use it. */
function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
    if (writeDocShots && name !== "1-loaded.png") {
        fs.mkdirSync(docImages, { recursive: true });
        fs.writeFileSync(path.join(docImages, `multiAttributeProfile-${name}`), png);
    }
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

/** Clicks the element in the export window whose label is `label`: an accordion title
 *  (a `.title` holding a span with that text) or a checkbox (an <i> icon followed by its
 *  <span> label, AnnotationExportApp.checkBox). Returns what happened, for the assertion. */
async function clickInExportWindow(main: Page, kind: "accordion" | "checkbox", label: string) {
    return main.evaluate(
        ({ d, k, l }) => {
            const spans = Array.from(document.querySelectorAll(`${d} span`)).filter(
                (s) => (s.textContent ?? "").trim() === l
            );
            const target =
                k === "accordion"
                    ? (spans.map((s) => s.closest(".title")).find((t) => t) as HTMLElement | null)
                    : (spans[0]?.previousElementSibling as HTMLElement | null);
            if (!target) return `no ${l} ${k}`;
            target.click();
            return "clicked";
        },
        { d: dimmer, k: kind, l: label }
    );
}

/** Rows of a CSV, honouring quoted cells (a comma in an annotation's text is quoted). */
function parseCsv(text: string): string[][] {
    const rows: string[][] = [];
    let row: string[] = [];
    let cell = "";
    let quoted = false;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (quoted) {
            if (c === '"' && text[i + 1] === '"') {
                cell += '"';
                i++;
            } else if (c === '"') quoted = false;
            else cell += c;
        } else if (c === '"') quoted = true;
        else if (c === ",") {
            row.push(cell);
            cell = "";
        } else if (c === "\n" || c === "\r") {
            if (c === "\r" && text[i + 1] === "\n") i++;
            row.push(cell);
            rows.push(row);
            row = [];
            cell = "";
        } else cell += c;
    }
    if (cell !== "" || row.length > 0) {
        row.push(cell);
        rows.push(row);
    }
    return rows;
}

test("a sky-projected line exports as a multi-attribute profile CSV", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    // Dimorphos as the scene body: without one, lat/lon/alt and groundDistance come out
    // empty, and groundDistance is the x-axis of a profile. drawingScene sets it up.
    const scene = drawingScene(artifacts, "profile");
    const csvPath = path.join(artifacts, "dimorphos-profile.csv");
    fs.rmSync(csvPath, { force: true });

    const { app, context, render } = await openDrawingViewer(browser, scene, "1-loaded.png", save);
    try {
        // the geographic columns depend on this, so fail here rather than on empty cells
        await expect
            .poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("Dimorphos");

        // the main page carries the top menu, the annotation toolbar and the export window
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        await drawSkyLine(context, app, render, main, async () => {
            const toolbar = main.locator(".pro3d-secondary-toolbar").first();
            if (await toolbar.count()) save("2-toolbar.png", await toolbar.screenshot());
        });
        await settled(render, "3-annotation.png", save);

        // --- export -------------------------------------------------------------------
        await stubSaveDialog(main, csvPath);
        await menu(main, "Annotations", "Export...");
        const panel = main.locator(`${dimmer} .ui.inverted.segment`).first();
        await expect(panel).toBeVisible({ timeout: 30_000 });

        // Profile preset: CSV, one record per point, scope Selected (the line just drawn is
        // the selected annotation), sampled points, every point attribute. It leaves Surface
        // properties off: that is the setting that makes the profile multi-attribute, and it
        // is never preset because it re-picks every point.
        await selectByOption(main, "Profile", "Profile", dimmer);
        await main.waitForTimeout(1000);

        // the Point attributes accordion exists only once the granularity is per point,
        // which the preset has just set
        expect(await clickInExportWindow(main, "accordion", "Point attributes")).toBe("clicked");
        await main.waitForTimeout(800);
        expect(await clickInExportWindow(main, "checkbox", "Surface properties")).toBe("clicked");
        await main.waitForTimeout(1000);
        save("4-export-window.png", await panel.screenshot());

        // The settings scroll between a fixed header and footer, and Surface properties sits
        // at the very bottom, off screen in the shot above. Scroll it into view for a second
        // image, so the documentation shows the one control it is about.
        await main.evaluate((d) => {
            const scroller = Array.from(document.querySelectorAll(`${d} div`)).find(
                (e) => (e as HTMLElement).scrollHeight > (e as HTMLElement).clientHeight + 20
            ) as HTMLElement | null;
            if (scroller) scroller.scrollTop = scroller.scrollHeight;
        }, dimmer);
        await main.waitForTimeout(600);
        save("5-surface-properties.png", await panel.screenshot());

        await main.locator(`${dimmer} .ui.primary.button`).first().click();

        // the export re-picks every sampled point, so it takes seconds, and PRo3D is
        // unresponsive meanwhile
        try {
            await expect
                .poll(() => fs.existsSync(csvPath), { timeout: 240_000, intervals: [1000] })
                .toBe(true);
        } catch {
            // the window stays open with a warning when it refuses to write; that text is
            // the diagnosis, so report it instead of "the file never appeared"
            const warning = await main
                .locator(`${dimmer} .warning.message`)
                .first()
                .textContent()
                .catch(() => null);
            throw new Error(`no CSV was written. Export window warning: ${warning ?? "(none)"}`);
        }

        // --- what is actually in the file ---------------------------------------------
        const [header, ...rows] = parseCsv(fs.readFileSync(csvPath, "utf-8").trim());
        console.log(`profile CSV: ${rows.length} rows, ${header.length} columns`);
        console.log(`header: ${header.join(",")}`);
        const cell = (row: string[], col: string) => row[header.indexOf(col)] ?? "";

        for (const c of ["pointIndex", "x", "y", "z", "lat", "lon", "alt", "distance", "groundDistance"])
            expect(header, `the profile schema has ${c}`).toContain(c);
        for (const r of rows) expect(r.length, "every row has every column").toBe(header.length);

        // Sky drapes the line over the surface between the two picks; two rows would mean
        // the projection silently fell back to a straight chord.
        expect(rows.length, "the sky projection sampled the span, not just its ends")
            .toBeGreaterThan(10);

        // A column of empty cells would satisfy "the column exists" while proving nothing,
        // so every row must carry a value.
        const surfaceCols = header.filter((c) => c.startsWith("surface_"));
        console.log(`surface layers sampled: ${surfaceCols.join(", ")}`);
        for (const layer of EXPECTED_LAYERS) {
            expect(surfaceCols, `the OPC ships a ${layer} layer`).toContain(`surface_${layer}`);
            const filled = rows.filter((r) => cell(r, `surface_${layer}`).trim() !== "").length;
            expect(filled, `surface_${layer} sampled under every point`).toBe(rows.length);
        }

        // ...and the values must differ along the line. Everything above still passes if
        // the sampler is stuck on one patch and returns one constant per column -- a flat
        // profile with every structural check green.
        for (const col of ["surface_Elevation", "surface_Slope", "x", "alt"]) {
            const distinct = new Set(rows.map((r) => cell(r, col))).size;
            expect(distinct, `${col} varies along the profile`).toBeGreaterThan(rows.length / 2);
        }

        // geographic columns resolved through SPICE: Dimorphos is planetocentric (reclat)
        expect(cell(rows[0], "latLonAltSource"), "lat/lon came from SPICE").toBe("spice_reclat");
        expect(cell(rows[0], "body")).toBe("Dimorphos");

        // distance runs through 3D space and must advance along the line
        const last = rows[rows.length - 1];
        expect(Number(cell(last, "distance")), "the profile has a length").toBeGreaterThan(0);

        // KNOWN DEFECT #830, asserted as it behaves today so that the fix trips this line:
        // groundDistance is 0 on every row of a Spherical-convention body, because
        // AnnotationExport.flatten sets altitude = 0, which on such a body is the body centre.
        // Once fixed, assert 0 < groundDistance <= distance instead.
        expect(Number(cell(last, "groundDistance")), "groundDistance on Dimorphos (#830)").toBe(0);

        // a stub of the real file, for the documentation's excerpt
        fs.writeFileSync(
            path.join(artifacts, "dimorphos-profile.stub.csv"),
            [header, ...rows.slice(0, 5)].map((r) => r.join(",")).join("\n") + "\n"
        );
    } finally {
        await context.close();
        await app.stop();
    }
});
