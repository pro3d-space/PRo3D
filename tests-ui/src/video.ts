import type { CDPSession, Page } from "@playwright/test";
import { spawnSync } from "child_process";
import * as fs from "fs";
import * as path from "path";

/**
 * Screen videos of a spec, with a visible pointer and captions (docs/dev/StoryVideo.md).
 *
 * Frames come from Chromium's screencast (CDP), not Playwright's `recordVideo`: its fixed
 * ~1 Mbit/s VP8 blurs a 1080p UI, and the screencast hands over each frame with its time, so
 * waits can be cut out while writing the frame list. ffmpeg then only encodes.
 *
 * The pointer is drawn into the page: headless Chromium draws no cursor, and mouse events
 * inside the panels' iframes never reach the top document, so it cannot follow the mouse by
 * itself - every move goes through `move`.
 */
export class Video {
    private frames: { t: number; file: string }[] = [];
    private cuts: [number, number][] = [];
    private start = Number.POSITIVE_INFINITY;
    private cdp?: CDPSession;
    private seq = 0;
    private at = { x: 0, y: 0 };
    private lastCut = 0;

    constructor(readonly page: Page, readonly dir: string) {}

    async record() {
        fs.rmSync(this.dir, { recursive: true, force: true });
        fs.mkdirSync(this.dir, { recursive: true });
        const vp = this.page.viewportSize() ?? { width: 1920, height: 1080 };
        this.cdp = await this.page.context().newCDPSession(this.page);
        this.cdp.on("Page.screencastFrame", (f: any) => {
            const file = path.join(this.dir, `f${String(this.seq++).padStart(6, "0")}.jpg`);
            fs.writeFileSync(file, Buffer.from(f.data, "base64"));
            this.frames.push({ t: f.metadata.timestamp ?? Date.now() / 1000, file });
            this.cdp?.send("Page.screencastFrameAck", { sessionId: f.sessionId }).catch(() => {});
        });
        await this.cdp.send("Page.startScreencast", { format: "jpeg", quality: 92, maxWidth: vp.width, maxHeight: vp.height });
    }

    /** The video starts here; everything before (loading) is dropped. */
    begin() {
        this.start = Date.now() / 1000;
    }

    /** Runs `wait` and cuts all of it from the video but its first `keep` seconds - nothing of
     *  it when it directly follows another cut wait. */
    async cut<T>(wait: () => Promise<T>, keep = 0.35): Promise<T> {
        const t0 = Date.now() / 1000;
        const r = await wait();
        const t1 = Date.now() / 1000;
        const k = t0 - this.lastCut < 0.1 ? 0 : keep;
        if (t1 - t0 > k + 0.1) {
            this.cuts.push([t0 + k, t1]);
            this.lastCut = t1;
        }
        return r;
    }

    /** Pointer, caption and key elements, idempotent (the app may replace the body). */
    private async overlay() {
        await this.page.evaluate(() => {
            if (document.getElementById("vid-pointer")) return;
            const root = document.documentElement;
            const style = document.createElement("style");
            style.textContent = `
#vid-pointer { position: fixed; left: 0; top: 0; z-index: 2147483647; pointer-events: none;
  width: 26px; height: 26px; transition-property: transform; transition-timing-function: ease-in-out; }
.vid-ripple { position: fixed; z-index: 2147483646; pointer-events: none; width: 44px; height: 44px;
  margin: -22px 0 0 -22px; border-radius: 50%; border: 3px solid #ffd54f; animation: vid-ripple .6s ease-out forwards; }
@keyframes vid-ripple { from { transform: scale(.2); opacity: 1 } to { transform: scale(1.4); opacity: 0 } }
#vid-caption { position: fixed; z-index: 2147483645; pointer-events: none; transform: translateX(-50%);
  max-width: 60%; padding: 12px 22px; border-radius: 8px; background: rgba(12,12,12,.82); color: #f2f2f2;
  font: 500 24px/1.35 "Segoe UI", sans-serif; text-align: center; transition: opacity .3s; opacity: 0; }
#vid-keys { position: fixed; z-index: 2147483645; pointer-events: none; padding: 6px 14px; border-radius: 6px;
  background: rgba(255,255,255,.92); color: #111; font: 600 20px "Segoe UI", sans-serif; opacity: 0; transition: opacity .15s; }`;
            root.appendChild(style);
            const p = document.createElement("div");
            p.id = "vid-pointer";
            p.innerHTML = `<svg width="26" height="26" viewBox="0 0 26 26"><path d="M2 2 L2 21 L7.5 16 L11 24 L14.5 22.5 L11 15 L18.5 15 Z"
                fill="#fff" stroke="#000" stroke-width="1.6" stroke-linejoin="round"/></svg>`;
            root.appendChild(p);
            for (const id of ["vid-caption", "vid-keys"]) {
                const d = document.createElement("div");
                d.id = id;
                root.appendChild(d);
            }
        });
    }

