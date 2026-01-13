namespace PRo3D.Base.Annotation

open PRo3D.Base.Annotation
open Thoth.Json.Net

open Aardvark.Base

open PRo3D.Extensions

module GeoJsonQGIS =

    type CoordinateConfiguration =
        | CartesianOnly of targetReferenceFrame : string
        | GeographicOnly
        | Both of targetReferenceFrame : string

    type LatLonAlt = V3d
    type Cartesian = V3d
    type ReferenceFrame = string
    type Body = string

    let latLonAlt2Xyz (body : string) (lat : float) (lon : float) (alt : float) =
        let mutable x, y, z = 0.0, 0.0, 0.0
        let res = CooTransformation.LatLonAlt2Xyz(body, lat, lon, alt, &x, &y, &z)
        if res <> 0 then
            None
        else
            Some(V3d(x, y, z))

    let xyz2LatLonAlt (body : string) (referenceFrame : string) (cartesian : V3d)=
        let mutable lat, lon, alt = 0.0, 0.0, 0.0
        Log.warn "reference frame + body  not yet done, assuming IAU_MARS"
        let res = CooTransformation.Xyz2LatLonAlt(body, cartesian.X,cartesian.Y, cartesian.Z, &lat, &lon, &alt)
        if res <> 0 then
            None
        else
            Some(V3d(lat, lon, alt))

    let coordinate (p : V3d) =
        Encode.list [ Encode.float p.X; Encode.float p.Y; Encode.float p.Z ]

    let featureProperties
        (annotation : Annotation)
        (isSelected : Annotation -> bool )=
        match annotation.geometry with
        | Geometry.AxisEllipse ->
            Encode.object [
                "ellipse", Encode.bool true
                "isSelected", Encode.bool (isSelected annotation)
            ]
        | _ -> 
            Encode.object [
                "isSelected", Encode.bool (isSelected annotation)
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
            ]
        | Geometry.Polygon ->
            let coordinates =
                annotation.points
                |> Seq.map coordinate
                |> Seq.toList
            Encode.object [
                "type", Encode.string "Polygon"
                "coordinates", Encode.list [ Encode.list coordinates ]
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
            ]
        | _ ->
            Encode.object [
                "type", Encode.string "unknown"
                "coordinates", Encode.list []
            ]


    let feature
        (annotation : Annotation)
        (isSelected : Annotation -> bool)=
        Encode.object [
            "type", Encode.string "Feature"
            "geometry", featureGeometry annotation
            "properties", featureProperties annotation isSelected
        ]

    let globalProperties (annotations : Annotation list) =
        Encode.object [
            "referenceFrame", Encode.string "IAU_MARS" // TODO
        ]

    let featureCollection
        (annotations : Annotation list)
        (encodeCartesian : bool)
        (isSelected : Annotation -> bool) =
        match encodeCartesian with
        | true ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (annotations |> List.map (fun ann -> feature ann isSelected))
                "properties", globalProperties annotations

            ]
        | false ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (annotations |> List.map (fun ann -> feature ann isSelected))
            ]

    let encoder
        (annotations : Annotation list)
        (isSelected : Annotation -> bool) : string =
        (featureCollection annotations true isSelected).ToString()