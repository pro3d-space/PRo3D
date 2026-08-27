// Does a given image's projection LAND on the body?
//
// Imports one folder from the scene's current camera (no fly-to, so a sidecar
// whose pointing is wrong cannot send the camera somewhere blank and stall the
// probe), adds the first image to the stack, and reports how much of the body
// the projection repainted. Writes baseline/projected screenshots next to the
// artifacts of the specs.
//
//   PRO3D_PROBE_IMAGE_DIR   folder with the image + its .mbi.json  (required)
//   PRO3D_PROBE_IMAGE       which image to use (default: the first row)
//   PRO3D_PROBE_LABEL       file-name prefix for the screenshots (default "probe")
//
// Unlike projection-overlap.spec.ts this asserts nothing -- it is the tool for
// looking at a case, including the ones that fail.
import { chromium, Page } from "@playwright/test";
import { launchPro3d } from "./pro3d";
import { bodyCoverage, diffPng, litFraction, streamLive } from "./image";
import * as fs from "fs";
import * as path from "path";

const dir = process.env.PRO3D_PROBE_IMAGE_DIR;
const label = process.env.PRO3D_PROBE_LABEL ?? "probe";
const artifacts = path.join(__dirname, "..", "artifacts");

async function settled(page: Page, name: string): Promise<Buffer> {
    const started = Date.now();
    let shot = await page.screenshot();
    while (
        (!streamLive(shot) || litFraction(shot) < 0.003) &&
        Date.now() - started < 600_000
    ) {
        await page.waitForTimeout(3000);
        shot = await page.screenshot();
    }
    let prev = shot;
    for (let i = 0; i < 60; i++) {
        await page.waitForTimeout(1000);
        const cur = await page.screenshot();
        if (diffPng(prev, cur).changedFraction < 0.001) {
            fs.writeFileSync(path.join(artifacts, name), cur);
            return cur;
        }
        prev = cur;
    }
    fs.writeFileSync(path.join(artifacts, name), prev);
    return prev;
}

