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



type AreaSelectionAction =
    | SetRadius of float
    | SetLocation of V3d
    | ToggleVisible
    | UpdateStatistics
    | MakeBigger
    | MakeSmaller
    | Nop


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AreaSelection =
    let areaSelectionColor =
        C4b (150uy,255uy,150uy,100uy) 
          |> AVal.constant 
    let pointSize = 7.0
    let areaSizeChangeFactor = 0.1

    let init guid : AreaSelection = 
        {
            id          = guid
            radius      = 8.0
            location    = V3d.OOO
            rotation    = Trafo3d.Identity
            visible     = true
            selectedVertices = IndexList.empty
            statistics  = None
        }



    let updateAreaStatistic  (surfaceModel : SurfaceModel) 
                              (refSystem    : ReferenceSystem) 
                              (area         : AreaSelection)
                              (surfaceName  : string) =
        let surfaceId = ComparisonUtils.findSurfaceByName surfaceModel surfaceName
        match surfaceId with
        | Some surfaceId ->
            let surface = surfaceModel.surfaces.flat |> HashMap.find surfaceId |> Leaf.toSurface
            let sgSurface = surfaceModel.sgSurfaces |> HashMap.find surfaceId
  
            Some (AreaComparison.calculateStatistics surface sgSurface refSystem area )
        | None -> None

    let update (m : AreaSelection) (action : AreaSelectionAction) =
        match action with
        | MakeBigger -> 
            {m with radius = m.radius * (1.0 + areaSizeChangeFactor)}
        | MakeSmaller -> 
            {m with radius = m.radius * (1.0 - areaSizeChangeFactor)}
        | SetRadius r -> {m with radius = r}
        | SetLocation location -> m
        | ToggleVisible ->
            {m with visible = not m.visible}
        | UpdateStatistics -> 
            m
        | Nop -> m

    let sgPoints (m : AdaptiveAreaSelection) = 
        Sg.drawPointList m.selectedVertices 
                         (C4b.Red |> AVal.constant) 
                         (pointSize |> AVal.constant) 
                         (0.0 |> AVal.constant)


        //(C4b (C3b.VRVisGreen, ) |> AVal.constant)

    let sgSphere (m : AdaptiveAreaSelection) =  
        Sg.sphere 12 areaSelectionColor m.radius
          |> Sg.trafo (m.location |> AVal.map (fun x -> Trafo3d.Translation x))


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

    let sgAllPoints (areas : amap<System.Guid, AdaptiveAreaSelection>) =
        areas
          |> AMap.toASet
          |> ASet.map (fun (g,x) -> sgPoints x)
          |> Sg.set
          |> Sg.noEvents
          |> Sg.effect [     
              Aardvark.UI.Trafos.Shader.stableTrafo |> toEffect 
              DefaultSurfaces.vertexColor |> toEffect
          ] 

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
        require GuiEx.semui (
          Html.table [      
            Html.row "Area size X"   [Incremental.text radius]
            Html.row "Area Location" [Incremental.text location]
        ])      
          



