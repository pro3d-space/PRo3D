namespace PRo3D.Core.Drawing

open Aardvark.Base
open PRo3D.Base

module EllipticAnnotations =
    let sampleNumber = 50    

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
            let position = 
                // use altitude from first point (arbitrary decision, projection function will put points to surface again)
               CooTransformation.getXYZFromLatLonAlt (Conv.cartesianToGeographical geographical latLon) planet
            match projectToSurface position with
            | None -> 
                Log.warn "could not reproject ellipse point."
                None
            | Some p -> 
                Some p
        )

    type ConstructedEllipse = 
        {
            constructionPlane : Plane3d
            ellipseOnPlane : Ellipse2d
            surfaceProjectedEllipsePoints : array<V3d>
        }

    module ConstructedEllipse =
        let createGeographicalEllipse (planet : Planet)  (referenceSystem: Option<PRo3D.Base.Gis.SpiceReferenceSystem>) (c : ConstructedEllipse)  =
            let coord (p : V3d) =
                let c =
                    match referenceSystem with
                    | Some r -> CooTransformation.getLatLonAltPlanet r.body.Value p 
                    | None ->  CooTransformation.getLatLonAlt planet p 
                Conv.geographicalToCartesian c

            let transform (planePoint : V2d) = 
                c.constructionPlane.GetPlaneToWorld().TransformPos(V3d(planePoint, 0.0)) |> coord

            let center = transform c.ellipseOnPlane.Center
            Ellipse2d(center, transform (c.ellipseOnPlane.Center + c.ellipseOnPlane.Axis0) - center, transform (c.ellipseOnPlane.Center + c.ellipseOnPlane.Axis1) - center)
   

    let constructAndSampleFromPlane (fittedPlane : Plane3d) (points : array<V3d>) (projectToSurface : V3d -> Option<V3d>) = 
        let w2Plane = fittedPlane.GetWorldToPlane()
        let plane2World = fittedPlane.GetPlaneToWorld()
        match points |> Array.map (fun p -> w2Plane.TransformPos(p), p) with
        | [| (plane0, p0); (plane1, p1); (plane2, p2); |] -> 
            let ellipse = EllipseConstruction.constructEllipseOrtho2d (plane0.XY) (plane1.XY) (plane2.XY)
            let sampledPoints = EllipseConstruction.computeEllipsePoints ellipse 200
            let projectedEllipse = 
                sampledPoints 
                |> Array.choose (fun planePoint -> 
                    let position = plane2World.TransformPos(V3d(planePoint, 0.0))
                    match projectToSurface position with
                    | None -> 
                        Log.warn "could not reproject ellipse point."
                        None
                    | Some p -> 
                        Some p
                )
            Some { constructionPlane = fittedPlane; ellipseOnPlane = ellipse; surfaceProjectedEllipsePoints = projectedEllipse }
        | _ -> 
            None

    let constructAndSampleGeographical (planet : Planet) (referenceSystem: Option<PRo3D.Base.Gis.SpiceReferenceSystem>) (points : array<V3d>) (projectToSurface : V3d -> Option<V3d>) = 
        let geographicalPoints = 
            points 
            |> Array.map (fun p -> 
                let spherical = 
                    match referenceSystem with
                    | Some r -> CooTransformation.getLatLonAltPlanet r.body.Value p 
                    | None ->  CooTransformation.getLatLonAlt planet p 
                spherical, Conv.geographicalToCartesian spherical
            )

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

