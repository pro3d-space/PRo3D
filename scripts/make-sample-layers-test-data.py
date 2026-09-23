#!/usr/bin/env python3
"""Generate the multi-instrument test set for `pro3d-tool sample-layers`.

    python scripts/make-sample-layers-test-data.py --opc <Dimorphos OPC> --out <folder>

Renders simulated observations of Dimorphos by three HERA instruments at the same epochs,
each in the layout its delivered product has, with `.mbi.json` sidecars describing the
exact render camera:

    AFC/     HERA/AFC-1, 1020x1020 8-bit PNG                     (Hera)
    ASPECT/  Milani/ASPECT 2B, 37 single-band float TIFFs 640x512 (Milani)
    HSH/     HERA/HyperScout 1B, one 25-plane float TIFF 409x217 (Hera)

The frames are self-referential, like the AFC-1 projection set: each is rendered from the
shape model and its sidecar is the camera that render used, so projecting it back -- in
the viewer, or through sample-layers -- has nothing to disagree with but the code.

The band values of the ASPECT and HyperScout cubes are the render's I/F times a made-up
spectrum (see docs/Pro3DTool-SimulateImage.md): good for checking that every band lands
where it should, meaningless as spectroscopy.

    kernels     PRO3D_SPICE_KERNELS, or pass --kernel-root
    tool        pro3d-tool on PATH, or pass --tool <path to PRo3D.Tool.exe / pro3d-tool>
"""

import argparse
import os
import subprocess
import sys

INSTRUMENTS = [
    # folder, SPICE frame, file stem, extension, extra args
    ("AFC", "HERA_AFC-1", "AFC1_SIM", ".png", ["--write-mbi", "--gain", "4.5"]),
    ("ASPECT", "MILANI_ASPECT_NIR1", "ASP_SIM", ".tif", ["--product"]),
    ("HSH", "HERA_HSH", "HSH_SIM", ".tif", ["--product"]),
]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--opc", required=True, help="Dimorphos OPC directory")
    ap.add_argument("--out", required=True, help="output folder; AFC/, ASPECT/ and HSH/ are created in it")
    ap.add_argument("--tool", default="pro3d-tool", help="pro3d-tool executable (default: on PATH)")
    ap.add_argument("--date", default="2027-03-21", help="observation date (default 2027-03-21)")
    ap.add_argument("--hours", default="14,17,20,23", help="UTC hours, comma separated (default 14,17,20,23)")
    ap.add_argument("--kernel-root", default=None, help="SPICE kernel tree (default $PRO3D_SPICE_KERNELS)")
    a = ap.parse_args()

    failures = 0
    for hour in [int(h) for h in a.hours.split(",")]:
        time = f"{a.date}T{hour:02d}:00:00Z"
        stamp = a.date.replace("-", "") + f"_{hour:02d}0000"
        for folder, frame, stem, ext, extra in INSTRUMENTS:
            target = os.path.join(a.out, folder)
            os.makedirs(target, exist_ok=True)
            cmd = [a.tool, "simulate-image", "--opc", a.opc, "--time", time, "--instrument", frame,
                   "--out", os.path.join(target, f"{stem}_{stamp}{ext}")] + extra
            if a.kernel_root:
                cmd += ["--kernel-root", a.kernel_root]
            print(" ".join(cmd), flush=True)
            r = subprocess.run(cmd, capture_output=True, text=True)
            for line in r.stdout.splitlines():
                if "[out]" in line or "round trip" in line or "ERROR" in line:
                    print("   ", line.strip())
            if r.returncode != 0:
                print(r.stdout[-2000:], r.stderr[-2000:], file=sys.stderr)
                failures += 1
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
