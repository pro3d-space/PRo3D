namespace PRo3D.Base.Annotation

open PRo3D.Base.Annotation
open Thoth.Json.Net

open Aardvark.Base

open PRo3D.Extensions

module GeoJsonQGIS =

    type CoordinateConfiguration =
        | CartesianOnly
        | GeographicOnly of targetReferenceFrame : string
        | Both of targetReferenceFrame : string

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
        (cooConfig : CoordinateConfiguration)
        (cartesian : V3d) =
        match cooConfig with
        | CoordinateConfiguration.Both planet
        | CoordinateConfiguration.GeographicOnly planet ->
            let latLon = xyz2LatLonAlt planet cartesian
            match latLon with
            | Some ll -> Encode.list [ Encode.float ll.X; Encode.float ll.Y; Encode.float ll.Z ]
            | None ->
                Log.warn "no reference frame set: can not convert from cartesian to geographic coordinates..."
                Encode.list [ Encode.string "Error: No / invalid reference frame set" ] // or could there be other reasons why xyz2LatLonAlt fails?
        | CoordinateConfiguration.CartesianOnly ->
            Encode.list [ Encode.float cartesian.X; Encode.float cartesian.Y; Encode.float cartesian.Z ]

    let featureProperties
        (cooConfig : CoordinateConfiguration)
        (isSelected : Annotation -> bool)
        (annotation : Annotation)=
        let coordinates =
            annotation.points
            |> Seq.map (fun point -> Encode.list [ Encode.float point.X; Encode.float point.Y; Encode.float point.Z ])
            |> Seq.toList
        if cooConfig.IsBoth then
            Encode.object [
                "isEllipse", Encode.bool (annotation.geometry = Geometry.AxisEllipse)
                "isSelected", Encode.bool (isSelected annotation)
                "cartesian", Encode.list coordinates
            ]
        else
            Encode.object [
                "isEllipse", Encode.bool (annotation.geometry = Geometry.AxisEllipse)
                "isSelected", Encode.bool (isSelected annotation)
            ]

    let featureGeometry
        (cooConfig : CoordinateConfiguration)
        (annotation: Annotation) =
        match annotation.geometry with
        | Geometry.Point ->
            let p =
                annotation.points
                |> Seq.head
            Encode.object [
                "type", Encode.string "Point"
                "coordinates", coordinate cooConfig p
            ]
        | Geometry.Line
        | Geometry.Polyline
        | Geometry.DnS
        | Geometry.TT ->
            let coordinates =
                annotation.points
                |> Seq.map (fun point -> coordinate cooConfig point)
                |> Seq.toList
            Encode.object [
                "type", Encode.string "LineString"
                "coordinates", Encode.list coordinates
            ]
        | Geometry.Polygon ->
            let coordinates =
                annotation.points
                |> Seq.map (fun point -> coordinate cooConfig point)
                |> Seq.toList
            Encode.object [
                "type", Encode.string "Polygon"
                "coordinates", Encode.list [ Encode.list coordinates ]
            ]
        | Geometry.AxisEllipse ->
            let coordinates =
                annotation.points
                |> Seq.map (fun point -> coordinate cooConfig point)
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
        (cooConfig : CoordinateConfiguration)
        (isSelected  : Annotation -> bool)
        (annotation : Annotation) =
        Encode.object [
            "type", Encode.string "Feature"
            "geometry", featureGeometry cooConfig annotation
            "properties", featureProperties cooConfig isSelected annotation
        ]

    let globalProperties (planet : string) =
        Encode.object [
            "planet", Encode.string planet
        ]

    let featureCollection
        (cooConfig : CoordinateConfiguration)
        (isSelected  : Annotation -> bool)
        (annotations : list<Annotation>)  =
        match cooConfig with
        | CoordinateConfiguration.Both planet
        | CoordinateConfiguration.GeographicOnly planet ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (annotations |> List.map (fun ann -> feature cooConfig isSelected ann))
                "properties", globalProperties planet
            ]
        | CoordinateConfiguration.CartesianOnly ->
            Encode.object [
                "type", Encode.string "FeatureCollection"
                "features", Encode.list (annotations |> List.map (fun ann -> feature cooConfig isSelected ann))
            ]

    let encoder
        (cooConfig : CoordinateConfiguration)
        (isSelected  : Annotation -> bool)
        (annotations : list<Annotation>) 
        : string =
        (featureCollection cooConfig isSelected annotations).ToString()
