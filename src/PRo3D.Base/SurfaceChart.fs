namespace PRo3D.Base

open System
open Aardvark.Base
open PRo3D.Base.Gis

/// A 2D chart of a neighbourhood of a surface: the domain in which planar geometry
/// (ellipse construction, triangulation, boolean ops, area) is valid.
///
/// Both directions are partial. Geographic conversion genuinely fails - outside a body's
/// definition, or where SPICE has no data - and the plane charts conform to the same shape so
/// callers never branch on which chart they are holding.
///
/// A chart maps to a *datum* - a plane, or the body at a given altitude - not to the terrain.
/// Landing on the actual surface is a separate raycast step; see
/// EllipticAnnotations.constructAndSampleFromPlane, which does chart inverse then projection.
type SurfaceChart =
    {
        /// Identifies the chart in diagnostics. Not semantic.
        name    : string
        toChart : V3d -> V2d option
        toWorld : V2d -> V3d option
    }

module SurfaceChart =

    let private isFinite (v : float) =
        not (Double.IsNaN v || Double.IsInfinity v)

    /// Latitude beyond which the geographic chart refuses points: longitude degenerates at the
    /// poles, so a ring spanning one cannot be represented.
    let private poleGuardDegrees = 89.9

    /// Chart of a plane: orthogonal projection onto it, and the inverse.
    ///
    /// Assumes a usable plane. Use tryOfPlane for planes read from stored data.
    let ofPlane (plane : Plane3d) : SurfaceChart =
        // cached once - GetWorldToPlane builds a matrix, and charts are applied per point
        let w2p = plane.GetWorldToPlane()
        let p2w = plane.GetPlaneToWorld()
        {
            name    = "plane"
            toChart = fun p -> Some (w2p.TransformPos(p).XY)
            toWorld = fun c -> Some (p2w.TransformPos(V3d(c, 0.0)))
        }

    /// ofPlane, rejecting planes that cannot define a chart.
    ///
    /// DipAndStrikeResults initialises every field to NaN / V3d.NaN (Annotation-Model.fs:237-242)
    /// and that sentinel round-trips through the project format, so a stored dnsResults plane may
    /// be degenerate. Plane3d.Invalid is caught by the same check.
    let tryOfPlane (plane : Plane3d) : SurfaceChart option =
        let n = plane.Normal
        if not (isFinite n.X && isFinite n.Y && isFinite n.Z && isFinite plane.Distance) then None
        elif n.LengthSquared < 1e-12 then None
        else Some (ofPlane plane)

    /// Chart of the plane through origin having up as its normal, with an in-plane basis built
    /// the same way as CrossSection.buildBasisFromUp (CrossSectionClipping.fs:10-24).
    let ofUpVector (up : V3d) (origin : V3d) : SurfaceChart =
        let n = if up.LengthSquared < 1e-30 then V3d.OOI else up.Normalized

        let x =
            let c = Vec.cross n V3d.OOI
            let c = if c.LengthSquared < 1e-12 then Vec.cross n V3d.IOO else c
            c.Normalized

        let y = (Vec.cross n x).Normalized

        {
            name    = "up-vector"
            toChart = fun p ->
                let d = p - origin
                Some (V2d(Vec.dot d x, Vec.dot d y))
            toWorld = fun c ->
                Some (origin + x * c.X + y * c.Y)
        }

    /// Geographic chart: longitude / latitude in degrees, at the base point's altitude.
    ///
    /// Unlike the plane charts this follows the body's curvature, so it does not sag beneath the
    /// datum the way a secant plane does across a large annotation (roughly L^2/8R: ~4 m over
    /// 10 km on Mars, ~370 m over 100 km).
    ///
    /// Longitudes are unwrapped relative to the base point, so a ring crossing the +/-180 deg
    /// meridian stays contiguous in chart space instead of jumping the seam. A ring spanning a
    /// pole has no such repair - longitude is degenerate there - so toChart refuses points within
    /// poleGuardDegrees of one.
    ///
    /// Equirectangular lon/lat is neither conformal nor equal-area. Triangulation topology is
    /// unaffected, but triangle quality degrades at high latitude.
    ///
    /// Returns None when the base point itself cannot be converted.
    let geographic
        (planet          : Planet)
        (referenceSystem : SpiceReferenceSystem option)
        (basePoint       : V3d)
        : SurfaceChart option =

        let toSpherical (p : V3d) =
            match referenceSystem with
            | Some r -> CooTransformation.tryGetLatLonAltPlanet r.body.Value p
            | None   -> CooTransformation.tryGetLatLonAlt planet p

        let fromSpherical (sc : CooTransformation.SphericalCoo) =
            match referenceSystem with
            | Some r -> CooTransformation.tryGetXYZFromLatLonAltPlanet sc r.body.Value
            | None   -> CooTransformation.tryGetXYZFromLatLonAlt sc planet

        match toSpherical basePoint with
        | None -> None
        | Some baseCoo ->
            // keeps a longitude within +/-180 deg of the base longitude, so a ring straddling the
            // antimeridian reads as one contiguous run rather than two clusters 360 deg apart
            let unwrapLongitude (lon : float) =
                let mutable d = lon - baseCoo.longitude
                while d > 180.0 do d <- d - 360.0
                while d < -180.0 do d <- d + 360.0
                baseCoo.longitude + d

            Some {
                name = "geographic"
                toChart = fun p ->
                    match toSpherical p with
                    | Some c when abs c.latitude <= poleGuardDegrees ->
                        Some (V2d(unwrapLongitude c.longitude, c.latitude))
                    | _ -> None
                toWorld = fun c ->
                    // altitude comes from the base point, matching EllipticAnnotations.Conv
                    // (EllipseAnnotation.fs:12-16): the chart is a shell at one height
                    fromSpherical { baseCoo with longitude = c.X; latitude = c.Y }
            }
