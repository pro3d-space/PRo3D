namespace PRo3D.Base.Annotation

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI

open PRo3D.Base.Annotation.GeoJSON
open PRo3D.Base

open Chiron

module GeoJSONExport =
    
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