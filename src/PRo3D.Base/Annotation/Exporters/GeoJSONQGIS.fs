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

    let xyz2LatLonAlt
        (body : string)
        (cartesian : V3d)=
        let mutable lat, lon, alt = 0.0, 0.0, 0.0
        let res = CooTransformation.Xyz2LatLonAlt(body, cartesian.X,cartesian.Y, cartesian.Z, &lat, &lon, &alt)
        if res <> 0 then
            None
        else
            Some(V3d(lat, lon, alt))

    let coordinate
        (planet : string)
        (cartesian : V3d) =
        let latLon = xyz2LatLonAlt planet cartesian
        match latLon with
        | Some ll -> Encode.list [ Encode.float ll.X; Encode.float ll.Y; Encode.float ll.Z ]
        | None -> Encode.list [] // what should be the default?

    let featureProperties
        (xyz : bool)
        (isSelected : Annotation -> bool)
        (annotation : Annotation)=
        let coordinates =
            annotation.points
            |> Seq.map (fun point -> Encode.list [ Encode.float point.X; Encode.float point.Y; Encode.float point.Z ])
            |> Seq.toList
        Encode.object [
            "isEllipse", Encode.bool (annotation.geometry = Geometry.AxisEllipse)
            "isSelected", Encode.bool (isSelected annotation)
            "cartesian", Encode.list coordinates
        ]

    let featureGeometry
        (planet : string)
        (annotation: Annotation) =
        match annotation.geometry with
        | Geometry.Point ->
            let p =
                annotation.points
                |> Seq.head
            Encode.object [
                "type", Encode.string "Point"
                "coordinates", coordinate planet p
            ]
        | Geometry.Line
        | Geometry.Polyline
        | Geometry.DnS
        | Geometry.TT ->
            let coordinates =
                annotation.points
                |> Seq.map (fun point -> coordinate planet point)
                |> Seq.toList
            Encode.object [
                "type", Encode.string "LineString"
                "coordinates", Encode.list coordinates
            ]
        | Geometry.Polygon ->
            let coordinates =
                annotation.points
                |> Seq.map (fun point -> coordinate planet point)
                |> Seq.toList
            Encode.object [
                "type", Encode.string "Polygon"
                "coordinates", Encode.list [ Encode.list coordinates ]
            ]
        | Geometry.AxisEllipse ->
            let coordinates =
                annotation.points
                |> Seq.map (fun point -> coordinate planet point)
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
        (xyz : bool)
        (planet      : string) 
        (isSelected  : Annotation -> bool)
        (annotation : Annotation) =
        Encode.object [
            "type", Encode.string "Feature"
            "geometry", featureGeometry planet annotation
            "properties", featureProperties xyz isSelected annotation
        ]

    let globalProperties (planet : string) =
        Encode.object [
            "planet", Encode.string planet
        ]

    let featureCollection
        (xyz : bool)
        (planet      : string) 
        (isSelected  : Annotation -> bool)
        (annotations : list<Annotation>)  =
        match xyz with
        | true ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (annotations |> List.map (fun ann -> feature xyz planet isSelected ann))
                "properties", globalProperties planet
            ]
        | false ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (annotations |> List.map (fun ann -> feature xyz planet isSelected ann))
            ]

    let encoder
        (xyz : bool)
        (planet      : string) 
        (isSelected  : Annotation -> bool)
        (annotations : list<Annotation>) 
        : string =
        (featureCollection xyz planet isSelected annotations).ToString()
