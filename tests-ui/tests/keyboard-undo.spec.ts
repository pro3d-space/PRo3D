import { test, expect, BrowserContext, Page } from "@playwright/test";
import { Pro3d } from "../src/pro3d";
import { annotationCount, awaitIdle, drawingScene, drawSkyLine, openDrawingViewer, pick } from "../src/drawing";
import * as fs from "fs";
import * as path from "path";

/**
 * Ctrl+Z / Ctrl+Y from anywhere -- docs/KeyboardShortcuts.md.
 *
 * Each dock panel is its own page, so the shortcut has to work on every page body, not just
 * on the focused render control. On the Dimorphos OPC, as a user does it:
 *
 *  1. draw two Sky lines;
 *  2. Ctrl+Z over the annotation *properties* panel: one is gone; Ctrl+Y: it is back;
 *  3. Ctrl+Z inside the toolbar's number field: nothing is undone (the field keeps it);
 *  4. Ctrl+Z over the 3D view: exactly one is gone -- the render control's own keydown
 *     bubbles to the render page's body, and must not undo a second time.
 *
 * The key decoding and the update are proven in Section18_KeyboardShortcuts.fs; this spec
 * proves the browser side: the handler is on the page, the filter lets the keys through and
 * keeps them out of text fields.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "keyboard-undo");

test.setTimeout(20 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

/** Presses `keys` on `page` with the mouse over its body, as a user with the cursor there. */
async function pressOn(page: Page, keys: string, app: Pro3d) {
    await page.bringToFront();
    const vp = page.viewportSize() ?? { width: 1280, height: 720 };
    await page.mouse.move(Math.round(vp.width / 2), Math.round(vp.height / 2));
    await page.waitForTimeout(300);
    await page.keyboard.press(keys);
    await awaitIdle(page, app, 2000);
}

async function expectCount(context: BrowserContext, app: Pro3d, n: number, why: string) {
    await expect
        .poll(() => annotationCount(context, app), { timeout: 60_000, intervals: [2000], message: why })
        .toBe(n);
}

test("Ctrl+Z/Y work from a dock panel, not in its text field, and undo once over the 3D view", async ({ browser }) => {
    const scene = drawingScene(artifacts, "keyboard-undo");
    const { app, context, render } = await openDrawingViewer(browser, scene, "00-settled.png", save);
    try {
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        // 1. two lines: drawSkyLine draws the first; the tool stays armed for the second
        await drawSkyLine(context, app, render, main);
        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const cx = Math.round(vp.width / 2);
        const cy = Math.round(vp.height / 2);
        await pick(render, cx - 110, cy + 60);
        await awaitIdle(render, app);
        await pick(render, cx + 110, cy + 60);
        await awaitIdle(render, app);
        await expectCount(context, app, 2, "two lines drawn");
        save("01-two-lines.png", await render.screenshot());

        // 2. the properties panel, cursor over it and nothing focused in it
        const props = await context.newPage();
        await props.goto(app.url + "?page=properties");
        await props.waitForLoadState("networkidle");
        await pressOn(props, "Control+z", app);
        await expectCount(context, app, 1, "Ctrl+Z over the properties panel undoes the last line");
        await pressOn(props, "Control+y", app);
        await expectCount(context, app, 2, "Ctrl+Y over the properties panel redoes it");

        // 3. inside a text-entry field the keys belong to the field: the toolbar's thickness
        //    (a number input, always shown while drawing)
        const field = main.locator(".pro3d-secondary-toolbar input[type=number]").first();
        await expect(field, "the drawing toolbar shows its thickness field").toBeVisible({ timeout: 30_000 });
        await main.bringToFront();
        await field.click();
        await field.press("Control+z");
        await awaitIdle(main, app, 2000);
        await expectCount(context, app, 2, "Ctrl+Z in a number field undoes no annotation");
        await field.blur();

        // 4. over the 3D view: one undo, not two
        await pressOn(render, "Control+z", app);
        await expectCount(context, app, 1, "Ctrl+Z over the 3D view undoes exactly one line");
        save("02-after-undo.png", await render.screenshot());
    } finally {
        await context.close();
        await app.stop();
    }
});
