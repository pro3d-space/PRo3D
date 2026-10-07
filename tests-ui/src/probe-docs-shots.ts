/**
 * The remaining screenshots for docs/MultiImageProjection.md: the GIS tab, the projected
 * stack, the opacity blend, the coverage view and fly-to.
 *
 * The teaser and the hover/frustum figure come from probe-teaser.ts, which owns the
 * camera story (a full saved view written into `cameraView`).
 *
 * This probe used to hard-code image names from a private COP folder, so nobody could
 * regenerate its figures; it now uses the AFC frames that ship in PRo3D.Resources.TestData
 * and sets the scene up the way the page says to, so anyone can. What it does NOT do any
 * more is launch the viewer, read the camera position out of the HUD text, tear everything
 * down and relaunch with a reconstructed view matrix -- a bookmark carries the whole view,
 * and rebuilding one from a position loses its orientation.
 *
 * Run: npx tsx src/probe-docs-shots.ts
 */
import { chromium } from "@playwright/test";
import { launchPro3d, fixture, sceneFor, surfaceShadersReady } from "./pro3d";
import { diffPng } from "./image";
import {
    assertBackground, clickRowIcon, hideChrome, measure, readScene, selectBeside, settled,
    setUpProjection, unhover, withFreeCamera, sceneWithCamera, DIMORPHOS_VIEWPOINTS,
} from "./probe-lib";
import * as fs from "fs";
import * as path from "path";

const outDir = path.resolve(__dirname, "..", "..", "docs", "images");
const work = path.resolve(__dirname, "..", "artifacts", "docs-shots");

/** lowest and highest sub-spacecraft latitude of the four AFC frames, so the second
 *  covers terrain the first misses */
const imgA = "AFC1_DRACO2_20270321_140000.png";
const imgB = "AFC1_DRACO2_20270321_230000.png";

/** the Image Opacity slider: an <input type=range> driven through the native setter,
 *  because assigning .value does not notify aardvark.media's listener */
async function setOpacity(gis: import("@playwright/test").Page, value: number) {
    const r = await gis.evaluate(
        `(function(){
            var inputs = Array.from(document.querySelectorAll('input[type=range]'))
                .filter(function(i){ return Math.abs(parseFloat(i.max) - 1) < 1e-9; });
            if (inputs.length !== 1) return "expected one 0..1 slider, found " + inputs.length;
            var inp = inputs[0];
            var set = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set;
            set.call(inp, String(${value}));
            inp.dispatchEvent(new Event("input", { bubbles: true }));
            inp.dispatchEvent(new Event("change", { bubbles: true }));
            return "ok";
        })()`
    );
    if (r !== "ok") throw new Error(`opacity ${value}: ${r}`);
    await gis.waitForTimeout(3000);
}

async function main() {
    fs.mkdirSync(work, { recursive: true });
    fs.mkdirSync(outDir, { recursive: true });

    // the scene section 1 of the page asks for: the body bound so the projection has a
    // frame to land in, and PRo3D's normal 60 deg camera rather than the AFC lens
    const file = path.join(work, "docs-shots.pro3d");
    sceneFor(fixture.sceneTemplate, fixture.opc, file);
    const d = withFreeCamera(readScene(file));
    d.gisApp.defaultObservationInfo.observer = { EntitySpiceName: "Dimorphos" };
    d.gisApp.defaultObservationInfo.referenceFrame = { FrameSpiceName: "DIMORPHOS_FIXED" };
    d.gisApp.defaultObservationInfo.time = "2027-03-21T23:00:00.0000000Z";
    d.referenceSystem.planet = 9; // Planet.Dimorphos
    if (!d.config?.frustumModel) throw new Error("scene has no config.frustumModel to set the focal on");
    d.config.frustumModel.focal = 10.25; // 60 deg; the template carries the 5.53 deg AFC lens
    // The template's own camera leaves the body a few hundred pixels across, so give it a
    // viewpoint that actually frames it (198.8 m, ~46 deg of a 60 deg fov).
    const view = DIMORPHOS_VIEWPOINTS[1];
    sceneWithCamera(d, view.view, view.navigationMode, file);

    const app = await launchPro3d(file);
    const browser = await chromium.launch();
    try {
        const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
        const render = await ctx.newPage();
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(render);
        await hideChrome(render);
        const bare = await settled(render, path.join(work, "bare.png"));
        assertBackground(measure(bare), "bare surface");

        const gis = await setUpProjection(ctx, app.url, imgA);

        // the GIS tab itself, chrome and all -- this one is a UI screenshot
        fs.writeFileSync(path.join(outDir, "multiProjection-gisTab.png"), await gis.screenshot());

        // one image projected. It was simulated from this very shape model, so it should
        // coincide with the texture underneath rather than fight it.
        await clickRowIcon(gis, imgA, "plus");
        await gis.waitForTimeout(2000);
        await unhover(gis); // a hovered row previews as the top stack layer (effectiveStack)
        const one = await settled(render, path.join(outDir, "multiProjection-stack.png"));
        console.log(`one image: ${(diffPng(bare, one).changedFraction * 100).toFixed(1)}% of the frame differs from bare`);

        // the opacity blend, the page's check that the projection registers
        for (const v of [0, 0.5, 1]) {
            await setOpacity(gis, v);
            const shot = await settled(
                render,
                v === 0.5 ? path.join(outDir, "multiProjection-opacity-050.png") : path.join(work, `opacity-${v}.png`)
            );
            console.log(`  opacity ${v}: ${(diffPng(bare, shot).changedFraction * 100).toFixed(2)}% of the frame differs from bare`);
        }
        await setOpacity(gis, 1);

        // a second image covering terrain the first never saw, then the coverage view
        await clickRowIcon(gis, imgB, "plus");
        await gis.waitForTimeout(2000);
        await unhover(gis);
        await settled(render, path.join(work, "two-images.png"));

        await selectBeside(gis, "Visibility:", "RelativeCount");
        await gis.waitForTimeout(2500);
        await settled(render, path.join(outDir, "multiProjection-coverage.png"));
        await selectBeside(gis, "Visibility:", "Off");
        await gis.waitForTimeout(2000);

        // fly-to: the camera onto the image's own projector axis
        await clickRowIcon(gis, imgA, "location arrow");
        await gis.waitForTimeout(4000);
        await settled(render, path.join(outDir, "multiProjection-flyTo.png"));

        console.log(`\nfigures -> ${outDir}`);
    } finally {
        await browser.close();
        await app.stop();
    }
}

main().catch((e) => { console.error(e); process.exit(1); });
