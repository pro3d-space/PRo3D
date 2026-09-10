import { test, expect, Page } from "@playwright/test";
import { spawnSync } from "child_process";
import { launchPro3d, Pro3d, config } from "../src/pro3d";
import { bodyCoverage, diffPng, litFraction, registration, streamLive } from "../src/image";
import * as fs from "fs";
import * as path from "path";

/**
 * Generate an instrument frame, project it, and check it lands where it came from.
 *
 * 1. `scripts/make-projection-test-data.py` renders one HERA/AFC-1 frame of the OPC
 *    with `pro3d-tool simulate-image --write-mbi` -- the sidecar describes the camera
 *    the render actually used -- and writes a scene that loads the same OPC with
 *    everything projection needs preset (observed body, SPICE-bound surface, epoch,
 *    the viewer's focal length set to the instrument's).
 * 2. The viewer opens that scene, imports the frame, flies to it and projects it,
 *    with Orientation Source MBI and the transfer function off.
 * 3. From the projector's own viewpoint through the instrument's field of view, the
 *    render must reproduce the frame: correlation at zero shift, the peak at zero
 *    shift, the identity beating every mirror and rotation, gray staying gray -- and
 *    the shader's own coverage view must show the frame covering the visible body.
 *
 * There is no metadata to doubt: the frame was rendered from this shape model, so
 * any disagreement is in the projection chain. Run it once per OPC winding --
 * `Dimorphos_opc` is wound outward (NormalFlip 0), `Dimorphos_0_Meridian` inward
 * (NormalFlip 1), and the second is the only thing that exercises the flip.
 *
 *   PRO3D_E2E_OPCS            OPC directories, ';'-separated (default: both workshop3 exports)
 *   PRO3D_E2E_SCENE_TEMPLATE  .pro3d the generated scene is derived from (default PRO3D_SCENE)
 *   PRO3D_SPICE_KERNELS       kernel tree (default: the sibling `spice/kernels` mirror)
 *   PRO3D_PYTHON              interpreter with numpy (default `python`)
 *   PRO3D_E2E_DATE / _EPOCH   observation (default 2027-03-21 20:00:00, the 6.7 km pass)
 *
 * The default epoch is tied to the kernel version: it was chosen against
 * hera_plan_v182_20260820. ESA's plan kernels move HERA's future trajectory between
 * releases -- against hera_plan_v182_20260527 the same epoch puts HERA at 8.0 km with
 * the body outside the AFC frame, and the generator fails ("the body does not appear
 * in the frame"). Point PRO3D_SPICE_KERNELS at a matching tree or change the epoch.
 */

const repoRoot = path.resolve(__dirname, "..", "..");
const artifacts = path.join(__dirname, "..", "artifacts");
const workshop = "C:\\pro3ddata\\HERA\\workshop3";

const opcs = (
    process.env.PRO3D_E2E_OPCS ??
    [
        path.join(workshop, "Dimorphos_opc", "Dimorphos"),
        path.join(workshop, "Dimorphos_0_Meridian", "Dimorphos"),
    ].join(";")
)
    .split(";")
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
const template = process.env.PRO3D_E2E_SCENE_TEMPLATE ?? config.scene;
const kernels =
    process.env.PRO3D_SPICE_KERNELS ?? path.join(repoRoot, "..", "spice", "kernels");
const python = process.env.PRO3D_PYTHON ?? "python";
const date = process.env.PRO3D_E2E_DATE ?? "2027-03-21";
const epoch = process.env.PRO3D_E2E_EPOCH ?? "20:00:00";
// DRACO_2 is the layer HERA looks at on these epochs; the others are near-black
// there, and two near-black images correlate beautifully (docs/ProjectionValidation.md)
const layer = "DRACO_2";

// generation, first-start shader compilation and OPC streaming, per OPC
test.setTimeout(25 * 60_000);
test.describe.configure({ mode: "serial" });

