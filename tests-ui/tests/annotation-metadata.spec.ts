import { test, expect, Page } from "@playwright/test";
import { config, firstImage, fixture } from "../src/pro3d";
import { clickRowIcon, setUpProjection } from "../src/probe-lib";
import { awaitIdle, clickTool, drawingScene, drawSkyLine, openDrawingViewer, settled, tool } from "../src/drawing";
import { exportDimmer as dimmer, menu, readCsv, runExport, stubSaveDialog } from "../src/export";
import * as fs from "fs";
import * as path from "path";

/**
 * Annotation metadata (#887, docs/AnnotationMetadata.md), end to end on Dimorphos:
 *
 *  1. import the AFC frames, put the first one on the projection stack and fly to it, so
 *     the view looks along its projector;
 *  2. draw a Sky line in the middle of the view, inside that image's footprint: finishing it
 *     captures the shape model and the projected images under its points;
 *  3. in the 3D view press 2 (confidence) and C, type a comment with a comma and quotes,
 *     Enter;
 *  4. the Properties panel shows all four; the CSV export carries them in its columns.
 *
 * Unit tests (Section23_AnnotationMetadata.fs) prove the parts; only this proves that the
 * projector, the kd-tree line of sight, the keys, the floating dialog and the export meet.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "annotation-metadata-e2e");
const COMMENT = 'edge unclear, "see" frame';

test.setTimeout(30 * 60_000);

function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
}

function focused(page: Page): Promise<string> {
    return page.evaluate(() => {
        const e = document.activeElement as HTMLElement | null;
        return e ? `${e.tagName.toLowerCase()}.${e.className}` : "none";
    });
}

/** label -> shown value of the metadata rows of the annotation Properties panel */
async function metadataRows(props: Page): Promise<Record<string, string>> {
    return props.evaluate(() => {
        const out: Record<string, string> = {};
        document.querySelectorAll("tr").forEach((tr) => {
            const cells = Array.from(tr.querySelectorAll("td"));
            if (cells.length < 2) return;
            const label = (cells[0].textContent ?? "").trim();
            if (!/^(Confidence|Comment|Shape Model|Images Used):$/.test(label)) return;
            const input = cells[1].querySelector("input") as HTMLInputElement | null;
            const active = cells[1].querySelector(".primary.button");
            out[label] = input ? input.value : active ? (active.textContent ?? "").trim() : (cells[1].textContent ?? "").trim();
        });
        return out;
    });
}

