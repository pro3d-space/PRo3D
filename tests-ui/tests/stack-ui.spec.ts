import { test, expect, Page } from "@playwright/test";
import { launchPro3d, Pro3d, config } from "../src/pro3d";

/**
 * Drives the projection-stack UI (multi-image projection): add two library
 * images to the stack, check the count, reorder, remove. Runs against the
 * same scene + COP data as the projection smoke test.
 */

let app: Pro3d;

test.beforeAll(async () => {
    app = await launchPro3d();
});

test.afterAll(async () => {
    await app?.stop();
});

/** click an icon inside the row of the given library image (single-shot DOM
 *  click; the incremental list re-renders too often for the actionability
 *  loop). `iconClass` example: "plus" for add-to-stack. */
async function clickRowIcon(gis: Page, imageName: string, iconClass: string) {
    const result = await gis.evaluate(
        ({ name, icon }) => {
            const matches = Array.from(document.querySelectorAll("*")).filter(
                (e) => (e.textContent ?? "").trim() === name
            );
            const deepest = matches.filter(
                (e) => !Array.from(e.children).some((c) => matches.includes(c))
            );
            if (deepest.length === 0) return "header not found";
            let el: Element | null = deepest[0];
            while (el) {
                const row = el.nextElementSibling;
                const box = row?.querySelector(`i.${icon}.icon`);
                if (box) {
                    (box as HTMLElement).click();
                    return "clicked";
                }
                el = el.parentElement;
            }
            return `no i.${icon}.icon in row`;
        },
        { name: imageName, icon: iconClass }
    );
    expect(result, `${iconClass} on ${imageName}`).toBe("clicked");
}

/** click an icon inside the stack-panel entry for the given image name */
async function clickStackIcon(gis: Page, imageName: string, iconClass: string) {
    const result = await gis.evaluate(
        ({ name, icon }) => {
            const rows = Array.from(document.querySelectorAll("div")).filter(
                (d) =>
                    d.querySelector(`i.${icon}.icon`) &&
                    (d.textContent ?? "").trim() === name
            );
            if (rows.length === 0) return "stack row not found";
            const box = rows[rows.length - 1].querySelector(`i.${icon}.icon`);
            (box as HTMLElement).click();
            return "clicked";
        },
        { name: imageName, icon: iconClass }
    );
    expect(result, `stack ${iconClass} on ${imageName}`).toBe("clicked");
}

/** the file names listed in the stack panel, top first */
async function stackEntries(gis: Page): Promise<string[]> {
    return gis.evaluate(() => {
        const remove = Array.from(document.querySelectorAll("i.remove.icon"));
        return remove
            .map((i) => (i.parentElement?.textContent ?? "").trim())
            .filter((t) => t.length > 0);
    });
}

/** poll until the stack panel shows exactly `expected` (top first) -- the
 *  incremental UI updates asynchronously after a click, and the app is busy
 *  re-rendering the projection when the stack changes */
async function expectStack(gis: Page, expected: string[]) {
    await expect
        .poll(() => stackEntries(gis), { timeout: 15_000 })
        .toEqual(expected);
}

test("stack add / reorder / remove through the GIS tab", async ({ browser }) => {
    const context = await browser.newContext();
    const gis = await context.newPage();
    await gis.goto(app.url + "?page=gis");
    await gis.waitForLoadState("networkidle");

    await gis.locator("text=Projected Images").first().click();
    await expect(gis.locator("text=Import Directory").first()).toBeVisible({
        timeout: 60_000,
    });

    const imageDirUnix = config.imageDir.replace(/\\/g, "/");
    await gis.evaluate((dir) => {
        const w = window as any;
        w.aardvark = w.aardvark ?? {};
        w.aardvark.dialog = {
            showOpenDialog: (_opts: unknown) =>
                Promise.resolve({ canceled: false, filePaths: [dir] }),
        };
    }, imageDirUnix);
    await gis.locator("text=Import Directory").first().click();

    const row = gis.locator("text=/HERA_AFC_\\d+_\\d+_\\d+_COP\\.png/").first();
    await expect(row).toBeVisible({ timeout: 120_000 });

    // empty stack to start with
    await expect(gis.locator("text=Projection Stack (0/32)")).toBeVisible();

    // the first two file names in the library
    const names: string[] = await gis.evaluate(() => {
        const all = Array.from(document.querySelectorAll("div"))
            .map((d) => (d.textContent ?? "").trim())
            .filter((t) => /^HERA_AFC_\d+_\d+_\d+_COP\.png$/.test(t));
        return [...new Set(all)].slice(0, 2);
    });
    expect(names.length).toBe(2);
    const [a, b] = names;

    // add both: stack bottom -> top = [a, b]; panel shows top first
    await clickRowIcon(gis, a, "plus");
    await expect(gis.locator("text=Projection Stack (1/32)")).toBeVisible();
    await clickRowIcon(gis, b, "plus");
    await expect(gis.locator("text=Projection Stack (2/32)")).toBeVisible();
    await expectStack(gis, [b, a]);

    // adding again must not duplicate
    await clickRowIcon(gis, a, "layer");
    // ("layer group icon" = already in stack -> that click REMOVES; re-add)
    await expect(gis.locator("text=Projection Stack (1/32)")).toBeVisible();
    await clickRowIcon(gis, a, "plus");
    await expect(gis.locator("text=Projection Stack (2/32)")).toBeVisible();
    await expectStack(gis, [a, b]);

    // move a down (to the bottom) -> order back to [b, a]
    await clickStackIcon(gis, a, "down");
    await expectStack(gis, [b, a]);

    // moving the top further up is a clamped no-op
    await clickStackIcon(gis, b, "up");
    await expectStack(gis, [b, a]);

    // remove the top
    await clickStackIcon(gis, b, "remove");
    await expect(gis.locator("text=Projection Stack (1/32)")).toBeVisible();
    await expectStack(gis, [a]);
});
