namespace PRo3D

open System
open Aardvark.UI
open PRo3D.Comparison
open FSharp.Data.Adaptive
open Aardvark.UI
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface

open Aardvark.UI.Primitives
open PRo3D.Base
module CustomGui =
    let dynamicDropdown<'msg when 'msg : equality> (items    : list<aval<string>>)
                                                   (selected : aval<string>) 
                                                   (change   : string -> 'msg) =
        let attributes (name : aval<string>) =
            AttributeMap.ofListCond [
                Incremental.always "value" name
                onlyWhen (AVal.map2 (=) name selected) (attribute "selected" "selected")
            ]
     
        let callback = onChange (fun str -> 
                                    str |> change)

        select [callback; style "color:black"] [
                for name in items do
                    let att = attributes name
                    yield Incremental.option att (AList.ofList [Incremental.text name])
        ] 

    let surfacesDropdown (surfaces : AdaptiveSurfaceModel) (change : string -> 'msg) (noSelection : string)=
        let surfaceToName (s : aval<AdaptiveSurface>) =
            s |> AVal.bind (fun s -> s.name)

        let surfaces = surfaces.surfaces.flat |> toAvalSurfaces
        let surfaceNames = surfaces |> AMap.map (fun g s -> s |> surfaceToName)                                                         
                                    |> AMap.toAVal
        let items = 
          surfaceNames |> AVal.map (fun n -> n.ToValueList ())
            |> AVal.map (fun x -> List.append [(noSelection |> AVal.constant)] x)

        let dropdown = 
            items |> AVal.map (fun items -> dynamicDropdown items (noSelection |> AVal.constant) change)

        Incremental.div (AttributeMap.ofList []) (AList.ofAValSingle dropdown)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ComparisonApp =
    let noSelection = "-None-"

    let init : ComparisonApp =
      {
          surface1 = None
          surface2 = None
          measurements1 = SurfaceMeasurements.init
          measurements2 = SurfaceMeasurements.init
      }

    let noSelectionToNone (str : string) =
        if str = noSelection then None else Some str

    let findSurfaceByName (surfaceModel : SurfaceModel) (name : string) =
        let surfacesWithName = 
            surfaceModel.surfaces.flat
              |> HashMap.map (fun x -> Leaf.toSurface)
              |> HashMap.filter (fun k v -> v.name = name)
        if surfacesWithName.Count > 0 then
            surfacesWithName |> HashMap.keys |> HashSet.toList |> List.tryHead
        else None



    let calculateMeasurements (surfaceModel : SurfaceModel) (surfaceName: string) =
        let surfaceId = findSurfaceByName surfaceModel surfaceName
        match surfaceId with
        | Some surfaceId ->
            let surface = surfaceModel.surfaces.flat |> HashMap.find surfaceId |> Leaf.toSurface
            let surfaceSg = surfaceModel.sgSurfaces |> HashMap.find surfaceId
            ()
        | None -> ()
        // model origin
        // direction x/y/-x/-y/-z
        
        // SurfaceIntersection.doKdTreeIntersection surfaces refSystem (FastRay3d(ray)) surfaceFilter cache

        //let ray = new Ray3d (


    let update (m : ComparisonApp) (surfaceModel : SurfaceModel) (msg : ComparisonAction) =
        match msg with
        | Update -> m
        | SelectSurface1 str -> 
            {m with surface1 = noSelectionToNone str}
        | SelectSurface2 str -> 
            {m with surface2 = noSelectionToNone str}
        | MeasurementMessage -> m

    let findASurfaceByName (surfaceModel : AdaptiveSurfaceModel) (name : aval<string>) =
        let surfaces = SurfaceUtils.toAvalSurfaces surfaceModel.surfaces.flat
        let toName (surface : aval<AdaptiveSurface>) =
            surface |> AVal.bind (fun s -> s.name)
        let filtered = surfaces |> AMap.filterA (fun k v -> AVal.map2 (fun a b -> a = b) name (toName v))
        let surfaceId =
            adaptive {
                let! count = AMap.count filtered
                if count > 0 then
                    let first = (filtered |> AMap.keys |> AList.ofASet |> AList.tryFirst)
                    return! first
                else return None
            }
        surfaceId

    let view (m : AdaptiveComparisonApp) (surfaces : AdaptiveSurfaceModel) =
        let measurementGui (name : option<string>) =
            match name with
            | Some name -> 
                adaptive {
                    let header = sprintf "Measurements for %s"  name
                    let accordion =
                        GuiEx.accordion header  "calculator" true [
                            SurfaceMeasurements.view m.measurements1
                        ]
                    return  accordion 
                }
            | None    -> 
                GuiEx.accordion "No surface selected"  "calculator" true [] 
                    |> AVal.constant

        let measurement1 = 
            m.surface1 |> AVal.bind (fun x -> measurementGui x)
                       |> AList.ofAValSingle

        let measurement2 = 
            m.surface2 |> AVal.bind (fun x -> measurementGui x)
                       |> AList.ofAValSingle

        div [][
            br []
            div [] [text "Surface1 ";CustomGui.surfacesDropdown surfaces SelectSurface1 noSelection]
            div [] [text "Surface2 ";CustomGui.surfacesDropdown surfaces SelectSurface2 noSelection]
            Incremental.div ([] |> AttributeMap.ofList) measurement1
            Incremental.div ([] |> AttributeMap.ofList) measurement2
            GuiEx.accordion "Difference" "calculator" true [
            ]
        ]
