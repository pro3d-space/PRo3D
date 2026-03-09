# Profile Drawing Tool

Draws a surface profile cross-section as a vector graphic (SVG) or raster image (PNG).

## Requirements

```
pip install matplotlib
```

## Usage

```bash
python draw_profile.py --profile testProfile.csv --curtain-height 30 --min-altitude -1812 --vertical-px 512 --output profile.svg
```

### Parameters

| Parameter | Description |
|---|---|
| `--profile` | Path to CSV file with `distance` and `elevation` columns |
| `--curtain-height` | Total vertical extent of the cross-section in meters |
| `--min-altitude` | Bottom altitude of the view in meters |
| `--vertical-px` | Vertical size in pixels (horizontal is computed from aspect ratio) |
| `--output` | Output path. Use `.svg` for vector graphics, `.png` for raster (default: `profile_output.svg`) |

### CSV format

```csv
"distance","elevation"
"0","-1787.81"
"15.19","-1788.74"
...
```

## Quick test

```bash
python test_draw_profile.py
```

This reads `testProfile.csv`, auto-computes sensible bounds with 5 m margin, and writes `test_profile_output.svg`.
