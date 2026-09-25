import { test, expect, Page, BrowserContext } from "@playwright/test";
import { launchPro3d, Pro3d, fixture, derivedScene as deriveScene, surfaceShadersReady } from "../src/pro3d";
import { litFraction, streamLive } from "../src/image";
import * as fs from "fs";
import * as http from "http";
import * as path from "path";

/**
 * Double-click finishes an annotation (#824, docs/DoubleClickFinish.md), driven with real
 * double-clicks in the running viewer on the Dimorphos OPC.
 *
 * A double-click reaches the viewer as click, click, dblclick: two picks at the same pixel,
 * then the finish. Every case checks the saved annotations (the scene's .ann written by
 * /api/saveScene), so a duplicate vertex or a missing finish shows up as a wrong count.
 *
 * One viewer runs all cases in order; each case measures what it added.
 */

const artifacts = path.join(__dirname, "..", "artifacts", "double-click-finish");

test.setTimeout(20 * 60_000);
test.describe.configure({ mode: "serial" });

// Geometry values as saved (PRo3D.Base.Annotation.Geometry)
const G = { Point: 0, Polyline: 2, Polygon: 3, DnS: 4 };

type Saved = { key: string; geometry: number; points: number[][]; segments: number };

let app: Pro3d;
let scene: string;
let context: BrowserContext;
let render: Page;
let main: Page;

// ---------------------------------------------------------------------------------------------
// helpers

function post(url: string, body: unknown): Promise<string> {
    return new Promise((resolve, reject) => {
        const data = Buffer.from(JSON.stringify(body));
        const req = http.request(url, { method: "POST", headers: { "Content-Type": "application/json", "Content-Length": data.length } }, (res) => {
            let out = "";
            res.on("data", (c) => (out += c));
            res.on("end", () => resolve(out));
        });
        req.on("error", reject);
        req.end(data);
    });
}

/** GET /busy: whether an update is running right now (docs/BusyIndicator.md) */
function busy(): Promise<boolean> {
    return new Promise((resolve, reject) => {
        const req = http.get(app.url + "busy", (res) => {
            let out = "";
            res.on("data", (c) => (out += c));
            res.on("end", () => resolve(JSON.parse(out).busy === true));
        });
        req.on("error", reject);
        req.setTimeout(10_000, () => req.destroy(new Error("/busy timed out")));
    });
}

/** Waits until the viewer has worked through what the browser sent: a pick on a cold KdTree
 *  takes seconds, and each update runs after the one before. Launched with -busyms 1, so
 *  /busy reports every running update; idle three times in a row means the queue is empty. */
async function settle() {
    await new Promise((r) => setTimeout(r, 250)); // let the events reach the server
    let idle = 0;
    const started = Date.now();
    while (idle < 3) {
        if (Date.now() - started > 120_000) throw new Error("viewer still busy after 120 s");
        idle = (await busy()) ? 0 : idle + 1;
        await new Promise((r) => setTimeout(r, 150));
    }
}

const parseV3 = (s: string) => JSON.parse(s) as number[];

/** every finished annotation, as the viewer saves them */
async function saved(): Promise<Saved[]> {
    await settle();
    const ann = scene + ".ann"; // the viewer appends: <scene>.pro3d.ann
    const before = fs.existsSync(ann) ? fs.statSync(ann).mtimeMs : 0;
    await post(app.url + "api/saveScene", { sceneFile: scene });
    await expect.poll(() => (fs.existsSync(ann) ? fs.statSync(ann).mtimeMs : 0), { timeout: 60_000 }).toBeGreaterThan(before);
    await new Promise((r) => setTimeout(r, 300)); // let the writer finish
    const d = JSON.parse(fs.readFileSync(ann, "utf-8").replace(/^﻿/, ""));
    return (d.annotations.flat as any[])
        .map((l) => l.Annotations)
        .filter((a) => a)
        .map((a) => ({
            key: a.key,
            geometry: a.geometry,
            points: (a.points.points as string[]).map(parseV3),
            segments: (a.segments?.segments ?? a.segments ?? []).length,
        }));
}

/** what `after` has that `before` did not */
const added = (before: Saved[], after: Saved[]) => after.filter((a) => !before.some((b) => b.key === a.key));

const dist = (a: number[], b: number[]) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);

/** points without the closing repeat of a ring, none of them doubled */
function distinctVertices(points: number[][]): number[][] {
    const out: number[][] = [];
    for (const p of points) if (!out.some((q) => dist(p, q) < 1e-9)) out.push(p);
    return out;
}

/** a position in the render control, as fractions of its size from its centre */
async function at(fx: number, fy: number): Promise<{ x: number; y: number }> {
    const box = await render.locator(".mainrendercontrol").first().boundingBox();
    if (!box) throw new Error("no render control");
    return { x: box.x + box.width * (0.5 + fx), y: box.y + box.height * (0.5 + fy) };
}

