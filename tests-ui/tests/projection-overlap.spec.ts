import { test, expect, Page } from "@playwright/test";
import { launchPro3d, Pro3d, config } from "../src/pro3d";
import { bodyCoverage, diffPng, litFraction, streamLive } from "../src/image";
import * as fs from "fs";
import * as path from "path";

/**
 * Closed-loop check of the projection geometry.
 *
 * `pro3d-tool simulate-image --write-mbi` renders the body from a camera it
 * chose and writes an .mbi.json describing exactly that camera. Importing the
 * pair here and projecting it back onto the SAME OPC must therefore repaint the
 * body: the projector and the render camera are the same camera, so anything
 * short of full coverage is a real disagreement in the chain (viewer, shader,
 * or sidecar), not a question of whether the source metadata was right.
 *
 * That is what separates this from projection-smoke.spec.ts, which only asks
 * whether the render CHANGED. A projector aimed 100 degrees away also changes
 * the render (the false-colour legend, the stack-coverage tint); it just does
 * not land on the body. This measures landing.
 *
 * Produce the input with (paths as appropriate for the machine):
 *
 *   pro3d-tool simulate-image --opc <the scene's OPC> --time <epoch>
 *       --body DIMORPHOS --frame DIMORPHOS_FIXED --observer HERA
 *       --instrument HERA_AFC-1 --out <dir>/SIM_AFC1.png --write-mbi
 *
 * and point PRO3D_SIM_IMAGE_DIR at <dir>. The OPC must be the one the scene
 * loads -- projecting a render of one shape model onto another is a different
 * (and worthwhile) experiment, but not this one.
 */

let app: Pro3d;
const artifacts = path.join(__dirname, "..", "artifacts");

// a changed surface effect compiles the big program from scratch on first
// start -- minutes during which the view stays empty
test.setTimeout(15 * 60_000);

/** folder holding the tool's render + its sidecar */
const simDir = process.env.PRO3D_SIM_IMAGE_DIR;

test.beforeAll(async () => {
    fs.mkdirSync(artifacts, { recursive: true });
    app = await launchPro3d();
});

test.afterAll(async () => {
    await app?.stop();
});

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
    expect(
        litFraction(shot),
        `render view still empty after ${Math.round((Date.now() - started) / 1000)}s (${name})`
    ).toBeGreaterThan(0.003);
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

/** click an icon in the library row of `name`; the rows re-render often enough
 *  that Playwright's actionability retry loop can starve, hence one DOM click */
async function clickRowIcon(gis: Page, name: string, icon: string) {
    const r = await gis.evaluate(
        ({ n, i }) => {
            const matches = Array.from(document.querySelectorAll("*")).filter(
                (e) => (e.textContent ?? "").trim() === n
            );
            const deepest = matches.filter(
                (e) => !Array.from(e.children).some((c) => matches.includes(c))
            );
            if (deepest.length === 0) return "header not found";
            let el: Element | null = deepest[0];
            while (el) {
                const row = el.nextElementSibling;
                const box = row?.querySelector(i);
                if (box) {
                    (box as HTMLElement).click();
                    return "clicked";
                }
                el = el.parentElement;
            }
            return `no ${i} in row`;
        },
        { n: name, i: icon }
    );
    expect(r, `${icon} on ${name}`).toBe("clicked");
}

