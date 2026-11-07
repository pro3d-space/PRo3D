// Annotation metadata in the real viewer (#887, docs/AnnotationMetadata.md): draw a Sky line on
// Dimorphos, press 3 (confidence), C (comment dialog), type, Enter; read back the Properties panel.
// Run: npx tsx src/probe-annotation-metadata.ts
import { chromium, Page } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { awaitIdle, drawingScene, drawSkyLine, openDrawingViewer } from "./drawing";

const artifacts = path.join(__dirname, "..", "artifacts", "annotation-metadata");
function save(name: string, png: Buffer) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
    console.log(`saved ${name}`);
}

function focused(page: Page): Promise<string> {
    return page.evaluate(() => {
        const e = document.activeElement as HTMLElement | null;
        return e ? `${e.tagName.toLowerCase()}.${e.className}` : "none";
    });
}

(async () => {
    const browser = await chromium.launch();
    // PRO3D_PROBE_OPC: an OPC folder to draw on instead of the test data's, e.g. the HERA V4
    // DRACO OPC, whose sidecar carries the FITS header (Source & Credits, the credit)
    const probeOpc = process.env.PRO3D_PROBE_OPC;
    const scene = drawingScene(artifacts, "annotation-metadata", (d) => {
        if (!probeOpc) return;
        const surface = d.surfaceModel.surfaces.flat[0].Surfaces;
        const entries = fs.readdirSync(probeOpc);
        const opcx = entries.find((f) => f.endsWith(".opcx"));
        surface.importPath = probeOpc;
        surface.opcPaths = entries
            .filter((f) => fs.statSync(path.join(probeOpc, f)).isDirectory())
            .map((f) => path.join(probeOpc, f));
        surface.opcNames = surface.opcPaths.map((p: string) => path.basename(p));
        surface.opcxPath = opcx ? path.join(probeOpc, opcx) : null;
        console.log("probe OPC:", probeOpc, surface.opcPaths);
    });
    const { app, context, render } = await openDrawingViewer(browser, scene, "00-settled.png", save);
    try {
        await render.setViewportSize({ width: 1600, height: 900 });
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");

        await drawSkyLine(context, app, render, main);
        save("01-line.png", await render.screenshot());

        // keys go to the focused render control: hover it
        await render.bringToFront();
        await render.mouse.move(700, 300);
        await render.waitForTimeout(500);
        console.log("focus before 3:", await focused(render));

        await render.keyboard.press("3");
        await render.waitForTimeout(700);
        save("02a-right-after-3.png", await render.screenshot());
        const text3 = await render.evaluate(() => document.body.innerText);
        console.log("message after 3:", JSON.stringify(text3.match(/Confidence[^\n]*/)?.[0] ?? "(none)"));
        await awaitIdle(render, app, 2000);
        save("02-confidence-3.png", await render.screenshot());

        await render.keyboard.press("c");
        await render.waitForSelector(".annotation-comment-dialog", { timeout: 15_000 });
        await render.waitForTimeout(500);
        console.log("focus in dialog:", await focused(render));
        await render.keyboard.type("edge unclear, check AFC frame 2");
        save("03-dialog.png", await render.screenshot());
        // move the mouse across the 3D view while typing: the field must keep focus
        await render.mouse.move(900, 500);
        await render.waitForTimeout(300);
        console.log("focus after hovering the view:", await focused(render));
        await render.keyboard.press("Enter");
        await render.waitForTimeout(1000);
        console.log("dialog open after Enter:", await render.locator(".annotation-comment-dialog").count());
        console.log("focus after Enter:", await focused(render));

        // 4 straight away, no mouse move: focus must be back on the 3D view
        await render.keyboard.press("4");
        await render.waitForTimeout(700);
        const text4 = await render.evaluate(() => document.body.innerText);
        console.log("message after 4:", JSON.stringify(text4.match(/Confidence[^\n]*/)?.[0] ?? "(none)"));
        await awaitIdle(render, app, 2000);
        save("04-confidence-4.png", await render.screenshot());

        const props = await context.newPage();
        await props.setViewportSize({ width: 700, height: 1400 });
        await props.goto(app.url + "?page=properties");
        await props.waitForLoadState("networkidle");
        await props.waitForTimeout(1500);
        const rows = await props.evaluate(() => {
            const out: string[] = [];
            document.querySelectorAll("tr").forEach((tr) => {
                const cells = Array.from(tr.querySelectorAll("td"));
                if (cells.length < 2) return;
                const label = (cells[0].textContent ?? "").trim();
                if (!/Confidence|Comment|Shape Model|Images Used/.test(label)) return;
                const input = cells[1].querySelector("input") as HTMLInputElement | null;
                const active = cells[1].querySelector(".primary.button");
                out.push(`${label} ${input ? JSON.stringify(input.value) : (cells[1].textContent ?? "").trim()}${active ? ` [active: ${active.textContent}]` : ""}`);
            });
            return out;
        });
        rows.forEach((r) => console.log("properties:", r));
        save("05-properties.png", await props.screenshot({ fullPage: true }));

        // does a plain text label draw here? set Text, then look at the 3D view
        const textInput = props.locator("tr", { hasText: "Text:" }).first().locator("input").first();
        await textInput.evaluate((e) => (e as HTMLInputElement).focus());
        await props.keyboard.type("Boulder");
        await props.keyboard.press("Enter");
        await props.waitForTimeout(1500);
        await render.bringToFront();
        await render.waitForTimeout(1500);
        save("05b-with-text.png", await render.screenshot());

        // Surfaces -> Source & Credits for the scene's OPC
        const surf = await context.newPage();
        await surf.setViewportSize({ width: 700, height: 1400 });
        await surf.goto(app.url + "?page=surfaces");
        await surf.waitForLoadState("networkidle");
        await surf.waitForTimeout(1500);
        const picked = await surf.evaluate(() => {
            const span = document.querySelector(".item .content .header span") as HTMLElement | null;
            if (!span) return "no surface row";
            span.click();
            return span.textContent ?? "";
        });
        console.log("surface selected:", picked);
        await surf.waitForTimeout(1500);
        const opened = await surf.evaluate(() => {
            const t = Array.from(document.querySelectorAll(".ui.accordion > .title"))
                .find((e) => (e.textContent ?? "").includes("Source & Credits")) as HTMLElement | undefined;
            if (!t) return "no Source & Credits title";
            t.click();
            return "clicked";
        });
        console.log("source section:", opened);
        await surf.waitForTimeout(1500);
        const source = await surf.evaluate(() => {
            const t = Array.from(document.querySelectorAll(".ui.accordion > .title"))
                .find((e) => (e.textContent ?? "").includes("Source & Credits"));
            const c = t?.nextElementSibling as HTMLElement | null;
            return c ? c.innerText : "(no content)";
        });
        source.split(/\r?\n/).slice(0, 30).forEach((l: string) => console.log("source:", l));
        save("07-source-credits.png", await surf.screenshot({ fullPage: true }));

        const anns = await context.newPage();
        await anns.goto(app.url + "?page=annotations");
        await anns.waitForLoadState("networkidle");
        save("06-annotations-panel.png", await anns.screenshot({ fullPage: true }));
    } finally {
        await context.close();
        await browser.close();
        await app.stop();
        process.exit(0);
    }
})();
