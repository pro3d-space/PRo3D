import { test, expect, Page, ElementHandle } from "@playwright/test";
import { fixture } from "../src/pro3d";
import { overlayPlanet } from "../src/viewer";
import { drawingScene, drawSkyLine, openDrawingViewer, selectByOption } from "../src/drawing";
import { clickInExportWindow, exportDimmer as dimmer, menu } from "../src/export";
import * as fs from "fs";
import * as path from "path";

/**
 * Screenshots of every place PRo3D shows a height (#866, docs/HeightReference.md), on
 * Dimorphos, where the value is the radius from the centre -- the case the old
 * "Altitude" label got most wrong.
 *
 * Native `title` tooltips are drawn by the browser outside the page and never appear in
 * a screenshot, so each shot reads the tooltip text out of the DOM and draws it as a
 * numbered callout next to the element. That doubles as the check that the tooltip is
 * there: a label without one fails the spec.
 *
 * Writes the single shots and a mosaic to artifacts/height-reference/ (not into docs/).
 */

const artifacts = path.join(__dirname, "..", "artifacts", "height-reference");

test.setTimeout(30 * 60_000);

interface Shot { file: string; caption: string }
const shots: Shot[] = [];

function save(name: string, png: Buffer, caption?: string) {
    fs.mkdirSync(artifacts, { recursive: true });
    fs.writeFileSync(path.join(artifacts, name), png);
    if (caption) shots.push({ file: name, caption });
}

/** Opens the GuiEx accordion titled `title` (no-op when open). */
async function openAccordion(page: Page, title: string) {
    const r = await page.evaluate((t) => {
        const el = Array.from(document.querySelectorAll(".ui.accordion > .title")).find(
            (e) => (e.textContent ?? "").trim() === t) as HTMLElement | undefined;
        if (!el) return "missing";
        if (!el.classList.contains("active")) el.click();
        return "ok";
    }, title);
    expect(r, `accordion ${title}`).toBe("ok");
    await page.waitForTimeout(1200);
}

/** The inverted segment around the accordion titled `title`. */
async function segmentOf(page: Page, title: string): Promise<ElementHandle> {
    const h = await page.evaluateHandle((t) => {
        const el = Array.from(document.querySelectorAll(".ui.accordion > .title")).find(
            (e) => (e.textContent ?? "").trim() === t);
        // the accordion's content element: on the properties page the accordions nest, so
        // any enclosing container can be the whole panel. The shot adds the title above it.
        return el ? el.nextElementSibling : null;
    }, title);
    const e = h.asElement();
    if (!e) throw new Error(`no segment for ${title}`);
    return e;
}

/**
 * Numbered callouts for the tooltips of the elements labelled `labels` (exact text of a
 * td/span/option). The tooltip is looked up on the element, its ancestors, and the value
 * cell beside it. Returns the texts; a missing tooltip throws.
 */
async function callouts(page: Page, labels: string[], within: ElementHandle | null): Promise<string[]> {
    const r = await page.evaluate(
        ({ labels, within }) => {
            document.querySelectorAll(".h866").forEach((e) => e.remove());
            const scope: Element = (within as Element) ?? document.body;
            const out: string[] = [];
            const marks: { rect: DOMRect; n: number }[] = [];
            labels.forEach((label, i) => {
                const n = i + 1;
                const el = Array.from(scope.querySelectorAll("td, span, option, div")).find(
                    (e) => Array.from(e.childNodes).some(
                        (c) => c.nodeType === 3 && (c.textContent ?? "").trim() === label)
                        // skip hidden copies (e.g. a dropdown's backing <select>)
                        && ((e.closest("select") ?? e) as HTMLElement)
                               .getBoundingClientRect().width > 0) as HTMLElement | undefined;
                if (!el) { out.push(`!missing label ${label}`); return; }
                let title = "";
                for (let a: HTMLElement | null = el, k = 0; a && k < 4 && !title; a = a.parentElement, k++)
                    title = a.getAttribute("title") ?? "";
                if (!title) {
                    // the value cell beside the label's cell (the label text may sit in a span)
                    const next = (el.closest("td") ?? el).nextElementSibling;
                    const t = next?.querySelector("[title]") ?? (next?.hasAttribute("title") ? next : null);
                    title = t?.getAttribute("title") ?? "";
                }
                if (!title) { out.push(`!no tooltip on ${label}`); return; }
                out.push(title);
                const anchor = (el.closest("select") as HTMLElement | null) ?? el;
                anchor.style.outline = "2px solid #f5c542";
                marks.push({ rect: anchor.getBoundingClientRect(), n });
            });
            const sx = window.scrollX, sy = window.scrollY;
            for (const m of marks) {
                const b = document.createElement("div");
                b.className = "h866";
                b.textContent = String(m.n);
                b.style.cssText = `position:absolute;left:${m.rect.left + sx - 22}px;top:${m.rect.top + sy}px;
                    width:18px;height:18px;border-radius:9px;background:#f5c542;color:#000;font:bold 12px sans-serif;
                    text-align:center;line-height:18px;z-index:100000`;
                document.body.appendChild(b);
            }
            const s = scope.getBoundingClientRect();
            const box = document.createElement("div");
            box.className = "h866 h866-box";
            box.style.cssText = `position:absolute;left:${s.left + sx}px;top:${s.bottom + sy + 8}px;
                width:${Math.min(Math.max(s.width, 420), 640)}px;z-index:100000;font:12px/1.35 sans-serif`;
            out.forEach((t, i) => {
                if (t.startsWith("!")) return;
                const p = document.createElement("div");
                p.style.cssText = "background:#fff8dc;color:#222;border:1px solid #c9a227;border-radius:4px;padding:6px 8px;margin-bottom:6px";
                p.innerHTML = `<b style="color:#8a6500">${i + 1}</b> `;
                p.appendChild(document.createTextNode(t));
                box.appendChild(p);
            });
            document.body.appendChild(box);
            return out;
        },
        { labels, within }
    );
    const bad = r.filter((t) => t.startsWith("!"));
    if (bad.length > 0) {
        const html = await page.evaluate((w) => ((w as Element) ?? document.body).outerHTML, within);
        fs.mkdirSync(artifacts, { recursive: true });
        fs.writeFileSync(path.join(artifacts, `missing-tooltip-${labels[0].replace(/\W+/g, "_")}.html`), html);
    }
    expect(bad, `tooltips on ${labels.join(", ")}`).toEqual([]);
    return r;
}

