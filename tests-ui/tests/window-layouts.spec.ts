import { test, expect, Page, Browser, BrowserContext } from "@playwright/test";
import { launchPro3d, Pro3d, fixture, sceneFor } from "../src/pro3d";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

/**
 * Window layouts (#614, docs/WindowLayouts.md), driven through the main page with its
 * Golden Layout: built-in layouts, closing and reopening panels, reloading the page,
 * restarting PRo3D, the layout library, the sidecar written beside a saved scene (and a
 * save that must succeed when the sidecar cannot be written), the question asked when a
 * scene with a different layout is opened, and popouts.
 *
 * Every case runs with its own PRO3D_LAYOUT_DIR, so the user's layouts in AppData are
 * never touched and cases do not see each other's layouts.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "window-layouts");

test.setTimeout(10 * 60_000);
test.describe.configure({ mode: "serial" });

const M2020 = ["Main View", "Surfaces", "Annotations", "ScaleBars", "Instrument View", "GIS View", "Config", "Bookmarks", "Seq. Bookmarks", "Viewplans", "Properties", "Traverses"];

function freshLayoutDir(): string {
    return fs.mkdtempSync(path.join(os.tmpdir(), "pro3d-layouts-"));
}

function scene(name: string): string {
    fs.mkdirSync(artifacts, { recursive: true });
    return sceneFor(fixture.sceneTemplate, fixture.opc, path.join(artifacts, `${name}.pro3d`));
}

async function poll<T>(what: string, f: () => Promise<T> | T, ok: (v: T) => boolean, timeoutMs = 30_000): Promise<T> {
    const started = Date.now();
    let v = await f();
    while (!ok(v)) {
        if (Date.now() - started > timeoutMs) throw new Error(`timed out waiting for ${what}; last value: ${JSON.stringify(v)}`);
        await new Promise((r) => setTimeout(r, 250));
        v = await f();
    }
    return v;
}

async function openMain(browser: Browser, app: Pro3d, handshake = true): Promise<{ context: BrowserContext; page: Page }> {
    const context = await browser.newContext({ viewport: { width: 1600, height: 900 } });
    context.on("weberror", (e) => console.log("[page error]", e.error()));
    const page = await context.newPage();
    await page.goto(app.url);
    await page.waitForSelector(".lm_tab", { timeout: 120_000 });
    if (!handshake) return { context, page };
    // events clicked before the page's socket is up are lost: prove a round trip first
    await poll("the page talks to the server", async () => {
        if (!(await exists(page, '[data-test="layout-dialog"]'))) {
            await page.evaluate(() => (document.querySelector('[data-test="layout-manage"]') as HTMLElement | null)?.click());
            await page.waitForTimeout(500);
        }
        return exists(page, '[data-test="layout-dialog"]');
    }, (v) => v, 60_000);
    await page.evaluate(() => (document.querySelector('[data-test="layout-close"]') as HTMLElement | null)?.click());
    await poll("dialog closed", () => exists(page, '[data-test="layout-dialog"]'), (v) => !v);
    return { context, page };
}

/** tab titles of the main window, in DOM order */
async function tabs(page: Page): Promise<string[]> {
    return page.evaluate(() =>
        Array.from(document.querySelectorAll(".lm_tab .lm_title")).map((t) => (t.textContent ?? "").trim())
    );
}

async function waitTabs(page: Page, what: string, ok: (t: string[]) => boolean) {
    return poll(what, () => tabs(page), ok);
}

async function status(page: Page): Promise<string> {
    return page.evaluate(() =>
        Array.from(document.querySelectorAll(".topmenu"))
            .map((e) => (e.textContent ?? "").trim())
            .find((t) => t.startsWith("Layout:")) ?? ""
    );
}

/** clicks the element matching `selector` in one DOM call (see README: no locator actions) */
async function click(page: Page, selector: string) {
    const r = await page.evaluate((s) => {
        const el = document.querySelector(s) as HTMLElement | null;
        if (!el) return "missing";
        el.click();
        return "clicked";
    }, selector);
    expect(r, selector).toBe("clicked");
}