async function click(fx: number, fy: number) {
    const p = await at(fx, fy);
    await render.mouse.click(p.x, p.y);
    await settle();
}

async function dblclick(fx: number, fy: number) {
    const p = await at(fx, fy);
    await render.mouse.dblclick(p.x, p.y);
    await settle();
}

/** the tool strip button with this icon (render page) */
async function tool(icon: string) {
    const r = await render.evaluate((icon) => {
        const i = Array.from(document.querySelectorAll(".pro3d-toolstrip i")).find((e) => e.className.trim() === `${icon} icon`);
        const t = i?.closest(".pro3d-tool") as HTMLElement | null;
        if (!t) return `no ${icon} tool`;
        t.click();
        return "ok";
    }, icon);
    expect(r).toBe("ok");
    await settle();
}

/** picks `option` in whichever <select> of the main page offers it */
async function choose(option: string) {
    await expect
        .poll(
            () =>
                main.evaluate((o) => {
                    const sel = Array.from(document.querySelectorAll("select")).find((s) =>
                        Array.from(s.options).some((x) => (x.textContent ?? "").trim() === o)
                    );
                    if (!sel) return `no select offering ${o}`;
                    const opt = Array.from(sel.options).find((x) => (x.textContent ?? "").trim() === o)!;
                    if (opt.disabled) return `${o} is disabled`;
                    sel.value = opt.value;
                    sel.dispatchEvent(new Event("change", { bubbles: true }));
                    return "ok";
                }, option),
            { timeout: 30_000 }
        )
        .toBe("ok");
    await settle();
}

/** clicks the checkbox in the row labelled `label` on `page`, until it reads `on` */
async function setCheckbox(page: Page, label: string, on: boolean) {
    await expect
        .poll(
            () =>
                page.evaluate(
                    ({ label, on }) => {
                        // the innermost element carrying the label, then outwards to the
                        // nearest container that holds a checkbox
                        const labels = Array.from(document.querySelectorAll("td, div, span")).filter(
                            (e) => (e.textContent ?? "").trim() === label
                        );
                        let node: Element | null = labels[labels.length - 1] ?? null;
                        let box: HTMLElement | null = null;
                        while (node && !box) {
                            box = node.querySelector("i.square.outline.icon");
                            node = node.parentElement;
                        }
                        if (!box) return `no checkbox beside ${label}`;
                        const checked = box.classList.contains("check");
                        if (checked === on) return "ok";
                        box.click();
                        return "clicked";
                    },
                    { label, on }
                ),
            { timeout: 30_000 }
        )
        .toBe("ok");
}

/** draws in the classic scheme: the tool is armed while Ctrl is held */
async function withCtrl(f: () => Promise<void>) {
    const p = await at(0, 0);
    await render.mouse.move(p.x, p.y);
    // keys only reach the render control while it has focus; it takes focus on mouseenter,
    // but a keydown sent right behind the move can beat it
    await render.locator(".mainrendercontrol").first().focus();
    await render.keyboard.down("Control");
    await settle();
    try {
        await f();
    } finally {
        await render.keyboard.up("Control");
        await settle();
    }
}

async function enter() {
    // a click elsewhere (the tool strip) takes the keyboard focus away from the render control
    await render.locator(".mainrendercontrol").first().focus();
    await render.keyboard.press("Enter");
    await settle();
}

// A small triangle around the centre of the view, where Dimorphos is; the cut stroke
// runs across it with both ends outside.
const A = [-0.04, -0.03], B = [0.04, -0.03], C = [0.0, 0.04];

// ---------------------------------------------------------------------------------------------

test.beforeAll(async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");
    fs.mkdirSync(artifacts, { recursive: true });
    // set up in the GIS view only, so loading gives the scene a body (Dimorphos): DnS
    // needs a reference body (docs/SceneBody.md)
    scene = deriveScene(
        "double-click",
        (d) => {
            d.gisApp.gisSurfaces = [];
            d.gisApp.defaultObservationInfo.observer = { EntitySpiceName: "Dimorphos" };
            d.gisApp.defaultObservationInfo.referenceFrame = { FrameSpiceName: "DIMORPHOS_FIXED" };
            d.referenceSystem.planet = 2;
            d.navigationMode = 0;
        },
        artifacts
    );
    app = await launchPro3d(scene, undefined, ["--remoteApi", "-busyms", "1"]);
    context = await browser.newContext({ viewport: { width: 1600, height: 900 } });
    context.on("weberror", (e) => console.log("[page error]", e.error()));
    render = await context.newPage();
    await render.goto(app.url + "?page=render");
    await render.waitForSelector("img.rendercontrol", { timeout: 60_000 });
    await surfaceShadersReady(render);

    // content on screen before the first pick
    const started = Date.now();
    let shot = await render.screenshot();
    while ((!streamLive(shot) || litFraction(shot) < 0.01) && Date.now() - started < 600_000) {
        await render.waitForTimeout(3000);
        shot = await render.screenshot();
    }
    fs.writeFileSync(path.join(artifacts, "loaded.png"), shot);
    expect(litFraction(shot), "Dimorphos is in view").toBeGreaterThan(0.01);

    main = await context.newPage();
    await main.goto(app.url);
    await main.waitForLoadState("domcontentloaded");

    await tool("pencil");
});

