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
        /// centre on the 3D view's cursor (its last preview-pick hit) instead of `center`. The
        /// preview pick only runs while picking (Ctrl held, or Direct Tool Mode with a tool armed)
        /// and PRo3D keeps the last hit, so during plain navigation the map stays on it.
        follow   : bool
    }

type MapProjectionAction =
    | SetKind   of MapProjectionKind
    /// pointer position and panel size, in pixels, with the centre the drag starts from -- which
    /// is the followed centre while `follow` is on, so grabbing the map takes it over from there
    | DragStart of at : V2d * size : V2d * centre : V2d
    | DragMove  of at : V2d * size : V2d
    | DragEnd
    /// wheel steps (positive zooms in) at a pointer position, with the panel size (pixels)
    | Zoom      of steps : float * at : V2d * size : V2d
    /// centre and zoom on a map-space box: *Zoom to data* (the box comes from the surfaces)
    | FitTo     of box : Box2d
    /// follow the 3D cursor, or stop and keep the centre the map is showing
    | SetFollow of on : bool * centre : V2d
    | ResetView