async function closeTab(page: Page, title: string) {
    const r = await page.evaluate((t) => {
        const tab = Array.from(document.querySelectorAll(".lm_tab")).find(
            (e) => (e.querySelector(".lm_title")?.textContent ?? "").trim() === t
        );
        const close = tab?.querySelector(".lm_close_tab") as HTMLElement | null;
        if (!tab) return "no tab";
        if (!close) return "no close button";
        close.click();
        return "closed";
    }, title);
    expect(r, `close ${title}`).toBe("closed");
}

async function setInput(page: Page, selector: string, value: string) {
    const r = await page.evaluate(
        ([s, v]) => {
            const el = document.querySelector(s) as HTMLInputElement | null;
            if (!el) return "missing";
            el.value = v;
            el.dispatchEvent(new Event("change", { bubbles: true }));
            return "set";
        },
        [selector, value]
    );
    expect(r, selector).toBe("set");
}

async function exists(page: Page, selector: string): Promise<boolean> {
    return page.evaluate((s) => document.querySelector(s) !== null, selector);
}

function readJson(file: string): any | undefined {
    try {
        return JSON.parse(fs.readFileSync(file, "utf-8"));
    } catch {
        return undefined;
    }
}

/** panel ids of a stored layout file (main window and popouts) */
function panelIds(file: any): string[] {
    const ids: string[] = [];
    const walk = (n: any) => {
        if (!n) return;
        if (n.type === "component") ids.push(n.componentType);
        (n.content ?? []).forEach(walk);
    };
    walk(file?.layout?.root);
    (file?.layout?.openPopouts ?? []).forEach((p: any) => walk(p.root));
    return ids;
}

/** waits until the user feedback overlay of the main view (inside its layout iframe) shows `text` */
async function feedbackShown(page: Page, text: string) {
    await poll(
        `user feedback "${text}"`,
        async () => {
            const frame = page.frames().find((f) => f.url().includes("page=render"));
            if (!frame) return "";
            try {
                return await frame.evaluate(() => document.body.innerText);
            } catch {
                return "";
            }
        },
        (t) => t.includes(text),
        60_000
    );
}

/** clicks a Scene menu entry with the Electron file dialog stubbed to answer `file` */
async function sceneMenu(page: Page, entry: "Save as" | "Open", file: string) {
    const r = await page.evaluate(
        ([entry, file]) => {
            const w = window as any;
            w.aardvark = w.aardvark || {};
            w.aardvark.dialog = {
                showSaveDialog: () => Promise.resolve({ canceled: false, filePath: file }),
                showOpenDialog: () => Promise.resolve({ canceled: false, filePaths: [file] }),
            };
            const item = Array.from(document.querySelectorAll(".ui.inverted.item")).find(
                (e) => (e.textContent ?? "").trim() === entry
            ) as HTMLElement | undefined;
            if (!item) return "no menu entry";
            item.click();
            return "clicked";
        },
        [entry, file] as const
    );
    expect(r, `Scene > ${entry}`).toBe("clicked");
}

