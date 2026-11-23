namespace PRo3D.MapProjection

open System
open Aardvark.Base

/// Which map the panel shows.
type MapProjectionKind =
    | Equirectangular = 0
    | PolarNorth      = 1
    | PolarSouth      = 2

/// The map projection math in double precision.
///
/// This is the source of truth the shaders (`Shaders.fs`) mirror: tests compare rendered
/// pixels against it, and later the cursor readout and picking invert through it.
///
/// Every axis assumption of the feature lives here and in the matching shader helper:
/// the body-fixed axes are taken as they are (+Z = pole, longitude 0 along +X, east
/// positive, planetocentric), which is what PRo3D's coordinate readout and the LatLon
/// overlay show. For data in DIMORPHOS_SHM the spin pole is -Z, so astronomical north is at
/// the bottom of the map. Whether to rotate is an open decision (#772).
module Projection =

    let pi     = Math.PI
    let twoPi  = 2.0 * Math.PI
    let halfPi = 0.5 * Math.PI

    /// Default polar cutoff: the stereographic map shows its own hemisphere down to the equator.
    let defaultMaxColatitude = halfPi

    /// Wraps an angle into [-pi, pi).
    let wrapPi (x : float) = x - twoPi * floor ((x + pi) / twoPi)

    /// (longitude, latitude, radius) of a body-centred position, planetocentric, radians.
    let lonLatR (p : V3d) =
        let r = p.Length
        if r <= 0.0 then V3d.Zero
        else V3d(atan2 p.Y p.X, asin (clamp -1.0 1.0 (p.Z / r)), r)

    /// +1 for the north polar map, -1 for the south polar map.
    let polarSign (kind : MapProjectionKind) =
        if kind = MapProjectionKind.PolarSouth then -1.0 else 1.0

    /// Angular distance from the map's pole.
    let colatitude (sign : float) (lat : float) = halfPi - sign * lat

    /// Polar stereographic, unit sphere: rho = 2 tan(colatitude / 2). Longitude 0 points down
    /// on the north map and up on the south map (USGS convention), east is counterclockwise
    /// on the north map.
    let polar (sign : float) (lon : float) (lat : float) =
        let rho = 2.0 * tan (0.5 * colatitude sign lat)
        V2d(rho * sin lon, -sign * rho * cos lon)

    let polarInverse (sign : float) (xy : V2d) =
        let rho = xy.Length
        let c = 2.0 * atan (0.5 * rho)
        let lon = if rho = 0.0 then 0.0 else atan2 xy.X (-sign * xy.Y)
        V2d(lon, sign * (halfPi - c))

    /// Map-space position of a (longitude, latitude) pair. Equirectangular map space is
    /// radians; polar map space is the unit-sphere stereographic plane.
    let forward (kind : MapProjectionKind) (lon : float) (lat : float) =
        match kind with
        | MapProjectionKind.Equirectangular -> V2d(lon, lat)
        | _ -> polar (polarSign kind) lon lat

    /// (longitude, latitude) of a map-space position; longitude wrapped to [-pi, pi).
    let inverse (kind : MapProjectionKind) (xy : V2d) =
        match kind with
        | MapProjectionKind.Equirectangular -> V2d(wrapPi xy.X, xy.Y)
        | _ ->
            let ll = polarInverse (polarSign kind) xy
            V2d(wrapPi ll.X, ll.Y)

    /// The map-space rectangle a zoom of 1 fits into the viewport.
    let extent (kind : MapProjectionKind) (maxColatitude : float) =
        match kind with
        | MapProjectionKind.Equirectangular -> Box2d(V2d(-pi, -halfPi), V2d(pi, halfPi))
        | _ ->
            let rho = 2.0 * tan (0.5 * maxColatitude)
            Box2d(V2d(-rho, -rho), V2d(rho, rho))

    /// How a triangle meets the equirectangular map, decided from its longitudes alone.
    type TriangleClass =
        /// Unwrapped, it lies inside [-pi, pi]; drawn once.
        | Regular
        /// Crosses the +-180 degree meridian; drawn once unwrapped plus one copy shifted by 2 pi.
        | Seam
        /// Contains a pole: its longitudes wind once around; drawn as a cap to the pole.
        | Pole

    /// Longitudes of the three corners unwrapped relative to the first, plus the winding
    /// (0 or +-2 pi). Mirrors `Shaders.equirectangular`.
    let unwrap (lonA : float) (lonB : float) (lonC : float) =
        let dab = wrapPi (lonB - lonA)
        let dbc = wrapPi (lonC - lonB)
        let dca = wrapPi (lonA - lonC)
        let xb = lonA + dab
        let xc = xb + dbc
        V3d(lonA, xb, xc), dab + dbc + dca

    let classify (lonA : float) (lonB : float) (lonC : float) =
        let xs, winding = unwrap lonA lonB lonC
        if abs winding > pi then Pole
        elif xs.MaxElement > pi || xs.MinElement < -pi then Seam
        else Regular

    /// Map-space -> NDC for a view centred on `center` (map space) at `zoom` (1 = the whole
    /// extent fits), aspect preserved. Composed in double; the shaders only ever see this
    /// matrix and map-space positions of order 1.
    let viewProj (kind : MapProjectionKind) (maxColatitude : float) (center : V2d) (zoom : float) (viewport : V2i) =
        let e = extent kind maxColatitude
        let size = V2d(float (max 1 viewport.X), float (max 1 viewport.Y))
        let pixelsPerUnit = zoom * min (size.X / e.Size.X) (size.Y / e.Size.Y)
        let scale = V3d(2.0 * pixelsPerUnit / size.X, 2.0 * pixelsPerUnit / size.Y, 1.0)
        Trafo3d.Translation(-center.X, -center.Y, 0.0) * Trafo3d.Scale scale

    /// Map units one pixel covers, for anything drawn at a constant size on screen
    /// (the camera marker, the minimum size of a data footprint).
    let unitsPerPixel (kind : MapProjectionKind) (maxColatitude : float) (zoom : float) (viewport : V2i) =
        let e = extent kind maxColatitude
        let size = V2d(float (max 1 viewport.X), float (max 1 viewport.Y))
        1.0 / (zoom * min (size.X / e.Size.X) (size.Y / e.Size.Y))

    /// Map-space box around body-centred positions -- a bounding box's corners, a camera.
    /// On the equirectangular map the longitudes are unwrapped relative to the first position,
    /// so a footprint sitting on the +-180 degree meridian stays one small box instead of
    /// spanning the whole map. None when there is nothing to bound.
    let mapBoxOf (kind : MapProjectionKind) (positions : seq<V3d>) : Option<Box2d> =
        let mutable box = Box2d.Invalid
        let mutable lon0 = nan
        for p in positions do
            let ll = lonLatR p
            if ll.Z > 0.0 then
                let lon =
                    if kind <> MapProjectionKind.Equirectangular then ll.X
                    elif Double.IsNaN lon0 then
                        lon0 <- ll.X
                        ll.X
                    else lon0 + wrapPi (ll.X - lon0)
                box <- box.ExtendedBy(forward kind lon ll.Y)
        if box.IsInvalid then None else Some box

    /// Centre and zoom that fit `box` into the viewport, using `fill` of the shorter side
    /// (0.8 leaves a margin around the data). Never zooms out past the whole map.
    let fitBox (kind : MapProjectionKind) (maxColatitude : float) (viewport : V2i) (fill : float) (maxZoom : float) (box : Box2d) =
        let e = extent kind maxColatitude
        let size = V2d(float (max 1 viewport.X), float (max 1 viewport.Y))
        let baseScale = min (size.X / e.Size.X) (size.Y / e.Size.Y)
        // a point (or a footprint far below one pixel) has no extent to fit: go to maxZoom
        let needed =
            let s = box.Size
            if s.X <= 0.0 && s.Y <= 0.0 then infinity
            else
                let byX = if s.X > 0.0 then size.X / s.X else infinity
                let byY = if s.Y > 0.0 then size.Y / s.Y else infinity
                min byX byY
        let zoom = clamp 1.0 maxZoom (fill * needed / baseScale)
        box.Center, zoom
    /// Map-space position under a pixel (origin top left, pixel centres at +0.5).
    let pixelToMap (viewProj : Trafo3d) (viewport : V2i) (pixel : V2d) =
        let ndc = V3d(2.0 * pixel.X / float viewport.X - 1.0, 1.0 - 2.0 * pixel.Y / float viewport.Y, 0.0)
        viewProj.Backward.TransformPos(ndc).XY

    /// Depth range the radius is normalised into: outermost surface wins the depth test.
    /// `maxRadius` bounds every vertex of the body (e.g. the bounding-box corner distance).
    let radiusRange (maxRadius : float) = V2d(0.0, max 1e-6 maxRadius * 1.01)
