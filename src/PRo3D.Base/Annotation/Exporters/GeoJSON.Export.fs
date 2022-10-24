#nowarn "8989" // pickler factory creation

namespace PRo3D.Base.Annotation

open System
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI

open PRo3D.Base.Annotation.GeoJSON
open PRo3D.Base

open Chiron
open MBrace.FsPickler
open Aardvark.Rendering

module GeoJSONExport =

    let private createPickler() =
        // 1. Create a pickler registry and make custom pickler registrations
        let registry = new CustomPicklerRegistry()
        let mkPickler (resolver : IPicklerResolver) =
            let intPickler = resolver.Resolve<Trafo3d> ()
    
            let writer (w : WriteState) (ns : CameraView) =
                intPickler.Write w "value" ns.ViewTrafo
    
            let reader (r : ReadState) =
                let v = intPickler.Read r "value" 
                CameraView.ofTrafo v
    
            Pickler.FromPrimitives(reader, writer)
        do registry.RegisterFactory mkPickler
    
        // 2. Construct a new pickler cache
        let cache = PicklerCache.FromCustomPicklerRegistry registry

        BinarySerializer(picklerResolver = cache)

    let pickler = createPickler()
    
    let annotationToGeoJsonGeometry 
        (planet : option<Planet>) 
        (a      : Annotation)
        : GeoJSON.GeoJsonGeometry =

        // add sampled points to the export
        let points = 
            a |> Annotation.retrievePoints
        
        let coordinates = 
            match planet with 
            | Some p ->
                points 
                |> List.map (fun x ->
                    let coord = CooTransformation.getLatLonAlt p x
                    { coord with longitude = 360.0 - coord.longitude }
                )
                |> List.map CooTransformation.SphericalCoo.toV3d                
            | None ->
                points                

        match a.geometry with
        | Geometry.Point ->
            coordinates |> List.map ThreeDim |> List.head |> GeoJsonGeometry.Point                
        | Geometry.Line -> 
            coordinates |> List.map ThreeDim |> GeoJsonGeometry.LineString
        | Geometry.Polyline -> 
            coordinates |> List.map ThreeDim |> List.singleton |> GeoJsonGeometry.MultiLineString
        | Geometry.Polygon -> 
            coordinates |> List.map ThreeDim |> List.singleton |> GeoJsonGeometry.Polygon
        | Geometry.DnS -> 
            coordinates |> List.map ThreeDim |> List.singleton |> GeoJsonGeometry.Polygon
        | Geometry.TT ->
            coordinates |> List.map ThreeDim |> GeoJsonGeometry.LineString
        | _ ->
            Point(V3d.NaN |> ThreeDim)
                  
    let writeGeoJSON 
        (planet      : option<Planet>) 
        (path        : string) 
        (annotations : list<Annotation>) 
        : unit = 

        if path.IsEmpty() then ()
    
        let geometryCollection =
            annotations
            |> List.map(annotationToGeoJsonGeometry (planet))
            |> GeoJsonGeometry.GeometryCollection

        geometryCollection
        |> Json.serialize
        |> Json.formatWith JsonFormattingOptions.Pretty
        |> Serialization.writeToFile path

        ()            

    let writeGeoJSON_XYZ 
        (path        : string)
        (annotations : list<Annotation>) 
        : unit = 

        writeGeoJSON None path annotations


    // exports geojson objects as line delimited json: https://en.wikipedia.org/wiki/JSON_streaming#Line-delimited_JSON
    // the feature has been discussed here: https://github.com/pro3d-space/PRo3D/issues/185
    let writeStreamGeoJSON_XYZ (path : string) (annotations : list<Annotation>) : unit = 
  
        let encode (bytes : byte[]) = Convert.ToBase64String(bytes);

        let lines =
            annotations
            |> List.map (fun annotation -> 

                let geometry = annotationToGeoJsonGeometry None annotation

                let feature = 
                    {
                        geometry   = geometry
                        bbox       = None
                        properties = 
                            Map.ofList [
                                ("id", annotation.key.ToString() |> Json.String)
                                ("color", annotation.color.c.ToString() |> Json.String)
                                ("geometry", annotation.geometry.ToString() |> Json.String)
                                // extend as desired. https://github.com/pro3d-space/PRo3D/issues/185
                            ]
                    }
                { feature with 
                    properties = 
                        Map.union feature.properties <| Map.ofList [
                            // the real, full hash of the pro3d annotation. 
                            // if this has changed something has changed in the annotation
                            // yet this does not mean the change is visible in the exported data.
                            // this can be used for example for pulling in changes using 
                            // the pro3d rest api.
                            ("fullHash", pickler.ComputeHash(annotation).Hash |> encode |> Json.String)
                            // the hash of the geojson value (not the internal annotation representation)
                            ("hash",     pickler.ComputeHash(feature).Hash |> encode  |> Json.String)
                            // hashes the geometry - useful for fitting/regression implementation in external tools
                            ("geometryHash", pickler.ComputeHash(geometry).Hash |> encode  |> Json.String)
                        ]
                }
            )
            |> List.map (Json.serialize >> Json.formatWith JsonFormattingOptions.SingleLine)
            |> List.toArray

        File.writeAllLines path lines
