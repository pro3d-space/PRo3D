// Busy indicator: PRo3D says what it is doing while it is too busy to say anything.
//
// The property under test is awkward, and it is the whole point of the design. PRo3D's
// update thread, its DOM-diff thread and the render service all take one lock, so during a
// slow update the server cannot repaint any page and the 3D stream stops. Anything driven
// from the model is therefore unobservable exactly when it matters.
//
// So the two things this spec proves are:
//   1. `/busy` keeps answering, promptly, *while* the update thread is blocked - it is a
//      route that reads one field and takes no lock (Program.fs, Busy.fs).
//   2. the overlay in the main page reacts during the stall - it is client-side JavaScript
//      and CSS (resources/utilities.js), fed by that route.
//
// Both are checked against real long-running operations: the ArcBall camera switch, which
// intersects the centre ray against every active surface synchronously
// (Navigation.pickOrbitCenter -> cold KdTrees), and an OPC import.
//
// See docs/BusyIndicator.md and plans/stallFeedback.md.
import { test, expect, Page } from "@playwright/test";
import * as http from "http";
import * as fs from "fs";
import * as path from "path";
import { launchPro3d, Pro3d, fixture, surfaceShadersReady } from "../src/pro3d";

let app: Pro3d;
const artifacts = path.join(__dirname, "..", "artifacts");

// a cold shader cache keeps the surface out of the view for minutes; the gates below wait
test.setTimeout(15 * 60_000);

test.beforeAll(async () => {
    fs.mkdirSync(artifacts, { recursive: true });
    // -busyms 1: the shipped 400 ms threshold is there so ordinary interaction never
    // flashes the pill. A test wants the opposite - see every operation, however short.
    app = await launchPro3d(undefined, undefined, ["-busyms", "1"]);
});

test.afterAll(async () => {
    await app?.stop();
});

type BusyState = { busy: boolean; op?: string; ms?: number };
type Sample = BusyState & { at: number; rtt: number };

/** One GET /busy, with its round-trip time. Rejects on transport failure, so a route that
 *  went down behind the lock fails the test rather than being silently skipped. */
function getBusy(url: string): Promise<Sample> {
    const started = Date.now();
    return new Promise((resolve, reject) => {
        const req = http.get(`${url}busy`, (res) => {
            let body = "";
            res.setEncoding("utf-8");
            res.on("data", (c) => (body += c));
            res.on("end", () => {
                try {
                    resolve({ ...JSON.parse(body), at: Date.now(), rtt: Date.now() - started });
                } catch (e) {
                    reject(new Error(`/busy returned ${res.statusCode}: ${body}`));
                }
            });
        });
        req.on("error", reject);
        req.setTimeout(10_000, () => req.destroy(new Error("/busy timed out after 10s")));
    });
}

/** Polls /busy every `everyMs` until `stop()` is called, keeping every sample. Faster than
 *  the app's own 200 ms, so an operation that blocks for only half a second is still caught
 *  many times over - but not a tight loop, which would put thousands of requests a second
 *  into the very server whose responsiveness is under test. */
function sampleBusy(url: string, everyMs = 20) {
    const samples: Sample[] = [];
    let running = true;
    const loop = (async () => {
        while (running) {
            samples.push(await getBusy(url));
            await new Promise((r) => setTimeout(r, everyMs));
        }
    })();
    return {
        samples,
        stop: async () => {
            running = false;
            await loop;
            return samples;
        },
    };
}

/** max over an array - not `Math.max(...xs)`, which overflows the stack on a long run */
const maxOf = (xs: number[]) => xs.reduce((a, b) => (b > a ? b : a), -Infinity);

/** What the overlay is showing right now, read from the browser - which stays responsive
 *  while the server does not. `null` when the element is not in the page at all. */
async function overlay(page: Page) {
    return page.evaluate(() => {
        const el = document.querySelector(".pro3d-busy") as HTMLElement | null;
        if (!el) return null;
        const text = el.querySelector(".pro3d-busy-text") as HTMLElement | null;
        return {
            display: getComputedStyle(el).display,
            pointerEvents: getComputedStyle(el).pointerEvents,
            text: text?.textContent ?? "",
        };
    });
}

