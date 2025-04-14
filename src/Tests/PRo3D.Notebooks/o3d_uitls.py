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
