namespace PRo3D.Viewer

open Aardvark.Base
open Aardvark.UI
open Chiron


type TraverseAction =
| LoadTraverse of string

module TraverseApp =
    let update (model : Traverse) (action : TraverseAction) : Traverse = 
        match action with
        | LoadTraverse path ->
            Log.line "[Traverse] Loading %s" path
            let geojson = System.IO.File.ReadAllText @".\M20_waypoints.json"
            let featurecollection_des : PRo3D.Base.Annotation.GeoJSON.GeoJsonFeatureCollection = 
                geojson |> Json.parse |> Json.deserialize

            Log.line "%A" featurecollection_des
            model

    let view (model : AdaptiveTraverse) : DomNode<TraverseAction> = 
        failwith ""
        

