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
    
    let annotationToGeoJsonGeometry (planet : option<Planet>) (a : Annotation) : GeoJSON.GeoJsonGeometry =

        let latLonAltPoints = 
            match planet with 
            | Some p ->
                a.points 
                |> IndexList.map (CooTransformation.getLatLonAlt p)
                |> IndexList.map CooTransformation.SphericalCoo.toV3d
                |> IndexList.toList
            | None ->
                a.points
                |> IndexList.toList

        match a.geometry with
        | Geometry.Point ->
            latLonAltPoints |> List.map ThreeDim |> List.head |> GeoJsonGeometry.Point                
        | Geometry.Line -> 
            latLonAltPoints |> List.map ThreeDim |> GeoJsonGeometry.LineString
        | Geometry.Polyline -> 
            latLonAltPoints |> List.map ThreeDim |> List.singleton |> GeoJsonGeometry.MultiLineString
        | Geometry.Polygon -> 
            latLonAltPoints |> List.map ThreeDim |> List.singleton |> GeoJsonGeometry.Polygon
        | Geometry.DnS -> 
            latLonAltPoints |> List.map ThreeDim |> List.singleton |> GeoJsonGeometry.Polygon
        | Geometry.TT ->
            latLonAltPoints |> List.map ThreeDim |> GeoJsonGeometry.LineString
        | _ ->
            Point(V3d.NaN |> ThreeDim)
                  
    let writeGeoJSON (planet : Planet) (path:string) (annotations : list<Annotation>) : unit = 

        if path.IsEmpty() then ()
    
        let geometryCollection =
            annotations
            |> List.map(annotationToGeoJsonGeometry (Some planet))
            |> GeoJsonGeometry.GeometryCollection

        geometryCollection
        |> Json.serialize 
        |> Json.formatWith JsonFormattingOptions.Pretty 
        |> Serialization.writeToFile path

        ()            

    let writeGeoJSON_XYZ (path:string) (annotations : list<Annotation>) : unit = 

        if path.IsEmpty() then ()
    
        let geometryCollection =
            annotations
            |> List.map(annotationToGeoJsonGeometry None)
            |> GeoJsonGeometry.GeometryCollection

        geometryCollection
        |> Json.serialize 
        |> Json.formatWith JsonFormattingOptions.Pretty 
        |> Serialization.writeToFile path

        ()


    // exports geojson objects as line delimited json: https://en.wikipedia.org/wiki/JSON_streaming#Line-delimited_JSON
    let writeStreamGeoJSON_XYZ (path : string) (annotations : list<Annotation>) : unit = 
  
        // base64 might be better but might contain \ which might be hard to handle
        let encode (bytes : byte[]) = System.Web.HttpUtility.UrlEncode(bytes)

        let lines =
            annotations
            |> List.map (fun annotation -> 

                let feature = 
                    {
                        geometry   = annotationToGeoJsonGeometry None annotation
                        bbox       = None
                        properties = 
                            Map.ofList [
                                ("id", annotation.key.ToString() |> Json.String)
                                ("color", annotation.color.c.ToString() |> Json.String)
                                // extend as desired.
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
                        ]
                }
            )
            |> List.map (Json.serialize >> Json.formatWith JsonFormattingOptions.SingleLine)
            |> List.toArray

        File.writeAllLines path lines
