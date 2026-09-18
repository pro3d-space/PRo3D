/**
 * Figures for docs/TimeAndSun.md: sliding the Mission Time slider sweeps the sun around
 * Dimorphos.
 *
 * Two things have to be right or the sun does not appear to move at all, and both were
 * found the hard way (measurements in the doc):
 *
 * 1. The scene must be in the body's OWN FIXED FRAME. The test-data template is saved
 *    with planet = None and the scene in J2000; in an inertial frame the body does not
 *    turn, and the lit area only creeps from 32.5% to 33.6% across the whole 4-day row.
 *    Picking the planet in the GUI is what sets this (SceneBody, #758).
 * 2. The row spans 4 days and Dimorphos turns in about 11 h, so ~9 turns fit in the
 *    slider. Sampling it in 8 or 9 steps lands on nearly the same phase every time -- the
 *    sun looks frozen, but that is aliasing. One turn is ~0.11 of the row.
 *
 * Mission-time rows are hard-coded (GisApp.getMissionTimeEntriesData) and appear only
 * after the "Load Data" button. A row's slider is inert until the row is clicked
 * (`pointer-events: none`), and clicking it also sets the scene time.
 *
 * Run: npx tsx src/probe-suntime.ts
 */
import { chromium, Page } from "@playwright/test";
import { launchPro3d, fixture, sceneFor, surfaceShadersReady } from "./pro3d";
import { litFraction, streamLive, diffPng } from "./image";
import { compose, cropToContent } from "./figure";
import { PNG } from "pngjs";
import * as fs from "fs";
import * as path from "path";

const outDir = path.resolve(__dirname, "..", "..", "docs", "images");
const work = path.resolve(__dirname, "..", "artifacts", "suntime");

const ROW = process.env.PRO3D_MISSION_ROW ?? "Didymos Orbital Insertion";
/** the row's window, from GisApp.getMissionTimeEntriesData (UTC) */
const ROW_START = Date.UTC(2026, 11, 12, 0, 0, 0);
const ROW_END = Date.UTC(2026, 11, 16, 0, 0, 0);
const dateAt = (v: number) => new Date(ROW_START + (ROW_END - ROW_START) * v);

/** how much of the row one full turn takes; measured by the scan below */
const SCAN_TO = Number(process.env.PRO3D_SUN_SCAN_TO ?? 0.26);
const SCAN_STEPS = Number(process.env.PRO3D_SUN_SCAN_STEPS ?? 26);
const STRIP = 6;

/** lit fraction of the body, and where the lit region sits */
function lit(buf: Buffer) {
    const img = PNG.sync.read(buf);
    let n = 0, sx = 0, body = 0;
    for (let y = 0; y < img.height; y++)
        for (let x = 0; x < img.width; x++) {
            const i = (img.width * y + x) * 4;
            const v = (img.data[i] + img.data[i + 1] + img.data[i + 2]) / 3;
            if (Math.abs(v - 34) <= 6) continue; // viewer clear colour, see ai/TESTING.md
            body++;
            if (v > 70) { n++; sx += x; }
        }
    return { litOfBody: body ? n / body : 0, cx: n ? sx / n : 0, bodyPixels: body };
}

async function settled(page: Page, name: string): Promise<Buffer> {
    let prev = await page.screenshot();
    for (let i = 0; i < 25; i++) {
        await page.waitForTimeout(700);
        const cur = await page.screenshot();
        if (diffPng(prev, cur).changedFraction < 0.0008) { prev = cur; break; }
        prev = cur;
    }
    fs.writeFileSync(path.join(work, name), prev);
    return prev;
}

async function hideChrome(page: Page) {
    await page.evaluate(
        `(function(){
            var img = document.querySelector("img.rendercontrol");
            if (!img) return;
            Array.prototype.forEach.call(document.body.querySelectorAll("*"), function(e){
                if (e === img || e.contains(img)) return;
                var cs = getComputedStyle(e);
                if (cs.position === "absolute" || cs.position === "fixed") e.style.display = "none";
            });
        })()`
    );
}