test("built-in layouts, closing and reopening panels, reload and restart", async ({ browser }) => {
    const dir = freshLayoutDir();
    const sceneFile = scene("layouts-basic");
    let app = await launchPro3d(sceneFile, { PRO3D_LAYOUT_DIR: dir });
    try {
        let { context, page } = await openMain(browser, app);
        await waitTabs(page, "the M2020 layout", (t) => JSON.stringify(t) === JSON.stringify(M2020));
        expect(await status(page)).toBe("Layout: M2020");

        // the main view has no close button, neither on its tab nor on its stack
        const renderClosable = await page.evaluate(() => {
            const tab = Array.from(document.querySelectorAll(".lm_tab")).find(
                (e) => (e.querySelector(".lm_title")?.textContent ?? "").trim() === "Main View"
            );
            const stackClose = tab?.closest(".lm_stack")?.querySelector(".lm_controls .lm_close") as HTMLElement | null;
            return {
                tabClose: tab?.querySelector(".lm_close_tab") !== null,
                stackClose: stackClose ? getComputedStyle(stackClose).display !== "none" : false,
            };
        });
        expect(renderClosable).toEqual({ tabClose: false, stackClose: false });

        const current = path.join(dir, "current.json");
        await poll("the default stored", () => readJson(current), (f) => f?.name === "M2020" && panelIds(f).includes("gis"));

        await click(page, '[data-dashboard="PRo3D Core"]');
        await waitTabs(page, "the Core layout", (t) => !t.includes("GIS View") && t.includes("Config"));
        await poll("status Core", () => status(page), (s) => s === "Layout: PRo3D Core");

        await closeTab(page, "Surfaces");
        await waitTabs(page, "Surfaces closed", (t) => !t.includes("Surfaces"));
        await poll("modified status", () => status(page), (s) => s === "Layout: PRo3D Core (modified)");
        expect(await exists(page, '[data-test="layout-reopen"] [data-panel="surfaces"]')).toBe(true);

        await click(page, '[data-panel="surfaces"]');
        await waitTabs(page, "Surfaces reopened", (t) => t.includes("Surfaces") && t.includes("Config"));
        expect(await exists(page, '[data-test="layout-reopen"] [data-panel="surfaces"]')).toBe(false);

        await closeTab(page, "Annotations");
        await waitTabs(page, "Annotations closed", (t) => !t.includes("Annotations"));
        await poll("stored without annotations", () => readJson(current), (f) => !!f && !panelIds(f).includes("annotations") && panelIds(f).includes("config") && !panelIds(f).includes("gis"));

        // a reloaded page boots into the layout the user left, not the startup layout
        // and not the last applied one
        await page.reload();
        await page.waitForSelector(".lm_tab", { timeout: 120_000 });
        await page.waitForTimeout(3000); // a replayed stale layout would arrive right after boot
        await waitTabs(page, "layout after reload", (t) => t.includes("Surfaces") && t.includes("Config") && !t.includes("Annotations") && !t.includes("GIS View"));
        await context.close();

        // so does PRo3D after a restart
        await app.stop();
        app = await launchPro3d(sceneFile, { PRO3D_LAYOUT_DIR: dir });
        ({ context, page } = await openMain(browser, app));
        await page.waitForTimeout(3000);
        await waitTabs(page, "layout after restart", (t) => t.includes("Surfaces") && t.includes("Config") && !t.includes("Annotations") && !t.includes("GIS View"));
        expect(await status(page)).toBe("Layout: PRo3D Core");
        await page.screenshot({ path: path.join(artifacts, "after-restart.png") });
        await context.close();
    } finally {
        await app.stop();
    }
});

test("the layout library: save as, load, rename, delete", async ({ browser }) => {
    const dir = freshLayoutDir();
    const app = await launchPro3d(scene("layouts-library"), { PRO3D_LAYOUT_DIR: dir });
    try {
        const { context, page } = await openMain(browser, app);
        await click(page, '[data-dashboard="Provenance"]');
        await waitTabs(page, "Provenance layout", (t) => t.includes("Provenance"));

        await click(page, '[data-test="layout-save-as"]');
        await page.waitForSelector('[data-test="layout-name"]', { state: "attached" });
        await setInput(page, '[data-test="layout-name"]', "Workshop");
        await poll("name input reached the model", async () => {
            if (await exists(page, '[data-test="layout-save"]')) await click(page, '[data-test="layout-save"]');
            return fs.existsSync(path.join(dir, "library", "Workshop.json"));
        }, (v) => v, 10_000);
        await poll("dialog closed", () => exists(page, '[data-test="layout-dialog"]'), (v) => !v);
        await poll("status", () => status(page), (s) => s === "Layout: Workshop");
        await poll("library menu entry", () => exists(page, '[data-layout="Workshop"]'), (v) => v);

        await click(page, '[data-dashboard="Render Only"]');
        await waitTabs(page, "Render Only", (t) => JSON.stringify(t) === JSON.stringify(["Main View"]));
        await click(page, '[data-layout="Workshop"]');
        await waitTabs(page, "Workshop loaded", (t) => t.includes("Provenance") && t.includes("Main View"));

        await click(page, '[data-test="layout-manage"]');
        await page.waitForSelector('[data-layout-row="Workshop"]', { state: "attached" });
        await click(page, '[data-layout-row="Workshop"] [data-test="layout-rename"]');
        await page.waitForSelector('[data-test="layout-rename-confirm"]', { state: "attached" });
        await setInput(page, '[data-test="layout-name"]', "Field Campaign");
        await poll("renamed on disk", async () => {
            if (await exists(page, '[data-test="layout-rename-confirm"]')) await click(page, '[data-test="layout-rename-confirm"]');
            return fs.existsSync(path.join(dir, "library", "Field Campaign.json")) && !fs.existsSync(path.join(dir, "library", "Workshop.json"));
        }, (v) => v, 10_000);
        await page.waitForSelector('[data-layout-row="Field Campaign"]', { state: "attached" });
        await poll("status follows the rename", () => status(page), (s) => s === "Layout: Field Campaign");

        await click(page, '[data-layout-row="Field Campaign"] [data-test="layout-delete"]');
        await poll("deleted on disk", () => fs.existsSync(path.join(dir, "library", "Field Campaign.json")), (v) => !v);
        await click(page, '[data-test="layout-close"]');
        await poll("dialog closed", () => exists(page, '[data-test="layout-dialog"]'), (v) => !v);
        await poll("library menu empty", () => exists(page, '[data-layout="Field Campaign"]'), (v) => !v);
        await context.close();
    } finally {
        await app.stop();
    }
});

