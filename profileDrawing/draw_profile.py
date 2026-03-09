import argparse
import csv
import matplotlib.pyplot as plt
import matplotlib.ticker as ticker


def read_profile(csv_path):
    distances = []
    elevations = []
    with open(csv_path, newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            distances.append(float(row["distance"]))
            elevations.append(float(row["elevation"]))
    return distances, elevations


def draw_profile(csv_path, curtain_height, min_altitude, vertical_px, output_path="profile_output.svg", overlay=False, x_interval=None, y_interval=None, x_grid=None, y_grid=None, hide_zero=False):
    distances, elevations = read_profile(csv_path)

    max_altitude = min_altitude + curtain_height
    horizontal_extent = distances[-1] - distances[0]
    vertical_extent = curtain_height

    aspect = horizontal_extent / vertical_extent
    horizontal_px = int(vertical_px * aspect)

    dpi = 100
    fig_w = horizontal_px / dpi
    fig_h = vertical_px / dpi

    fig, ax = plt.subplots(figsize=(fig_w, fig_h), dpi=dpi)

    if overlay:
        # data area fills the entire figure — labels overlay on top
        ax.set_position([0, 0, 1, 1])

    # draw the surface profile line
    ax.plot(distances, elevations, color="saddlebrown", linewidth=2, label="Surface")
    # fill below the surface
    ax.fill_between(distances, min_altitude, elevations, color="burlywood", alpha=0.5)

    ax.set_xlim(distances[0], distances[-1])
    ax.set_ylim(min_altitude, max_altitude)

    # major tick / label intervals
    ax.xaxis.set_major_locator(ticker.MultipleLocator(x_interval) if x_interval else ticker.AutoLocator())
    ax.yaxis.set_major_locator(ticker.MultipleLocator(y_interval) if y_interval else ticker.AutoLocator())

    # grid line intervals (minor grid)
    ax.xaxis.set_minor_locator(ticker.MultipleLocator(x_grid) if x_grid else ticker.AutoMinorLocator())
    ax.yaxis.set_minor_locator(ticker.MultipleLocator(y_grid) if y_grid else ticker.AutoMinorLocator())

    ax.grid(True, which="major", linestyle="--", alpha=0.4)
    ax.grid(True, which="minor", linestyle=":", linewidth=0.5, alpha=0.25)

    if overlay:
        # scale font size to be readable relative to the vertical image size
        label_pt = vertical_px / dpi * 72 * 0.04  # ~4% of image height
        tick_pt = label_pt * 0.8
        box_style = dict(facecolor="black", alpha=0.6, edgecolor="none", pad=3)

        # move ticks inside the plot so they aren't clipped by the SVG viewBox
        # x-ticks: move up from bottom edge
        ax.tick_params(axis="x", colors="white", labelsize=tick_pt, length=0,
                       direction="in", pad=-tick_pt * 3 - 8)
        # y-ticks: push further right to leave room for axis description
        ax.tick_params(axis="y", colors="white", labelsize=tick_pt, length=0,
                       direction="in", pad=-tick_pt * 3 - 14)
        for spine in ax.spines.values():
            spine.set_visible(False)

        # force draw to generate tick labels before styling them
        fig.canvas.draw()

        for label in ax.get_xticklabels():
            label.set_va("top")
            label.set_bbox(box_style)
        ytick_labels = ax.get_yticklabels()
        y_lo, y_hi = ax.get_ylim()
        for label in ytick_labels:
            label.set_ha("left")
            label.set_bbox(box_style)
            # nudge labels near the top/bottom edges so they don't get clipped
            y_val = label.get_position()[1]
            if y_val >= y_hi - (y_hi - y_lo) * 0.1:
                label.set_va("top")
            elif y_val <= y_lo + (y_hi - y_lo) * 0.1:
                label.set_va("bottom")

        # optionally hide the first x-tick (0) since it can get clipped at the left edge
        if hide_zero:
            xticks = ax.get_xticklabels()
            if xticks:
                xticks[0].set_visible(False)

        # "Altitude" vertically centered at the left edge
        ax.text(0.005, 0.5, "Altitude (m)", transform=ax.transAxes,
                ha="left", va="center", rotation=90, fontsize=label_pt, color="white", bbox=box_style)
        # "Distance" at bottom center, below the x-tick labels
        ax.text(0.5, 0.01, "Distance from start (m)", transform=ax.transAxes,
                ha="center", va="bottom", fontsize=label_pt, color="white", bbox=box_style)

        # remove default axis labels (we use text instead)
        ax.set_xlabel("")
        ax.set_ylabel("")
    else:
        ax.set_xlabel("Distance from start (m)")
        ax.set_ylabel("Altitude (m)")
        fig.tight_layout()

    # force a draw so layout finalizes positions
    fig.canvas.draw()

    # get the data area bounding box in figure-relative coords (= UV coords)
    bbox = ax.get_position()
    uv_min_u = bbox.x0
    uv_max_u = bbox.x1
    uv_min_v = bbox.y0
    uv_max_v = bbox.y1

    is_svg = output_path.lower().endswith(".svg")
    fig.savefig(output_path, dpi=dpi, format="svg" if is_svg else None)
    plt.close(fig)

    # patch SVG to prevent viewBox clipping of overlaid labels
    if is_svg and overlay:
        with open(output_path, "r") as f:
            svg = f.read()
        svg = svg.replace("<svg ", '<svg style="overflow:visible" ', 1)
        with open(output_path, "w") as f:
            f.write(svg)

    fmt = "SVG vector" if is_svg else f"{horizontal_px}x{vertical_px} px"
    print(f"Saved profile image to {output_path}  ({fmt})")
    print(f"Data area UV box:")
    print(f"  U: {uv_min_u:.6f} – {uv_max_u:.6f}")
    print(f"  V: {uv_min_v:.6f} – {uv_max_v:.6f}")

    return {"u_min": uv_min_u, "u_max": uv_max_u, "v_min": uv_min_v, "v_max": uv_max_v}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Draw a surface profile cross-section image.")
    parser.add_argument("--profile", required=True, help="Path to profile CSV (distance, elevation)")
    parser.add_argument("--curtain-height", type=float, required=True, help="Total curtain height in meters")
    parser.add_argument("--min-altitude", type=float, required=True, help="Minimum altitude in meters")
    parser.add_argument("--vertical-px", type=int, required=True, help="Vertical image size in pixels")
    parser.add_argument("--output", default="profile_output.svg", help="Output image path (.svg for vector, .png for raster)")
    parser.add_argument("--overlay", action="store_true", help="Overlay labels on data (data fills full image, UV = 0-1)")
    parser.add_argument("--x-interval", type=float, default=None, help="Major tick/label interval for x-axis (meters)")
    parser.add_argument("--y-interval", type=float, default=None, help="Major tick/label interval for y-axis (meters)")
    parser.add_argument("--x-grid", type=float, default=None, help="Minor grid line interval for x-axis (meters)")
    parser.add_argument("--y-grid", type=float, default=None, help="Minor grid line interval for y-axis (meters)")
    parser.add_argument("--hide-zero", action="store_true", help="Hide the first x-tick label (0) to avoid clipping at the left edge")
    args = parser.parse_args()

    draw_profile(args.profile, args.curtain_height, args.min_altitude, args.vertical_px, args.output, args.overlay,
                 args.x_interval, args.y_interval, args.x_grid, args.y_grid, args.hide_zero)
