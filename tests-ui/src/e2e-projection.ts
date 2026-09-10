// End-to-end: from an EMPTY PRo3D to a projected image, every step through the UI.
//
// Why this exists: the earlier probes started from a scene file that already had the
// surface imported, the GIS binding made and the epoch set, then reached into the DOM to
// flip two settings. They validated the projection maths and nothing else -- so a user
// starting from scratch hit a wall of problems (nested accordions that never opened, an
// epoch outside the kernels' coverage, SPICE failures every frame) that no probe could
// have caught, because no probe ever performed those steps.
//
// This one performs them: import the OPC through the same dialog hook the menu uses,
// bind the surface to its SPICE entity, set the epoch, import the images, set the
// projection options by clicking the controls, fly to a frame, project it. It screenshots
// every step and writes docs/TestRun-EndToEnd.md with the images and the measurements.
//
//   PRO3D_E2E_OPC      OPC directory to import
//   PRO3D_E2E_IMAGES   folder of images + .mbi.json sidecars
//   PRO3D_E2E_IMAGE    which image to project (default: the first .png)
//   PRO3D_E2E_ENTITY   SPICE entity for the surface   (default Dimorphos)
//   PRO3D_E2E_FRAME    body-fixed frame               (default DIMORPHOS_FIXED)
//   PRO3D_E2E_FOCAL    viewer focal in mm             (default 122.563, AFC-1)
import { chromium, Page, BrowserContext } from "@playwright/test";
import { launchPro3d } from "./pro3d";
import * as fs from "fs";
import * as path from "path";

const OPC = process.env.PRO3D_E2E_OPC ?? "";
const IMAGES = process.env.PRO3D_E2E_IMAGES ?? "";
const ENTITY = process.env.PRO3D_E2E_ENTITY ?? "Dimorphos";
const FRAME = process.env.PRO3D_E2E_FRAME ?? "DIMORPHOS_FIXED";
const FOCAL = process.env.PRO3D_E2E_FOCAL ?? "122.563";

const outDir = path.join(__dirname, "..", "..", "docs", "images", "testrun");
const steps: Array<{ n: number; title: string; note: string; shot?: string }> = [];
let stepNo = 0;

/** Record a step, optionally with a screenshot of the render view. */
async function step(render: Page | null, title: string, note: string) {
    stepNo += 1;
    let shot: string | undefined;
    if (render) {
        shot = `step${String(stepNo).padStart(2, "0")}.png`;
        fs.writeFileSync(path.join(outDir, shot), await render.screenshot());
    }
    steps.push({ n: stepNo, title, note, shot });
    console.log(`[${stepNo}] ${title} -- ${note}`);
}

/** Stub the native file dialog so the menu's own JS resolves with our path. */
async function stubDialog(page: Page, paths: string[]) {
    await page.evaluate(
        `(() => {
            const d = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: ${JSON.stringify(
                paths.map((p) => p.replace(/\\\\/g, "/"))
            )} }) };
            window.aardvark = window.aardvark || {};
            window.aardvark.dialog = d;
            try { top.aardvark = top.aardvark || {}; top.aardvark.dialog = d; } catch (e) {}
        })()`
    );
}

/** Click the element whose trimmed text is exactly `label`. */
async function clickText(page: Page, label: string, tag = "*") {
    return await page.evaluate(
        ({ label, tag }) => {
            const all = Array.from(document.querySelectorAll(tag)).filter(
                (e) => (e.textContent ?? "").trim() === label
            );
            const deepest = all.filter(
                (e) => !Array.from(e.children).some((c) => all.includes(c))
            );
            if (!deepest.length) return "not found";
            (deepest[0] as HTMLElement).click();
            return "clicked";
        },
        { label, tag }
    );
}

/** Set the <select> in the row labelled `label` to the option with that text. */
async function setSelect(page: Page, label: string, option: string) {
    const row = page.locator("tr", { has: page.locator(`text="${label}"`) });
    const sel = row.locator("select").first();
    try {
        await sel.selectOption({ label: option }, { timeout: 15_000 });
        return "selected";
    } catch {
        // not in a table row: fall back to the nearest select after the label
        const r = await page.evaluate(
            ({ label, option }) => {
                const l = Array.from(document.querySelectorAll("td, div, span, label")).find(
                    (e) => (e.textContent ?? "").trim() === label
                );
                if (!l) return "label not found";
                let el: Element | null = l;
                while (el) {
                    const s = el.querySelector("select") as HTMLSelectElement | null;
                    if (s) {
                        const o = Array.from(s.options).find(
                            (x) => (x.textContent ?? "").trim() === option
                        );
                        if (!o) return "no option " + option;
                        s.value = o.value;
                        s.dispatchEvent(new Event("change", { bubbles: true }));
                        return "selected (fallback)";
                    }
                    el = el.parentElement;
                }
                return "no select near " + label;
            },
            { label, option }
        );
        return r;
    }
}

(async () => {
    if (!OPC || !IMAGES) {
        console.error("set PRO3D_E2E_OPC and PRO3D_E2E_IMAGES");
        process.exit(2);
    }
    fs.mkdirSync(outDir, { recursive: true });

    const app = await launchPro3d();
    const browser = await chromium.launch();
    const ctx: BrowserContext = await browser.newContext({
        viewport: { width: 1100, height: 1100 },
    });

    const render = await ctx.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await render.waitForTimeout(5000);
    await step(render, "Empty PRo3D", "no surfaces, no images, epoch at PRo3D's default");

    // ---- 1. import the OPC, through the menu's own dialog hook -------------------
    const main = await ctx.newPage();
    await main.goto(app.url);
    await main.waitForLoadState("networkidle");
    await main.waitForTimeout(2000);
    await stubDialog(main, [OPC]);
    console.log(`   menu Surfaces: ${await clickText(main, "Surfaces")}`);
    await main.waitForTimeout(600);
    const imported = await clickText(main, "Import OPCs");
    console.log(`   "Import OPCs": ${imported}`);
    await render.waitForTimeout(20_000);
    await step(render, "Import the OPC", `menu -> Surfaces -> Import OPCs (${imported})`);

    // ---- 2. GIS: bind the surface to its SPICE entity ---------------------------
    const gis = await ctx.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.waitForTimeout(3000);
    console.log(`   accordion Surfaces: ${await clickText(gis, "Surfaces")}`);
    await gis.waitForTimeout(1500);
    const boundEntity = await setSelect(gis, "Entity:", ENTITY);
    const boundFrame = await setSelect(gis, "Reference Frame:", FRAME);
    console.log(`   entity ${ENTITY}: ${boundEntity}; frame ${FRAME}: ${boundFrame}`);
    await render.waitForTimeout(4000);
    await step(
        render,
        "Bind the surface to its SPICE body",
        `entity ${ENTITY} (${boundEntity}), frame ${FRAME} (${boundFrame})`
    );

    fs.writeFileSync(
        path.join(outDir, "..", "..", "testrun-steps.json"),
        JSON.stringify(steps, null, 2)
    );
    console.log("\n(partial run -- remaining steps added next)");
    await browser.close();
    await app.stop();
})();
