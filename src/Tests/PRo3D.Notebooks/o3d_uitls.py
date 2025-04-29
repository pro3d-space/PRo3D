import numpy as np
import open3d as o3d


def compute_rigid_transformation_from_correspondences(
    src_points: np.ndarray,
    tgt_points: np.ndarray
) -> np.ndarray:
    """
    Computes a rigid transformation matrix (Helmert) from corresponding source and target points.

    Args:
        src_points (np.ndarray): Nx3 array of source points.
        tgt_points (np.ndarray): Nx3 array of target points.

    Returns:
        np.ndarray: 4x4 transformation matrix.

    Raises:
        ValueError: If the point sets do not have the same shape or are not Nx3.
    """
    if src_points.shape != tgt_points.shape:
        raise ValueError(
            f"Mismatch in number of points: {src_points.shape[0]} source vs {tgt_points.shape[0]} target"
        )

    if src_points.shape[1] != 3:
        raise ValueError(f"Expected Nx3 input arrays, got shape {src_points.shape}")

    # Generate 1-to-1 correspondences
    n = src_points.shape[0]
    correspondences = np.array([[i, i] for i in range(n)], dtype=np.int32)
    correspondences_o3d = o3d.utility.Vector2iVector(correspondences)

    # Convert to Open3D point clouds
    source_pcd = o3d.geometry.PointCloud()
    source_pcd.points = o3d.utility.Vector3dVector(src_points)

    target_pcd = o3d.geometry.PointCloud()
    target_pcd.points = o3d.utility.Vector3dVector(tgt_points)

    # Compute transformation
    estimation = o3d.pipelines.registration.TransformationEstimationPointToPoint()
    transformation_matrix = estimation.compute_transformation(
        target_pcd, source_pcd, correspondences_o3d
    )

    return transformation_matrix

def create_point_cloud_from_array(
    points: np.ndarray,
    show: bool = False
) -> o3d.geometry.PointCloud:
    """
    Creates an Open3D PointCloud object from an Nx3 NumPy array.
    
    Optionally visualizes the result in a window.

    Args:
        points (np.ndarray): Nx3 array of 3D points.
        show (bool): If True, visualize the point cloud immediately.

    Returns:
        o3d.geometry.PointCloud: The created point cloud.

    Raises:
        ValueError: If the input is not a valid Nx3 array.
    """
    if not isinstance(points, np.ndarray):
        raise ValueError("Input must be a NumPy array.")
    if points.ndim != 2 or points.shape[1] != 3:
        raise ValueError(f"Expected shape Nx3, but got {points.shape}")

    pcd = o3d.geometry.PointCloud()
    pcd.points = o3d.utility.Vector3dVector(points)

    if show:
        o3d.visualization.draw_geometries([pcd])

    return pcd

def refine_registration_icp(
    src_pcd: o3d.geometry.PointCloud,
    tgt_pcd: o3d.geometry.PointCloud,
    pre_alignment: np.ndarray,
    voxel_size: float = 0.05,
    max_correspondence_distance: float = 0.075,
    max_iterations: int = 200,
    verbose: bool = True
) -> o3d.pipelines.registration.RegistrationResult:
    """
    Refines an initial rigid alignment using ICP.

    Args:
        src_pcd (o3d.geometry.PointCloud): Source point cloud.
        tgt_pcd (o3d.geometry.PointCloud): Target point cloud.
        pre_alignment (np.ndarray): 4x4 initial transformation matrix.
        voxel_size (float): Voxel size for downsampling.
        max_correspondence_distance (float): ICP correspondence threshold.
        max_iterations (int): Max number of ICP iterations.
        verbose (bool): If True, prints info and results.

    Returns:
        o3d.pipelines.registration.RegistrationResult: Result containing refined transformation.
    """
    # Downsample
    src_down = src_pcd.voxel_down_sample(voxel_size)
    tgt_down = tgt_pcd.voxel_down_sample(voxel_size)

    # Estimate normals (required by many ICP methods)
    src_down.estimate_normals()
    tgt_down.estimate_normals()

    if verbose:
        print("Running ICP fine registration...")
        print(f"Voxel size: {voxel_size}, Max correspondence distance: {max_correspondence_distance}, Iterations: {max_iterations}")
        print("Pre-alignment (Helmert):")
        print(pre_alignment)

    # Run ICP
    result = o3d.pipelines.registration.registration_icp(
        src_down,
        tgt_down,
        max_correspondence_distance,
        pre_alignment,
        o3d.pipelines.registration.TransformationEstimationPointToPoint(),
        o3d.pipelines.registration.ICPConvergenceCriteria(max_iteration=max_iterations)
    )

    if verbose:
        print("\nICP Refinement Result:")
        print(result.transformation)

    return result

import subprocess

def run_m3c2(cloudcompare_path, src_path, tgt_path, param_file):
    subprocess.run([
        cloudcompare_path,        
        "-O", src_path,
        "-O", tgt_path,        
        "-M3C2", param_file
    ], check=True)

def open_cc_with_result(cloudcompare_path: str, result_bin_path: str) -> None:
    subprocess.run([
        cloudcompare_path,
        "-O", result_bin_path
    ])