/** Clicks a tool-strip button by the class of its icon (ViewerGUI.ToolStrip.button renders
 *  `div.pro3d-tool > i.<icon> .icon`). Single-shot evaluate, per tests-ui/README.md. */
async function clickTool(render: Page, iconClasses: string[]) {
    const hit = await render.evaluate((classes) => {
        const buttons = Array.from(document.querySelectorAll("div.pro3d-tool"));
        const target = buttons.find((b) => {
            const i = b.querySelector("i");
            return !!i && classes.every((c) => i.classList.contains(c));
        });
        if (!target) return false;
        (target as HTMLElement).click();
        return true;
    }, iconClasses);
    expect(hit, `no tool-strip button with icon ${iconClasses.join(".")}`).toBe(true);
}

test("at rest /busy reports idle and the overlay is hidden and click-through", async ({ browser }) => {
    const page = await browser.newPage();
    await page.goto(app.url);
    await page.waitForSelector(".pro3d-busy", { state: "attached", timeout: 60_000 });

    // The app is only quiet between updates, and animation ticks keep arriving, so this is
    // about the *shape* of the answer, not a single sample: over a second of polling the
    // indicator must settle to idle rather than latch on.
    const s = sampleBusy(app.url);
    await page.waitForTimeout(1000);
    const samples = await s.stop();

    expect(samples.length, "/busy did not answer at all").toBeGreaterThan(5);
    expect(samples.some((x) => x.busy === false), "/busy never reported idle while nothing was going on")
        .toBe(true);

    const o = await overlay(page);
    expect(o, ".pro3d-busy missing from the main page").not.toBeNull();
    expect(o!.display, "the overlay shows itself when the app is idle").toBe("none");
    // The one property that makes this safe to ship: however the overlay ends up, it can
    // never intercept a click meant for the scene or a panel.
    expect(o!.pointerEvents, "the overlay would swallow clicks").toBe("none");

    await page.close();
});

// The escape hatch is the reason this is safe to ship, so it gets a test of its own. It
// also caught a real bug: `startBusyIndicator` returns before injecting its stylesheet when
// the indicator is off, and the overlay used to take its hidden/inert state from that
// stylesheet - so -nobusy left a visible, clickable div in the page's normal flow.
test("-nobusy leaves nothing behind", async ({ browser }) => {
    const off = await launchPro3d(undefined, undefined, ["-nobusy"]);
    try {
        const page = await browser.newPage();
        await page.goto(off.url);
        await page.waitForSelector(".pro3d-busy", { state: "attached", timeout: 60_000 });
        await page.waitForTimeout(2000);

        const o = await overlay(page);
        expect(o!.display, "the overlay is visible with -nobusy").toBe("none");
        expect(o!.pointerEvents, "the overlay can take clicks with -nobusy").toBe("none");
        expect(
            await page.evaluate(() => !!document.getElementById("pro3d-busy-style")),
            "-nobusy still injected the indicator's stylesheet"
        ).toBe(false);

        // the route stays mounted either way - the flag is about the UI, not the server
        expect((await getBusy(off.url)).busy).toBe(false);
        await page.close();
    } finally {
        await off.stop();
    }
});

