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
// Unlike projection-e2e.spec.ts this asserts nothing -- it is the tool for
// looking at a case, including the ones that fail.
import { chromium, Page } from "@playwright/test";
import { launchPro3d } from "./pro3d";
import { bodyCoverage, diffPng, litFraction, streamLive } from "./image";
import * as fs from "fs";
import * as path from "path";

const dir = process.env.PRO3D_PROBE_IMAGE_DIR;
const label = process.env.PRO3D_PROBE_LABEL ?? "probe";
const artifacts = path.join(__dirname, "..", "artifacts");

// How long to wait for lit content before giving up. The first start after a shader
// change compiles the surface program and can genuinely take minutes, but every other
// run that reaches this ceiling has simply failed -- and paying 10 minutes per settle to
// find that out (twice per probe) is most of a lost afternoon. Raise it deliberately via
// PRO3D_PROBE_SETTLE_MS when a recompile is expected.
const settleMs = Number(process.env.PRO3D_PROBE_SETTLE_MS ?? 120_000);

async function settled(page: Page, name: string): Promise<Buffer> {
    const started = Date.now();
    let shot = await page.screenshot();
    while (
        (!streamLive(shot) || litFraction(shot) < 0.003) &&
        Date.now() - started < settleMs
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
    // wait for the scene, but do NOT take the baseline yet: with PRO3D_PROBE_FLYTO the
    // camera still has to move, and a baseline from a different viewpoint makes every
    // pixel "changed"
    await settled(render, `${label}-loaded.png`);

    // PRO3D_PROBE_FOCAL sets "Focal (mm)" on the Config page, which drives the
    // INTERACTIVE view's frustum (hfov = 2*atan(11.84/(2*focal))). 122.563 mm is
    // HERA/AFC-1's 5.5307 degrees, i.e. looking through the instrument.
    // Near/Far matter as much as the focal here: at the instrument's own distance the body
    // sits ~8 km out, and a far plane inside that clips it away entirely -- an empty frame
    // that looks exactly like a broken projection. Both rebuild m.frustum (Viewer.fs:
    // SetNearPlane/SetFarPlane preserve fov and aspect; UpdateFocal preserves near/far).
    const cfgFields: Array<[string, string | undefined]> = [
        ["Near Plane:", process.env.PRO3D_PROBE_NEAR],
        ["Far Plane:", process.env.PRO3D_PROBE_FAR],
        ["Focal (mm):", process.env.PRO3D_PROBE_FOCAL],
    ];
    if (cfgFields.some(([, v]) => v)) {
        const cfg = await context.newPage();
        await cfg.goto(app.url + "?page=config");
        await cfg.waitForLoadState("networkidle");
        for (const [labelText, value] of cfgFields) {
            if (!value) continue;
            // networkidle is not enough: the config page's rows arrive over the incremental
            // DOM channel afterwards, so a field set too early silently reports
            // "label not found" and the viewer keeps its default frustum
            // "attached", not "visible": the rows live inside collapsed accordion
            // sections, so they are in the DOM (and settable) while never being visible
            await cfg
                .locator(`text=${labelText}`)
                .first()
                .waitFor({ state: "attached", timeout: 30_000 });
            const r = await cfg.evaluate(
                ({ labelText, value }) => {
                    const label = Array.from(
                        document.querySelectorAll("td, div, span")
                    ).find((e) => (e.textContent ?? "").trim() === labelText);
                    if (!label) return "label not found";
                    const row = label.closest("tr") ?? label.parentElement;
                    // the slider comes first in the DOM for Focal; the box is the last input
                    const inputs = Array.from(row?.querySelectorAll("input") ?? []);
                    const input = inputs[inputs.length - 1] as HTMLInputElement | undefined;
                    if (!input) return "no input in the row";
                    const setter = Object.getOwnPropertyDescriptor(
                        window.HTMLInputElement.prototype, "value"
                    )!.set!;
                    setter.call(input, value);
                    input.dispatchEvent(new Event("input", { bubbles: true }));
                    input.dispatchEvent(new Event("change", { bubbles: true }));
                    input.dispatchEvent(
                        new KeyboardEvent("keydown", { key: "Enter", bubbles: true })
                    );
                    return "set " + input.value;
                },
                { labelText, value }
            );
            console.log(`${labelText} -> ${value}: ${r}`);
            await render.waitForTimeout(1500);
            // one screenshot per field: an empty frame three steps later is impossible to
            // attribute, and every "the viewer renders nothing" hunt in this file so far
            // has cost hours because the first empty frame was never localised
            const s = await render.screenshot();
            fs.writeFileSync(
                path.join(artifacts, `${label}-after-${labelText.replace(/[^a-z]/gi, "")}.png`),
                s
            );
            console.log(`   lit after ${labelText} ${(litFraction(s) * 100).toFixed(2)}%`);
        }
        await render.waitForTimeout(3000);
    }

    // PRO3D_PROBE_SCALAR picks a scalar layer in Surfaces -> "Scalars:" (e.g. "Slope"),
    // and PRO3D_PROBE_FALSECOLOR=1 ticks that layer's false-colour box.
    //
    // This is the underlay for the strongest projection test available: the surface
    // renders a garish scalar ramp that looks nothing like an instrument image, the
    // projection overwrites it wherever it covers, and so any body pixel still carrying
    // saturated colour is a pixel the projection missed. Coverage becomes an exact
    // saturation test rather than a "did this pixel change" heuristic, and there is no
    // way for a broken projection to score well by leaving the terrain alone.
    //
    // It has to go through the UI: `selectedScalar` written into the .pro3d file is
    // ignored on load (measured -- the render is pixel-identical to the untouched scene).
    const scalar = process.env.PRO3D_PROBE_SCALAR;
    if (scalar) {
        const surf = await context.newPage();
        await surf.goto(app.url + "?page=surfaces");
        await surf.waitForLoadState("networkidle");
        // the surface tree arrives over the incremental DOM channel well after
        // networkidle; without this the click below finds an empty list
        await surf
            .locator(`text=${process.env.PRO3D_PROBE_SURFACE ?? "Dimorphos"}`)
            .first()
            .waitFor({ state: "attached", timeout: 60_000 });
        await surf.waitForTimeout(1500);
        // the properties pane only renders for the SELECTED surface, so click the
        // surface entry first -- otherwise "Scalars:" is simply not in the DOM
        // the list renders entries as "<index>|<name>", e.g. "0|Dimorphos", so match on
        // containment rather than equality
        const picked = await surf.evaluate((name) => {
            const all = Array.from(document.querySelectorAll("span, div, a")).filter(
                (e) => {
                    const t = (e.textContent ?? "").trim();
                    return t.endsWith("|" + name) || t === name;
                }
            );
            const deepest = all.filter(
                (e) => !Array.from(e.children).some((c) => all.includes(c))
            );
            if (!deepest.length) return "surface '" + name + "' not in the list";
            (deepest[0] as HTMLElement).click();
            return "selected '" + (deepest[0].textContent ?? "").trim() + "'";
        }, process.env.PRO3D_PROBE_SURFACE ?? "Dimorphos");
        console.log(`surface: ${picked}`);
        await surf.waitForTimeout(2500);
        await surf
            .locator("text=Scalars:")
            .first()
            .waitFor({ state: "attached", timeout: 60_000 });
        // Playwright's selectOption, not a hand-dispatched "change": aardvark.media's
        // UI.dropDown'' does not act on a synthetic event (measured -- the call reported
        // "set Slope" and the render did not change one pixel), while selectOption drives
        // the control the way a user does.
        const sel = surf
            .locator("tr", { has: surf.locator("text=Scalars:") })
            .locator("select")
            .first();
        let r: string;
        try {
            await sel.selectOption({ label: scalar }, { timeout: 20_000 });
            r = "selected";
        } catch (e) {
            const opts = await surf
                .locator("select")
                .first()
                .evaluate((s: any) =>
                    Array.from(s.options).map((o: any) => o.textContent).join("|")
                )
                .catch(() => "?");
            r = `FAILED (${(e as Error).message.split("\n")[0]}); options seen: ${opts}`;
        }
        console.log(`scalar layer -> ${scalar}: ${r}`);
        await render.waitForTimeout(5000);

        if (process.env.PRO3D_PROBE_FALSECOLOR === "1") {
            const fc = await surf.evaluate(() => {
                const hit = Array.from(document.querySelectorAll("td, div, span, label")).filter(
                    (e) => /false\s*colou?rs?/i.test((e.textContent ?? "").trim()) &&
                           (e.textContent ?? "").trim().length < 40
                );
                for (const h of hit) {
                    const row = h.closest("tr") ?? h.parentElement;
                    const box = row?.querySelector("i.checkbox, input[type=checkbox], i");
                    if (box) {
                        (box as HTMLElement).click();
                        return "clicked on '" + (h.textContent ?? "").trim() + "'";
                    }
                }
                return "no false-colour control found";
            });
            console.log(`false colours: ${fc}`);
            await render.waitForTimeout(4000);
        }
        const s = await render.screenshot();
        fs.writeFileSync(path.join(artifacts, `${label}-underlay.png`), s);
    }

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

    // PRO3D_PROBE_VISIBILITY=RelativeCount switches the "Visibility:" dropdown to the
    // coverage view, which tints each fragment by how many stack layers cover it.
    // This is the only HONEST coverage measurement: comparing a before/after screenshot
    // counts pixels that CHANGED, which cannot tell "not covered" apart from "covered by
    // a value that happens to match the terrain underneath".
    const visibility = process.env.PRO3D_PROBE_VISIBILITY;
    if (visibility) {
        const r = await gis.evaluate((v) => {
            const label = Array.from(document.querySelectorAll("*")).find(
                (e) => (e.textContent ?? "").trim() === "Visibility:"
            );
            const sel = label?.parentElement?.querySelector("select") as HTMLSelectElement | null;
            if (!sel) return "no select for Visibility:";
            const opt = Array.from(sel.options).find(
                (o) => o.textContent?.trim() === v || o.value === v
            );
            if (!opt)
                return "no option " + v + " in " + Array.from(sel.options).map((o) => o.textContent).join("|");
            sel.value = opt.value;
            sel.dispatchEvent(new Event("change", { bubbles: true }));
            return "set " + opt.textContent;
        }, visibility);
        console.log(`visibility -> ${visibility}: ${r}`);
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

    /** click an icon in the library row of `name` -- one DOM click, because the
     *  incremental list re-renders often enough to starve an actionability loop */
    const clickRowIcon = async (icon: string) =>
        await gis.evaluate(
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
                    const box = el.nextElementSibling?.querySelector(i);
                    if (box) {
                        (box as HTMLElement).click();
                        return "clicked";
                    }
                    el = el.parentElement;
                }
                return `no ${i} in row`;
            },
            { n: wanted, i: icon }
        );

    // Fly FIRST, then take the baseline: the comparison is only meaningful between two
    // frames from the same viewpoint, and fly-to moves the camera.
    //
    // PRO3D_PROBE_FLYTO=1 uses the GIS tab's own fly-to (the location arrow on the
    // image's row), which puts the camera on that image's projector axis at the standoff
    // that frames its footprint in the viewer's field of view. With the viewer's focal
    // length set to the instrument's, that standoff IS the instrument's own distance.
    if (process.env.PRO3D_PROBE_FLYTO === "1") {
        console.log(`fly to ${wanted}: ${await clickRowIcon("i.location.icon")}`);
        await render.waitForTimeout(9000);   // the animation runs 3.5 s
        const s = await render.screenshot();
        fs.writeFileSync(path.join(artifacts, `${label}-after-flyto.png`), s);
        console.log(`   lit after fly-to ${(litFraction(s) * 100).toFixed(2)}%`);

        // PRO3D_PROBE_FOCAL_AFTER widens the fov again once the camera has arrived.
        // An empty frame at the instrument's own 5.5 degrees says nothing about WHY it is
        // empty: at 60 degrees the body is still 1/48 of the frame, so if the camera is
        // pointing anywhere near it the body shows up and the pointing is fine; if 60
        // degrees is empty too, the camera is aimed somewhere else entirely.
        const after = process.env.PRO3D_PROBE_FOCAL_AFTER;
        if (after) {
            const cfg2 = await context.newPage();
            await cfg2.goto(app.url + "?page=config");
            await cfg2.waitForLoadState("networkidle");
            await cfg2
                .locator("text=Focal (mm):")
                .first()
                .waitFor({ state: "attached", timeout: 30_000 });
            const r = await cfg2.evaluate((v) => {
                const lbl = Array.from(document.querySelectorAll("td, div, span")).find(
                    (e) => (e.textContent ?? "").trim() === "Focal (mm):"
                );
                const inputs = Array.from(
                    (lbl!.closest("tr") ?? lbl!.parentElement)?.querySelectorAll("input") ?? []
                );
                const input = inputs[inputs.length - 1] as HTMLInputElement;
                Object.getOwnPropertyDescriptor(
                    window.HTMLInputElement.prototype, "value"
                )!.set!.call(input, v);
                for (const e of ["input", "change"])
                    input.dispatchEvent(new Event(e, { bubbles: true }));
                input.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
                return "set " + input.value;
            }, after);
            await render.waitForTimeout(4000);
            const w = await render.screenshot();
            fs.writeFileSync(path.join(artifacts, `${label}-after-flyto-wide.png`), w);
            console.log(`   focal -> ${after}: ${r}; lit wide ${(litFraction(w) * 100).toFixed(2)}%`);
        }
    }

    const baseline = await settled(render, `${label}-baseline.png`);

    console.log(`add ${wanted} to stack: ${await clickRowIcon("i.plus.icon")}`);

    await render.waitForTimeout(6000);
    const projected = await settled(render, `${label}-projected.png`);

    const c = bodyCoverage(baseline, projected);
    console.log(
        `[${label}] body pixels ${c.bodyPixels}, covered ${(c.coveredFraction * 100).toFixed(1)}%, spilled ${(c.spilledFraction * 100).toFixed(2)}%`
    );

    await browser.close();
    await app.stop();
})();
