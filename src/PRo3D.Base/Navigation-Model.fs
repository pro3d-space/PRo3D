namespace PRo3D.Base

open FSharp.Data.Adaptive
open Adaptify

open Aardvark.Base
open Aardvark.UI.Primitives

type NavigationMode =
    | FreeFly = 0
    | ArcBall = 1
    | MapView = 2

/// One of the three unsigned axes of the navigation gizmo (a whole edge, e.g. -N..+N).
/// Locking one constrains mouse navigation to a pure rotation about that world axis.
type NavigationAxis =
    | NorthSouth
    | EastWest
    | UpDown

[<ModelType>]
type NavigationModel = {
    camera         : CameraControllerState
    navigationMode : NavigationMode
    exploreCenter  : V3d
    updatePerFrame : bool
    /// Gizmo axis lock (transient, never persisted). While `Some`, navigation may
    /// only rotate the camera about this axis; cleared on mode change, gizmo circle
    /// click, re-clicking the edge, and scene/bookmark load.
    lockedAxis     : Option<NavigationAxis>
}
