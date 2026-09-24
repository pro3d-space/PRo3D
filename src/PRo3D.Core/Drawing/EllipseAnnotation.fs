namespace PRo3D.Core.Drawing

open System

open Aardvark.Base
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

module EllipticAnnotations =
    let sampleNumber = 50
    /// sample count used when the ellipse is constructed on the fitted plane (world space)
    let planeSampleNumber = 200

    module Conv = 
        let geographicalToCartesian (v : CooTransformation.SphericalCoo) =
            V2d(v.longitude, v.latitude)
        let cartesianToGeographical (basePosition : CooTransformation.SphericalCoo) (v : V2d) : CooTransformation.SphericalCoo = 
            { basePosition with
                CooTransformation.SphericalCoo.longitude = v.X
                CooTransformation.SphericalCoo.latitude = v.Y
            }
    let createProjectedEllipse (projectToSurface : V3d -> Option<V3d>) (planet : Planet) (geographical : CooTransformation.SphericalCoo) (points : V2d[]) =
        points
        |> Array.choose (fun latLon ->
            let geo = Conv.cartesianToGeographical geographical latLon
            match CooTransformation.tryGetXYZFromLatLonAlt geo planet with
            | None ->
                Log.warn "[EllipseAnnotation] could not convert lat/lon to xyz for ellipse point."
                None
            | Some position ->
                match projectToSurface position with
                | None ->
                    Log.warn "could not reproject ellipse point."
                    None
                | Some p -> Some p
        )

    type ConstructedEllipse = 
        {
            constructionPlane : Plane3d
            ellipseOnPlane : Ellipse2d
            /// second half-ellipse of an asymmetric (four point) ellipse. it shares the major
            /// axis with ellipseOnPlane and only differs in its semi-minor axis.
            ellipseOnPlaneAssym : Option<Ellipse2d>
            surfaceProjectedEllipsePoints : array<V3d>
        }

    module ConstructedEllipse =
        let createGeographicalEllipse (planet : Planet) (referenceSystem: Option<PRo3D.Base.Gis.SpiceReferenceSystem>) (c : ConstructedEllipse) : Ellipse2d option =
            let coord (p : V3d) : V2d option =
                let sphericalOpt =
                    match referenceSystem with
                    | Some r -> CooTransformation.tryGetLatLonAltOfBody r.body.Value p
                    | None   -> CooTransformation.tryGetLatLonAlt planet p
                sphericalOpt |> Option.map Conv.geographicalToCartesian

            let transform (planePoint : V2d) =
                c.constructionPlane.GetPlaneToWorld().TransformPos(V3d(planePoint, 0.0)) |> coord

            match transform c.ellipseOnPlane.Center,
                  transform (c.ellipseOnPlane.Center + c.ellipseOnPlane.Axis0),
                  transform (c.ellipseOnPlane.Center + c.ellipseOnPlane.Axis1) with
            | Some center, Some ax0, Some ax1 ->
                Some (Ellipse2d(center, ax0 - center, ax1 - center))
            | _ ->
                Log.warn "[EllipseAnnotation] could not construct geographical ellipse."
                None

    /// The metric shape stored with an ellipse annotation (`EllipticAnnotationResult`),
    /// computed once when the ellipse is constructed, so that the export and colour by
    /// category only read stored values.
    module Measures =

        /// Direction of `axis` projected into the local horizontal, in degrees clockwise
        /// from `north`, folded into [0, 180): an axis has no head, so 10 and 190 are the
        /// same direction. NaN when the axis is (near) parallel to `up`.
        let axialAzimuth (up : V3d) (north : V3d) (axis : V3d) =
            let u = up.Normalized
            let horizontal = axis - u * Vec.dot axis u
            let n = (north - u * Vec.dot north u).Normalized
            let e = Vec.cross n u
            if axis.Length = 0.0 || horizontal.Length < 1e-9 * axis.Length || n.AnyNaN then
                Double.NaN
            else
                let azimuth = atan2 (Vec.dot horizontal e) (Vec.dot horizontal n) * Constant.DegreesPerRadian
                let folded = azimuth % 180.0
                let folded = if folded < 0.0 then folded + 180.0 else folded
                // an axis due north comes out a hair below 180 as often as a hair above
                // 0; both are north, and [0, 180) says which one to write
                if 180.0 - folded < 1e-6 then 0.0 else folded

        /// Local up and north at `p`. On a body both are re-derived there, like
        /// `ReferenceSystemApp.updateCoordSystemAt` does, because the reference system's
        /// own vectors belong to its origin, not to the ellipse. Flat frames keep the given ones.
        let localFrame (planet : Planet) (up : V3d) (north : V3d) (p : V3d) =
            match planet with
            | Planet.None | Planet.JPL | Planet.ENU -> up, north
            | _ ->
                let localUp = ReferenceSystemApp.upVector p planet
                localUp, ReferenceSystemApp.northVector localUp

        /// A result from a centre and two perpendicular semi-axes (world space, metres),
        /// in any order: the longer one becomes the major axis. `up` and `north` are the
        /// local frame at `center`. A circle (axes equal to a millionth) has no long axis,
        /// so no azimuth either.
        let ofAxes (up : V3d) (north : V3d) (center : V3d) (axis0 : V3d) (axis1 : V3d) : EllipticAnnotationResult =
            let major, minor =
                if axis1.Length > axis0.Length then axis1, axis0 else axis0, axis1
            {
                geographicalEllipse      = None
                geographicalEllipseAssym = None
                center                   = center
                semiMajorAxis            = major
                semiMinorAxis            = minor
                majorAxisAzimuth         =
                    if major.Length - minor.Length <= 1e-6 * major.Length then Double.NaN
                    else axialAzimuth up north major
            }

        /// The result for an ellipse constructed on its fitted plane.
        ///
        /// A four-point ellipse is two half-ellipses sharing the clicked axis, each with
        /// its own semi-minor on its own side. It is stored as the symmetric ellipse with
        /// the same extent: the minor semi-axis is half the full width across the clicked
        /// axis, and the centre sits in the middle of that width.
        let ofConstructed (planet : Planet) (up : V3d) (north : V3d) (c : ConstructedEllipse) =
            let toWorld = c.constructionPlane.GetPlaneToWorld()
            let e = c.ellipseOnPlane

            let centerOnPlane, minorOnPlane =
                match c.ellipseOnPlaneAssym with
                | None -> e.Center, e.Axis1
                | Some other ->
                    // both halves share the clicked axis, so their minor axes are parallel
                    let d =
                        if e.Axis1.Length > 0.0 then e.Axis1.Normalized
                        else V2d(-e.Axis0.Y, e.Axis0.X).Normalized
                    let s0 = Vec.dot e.Axis1 d
                    let s1 = Vec.dot other.Axis1 d
                    let lo = min 0.0 (min s0 s1)
                    let hi = max 0.0 (max s0 s1)
                    e.Center + d * ((hi + lo) * 0.5), d * ((hi - lo) * 0.5)

            let center = toWorld.TransformPos(V3d(centerOnPlane, 0.0))
            let axis0  = toWorld.TransformDir(V3d(e.Axis0, 0.0))
            let axis1  = toWorld.TransformDir(V3d(minorOnPlane, 0.0))
            let up, north = localFrame planet up north center
            ofAxes up north center axis0 axis1


    let constructAndSampleFromPlane (fittedPlane : Plane3d) (points : array<V3d>) (projectToSurface : V3d -> Option<V3d>) = 
        let w2Plane = fittedPlane.GetWorldToPlane()
        let plane2World = fittedPlane.GetPlaneToWorld()

        let projectOntoSurface (planePoints : array<V2d>) =
            planePoints
            |> Array.choose (fun planePoint ->
                let position = plane2World.TransformPos(V3d(planePoint, 0.0))
                match projectToSurface position with
                | None ->
                    Log.warn "could not reproject ellipse point."
                    None
                | Some p ->
                    Some p
            )

        match points |> Array.map (fun p -> w2Plane.TransformPos(p).XY) with
        | [| plane0; plane1; plane2 |] ->
            let ellipse = EllipseConstruction.constructEllipseOrtho2d plane0 plane1 plane2
            let sampledPoints = EllipseConstruction.computeEllipsePoints ellipse planeSampleNumber
            Some {
                constructionPlane = fittedPlane
                ellipseOnPlane = ellipse
                ellipseOnPlaneAssym = None
                surfaceProjectedEllipsePoints = projectOntoSurface sampledPoints
            }
        // four point (asymmetric) ellipse: plane0/plane1 are the ends of the major axis,
        // plane2 and plane3 give the semi-minor length on either side of it.
        | [| plane0; plane1; plane2; plane3 |] ->
            let ellipse0 = EllipseConstruction.constructEllipseOrtho2d plane0 plane1 plane2
            let ellipse1 = EllipseConstruction.constructEllipseOrtho2d plane0 plane1 plane3
            let sampledPoints =
                EllipseConstruction.constructAssimmetricalEllipse2dPoints ellipse0 ellipse1 planeSampleNumber
            Some {
                constructionPlane = fittedPlane
                ellipseOnPlane = ellipse0
                ellipseOnPlaneAssym = Some ellipse1
                surfaceProjectedEllipsePoints = projectOntoSurface sampledPoints
            }
        | _ ->
            None

    let constructAndSampleGeographical (planet : Planet) (referenceSystem: Option<PRo3D.Base.Gis.SpiceReferenceSystem>) (points : array<V3d>) (projectToSurface : V3d -> Option<V3d>) =
        let geographicalPointsOpt =
            points
            |> Array.map (fun p ->
                let sphericalOpt =
                    match referenceSystem with
                    | Some r -> CooTransformation.tryGetLatLonAltOfBody r.body.Value p
                    | None   -> CooTransformation.tryGetLatLonAlt planet p
                sphericalOpt |> Option.map (fun sc -> sc, Conv.geographicalToCartesian sc))

        if geographicalPointsOpt |> Array.exists Option.isNone then
            Log.warn "[EllipseAnnotation] could not compute geographical coordinates for ellipse sample."
            None
        else

        let geographicalPoints = geographicalPointsOpt |> Array.choose id
        match geographicalPoints with
        | [| (geographical, p0); (_,p1); (_,p2); (_,p3) |] ->
            let ellipse1 = EllipseConstruction.constructEllipseOrtho2d p0 p1 p2
            let ellipse2 = EllipseConstruction.constructEllipseOrtho2d p0 p1 p3
            let sampledEllipse = EllipseConstruction.constructAssimmetricalEllipse2dPoints ellipse1 ellipse2 sampleNumber
                        
            let projectedEllipse = createProjectedEllipse projectToSurface planet geographical sampledEllipse

            Some([ ellipse1; ellipse2 ], projectedEllipse)
                

        | [| (geographical, p0); (_,p1); (_,p2) |] -> 
            Log.line "ellipse points: %A" geographicalPoints
            let ellipse = EllipseConstruction.constructEllipseOrtho2d p0 p1 p2
            let sampledEllipse = EllipseConstruction.computeEllipsePoints ellipse sampleNumber
            let projectedEllipse = createProjectedEllipse projectToSurface planet geographical sampledEllipse
                
            Some ([ ellipse ], projectedEllipse)
        | _ -> 
            None

