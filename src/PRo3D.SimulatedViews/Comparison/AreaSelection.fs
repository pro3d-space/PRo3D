namespace PRo3D.Comparison

open Adaptify
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive
open PRo3D.Base
open Aardvark.UI
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Base
open Aardvark.Rendering
open Adaptify.FSharp.Core
open Aardvark.GeoSpatial
open Aardvark.UI
open Aardvark.UI.Primitives


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AreaSelection =
    let areaSelectionColor =
        C4b (150uy,255uy,150uy,100uy) 
          |> AVal.constant 
    //let pointSize = 7.0
    let areaSizeChangeFactor = 0.1

    let init guid label : AreaSelection = 
        {
            id          = guid
            label       = label
            radius      = 1.0
            location    = V3d.OOO
            surfaceTrafo    = Trafo3d.Identity
            visible     = true
            highResolution = false
            verticesSurf1 = IndexList.empty
            verticesSurf2 = IndexList.empty
            statistics  = None
        }



    let updateAreaStatistic  (surfaceModel : SurfaceModel) 
                              (refSystem    : ReferenceSystem) 
                              (surfaceGeometry : SurfaceGeometryType)
                              (area         : AreaSelection)
                              (surfaceName1  : string) 
                              (surfaceName2  : string) =
        let surfaceId1 = ComparisonUtils.findSurfaceByName surfaceModel surfaceName1
        let surfaceId2 = ComparisonUtils.findSurfaceByName surfaceModel surfaceName2
        match surfaceId1, surfaceId2 with
        | Some surfaceId1, Some surfaceId2 ->
            let surface1 = surfaceModel.surfaces.flat |> HashMap.find surfaceId1 |> Leaf.toSurface
            let sgSurface1 = surfaceModel.sgSurfaces |> HashMap.find surfaceId1
            let surface2 = surfaceModel.surfaces.flat |> HashMap.find surfaceId2 |> Leaf.toSurface
            let sgSurface2 = surfaceModel.sgSurfaces |> HashMap.find surfaceId2
  
            Some (AreaComparison.calculateStatistics surface1 sgSurface1
                                                     surface2 sgSurface2
                                                     surfaceModel
                                                     surfaceGeometry
                                                     refSystem area )
        | _,_ -> None

    let update (m : AreaSelection) (action : AreaSelectionAction) =
        match action with
        | AreaSelectionAction.MakeBigger -> 
            {m with radius = m.radius * (1.0 + areaSizeChangeFactor)}
        | AreaSelectionAction.MakeSmaller -> 
            {m with radius = m.radius * (1.0 - areaSizeChangeFactor)}
        | AreaSelectionAction.SetRadius r -> {m with radius = r}
        | AreaSelectionAction.SetLocation location -> m
        | AreaSelectionAction.ToggleVisible ->
            {m with visible = not m.visible}
        | AreaSelectionAction.ToggleResolution ->
            {m with highResolution = not m.highResolution}
        | AreaSelectionAction.UpdateStatistics -> 
            m
        | AreaSelectionAction.Nop -> m

    //let sgPoints (m : AdaptiveAreaSelection) = 
    //    let pointSize = m.radius |> AVal.map (fun r -> r * 0.1)
    //    Sg.drawPointList m.verticesSurf1 
    //                     (C4b.Red |> AVal.constant) 
    //                     pointSize
    //                     (0.0 |> AVal.constant)
    //        |> Sg.andAlso 
    //            (Sg.drawPointList m.verticesSurf2
    //            (C4b.Blue |> AVal.constant) 
    //            pointSize
    //            (0.0 |> AVal.constant))

        //(C4b (C3b.VRVisGreen, ) |> AVal.constant)

    let sgSphere (m : AdaptiveAreaSelection) =  
        let createSg visible (stats : AdaptiveOptionCase<'a,'b,'b>) =
            if visible && (AdaptiveOption.toOption stats).IsNone then 
              Sg.sphere 12 areaSelectionColor m.radius
                |> Sg.trafo (m.location |> AVal.map (fun x -> Trafo3d.Translation x))
            else Sg.empty

        (AVal.map2 createSg m.visible m.statistics)
            |> Sg.dynamic

        //m.visible 
        //  |> AVal.map (fun v -> if v then 
        //                          Sg.sphere 12 areaSelectionColor m.radius
        //                            |> Sg.trafo (m.location |> AVal.map (fun x -> Trafo3d.Translation x))
        //                        else Sg.empty) |> Sg.dynamic


    let sgAllAreas (areas : amap<System.Guid, AdaptiveAreaSelection>) =
        areas
            |> AMap.toASet
            |> ASet.map (fun (g,x) -> sgSphere x)
            |> Sg.set
            |> Sg.noEvents
            |> Sg.blendMode (AVal.init BlendMode.Blend)
            |> Sg.effect [     
                Aardvark.UI.Trafos.Shader.stableTrafo |> toEffect 
                DefaultSurfaces.vertexColor |> toEffect
            ] 

    //let sgAllPoints (areas : amap<System.Guid, AdaptiveAreaSelection>) =
    //    areas
    //      |> AMap.toASet
    //      |> ASet.map (fun (g,x) -> sgPoints x)
    //      |> Sg.set
    //      |> Sg.noEvents
    //      |> Sg.effect [     
    //          Aardvark.UI.Trafos.Shader.stableTrafo |> toEffect 
    //          DefaultSurfaces.vertexColor |> toEffect
    //      ] 





    //let sg (m : AdaptiveAreaSelection) =
    //    let createAndTransform (center : V3d) size (rotation : Trafo3d) =
    //        let box = Box3d.FromCenterAndSize (V3d.OOO,size)
    //        let trafo = rotation * (Trafo3d.Translation center)
    //        box.Transformed trafo
            

    //    let box = AVal.map3 createAndTransform
    //                        m.location m.dimensions m.rotation
        
    //    Sg.wireBox (C4b.VRVisGreen |> AVal.constant) box
    //      |> Sg.andAlso (Sg.drawPointList m.selectedVertices 
    //                                     (C4b.Red |> AVal.constant) 
    //                                     (4.0 |> AVal.constant) 
    //                                     (0.0 |> AVal.constant))


    let view (m : AdaptiveAreaSelection) =
        let radius = m.radius |> AVal.map (fun x -> sprintf "%f" x)
        let location  = m.location |> AVal.map (fun v -> sprintf "%s" (v.ToString ()))
        let getStatsGui (stats : aval<AdaptiveOptionCase<VertexStatistics, AdaptiveVertexStatistics, AdaptiveVertexStatistics>>) =
            let content = 
                alist {
                    let! stats = stats
                    let rows = 
                        match stats with
                        | AdaptiveSome stats ->
                            let minDistance = stats.minDistance |> AVal.map (fun x -> sprintf "%f" x)
                            let avgDistance = stats.avgDistance |> AVal.map (fun x -> sprintf "%f" x)
                            let maxDistance = stats.maxDistance |> AVal.map (fun x -> sprintf "%f" x)
                    
                            [
                              Html.row "Statistical parameter" [div[style "font-weight: bold"] 
                                                               [text "value"]]
                              Html.row "Minimum distance" [Incremental.text minDistance]
                              Html.row "Average distance" [Incremental.text avgDistance]
                              Html.row "Maximum distance" [Incremental.text maxDistance]
                            ]
                    
                        | AdaptiveNone -> []
                    yield Html.table rows
                }
            Incremental.div ([] |> AttributeMap.ofList) content
        let statsGui = getStatsGui m.statistics
 //       require GuiEx.semui (
        div [] [
            Html.table ([      
              Html.row "Selected Area" [div[style "font-weight: bold"] 
                                       [Incremental.text m.label]]
              Html.row "Area radius"   [Incremental.text radius]
              Html.row "Area location" [Incremental.text location]
            ])
            statsGui
        ]
 //       )      
          



