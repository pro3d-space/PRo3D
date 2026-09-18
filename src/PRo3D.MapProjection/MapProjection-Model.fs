namespace PRo3D.MapProjection

open Aardvark.Base
open Adaptify

/// State of the map projection panel (#772). Session-only: lives on the viewer's `Model`,
/// not on the scene, so nothing is persisted.
[<ModelType>]
type MapProjectionModel =
    {
        kind     : MapProjectionKind
        /// map-space point in the middle of the panel
        center   : V2d
        /// 1 = the whole map fits the panel
        zoom     : float
        /// panel size in pixels, for converting pointer motion into map space
        viewport : V2i
        /// last pointer position (pixels) while dragging
        dragFrom : Option<V2d>
    }

type MapProjectionAction =
    | SetKind   of MapProjectionKind
    /// pointer position and panel size, in pixels
    | DragStart of at : V2d * size : V2d
    | DragMove  of at : V2d * size : V2d
    | DragEnd
    /// wheel steps (positive zooms in) at a pointer position, with the panel size (pixels)
    | Zoom      of steps : float * at : V2d * size : V2d
    | ResetView
