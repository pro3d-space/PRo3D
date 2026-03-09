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


def draw_profile(csv_path, curtain_height, min_altitude, vertical_px, output_path="profile_output.svg"):
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

    # draw the surface profile line
    ax.plot(distances, elevations, color="saddlebrown", linewidth=2, label="Surface")
    # fill below the surface
    ax.fill_between(distances, min_altitude, elevations, color="burlywood", alpha=0.5)

    ax.set_xlim(distances[0], distances[-1])
    ax.set_ylim(min_altitude, max_altitude)

    ax.set_xlabel("Distance from start (m)")
    ax.set_ylabel("Altitude (m)")
    ax.xaxis.set_major_locator(ticker.AutoLocator())
    ax.yaxis.set_major_locator(ticker.AutoLocator())
    ax.grid(True, linestyle="--", alpha=0.4)

    fig.tight_layout()

    # force a draw so tight_layout finalizes positions
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

    fmt = "SVG vector" if is_svg else f"{horizontal_px}x{vertical_px} px"
    print(f"Saved profile image to {output_path}  ({fmt})")
    print(f"Data area UV box:")
    print(f"  U: {uv_min_u:.6f} – {uv_max_u:.6f}")
    print(f"  V: {uv_min_v:.6f} – {uv_max_v:.6f}")
    print(f"  (bottom-left = ({uv_min_u:.6f}, {uv_min_v:.6f}), top-right = ({uv_max_u:.6f}, {uv_max_v:.6f}))")

    return {"u_min": uv_min_u, "u_max": uv_max_u, "v_min": uv_min_v, "v_max": uv_max_v}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Draw a surface profile cross-section image.")
    parser.add_argument("--profile", required=True, help="Path to profile CSV (distance, elevation)")
    parser.add_argument("--curtain-height", type=float, required=True, help="Total curtain height in meters")
    parser.add_argument("--min-altitude", type=float, required=True, help="Minimum altitude in meters")
    parser.add_argument("--vertical-px", type=int, required=True, help="Vertical image size in pixels")
    parser.add_argument("--output", default="profile_output.svg", help="Output image path (.svg for vector, .png for raster)")
    args = parser.parse_args()

    draw_profile(args.profile, args.curtain_height, args.min_altitude, args.vertical_px, args.output)
