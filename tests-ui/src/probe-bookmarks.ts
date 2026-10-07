/**
 * Render every bookmark of a scene and report whether the body is actually in frame.
 *
 * This is the first thing to run when a screenshot comes out empty, full of smeared
 * terrain, or otherwise not showing the body: it prints, per bookmark, the camera's
 * distance, the angle the body subtends at it, and how much of the frame is body. A body
 * that subtends more than the vertical fov overflows the frame; one that subtends a couple
 * of degrees is a speck. Both look like "the renderer is broken" and neither is.
 *
 * Run: SCENE=<path to .pro3d> npx tsx src/probe-bookmarks.ts
 */
import { chromium } from "@playwright/test";
import { launchPro3d, surfaceShadersReady } from "./pro3d";
import {
    assertBackground, bookmarksOf, distanceOf, hideChrome, measure, readScene,
    sceneWithCamera, settled, subtends,
} from "./probe-lib";
import * as fs from "fs";
import * as path from "path";

const work = path.resolve(__dirname, "..", "artifacts", "bookmarks");

/** spherical-convention radius for Dimorphos; override for another body */
const BODY_RADIUS = Number(process.env.PRO3D_BODY_RADIUS ?? 77.2);

async function main() {
    const scenePath = process.env.SCENE;
    if (!scenePath) throw new Error("set SCENE to the .pro3d file");
    fs.mkdirSync(work, { recursive: true });

    const scene = readScene(scenePath);
    const marks = bookmarksOf(scene);
    if (marks.length === 0) throw new Error(`${scenePath} has no bookmarks`);

    const focal = scene.config?.frustumModel?.focal;
    console.log(`focal ${focal} (10.25 => 60 deg vertical fov), body radius ${BODY_RADIUS} m\n`);

    for (const m of marks) {
        const file = sceneWithCamera(
            scene, m.view, m.navigationMode,
            path.join(work, `scene-${m.name.replace(/[^\w]+/g, "-").toLowerCase()}.pro3d`)
        );
        const app = await launchPro3d(file);
        const browser = await chromium.launch();
        try {
            const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
            const page = await ctx.newPage();
            await page.goto(app.url + "?page=render");
            await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
            await surfaceShadersReady(page);
            await hideChrome(page);

            const slug = m.name.replace(/[^\w]+/g, "-").toLowerCase();
            const shot = await settled(page, path.join(work, `${slug}.png`));
            const s = measure(shot);
            assertBackground(s, m.name);
            const d = distanceOf(m.view);
            console.log(
                `${m.name.padEnd(16)} ${d.toFixed(1).padStart(7)} m  ` +
                `subtends ${subtends(BODY_RADIUS, d).toFixed(1).padStart(5)} deg  ` +
                `coverage ${(s.coverage * 100).toFixed(1).padStart(5)}%  ` +
                `unobserved ${(s.darkOfBody * 100).toFixed(1).padStart(5)}% of body`
            );
        } finally {
            await browser.close();
            await app.stop();
        }
    }
    console.log(`\nframes in ${work}`);
}

main().catch((e) => { console.error(e); process.exit(1); });
