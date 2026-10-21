import { expect, Page, BrowserContext } from "@playwright/test";
import { PNG } from "pngjs";

/**
 * Server screenshots (/rendering/screenshot) render the scene again at the requested size. The
 * surfaces' offscreen targets must follow it (ViewerUtils.offscreenScene builds them from
 * RenderClientValues): at another aspect ratio a control-sized surface texture would be
 * stretched under correctly projected outlines.
 *
 * Only the viewer's sample count (4, what PRo3D's own screenshot clients send): with another
 * the render server compiles a scene of its own, whose single frame comes out without
 * surfaces - on develop as much as here.
 */
export type Shot = { name: string; w: number; h: number; samples: number };

export function shotsFor(W: number, H: number): Shot[] {
    return [
        { name: "wide", w: 2 * W, h: H, samples: 4 },
        { name: "large", w: Math.round(1.5 * W), h: Math.round(1.5 * H), samples: 4 },
    ];
}

/**
 * Fraction of pixels in the live-frame rectangle whose colour differs between the live frame
 * and the server shot `s`, sampled where the live pixel lands in it: the standard render
 * control config keeps the horizontal field of view (Frustum.withAspect), so the shot is the
 * live view scaled by w / W about the centre.
 */
export function mismatch(live: PNG, shot: PNG, x0: number, y0: number, x1: number, y1: number, threshold = 60) {
    const k = shot.width / live.width;
    let changed = 0;
    let total = 0;
    for (let y = y0; y < y1; y++)
        for (let x = x0; x < x1; x++) {
            const sx = Math.round(shot.width / 2 + (x + 0.5 - live.width / 2) * k - 0.5);
            const sy = Math.round(shot.height / 2 + (y + 0.5 - live.height / 2) * k - 0.5);
            const i = (y * live.width + x) * 4;
            const j = (sy * shot.width + sx) * 4;
            const d =
                Math.abs(live.data[i] - shot.data[j]) + Math.abs(live.data[i + 1] - shot.data[j + 1]) + Math.abs(live.data[i + 2] - shot.data[j + 2]);
            if (d > threshold) changed++;
            total++;
        }
    return changed / total;
}

/**
 * The render page's client, as the render server names it: its control's element id.
 *
 * Not looked up in /rendering/stats.json: requesting it while the page renders stopped the
 * live view from updating (aardvark.media 5.7.3), on this branch and before it.
 */
export async function renderClient(render: Page) {
    return render.evaluate(() => document.querySelector("img.rendercontrol")?.closest("[id]")?.id ?? "");
}

/** The size of the render control on the page, in CSS pixels. */
export async function controlSize(render: Page) {
    return render.evaluate(() => {
        const rc = document.querySelector("img.rendercontrol")!.getBoundingClientRect();
        return { width: Math.round(rc.width), height: Math.round(rc.height) };
    });
}

export async function serverShot(context: BrowserContext, app: { url: string }, client: string, s: Shot, extra = "") {
    const res = await context.request.get(
        new URL(`/rendering/screenshot/${client}?w=${s.w}&h=${s.h}&samples=${s.samples}&fmt=png${extra}`, app.url).href
    );
    expect(res.ok(), `screenshot ${s.name}: ${res.status()}`).toBe(true);
    return Buffer.from(await res.body());
}