test("projected images, confidence and comment reach Properties and the export", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");
    test.skip(!process.env.PRO3D_SPICE_KERNELS, "set PRO3D_SPICE_KERNELS: the projector needs the kernels");

    const scene = drawingScene(artifacts, "annotation-metadata");
    const csvPath = path.join(artifacts, "annotations.csv");
    fs.rmSync(csvPath, { force: true });
    const image = firstImage(config.imageDir);

    const { app, context, render } = await openDrawingViewer(browser, scene, "00-loaded.png", save);
    try {
        await render.setViewportSize({ width: 1600, height: 900 });

        // 1. the image on the stack, the camera on its projector
        const gis = await setUpProjection(context, app.url, image);
        await clickRowIcon(gis, image, "plus");
        await gis.waitForTimeout(1500);
        await clickRowIcon(gis, image, "location");
        await awaitIdle(render, app);
        await settled(render, "01-flown-to-image.png", save);

        // 2. a Sky line across the middle of the view
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");
        await drawSkyLine(context, app, render, main, async () => {}, 60, 0);
        await settled(render, "02-line.png", save);

        // 3. keys in the 3D view. With the draw tool still active, 2 is the semantic of the next
        //    annotation, even though finishing selected the line: it must not rate it
        await render.bringToFront();
        await render.mouse.move(700, 300);
        await render.waitForTimeout(500);
        expect(await focused(render), "the 3D view has focus").toContain("mainrendercontrol");
        await render.keyboard.press("2");
        await render.waitForTimeout(1500);
        expect(await render.locator(".annotation-rating-balloon").count(), "the draw tool keeps 2 as the semantic: no bubble")
            .toBe(0);
        expect(await render.evaluate(() => document.body.innerText), "and no message")
            .not.toContain("Confidence 2 set");

        //    Select annotation: now 2 rates the selected line, C comments on it
        await clickTool(render, "Select annotation");
        await expect.poll(async () => (await tool(render, "Select annotation")).active, { timeout: 30_000 }).toBe(true);
        await render.mouse.move(720, 320);
        await render.waitForTimeout(500);
        await render.keyboard.press("2");
        // one annotation: a bubble beside it says C2 (no message), then fades
        const bubble = render.locator(".annotation-rating-balloon").first();
        await expect(bubble, "the rating bubble").toHaveText("C2", { timeout: 10_000 });
        save("02b-rating-bubble.png", await render.screenshot());
        const vpb = render.viewportSize() ?? { width: 1600, height: 900 };
        const bb = await bubble.boundingBox();
        expect(bb, "the bubble is laid out").not.toBeNull();
        console.log(`bubble at ${bb!.x}, ${bb!.y}; view centre ${vpb.width / 2}, ${vpb.height / 2}`);
        expect(Math.abs(bb!.x - (vpb.width / 2 + 10)), "bubble beside the annotation (x)").toBeLessThan(40);
        expect(Math.abs(bb!.y - (vpb.height / 2 - 34)), "bubble beside the annotation (y)").toBeLessThan(40);
        await expect
            .poll(() => bubble.evaluate((e) => getComputedStyle(e).opacity), { timeout: 5_000, message: "the bubble fades" })
            .toBe("0");

        await render.keyboard.press("c");
        await render.waitForSelector(".annotation-comment-dialog", { timeout: 15_000 });
        await expect.poll(() => focused(render), { timeout: 5_000, message: "the dialog's text field has focus" })
            .toMatch(/^textarea/);
        // next to the annotation: the line's picks were the view centre +- 60 px, so its centre
        // is about the view centre, and the dialog starts 24 px to the right of it
        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const box = await render.locator(".annotation-comment-dialog").boundingBox();
        expect(box, "the dialog is laid out").not.toBeNull();
        console.log(`dialog at ${box!.x}, ${box!.y}; view centre ${vp.width / 2}, ${vp.height / 2}`);
        expect(Math.abs(box!.x - (vp.width / 2 + 24)), "dialog beside the annotation (x)").toBeLessThan(40);
        expect(Math.abs(box!.y - (vp.height / 2 - 20)), "dialog beside the annotation (y)").toBeLessThan(40);
        await render.keyboard.type(COMMENT);
        save("03-dialog.png", await render.screenshot());
        await render.keyboard.press("Enter");
        await expect(render.locator(".annotation-comment-dialog"), "Enter closes the dialog").toHaveCount(0, { timeout: 10_000 });
        await expect.poll(() => focused(render), { timeout: 5_000, message: "the keys are back with the 3D view" })
            .toContain("mainrendercontrol");
        await awaitIdle(render, app, 2000);

        // 4a. Properties
        const props = await context.newPage();
        await props.setViewportSize({ width: 700, height: 1400 });
        await props.goto(app.url + "?page=properties");
        await props.waitForLoadState("networkidle");
        await expect.poll(async () => (await metadataRows(props))["Comment:"], { timeout: 30_000 }).toBe(COMMENT);
        const rows = await metadataRows(props);
        console.log("properties:", JSON.stringify(rows));
        expect(rows["Confidence:"], "confidence").toBe("2");
        expect(rows["Shape Model:"], "shape model recorded").not.toBe("not recorded");
        expect(rows["Images Used:"], "the stack image is under the line").toContain(image);
        save("04-properties.png", await props.screenshot({ fullPage: true }));

        // 4b. the export, with its default columns
        await stubSaveDialog(main, csvPath);
        await menu(main, "Annotations", "Export...");
        await expect(main.locator(`${dimmer} .ui.inverted.segment`).first()).toBeVisible({ timeout: 30_000 });
        await runExport(main, [csvPath], fs.existsSync);
        const { header, rows: csvRows, cell } = readCsv(fs.readFileSync(csvPath, "utf-8"));
        console.log(`export header: ${header.join(",")}`);
        for (const col of ["confidence", "comment", "shapeModel", "shapeModelVersion", "shapeModelCredit", "projectedImages"])
            expect(header, `the export has ${col}`).toContain(col);
        expect(csvRows.length, "one annotation").toBe(1);
        const row = csvRows[0];
        expect(cell(row, "confidence")).toBe("2");
        expect(cell(row, "comment"), "comma and quotes survive the CSV").toBe(COMMENT);
        expect(cell(row, "shapeModelVersion"), "the test OPC's version, from its file name").toBe("v003");
        expect(cell(row, "projectedImages")).toContain(image);
    } finally {
        await context.close();
        await app.stop();
    }
});