test("saving a scene writes its layout beside it, and a blocked sidecar does not fail the save", async ({ browser }) => {
    const dir = freshLayoutDir();
    const template = scene("layouts-save-template");
    const templateDockConfig = readJson(template).dockConfig;
    expect(typeof templateDockConfig, "the 6.x scene fixture carries a dockConfig").toBe("string");

    const app = await launchPro3d(template, { PRO3D_LAYOUT_DIR: dir });
    try {
        const { context, page } = await openMain(browser, app);
        await click(page, '[data-dashboard="PRo3D Core"]');
        await waitTabs(page, "Core layout", (t) => !t.includes("GIS View") && t.includes("Config"));

        const saved = path.join(artifacts, "layouts-saved.pro3d");
        for (const f of [saved, saved + ".layout"]) fs.rmSync(f, { force: true, recursive: true });
        await sceneMenu(page, "Save as", saved);
        const sidecar = await poll("sidecar", () => readJson(saved + ".layout"), (f) => !!f);
        expect(sidecar.format).toBe("pro3d-layout");
        expect(panelIds(sidecar)).toContain("config");
        expect(panelIds(sidecar)).not.toContain("gis");
        expect(panelIds(sidecar)).toContain("render");
        // PRo3D <= 6.2 needs dockConfig; a loaded scene writes back what it read
        const savedScene = await poll("saved scene", () => readJson(saved), (f) => !!f);
        expect(savedScene.dockConfig).toBe(templateDockConfig);

        const blocked = path.join(artifacts, "layouts-blocked.pro3d");
        fs.rmSync(blocked, { force: true });
        fs.rmSync(blocked + ".layout", { force: true, recursive: true });
        fs.mkdirSync(blocked + ".layout"); // a directory where the sidecar goes
        await sceneMenu(page, "Save as", blocked);
        const blockedScene = await poll("scene saved despite the blocked sidecar", () => readJson(blocked), (f) => !!f);
        expect(blockedScene.dockConfig).toBe(templateDockConfig);
        await poll(
            "the failure is logged",
            () => fs.readFileSync(app.logFile, "utf-8"),
            (log) => log.includes("scene saved, but not the layout beside it")
        );
        await feedbackShown(page, "window layout could not be stored beside it");
        expect(fs.statSync(blocked + ".layout").isDirectory()).toBe(true);
        await context.close();
    } finally {
        await app.stop();
    }
});

