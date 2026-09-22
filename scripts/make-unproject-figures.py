#!/usr/bin/env python3
"""Regenerate the two figures on docs/Pro3DTool-Unproject.md.

    python scripts/make-unproject-figures.py <path-to-PRo3D.Resources.TestData>

The overview figure is drawn from a real run of the tool, not mocked up: the script writes a
grid of pixel coordinates, runs `pro3d-tool unproject` over them, and plots what came back. So
it needs the same things a real run needs --

    test data   git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git
    kernels     PRO3D_SPICE_KERNELS, or pass --kernel-root
    python      matplotlib, numpy, pillow

The pixel-convention figure is synthetic and needs only matplotlib.

Writes docs/images/unproject-overview.png and docs/images/unproject-pixel-convention.png.
"""

import argparse
import csv
import os
import subprocess
import sys
import tempfile

import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
IMAGES = os.path.join(ROOT, 'docs', 'images')

IMAGE = 'ASP_000000_270323T060000_2B_NIR1_0.tif'
STEP = 16                      # pixel grid spacing for the sweep

HIT, MISS = '#2ecc71', '#e74c3c'


# ---------------------------------------------------------------------------------------
# overview: a real run of the tool
# ---------------------------------------------------------------------------------------

def run_tool(testdata, kernel_root, workdir):
    """Sweep a grid of pixels through `unproject` and return its output rows."""
    source = os.path.join(testdata, 'HERA', 'Instrument Data', IMAGE)
    if not os.path.exists(source):
        sys.exit('image not found: %s\n(is <testdata> a PRo3D.Resources.TestData clone?)' % source)

    width, height = Image.open(source).size

    grid = os.path.join(workdir, 'grid.csv')
    with open(grid, 'w', newline='\n') as f:
        f.write('image,x,y\n')
        for y in range(STEP // 2, height, STEP):
            for x in range(STEP // 2, width, STEP):
                f.write('%s,%d,%d\n' % (IMAGE, x, y))

    out = os.path.join(workdir, 'grid-out.csv')
    cmd = [
        'dotnet', 'run', '--project', os.path.join(ROOT, 'src', 'PRo3D.Tool', 'PRo3D.Tool.fsproj'),
        '--', 'unproject',
        '--opc', os.path.join(testdata, 'HERA', 'Didymos_ASPECT'),
        '--images', os.path.join(testdata, 'HERA', 'Instrument Data'),
        '--input', grid,
        '--out', out,
    ]
    if kernel_root:
        cmd += ['--kernel-root', kernel_root]

    print('running: pro3d-tool unproject over %d pixels' % (len(range(STEP // 2, width, STEP)) *
                                                            len(range(STEP // 2, height, STEP))))
    result = subprocess.run(cmd, cwd=ROOT)
    if not os.path.exists(out):
        sys.exit('the tool produced no output (exit %d) -- are SPICE kernels available?' % result.returncode)

    with open(out) as f:
        return source, list(csv.DictReader(f))


def draw_overview(source, rows):
    img = np.array(Image.open(source)).astype(float)
    img = np.clip(img / np.percentile(img[img > 0], 99.5), 0, 1) ** 0.7

    ok = [r for r in rows if r['status'] == 'ok']
    miss = [r for r in rows if r['status'] == 'no-hit']

    fig, ax = plt.subplots(1, 2, figsize=(12.6, 5.0), facecolor='white')

    ax[0].imshow(img, cmap='gray', origin='upper', interpolation='nearest')
    ax[0].scatter([float(r['x']) for r in miss], [float(r['y']) for r in miss],
                  s=6, c=MISS, alpha=.55, linewidths=0, label='no-hit (off the limb)')
    ax[0].scatter([float(r['x']) for r in ok], [float(r['y']) for r in ok],
                  s=6, c=HIT, alpha=.9, linewidths=0, label='ok (surface found)')
    ax[0].set_title('input: pixels in the instrument image', fontsize=11)
    ax[0].set_xlabel('x  (0 = left, pixel centres)')
    ax[0].set_ylabel('y  (0 = top row)')
    ax[0].set_xlim(0, img.shape[1])
    ax[0].set_ylim(img.shape[0], 0)
    ax[0].legend(loc='lower right', fontsize=8, framealpha=.85)

    Y = np.array([float(r['y_m']) for r in ok])
    Z = np.array([float(r['z_m']) for r in ok])
    LAT = np.array([float(r['lat_deg']) for r in ok])
    s = ax[1].scatter(Y, Z, c=LAT, s=14, cmap='viridis', linewidths=0)
    ax[1].set_aspect('equal')
    ax[1].grid(alpha=.25, linewidth=.5)
    ax[1].set_title('output: surface points in DIDYMOS_FIXED', fontsize=11)
    ax[1].set_xlabel('y  [m]')
    ax[1].set_ylabel('z  [m]')
    fig.colorbar(s, ax=ax[1], label='lat_deg', shrink=.85)
    ax[1].text(.02, .02, '%d of %d pixels met the surface' % (len(ok), len(rows)),
               transform=ax[1].transAxes, fontsize=8, color='#444')

    fig.suptitle('pro3d-tool unproject - every green pixel becomes one row of body-fixed coordinates',
                 fontsize=12)
    fig.tight_layout(rect=[0, 0, 1, .95])
    path = os.path.join(IMAGES, 'unproject-overview.png')
    fig.savefig(path, dpi=110)
    print('wrote %s (%d hits)' % (path, len(ok)))


# ---------------------------------------------------------------------------------------
# pixel convention: synthetic
# ---------------------------------------------------------------------------------------

def draw_convention():
    W, H = 6, 4
    COL, ROW = 2.35, 1.72          # one physical location, 0-based from left / from top

    fig, axes = plt.subplots(1, 2, figsize=(12.6, 4.9), facecolor='white')

    def draw(ax, title, base, y_down, colour):
        for gx in range(W + 1):
            ax.plot([gx, gx], [0, H], color='#d8d8d8', lw=.8, zorder=1)
        for gy in range(H + 1):
            ax.plot([0, W], [gy, gy], color='#d8d8d8', lw=.8, zorder=1)

        # integers mark pixel CENTRES -- names of pixels, not the only legal values
        for row in range(H):
            for col in range(W):
                ly = (row + base) if y_down else (H - 1 - row + base)
                ax.plot(col + .5, row + .5, '+', ms=5, color='#9aa0a6', mew=1.0, zorder=3)
                ax.text(col + .5, row + .82, '%d,%d' % (col + base, ly), ha='center',
                        va='center', fontsize=7, color='#9aa0a6', zorder=3)

        for col in range(W + 1):
            ax.plot([col, col], [H, H + .12], color='#666', lw=.9)
            ax.text(col, H + .32, '%.1f' % (col - .5 + base), ha='center', va='center',
                    fontsize=7, color='#666')
        ax.text(W / 2., H + .78, 'pixel boundaries', ha='center', fontsize=8, color='#666')

        px = 2
        ax.add_patch(Rectangle((px, 0), 1, H, facecolor='#f2c14e', alpha=.14, zorder=0))
        ax.annotate('', xy=(px, -.45), xytext=(px + 1, -.45),
                    arrowprops=dict(arrowstyle='<->', color='#b8860b', lw=1.1))
        ax.text(px + .5, -.78, 'pixel %d spans %.1f - %.1f'
                % (px + base, px - .5 + base, px + .5 + base),
                ha='center', fontsize=8, color='#b8860b')

        cx, cy = COL + .5, ROW + .5     # both panels draw the same physical grid
        fx = COL + base
        fy = (ROW + base) if y_down else (H - 1 - ROW + base)
        ax.plot([cx, cx], [0, H], ls=':', lw=1.0, color=colour, zorder=4)
        ax.plot([0, W], [cy, cy], ls=':', lw=1.0, color=colour, zorder=4)
        ax.plot(cx, cy, 'o', ms=7, color=colour, zorder=5)
        ax.annotate('a centroid at (%.2f, %.2f)' % (fx, fy), xy=(cx, cy), xytext=(W + .35, cy),
                    fontsize=9, color=colour, va='center', ha='left',
                    arrowprops=dict(arrowstyle='->', color=colour, lw=1.2))

        ax.annotate('', xy=(-.5, H if not y_down else 0), xytext=(-.5, 0 if not y_down else H),
                    arrowprops=dict(arrowstyle='<-', color=colour, lw=1.5))
        ax.text(-.85, H / 2., 'y', fontsize=11, color=colour, va='center', ha='center')

        ax.set_xlim(-1.2, W + 4.0)
        ax.set_ylim(H + 1.05, -1.05)
        ax.set_aspect('equal')
        ax.axis('off')
        ax.set_title(title, fontsize=10.5, color=colour, pad=12)

    draw(axes[0], '--pixel-convention image   (default)\n0-based, origin top-left, y downwards',
         0, True, '#1f77b4')
    draw(axes[1], '--pixel-convention fits\n1-based, origin bottom-left, y upwards',
         1, False, '#c0392b')

    fig.text(.5, .06,
             'Image addressing - all in float, pixel k covers [k-0.5, k+0.5]. '
             'Convention specified by the user.',
             ha='center', fontsize=9.5, color='#444')
    fig.subplots_adjust(top=.80, bottom=.16, left=.02, right=.98)
    path = os.path.join(IMAGES, 'unproject-pixel-convention.png')
    fig.savefig(path, dpi=115)
    print('wrote %s' % path)


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('testdata', nargs='?',
                   help='a clone of PRo3D.Resources.TestData; omit to redraw only the '
                        'convention figure, which needs no data')
    p.add_argument('--kernel-root', default=os.environ.get('PRO3D_SPICE_KERNELS'),
                   help='SPICE kernel tree (default: $PRO3D_SPICE_KERNELS)')
    args = p.parse_args()

    draw_convention()

    if args.testdata:
        with tempfile.TemporaryDirectory() as workdir:
            source, rows = run_tool(args.testdata, args.kernel_root, workdir)
            draw_overview(source, rows)
    else:
        print('no <testdata> given: skipped the overview figure')


if __name__ == '__main__':
    main()
