/**
 * Render each bookmark of a scene and report whether the body is actually in frame.
 *
 * The point is to check the user's own saved viewpoints rather than a reconstruction:
 * a bookmark stores the full [Sky, Location, Forward, Up, Right] view, so writing it
 * into the scene's top-level cameraView reproduces the view EXACTLY, with none of the
 * "position + look at origin" rebuilding that reframes the shot at close range.
 *
 * Run: SCENE=<path to .pro3d> npx tsx src/probe-bookmarks.ts
 */
import { chromium } from "@playwright/test";
import { launchPro3d, surfaceShadersReady } from "./pro3d";
import { litFraction, streamLive, diffPng } from "./image";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

const work = path.resolve(__dirname, "..", "artifacts", "bookmarks");

/** Body vs. background, with the background sampled as the darkest corner-ish region.
 *  Coverage = fraction of the render area that is body (lit or black-but-not-background).
 *  Background here is the render div's #222222, NOT pure black, which is why an
 *  unobserved (truly black) body region is distinguishable from empty space at all. */
function coverage(buf: Buffer, left = 120, right = 70) {
    const img = PNG.sync.read(buf);
    const at = (x: number, y: number) => {
        const i = (img.width * y + x) * 4;
        return (img.data[i] + img.data[i + 1] + img.data[i + 2]) / 3;
    };
    const bg = at(left + 4, img.height - 6); // bottom-left of the render area
    let n = 0, body = 0, dark = 0;
    for (let y = 0; y < img.height; y++)
        for (let x = left; x < img.width - right; x++) {
            n++;
            const v = at(x, y);
            if (Math.abs(v - bg) <= 6) continue; // background
            body++;
            if (v < 25) dark++; // on-body but unobserved
        }
    return { bg, frac: n ? body / n : 0, darkOfBody: body ? dark / body : 0, bodyPixels: body };
}

async function settled(page: import("@playwright/test").Page, file: string): Promise<Buffer> {
    const t0 = Date.now();
    let shot = await page.screenshot();
    while ((!streamLive(shot) || litFraction(shot) < 0.003) && Date.now() - t0 < 420_000) {
        await page.waitForTimeout(3000);
        shot = await page.screenshot();
    }
    let prev = shot;
    for (let i = 0; i < 30; i++) {
        await page.waitForTimeout(800);
        const cur = await page.screenshot();
        if (diffPng(prev, cur).changedFraction < 0.001) { prev = cur; break; }
        prev = cur;
    }
    fs.writeFileSync(file, prev);
    return prev;
}

function norm(v: number[]) { return Math.hypot(v[0], v[1], v[2]); }
function parseVec(s: string): number[] {
    return s.replace(/[[\]]/g, "").split(",").map((x) => Number(x.trim()));
}

async function main() {
    const scenePath = process.env.SCENE;
    if (!scenePath) throw new Error("set SCENE to the .pro3d file");
    fs.mkdirSync(work, { recursive: true });

    const raw = fs.readFileSync(scenePath, "utf-8").replace(/^﻿/, "");
    const base = JSON.parse(raw);
    const marks: Array<{ name: string; view: string[]; navigationMode: number }> =
        (base.bookmarks?.flat ?? []).map((e: any) => ({
            name: e.Bookmarks.name,
            view: e.Bookmarks.cameraView.view,
            navigationMode: e.Bookmarks.navigationMode,
        }));
    if (marks.length === 0) throw new Error("no bookmarks in scene");
    marks.sort((a, b) => a.name.localeCompare(b.name));

    console.log(`focal ${base.config?.frustumModel?.focal}  (10.25 => 60 deg)`);
    for (const m of marks) {
        const loc = parseVec(m.view[1]);
        console.log(`${m.name}: |location| = ${norm(loc).toFixed(2)} m  nav=${m.navigationMode}`);
    }

    for (const m of marks) {
        const slug = m.name.replace(/[^\w]+/g, "-").toLowerCase();
        const out = path.join(work, `scene-${slug}.pro3d`);
        const d = JSON.parse(raw);
        d.cameraView = { view: m.view };      // verbatim, all five vectors
        d.navigationMode = m.navigationMode;
        d.scenePath = out;
        fs.writeFileSync(out, JSON.stringify(d, null, 2));

        const app = await launchPro3d(out);
        const browser = await chromium.launch();
        try {
            const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
            const page = await ctx.newPage();
            await page.goto(app.url + "?page=render");
            await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
            await surfaceShadersReady(page);
            const shot = await settled(page, path.join(work, `${slug}.png`));
            const c = coverage(shot);
            console.log(
                `${m.name}: bodyPixels=${c.bodyPixels} coverage=${(c.frac * 100).toFixed(1)}% ` +
                `unobserved=${(c.darkOfBody * 100).toFixed(1)}% of body  (bg=${c.bg.toFixed(0)})`
            );
        } finally {
            await browser.close();
            await app.stop();
        }
    }
    console.log(`\nshots in ${work}`);
}

main().catch((e) => { console.error(e); process.exit(1); });
