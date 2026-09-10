// Can the viewer be put where the instrument was, and does it then see what the
// instrument saw?
//
// This is the gate the projection tests stand on: an image can only be checked against
// the terrain it is projected onto from the viewpoint it was taken from. Everything here
// is measured against the image's own .mbi.json sidecar, so a regression in the fly-to,
// in the focal-length control, or in the projector pose fails the test rather than
// quietly producing a plausible-looking picture.
//
// It caught a real one: the fly-to used to extract the look direction as
// camToBody.TransformDir(-V3d.OOI), which is only the look direction for a PROPER
// camera-to-world basis. InstrumentProjection.specialTrafos are improper (det -1), so
// the camera landed in exactly the right place pointing exactly the wrong way, and the
// frame came back empty.
import { test, expect } from "@playwright/test";
import { launchPro3d, config } from "../src/pro3d";
import { litFraction, streamLive, diffPng } from "../src/image";
import * as fs from "fs";
import * as path from "path";

/** HERA/AFC-1: hfov = 2*atan(11.84/(2*focal)) = 5.5307 deg */
const AFC_FOCAL_MM = 122.563;
const AFC_HFOV_DEG = 5.5307;
/** Dimorphos' longest axis, metres -- the scale the apparent size is checked against */
const DIMORPHOS_EXTENT_M = 177;

const imageDir = process.env.PRO3D_AFC_DIR ?? config.imageDir;
const artifacts = path.join(__dirname, "..", "artifacts");

/** every {key: {value}} block of the sidecar, flattened */
function mbiHeader(file: string): Record<string, any> {
    const out: Record<string, any> = {};
    for (const blk of JSON.parse(fs.readFileSync(file, "utf-8")).fits_hdu_headers)
        for (const [k, v] of Object.entries<any>(blk)) if (!(k in out)) out[k] = v.value;
    return out;
}

