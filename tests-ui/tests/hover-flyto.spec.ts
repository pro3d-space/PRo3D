import { test, expect, Page } from "@playwright/test";
import { launchPro3d, Pro3d, config } from "../src/pro3d";
import { diffPng, litFraction, streamLive } from "../src/image";
import * as fs from "fs";
import * as path from "path";

/**
 * Phase 3 behaviors: hovering a library image previews it as the top stack
 * layer (with footprint outline + frustum wireframe), and fly-to animates the
 * camera onto the image's projector axis.
 */

let app: Pro3d;
const artifacts = path.join(__dirname, "..", "artifacts");

// a changed surface effect means the driver compiles the big program from
// scratch on first start -- minutes during which the view stays empty
test.setTimeout(15 * 60_000);

test.beforeAll(async () => {
    fs.mkdirSync(artifacts, { recursive: true });
    app = await launchPro3d();
});

test.afterAll(async () => {
    await app?.stop();
});

const image = () =>
    process.env.PRO3D_SELECT_IMAGE ?? "HERA_AFC_2317_20270301_040000_COP.png";

/** wait until the server stream shows the live scene (not the AARDVARK
 *  loading splash, which is rendered INTO the stream and has a bright logo
 *  that fools a naive brightness gate) with lit 3D content, then for two
 *  stable consecutive frames */
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

/** dispatch a mouse event on the library row block of the given image */
async function rowMouse(gis: Page, name: string, type: string) {
    const r = await gis.evaluate(
        ({ n, t }) => {
            const matches = Array.from(document.querySelectorAll("*")).filter(
                (e) => (e.textContent ?? "").trim() === n
            );
            const deepest = matches.filter(
                (e) => !Array.from(e.children).some((c) => matches.includes(c))
            );
            if (deepest.length === 0) return "not found";
            // the handler sits on the row block: the ancestor whose next
            // sibling structure holds the cells -- just walk up and fire on
            // every ancestor until one has the border style (the block)
            let el: Element | null = deepest[0];
            while (el && !(el instanceof HTMLElement && el.style.border.length > 0)) {
                el = el.parentElement;
            }
            if (!el) return "block not found";
            el.dispatchEvent(new MouseEvent(t, { bubbles: false }));
            return "dispatched";
        },
        { n: name, t: type }
    );
    expect(r, `${type} on ${name}`).toBe("dispatched");
}

test("hover previews the image; fly-to moves the camera", async ({ browser }) => {
    const context = await browser.newContext();
    const render = await context.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });

    const gis = await context.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");
    await gis.locator("text=Projected Images").first().click();
    await expect(gis.locator("text=Import Directory").first()).toBeVisible({
        timeout: 60_000,
    });
    const dir = config.imageDir.replace(/\\/g, "/");
    await gis.evaluate(
        `(() => { window.aardvark = window.aardvark || {}; window.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(dir)}] }) }; })()`
    );
    await gis.locator("text=Import Directory").first().click();
    await expect(
        gis.locator("text=/HERA_AFC_\\d+_\\d+_\\d+_COP\\.png/").first()
    ).toBeVisible({ timeout: 120_000 });
    await expect
        .poll(
            () =>
                gis.evaluate(
                    `Array.from(document.querySelectorAll("*")).some(e => (e.textContent || "").trim() === ${JSON.stringify(image())})`
                ),
            { timeout: 30_000 }
        )
        .toBe(true);

    // baseline AFTER the import: importing auto-selects an image, which shows
    // the false-color legend -- captured before, it would pollute every diff
    const baseline = await settled(render, "hover-baseline.png");

    // --- hover: the image previews as the top layer + footprint ---------------
    await rowMouse(gis, image(), "mouseenter");
    await render.waitForTimeout(4000);
    const hovered = await settled(render, "hover-active.png");
    const dHover = diffPng(baseline, hovered);
    console.log(
        `hover diff: ${(dHover.changedFraction * 100).toFixed(2)}% pixels`
    );
    expect(
        dHover.changedFraction,
        "hovering must preview the projection on the surface"
    ).toBeGreaterThan(0.001);

    // --- unhover: preview disappears ------------------------------------------
    await rowMouse(gis, image(), "mouseleave");
    await render.waitForTimeout(4000);
    const unhovered = await settled(render, "hover-cleared.png");
    const dBack = diffPng(baseline, unhovered);
    console.log(
        `after unhover, diff vs baseline: ${(dBack.changedFraction * 100).toFixed(2)}%`
    );
    expect(
        dBack.changedFraction,
        "leaving the row must remove the preview again"
    ).toBeLessThan(0.005);

    // --- fly-to: camera animates onto the projector axis ----------------------
    const posBefore: string = await render.evaluate(
        `(() => { const c = Array.from(document.querySelectorAll("td, div")).map(e => e.textContent || "").find(t => t.includes("Position:")); return c || document.body.innerText.slice(0, 400); })()`
    );
    const flyResult = await gis.evaluate(
        `(() => {
            const matches = Array.from(document.querySelectorAll("*")).filter(e => (e.textContent || "").trim() === ${JSON.stringify(image())});
            const deepest = matches.filter(e => !Array.from(e.children).some(c => matches.includes(c)));
            if (deepest.length === 0) return "header not found";
            let el = deepest[0];
            while (el) {
                const row = el.nextElementSibling;
                const box = row ? row.querySelector("i.location.icon") : null;
                if (box) { box.click(); return "clicked"; }
                el = el.parentElement;
            }
            return "no fly-to icon";
        })()`
    );
    expect(flyResult, "fly-to click").toBe("clicked");
    // animation runs 3.5 s
    await render.waitForTimeout(8000);
    const flown = await settled(render, "flyto.png");
    const posAfter: string = await render.evaluate(
        `(() => { const c = Array.from(document.querySelectorAll("td, div")).map(e => e.textContent || "").find(t => t.includes("Position:")); return c || document.body.innerText.slice(0, 400); })()`
    );
    console.log("position before:", posBefore.slice(0, 120));
    console.log("position after: ", posAfter.slice(0, 120));
    expect(posAfter, "fly-to must move the camera").not.toBe(posBefore);
    expect(
        litFraction(flown),
        "the body must be in view after fly-to"
    ).toBeGreaterThan(0.003);
});
