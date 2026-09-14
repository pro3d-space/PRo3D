// Reproduce the collapsing accordions in the GIS panel, by CLICKING like a user.
//
// The other probes set values by evaluating JS against the DOM, which is why they never
// saw this: they never open a panel, they reach into it. This one clicks the titles and
// then asks whether the content is actually visible afterwards.
//
//   PRO3D_ACC_OUTER   outer accordion title (default "Projected Images")
//   PRO3D_ACC_INNER   inner accordion title to test (default "Projection Settings")
import { chromium, Page } from "@playwright/test";
import { launchPro3d } from "./pro3d";
import * as fs from "fs";
import * as path from "path";

const artifacts = path.join(__dirname, "..", "artifacts");
const OUTER = process.env.PRO3D_ACC_OUTER ?? "Projected Images";
const INNER = process.env.PRO3D_ACC_INNER ?? "Projection Settings";

/** the .title element whose own text is `label`, and whether its paired .content is open */
async function state(page: Page, label: string) {
    return await page.evaluate((label) => {
        const titles = Array.from(document.querySelectorAll(".title"));
        const t = titles.find((e) => (e.textContent ?? "").trim() === label);
        if (!t) return { found: false, active: false, visible: false, h: 0 };
        const c = t.nextElementSibling as HTMLElement | null;
        const r = c ? c.getBoundingClientRect() : null;
        return {
            found: true,
            active: t.classList.contains("active"),
            visible: !!(c && c.classList.contains("active")),
            h: r ? Math.round(r.height) : 0,
        };
    }, label);
}

async function clickTitle(page: Page, label: string) {
    return await page.evaluate((label) => {
        const titles = Array.from(document.querySelectorAll(".title"));
        const t = titles.find((e) => (e.textContent ?? "").trim() === label);
        if (!t) return "not found";
        (t as HTMLElement).click();
        return "clicked";
    }, label);
}

(async () => {
    fs.mkdirSync(artifacts, { recursive: true });
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const page = await (
        await browser.newContext({ viewport: { width: 700, height: 1000 } })
    ).newPage();
    await page.goto(app.url + "?page=gis");
    await page.waitForLoadState("networkidle");
    await page.locator(`text=${OUTER}`).first().waitFor({ timeout: 60_000 });
    await page.waitForTimeout(2000);

    // how many accordion roots are initialised, and do any contain another?
    const nesting = await page.evaluate(() => {
        const roots = Array.from(document.querySelectorAll(".ui.accordion"));
        const nested = roots.filter((r) =>
            roots.some((o) => o !== r && o.contains(r))
        );
        // for each root, how many .title elements does it see with the default selector?
        const owned = roots.map((r) => ({
            titles: r.querySelectorAll(".title").length,
            own: Array.from(r.children).filter((c) => c.classList.contains("title")).length,
        }));
        return { roots: roots.length, nested: nested.length, owned };
    });
    console.log(
        `accordion roots: ${nesting.roots}, of which nested inside another: ${nesting.nested}`
    );
    for (const [i, o] of nesting.owned.entries())
        if (o.titles > o.own)
            console.log(
                `  root #${i} sees ${o.titles} .title elements but only ${o.own} are its own` +
                    ` <- it will also handle clicks on the nested ones`
            );

    console.log(`\nbefore: outer ${JSON.stringify(await state(page, OUTER))}`);
    console.log(`        inner ${JSON.stringify(await state(page, INNER))}`);

    console.log(`\nclick "${OUTER}": ${await clickTitle(page, OUTER)}`);
    await page.waitForTimeout(1200);
    console.log(`        outer ${JSON.stringify(await state(page, OUTER))}`);
    console.log(`        inner ${JSON.stringify(await state(page, INNER))}`);

    console.log(`\nclick "${INNER}": ${await clickTitle(page, INNER)}`);
    await page.waitForTimeout(400);
    const immediately = await state(page, INNER);
    await page.waitForTimeout(1600);
    const settled = await state(page, INNER);
    const outerAfter = await state(page, OUTER);
    console.log(`        inner right after the click: ${JSON.stringify(immediately)}`);
    console.log(`        inner 2 s later            : ${JSON.stringify(settled)}`);
    console.log(`        outer 2 s later            : ${JSON.stringify(outerAfter)}`);

    fs.writeFileSync(path.join(artifacts, "accordion.png"), await page.screenshot());
    console.log();
    if (!settled.visible && immediately.visible)
        console.log("REPRODUCED: the inner accordion opened and then collapsed on its own");
    else if (!settled.visible)
        console.log("REPRODUCED: the inner accordion never opened");
    else if (!outerAfter.visible)
        console.log("REPRODUCED: opening the inner one closed the OUTER one");
    else console.log("inner accordion stayed open -- not reproduced this way");

    await browser.close();
    await app.stop();
})();
