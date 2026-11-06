import { test, expect, BrowserContext, Page } from "@playwright/test";
import { fixture, Pro3d } from "../src/pro3d";
import { overlayPlanet } from "../src/viewer";
import {
    awaitIdle,
    clickTool,
    drawingScene,
    openDrawingViewer,
    pick,
    selectByOption,
    settled,
} from "../src/drawing";
import { crop, fitOutline } from "../src/ellipse-fit";
import * as fs from "fs";
import * as path from "path";

/**
 * AxisEllipse on the Dimorphos OPC, in the real viewer.
 *
 * `TC-3.6` in src/Tests/Features/Section03_DrawingAnnotations.fs already covers the model
 * side against a synthetic sampler: geometry, `Projection.Sky`, and an outline of more than
 * three points. Repeating that here would only be slower, so this spec is about the two
 * things a unit test cannot reach:
 *
 *  - **the shape that comes out of a real drape.** The outline is fitted on the plane
 *    through the three picks and then draped onto the terrain by ray casting the KdTrees
 *    along the scene's up vector. `fitOutline` reads the drawn curve back out of the
 *    render and measures how far it is from its own best-fit ellipse. This is not a
 *    hypothetical failure: the manual's figure up to 6.3.2 was drawn on a scene with no
 *    reference body, where that drape has no up vector, and it scores mean 0.097 / p95
 *    0.251 by this measure where a real ellipse scores 0.018 / 0.041.
 *  - **the dropdown gate** that now makes such a scene impossible to draw on: with
 *    `Planet.None` the ellipse entries are rendered disabled, carrying "needs a reference
 *    body" as their tooltip (`dropDownDisabled`, Drawing.UI.fs).
 *
 * PRO3D_DOC_SHOTS=1 also refreshes the manual's figure,
 * docs/PRo3D_ShortUserManual/pics/AnnotationEllipse.PNG.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "ellipse");
const manualPics = path.join(__dirname, "..", "..", "docs", "PRo3D_ShortUserManual", "pics");
const writeDocShots = process.env.PRO3D_DOC_SHOTS === "1";

/** Sampled outline length: `EllipticAnnotations.planeSampleNumber` + 1. */
const OUTLINE_POINTS = 201;