const clickTitle = (title: string) => `(function(){
    var t = Array.from(document.querySelectorAll('.title')).find(function(e){
        return (e.textContent||'').trim().indexOf(${JSON.stringify(title)}) === 0; });
    if (!t) return 'no accordion ' + ${JSON.stringify(title)};
    t.click();
    return 'ok';
})()`;

async function setSun(gis: Page, mode: string) {
    const r = await gis.evaluate(
        `(function(){
            var el = Array.from(document.querySelectorAll("*")).find(function(e){
                return (e.textContent || "").trim() === "Sun / Lighting Mode:"; });
            var sel = el && el.parentElement ? el.parentElement.querySelector("select") : null;
            if (!sel) return "no Sun / Lighting Mode select";
            var opt = Array.from(sel.options).find(function(o){
                return o.textContent.trim() === ${JSON.stringify(mode)}; });
            if (!opt) return "no option " + ${JSON.stringify(mode)};
            sel.value = opt.value;
            sel.dispatchEvent(new Event("change", { bubbles: true }));
            return "ok";
        })()`
    );
    if (r !== "ok") throw new Error(`sun mode ${mode}: ${r}`);
    await gis.waitForTimeout(2500);
}

async function setSlider(gis: Page, v: number) {
    const r = await gis.evaluate(
        `(function(){
            var row = Array.from(document.querySelectorAll("tr")).find(function(e){
                return (e.textContent||"").indexOf(${JSON.stringify(ROW)}) >= 0; });
            if (!row) return "row gone";
            var inp = row.querySelector('input[type=range]');
            if (!inp) return "no slider in row (is it selected?)";
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

async function main() {
    fs.mkdirSync(work, { recursive: true });
    fs.mkdirSync(outDir, { recursive: true });

    const scene = sceneFor(fixture.sceneTemplate, fixture.opc, path.join(work, "scene.pro3d"));
    // put the scene in the body's fixed frame -- what picking the planet does in the GUI
    const d = JSON.parse(fs.readFileSync(scene, "utf-8").replace(/^﻿/, ""));
    d.referenceSystem = { ...(d.referenceSystem ?? {}), planet: 9 };
    d.gisApp = d.gisApp ?? {};
    d.gisApp.defaultObservationInfo = {
        ...(d.gisApp.defaultObservationInfo ?? {}),
        observer: { EntitySpiceName: "Dimorphos" },
        referenceFrame: { FrameSpiceName: "DIMORPHOS_FIXED" },
    };
    fs.writeFileSync(scene, JSON.stringify(d, null, 2));

    const app = await launchPro3d(scene);
    const browser = await chromium.launch();
    try {
        const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
        const render = await ctx.newPage();
        await render.goto(app.url + "?page=render");
        await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(render);
        let s = await render.screenshot();
        const t0 = Date.now();
        while ((!streamLive(s) || litFraction(s) < 0.003) && Date.now() - t0 < 300_000) {
            await render.waitForTimeout(3000);
            s = await render.screenshot();
        }
        await hideChrome(render);

        const gis = await ctx.newPage();
        await gis.setViewportSize({ width: 1200, height: 1100 });
        await gis.goto(app.url + "?page=gis");
        await gis.waitForLoadState("networkidle");
        await gis.locator("text=Projected Images").first().click();
        await gis.locator("text=Projection Settings").first().waitFor({ timeout: 60_000 });
        await gis.locator("text=Projection Settings").first().click();
        await gis.waitForTimeout(1000);

        // --- lighting modes, same instant -------------------------------------------
        const offShot = await settled(render, "mode-off.png");
        await setSun(gis, "SunDirect");
        const directShot = await settled(render, "mode-direct.png");
        await setSun(gis, "SunShadow");
        const shadowShot = await settled(render, "mode-shadow.png");
        console.log(`Off        lit=${(lit(offShot).litOfBody * 100).toFixed(1)}%`);
        console.log(`SunDirect  lit=${(lit(directShot).litOfBody * 100).toFixed(1)}%`);
        console.log(`SunShadow  lit=${(lit(shadowShot).litOfBody * 100).toFixed(1)}%`);
        await setSun(gis, "SunDirect"); // the strip uses the simpler mode

        // --- mission time ------------------------------------------------------------
        console.log("Mission Time accordion:", await gis.evaluate(clickTitle("Mission Time")));
        await gis.waitForTimeout(800);
        const loaded = await gis.evaluate(
            `(function(){
                var b = Array.from(document.querySelectorAll("button")).find(function(e){
                    return (e.textContent||"").trim() === "Load Data"; });
                if (!b) return "no Load Data button";
                b.click(); return "ok";
            })()`
        );
        if (loaded !== "ok") throw new Error(`Load Data: ${loaded}`);
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

        // --- scan for the rotation period ---------------------------------------------
        console.log(`\nscanning 0..${SCAN_TO} of the row (${ROW}, 4 days)`);
        const scan: Array<{ v: number; lit: number; cx: number }> = [];
        for (let i = 0; i <= SCAN_STEPS; i++) {
            const v = (SCAN_TO * i) / SCAN_STEPS;
            await setSlider(gis, v);
            const shot = await settled(render, `scan-${String(i).padStart(2, "0")}.png`);
            const l = lit(shot);
            scan.push({ v, lit: l.litOfBody, cx: l.cx });
            console.log(
                `  ${v.toFixed(3)}  ${dateAt(v).toISOString().replace("T", " ").slice(0, 16)}  ` +
                `lit=${(l.litOfBody * 100).toFixed(1)}%  cx=${l.cx.toFixed(0)}`
            );
        }

        // local minima of the lit fraction are one full turn apart
        const minima = scan.filter(
            (p, i) => i > 0 && i < scan.length - 1 && p.lit < scan[i - 1].lit && p.lit <= scan[i + 1].lit
        );
        let period = 0;
        if (minima.length >= 2) {
            period = (minima[minima.length - 1].v - minima[0].v) / (minima.length - 1);
            const hours = (period * (ROW_END - ROW_START)) / 3_600_000;
            console.log(
                `\nminima at ${minima.map((m) => m.v.toFixed(3)).join(", ")} ` +
                `-> one turn = ${period.toFixed(3)} of the row = ${hours.toFixed(1)} h`
            );
        } else {
            console.log(`\nonly ${minima.length} minima found; widen PRO3D_SUN_SCAN_TO`);
        }

        // --- the filmstrip: one turn, evenly sampled ----------------------------------
        const from = minima.length ? minima[0].v : 0.04;
        const span = period || 0.11;
        const panels = [];
        for (let i = 0; i < STRIP; i++) {
            const v = from + (span * i) / STRIP;
            await setSlider(gis, v);
            const name = `strip-${i}.png`;
            const shot = await settled(render, name);
            const l = lit(shot);
            panels.push({
                file: path.join(work, name),
                caption: dateAt(v).toISOString().replace("T", " ").slice(0, 16) + " UTC",
                note: `${(l.litOfBody * 100).toFixed(0)}% lit`,
            });
            console.log(`strip ${i}: v=${v.toFixed(3)} lit=${(l.litOfBody * 100).toFixed(1)}%`);
        }

        // the body fills only a fraction of the frame at this camera; crop every panel
        // to one common box so the strip is body, not background
        const cropped = cropToContent(panels.map((p) => p.file));
        const strip = await compose(
            ctx,
            panels.map((p, i) => ({ ...p, file: cropped.files[i] })),
            { cols: 3, width: cropped.width, height: cropped.height, dir: work }
        );
        fs.writeFileSync(path.join(outDir, "timeSun-rotation.png"), strip);
        console.log(`\nstrip -> ${path.join(outDir, "timeSun-rotation.png")}`);

        const modeFiles = ["mode-off.png", "mode-direct.png", "mode-shadow.png"].map((f) =>
            path.join(work, f)
        );
        const modeCrop = cropToContent(modeFiles);
        const modes = await compose(
            ctx,
            ["Off", "SunDirect", "SunShadow"].map((caption, i) => ({
                file: modeCrop.files[i],
                caption,
            })),
            { cols: 3, width: modeCrop.width, height: modeCrop.height, dir: work }
        );
        fs.writeFileSync(path.join(outDir, "timeSun-modes.png"), modes);
        console.log(`modes -> ${path.join(outDir, "timeSun-modes.png")}`);
    } finally {
        await browser.close();
        await app.stop();
    }
}

main().catch((e) => { console.error(e); process.exit(1); });
