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

export interface Coverage {
    /// number of pixels the baseline counts as body
    bodyPixels: number;
    /// of those, the fraction whose colour the projection changed
    coveredFraction: number;
    /// fraction of the NON-body pixels the projection changed -- a projector
    /// that lands beside the body paints space, and that must stay ~0
    spilledFraction: number;
}

/**
 * How much of the body the projection actually landed on.
 *
 * `baseline` is the render with nothing projected, `projected` the same view
 * with the image in the stack. Pixels the baseline shows as surface (anything
 * that is not the clear colour) are "body"; a projection that is geometrically
 * right repaints nearly all of them and leaves the surrounding space alone. A
 * projector pointing somewhere else changes almost nothing -- which is the
 * failure this measures, and it is insensitive to how the projected image
 * happens to be exposed or coloured.
 *
 * Deliberately not a pixel-exact comparison against the source image: the
 * viewer looks at the body from the projector's axis but neither at the
 * instrument's standoff nor through its frustum, so the two framings differ by
 * a scale no screenshot comparison should have to model.
 */
export function bodyCoverage(
    baseline: Buffer,
    projected: Buffer,
    backgroundTolerance = 10,
    epsilon = 12
): Coverage {
    const pa = PNG.sync.read(baseline);
    const pb = PNG.sync.read(projected);
    if (pa.width !== pb.width || pa.height !== pb.height)
        throw new Error(
            `size mismatch: ${pa.width}x${pa.height} vs ${pb.width}x${pb.height}`
        );

    // central region only: the false-color legend on the left edge and the HUD
    // text top-left are neither body nor space, and both change on their own
    const x0 = Math.floor(pa.width * 0.3),
        x1 = Math.floor(pa.width * 0.95);
    const y0 = Math.floor(pa.height * 0.15),
        y1 = Math.floor(pa.height * 0.95);

    // "body" is anything that is not the viewer's clear colour, NOT anything
    // bright: the DRACO mosaic is hemispheric, so the unobserved cap renders
    // pure black while empty space is the clear colour (#2A2A2A). A brightness
    // threshold puts that cap on the space side and then reports every pixel
    // the projection legitimately paints there as spill.
    const bg = (() => {
        let r = 0, g = 0, b = 0, n = 0;
        for (let y = y1 - 12; y < y1 - 4; y++)
            for (let x = x1 - 12; x < x1 - 4; x++) {
                const o = (y * pa.width + x) * 4;
                r += pa.data[o]; g += pa.data[o + 1]; b += pa.data[o + 2]; n++;
            }
        return [r / n, g / n, b / n];
    })();

    let body = 0,
        covered = 0,
        space = 0,
        spilled = 0;
    for (let y = y0; y < y1; y++) {
        for (let x = x0; x < x1; x++) {
            const o = (y * pa.width + x) * 4;
            const isBody =
                Math.max(
                    Math.abs(pa.data[o] - bg[0]),
                    Math.abs(pa.data[o + 1] - bg[1]),
                    Math.abs(pa.data[o + 2] - bg[2])
                ) > backgroundTolerance;
            const changed =
                Math.max(
                    Math.abs(pa.data[o] - pb.data[o]),
                    Math.abs(pa.data[o + 1] - pb.data[o + 1]),
                    Math.abs(pa.data[o + 2] - pb.data[o + 2])
                ) > epsilon;
            if (isBody) {
                body++;
                if (changed) covered++;
            } else {
                space++;
                if (changed) spilled++;
            }
        }
    }
    return {
        bodyPixels: body,
        coveredFraction: body === 0 ? 0 : covered / body,
        spilledFraction: space === 0 ? 0 : spilled / space,
    };
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