test.afterAll(async () => {
    await context?.close();
    await app?.stop();
});

test("Enter still finishes a polyline (baseline: the picks hit the surface)", async () => {
    await choose("Polyline");
    const before = await saved();
    await withCtrl(async () => {
        await click(A[0], A[1]);
        await click(B[0], B[1]);
        await click(C[0], C[1]);
    });
    await enter();
    const n = added(before, await saved());
    expect(n.length, "one polyline").toBe(1);
    expect(n[0].geometry).toBe(G.Polyline);
    expect(n[0].points.length, "three clicks, three points").toBe(3);
});

test("double-click finishes a polyline, without a duplicate vertex", async () => {
    await choose("Polyline");
    const before = await saved();
    await withCtrl(async () => {
        await click(A[0], A[1]);
        await click(B[0], B[1]);
        await dblclick(C[0], C[1]);
    });
    await render.screenshot({ path: path.join(artifacts, "polyline.png") });
    const n = added(before, await saved());
    expect(n.length, "the double-click finished it").toBe(1);
    expect(n[0].geometry).toBe(G.Polyline);
    expect(n[0].points.length, "a, b, c - the second click's point is gone").toBe(3);
    expect(distinctVertices(n[0].points).length).toBe(3);
});

test("a double-click whose clicks land 2 px apart drops the second point", async () => {
    // A still mouse gives both clicks the same ray, and PickSurface ignores a repeated ray
    // (lastHash) - so the duplicate only exists when the mouse moved a little in between.
    await choose("Polyline");
    const before = await saved();
    const fourPoints = () => (fs.readFileSync(app.logFile, "utf-8").match(/working contains 4 points/g) ?? []).length;
    const fourBefore = fourPoints();
    await withCtrl(async () => {
        await click(A[0], A[1]);
        await click(B[0], B[1]);
        const p = await at(C[0], C[1]);
        await render.mouse.move(p.x, p.y);
        await render.mouse.down({ clickCount: 1 });
        await render.mouse.up({ clickCount: 1 });
        await render.mouse.move(p.x + 2, p.y + 1);
        await render.mouse.down({ clickCount: 2 });
        await render.mouse.up({ clickCount: 2 }); // clickCount 2 on release: the browser fires dblclick
        await settle();
    });
    const n = added(before, await saved());
    expect(n.length, "the double-click finished it").toBe(1);
    expect(n[0].points.length, "a, b, c - the point 2 px from c is gone").toBe(3);
    // the second click really did place a point that the finish then dropped
    expect(fourPoints(), "the second click placed a fourth point").toBeGreaterThan(fourBefore);
});

test("double-click finishes a polygon, closed", async () => {
    await choose("Polygon");
    const before = await saved();
    await withCtrl(async () => {
        await click(A[0], A[1]);
        await click(B[0], B[1]);
        await dblclick(C[0], C[1]);
    });
    const n = added(before, await saved());
    expect(n.length).toBe(1);
    expect(n[0].geometry).toBe(G.Polygon);
    expect(distinctVertices(n[0].points).length, "three vertices").toBe(3);
});

test("double-click finishes dip and strike", async () => {
    await choose("DnS");
    const before = await saved();
    await withCtrl(async () => {
        await click(A[0], A[1]);
        await click(B[0], B[1]);
        await dblclick(C[0], C[1]);
    });
    const n = added(before, await saved());
    expect(n.length).toBe(1);
    expect(n[0].geometry).toBe(G.DnS);
    expect(n[0].points.length).toBe(3);
});

test("a double-click too early places its point and drawing continues", async () => {
    await choose("Polygon");
    const before = await saved();
    await withCtrl(async () => {
        await click(A[0], A[1]);
        await dblclick(B[0], B[1]); // two points: not a polygon yet
    });
    expect(added(before, await saved()).length, "nothing finished yet").toBe(0);
    await withCtrl(async () => {
        await dblclick(C[0], C[1]);
    });
    const n = added(before, await saved());
    expect(n.length, "the third point finishes it").toBe(1);
    expect(distinctVertices(n[0].points).length, "a, b, c").toBe(3);
});

