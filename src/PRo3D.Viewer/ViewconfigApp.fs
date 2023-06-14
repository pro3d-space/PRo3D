namespace PRo3D

open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core


module ConfigProperties =

    type Action =
        | Nop
        | SetNearPlane              of Numeric.Action
        | SetFarPlane               of Numeric.Action
        | SetNavigationSensitivity  of Numeric.Action
        | SetArrowLength            of Numeric.Action
        | SetImportTriangleSize     of Numeric.Action
        | SetArrowThickness         of Numeric.Action
        | SetDnSPlaneSize           of Numeric.Action
        | SetOffset                 of Numeric.Action
        | SetPickingTolerance       of Numeric.Action
        | ToggleLodColors
        | ToggleOrientationCube 
        | ToggleSurfaceHighlighting
        | ToggleExplorationPointGui
        

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
        | ToggleOrientationCube -> 
            { model with drawOrientationCube = not model.drawOrientationCube}
        | ToggleSurfaceHighlighting -> 
            model // {model with useSurfaceHighlighting = not model.useSurfaceHighlighting}
        | SetPickingTolerance tolerance ->
            { model with pickingTolerance = Numeric.update model.pickingTolerance tolerance }
        | ToggleExplorationPointGui -> {model with showExplorationPointGui = not model.showExplorationPointGui}
        | Nop -> model
        | _ -> 
            Log.warn "[ConfigProperties] Unknown action %A" act
            model
        
           

    let view (model : AdaptiveViewConfigModel) =    
        require GuiEx.semui (
            Html.table [      
                Html.row "Picking Tolerance:"       [Numeric.view' [InputBox] model.pickingTolerance      |> UI.map SetPickingTolerance ]
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
                Html.row "Exploration Point: "      [GuiEx.iconCheckBox model.showExplorationPointGui ToggleExplorationPointGui]
            ]
        )

module CameraProperties =
    let view (refSystem:AdaptiveReferenceSystem) (camera : AdaptiveCameraControllerState) =    
        let bearing = 
            adaptive {
                let! up = refSystem.up.value
                let! north = refSystem.northO //model.north.value 
                let! view = camera.view
                return Calculations.bearing up north view.Forward
            }

        let pitch = 
            adaptive {
                let! up = refSystem.up.value
                let! view = camera.view
                return Calculations.pitch up view.Forward
            }

        require GuiEx.semui (
            Html.table [      
                Html.row "Location:"    [Incremental.text (camera.view |> AVal.map(fun x -> x.Location.ToString("0.00")))]
                Html.row "Forward:"     [Incremental.text (camera.view |> AVal.map(fun x -> x.Forward.ToString("0.000")))]
                Html.row "Sky:"         [Incremental.text (camera.view |> AVal.map(fun x -> x.Sky.ToString("0.000")))]
                Html.row "Bearing:"     [Incremental.text (bearing |> AVal.map (fun x -> x.ToString("0.00")))] // compute azimuth with view dir, north vector and up vector
                Html.row "Pitch:"       [Incremental.text (pitch |> AVal.map (fun x -> x.ToString("0.00")))]  // same for pitch which relates to dip angle
            ]
        )

module FrustumProperties =

    type Action =
         | UpdateFocal           of Numeric.Action
         | ToggleUseFocal
     
    
    let updateFrustum (focal : float) (near : float) (far: float) =
        // http://paulbourke.net/miscellaneous/lens/
        // https://photo.stackexchange.com/questions/41273/how-to-calculate-the-fov-in-degrees-from-focal-length-or-distance
        let hfov = 2.0 * atan(11.84 /(focal*2.0))
        Frustum.perspective (hfov.DegreesFromRadians()) near far 1.0

    let update (model : FrustumModel) (act : Action) =
        match act with
        | ToggleUseFocal ->
            { model with toggleFocal = not model.toggleFocal}
        | UpdateFocal f ->
            { model with focal = Numeric.update model.focal f }
        

    let view (model : AdaptiveFrustumModel) =    
        require GuiEx.semui (
            Html.table [  
                //Html.row "use Focal:"   [GuiEx.iconCheckBox model.toggleFocal ToggleUseFocal] // disabled because of https://github.com/pro3d-space/PRo3D/issues/261
                Html.row "Focal (mm):"  [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox] model.focal |> UI.map UpdateFocal ]  

            ]
        )
    