test("a self-rendered image projects back onto the body it was rendered from", async ({
    browser,
}) => {
    test.skip(
        !simDir || !fs.existsSync(simDir),
        "set PRO3D_SIM_IMAGE_DIR to a folder holding a `simulate-image --write-mbi` render (see the header of this file)"
    );

    const images = fs
        .readdirSync(simDir!)
        .filter((f) => /\.(png|tif|tiff)$/i.test(f) && !f.endsWith(".mbi.json"));
    expect(images.length, `no image in ${simDir}`).toBeGreaterThan(0);
    const image = process.env.PRO3D_SIM_IMAGE ?? images[0];
    expect(
        fs.existsSync(path.join(simDir!, image.replace(/\.[^.]+$/, ".mbi.json"))),
        `${image} has no .mbi.json sidecar -- was it rendered with --write-mbi?`
    ).toBe(true);

    const context = await browser.newContext();
    context.on("weberror", (e) => console.log("[page error]", e.error()));

    const render = await context.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });

    // --- import the tool's output --------------------------------------------
    const gis = await context.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await expect(gis.locator("text=Import Directory").first()).toBeVisible({
        timeout: 60_000,
    });

    const dir = simDir!.replace(/\\/g, "/");
    await gis.evaluate(
        `(() => { window.aardvark = window.aardvark || {}; window.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(dir)}] }) }; })()`
    );
    await gis.locator("text=Import Directory").first().click();
    await expect
        .poll(
            () =>
                gis.evaluate(
                    `Array.from(document.querySelectorAll("*")).some(e => (e.textContent || "").trim() === ${JSON.stringify(image)})`
                ),
            { timeout: 120_000 }
        )
        .toBe(true);

    // The sidecar is the thing under test, so the projector has to read it: the
    // scene default (SPICE) ignores the image's attitude and aims at the body
    // centre, which would pass this test for a render that agreed with nothing.
    const orientation = process.env.PRO3D_ORIENTATION_SOURCE ?? "MbiBased";
    const set = await gis.evaluate((m) => {
        const label = Array.from(document.querySelectorAll("*")).find(
            (e) => (e.textContent ?? "").trim() === "Orientation Source:"
        );
        const sel = label?.parentElement?.querySelector("select") as HTMLSelectElement | null;
        if (!sel) return "not found";
        const opt = Array.from(sel.options).find(
            (o) => o.textContent?.trim() === m || o.value === m
        );
        if (!opt) return "no option " + m;
        sel.value = opt.value;
        sel.dispatchEvent(new Event("change", { bubbles: true }));
        return "ok";
    }, orientation);
    expect(set, `orientation source -> ${orientation}`).toBe("ok");
    await render.waitForTimeout(3000);

    // --- look along the projector axis ---------------------------------------
    // Fly-to puts the camera on the image's own axis, so the body fills the view
    // the way the projector sees it -- coverage measured from anywhere else
    // would be dominated by whichever limb happens to face the camera.
    await clickRowIcon(gis, image, "i.location.icon");
    await render.waitForTimeout(8000); // the animation runs 3.5 s
    const baseline = await settled(render, "overlap-baseline.png");

    // --- project it ----------------------------------------------------------
    await clickRowIcon(gis, image, "i.plus.icon");
    await expect(gis.locator("text=Projection Stack (1/32)")).toBeVisible({
        timeout: 30_000,
    });
    await render.waitForTimeout(4000);
    const projected = await settled(render, "overlap-projected.png");

    const c = bodyCoverage(baseline, projected);
    console.log(
        `body pixels ${c.bodyPixels}, covered ${(c.coveredFraction * 100).toFixed(1)}%, spilled into space ${(c.spilledFraction * 100).toFixed(2)}%`
    );

    expect(c.bodyPixels, "the body must be visible after fly-to").toBeGreaterThan(
        2000
    );
    // The render and the projector are the same camera, so the whole visible
    // disk is inside the frustum. What is left over is the limb, where the
    // projection grazes the surface and falls off, plus terrain the projector
    // sees at such an angle that its contribution is below the diff threshold.
    expect(
        c.coveredFraction,
        "an image rendered from this very camera must repaint the body it came from -- " +
            "low coverage means the projector and the render camera disagree"
    ).toBeGreaterThan(0.8);
    // A projector that lands beside the body paints the space around it; a
    // correct one cannot, because there is no geometry there to receive it.
    expect(
        c.spilledFraction,
        "the projection must not change pixels where there is no surface"
    ).toBeLessThan(0.02);
});
