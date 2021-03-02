namespace PRo3D

open System
open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open PRo3D.Base

module ConfigProperties =

    type Action =
        | SetNearPlane              of Numeric.Action
        | SetFarPlane               of Numeric.Action
        | SetNavigationSensitivity  of Numeric.Action
        | SetArrowLength            of Numeric.Action
        | SetImportTriangleSize     of Numeric.Action
        | SetArrowThickness         of Numeric.Action
        | SetDnSPlaneSize           of Numeric.Action
        | SetOffset                 of Numeric.Action
        | ToggleLodColors
        | ToggleOrientationCube 
        | ToggleSurfaceHighlighting
        | ShadingMessage            of Shading.ShadingAction
        | SnapshotMessage           of SimulatedViews.SnapshotSettingsAction
        | ToggleExplorationPoint
        | ToggleFilterTexture
        

    let update (model : ViewConfigModel) (act : Action) =
        match act with
        | SetNearPlane s ->
            { model with nearPlane = Numeric.update model.nearPlane s }
        //| SetOffset s ->
        //    { model with offset = Numeric.update model.offset s }
        | SetFarPlane s ->
            { model with farPlane = Numeric.update model.farPlane s }
        | SetNavigationSensitivity s ->
            Log.warn "sense %A" s
            { model with navigationSensitivity = Numeric.update model.navigationSensitivity s }
        | SetArrowLength al ->
            { model with arrowLength = Numeric.update model.arrowLength al }
        | SetImportTriangleSize size ->
            { model with importTriangleSize = Numeric.update model.importTriangleSize size }
        | SetArrowThickness at ->
            { model with arrowThickness = Numeric.update model.arrowThickness at }
        | SetDnSPlaneSize s ->
            { model with dnsPlaneSize = Numeric.update model.dnsPlaneSize s }
        | ToggleLodColors ->
            { model with lodColoring = not model.lodColoring}
        | ToggleOrientationCube  -> {model with drawOrientationCube = not model.drawOrientationCube}
        | ToggleSurfaceHighlighting  -> model // {model with useSurfaceHighlighting = not model.useSurfaceHighlighting}
        | ShadingMessage message ->
            {model with shadingApp = Shading.ShadingApp.update model.shadingApp message}
        | SnapshotMessage message ->
            {model with snapshotSettings = SimulatedViews.ObjectPlacementApp.updateSnapshotSettings model.snapshotSettings message}
        | ToggleExplorationPoint -> {model with showExplorationPoint = not model.showExplorationPoint}
        | ToggleFilterTexture -> {model with filterTexture = not model.filterTexture}
        | _ -> 
            Log.warn "[ConfigProperties] Unknown action %A" act
            model
        
           

    let view (model : AdaptiveViewConfigModel) =    
        require GuiEx.semui (
            div [] [
                Html.table [      
                    Html.row "Near Plane:"              [Numeric.view' [InputBox] model.nearPlane             |> UI.map SetNearPlane ]               
                    Html.row "Far Plane:"               [Numeric.view' [InputBox] model.farPlane              |> UI.map SetFarPlane ]    
                    Html.row "Navigation Sensitivity:"  [Numeric.view' [Slider] model.navigationSensitivity   |> UI.map SetNavigationSensitivity ]    
                    Html.row "Import Triangle Size(m):" [Numeric.view' [InputBox] model.importTriangleSize    |> UI.map SetImportTriangleSize ] 
                    Html.row "Arrow Length:"            [Numeric.view' [InputBox] model.arrowLength           |> UI.map SetArrowLength ] 
                    Html.row "Arrow Thickness:"         [Numeric.view' [InputBox] model.arrowThickness        |> UI.map SetArrowThickness ]   
                    Html.row "D+S Plane Size:"          [Numeric.view' [InputBox] model.dnsPlaneSize          |> UI.map SetDnSPlaneSize ]   
                 //   Html.row "Offset:"                  [Numeric.view' [InputBox] model.offset          |> UI.map SetOffset ]   
                    Html.row "Lod colors:"              [GuiEx.iconCheckBox model.lodColoring ToggleLodColors]
                    Html.row "Orientation Cube: "       [GuiEx.iconCheckBox model.drawOrientationCube ToggleOrientationCube]
                  //  Html.row "Surface highlighting: "   [GuiEx.iconCheckBox model.useSurfaceHighlighting ToggleSurfaceHighlighting]
                    Html.row "Exploration Point: "      [GuiEx.iconCheckBox model.showExplorationPoint ToggleExplorationPoint]
                    Html.row "Filter Texture: "         [GuiEx.iconCheckBox model.filterTexture ToggleFilterTexture]
                ]
                (Shading.ShadingApp.view model.shadingApp) |> UI.map ShadingMessage
                (SimulatedViews.SnapshotSettings.view model.snapshotSettings) |> UI.map SnapshotMessage

            ]
            
        )

module CameraProperties =
    let view (model : AdaptiveCameraControllerState) =    
        require GuiEx.semui (
            Html.table [      
                Html.row "Location:"    [Incremental.text (model.view |> AVal.map(fun x -> x.Location.ToString("0.00")))]
                Html.row "Forward:"     [Incremental.text (model.view |> AVal.map(fun x -> x.Forward.ToString("0.000")))]
                Html.row "Sky:"         [Incremental.text (model.view |> AVal.map(fun x -> x.Sky.ToString("0.000")))]
            ]
        )
    