test("the viewer can look at Dimorphos from where the AFC was", async ({ browser }) => {
    const sidecar = fs
        .readdirSync(imageDir)
        .filter((f) => f.endsWith(".mbi.json"))
        .sort()[0];
    expect(sidecar, `no .mbi.json in ${imageDir}`).toBeTruthy();
    const h = mbiHeader(path.join(imageDir, sidecar));
    // TRG_POS is target-minus-spacecraft in km, so the camera sits at -TRG_POS metres
    // from the body, which is the render-space origin (the GIS observer is the body).
    const range =
        1000 *
        Math.hypot(h["TRG_POSX"], h["TRG_POSY"], h["TRG_POSZ"]);
    expect(range).toBeGreaterThan(100);

    fs.mkdirSync(artifacts, { recursive: true });
    const app = await launchPro3d();
    // square window: with the field of view set to a square detector's, a 16:9 window
    // would frame the body by its shorter vertical fov instead of the instrument's
    const page = await (
        await browser.newContext({ viewport: { width: 1100, height: 1100 } })
    ).newPage();
    try {
        await page.goto(app.url + "?page=render");
        await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });

        const settle = async (name: string) => {
            const until = Date.now() + 120_000;
            let shot = await page.screenshot();
            while (!streamLive(shot) && Date.now() < until) {
                await page.waitForTimeout(3000);
                shot = await page.screenshot();
            }
            for (let i = 0; i < 60; i++) {
                await page.waitForTimeout(1000);
                const cur = await page.screenshot();
                const done = diffPng(shot, cur).changedFraction < 0.001;
                shot = cur;
                if (done) break;
            }
            fs.writeFileSync(path.join(artifacts, name), shot);
            return shot;
        };
        await settle("lookat-loaded.png");

        // look through the instrument: hfov 5.53 deg instead of the default 60
        const cfg = await page.context().newPage();
        await cfg.goto(app.url + "?page=config");
        await cfg.waitForLoadState("networkidle");
        // "attached", not "visible": the config rows live in collapsed accordion
        // sections, and they arrive over the incremental DOM channel after
        // networkidle -- setting too early silently misses and leaves the default fov
        await cfg
            .locator("text=Focal (mm):")
            .first()
            .waitFor({ state: "attached", timeout: 30_000 });
        const set = await cfg.evaluate((focal) => {
            const lbl = Array.from(document.querySelectorAll("td, div, span")).find(
                (e) => (e.textContent ?? "").trim() === "Focal (mm):"
            );
            if (!lbl) return "label not found";
            const inputs = Array.from(
                (lbl.closest("tr") ?? lbl.parentElement)?.querySelectorAll("input") ?? []
            );
            const input = inputs[inputs.length - 1] as HTMLInputElement;
            Object.getOwnPropertyDescriptor(
                window.HTMLInputElement.prototype,
                "value"
            )!.set!.call(input, String(focal));
            for (const e of ["input", "change"])
                input.dispatchEvent(new Event(e, { bubbles: true }));
            input.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
            return "set " + input.value;
        }, AFC_FOCAL_MM);
        expect(set).toContain("set");
        await page.waitForTimeout(4000);

        // import the folder and fly to the first image
        const gis = await page.context().newPage();
        await gis.goto(app.url + "?page=gis");
        await gis.waitForLoadState("networkidle");
        await gis.locator("text=Projected Images").first().click();
        await gis.locator("text=Import Directory").first().waitFor({ timeout: 60_000 });
        await gis.evaluate(
            `(() => { window.aardvark = window.aardvark || {}; window.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(
                imageDir.replace(/\\/g, "/")
            )}] }) }; })()`
        );
        await gis.locator("text=Import Directory").first().click();

        const image = sidecar.replace(/\.mbi\.json$/, ".png");
        await gis.locator(`text=${image}`).first().waitFor({ timeout: 120_000 });
        const flown = await gis.evaluate((n) => {
            const all = Array.from(document.querySelectorAll("*")).filter(
                (e) => (e.textContent ?? "").trim() === n
            );
            const deepest = all.filter(
                (e) => !Array.from(e.children).some((c) => all.includes(c))
            );
            let el: Element | null = deepest[0] ?? null;
            while (el) {
                const icon = el.nextElementSibling?.querySelector("i.location.icon");
                if (icon) {
                    (icon as HTMLElement).click();
                    return "clicked";
                }
                el = el.parentElement;
            }
            return "no fly-to icon in the row";
        }, image);
        expect(flown).toBe("clicked");
        await page.waitForTimeout(9000); // the animation runs 3.5 s

        const shot = await settle("lookat-afc.png");

        // The body must be IN the frame. An empty frame is what a mis-signed forward
        // vector produces, and it is indistinguishable from "the projection is broken"
        // unless it is asserted here.
        const lit = litFraction(shot);
        expect(lit, "nothing rendered from the AFC viewpoint").toBeGreaterThan(0.005);

        // ...and it must be the right APPARENT SIZE: the body subtends
        // atan(extent/range), which at the instrument's own fov is a fixed fraction of
        // the frame. This is what distinguishes "flew to the right place" from "flew
        // somewhere that happens to show terrain".
        const expectedFrac =
            ((Math.atan(DIMORPHOS_EXTENT_M / range) * 180) / Math.PI) / AFC_HFOV_DEG;
        const { width, height, bbox } = bodyBox(shot);
        const gotFrac = (bbox.x1 - bbox.x0 + 1) / width;
        console.log(
            `range ${(range / 1000).toFixed(3)} km, body ${(gotFrac * 100).toFixed(1)}% of ` +
                `frame width, expected ${(expectedFrac * 100).toFixed(1)}%`
        );
        expect(gotFrac).toBeGreaterThan(expectedFrac * 0.6);
        expect(gotFrac).toBeLessThan(expectedFrac * 1.6);

        // and roughly centred -- the boresight points at the body
        const cx = (bbox.x0 + bbox.x1) / 2 / width;
        const cy = (bbox.y0 + bbox.y1) / 2 / height;
        expect(Math.abs(cx - 0.5)).toBeLessThan(0.2);
        expect(Math.abs(cy - 0.5)).toBeLessThan(0.2);
    } finally {
        await app.stop();
    }
});

/** bounding box of everything that is not the clear colour, ignoring the overlays */
function bodyBox(png: Buffer) {
    const { PNG } = require("pngjs");
    const img = PNG.sync.read(png);
    const clear = img.data[(img.width * (img.height - 1) + 1) * 4];
    let x0 = img.width, y0 = img.height, x1 = -1, y1 = -1;
    for (let y = 0; y < img.height; y++)
        for (let x = 0; x < img.width; x++) {
            if (x < 460 && y < 240) continue; // HUD text
            // the colour legend runs the full height of the left edge; without this the
            // box spans the whole frame and the apparent-size check is meaningless
            if (x < 130) continue;
            const i = (img.width * y + x) * 4;
            if (Math.abs(img.data[i] - clear) > 6) {
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
        }
    return { width: img.width, height: img.height, bbox: { x0, y0, x1, y1 } };
}