/** Screenshot of `el` plus the callouts drawn for it. */
async function shotWithCallouts(page: Page, el: ElementHandle, name: string, caption: string) {
    const clip = await page.evaluate((e) => {
        const prev = (e as Element).previousElementSibling;
        const rects = [ (e as Element).getBoundingClientRect(),
            ...(prev && prev.classList.contains("title") ? [prev.getBoundingClientRect()] : []),
            ...Array.from(document.querySelectorAll(".h866")).map((x) => x.getBoundingClientRect()) ];
        const l = Math.min(...rects.map((r) => r.left)) - 26, t = Math.min(...rects.map((r) => r.top)) - 4;
        const r = Math.max(...rects.map((r) => r.right)) + 6, b = Math.max(...rects.map((r) => r.bottom)) + 6;
        return { x: Math.max(0, l + window.scrollX), y: Math.max(0, t + window.scrollY), width: r - l, height: b - t };
    }, el);
    save(name, await page.screenshot({ clip, fullPage: true }), caption);
}

async function panelPage(context: any, url: string, page: string): Promise<Page> {
    const p: Page = await context.newPage();
    await p.setViewportSize({ width: 600, height: 1600 });
    await p.goto(`${url}?page=${page}`);
    await p.waitForLoadState("domcontentloaded");
    await p.waitForTimeout(2500);
    return p;
}

