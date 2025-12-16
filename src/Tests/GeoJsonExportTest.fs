namespace GeoJsonRework

module GeoJsonExportTest =


    open Aardvark.Base
    open PRo3D.Core
    open PRo3D.Base.Annotation
    open FSharp.Data.Adaptive
    open Adaptify.FSharp


    module GeoJsonImportExport =  

        // requirements
        // - geojson conformant export
        // - should be compatible with qgis and arcgis
        // - additional properties: isSelected, ellipse
        // - support for cartesian and geographic coordinates
        //    - by default export in geographic coordinates (lon, lat, alt)
        //    - flag while export, whether cartesian coordiantes shall be included (in properties field)
        // - if cartesian coordinates are included, the reference frame must be included as well (in properties field top level meta data)
        // - support for different annotation types (point, line, polygon, ellipse, ...)

        (*
    
       {
           "type": "FeatureCollection",
           "features": [{
	           "type": "Feature",
	           "geometry": {
		           "type": "Point",
		           "coordinates": [102.0, 0.5],
                   "properties": {
                        "cartesianCoordinates": [x, y, z]
                    }
	           },
	           "properties": {
		           "referenceFrame": "IAU_MARS" // only if cartesian coordinates are included
	           }
             }]
       }
    
        *)

        (*
    
        earlier: Annotations -> GeometryCollection -> Chiron Serilizer 
        then: Annoations -> string (via json lib)

        read in: string -> Annoations (low prio)

        *)


        let test (a : Annotation) = 
            ()

        type CoordinateConfiguration =
            | CartesianOnly of referenceFrame : string
            | GeographicOnly
            | Both of referenceFrame : string

        type LatLonAlt = V3d
        type Cartesian = V3d
    

        let toJsonString (referenceFrame : string) 
                         (configuration : CoordinateConfiguration) 
                         (toLatLonAlt : Cartesian -> LatLonAlt)
                         (a : Annotations) : string = 
            failwith ""


        let annoations = 
            { Annotation.initial with geometry = Geometry.Point; points = IndexList.ofList [V3d(1.0,10.0,10.0)] }


module Tests =

    open System
    open System.IO

    open Expecto

    open FSharp.NativeInterop

    open Aardvark.Base

    open PRo3D.Extensions
    open PRo3D.Extensions.FSharp

    let logDir = Path.Combine(".", "logs")
    let spiceRoot = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..")
    let spiceFileName = Path.Combine(spiceRoot, "spice", "kernels", "mk", "hera_ops.tm")

    do Aardvark.Base.Aardvark.UnpackNativeDependencies(typeof<CooTransformation.RelState>.Assembly)

    let init () =
        if not (Directory.Exists(logDir)) then 
            Directory.CreateDirectory(logDir) |> ignore

        let r = CooTransformation.Init(true, Path.Combine(logDir, "CooTrafo.log"), 4, 4)
        if r <> 0 then failwith "init failed."
        { new IDisposable with member x.Dispose() = CooTransformation.DeInit()}

    let latLonAlt2Xyz (body : string) (lat : float) (lon : float) (alt : float) =
        let mutable x, y, z = 0.0, 0.0, 0.0
        let res = CooTransformation.LatLonAlt2Xyz(body, lat, lon, alt, &x, &y, &z)
        if res <> 0 then
            None
        else
            Some(V3d(x, y, z))

    let tests () =
        testSequenced <| testList "init" [

            use _ = init()
            let fullPath = Path.GetFullPath(spiceFileName)
            System.Environment.CurrentDirectory <- Path.GetDirectoryName(fullPath)
            let init = CooTransformation.AddSpiceKernel(fullPath)
            Expect.equal 0 init "spice adding"


            test "GetCoords" {
                // 	18° 22′ 48″ N, 77° 34′ 48″ E  (18.38°, 77.58°)
                let lat, lon, alt = 18.38, 77.58, 0.0

                let xyz = latLonAlt2Xyz "mars" lat lon alt
                Expect.isSome xyz "could get xyz from latlonalt"
            }
        ]