// Import an SBMT catalog, then Color by Category -> surface attribute
// Slope -> resample surface. Reports wall time and the viewer's private bytes throughout.
//
//   PRO3D_SBMT_FILE   the SBMT structure file to import (required)
//   PRO3D_CBC_LAYER   surface layer (default Slope)
import { chromium, Page } from "@playwright/test";
import { execSync } from "child_process";
import * as fs from "fs";
import * as path from "path";
import { launchPro3d, sceneFor, fixture, testData, surfaceShadersReady } from "./pro3d";

const artifacts = path.join(__dirname, "..", "artifacts", "sbmt-cbc");
const SBMT = process.env.PRO3D_SBMT_FILE ?? "";
if (!SBMT) throw new Error("set PRO3D_SBMT_FILE to an SBMT structure file");
const LAYER = process.env.PRO3D_CBC_LAYER ?? "Slope";
const OPC = path.join(testData, "HERA", "Dimorphos_opc", "Dimorphos_DRACO1_DRACO2_Earth", "Dimorphos");

const t0 = Date.now();
const secs = () => ((Date.now() - t0) / 1000).toFixed(1);
const log = (s: string) => console.log(`[${secs()}s] ${s}`);

function privateMb(pid: number): number {
    try {
        const out = execSync(
            `powershell -NoProfile -Command "(Get-Process -Id ${pid}).PrivateMemorySize64"`
        ).toString().trim();
        return Math.round(Number(out) / (1024 * 1024));
    } catch {
        return -1;
    }
}

async function clickText(page: Page, selector: string, label: string) {
    return page.evaluate(
        ([selector, label]) => {
            const el = Array.from(document.querySelectorAll(selector)).find(
                (e) => (e.textContent ?? "").trim().startsWith(label)
            ) as HTMLElement | undefined;
            if (!el) return false;
            el.click();
            return true;
        },
        [selector, label]
    );
}

/** the <select> in the table row labelled `rowLabel`: pick the option with `value` */
async function selectInRow(page: Page, rowLabel: string, value: string) {
    return page.evaluate(
        ([rowLabel, value]) => {
            const row = Array.from(document.querySelectorAll("tr")).find(
                (r) => (r.firstElementChild?.textContent ?? "").trim() === rowLabel
            );
            const sel = row?.querySelector("select") as HTMLSelectElement | null;
            if (!sel) return `no select in row ${rowLabel}`;
            const opts = Array.from(sel.options).map((o) => o.value);
            if (!opts.includes(value)) return `no option ${value}; have ${opts.join(",")}`;
            sel.value = value;
            sel.dispatchEvent(new Event("change", { bubbles: true }));
            return "ok";
        },
        [rowLabel, value]
    );
}

(async () => {
    fs.mkdirSync(artifacts, { recursive: true });
    const scene = sceneFor(fixture.sceneTemplate, OPC, path.join(artifacts, "scene.pro3d"));
    const app = await launchPro3d(scene);
    const pid = app.proc.pid!;
    let peak = 0;
    const poll = setInterval(() => (peak = Math.max(peak, privateMb(pid))), 1000);

    const browser = await chromium.launch();
    const ctx = await browser.newContext({ viewport: { width: 1200, height: 800 } });
    // tsx wraps named functions in __name(...), which also ends up in evaluate() bodies
    await ctx.addInitScript("window.__name = (f) => f");
    const render = await ctx.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 120_000 });
    await surfaceShadersReady(render);
    await render.waitForTimeout(10_000);
    log(`render ready, private ${privateMb(pid)} MB`);

    // --- import SBMT through the menu, with the Electron dialog stubbed
    const main = await ctx.newPage();
    await main.goto(app.url);
    await main.waitForTimeout(3000);
    await main.evaluate((file) => {
        (window as any).aardvark = (window as any).aardvark || {};
        (window as any).aardvark.dialog = {
            showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [file] }),
        };
    }, SBMT);
    const before = privateMb(pid);
    const tImport = Date.now();
    log(`import click: ${await clickText(main, ".item", "Import SBMT Annotations")}`);

    const annos = await ctx.newPage();
    await annos.goto(app.url + "?page=annotations");
    const group = path.basename(SBMT);
    await annos.waitForFunction((g) => (document.body.textContent ?? "").includes(g), group, { timeout: 600_000 });
    log(`import done in ${((Date.now() - tImport) / 1000).toFixed(1)} s, private ${before} -> ${privateMb(pid)} MB`);

    // --- Color by Category: enable, surface attribute, layer, resample
    const props = await ctx.newPage();
    await props.goto(app.url + "?page=properties");
    await props.waitForFunction(() => (document.body.textContent ?? "").includes("attribute type:"), null, { timeout: 60_000 });
    log(`attribute type: ${await selectInRow(props, "attribute type:", "SurfaceAttribute")}`);
    await props.waitForTimeout(1000);
    log(`layer: ${await selectInRow(props, "attribute:", LAYER)}`);
    await props.waitForTimeout(1000);
    const enabled = await props.evaluate(() => {
        const row = Array.from(document.querySelectorAll("tr")).find(
            (r) => (r.firstElementChild?.textContent ?? "").trim() === "enable:"
        );
        const i = row?.querySelector("i") as HTMLElement | null;
        if (!i) return "no checkbox";
        if (!i.className.includes("check")) i.click();
        return "ok";
    });
    log(`enable: ${enabled}`);
    await props.waitForTimeout(2000);

    const beforeResample = privateMb(pid);
    peak = beforeResample;
    const tRes = Date.now();
    log(`resample click: ${await clickText(props, ".button", "resample surface")}`);
    // done when the button is no longer flagged stale
    await props.waitForFunction(
        () => {
            const b = Array.from(document.querySelectorAll(".button")).find((e) =>
                (e.textContent ?? "").startsWith("resample surface")
            );
            return !!b && !(b.textContent ?? "").includes("●");
        },
        null,
        { timeout: 1_800_000, polling: 1000 }
    );
    const resampleSecs = (Date.now() - tRes) / 1000;
    log(`resample done in ${resampleSecs.toFixed(1)} s, private ${beforeResample} -> ${privateMb(pid)} MB, peak ${peak} MB`);

    await render.waitForTimeout(5000);
    await render.screenshot({ path: path.join(artifacts, "render.png") });
    await props.screenshot({ path: path.join(artifacts, "properties.png"), fullPage: true });
    log(`screenshots in ${artifacts}`);

    clearInterval(poll);
    await browser.close();
    await app.stop();
    process.exit(0);
})().catch((e) => {
    console.error(e);
    process.exit(1);
});