test.setTimeout(30 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

/** Moves the cursor below the body, where the pick ray hits nothing and no marker is drawn. */
async function parkCursor(render: Page, vp: { width: number; height: number }) {
    await render.mouse.move(Math.round(vp.width / 2), vp.height - 40);
    await render.waitForTimeout(1500);
}

/** "#Points: 201 | taken on: ..." per annotation on the annotations page (Drawing.UI.fs). */
async function annotationPointCounts(context: BrowserContext, app: Pro3d): Promise<number[]> {
    const page = await context.newPage();
    try {
        await page.goto(app.url + "?page=annotations");
        await page.waitForLoadState("networkidle");
        const text = (await page.evaluate(() => document.body.innerText)) as string;
        return Array.from(text.matchAll(/#Points:\s*(\d+)/g)).map((m) => Number(m[1]));
    } finally {
        await page.close();
    }
}

/** An entry of one of the toolbar dropdowns: `dropDownDisabled` renders the ones a
 *  geometry or a scene does not allow as disabled options carrying the reason. */
async function dropdownOption(
    main: Page,
    name: string
): Promise<{ found: boolean; enabled: boolean; title: string }> {
    return main.evaluate((o) => {
        const opt = Array.from(document.querySelectorAll("option")).find(
            (x) => (x.textContent ?? "").trim() === o
        ) as HTMLOptionElement | undefined;
        if (!opt) return { found: false, enabled: false, title: "" };
        return { found: true, enabled: !opt.disabled, title: opt.title ?? "" };
    }, name);
}

/** Selected value of the <select> that offers `option`; the toolbar's dropdowns have no
 *  labels, so an option only one of them has identifies it (as in selectByOption). */
async function selectedOf(main: Page, option: string): Promise<string> {
    return main.evaluate((o) => {
        const sel = Array.from(document.querySelectorAll("select")).find((s) =>
            Array.from(s.options).some((x) => (x.textContent ?? "").trim() === o)
        );
        return sel ? sel.value : "(no such dropdown)";
    }, option);
}

test("an AxisEllipse drawn on a body is an ellipse on the terrain", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    // Dimorphos as the scene body: the precondition for the whole tool, see the other case
    const scene = drawingScene(artifacts, "ellipse");
    const { app, context, render } = await openDrawingViewer(browser, scene, "1-loaded.png", save);
    try {
        await expect
            .poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("Dimorphos");

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        await clickTool(render, "Draw annotation");
        await expect
            .poll(() => dropdownOption(main, "AxisEllipse").then((o) => o.enabled), {
                timeout: 60_000,
                intervals: [500],
            })
            .toBe(true);
        await selectByOption(main, "AxisEllipse", "AxisEllipse");

        // Ellipses allow Sky alone, and the toolbar shows that both ways round: the
        // projection follows the geometry, and the ones it excludes are greyed out.
        await expect
            .poll(() => selectedOf(main, "Viewpoint"), { timeout: 30_000, intervals: [500] })
            .toBe("Sky");
        expect((await dropdownOption(main, "Viewpoint")).enabled, "Viewpoint is greyed out").toBe(
            false
        );
        expect((await dropdownOption(main, "Linear")).enabled, "Linear is greyed out").toBe(false);

        // Thickness drives both the drawn line and the picks while drawing (a point list of
        // thickness * 1.5 px, Sg.drawWorkingAnnotation). 2 keeps the outline thin enough to
        // measure without the picks disappearing.
        const thickness = main.locator(".pro3d-secondary-toolbar input[type=number]").first();
        await thickness.fill("2");
        await thickness.press("Enter");
        await main.waitForTimeout(800);

        // Two picks for the ends of the major axis, one off to the side for the semi-minor
        // length. All three stay well inside the body, which spans roughly 700 px of the
        // 1600 px view at CAMERA_DISTANCE.
        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);
        const ax = 95; // half the major axis, in pixels
        const ay = 55; // how far its ends sit above/below the centre
        const len = Math.hypot(2 * ax, 2 * ay); // unit normal of the axis, times 65 px
        const picks = [
            { x: cx - ax, y: cy + ay },
            { x: cx + ax, y: cy - ay },
            {
                x: Math.round(cx + ((2 * ay) / len) * 65),
                y: Math.round(cy + ((2 * ax) / len) * 65),
            },
        ];

        for (const p of picks) {
            await awaitIdle(render, app);
            await pick(render, p.x, p.y);
        }
        await awaitIdle(render, app);

        // AxisEllipse finishes itself on the third point (DrawingApp.addPoint), so there is
        // no Enter to press -- the annotation appears once the drape has run.
        await expect
            .poll(() => annotationPointCounts(context, app), { timeout: 180_000, intervals: [5000] })
            .toHaveLength(1);

        // the sampled outline reached the annotation, not the three raw picks
        expect(
            (await annotationPointCounts(context, app))[0],
            "the annotation carries the sampled outline"
        ).toBe(OUTLINE_POINTS);

        await parkCursor(render, vp);
        const shot = await settled(render, "2-ellipse-full.png", save);
        const figure = crop(shot, cx - ax - 100, cy - ay - 100, 2 * ax + 200, 2 * ay + 200);
        save("3-ellipse.png", figure);

        // --- is it an ellipse? ---------------------------------------------------------
        const fit = fitOutline(figure);
        console.log(`outline fit: ${JSON.stringify(fit)}`);
        expect(fit, "the outline was found in the render (annotations draw red)").not.toBeNull();
        const f = fit!;

        // every sector around the centre has curve in it: a drape that failed to reproject
        // part of the outline drops those points with a warning and leaves a gap
        expect(f.emptyBins, "the outline is closed all the way round").toBe(0);

        // the picks asked for a 110 x 65 px ellipse; perspective and the terrain move that
        // around, but a collapsed or runaway axis is not an ellipse anybody drew
        expect(f.ratio, "the axes keep the proportions that were picked").toBeGreaterThan(1.2);
        expect(f.ratio).toBeLessThan(2.5);

        // and the curve itself: the old body-less figure scores 0.097 / 0.251 here
        expect(f.mean, "the drawn curve is an ellipse").toBeLessThan(0.04);
        expect(f.p95, "no part of it wanders off the ellipse").toBeLessThan(0.1);

        if (writeDocShots) {
            fs.writeFileSync(path.join(manualPics, "AnnotationEllipse.PNG"), figure);
            console.log(`wrote the manual figure into ${manualPics}`);
        }
    } finally {
        await context.close();
        await app.stop();
    }
});

test("the ellipse tools are greyed out while the scene has no reference body", async ({
    browser,
}) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");

    // The same scene, but observing Dimorphos in J2000 again: not a body-fixed frame, so
    // the scene-body sync leaves the planet at None (scene-body.spec.ts covers why).
    const scene = drawingScene(artifacts, "ellipse-no-body", (d) => {
        d.gisApp.defaultObservationInfo.referenceFrame = { FrameSpiceName: "J2000" };
    });
    const { app, context, render } = await openDrawingViewer(browser, scene, "4-no-body-loaded.png", save);
    try {
        await expect.poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] })
            .toBe("None xyz");

        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");
        await clickTool(render, "Draw annotation");

        // the tool is offered but not selectable, and says why
        await expect
            .poll(() => dropdownOption(main, "AxisEllipse").then((o) => o.found), {
                timeout: 60_000,
                intervals: [500],
            })
            .toBe(true);
        const ellipse = await dropdownOption(main, "AxisEllipse");
        expect(ellipse.enabled, "AxisEllipse needs a body and there is none").toBe(false);
        expect(ellipse.title, "the tooltip carries the reason").toContain("needs a reference body");

        // ...while the geometries that do not need one stay available, i.e. this is the
        // reference-body gate and not a dead dropdown
        expect((await dropdownOption(main, "Line")).enabled, "Line stays available").toBe(true);
        expect((await dropdownOption(main, "Polyline")).enabled, "Polyline stays available").toBe(
            true
        );
    } finally {
        await context.close();
        await app.stop();
    }
});
