import { expect, Page } from "@playwright/test";

/**
 * Shared steps for specs that drive the annotation export window (AnnotationExportApp):
 * the top menu, the stubbed save dialog, the window's accordions and checkboxes, and
 * reading the CSV back.
 */

/** Panel of the export window (AnnotationExportApp). */
export const exportDimmer = ".annotation-export-dimmer";

/** The Electron save dialog cannot be driven from Chromium, so it is stubbed to answer
 *  with `filePath` -- the same shape the import specs use for showOpenDialog. */
export async function stubSaveDialog(main: Page, filePath: string) {
    await main.evaluate((p) => {
        const w = window as any;
        w.aardvark = w.aardvark ?? {};
        w.aardvark.dialog = { showSaveDialog: (_o: unknown) => Promise.resolve({ filePath: p }) };
    }, filePath.replace(/\\/g, "/"));
}

/** Opens a top-menu dropdown and clicks one of its items. */
export async function menu(main: Page, dropdown: string, item: string) {
    const r = await main.evaluate(
        ({ d, i }) => {
            const dd = Array.from(document.querySelectorAll(".ui.dropdown.item")).find((e) =>
                (e.textContent ?? "").startsWith(d)
            ) as HTMLElement | null;
            if (!dd) return `no ${d} dropdown`;
            dd.click();
            const entry = Array.from(dd.querySelectorAll(".item")).find(
                (e) => (e.textContent ?? "").trim() === i
            ) as HTMLElement | null;
            if (!entry) return `no ${i} item`;
            entry.click();
            return "clicked";
        },
        { d: dropdown, i: item }
    );
    expect(r, `${dropdown} -> ${item}`).toBe("clicked");
}

/** Clicks the element in the export window whose label is `label`: an accordion title
 *  (a `.title` holding a span with that text) or a checkbox (an <i> icon followed by its
 *  <span> label, AnnotationExportApp.checkBox). Returns what happened, for the assertion. */
export async function clickInExportWindow(main: Page, kind: "accordion" | "checkbox", label: string) {
    return main.evaluate(
        ({ d, k, l }) => {
            const spans = Array.from(document.querySelectorAll(`${d} span`)).filter(
                (s) => (s.textContent ?? "").trim() === l
            );
            const target =
                k === "accordion"
                    ? (spans.map((s) => s.closest(".title")).find((t) => t) as HTMLElement | null)
                    : (spans[0]?.previousElementSibling as HTMLElement | null);
            if (!target) return `no ${l} ${k}`;
            target.click();
            return "clicked";
        },
        { d: exportDimmer, k: kind, l: label }
    );
}

/** Rows of a CSV, honouring quoted cells (a comma in an annotation's text is quoted). */
export function parseCsv(text: string): string[][] {
    const rows: string[][] = [];
    let row: string[] = [];
    let cell = "";
    let quoted = false;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (quoted) {
            if (c === '"' && text[i + 1] === '"') {
                cell += '"';
                i++;
            } else if (c === '"') quoted = false;
            else cell += c;
        } else if (c === '"') quoted = true;
        else if (c === ",") {
            row.push(cell);
            cell = "";
        } else if (c === "\n" || c === "\r") {
            if (c === "\r" && text[i + 1] === "\n") i++;
            row.push(cell);
            rows.push(row);
            row = [];
            cell = "";
        } else cell += c;
    }
    if (cell !== "" || row.length > 0) {
        row.push(cell);
        rows.push(row);
    }
    return rows;
}

/** A CSV file as header + rows, with a by-name cell accessor. */
export function readCsv(text: string) {
    const [header, ...rows] = parseCsv(text.trim());
    const cell = (row: string[], col: string) => row[header.indexOf(col)] ?? "";
    return { header, rows, cell };
}

/** Clicks the export window's primary button and waits for `files` to appear. When they
 *  do not, the window's warning (it stays open when it refuses to write) is the diagnosis. */
export async function runExport(main: Page, files: string[], exists: (f: string) => boolean) {
    await main.locator(`${exportDimmer} .ui.primary.button`).first().click();
    try {
        await expect
            .poll(() => files.every(exists), { timeout: 240_000, intervals: [1000] })
            .toBe(true);
    } catch {
        const warning = await main
            .locator(`${exportDimmer} .warning.message`)
            .first()
            .textContent()
            .catch(() => null);
        throw new Error(
            `not all of ${files.join(", ")} were written. Export window warning: ${warning ?? "(none)"}`
        );
    }
}
