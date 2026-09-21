/**
 * Figures for docs/TimeAndSun.md: moving the Mission Time slider sweeps the sun around
 * Dimorphos.
 *
 * Two things must be right or the sun appears not to move at all, and both look like a
 * broken feature:
 *
 * 1. The scene must be in the body's OWN FIXED FRAME. The test-data template ships with
 *    the scene in J2000; in an inertial frame the body does not turn, and the lit area
 *    only creeps from 32.5% to 33.6% across the whole four-day row (measured by
 *    PRO3D_SUN_INERTIAL=1, which reproduces exactly that).
 * 2. The row spans four days and Dimorphos turns in about 10.6 h, so ~9 turns fit in the
 *    slider. Sampling it in eighths or ninths lands on nearly the same phase every time --
 *    the sun looks frozen, but that is aliasing.
 *
 * Mission-time rows are hard-coded (GisApp.getMissionTimeEntriesData). A fresh viewer has
 * them already (GisApp.initial); they are absent only when a saved scene is loaded
 * (GisApp-Model read0 sets None), which is this probe's case, hence the "Load Data" click.
 * A row's slider is inert until the row itself is clicked (`pointer-events: none`).
 *
 * Run: npx tsx src/probe-suntime.ts
 *      PRO3D_SUN_INERTIAL=1 npx tsx src/probe-suntime.ts   (measure the J2000 case)
 */
import { chromium, Page } from "@playwright/test";
import { launchPro3d, fixture, sceneFor, surfaceShadersReady } from "./pro3d";
import { compose, cropToContent } from "./figure";
import {
    assertBackground, hideChrome, measure, readScene, selectBeside, settled,
    withBodyFixedFrame, withFreeCamera,
} from "./probe-lib";
import * as fs from "fs";
import * as path from "path";

const outDir = path.resolve(__dirname, "..", "..", "docs", "images");
const work = path.resolve(__dirname, "..", "artifacts", "suntime");

const ROW = "Didymos Orbital Insertion";
/** the row's window, from GisApp.getMissionTimeEntriesData (UTC) */
const ROW_START = Date.UTC(2026, 11, 12, 0, 0, 0);
const ROW_END = Date.UTC(2026, 11, 16, 0, 0, 0);
const ROW_HOURS = (ROW_END - ROW_START) / 3_600_000;
const dateAt = (v: number) => new Date(ROW_START + (ROW_END - ROW_START) * v);

/** scan far enough to see at least two lit-fraction minima, i.e. two full turns */
const SCAN_TO = 0.26;
const SCAN_STEPS = 26;
const STRIP = 6;
/** the inertial (J2000) comparison needs only the two ends of the row */
const INERTIAL = process.env.PRO3D_SUN_INERTIAL === "1";

async function setSun(gis: Page, mode: string) {
    await selectBeside(gis, "Sun / Lighting Mode:", mode);
    await gis.waitForTimeout(2500);
}

async function setSlider(gis: Page, v: number) {
    const r = await gis.evaluate(
        `(function(){
            var row = Array.from(document.querySelectorAll("tr")).find(function(e){
                return (e.textContent||"").indexOf(${JSON.stringify(ROW)}) >= 0; });
            if (!row) return "row gone";
            var inp = row.querySelector('input[type=range]');
            if (!inp) return "no slider in the row -- has it been clicked to select it?";
            var set = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set;
            set.call(inp, String(${v}));
            inp.dispatchEvent(new Event("input", { bubbles: true }));
            inp.dispatchEvent(new Event("change", { bubbles: true }));
            return "ok";
        })()`
    );
    if (r !== "ok") throw new Error(`slider ${v}: ${r}`);
    await gis.waitForTimeout(2200);
}

