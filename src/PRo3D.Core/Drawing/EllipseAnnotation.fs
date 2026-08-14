namespace PRo3D.Core.Drawing

open Aardvark.Base
open PRo3D.Base

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
                    | Some r -> CooTransformation.tryGetLatLonAltPlanet r.body.Value p
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
                    | Some r -> CooTransformation.tryGetLatLonAltPlanet r.body.Value p
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

