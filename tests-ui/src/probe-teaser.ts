/**
 * Teaser figure for docs/MultiImageProjection.md.
 *
 * Four panels of the same body:
 *   1. the DRACO mosaic alone, from a direction where most of it is unobserved (black)
 *   2. the same camera with the AFC stack projected, filling it
 *   3. a second direction, stack projected
 *   4. the projector frustum of a hovered image
 *
 * Cameras. Each panel's camera is a full [Sky, Location, Forward, Up, Right] view written
 * verbatim into the scene's top-level `cameraView`. Do NOT rebuild a view as "position,
 * looking at the origin": that discards the saved orientation and reframes the shot.
 *
 * Distance. These viewpoints sit 198.8 m from the centre of a body whose spherical radius
 * is 77.2 m, so it subtends ~46 deg inside the scene's 60 deg fov (focal 10.25). Shot from
 * 136.6 m -- where an earlier version of this probe sat -- the body subtends ~69 deg and
 * overflows the frame, which is what made those panels close-ups of smeared terrain
 * rather than an asteroid.
 *
 * Run: npx tsx src/probe-teaser.ts
 *      SCENE=<a .pro3d with bookmarks> npx tsx src/probe-teaser.ts   (use its bookmarks)
 */
import { chromium, BrowserContext } from "@playwright/test";
import { launchPro3d, fixture, sceneFor, surfaceShadersReady } from "./pro3d";
import { compose } from "./figure";
import {
    View, assertBackground, bookmarksOf, clickRowIcon, distanceOf, hideChrome,
    hoverRow, measure, pullBack, readScene, sceneWithCamera, settled, setUpProjection,
    stats, subtends, unhover, withBodyFixedFrame, withViewerLens, withPrimaryTexture, DIMORPHOS_RADIUS,
    DIMORPHOS_VIEWPOINTS,
} from "./probe-lib";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

const outDir = path.resolve(__dirname, "..", "..", "docs", "images");
const work = path.resolve(__dirname, "..", "artifacts", "teaser");

const IMAGES = [
    "AFC1_DRACO2_20270321_140000.png",
    "AFC1_DRACO2_20270321_170000.png",
    "AFC1_DRACO2_20270321_200000.png",
    "AFC1_DRACO2_20270321_230000.png",
];

/** the frustum's apex is at the projector, kilometres out; dolly back to see it as a cone */
const FRUSTUM_K = 4;

interface Shots { bare?: Buffer; stacked?: Buffer; frustum?: Buffer }

async function shoot(
    scene: any,
    view: View,
    navigationMode: number,
    slug: string,
    opts: { bare?: boolean; hover?: boolean }
): Promise<Shots> {
    const file = sceneWithCamera(scene, view, navigationMode, path.join(work, `scene-${slug}.pro3d`));
    const app = await launchPro3d(file);
    const browser = await chromium.launch();
    try {
        const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
        const render = await ctx.newPage();
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(render);
        await hideChrome(render);

        const shots: Shots = {};
        if (opts.bare) shots.bare = await settled(render, path.join(work, `${slug}-bare.png`));

        const gis = await setUpProjection(ctx, app.url, IMAGES[0]);
        for (const img of IMAGES) {
            await clickRowIcon(gis, img, "plus");
            await gis.waitForTimeout(1500);
        }
        // clicking + leaves the pointer on a row, and a hovered row is previewed as the
        // top stack layer (effectiveStack) -- move off before shooting the stack panel
        await unhover(gis);
        shots.stacked = await settled(render, path.join(work, `${slug}-stacked.png`));

        if (opts.hover) {
            // green comes from the frustum and from anything else green in frame, so score
            // each hover against the un-hovered frame rather than in absolute terms
            const base = measure(shots.stacked).greenPixels;
            let best: { name: string; green: number } | undefined;
            for (const img of IMAGES) {
                await hoverRow(gis, img, IMAGES);
                const g = measure(await settled(render, path.join(work, `${slug}-hover-${img}`))).greenPixels - base;
                console.log(`  hover ${img}: frustum green = ${g}`);
                if (!best || g > best.green) best = { name: img, green: g };
            }
            if (!best || best.green <= 20)
                throw new Error(
                    `no projector frustum visible from ${slug} (best +${best?.green ?? 0} green px over ${base}). ` +
                    `Refusing to publish a plain stacked frame under a "frustum" caption.`
                );
            await hoverRow(gis, best.name, IMAGES);
            shots.frustum = await settled(render, path.join(work, `${slug}-frustum.png`));
            console.log(`  frustum from ${best.name} (+${best.green} green px)`);
        }
        return shots;
    } finally {
        await browser.close();
        await app.stop();
    }
}