async function selectRow(gis: Page) {
    // rows exist already in a fresh viewer; a loaded scene needs Load Data first
    const loaded = await gis.evaluate(
        `(function(){
            var b = Array.from(document.querySelectorAll("button")).find(function(e){
                return (e.textContent||"").trim() === "Load Data"; });
            if (!b) return "already present";
            b.click(); return "clicked";
        })()`
    );
    console.log(`  mission-time rows: ${loaded}`);
    await gis.waitForTimeout(1500);
    const picked = await gis.evaluate(
        `(function(){
            var r = Array.from(document.querySelectorAll("tr")).find(function(e){
                return (e.textContent||"").indexOf(${JSON.stringify(ROW)}) >= 0; });
            if (!r) return "row not found";
            r.click(); return "ok";
        })()`
    );
    if (picked !== "ok") throw new Error(`row ${ROW}: ${picked}`);
    await gis.waitForTimeout(2000);
}

async function main() {
    fs.mkdirSync(work, { recursive: true });
    fs.mkdirSync(outDir, { recursive: true });

    const scene = withFreeCamera(readScene(sceneFor(fixture.sceneTemplate, fixture.opc, path.join(work, "raw.pro3d"))));
    if (!INERTIAL) withBodyFixedFrame(scene, 9, "Dimorphos", "DIMORPHOS_FIXED");
    const file = path.join(work, "scene.pro3d");
    scene.scenePath = file;
    fs.writeFileSync(file, JSON.stringify(scene, null, 2));
    console.log(INERTIAL ? "frame: J2000 (inertial)" : "frame: DIMORPHOS_FIXED (body-fixed)");

    const app = await launchPro3d(file);
    const browser = await chromium.launch();
    try {
        const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
        const render = await ctx.newPage();
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(render);
        await hideChrome(render);

        const gis = await ctx.newPage();
        await gis.setViewportSize({ width: 1200, height: 1100 });
        await gis.goto(app.url + "?page=gis");
        await gis.waitForLoadState("networkidle");
        await gis.locator("text=Projected Images").first().click();
        await gis.locator("text=Projection Settings").first().waitFor({ timeout: 60_000 });
        await gis.locator("text=Projection Settings").first().click();
        await gis.waitForTimeout(1000);

        // --- the three lighting modes, one instant -----------------------------------
        const modes: Array<[string, string]> = [["Off", "mode-off.png"], ["SunDirect", "mode-direct.png"], ["SunShadow", "mode-shadow.png"]];
        for (const [mode, name] of modes) {
            if (mode !== "Off") await setSun(gis, mode);
            const shot = await settled(render, path.join(work, name));
            const s = measure(shot);
            assertBackground(s, mode);
            console.log(`${mode.padEnd(10)} lit=${(s.litOfBody * 100).toFixed(1)}% of body`);
        }
        await setSun(gis, "SunDirect");

        // --- mission time -------------------------------------------------------------
        await gis.evaluate(
            `(function(){
                var t = Array.from(document.querySelectorAll('.title')).find(function(e){
                    return (e.textContent||'').trim().indexOf("Mission Time") === 0; });
                if (t) t.click();
            })()`
        );
        await gis.waitForTimeout(800);
        await selectRow(gis);

        if (INERTIAL) {
            // the whole row, coarsely: the point is that nothing changes
            let lo = 1, hi = 0;
            for (let i = 0; i <= 8; i++) {
                const v = i / 8;
                await setSlider(gis, v);
                const s = measure(await settled(render, path.join(work, `inertial-${i}.png`)));
                lo = Math.min(lo, s.litOfBody); hi = Math.max(hi, s.litOfBody);
                console.log(`  ${v.toFixed(3)}  ${dateAt(v).toISOString().slice(0, 16)}  lit=${(s.litOfBody * 100).toFixed(1)}%`);
            }
            console.log(`\nJ2000: lit area spans ${(lo * 100).toFixed(1)}% .. ${(hi * 100).toFixed(1)}% across the whole row`);
            return;
        }

        // --- scan for the rotation period ----------------------------------------------
        console.log(`\nscanning 0..${SCAN_TO} of the row (${ROW}, ${ROW_HOURS} h)`);
        const scan: Array<{ v: number; lit: number }> = [];
        for (let i = 0; i <= SCAN_STEPS; i++) {
            const v = (SCAN_TO * i) / SCAN_STEPS;
            await setSlider(gis, v);
            const s = measure(await settled(render, path.join(work, `scan-${String(i).padStart(2, "0")}.png`)));
            scan.push({ v, lit: s.litOfBody });
            console.log(
                `  ${v.toFixed(3)}  ${dateAt(v).toISOString().replace("T", " ").slice(0, 16)}  ` +
                `lit=${(s.litOfBody * 100).toFixed(1)}%`
            );
        }

        // Local minima of the lit fraction are one turn apart. Require a margin, so noise
        // on a 27-sample curve cannot invent a minimum and silently divide the period down.
        const MARGIN = 0.02;
        const minima = scan.filter((p, i) =>
            i > 0 && i < scan.length - 1 &&
            p.lit + MARGIN < scan[i - 1].lit && p.lit + MARGIN < scan[i + 1].lit
        );
        if (minima.length < 2)
            throw new Error(
                `found ${minima.length} lit-fraction minima in 0..${SCAN_TO}; cannot measure the ` +
                `rotation period, and a hard-coded fallback would make the figure a guess`
            );
        const period = (minima[minima.length - 1].v - minima[0].v) / (minima.length - 1);
        const hours = period * ROW_HOURS;
        // one scan step is the resolution of this measurement -- quote it, do not imply more
        const resolution = (SCAN_TO / SCAN_STEPS) * ROW_HOURS;
        console.log(
            `\nminima at ${minima.map((m) => m.v.toFixed(3)).join(", ")} -> one turn = ` +
            `${period.toFixed(3)} of the row = ${hours.toFixed(1)} h (+/- ${resolution.toFixed(1)} h, one scan step)`
        );
        console.log(`the row is ${(1 / period).toFixed(1)} turns end to end`);

        // --- the filmstrip --------------------------------------------------------------
        // sample [0, period) so the six frames span one turn without repeating the start
        const panels: Array<{ file: string; caption: string; note: string }> = [];
        for (let i = 0; i < STRIP; i++) {
            const v = minima[0].v + (period * i) / STRIP;
            await setSlider(gis, v);
            const name = `strip-${i}.png`;
            const s = measure(await settled(render, path.join(work, name)));
            panels.push({
                file: path.join(work, name),
                caption: dateAt(v).toISOString().replace("T", " ").slice(0, 16) + " UTC",
                note: `${(s.litOfBody * 100).toFixed(0)}% lit`,
            });
            console.log(`strip ${i}: v=${v.toFixed(3)} lit=${(s.litOfBody * 100).toFixed(1)}%`);
        }
        const stepMinutes = (period / STRIP) * ROW_HOURS * 60;
        console.log(`strip step = ${stepMinutes.toFixed(0)} min`);

        const cropped = cropToContent(panels.map((p) => p.file));
        const strip = await compose(
            ctx,
            panels.map((p, i) => ({ ...p, file: cropped.files[i] })),
            { cols: 3, width: cropped.width, height: cropped.height, dir: work }
        );
        fs.writeFileSync(path.join(outDir, "timeSun-rotation.png"), strip);
        console.log(`\nstrip -> ${path.join(outDir, "timeSun-rotation.png")}`);

        const modeCrop = cropToContent(modes.map(([, n]) => path.join(work, n)));
        const modeFig = await compose(
            ctx,
            modes.map(([mode], i) => ({ file: modeCrop.files[i], caption: mode })),
            { cols: 3, width: modeCrop.width, height: modeCrop.height, dir: work }
        );
        fs.writeFileSync(path.join(outDir, "timeSun-modes.png"), modeFig);
        console.log(`modes -> ${path.join(outDir, "timeSun-modes.png")}`);
    } finally {
        await browser.close();
        await app.stop();
    }
}

main().catch((e) => { console.error(e); process.exit(1); });
