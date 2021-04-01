namespace PRo3D

open System
open Aardvark.UI
open PRo3D.Comparison
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Trafos
open Aardvark.UI.Primitives
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Base
open Aardvark.Rendering

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
          showMeasurementsSg = true
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



    let calculateMeasurements (surfaceModel : SurfaceModel) 
                              (refSystem    : ReferenceSystem) 
                              (surfaceName  : string) =
        let surfaceId = findSurfaceByName surfaceModel surfaceName
        match surfaceId with
        | Some surfaceId ->
            let surfaceFilter (id : Guid) (l : Leaf) (s : SgSurface) =
                id = surfaceId
            let surface = surfaceModel.surfaces.flat |> HashMap.find surfaceId |> Leaf.toSurface
            let surfaceSg = surfaceModel.sgSurfaces |> HashMap.find surfaceId
            let trafo = SurfaceTransformations.fullTrafo' surface refSystem

            
                            

            ()
        | None -> ()



        ()
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

    let isSelected (surfaceName : aval<string>) (m : AdaptiveComparisonApp) =
        let showSg = 
            AVal.map3 (fun (s1 : option<string>) s2 surfaceName -> 
                          match s1, s2 with
                          | Some s1, Some s2 ->
                            s1 = surfaceName || s2 = surfaceName
                          | Some s1, None -> s1 = surfaceName
                          | None, Some s2 -> s2 = surfaceName
                          | None, None -> false
                      ) m.surface1 m.surface2 surfaceName
        showSg

    let defaultCoordinateCross size trafo =
        let sg = 
            Sg.coordinateCross size
                |> Sg.trafo trafo
                |> Sg.noEvents
                |> Sg.effect [              
                    Shader.stableTrafo |> toEffect 
                    DefaultSurfaces.vertexColor |> toEffect
                ] 
                |> Sg.noEvents
        sg

    let measurementsSg (surfaceName : aval<string>)
                       (size        : aval<float>)
                       (trafo       : aval<Trafo3d>) 
                       (m           : AdaptiveComparisonApp) =    


        let showSg = isSelected surfaceName m

        let sg =
            showSg |> AVal.map (fun show -> 
                                  match show with
                                  | true -> defaultCoordinateCross size trafo
                                  | false -> Sg.empty
                               )
        sg |> Sg.dynamic


            
                

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