test("a long operation is reported while it blocks the UI", async ({ browser }) => {
    const main = await browser.newPage();
    await main.goto(app.url);
    await main.waitForSelector(".pro3d-busy", { state: "attached", timeout: 60_000 });

    const render = await browser.newPage();
    await render.goto(`${app.url}?page=render`);
    await render.waitForSelector("img.rendercontrol", { timeout: 120_000 });
    // the centre-ray pick is only slow with a surface to hit, and only cold once
    await surfaceShadersReady(render);
    await render.waitForSelector("div.pro3d-tool", { timeout: 120_000 });
    await render.waitForTimeout(20_000); // let the OPC patches stream in

    // Watch the overlay from the browser at the same time as /busy from node: the two are
    // the two halves of the claim (route answers; page reacts). The browser stays
    // responsive throughout - it is the server that freezes - so this keeps recording.
    await main.evaluate(() => {
        const w = window as any;
        w.__busySeen = [];
        w.__busyWatch = setInterval(() => {
            const el = document.querySelector(".pro3d-busy") as HTMLElement | null;
            if (el && getComputedStyle(el).display !== "none") {
                const s = (el.querySelector(".pro3d-busy-text") as HTMLElement | null)?.textContent ?? "";
                if (s) w.__busySeen.push(s);
            }
        }, 100);
    });

    const s = sampleBusy(app.url);

    // ArcBall: picks the centre ray against every active surface, synchronously, on the
    // update thread. This is the stall the indicator was built for (plans/stallFeedback.md).
    await clickTool(render, ["dot", "circle", "outline", "icon"]);
    await render.waitForTimeout(8_000);

    // ...and an OPC import. This one is here for *label* coverage, not for its duration:
    // the scene already holds this OPC, so the import short-circuits in under 100 ms. The
    // ArcBall switch above is the one that really blocks (~4 s on this data, cold).
    await main.evaluate(`(() => {
      window.aardvark = window.aardvark || {};
      window.aardvark.dialog = { showOpenDialog: () =>
        Promise.resolve({ canceled: false, filePaths: [${JSON.stringify(fixture.opc)}] }) };
    })()`);
    await main.evaluate(() => {
        const item = Array.from(document.querySelectorAll("div.item"))
            .find((e) => (e.textContent ?? "").trim() === "Import OPCs");
        if (item) (item as HTMLElement).click();
    });
    await main.waitForTimeout(20_000);

    const samples = await s.stop();
    const seen: string[] = await main.evaluate(() => {
        const w = window as any;
        clearInterval(w.__busyWatch);
        return w.__busySeen as string[];
    });

    const busy = samples.filter((x) => x.busy);
    const labels = Array.from(new Set(busy.map((x) => x.op)));
    const slowest = maxOf(samples.map((x) => x.rtt));
    const longest = Object.fromEntries(
        labels.map((l) => [l, maxOf(busy.filter((x) => x.op === l).map((x) => x.ms ?? 0))])
    );

    // The interesting numbers, for when this fails or when someone wants to know how long
    // the stalls actually are. `longest` is the blocked time per operation, in ms.
    fs.writeFileSync(
        path.join(artifacts, "busy-samples.json"),
        JSON.stringify({ polls: samples.length, labels, longest, slowestPollMs: slowest, overlay: seen }, null, 2)
    );
    console.log(`[busy] labels=${labels} longest=${JSON.stringify(longest)} slowest poll=${slowest}ms`);

    // 1. the route saw the work, and named it
    expect(busy.length, `/busy never reported an operation; samples=${samples.length}`)
        .toBeGreaterThan(0);
    expect(labels, `the ArcBall switch was not reported; saw ${labels}`).toContain("camera");
    expect(labels, `the OPC import was not reported; saw ${labels}`).toContain("importing surfaces");

    // 2. it answered promptly *throughout* - a route that took app.lock would have blocked
    //    here for as long as the operation did, which is the whole failure mode this design
    //    exists to avoid. The blocked operations themselves are far longer than this bound.
    expect(slowest, `/busy stalled for ${slowest} ms - is it taking app.lock?`).toBeLessThan(2000);
    expect(
        maxOf(Object.values(longest) as number[]),
        "no operation blocked long enough to be worth reporting - the spec is not exercising a stall"
    ).toBeGreaterThan(400);

    // 3. the page reacted while the server was busy
    expect(seen.length, "the overlay never became visible during the long operations").toBeGreaterThan(0);
    expect(seen.join(" | "), "the overlay showed no operation label").toMatch(/[a-z]/);

    // and it cleared again afterwards
    await expect.poll(async () => (await overlay(main))!.display, { timeout: 30_000 }).toBe("none");

    await render.close();
    await main.close();
});
