import { test, expect, Page } from "@playwright/test";
import { fixture, launchPro3d } from "../src/pro3d";
import { overlayPlanet } from "../src/viewer";
import * as fs from "fs";
import * as path from "path";

/**
 * The first OPC names its body (#894, docs/SceneBody.md): an empty PRo3D, one OPC imported
 * through the menu's *Import OPCs*, and the scene body is set from what the OPC
 * says for sure about its body -- the planet in the overlay, and a message saying why.
 *
 * Runs on the test data's Dimorphos OPC (`g_01960mm_spc_dtm_dimo_..._v003`: the body code in
 * the product name) and, with PRO3D_V4_OPCS, on the HERA V4 global OPC (FITS TARGET).
 */

test.setTimeout(10 * 60_000);

async function importOpc(main: Page, dir: string) {
    await main.evaluate((d) => {
        const w = window as any;
        w.aardvark = w.aardvark ?? {};
        w.aardvark.dialog = { showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [d] }) };
    }, dir.replace(/\\/g, "/"));
    const r = await main.evaluate(() => {
        const item = Array.from(document.querySelectorAll("div.item"))
            .find((e) => (e.textContent ?? "").trim() === "Import OPCs") as HTMLElement | undefined;
        if (!item) return "no Import OPCs item";
        item.click();
        return "clicked";
    });
    expect(r, "Import OPCs").toBe("clicked");
}

const cases: { name: string; opc: string | undefined; source: string }[] = [
    { name: "test-data OPC, body code in the product name", opc: fixture.opc, source: "the product name" },
    {
        name: "HERA V4 global OPC, FITS TARGET",
        opc: process.env.PRO3D_V4_OPCS
            ? path.join(process.env.PRO3D_V4_OPCS, "Dimorphos_DRACO", "Dimorphos_DRACO")
            : undefined,
        source: "the FITS header's TARGET",
    },
];

for (const c of cases) {
    test(`an empty scene takes the body of its first OPC: ${c.name}`, async ({ browser }) => {
        test.skip(!c.opc || !fs.existsSync(c.opc), `OPC not available: ${c.opc ?? "(set PRO3D_V4_OPCS)"}`);

        const app = await launchPro3d(""); // no scene: an empty PRo3D
        const context = await browser.newContext();
        try {
            const main = await context.newPage();
            await main.goto(app.url);
            await main.waitForLoadState("networkidle");
            const render = await context.newPage();
            await render.goto(app.url + "?page=render");
            await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });

            // an empty PRo3D: the default planet (Mars), no GIS body observed
            const before = await overlayPlanet(render);
            console.log("before import:", before);
            expect(before, "an empty PRo3D is not set to Dimorphos").not.toBe("Dimorphos");

            await importOpc(main, c.opc!);
            await expect
                .poll(() => overlayPlanet(render), { timeout: 240_000, intervals: [2000], message: "the scene body" })
                .toBe("Dimorphos");
            const text = await render.evaluate(() => document.body.innerText);
            console.log("message:", (text.match(/Scene body set to[^\n]*/) ?? ["(none)"])[0]);
            expect(text, "the message says what and why").toContain(`Scene body set to Dimorphos, from ${c.source}`);
        } finally {
            await context.close();
            await app.stop();
        }
    });
}