test("a projected polyline keeps one segment per pair of points", async () => {
    await choose("Polyline");
    await choose("Viewpoint");
    try {
        const before = await saved();
        await withCtrl(async () => {
            await click(A[0], A[1]);
            await click(B[0], B[1]);
            await dblclick(C[0], C[1]);
        });
        const n = added(before, await saved());
        expect(n.length).toBe(1);
        expect(n[0].points.length).toBe(3);
        expect(n[0].segments, "the duplicate's segment went with it").toBe(2);
    } finally {
        await choose("Linear");
    }
});

test("a Point double-click places one point and finishes nothing else", async () => {
    await choose("Point");
    const before = await saved();
    await withCtrl(async () => {
        await dblclick(A[0], A[1]);
    });
    const n = added(before, await saved());
    // the second click repeats the first click's ray, which PickSurface ignores (lastHash):
    // one point, as before double-click existed
    expect(n.length, "one point, nothing else").toBe(1);
    expect(n.every((a) => a.geometry === G.Point)).toBe(true);
});

test("double-click on the tool strip does not finish", async () => {
    await choose("Polyline");
    const before = await saved();
    await withCtrl(async () => {
        // not starting at A: the case before picked A last, and PickSurface ignores a pick
        // that repeats the previous ray exactly (lastHash)
        await click(C[0], C[1]);
        await click(B[0], B[1]);
        // a divider: inside the strip (which stops dblclick), but no button to trigger
        const divider = await render.locator(".pro3d-toolstrip .pro3d-tool-divider").first().boundingBox();
        expect(divider, "the tool strip has a divider").not.toBeNull();
        await render.mouse.dblclick(divider!.x + divider!.width / 2, divider!.y + divider!.height / 2);
        await settle();
    });
    expect(added(before, await saved()).length, "the overlay swallowed it").toBe(0);
    await enter();
    const n = added(before, await saved());
    expect(n.length).toBe(1);
    expect(n[0].points.length, "Enter finishes what was drawn").toBe(2);
});

test("switched off in the config, double-click only places points", async () => {
    const config = await context.newPage();
    await config.goto(app.url + "?page=config");
    await config.waitForLoadState("domcontentloaded");
    await setCheckbox(config, "Double-click finishes:", false);
    try {
        await choose("Polyline");
        const before = await saved();
        await withCtrl(async () => {
            await click(A[0], A[1]);
            await click(B[0], B[1]);
            await dblclick(C[0], C[1]);
        });
        expect(added(before, await saved()).length, "no finish").toBe(0);
        await enter();
        const n = added(before, await saved());
        expect(n.length).toBe(1);
        expect(n[0].points.length, "a, b, c - the repeated ray adds nothing, and nothing finished early").toBe(3);
    } finally {
        await setCheckbox(config, "Double-click finishes:", true);
        await config.close();
    }
});

test("Direct Tool Mode: double-click finishes without Ctrl", async () => {
    await setCheckbox(main, "Direct Tool Mode:", true);
    try {
        await choose("Polyline");
        const before = await saved();
        const p = await at(0, 0);
        await render.mouse.move(p.x, p.y);
        await click(A[0], A[1]);
        await click(B[0], B[1]);
        await dblclick(C[0], C[1]);
        const n = added(before, await saved());
        expect(n.length).toBe(1);
        expect(n[0].points.length).toBe(3);
    } finally {
        await setCheckbox(main, "Direct Tool Mode:", false);
    }
});

test("without Ctrl in the classic scheme a double-click does nothing", async () => {
    await choose("Polyline");
    const before = await saved();
    await withCtrl(async () => {
        await click(A[0], A[1]);
        await click(B[0], B[1]);
    });
    await dblclick(C[0], C[1]); // tool not armed: no points, no finish
    expect(added(before, await saved()).length).toBe(0);
    await enter();
    const n = added(before, await saved());
    expect(n.length).toBe(1);
    expect(n[0].points.length, "only the armed clicks placed points").toBe(2);
});

test("double-click applies a cut stroke", async () => {
    await choose("Polygon");
    const beforePolygon = await saved();
    await withCtrl(async () => {
        await click(A[0], A[1]);
        await click(B[0], B[1]);
        await dblclick(C[0], C[1]);
    });
    const [target] = added(beforePolygon, await saved());
    expect(target, "a polygon to cut (selected on finish)").toBeTruthy();

    await tool("cut");
    try {
        const before = await saved();
        await withCtrl(async () => {
            await click(-0.09, 0.0);
            await dblclick(0.09, 0.0);
        });
        await render.screenshot({ path: path.join(artifacts, "cut.png") });
        const after = await saved();
        expect(after.some((a) => a.key === target.key), "the polygon was replaced").toBe(false);
        const pieces = added(before, after);
        expect(pieces.length, "two pieces").toBe(2);
    } finally {
        await tool("pencil");
    }
});
