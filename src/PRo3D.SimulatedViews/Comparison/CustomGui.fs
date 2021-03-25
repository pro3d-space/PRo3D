namespace Pro3D

open FSharp.Data.Adaptive
open Aardvark.UI
//open Aardvark.UI.Incremental
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface

module CustomGui =
    let dynamicDropdown<'msg when 'msg : equality> (items    : list<aval<string>>)
                                                   (selected : aval<string>) 
                                                   (change   : string -> 'msg) =
        let attributes (name : aval<string>) =
            AttributeMap.ofListCond [
                Incremental.always "value" name
                onlyWhen (AVal.map2 (=) name selected) (attribute "selected" "selected")
            ]
       
        select [onChange (fun str -> str |> change); style "color:black"] [
                for name in items do
                    let att = attributes name
                    yield Incremental.option att (AList.ofList [Incremental.text name])
        ] 

    let surfacesDropdown (surfaces : AdaptiveSurfaceModel) (change : string -> 'msg) =
        let surfaceToName (s : aval<AdaptiveSurface>) =
            s |> AVal.bind (fun s -> s.name)

        let surfaces = surfaces.surfaces.flat |> toAvalSurfaces
        let surfaceNames = surfaces |> AMap.map (fun g s -> s |> surfaceToName)                                                         
                                    |> AMap.toAVal
        
        let items = 
          surfaceNames |> AVal.map (fun n -> n.ToValueList ())

        let dropdown = 
            items |> AVal.map (fun items -> dynamicDropdown items ("" |> AVal.constant) change)

        Incremental.div (AttributeMap.ofList []) (AList.ofAValSingle dropdown)
        

