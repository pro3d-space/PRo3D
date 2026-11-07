// GET /activity (docs/Activity.md): what PRo3D is doing right now, for automation deciding when
// the next input is safe to send. Checked against the cases it exists for: the Ctrl keydown that
// arms a tool, and the cold KdTree load a first pick triggers.
import { test, expect } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { fixture } from "../src/pro3d";
import { clickTool, drawingScene, openDrawingViewer, tool } from "../src/drawing";
import { activity, untilArmed, untilIdle } from "../src/activity";

const artifacts = path.join(__dirname, "..", "artifacts", "activity");

test.setTimeout(15 * 60_000);

test("/activity: idle, armed by Ctrl, and a cold pick seen while it runs", async ({ browser }) => {
    test.skip(!fs.existsSync(fixture.sceneTemplate), "set PRO3D_TEST_DATA");
    fs.mkdirSync(artifacts, { recursive: true });
    const scene = drawingScene(artifacts, "activity");
    const { app, context, render } = await openDrawingViewer(browser, scene, "activity-0.png",
        (n, png) => fs.writeFileSync(path.join(artifacts, n), png));
    try {
        const first = await untilIdle(app);
        expect(first.armed, "no tool armed at start").toBe(false);
        expect(first.running).toEqual({});

        await clickTool(render, "Draw annotation");
        await expect.poll(async () => (await tool(render, "Draw annotation")).active, { timeout: 30_000 }).toBe(true);
        await untilIdle(app);
        const before = (await activity(app)).processed;

        // Ctrl arms the tool - once the keydown has been through update, not when it is sent
        const vp = render.viewportSize() ?? { width: 1600, height: 900 };
        const centre = { x: vp.width / 2, y: vp.height / 2 };
        await render.mouse.move(centre.x, centre.y);
        await render.waitForTimeout(300); // mouseenter focuses the render control
        await render.keyboard.down("Control");
        const armed = await untilArmed(app, true);
        expect(armed.processed, "the keydown went through update").toBeGreaterThan(before);

        // a first pick on a cold patch loads its KdTree; sample while it runs
        const seen = new Set<string>();
        let sampling = true;
        const sampler = (async () => {
            while (sampling) {
                for (const k of Object.keys((await activity(app)).running)) seen.add(k);
                await new Promise((r) => setTimeout(r, 20));
            }
        })();
        await render.mouse.click(centre.x, centre.y);
        await render.keyboard.up("Control");
        await untilArmed(app, false);
        const settled = await untilIdle(app);
        sampling = false;
        await sampler;
        console.log(`[activity] seen while picking: ${[...seen].join(", ") || "(nothing)"}`);
        expect([...seen].some((k) => k === "kdtree" || k === "picking"),
            "the cold pick showed as running work").toBe(true);
        expect(settled.running, "nothing left running").toEqual({});
        expect(settled.queued).toBe(0);
    } finally {
        await context.close();
        await app.stop();
    }
});