async function main() {
    fs.mkdirSync(work, { recursive: true });
    fs.mkdirSync(outDir, { recursive: true });

    const base = process.env.SCENE ?? sceneFor(fixture.sceneTemplate, fixture.opc, path.join(work, "scene.pro3d"));
    const scene = readScene(base);
    // The template ships with planet = None and the scene in J2000, so the projection has
    // no body-fixed frame to land in. Bind it, as picking the body under Reference System
    // does in the GUI. (sceneWithCamera additionally clears the Camera source Body, which
    // would otherwise place the camera at HERA and ignore every view set below.)
    if (!process.env.SCENE) {
        withBodyFixedFrame(scene, 9, "Dimorphos", "DIMORPHOS_FIXED");
        // the template carries the 5.53 deg AFC instrument lens; these viewpoints need 60
        withViewerLens(scene);
        // ... and it selects DRACO_2, which covers the body; panel 1 is about the region
        // DRACO_1 never observed
        withPrimaryTexture(scene, "DRACO_1");
    }
    const marks = process.env.SCENE ? bookmarksOf(scene) : DIMORPHOS_VIEWPOINTS;
    if (marks.length < 3) throw new Error(`need 3 viewpoints, ${base} has ${marks.length} bookmarks`);
    const [unobserved, textured, second] = marks;

    for (const mk of marks) {
        const d = distanceOf(mk.view);
        console.log(`${mk.name}: ${d.toFixed(2)} m -> body subtends ${subtends(DIMORPHOS_RADIUS, d).toFixed(1)} deg`);
    }

    console.log(`\n=== ${unobserved.name}: bare + stacked ===`);
    const s0 = await shoot(scene, unobserved.view, unobserved.navigationMode, "b0", { bare: true });
    console.log(`\n=== ${second.name}: stacked ===`);
    const s2 = await shoot(scene, second.view, second.navigationMode, "b2", {});

    const frustumView = pullBack(textured.view, FRUSTUM_K);
    const frustumDist = distanceOf(frustumView);
    console.log(`\n=== ${textured.name}: frustum, ${FRUSTUM_K}x out (${frustumDist.toFixed(0)} m) ===`);
    const s1 = await shoot(scene, frustumView, textured.navigationMode, "b1", { hover: true });

    const need = (b: Buffer | undefined, what: string) => {
        if (!b) throw new Error(`missing panel: ${what}`);
        return PNG.sync.read(b);
    };
    const panels = [
        { png: need(s0.bare, "bare"), file: "b0-bare.png", caption: "DRACO mosaic only" },
        { png: need(s0.stacked, "stacked"), file: "b0-stacked.png", caption: "Plus four AFC images" },
        { png: need(s2.stacked, "second"), file: "b2-stacked.png", caption: "A second direction" },
        { png: need(s1.frustum, "frustum"), file: "b1-frustum.png", caption: "Projector frustum of a hovered image" },
    ];

    console.log("\n--- panels ---");
    const m = panels.map((p, i) => {
        const s = stats(p.png);
        assertBackground(s, `panel ${i + 1}`);
        console.log(
            `${i + 1} ${p.caption.padEnd(38)} coverage=${(s.coverage * 100).toFixed(1)}%  ` +
            `unobserved=${(s.darkOfBody * 100).toFixed(1)}% of body  green=${s.greenPixels}`
        );
        return s;
    });

    const pct = (x: number) => `${Math.round(x * 100)}% unobserved`;
    const browser = await chromium.launch();
    let buf: Buffer;
    try {
        const ctx: BrowserContext = await browser.newContext();
        buf = await compose(
            ctx,
            [
                { file: path.join(work, panels[0].file), caption: panels[0].caption, note: pct(m[0].darkOfBody) },
                { file: path.join(work, panels[1].file), caption: panels[1].caption, note: pct(m[1].darkOfBody) },
                { file: path.join(work, panels[2].file), caption: panels[2].caption, note: pct(m[2].darkOfBody) },
                { file: path.join(work, panels[3].file), caption: panels[3].caption, note: `camera ${frustumDist.toFixed(0)} m` },
            ],
            { cols: 2, width: panels[0].png.width, height: panels[0].png.height, dir: work }
        );
    } finally {
        await browser.close();
    }
    fs.writeFileSync(path.join(outDir, "multiProjection-teaser.png"), buf);
    console.log(`\nteaser -> ${path.join(outDir, "multiProjection-teaser.png")}`);

    // section 3's hover figure is the same frame as panel 4, so the page's description of
    // the camera distance cannot drift from the picture it sits under
    fs.writeFileSync(path.join(outDir, "multiProjection-hover.png"), s1.frustum!);
    console.log(`hover  -> ${path.join(outDir, "multiProjection-hover.png")} (camera ${frustumDist.toFixed(0)} m)`);
}

main().catch((e) => { console.error(e); process.exit(1); });
