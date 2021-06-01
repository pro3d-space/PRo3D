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
    | SetDimensions of V3d
    | SetLocation of V3d
    | ToggleVisible
    | UpdateStatistics
    | MakeBigger
    | MakeSmaller
    | Nop


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AreaSelection =
    let init guid : AreaSelection = 
        {
            id          = guid
            dimensions  = V3d.III * 3.0
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
        | MakeBigger -> {m with dimensions = m.dimensions * 1.1}
        | MakeSmaller -> {m with dimensions = m.dimensions * 0.9}
        | SetDimensions v -> {m with dimensions = v}
        | SetLocation location -> m
        | ToggleVisible ->
            {m with visible = not m.visible}
        | UpdateStatistics -> 
            m
        | Nop -> m


    let sg (m : AdaptiveAreaSelection) =
        let createAndTransform center size (rotation : Trafo3d) =
            let box = Box3d.FromCenterAndSize (center,size)
            box.Transformed rotation

        let box = AVal.map3 createAndTransform
                            m.location m.dimensions m.rotation
        
        Sg.wireBox (C4b.VRVisGreen |> AVal.constant) box
          |> Sg.andAlso (Sg.drawPointList m.selectedVertices 
                                         (C4b.Red |> AVal.constant) 
                                         (4.0 |> AVal.constant) 
                                         (0.0 |> AVal.constant))

    let view (m : AdaptiveAreaSelection) =
        let xSize = m.dimensions |> AVal.map (fun v -> sprintf "%f" v.X)
        let ySize = m.dimensions |> AVal.map (fun v -> sprintf "%f" v.Y)
        let zSize = m.dimensions |> AVal.map (fun v -> sprintf "%f" v.Z)
        let location  = m.location |> AVal.map (fun v -> sprintf "%s" (v.ToString ()))
        require GuiEx.semui (
          Html.table [      
            Html.row "Area size X" [Incremental.text xSize]
            Html.row "Area size Y" [Incremental.text ySize]
            Html.row "Area size Z" [Incremental.text zSize]
            Html.row "Area Location" [Incremental.text location]
        ])      
          



