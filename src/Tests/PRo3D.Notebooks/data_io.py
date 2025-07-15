import subprocess
from pathlib import Path
from typing import Any

def write_pcd_obj(output_file: str, pcd: Any) -> None:
    """
    Writes a point cloud to a .obj file in vertex format.

    Args:
        output_file (str): Path to the output OBJ file.
        pcd (Any): A point cloud object with a `.points` attribute (Nx3).
    """
    with open(output_file, "w") as f:
        for point in pcd.points:
            f.write(f"v {point[0]} {point[1]} {point[2]}\n")
    print(f"✅ Wrote OBJ to {output_file}")

def open_file_in_meshlab(file_path: str) -> None:
    """
    Opens a file in MeshLab.

    Args:
        file_path (str): Path to the file to open in MeshLab.
    """
    try:
        subprocess.Popen([
            r"C:\Program Files\vcg\MeshLab\meshlab.exe", file_path
        ])
        print("✅ MeshLab started successfully.")
    except FileNotFoundError:
        print("❌ MeshLab executable not found. Please ensure it's installed.")
