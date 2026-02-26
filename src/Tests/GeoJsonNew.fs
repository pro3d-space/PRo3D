module GeoJsonNew

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

    // 	18° 22′ 48″ N, 77° 34′ 48″ E  (18.38°, 77.58°)

    let annoations = 
        { Annotation.initial with geometry = Geometry.Point; points = IndexList.ofList [V3d(1.0,10.0,10.0)] }
