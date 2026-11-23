# render-view fixtures (#878)

Used by `src/Tests/RenderViewTests.fs`.

- `afc1_20270321T170000.json`: a `pro3d-tool render-view` job written by
  `scripts/render-view-job.py` from the pinned SPICE set (`hera_plan.tm`,
  `v182_20260527_001`). The OPC path was then made relative (`"Dimorphos"`, resolved
  against `$PRO3D_TEST_DATA/HERA/Dimorphos_opc/Dimorphos_DRACO1_DRACO2_Earth`), so the test
  needs no SPICE:

      python scripts/render-view-job.py --opc <Dimorphos OPC> --times 2027-03-21T17:00:00 --untextured --out job.json

  At 17:00, AFC-1 has Dimorphos in view: 2.4° off the boresight, inside the 2.75° half
  field of view. At 14:00 and 20:00 it does not.

- `simulate-image_afc1_20270321T170000.png`: the reference, rendered by the same
  revision's simulate-image with the same kernels:

      pro3d-tool simulate-image --opc <Dimorphos OPC> --body DIMORPHOS --frame DIMORPHOS_FIXED \
          --observer HERA --instrument HERA_AFC-1 --time 2027-03-21T17:00:00Z --out ref.png

  It uses constant albedo, so its non-black pixels are the body's silhouette.

Both the job and simulate-image use AFC-1's 5.5° field of view from the instrument kernel.
The reference was re-rendered when simulate-image's hardcoded 5.5307° was corrected (#801),
so the silhouette test also checks that both draw the body at the same size.
