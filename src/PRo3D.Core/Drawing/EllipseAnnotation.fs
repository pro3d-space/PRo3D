namespace PRo3D.Core.Drawing

open Aardvark.Base
open PRo3D.Base

module EllipticAnnotations =

    module Conv = 
        let geographicalToCartesian (v : CooTransformation.SphericalCoo) =
            V2d(v.longitude, v.latitude)
        let cartesianToGeographical (basePosition : CooTransformation.SphericalCoo) (v : V2d) : CooTransformation.SphericalCoo = 
            { basePosition with
                CooTransformation.SphericalCoo.longitude = v.X
                CooTransformation.SphericalCoo.latitude = v.Y
            }
            


    let constructAndSample (planet : Planet) (referenceSystem: Option<PRo3D.Base.Gis.SpiceReferenceSystem>) (points : array<V3d>) (projectToSurface : V3d -> Option<V3d>) = 
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
        | [| (geographical, p0); (_,p1); (_,p2) |] -> 
            Log.line "ellipse points: %A" geographicalPoints
            let ellipse = EllipseConstruction.constructEllipseOrtho2d p0 p1 p2
            let sampledEllipse = EllipseConstruction.computeEllipsePoints ellipse 200
            let projectedEllipse = 
                sampledEllipse 
                |> Array.choose (fun latLon -> 
                    let position = 
                        match referenceSystem with
                        | None -> 
                            // use altitude from first point (arbitrary decision, projection function will put points to surface again)
                           CooTransformation.getXYZFromLatLonAlt (Conv.cartesianToGeographical geographical latLon) planet
                        | Some r -> 
                           CooTransformation.getXYZFromLatLonAltPlanet (Conv.cartesianToGeographical geographical latLon) r.body.Value
                    match projectToSurface position with
                    | None -> 
                        Log.warn "could not reproject ellipse point."
                        None
                    | Some p -> 
                        Some p
                )
            Some (ellipse, projectedEllipse)
        | _ -> 
            None