test("height labels and tooltips, documented on Dimorphos", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");
    fs.rmSync(artifacts, { recursive: true, force: true });

    const scene = drawingScene(artifacts, "height-reference");
    const { app, context, render } = await openDrawingViewer(browser, scene, "0-loaded.png", (n, b) => save(n, b));
    try {
        await expect.poll(() => overlayPlanet(render), { timeout: 180_000, intervals: [5000] }).toBe("Dimorphos");

        // 1. HUD -- ignores the mouse, so the label itself has to say it
        await expect.poll(() => render.evaluate(() => document.body.innerText), { timeout: 60_000 })
            .toContain("Radius from centre:");
        const hud = (await render.locator("table").first().elementHandle())!;
        const hb = (await hud.boundingBox())!;
        save("1-hud.png", await render.screenshot({ clip: { x: 0, y: 0, width: hb.x + hb.width + 20, height: hb.y + hb.height + 12 } }),
            "HUD: label names the quantity");

        // 2. draw a line so there are annotation results to show
        const main = await context.newPage();
        await main.goto(app.url);
        await main.waitForLoadState("domcontentloaded");
        await drawSkyLine(context, app, render, main);

        // 3. Coordinate System panel and the Under Cursor readout (config page)
        const config = await panelPage(context, app.url, "config");
        await openAccordion(config, "Coordinate System");
        const coo = await segmentOf(config, "Coordinate System");
        await callouts(config, ["Radius from centre:", "Convention:"], coo);
        await shotWithCallouts(config, coo, "2-coordinate-system.png",
            "Coordinate System");

        // the readout follows ctrl+hover on the render view
        await render.bringToFront();
        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        await render.mouse.move(vp.width / 2, vp.height / 2);
        await render.waitForTimeout(400);
        await render.keyboard.down("Control");
        for (let i = 0; i < 4; i++) {
            await render.mouse.move(vp.width / 2 + 20 + 8 * i, vp.height / 2 - 30 + 4 * i);
            await render.waitForTimeout(1500);
        }
        await expect.poll(() => config.evaluate(() => document.body.innerText), { timeout: 60_000, intervals: [1000] })
            .toContain("Elevation:");
        await render.keyboard.up("Control");
        const cursor = await segmentOf(config, "Under Cursor");
        await callouts(config, ["Radius from centre:", "Elevation:", "Slope:"], cursor);
        await shotWithCallouts(config, cursor, "3-under-cursor.png",
            "Under Cursor: PRo3D value vs. shape-model layers");

        // 4. annotation results and Color by Category (properties page)
        const props = await panelPage(context, app.url, "properties");
        await openAccordion(props, "Measurements");
        const meas = await segmentOf(props, "Measurements");
        await callouts(props, ["Max - min radius:", "Avg radius:", "Slope:"], meas);
        await shotWithCallouts(props, meas, "4-measurements.png",
            "Measurements");

        await openAccordion(props, "Color by Category");
        await props.evaluate(() => {
            const s = Array.from(document.querySelectorAll("select")).find((x) =>
                Array.from(x.options).some((o) => o.value === "AvgAltitude"));
            if (s) { s.value = "AvgAltitude"; s.dispatchEvent(new Event("change", { bubbles: true })); }
        });
        await props.waitForTimeout(1500);
        const cbc = await segmentOf(props, "Color by Category");
        await callouts(props, ["Avg height"], cbc);
        await shotWithCallouts(props, cbc, "5-color-by-category.png",
            "Color by Category");

        // 5. cross-section curtain (annotations page)
        const anns = await panelPage(context, app.url, "annotations");
        await openAccordion(anns, "Curtain Settings");
        const curtain = await segmentOf(anns, "Curtain Settings");
        await callouts(anns, ["Absolute Height:", "Depth / Height (m):", "Tex Start Height (m):"], curtain);
        await shotWithCallouts(anns, curtain, "6-curtain.png",
            "Curtain Settings");

        // 6. export window (main page)
        await main.setViewportSize({ width: 1400, height: 1600 });
        await menu(main, "Annotations", "Export...");
        const panel = main.locator(`${dimmer} .ui.inverted.segment`).first();
        await expect(panel).toBeVisible({ timeout: 30_000 });
        await selectByOption(main, "Profile", "Profile", dimmer);
        await main.waitForTimeout(1000);
        // tall viewport: let the window grow instead of scrolling its settings
        await main.addStyleTag({ content: `${dimmer} * { max-height: none !important; }` });
        const panelEl = (await panel.elementHandle())!;
        await callouts(main, ["Lat/Lon/Alt Source:"], panelEl);
        await shotWithCallouts(main, panelEl, "7-export-source.png",
            "Export: lat/lon/alt source");

        expect(await clickInExportWindow(main, "accordion", "Annotation attributes")).toBe("clicked");
        await main.waitForTimeout(1000);
        expect(await clickInExportWindow(main, "accordion", "Point attributes")).toBe("clicked");
        await main.waitForTimeout(1000);
        await callouts(main, ["Height range, max - min (m)", "Avg height (m)", "Slope (deg)",
                              "Geographic lat / lon / alt (alt: see tooltip)"], panelEl);
        await shotWithCallouts(main, panelEl, "8-export-fields.png",
            "Export: fields");
    } finally {
        await context.close();
        await app.stop();
    }

    // mosaic: the shots in a captioned grid, rendered by the same browser
    const cells = shots.map((s) => `<figure><img src="${s.file}"><figcaption>${s.caption}</figcaption></figure>`).join("\n");
    const html = `<!doctype html><meta charset="utf-8"><style>
        body{margin:0;padding:16px;background:#1b1b1b;color:#ddd;font:13px/1.4 sans-serif;width:1880px}
        h1{font-size:18px;margin:0 0 4px} p{margin:0 0 14px;color:#aaa}
        .g{column-count:3;column-gap:14px}
        figure{break-inside:avoid;margin:0 0 14px;background:#2a2a2a;border:1px solid #444;border-radius:6px;padding:8px}
        img{max-width:100%;display:block;margin:0 auto 6px} figcaption{color:#ccc}
        </style><h1>Height labels and tooltips (#866) - Dimorphos</h1>
        <p>Numbers mark labels; boxes show their tooltips.</p>
        <div class="g">${cells}</div>`;
    const htmlPath = path.join(artifacts, "mosaic.html");
    fs.writeFileSync(htmlPath, html);
    const page = await browser.newPage({ viewport: { width: 1912, height: 1000 } });
    await page.goto("file:///" + htmlPath.replace(/\\/g, "/"));
    await page.waitForLoadState("load");
    const mosaic = await page.screenshot({ fullPage: true });
    save("mosaic.png", mosaic);
    await page.close();
});
