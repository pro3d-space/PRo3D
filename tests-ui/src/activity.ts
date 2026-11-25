/**
 * GET /activity (docs/Activity.md): what PRo3D is doing right now, for deciding when the next
 * input is safe to send. Use these instead of fixed sleeps:
 *
 *   await untilIdle(app);              // before a click: nothing running, nothing queued
 *   await keyboard.down("Control");
 *   await untilArmed(app, true);       // the keydown has armed the tool
 *   await mouse.click(x, y);
 *
 * `/busy` is the busy pill's source (one slot per kind, labelled work only) and is not meant
 * for this.
 */
import { Pro3d } from "./pro3d";

export type Activity = {
    idle: boolean;
    updating: number;
    queued: number;
    processed: number;
    armed: boolean;
    running: Record<string, number>;
};

export async function activity(app: Pro3d): Promise<Activity> {
    const r = await fetch(new URL("activity", app.url));
    if (!r.ok) throw new Error(`/activity returned ${r.status}`);
    return (await r.json()) as Activity;
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/** Polls `/activity` until `ok` holds `polls` times in a row, `intervalMs` apart. */
async function until(app: Pro3d, what: string, ok: (a: Activity) => boolean,
                     polls: number, intervalMs: number, timeoutMs: number): Promise<Activity> {
    const started = Date.now();
    let inRow = 0;
    let last: Activity | undefined;
    while (true) {
        try {
            last = await activity(app);
            inRow = ok(last) ? inRow + 1 : 0;
        } catch {
            inRow = 0; // a failed poll is not an answer
        }
        if (inRow >= polls) return last!;
        if (Date.now() - started > timeoutMs)
            throw new Error(`PRo3D not ${what} after ${timeoutMs / 1000} s: ${JSON.stringify(last)}`);
        await sleep(intervalMs);
    }
}

/** Nothing running and nothing queued, three polls in a row (100 ms apart): an update that
 *  starts work in the background shows it before it returns, so a gap between two polls
 *  cannot hide a job that is still going. */
export function untilIdle(app: Pro3d, timeoutMs = 180_000) {
    return until(app, "idle", (a) => a.idle, 3, 100, timeoutMs);
}

/** The active tool owns (`true`) or released (`false`) the left button: after a Ctrl
 *  keydown or keyup has been through PRo3D's update. */
export function untilArmed(app: Pro3d, armed: boolean, timeoutMs = 30_000) {
    return until(app, armed ? "armed" : "disarmed", (a) => a.armed === armed, 1, 50, timeoutMs);
}