async function settled(page: Page, name: string, out: string): Promise<Buffer> {
    const started = Date.now();
    let shot = await page.screenshot();
    while ((!streamLive(shot) || litFraction(shot) < 0.003) && Date.now() - started < 600_000) {
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
            prev = cur;
            break;
        }
        prev = cur;
    }
    fs.writeFileSync(path.join(out, name), prev);
    return prev;
}

/** one DOM click on an icon in the library row of `name`: the incremental list
 *  re-renders often enough to starve Playwright's actionability retries */
async function clickRowIcon(gis: Page, name: string, icon: string) {
    const r = await gis.evaluate(
        ({ n, i }) => {
            const matches = Array.from(document.querySelectorAll("*")).filter(
                (e) => (e.textContent ?? "").trim() === n
            );
            const deepest = matches.filter(
                (e) => !Array.from(e.children).some((c) => matches.includes(c))
            );
            if (deepest.length === 0) return "row not found";
            let el: Element | null = deepest[0];
            while (el) {
                const box = el.nextElementSibling?.querySelector(i);
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

/** set the <select> next to `label` in the GIS panel */
async function selectBeside(gis: Page, label: string, option: string) {
    const r = await gis.evaluate(
        ({ l, o }) => {
            const el = Array.from(document.querySelectorAll("*")).find(
                (e) => (e.textContent ?? "").trim() === l
            );
            const sel = el?.parentElement?.querySelector("select") as HTMLSelectElement | null;
            if (!sel) return `no select beside ${l}`;
            const opt = Array.from(sel.options).find(
                (x) => x.textContent?.trim() === o || x.value === o
            );
            if (!opt) return `no option ${o}`;
            sel.value = opt.value;
            sel.dispatchEvent(new Event("change", { bubbles: true }));
            return "ok";
        },
        { l: label, o: option }
    );
    expect(r, `${label} -> ${option}`).toBe("ok");
}

for (const opc of opcs) {
    const name = path.basename(path.dirname(opc));

    test(`a frame rendered from ${name} projects back onto it`, async ({ browser }) => {
        test.skip(!fs.existsSync(opc), `OPC not found: ${opc} (set PRO3D_E2E_OPCS)`);
        test.skip(
            !fs.existsSync(template),
            `scene template not found: ${template} (set PRO3D_E2E_SCENE_TEMPLATE)`
        );

        // --- 1. generate the frame and its scene --------------------------------
        const out = path.join(artifacts, "e2e", name);
        fs.rmSync(out, { recursive: true, force: true });
        fs.mkdirSync(out, { recursive: true });
        const gen = spawnSync(
            python,
            [
                path.join(repoRoot, "scripts", "make-projection-test-data.py"),
                "--opc", opc,
                "--out", out,
                "--texture-layer", layer,
                "--scene-template", template,
                "--date", date,
                "--epochs", epoch,
                ...(fs.existsSync(kernels) ? ["--kernel-root", kernels] : []),
            ],
            { encoding: "utf8" }
        );
        console.log(gen.stdout);
        expect(gen.status, `generator failed:\n${gen.stdout}\n${gen.stderr}`).toBe(0);

        const scene = path.join(out, "ProjectionTest.pro3d");
        const frame = `AFC1_${layer.replace("_", "")}_${date.replace(/-/g, "")}_${epoch.replace(/:/g, "")}.png`;
        expect(fs.existsSync(scene), "the generator must write the scene").toBe(true);
        expect(fs.existsSync(path.join(out, frame)), `the generator must render ${frame}`).toBe(true);

        // --- 2. project it in the viewer ----------------------------------------
        let app: Pro3d | undefined;
        try {
            app = await launchPro3d(scene);
            // square: the frame is square, and the scene's focal length is the
            // instrument's, so a square window makes the two the same projection
            const context = await browser.newContext({ viewport: { width: 1100, height: 1100 } });
            const render = await context.newPage();
            await render.goto(app.url + "?page=render");
            await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
            await settled(render, "loaded.png", out);

            const gis = await context.newPage();
            await gis.goto(app.url + "?page=gis");
            await gis.waitForLoadState("networkidle");
            await gis.locator("text=Projected Images").first().click();
            await expect(gis.locator("text=Import Directory").first()).toBeVisible({ timeout: 60_000 });
            const dir = out.replace(/\\/g, "/");
            await gis.evaluate(
                `(() => { window.aardvark = window.aardvark || {}; window.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(dir)}] }) }; })()`
            );
            await gis.locator("text=Import Directory").first().click();
            await expect
                .poll(
                    () =>
                        gis.evaluate(
                            `Array.from(document.querySelectorAll("*")).some(e => (e.textContent || "").trim() === ${JSON.stringify(frame)})`
                        ),
                    { timeout: 120_000 }
                )
                .toBe(true);

            // the sidecar is what is under test, so the projector must read it
            await selectBeside(gis, "Orientation Source:", "MbiBased");
            // untransformed pixels, so the render is comparable with the source
            const tf = await gis.evaluate(() => {
                const el = Array.from(document.querySelectorAll("*")).find(
                    (e) => (e.textContent ?? "").trim() === "Transfer Function:"
                );
                const box = el?.parentElement?.querySelector("i");
                if (!box) return "checkbox not found";
                (box as HTMLElement).click();
                return "clicked";
            });
            expect(tf, "Transfer Function -> off").toBe("clicked");

            await clickRowIcon(gis, frame, "i.location.icon");
            await render.waitForTimeout(5000);
            const baseline = await settled(render, "baseline.png", out);

            await clickRowIcon(gis, frame, "i.plus.icon");
            await expect(gis.locator("text=Projection Stack (1/32)")).toBeVisible({ timeout: 30_000 });
            await render.waitForTimeout(4000);
            const projected = await settled(render, "projected.png", out);

            // --- 3. does it reproduce the frame? -----------------------------------
            const reg = registration(fs.readFileSync(path.join(out, frame)), projected);
            const flips = reg.symmetries.slice(1);
            const margin = reg.symmetries[0] - Math.max(...flips);
            console.log(
                `[${name}] correlation ${reg.zeroShift.toFixed(4)} at zero shift over ${reg.samples} px; ` +
                    `peak ${reg.best.toFixed(4)} at (${reg.bestShift[0]}, ${reg.bestShift[1]}); ` +
                    `identity beats the best mirror/rotation by ${margin.toFixed(4)}; chroma ${reg.chroma.toFixed(2)}`
            );

            // coverage from the shader's own coverage view, which tints every fragment a
            // stack layer covers -- a before/after diff cannot tell "not covered" from
            // "covered by a value that matches the terrain", and here they do match
            await selectBeside(gis, "Visibility:", "RelativeCount");
            await render.waitForTimeout(3000);
            const tinted = await settled(render, "coverage.png", out);
            const c = bodyCoverage(projected, tinted);
            console.log(
                `[${name}] coverage ${(c.coveredFraction * 100).toFixed(1)}% of ${c.bodyPixels} body px, ` +
                    `spill ${(c.spilledFraction * 100).toFixed(2)}%`
            );
            expect(c.coveredFraction, "the frame must cover the body it was rendered from").toBeGreaterThan(0.9);
            expect(c.spilledFraction, "nothing may be painted where there is no surface").toBeLessThan(0.02);

            expect(reg.samples, "the frame must have signal inside the compared window").toBeGreaterThan(5000);
            // measured 0.9705 on Dimorphos_opc (docs/ProjectionValidation.md)
            expect(reg.zeroShift, "the render must reproduce the projected frame").toBeGreaterThan(0.9);
            expect(
                Math.max(Math.abs(reg.bestShift[0]), Math.abs(reg.bestShift[1])),
                "the correlation must peak at zero shift -- no pointing offset"
            ).toBeLessThanOrEqual(2);
            expect(margin, "the identity must beat every mirror and rotation of the frame").toBeGreaterThan(0.2);
            expect(reg.chroma, "a grayscale frame painted untransformed must stay gray").toBeLessThan(12);
            await context.close();
        } finally {
            await app?.stop();
        }
    });
}
