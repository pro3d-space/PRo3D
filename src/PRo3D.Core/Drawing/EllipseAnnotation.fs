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

    let constructAndSample (planet : Planet) (points : array<V3d>) (projectToSurface : V3d -> Option<V3d>) = 
        let geographicalPoints = 
            points 
            |> Array.map (fun p -> 
                let spherical = CooTransformation.getLatLonAlt planet p 
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
            let ellipse = EllipseConstruction.constructEllipseOrtho2d p0 p1 p2
            let sampledEllipse = EllipseConstruction.computeEllipsePoints ellipse sampleNumber
            let projectedEllipse = createProjectedEllipse projectToSurface planet geographical sampledEllipse
                
            Some ([ ellipse ], projectedEllipse)
        | _ -> 
            None

