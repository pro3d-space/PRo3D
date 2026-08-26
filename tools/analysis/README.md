# Analysis scripts

Throwaway-but-checked-in numerical experiments backing decisions recorded in `plans/`.
They need only `numpy`.

| Script | Backs |
| --- | --- |
| `compare_plane_averaging.py` | [plans/coastLines.md](../../plans/coastLines.md) §4 — the three candidate ways to average a set of measured planes, on the cases where they disagree |
| `calibrate_plane_cluster_guard.py` | [plans/coastLines.md](../../plans/coastLines.md) §4.4 — where to put the `S1` / `S2/S1` thresholds that decide whether one mean plane may be drawn at all |

```bash
python3 -m venv .venv && .venv/bin/pip install numpy
.venv/bin/python tools/analysis/compare_plane_averaging.py
```
