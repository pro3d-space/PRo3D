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

With overlay mode (data fills full image, labels drawn on top — ideal for use as a 3D texture where UV = 0-1):

```bash
python draw_profile.py --profile testProfile.csv --curtain-height 30 --min-altitude -1812 --vertical-px 512 --overlay --x-interval 100 --y-interval 5 --x-grid 25 --y-grid 1
```

### Parameters

| Parameter | Description |
|---|---|
| `--profile` | Path to CSV file with `distance` and `elevation` columns |
| `--curtain-height` | Total vertical extent of the cross-section in meters |
| `--min-altitude` | Bottom altitude of the view in meters |
| `--vertical-px` | Vertical size in pixels (horizontal is computed from aspect ratio) |
| `--output` | Output path. `.svg` for vector graphics, `.png` for raster (default: `profile_output.svg`) |
| `--overlay` | Overlay labels on data so the plot fills the full image (UV = 0-1) |
| `--x-interval` | Optional. Major tick/label interval for x-axis in meters |
| `--y-interval` | Optional. Major tick/label interval for y-axis in meters |
| `--x-grid` | Optional. Minor grid line interval for x-axis in meters |
| `--y-grid` | Optional. Minor grid line interval for y-axis in meters |

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

This reads `testProfile.csv`, auto-computes sensible bounds with 5 m margin, and writes both `test_profile_normal.svg` (labels outside) and `test_profile_overlay.svg` (labels overlaid).
