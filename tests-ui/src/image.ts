import { PNG } from "pngjs";

export interface Diff {
    /// fraction of pixels whose max channel delta exceeds the epsilon
    changedFraction: number;
    /// mean absolute delta over all channels/pixels, 0..255
    meanDelta: number;
}

/** fraction of clearly-lit pixels in the CENTRAL region of the render view --
 *  a proxy for "the 3D content has actually rendered" (OPC streaming, effect
 *  re-preparation and first-run shader compilation can take a while before
 *  anything appears). Central region only: the false-color legend (left edge)
 *  and the HUD text (top-left) would otherwise count as content. */
export function litFraction(buf: Buffer, threshold = 70): number {
    const png = PNG.sync.read(buf);
    let lit = 0,
        n = 0;
    const x0 = Math.floor(png.width * 0.3),
        x1 = Math.floor(png.width * 0.9);
    const y0 = Math.floor(png.height * 0.3),
        y1 = Math.floor(png.height * 0.95);
    for (let y = y0; y < y1; y++) {
        for (let x = x0; x < x1; x++) {
            const o = (y * png.width + x) * 4;
            n++;
            if (
                png.data[o] > threshold ||
                png.data[o + 1] > threshold ||
                png.data[o + 2] > threshold
            )
                lit++;
        }
    }
    return lit / n;
}

/** whether the server-side render stream shows live scene content rather than
 *  the AARDVARK loading splash: the splash background is pure black, the
 *  viewer clears to dark gray (#2A2A2A) -- corner pixels tell them apart */
export function streamLive(buf: Buffer): boolean {
    const png = PNG.sync.read(buf);
    const at = (x: number, y: number) => {
        const o = (y * png.width + x) * 4;
        return (png.data[o] + png.data[o + 1] + png.data[o + 2]) / 3;
    };
    const m = 8;
    const corners = [
        at(m, png.height - m),
        at(png.width - m, png.height - m),
        at(png.width - m, Math.floor(png.height / 2)),
    ];
    return corners.every((c) => c > 15);
}

export function diffPng(a: Buffer, b: Buffer, epsilon = 12): Diff {
    const pa = PNG.sync.read(a);
    const pb = PNG.sync.read(b);
    if (pa.width !== pb.width || pa.height !== pb.height)
        throw new Error(
            `size mismatch: ${pa.width}x${pa.height} vs ${pb.width}x${pb.height}`
        );
    let changed = 0;
    let sum = 0;
    const n = pa.width * pa.height;
    for (let i = 0; i < n; i++) {
        const o = i * 4;
        const dr = Math.abs(pa.data[o] - pb.data[o]);
        const dg = Math.abs(pa.data[o + 1] - pb.data[o + 1]);
        const db = Math.abs(pa.data[o + 2] - pb.data[o + 2]);
        sum += dr + dg + db;
        if (Math.max(dr, dg, db) > epsilon) changed++;
    }
    return { changedFraction: changed / n, meanDelta: sum / (n * 3) };
}
