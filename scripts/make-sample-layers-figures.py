#!/usr/bin/env python3
"""Regenerate the figures and CSV excerpts on docs/Pro3DTool-SampleLayers.md.

    python scripts/make-sample-layers-figures.py <path-to-PRo3D.Resources.TestData> [--tool <pro3d-tool>]

Drawn from a real run, not mocked up: the script runs `pro3d-tool sample-layers` over the
simulated AFC / ASPECT / HyperScout set in HERA/Dimorphos_opc/SampleLayers_2027-03-21 (made
by scripts/make-sample-layers-test-data.py) and plots what came back. Pass --from <dir> to
plot an existing output directory instead of running the tool.

    test data   git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
    kernels     PRO3D_SPICE_KERNELS, or pass --kernel-root
    python      matplotlib, numpy, pandas, pillow, tifffile

Writes docs/images/sample-layers-*.png and docs/examples/sample-layers/*.
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile

import numpy as np
import pandas as pd
import tifffile
from PIL import Image
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.join(HERE, '..', 'docs')
EPOCH = '20270321_200000'


def run_tool(tool, testdata, out, kernel_root):
    opc = os.path.join(testdata, 'HERA', 'Dimorphos_opc', 'Dimorphos')
    data = os.path.join(testdata, 'HERA', 'Dimorphos_opc', 'SampleLayers_2027-03-21')
    cmd = [tool, 'sample-layers', '--opc', opc, '--images',
           os.path.join(data, 'AFC'), os.path.join(data, 'ASPECT'), os.path.join(data, 'HSH'), '--out', out]
    if kernel_root:
        cmd += ['--kernel-root', kernel_root]
    print(' '.join(cmd), flush=True)
    subprocess.run(cmd, check=True)
    return data


def crop_to_body(img, margin=0.25):
    ys, xs = np.nonzero(img > 0)
    if len(xs) == 0:
        return img, (0, 0)
    h = max(ys.max() - ys.min(), xs.max() - xs.min())
    m = int(h * margin) + 2
    y0, y1 = max(0, ys.min() - m), min(img.shape[0], ys.max() + m + 1)
    x0, x1 = max(0, xs.min() - m), min(img.shape[1], xs.max() + m + 1)
    return img[y0:y1, x0:x1], (x0, y0)


def figure_inputs(data, path):
    afc = np.asarray(Image.open(os.path.join(data, 'AFC', f'AFC1_SIM_{EPOCH}.png')).convert('L'), float)
    asp = tifffile.imread(os.path.join(data, 'ASPECT', f'ASP_SIM_{EPOCH}_NIR1_0.tif'))
    hsh = tifffile.imread(os.path.join(data, 'HSH', f'HSH_SIM_{EPOCH}_Stacked.tif'))
    panels = [
        ('HERA / AFC-1', afc, '1020 x 1020, 1 band, 8-bit PNG'),
        ('Milani / ASPECT', asp, '640 x 512, 37 bands, one float TIFF each\n(shown: NIR1_0, 875 nm)'),
        ('HERA / HyperScout', hsh[0], '409 x 217, 25 bands in one float TIFF\n(shown: plane 0, 661 nm)'),
    ]
    fig, axes = plt.subplots(1, 3, figsize=(12, 4.4))
    for ax, (title, img, sub) in zip(axes, panels):
        c, _ = crop_to_body(img)
        ax.imshow(c, cmap='gray', interpolation='nearest')
        ax.set_title(f'{title}\n{sub}', fontsize=9)
        ax.set_xticks([]); ax.set_yticks([])
        ax.text(0.02, 0.02, f'{c.shape[1]} x {c.shape[0]} px shown', transform=ax.transAxes,
                color='w', fontsize=8, va='bottom')
    fig.suptitle('Dimorphos, 2027-03-21 20:00 UTC -- the same body, three instruments, three pixel grids '
                 '(cropped to the body)', fontsize=10)
    fig.tight_layout(rect=(0, 0, 1, 0.93))
    fig.savefig(path, dpi=110)
    plt.close(fig)


def view_basis(direction):
    d = direction / np.linalg.norm(direction)
    up = np.array([0.0, 0.0, 1.0])
    if abs(d @ up) > 0.95:
        up = np.array([0.0, 1.0, 0.0])
    r = np.cross(up, d); r /= np.linalg.norm(r)
    u = np.cross(d, r)
    return d, r, u


def draw_cloud(ax, P, values, direction, cmap, label, stride=4):
    """Orthographic view of the vertices from `direction`, painter-sorted, near hemisphere only."""
    d, r, u = view_basis(direction)
    depth = P @ d
    keep = np.arange(len(P))[::stride]
    keep = keep[depth[keep] > np.percentile(depth, 35)]
    keep = keep[np.argsort(depth[keep])]
    x, y = P[keep] @ r, P[keep] @ u
    v = values[keep]
    seen = np.isfinite(v)
    ax.scatter(x[~seen], y[~seen], s=0.6, c='#d0d0d0', linewidths=0, rasterized=True)
    sc = ax.scatter(x[seen], y[seen], s=0.6, c=v[seen], cmap=cmap, linewidths=0, rasterized=True)
    ax.set_aspect('equal'); ax.set_xticks([]); ax.set_yticks([])
    ax.set_title(label, fontsize=9)
    return sc


def figure_assembled(out, path):
    V = pd.read_csv(os.path.join(out, 'vertices.csv'))
    P = V[['x', 'y', 'z']].values
    n = len(P)

    def column(name, col):
        T = pd.read_csv(os.path.join(out, 'images', f'{name}.csv'), usecols=['id', col])
        a = np.full(n, np.nan)
        a[T.id.values] = T[col].values
        return a

    afc = column(f'AFC1_SIM_{EPOCH}', f'AFC1_SIM_{EPOCH}')
    asp = column(f'ASP_SIM_{EPOCH}', 'NIR1_0')
    hsh = column(f'HSH_SIM_{EPOCH}', 'Stacked_0')
    slope = pd.read_csv(os.path.join(out, 'attributes', 'Slope.csv')).Slope.values
    # look from where the AFC frame was taken: the mean direction of what it saw
    direction = np.nanmean(P[np.isfinite(afc)], axis=0)

    fig, axes = plt.subplots(1, 4, figsize=(14, 4.2))
    for ax, vals, cmap, label in [
        (axes[0], afc, 'gray', 'AFC-1 (DN)'),
        (axes[1], asp, 'gray', 'ASPECT NIR1_0 (I/F)'),
        (axes[2], hsh, 'gray', 'HyperScout plane 0 (I/F)'),
        (axes[3], slope, 'viridis', 'OPC attribute: Slope (deg)'),
    ]:
        sc = draw_cloud(ax, P, vals, direction, cmap, label)
        fig.colorbar(sc, ax=ax, fraction=0.046, pad=0.02)
    fig.suptitle('The same vertices, 20:00 UTC: each instrument sampled at its nearest pixel '
                 '(grey: not seen by that image). HyperScout\'s 8.6 m pixels show as blocks.', fontsize=10)
    fig.tight_layout(rect=(0, 0, 1, 0.93))
    fig.savefig(path, dpi=110)
    plt.close(fig)


def figure_coverage(out, manifest, path):
    V = pd.read_csv(os.path.join(out, 'vertices.csv'))
    P = V[['x', 'y', 'z']].values
    count = np.zeros(len(P))
    for img in manifest['images']:
        ids = pd.read_csv(os.path.join(out, img['csv']), usecols=['id']).id.values
        count[ids] += 1
    fig, axes = plt.subplots(1, 2, figsize=(9, 4.2))
    for ax, direction, label in [(axes[0], np.array([1.0, 0.3, 0.2]), 'from +X'),
                                 (axes[1], np.array([-1.0, -0.3, 0.2]), 'from -X')]:
        sc = draw_cloud(ax, P, np.where(count > 0, count, np.nan), direction, 'magma', label)
    fig.colorbar(sc, ax=axes, fraction=0.03, pad=0.02, label=f'observations seeing the vertex (of {len(manifest["images"])})')
    fig.suptitle('Coverage of the combined dataset: 4 epochs x 3 instruments', fontsize=10)
    fig.savefig(path, dpi=110, bbox_inches='tight')
    plt.close(fig)
    return count


def figure_spectrum(out, manifest, path):
    by_name = {img['name']: img for img in manifest['images']}
    asp_img, hsh_img = by_name[f'ASP_SIM_{EPOCH}'], by_name[f'HSH_SIM_{EPOCH}']
    A = pd.read_csv(os.path.join(out, asp_img['csv']))
    H = pd.read_csv(os.path.join(out, hsh_img['csv']))
    J = A.merge(H, on='id', suffixes=('_asp', '_hsh'))
    J = J[(J.incidence_deg_asp < 40) & (J.emission_deg_asp < 40)]
    row = J.iloc[len(J) // 2]
    fig, ax = plt.subplots(figsize=(7, 3.6))
    for img, marker, label in [(asp_img, 'o', 'ASPECT (Milani, 37 bands)'), (hsh_img, 's', 'HyperScout (Hera, 25 bands)')]:
        wl = [b['wavelengthNm'] for b in img['bands']]
        suffix = '_asp' if img is asp_img else '_hsh'
        vals = [row[b['column'] + ('' if b['column'] + suffix not in row.index else suffix)] for b in img['bands']]
        ax.plot(wl, vals, marker=marker, ms=3.5, lw=1, label=label)
    ax.set_xlabel('wavelength (nm)'); ax.set_ylabel('I/F at the nearest pixel')
    ax.set_title(f'One surface point, vertex {int(row.id)}: every band of both instruments, from one CSV row each\n'
                 '(the spectrum is the made-up one simulate-image applies; the two differ by viewing geometry)', fontsize=9)
    ax.legend(fontsize=8)
    fig.tight_layout()
    fig.savefig(path, dpi=110)
    plt.close(fig)
    return int(row.id)


def excerpts(out, target, vertex_id):
    os.makedirs(target, exist_ok=True)

    def head(src, dst, rows=6, ids=None):
        T = pd.read_csv(src, dtype=str, keep_default_na=False)
        if ids is not None:
            T = T[T.id.isin([str(i) for i in ids])]
        T.head(rows).to_csv(dst, index=False, lineterminator='\n')

    head(os.path.join(out, 'vertices.csv'), os.path.join(target, 'vertices.csv'))
    head(os.path.join(out, 'attributes', 'Slope.csv'), os.path.join(target, 'Slope.csv'))
    # the same few vertices across all three instruments, so the excerpts line up by id
    A = pd.read_csv(os.path.join(out, 'images', f'AFC1_SIM_{EPOCH}.csv'), usecols=['id'])
    S = pd.read_csv(os.path.join(out, 'images', f'ASP_SIM_{EPOCH}.csv'), usecols=['id'])
    H = pd.read_csv(os.path.join(out, 'images', f'HSH_SIM_{EPOCH}.csv'), usecols=['id'])
    common = np.intersect1d(np.intersect1d(A.id, S.id), H.id)
    ids = [vertex_id] + list(common[:: max(1, len(common) // 4)][:4])
    for name in [f'AFC1_SIM_{EPOCH}', f'ASP_SIM_{EPOCH}', f'HSH_SIM_{EPOCH}']:
        head(os.path.join(out, 'images', f'{name}.csv'), os.path.join(target, f'{name}.csv'), rows=10, ids=ids)
    # the manifest of the same three images, without this machine's absolute paths
    m = json.load(open(os.path.join(out, 'manifest.json')))
    m['opc'] = '<PRo3D.Resources.TestData>/HERA/Dimorphos_opc/Dimorphos'
    m['kernel'] = '<kernel-root>/mk/' + os.path.basename(m['kernel'])
    m['images'] = [img for img in m['images'] if img['name'].endswith(EPOCH)]
    for img in m['images']:
        folder = os.path.basename(os.path.dirname(img['sidecar']))
        img['sidecar'] = f'<PRo3D.Resources.TestData>/HERA/Dimorphos_opc/SampleLayers_2027-03-21/{folder}/{os.path.basename(img["sidecar"])}'
    with open(os.path.join(target, 'manifest.json'), 'w', newline='\n') as f:
        json.dump(m, f, indent=2)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('testdata', help='PRo3D.Resources.TestData checkout')
    ap.add_argument('--tool', default='pro3d-tool')
    ap.add_argument('--from', dest='existing', default=None, help='plot this sample-layers output instead of running the tool')
    ap.add_argument('--kernel-root', default=None)
    a = ap.parse_args()

    data = os.path.join(a.testdata, 'HERA', 'Dimorphos_opc', 'SampleLayers_2027-03-21')
    out = a.existing or tempfile.mkdtemp(prefix='sample-layers-')
    if not a.existing:
        run_tool(a.tool, a.testdata, out, a.kernel_root)
    manifest = json.load(open(os.path.join(out, 'manifest.json')))

    images = os.path.join(DOCS, 'images')
    figure_inputs(data, os.path.join(images, 'sample-layers-inputs.png'))
    figure_assembled(out, os.path.join(images, 'sample-layers-assembled.png'))
    figure_coverage(out, manifest, os.path.join(images, 'sample-layers-coverage.png'))
    vid = figure_spectrum(out, manifest, os.path.join(images, 'sample-layers-spectrum.png'))
    excerpts(out, os.path.join(DOCS, 'examples', 'sample-layers'), vid)
    print('figures in', os.path.abspath(images))
    return 0


if __name__ == '__main__':
    sys.exit(main())