    /** Glides the pointer to (x, y) and moves the real mouse along. */
    async move(x: number, y: number) {
        await this.overlay();
        const d = Math.hypot(x - this.at.x, y - this.at.y);
        const ms = d < 1 ? 0 : Math.round(Math.min(600, Math.max(200, d * 0.6)));
        await this.page.evaluate(({ x, y, ms }) => {
            const p = document.getElementById("vid-pointer")!;
            p.style.transitionDuration = `${ms}ms`;
            p.style.transform = `translate(${x - 2}px, ${y - 2}px)`;
        }, { x, y, ms });
        await this.page.mouse.move(x, y, { steps: Math.max(1, Math.round(ms / 40)) });
        await this.page.waitForTimeout(ms);
        this.at = { x, y };
    }

    async ripple() {
        await this.page.evaluate(({ x, y }) => {
            const r = document.createElement("div");
            r.className = "vid-ripple";
            r.style.left = `${x}px`;
            r.style.top = `${y}px`;
            document.documentElement.appendChild(r);
            setTimeout(() => r.remove(), 700);
        }, this.at);
    }

    /** A caption centred at the bottom of `box` (the render view); `null` hides it. Holds `hold` s. */
    async caption(text: string | null, box: { x: number; y: number; width: number; height: number }, hold = 2.5) {
        await this.overlay();
        await this.page.evaluate(({ text, x, y }) => {
            const c = document.getElementById("vid-caption")!;
            if (text) c.textContent = text;
            c.style.left = `${x}px`;
            c.style.top = `${y}px`;
            c.style.opacity = text ? "1" : "0";
        }, { text, x: box.x + box.width / 2, y: box.y + box.height - 200 });
        if (text) await this.page.waitForTimeout(hold * 1000);
    }

    /** A key badge in the render view's top-left corner; `null` hides it. */
    async keys(label: string | null, box: { x: number; y: number }) {
        await this.overlay();
        await this.page.evaluate(({ label, x, y }) => {
            const k = document.getElementById("vid-keys")!;
            if (label) k.textContent = label;
            k.style.left = `${x}px`;
            k.style.top = `${y}px`;
            k.style.opacity = label ? "1" : "0";
        }, { label, x: box.x + 16, y: box.y + 16 });
    }

    /** Stops recording and encodes `out` (MP4, H.264) with ffmpeg ($FFMPEG or `ffmpeg` on PATH). */
    async encode(out: string, fps = 30) {
        await this.cdp?.send("Page.stopScreencast").catch(() => {});
        await this.page.waitForTimeout(500);
        const end = Date.now() / 1000;
        // kept intervals: [start, end] minus the cuts
        const cuts = this.cuts.filter(([a, b]) => b > this.start).sort((p, q) => p[0] - q[0]);
        const kept: [number, number][] = [];
        let from = this.start;
        for (const [a, b] of cuts) {
            if (a > from) kept.push([from, a]);
            from = Math.max(from, b);
        }
        if (end > from) kept.push([from, end]);
        // each kept interval shows the frame current at its start, then the frames inside it
        const list: string[] = [];
        let last: string | undefined;
        let i = 0;
        for (const [a, b] of kept) {
            while (i < this.frames.length && this.frames[i].t <= a) last = this.frames[i++].file;
            let shown = last;
            let since = a;
            while (i < this.frames.length && this.frames[i].t < b) {
                const f = this.frames[i++];
                if (shown) list.push(`file '${shown.replace(/\\/g, "/")}'`, `duration ${(f.t - since).toFixed(4)}`);
                shown = f.file;
                since = f.t;
            }
            if (shown) list.push(`file '${shown.replace(/\\/g, "/")}'`, `duration ${(b - since).toFixed(4)}`);
            last = shown;
        }
        // the concat demuxer ignores the last entry's duration unless the file is repeated
        if (last) list.push(`file '${last.replace(/\\/g, "/")}'`);
        const listFile = path.join(this.dir, "frames.txt");
        fs.writeFileSync(listFile, list.join("\n"));
        const r = spawnSync(process.env.FFMPEG ?? "ffmpeg", [
            "-y", "-f", "concat", "-safe", "0", "-i", listFile,
            "-vf", `fps=${fps},scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p`,
            "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-movflags", "+faststart", out,
        ], { encoding: "utf-8" });
        if (r.status !== 0) throw new Error(`ffmpeg failed (${r.error ?? r.status}): ${(r.stderr ?? "").slice(-2000)}`);
        try {
            fs.rmSync(this.dir, { recursive: true, force: true });
        } catch (e) {
            console.log(`[video] frames left in ${this.dir}: ${e}`);
        }
    }
}
