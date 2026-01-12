namespace PRo3D.Base.Annotation

open PRo3D.Core
open PRo3D.Base.Annotation
open Thoth.Json.Net

open Aardvark.Base
open FSharp.Data.Adaptive

module GeoJsonThoth =

    let coordinate (p : V3d) =
        Encode.list [ Encode.float p.X; Encode.float p.Y; Encode.float p.Z ]

    let featureProperties (annotation: Annotation) =
        match annotation.geometry with
        | Geometry.AxisEllipse ->
            Encode.object [
                "ellipse", Encode.bool true
            ]
        | _ -> 
            Encode.object [
            ]

    let featureGeometry (annotation: Annotation) =
        match annotation.geometry with
        | Geometry.Point ->
            let p =
                annotation.points
                |> Seq.head
            Encode.object [
                "type", Encode.string "Point"
                "coordinates", coordinate p
                "properties", Encode.object []
            ]
        | Geometry.Line
        | Geometry.Polyline
        | Geometry.DnS
        | Geometry.TT ->
            let coordinates =
                annotation.points
                |> Seq.map coordinate
                |> Seq.toList
            Encode.object [
                "type", Encode.string "LineString"
                "coordinates", Encode.list coordinates
                "properties", Encode.object []
            ]
        | Geometry.Polygon ->
            let coordinates =
                annotation.points
                |> Seq.map coordinate
                |> Seq.toList
            Encode.object [
                "type", Encode.string "Polygon"
                "coordinates", Encode.list [ Encode.list coordinates ]
                "properties", Encode.object []
            ]
        | Geometry.AxisEllipse ->
            let coordinates =
                annotation.points
                |> Seq.map coordinate
                |> Seq.toList
            let coordinatesClosed =
                match coordinates with
                | [] -> []
                | h :: _ -> coordinates @ [h]
            Encode.object [
                "type", Encode.string "Polygon"
                "coordinates", Encode.list [ Encode.list coordinatesClosed ]
                "properties", Encode.object []
            ]
        | _ ->
            Encode.object [
                "type", Encode.string "unknown"
                "coordinates", Encode.list []
            ]


    let feature (annotation : Annotation) =
        Encode.object [
            "type", Encode.string "Feature"
            "geometry", featureGeometry annotation
            "properties", featureProperties annotation
        ]

    let globalProperties (annotations : Annotation list) =
        Encode.object [
            "referenceFrame", Encode.string "IAU_MARS" // TODO
        ]

    let featureCollection (annotations : Annotation list) (encodeCartesian : bool)=
        match encodeCartesian with
        | true ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (annotations |> List.map (fun ann -> feature ann))
                "properties", globalProperties annotations

            ]
        | false ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (annotations |> List.map (fun ann -> feature ann))
            ]

    let encoder (annotations : Annotation list) : string =
        (featureCollection annotations true).ToString()