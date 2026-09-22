"""Test script for draw_profile.py using testProfile.csv."""

from draw_profile import draw_profile, read_profile


def main():
    csv_path = "testProfile.csv"

    distances, elevations = read_profile(csv_path)
    print(f"Loaded {len(distances)} points")
    print(f"  Distance range: {distances[0]:.1f} – {distances[-1]:.1f} m")
    print(f"  Elevation range: {min(elevations):.1f} – {max(elevations):.1f} m")

    min_elev = min(elevations)
    max_elev = max(elevations)
    margin = 5  # meters padding above and below

    min_altitude = min_elev - margin
    curtain_height = (max_elev - min_elev) + 2 * margin
    vertical_px = 512

    print(f"\nRendering with:")
    print(f"  min_altitude   = {min_altitude:.1f} m")
    print(f"  curtain_height = {curtain_height:.1f} m")
    print(f"  vertical_px    = {vertical_px}")

    print("\n--- Normal mode (labels outside) ---")
    draw_profile(csv_path, curtain_height, min_altitude, vertical_px, output_path="test_profile_normal.svg")

    print("\n--- Overlay mode (labels on data, UV = 0-1) ---")
    draw_profile(csv_path, curtain_height, min_altitude, vertical_px, output_path="test_profile_overlay.svg", overlay=True)


if __name__ == "__main__":
    main()