test("opening a scene with a different layout asks whether to import and to apply it", async ({ browser }) => {
    const dir = freshLayoutDir();
    const renderOnly = {
        format: "pro3d-layout",
        version: 1,
        name: "whatever",
        layout: { root: { type: "stack", content: [{ type: "component", componentType: "render", title: "x" }] }, openPopouts: [] },
    };

    // at startup: the dialog is waiting on the first page; ignoring keeps the layout
    const atStart = scene("layouts-shared-start");
    fs.writeFileSync(atStart + ".layout", JSON.stringify(renderOnly));
    const app = await launchPro3d(atStart, { PRO3D_LAYOUT_DIR: dir });
    try {
        // no handshake: the dialog is already open; clicking Ignore until it closes is the round trip
        const { context, page } = await openMain(browser, app, false);
        await page.waitForSelector('[data-test="layout-scene-ignore"]', { state: "attached", timeout: 60_000 });
        await poll("dialog closed", async () => {
            await page.evaluate(() => (document.querySelector('[data-test="layout-scene-ignore"]') as HTMLElement | null)?.click());
            await page.waitForTimeout(500);
            return exists(page, '[data-test="layout-dialog"]');
        }, (v) => !v, 60_000);
        await waitTabs(page, "layout unchanged", (t) => JSON.stringify(t) === JSON.stringify(M2020));
        expect(fs.existsSync(path.join(dir, "library"))).toBe(false);

        // opened later: import and apply
        const shared = scene("layouts-shared");
        fs.writeFileSync(shared + ".layout", JSON.stringify(renderOnly));
        await sceneMenu(page, "Open", shared);
        await page.waitForSelector('[data-test="layout-scene-import"]', { state: "attached", timeout: 60_000 });
        await click(page, '[data-test="layout-scene-import"]');
        await poll("import checked", () => page.evaluate(() => (document.querySelector('[data-test="layout-scene-import"] input') as HTMLInputElement | null)?.checked ?? false), (v) => v);
        await click(page, '[data-test="layout-scene-ok"]');
        await waitTabs(page, "the scene's layout", (t) => JSON.stringify(t) === JSON.stringify(["Main View"]));
        await poll("imported into the library", () => fs.existsSync(path.join(dir, "library", "layouts-shared.json")), (v) => v);
        await poll("status", () => status(page), (s) => s === "Layout: layouts-shared");

        // same arrangement as the current one: nothing to ask
        await sceneMenu(page, "Open", shared);
        await page.waitForTimeout(5000);
        expect(await exists(page, '[data-test="layout-dialog"]')).toBe(false);

        // a broken sidecar is ignored, the scene still loads
        const broken = scene("layouts-broken");
        fs.writeFileSync(broken + ".layout", '{"format":"pro3d-layout","version":1,"layout":{"root":');
        await sceneMenu(page, "Open", broken);
        await poll("broken sidecar logged", () => fs.readFileSync(app.logFile, "utf-8"), (log) => log.includes("ignoring the layout beside"));
        await feedbackShown(page, "layout stored beside the scene could not be read");
        await poll("scene loaded", () => fs.readFileSync(app.logFile, "utf-8"), (log) => log.includes("loaded scene: " + broken) || log.includes("loaded scene: " + broken.replace(/\//g, "\\")));
        expect(await exists(page, '[data-test="layout-dialog"]')).toBe(false);
        await context.close();
    } finally {
        await app.stop();
    }
});

test("broken layout files: the user is told, PRo3D keeps working", async ({ browser }) => {
    const dir = freshLayoutDir();
    // a last-used layout cut off mid-write, and a library entry that is no layout at all
    fs.writeFileSync(path.join(dir, "current.json"), '{"format":"pro3d-layout","version":1,"name":"x","layout":{"root":{"type":"row","content":[');
    fs.mkdirSync(path.join(dir, "library"));
    fs.writeFileSync(path.join(dir, "library", "Damaged.json"), "this is not json");

    const app = await launchPro3d(scene("layouts-broken-files"), { PRO3D_LAYOUT_DIR: dir });
    try {
        // a notice that stays until read (a toast would be gone before the window shows);
        // no handshake: acknowledging it is the round trip
        const { context, page } = await openMain(browser, app, false);
        await page.waitForSelector('[data-test="layout-notice"]', { state: "attached", timeout: 60_000 });
        const notice = await page.evaluate(() => document.querySelector('[data-test="layout-notice"]')?.textContent ?? "");
        expect(notice).toContain("last window layout could not be read");
        expect(notice).toContain("current.json.corrupt");
        await page.screenshot({ path: path.join(artifacts, "broken-current-notice.png") });
        await poll("notice acknowledged", async () => {
            await page.evaluate(() => (document.querySelector('[data-test="layout-notice-ok"]') as HTMLElement | null)?.click());
            await page.waitForTimeout(500);
            return exists(page, '[data-test="layout-dialog"]');
        }, (v) => !v, 60_000);
        await waitTabs(page, "the default layout instead", (t) => JSON.stringify(t) === JSON.stringify(M2020));
        expect(fs.existsSync(path.join(dir, "current.json.corrupt")), "kept aside").toBe(true);
        await poll("a fresh current layout", () => readJson(path.join(dir, "current.json")), (f) => f?.name === "M2020");

        // the damaged entry is listed, opening it explains the problem and changes nothing
        await poll("damaged entry listed", () => exists(page, '[data-layout="Damaged"]'), (v) => v);
        await click(page, '[data-layout="Damaged"]');
        await feedbackShown(page, "The layout 'Damaged' cannot be read");
        await page.waitForTimeout(2000);
        expect(await tabs(page)).toEqual(M2020);
        expect(await status(page)).toBe("Layout: M2020");

        // and it can be deleted
        await click(page, '[data-test="layout-manage"]');
        await page.waitForSelector('[data-layout-row="Damaged"]', { state: "attached" });
        await click(page, '[data-layout-row="Damaged"] [data-test="layout-delete"]');
        await poll("deleted", () => fs.existsSync(path.join(dir, "library", "Damaged.json")), (v) => !v);
        await click(page, '[data-test="layout-close"]');
        await context.close();
    } finally {
        await app.stop();
    }
});

test("a popped out stack is part of the layout and docks back when its window closes", async ({ browser }) => {
    const dir = freshLayoutDir();
    const app = await launchPro3d(scene("layouts-popout"), { PRO3D_LAYOUT_DIR: dir });
    try {
        const { context, page } = await openMain(browser, app);
        await waitTabs(page, "M2020", (t) => t.includes("Surfaces"));

        const popup = context.waitForEvent("page", { timeout: 60_000 });
        const r = await page.evaluate(() => {
            const tab = Array.from(document.querySelectorAll(".lm_tab")).find(
                (e) => (e.querySelector(".lm_title")?.textContent ?? "").trim() === "Surfaces"
            );
            const button = tab?.closest(".lm_stack")?.querySelector(".lm_controls .lm_popout") as HTMLElement | null;
            if (!button) return "no popout button";
            button.click();
            return "clicked";
        });
        expect(r).toBe("clicked");
        const window = await popup;
        await window.waitForLoadState();
        expect(window.url()).toContain("gl-popout");

        await waitTabs(page, "stack moved out of the main window", (t) => !t.includes("Surfaces") && t.includes("Main View"));
        const current = path.join(dir, "current.json");
        const withPopout = await poll("popout stored", () => readJson(current), (f) => (f?.layout?.openPopouts ?? []).length === 1);
        const popoutIds: string[] = [];
        const walk = (n: any) => { if (n?.type === "component") popoutIds.push(n.componentType); (n?.content ?? []).forEach(walk); };
        walk(withPopout.layout.openPopouts[0].root);
        expect(popoutIds).toContain("surfaces");

        await page.screenshot({ path: path.join(artifacts, "popout-main.png") });
        await window.screenshot({ path: path.join(artifacts, "popout-window.png") });
        // as the user closing it: the window closes itself, which runs its unload handlers
        // (Playwright's page.close skips them, and Golden Layout docks back on unload)
        await Promise.all([window.waitForEvent("close"), window.evaluate(() => window.close())]);
        await waitTabs(page, "docked back", (t) => t.includes("Surfaces"));
        await poll("no popout stored", () => readJson(current), (f) => !!f && (f.layout.openPopouts ?? []).length === 0 && panelIds(f).includes("surfaces"));
        await context.close();
    } finally {
        await app.stop();
    }
});
