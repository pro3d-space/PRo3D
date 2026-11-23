import { PNG } from "pngjs";

/**
 * Reading an ellipse annotation back out of a render: the drawn outline, found by colour,
 * fitted with an ellipse. Shared by ellipse-annotation.spec.ts (is the drape an ellipse?) and
 * ellipse-editing.spec.ts (did an edit move the axes it should?).
 */

/** The manual's figure is a close-up, and the fit below wants the outline without the
 *  coloured tool-strip icons down the right edge, so both work on a cut-out. */
export function crop(png: Buffer, x: number, y: number, w: number, h: number): Buffer {
    const src = PNG.sync.read(png);
    const x0 = Math.max(0, Math.min(Math.round(x), src.width - 1));
    const y0 = Math.max(0, Math.min(Math.round(y), src.height - 1));
    const width = Math.min(Math.round(w), src.width - x0);
    const height = Math.min(Math.round(h), src.height - y0);
    const out = new PNG({ width, height });
    PNG.bitblt(src, out, x0, y0, width, height, 0, 0);
    return PNG.sync.write(out);
}

export interface EllipseFit {
    /** outline pixels found */
    pixels: number;
    /** centroid of the outline pixels, in the coordinates of the image handed in */
    cx: number;
    cy: number;
    /** angular sectors around the centre with no outline in them: gaps in the curve */
    emptyBins: number;
    /** semi-axes of the fitted ellipse, in pixels, and their ratio */
    a: number;
    b: number;
    ratio: number;
    /** |p·u² + q·v² − 1| over the thinned curve: 0 is an ellipse */
    mean: number;
    p95: number;
}

export const BINS = 180;

/**
 * Reads the drawn annotation back out of a render screenshot and measures how elliptical
 * it is.
 *
 * The curve is found by colour -- annotations take the active group's default, which is
 * red, over a greyscale body -- then thinned to one point per angular sector (the drawn
 * line is a band several pixels wide, and its thickness would otherwise dominate the
 * residual). Semi-axes come from a least-squares fit of `p·u² + q·v² = 1` in the frame of
 * the curve's principal axes, which is linear in (p, q), and the residual of that same
 * expression is what gets reported.
 *
 * A planar ellipse stays an ellipse under perspective, so a correct drape scores near
 * zero; the residual grows with how far the draped curve departs from a plane section.
 */
export function fitOutline(png: Buffer): EllipseFit | null {
    const img = PNG.sync.read(png);
    const pts: Array<[number, number]> = [];
    for (let y = 0; y < img.height; y++)
        for (let x = 0; x < img.width; x++) {
            const i = (img.width * y + x) * 4;
            const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
            if (r > 60 && r - g > 30 && r - b > 30) pts.push([x, y]);
        }
    if (pts.length < 100) return null;

    const n = pts.length;
    const cx = pts.reduce((s, p) => s + p[0], 0) / n;
    const cy = pts.reduce((s, p) => s + p[1], 0) / n;
    let sxx = 0, sxy = 0, syy = 0;
    for (const [x, y] of pts) {
        const dx = x - cx, dy = y - cy;
        sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
    }
    const th = 0.5 * Math.atan2((2 * sxy) / n, (sxx - syy) / n);
    const ct = Math.cos(th), st = Math.sin(th);
    const uv = pts.map(([x, y]): [number, number] => {
        const dx = x - cx, dy = y - cy;
        return [dx * ct + dy * st, -dx * st + dy * ct];
    });

    const bins: Array<Array<[number, number]>> = Array.from({ length: BINS }, () => []);
    for (const [u, v] of uv) {
        const ang = Math.atan2(v, u);
        const k = Math.floor(((ang + Math.PI) / (2 * Math.PI)) * BINS) % BINS;
        bins[k].push([Math.hypot(u, v), ang]);
    }
    const curve: Array<[number, number]> = [];
    for (const bin of bins) {
        if (!bin.length) continue;
        const radii = bin.map((x) => x[0]).sort((x, y) => x - y);
        const r = radii[Math.floor(radii.length / 2)];
        const ang = bin.reduce((s, x) => s + x[1], 0) / bin.length;
        curve.push([r * Math.cos(ang), r * Math.sin(ang)]);
    }

    let a11 = 0, a12 = 0, a22 = 0, r1 = 0, r2 = 0;
    for (const [u, v] of curve) {
        const u2 = u * u, v2 = v * v;
        a11 += u2 * u2; a12 += u2 * v2; a22 += v2 * v2;
        r1 += u2; r2 += v2;
    }
    const det = a11 * a22 - a12 * a12;
    const p = (r1 * a22 - r2 * a12) / det;
    const q = (a11 * r2 - a12 * r1) / det;
    const residuals = curve
        .map(([u, v]) => Math.abs(p * u * u + q * v * v - 1))
        .sort((x, y) => x - y);
    const a = 1 / Math.sqrt(p), b = 1 / Math.sqrt(q);

    return {
        pixels: n,
        cx: +cx.toFixed(1),
        cy: +cy.toFixed(1),
        emptyBins: bins.filter((x) => x.length === 0).length,
        a: +a.toFixed(1),
        b: +b.toFixed(1),
        ratio: +(a / b).toFixed(2),
        mean: +(residuals.reduce((x, y) => x + y, 0) / residuals.length).toFixed(4),
        p95: +residuals[Math.floor(0.95 * residuals.length)].toFixed(4),
    };
}
