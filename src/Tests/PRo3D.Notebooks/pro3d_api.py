import requests
import numpy as np
from typing import Any, Optional


class Pro3DClient:
    def __init__(self, host: str = "localhost", port: int = 4321):
        self.base_url = f"http://{host}:{port}/api"

    def get_selected_annotation_points(self, verbose: bool = False) -> Optional[np.ndarray]:
        """
        Fetches the currently selected annotation points.
        
        Args:
            verbose (bool): If True, prints the raw response content.

        Returns:
            A NumPy array of points, or None if not found or invalid.
        """
        url = f"{self.base_url}/annotations/selected/points"
        try:
            response = requests.get(url)
            response.raise_for_status()
            if verbose:
                print("📦 Raw response:", response.text)
            data = response.json()
            if isinstance(data, list) and data:
                return np.array(data)
            else:
                print("⚠️ No points found or invalid format.")
                return None
            
        except requests.HTTPError as e:
            # Attempt to get error message from response body
            message = e.response.text if e.response is not None else str(e)
            print(f"❌ Server responded with error: {e.response.status_code} - {message.strip()}")
            raise

        except requests.RequestException as e:
            print(f"❌ Request failed: {e}")
            return None

    def apply_transformation_to_surface(
            self,
            surface_id: str,
            transformation_matrix: np.ndarray,
            verbose: bool = False
        ) -> bool:
        """
        Applies a transformation matrix to the surface with the given ID using an HTTP PUT request.

        Args:
            surface_id (str): The unique identifier of the surface.
            transformation_matrix (np.ndarray): A 2D NumPy array representing the transformation matrix.
            verbose (bool): If True, prints the raw response content.

        Returns:
            True if the operation succeeded, False otherwise.
        """
        url = f"{self.base_url}/surfaces/{surface_id}/transformation"

        payload: dict[str, Any] = {
            "forward": transformation_matrix.tolist()
        }

        headers: dict[str, str] = {
            "Content-Type": "application/json"
        }

        try:
            response = requests.put(url, json=payload, headers=headers)
            response.raise_for_status()
            print(f"✅ Transformation applied to surface {surface_id}")
            if verbose:
                print("📦 Response:", response.text)
            return True
        
        except requests.HTTPError as e:
            # Attempt to get error message from response body
            message = e.response.text if e.response is not None else str(e)
            print(f"❌ Server responded with error: {e.response.status_code} - {message.strip()}")
            raise

        except requests.RequestException as e:
            print(f"❌ Failed to apply transformation: {e}")
            return False

    def apply_transformation_to_selected_surface(
            self,        
            transformation_matrix: np.ndarray,
            verbose: bool = False
        ) -> bool:
            """
            Applies a transformation matrix to the selected surface using an HTTP PUT request.

            Args:            
                transformation_matrix (np.ndarray): A 2D NumPy array representing the transformation matrix.
                verbose (bool): If True, prints the raw response content.

            Returns:
                True if the operation succeeded, False otherwise.
            """

            return self.apply_transformation_to_surface(
                surface_id="selected",
                transformation_matrix=transformation_matrix,
                verbose=verbose
            )
    
    def query_annotation_as_obj(
            self,
            verbose: bool = False
        ) -> str:
        """
        Sends a query to retrieve cutout geometry from the currently selected annotation as OBJ string.

        Args:
            verbose (bool): If True, prints the raw OBJ response.

        Returns:
            str: The raw OBJ string from the response.

        Raises:
            requests.RequestException: If the request fails.
            ValueError: If the response doesn't look like a valid OBJ.
        """
        url = f"{self.base_url}/queries/queryAnnotationAsObj"

        payload = {
            "queryAttributes": [],
            "distanceToPlane": 100000.0,
            "outputReferenceFrame": "global",
            "outputGeometryType": "mesh",
        }

        try:
            if verbose:
                print("📤 Sending payload:", payload)

            response = requests.post(url, json=payload)
            response.raise_for_status()

            obj_text = response.text.strip()

            if verbose:
                print("📥 Received OBJ data:\n", obj_text[:300], "...\n")

            # Optional: basic sanity check
            if not obj_text.startswith("v ") and not obj_text.startswith("o "):
                raise ValueError("Response does not appear to be in valid OBJ format.")

            return obj_text
        
        except requests.HTTPError as e:
            # Attempt to get error message from response body
            message = e.response.text if e.response is not None else str(e)
            print(f"❌ Server responded with error: {e.response.status_code} - {message.strip()}")
            raise

        except requests.RequestException as e:
            print(f"❌ Request to queryAnnotationAsObj failed: {e}")
            raise