(async () => {
    if (!dir || !fs.existsSync(dir)) {
        console.error("set PRO3D_PROBE_IMAGE_DIR to a folder with an image + .mbi.json");
        process.exit(2);
    }
    fs.mkdirSync(artifacts, { recursive: true });

    const app = await launchPro3d();
    const browser = await chromium.launch();
    // PRO3D_PROBE_VIEWPORT=WxH. Square matters once the viewer's field of view is set to
    // a square detector's: in a 16:9 window the vertical fov is the shorter one, so the
    // framing would no longer be the instrument's.
    const [vw, vh] = (process.env.PRO3D_PROBE_VIEWPORT ?? "1600x900")
        .split("x")
        .map((n) => parseInt(n, 10));
    const context = await browser.newContext({ viewport: { width: vw, height: vh } });

    const render = await context.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    const baseline = await settled(render, `${label}-baseline.png`);

    const gis = await context.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await gis.locator("text=Import Directory").first().waitFor({ timeout: 60_000 });
    await gis.evaluate(
        `(() => { window.aardvark = window.aardvark || {}; window.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(
            dir.replace(/\\/g, "/")
        )}] }) }; })()`
    );
    await gis.locator("text=Import Directory").first().click();

    // "Orientation Source": Spice (the default -- boresight forced onto the body
    // centre, arbitrary roll) vs MbiBased (the image's measured attitude)
    const method = process.env.PRO3D_PROBE_METHOD;
    if (method) {
        const r = await gis.evaluate((m) => {
            const label = Array.from(document.querySelectorAll("*")).find(
                (e) => (e.textContent ?? "").trim() === "Orientation Source:"
            );
            if (!label) return "label not found";
            const row = label.parentElement;
            const sel = row?.querySelector("select") as HTMLSelectElement | null;
            if (!sel) return "no select: " + (row?.outerHTML ?? "").slice(0, 400);
            const opt = Array.from(sel.options).find(
                (o) => o.textContent?.trim() === m || o.value === m
            );
            if (!opt) return "no option " + m + " in " + Array.from(sel.options).map((o) => o.textContent).join("|");
            sel.value = opt.value;
            sel.dispatchEvent(new Event("change", { bubbles: true }));
            return "set " + opt.textContent;
        }, method);
        console.log(`orientation source -> ${method}: ${r}`);
        await render.waitForTimeout(3000);
    }

    // PRO3D_PROBE_TRANSFER=off unticks "Transfer Function", so the projected
    // image is painted as its own RGB. That is the only way the render can be
    // compared with the source image rather than with a colour-mapped version
    // of it.
    if ((process.env.PRO3D_PROBE_TRANSFER ?? "").toLowerCase() === "off") {
        const r = await gis.evaluate(() => {
            const label = Array.from(document.querySelectorAll("*")).find(
                (e) => (e.textContent ?? "").trim() === "Transfer Function:"
            );
            const box = label?.parentElement?.querySelector("i");
            if (!box) return "checkbox not found";
            (box as HTMLElement).click();
            return "clicked";
        });
        console.log(`transfer function -> off: ${r}`);
        await render.waitForTimeout(3000);
    }

    const wanted =
        process.env.PRO3D_PROBE_IMAGE ??
        fs
            .readdirSync(dir)
            .filter((f) => /\.(png|tif|tiff)$/i.test(f))
            .sort()[0];
    await gis
        .locator(`text=${wanted}`)
        .first()
        .waitFor({ timeout: 120_000 });

    const clicked = await gis.evaluate((n) => {
        const matches = Array.from(document.querySelectorAll("*")).filter(
            (e) => (e.textContent ?? "").trim() === n
        );
        const deepest = matches.filter(
            (e) => !Array.from(e.children).some((c) => matches.includes(c))
        );
        if (deepest.length === 0) return "header not found";
        let el: Element | null = deepest[0];
        while (el) {
            const box = el.nextElementSibling?.querySelector("i.plus.icon");
            if (box) {
                (box as HTMLElement).click();
                return "clicked";
            }
            el = el.parentElement;
        }
        return "no plus icon";
    }, wanted);
    console.log(`add ${wanted} to stack: ${clicked}`);

    // PRO3D_PROBE_FLYTO=1 uses the GIS tab's own fly-to (the location arrow on the
    // image's row), which puts the camera on that image's projector axis at the standoff
    // that frames its footprint in the viewer's field of view. With the viewer's focal
    // length set to the instrument's, that standoff IS the instrument's own distance.
    if (process.env.PRO3D_PROBE_FLYTO === "1") {
        const flew = await gis.evaluate((n) => {
            const matches = Array.from(document.querySelectorAll("*")).filter(
                (e) => (e.textContent ?? "").trim() === n
            );
            const deepest = matches.filter(
                (e) => !Array.from(e.children).some((c) => matches.includes(c))
            );
            if (deepest.length === 0) return "header not found";
            let el: Element | null = deepest[0];
            while (el) {
                const box = el.nextElementSibling?.querySelector("i.location.icon");
                if (box) {
                    (box as HTMLElement).click();
                    return "clicked";
                }
                el = el.parentElement;
            }
            return "no fly-to icon in row";
        }, wanted);
        console.log(`fly to ${wanted}: ${flew}`);
        await render.waitForTimeout(9000);   // the animation runs 3.5 s
    }

    await render.waitForTimeout(6000);
    const projected = await settled(render, `${label}-projected.png`);

    const c = bodyCoverage(baseline, projected);
    console.log(
        `[${label}] body pixels ${c.bodyPixels}, covered ${(c.coveredFraction * 100).toFixed(1)}%, spilled ${(c.spilledFraction * 100).toFixed(2)}%`
    );

    await browser.close();
    await app.stop();
})();
