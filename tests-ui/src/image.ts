import { PNG } from "pngjs";

export interface Diff {
    /// fraction of pixels whose max channel delta exceeds the epsilon
    changedFraction: number;
    /// mean absolute delta over all channels/pixels, 0..255
    meanDelta: number;
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
