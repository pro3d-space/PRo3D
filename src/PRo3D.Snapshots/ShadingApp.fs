namespace PRo3D.Shading
open System
open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering.Text
open System.Collections.Concurrent
open System.Runtime.Serialization
open PRo3D.Base
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Core.Surface
open PRo3D.SimulatedViews
open Chiron

type ShadingAction =
    | SetLightDirectionV3d of V3d
    | SetLightDirection    of Vector3d.Action
    | ToggleUseDirection
    | SetLightDistance     of  Numeric.Action
    | SetLightPositionV3d of V3d
    | SetLightLocation    of Vector3d.Action
    | ToggleLighting
    | ToggleDebug
    | ToggleUseLocation
    | ToggleUseMask
    | SetAmbient          of Numeric.Action
    | SetAmbientShadow    of Numeric.Action
    | SetShadowFrustum    of Numeric.Action
    | SetUseLighting      of bool
    | SetShadowProjection of ShadowProjection
    | SetDebug            of bool
    | ToggleShowShadowMap
    | NoAction

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ShadingApp =
    let currentVersion = 0

    let init : ShadingApp = 
       let coordinate = {
            value   = float 0.0
            min     = float -10000000.0
            max     = float  10000000.0
            step    = float 0.1
            format  = "{0:0.0}"
       }

       let ambientRange = {
            value   = float 0.0
            min     = float 0.0
            max     = float 0.3
            step    = float 0.01
            format  = "{0:0.00}"       
       }

       let frustumRange = {
            value   = float 1.0
            min     = float 0.1
            max     = float 100.0
            step    = float 0.1
            format  = "{0:0.0}"       
       }

       let minusOneToOne  = {
            value   = float 1.0
            min     = float -1.0
            max     = float 1.0
            step    = float 0.1
            format  = "{0:0.0}"       
       }

       let lightDist = {
            value   = float 1.0
            min     = float 0.1
            max     = float 10.0
            step    = float 0.1
            format  = "{0:0.0}"       
       }
       
       {  
            version         = currentVersion
            lightLocation   =  {
                                    x = coordinate
                                    y = coordinate
                                    z = coordinate
                                    value = V3d(0.0)
                                }
            lightDirection = {
                                    x = {minusOneToOne with value = 0.1842}
                                    y = {minusOneToOne with value = -0.93675}
                                    z = {minusOneToOne with value = -0.29759}
                                    value = V3d(0.1842,-0.93675,-0.29759).Normalized
                             }
            normalizeLightDirection = true
            lightDistance    = lightDist
            useLightLocation = false
            useLighting      = true
            useShadows       = true
            useMask          = false
            ambient          = ambientRange
            ambientShadow    = {ambientRange with value = 0.2}
            shadowFrustum    = frustumRange
            debug            = false
            showShadowMap    = false
            shadowProjection = ShadowProjection.Othographic
       } 
    
    let update (model : ShadingApp) (message : ShadingAction) =
        match message with
        | SetLightLocation vectorAction ->
            let lightLocation = (Vector3d.update model.lightLocation vectorAction)
            {model with lightLocation = Vector3d.updateV3d lightLocation (lightLocation.value.Normalized)}
        | SetLightPositionV3d v ->
            let loc = Vector3d.updateV3d model.lightLocation (v.Normalized)
            {model with lightLocation = loc}
        | SetLightDirection vectorAction ->
            let direction = Vector3d.update model.lightDirection vectorAction
            let direction =
                match model.normalizeLightDirection with
                | true ->
                    let v = direction.value.Normalized
                    Vector3d.updateV3d direction v
                | false -> direction
            {model with lightDirection = direction}
        | ToggleUseDirection ->
            let lightDirection = 
                match model.normalizeLightDirection with
                | false -> model.lightDirection.value.Normalized
                | true -> model.lightDirection.value
            {model with normalizeLightDirection = not model.normalizeLightDirection
                        lightDirection    = Vector3d.updateV3d model.lightDirection lightDirection}
        | SetLightDirectionV3d v ->
            let v = v.Normalized
            let direction = Vector3d.updateV3d model.lightDirection v
            {model with lightDirection = direction}
        | SetLightDistance numericAction -> 
            let lightDistance = Numeric.update model.lightDistance numericAction
            {model with lightDistance = lightDistance}        
        | ToggleLighting -> 
            {model with useLighting = not model.useLighting}
        | ToggleDebug -> 
            {model with debug = not model.debug}
        | ToggleUseLocation -> 
            {model with useLightLocation = not model.useLightLocation}
        | ToggleUseMask -> 
            {model with useMask = not model.useMask}
        | SetUseLighting b -> 
            {model with useLighting = b}
        | SetDebug b -> 
            {model with debug = b}
        | ToggleShowShadowMap -> 
            {model with showShadowMap = not model.showShadowMap}
        | SetAmbient numericAction -> 
            let ambient = Numeric.update model.ambient numericAction
            {model with ambient = ambient}     
        | SetAmbientShadow numericAction -> 
            let ambientShadow = Numeric.update model.ambientShadow numericAction
            {model with ambientShadow = ambientShadow}                  
        | SetShadowFrustum numericAction -> 
            let shadowFrustum = Numeric.update model.shadowFrustum numericAction
            Log.line "[Viewer] New Shadow Frustum Size."
            {model with shadowFrustum = shadowFrustum}                       
        | SetShadowProjection proj ->
            Log.line "Shadow Projection set to %s" (proj.ToString ())
            {model with shadowProjection = proj}
        | NoAction -> model

    let view (model : AdaptiveShadingApp) =
       require Html.semui (
            Html.table [      
                Html.row "Shading Settings" []
                Html.row "Light Direction:"
                    [
                        div [] [text "Normalize Direction ";(Html.SemUi.iconCheckBox model.normalizeLightDirection ShadingAction.ToggleUseDirection)]
                        (Aardvark.UI.Vector3d.view model.lightDirection) 
                            |> UI.map ShadingAction.SetLightDirection
                    ]
                //Html.row "Light Distance: "               
                //    [
                //        Numeric.view' [InputBox] model.lightDistance
                //          |> UI.map SetLightDistance
                //        Numeric.view' [Slider] model.lightDistance
                //          |> UI.map SetLightDistance
                //    ]
                //Html.row "Use Light Location: "
                //    [
                //        (PRo3D.GuiEx.iconCheckBox model.useLightLocation ShadingActions.ToggleUseLocation)
                //    ]
                //Html.row "Light Location: "
                //    [
                //        (Aardvark.UI.Vector3d.view model.lightLocation) 
                //            |> UI.map ShadingActions.SetLightLocation
                //    ]
                Html.row "Use Mask: "
                    [
                        (Html.SemUi.iconCheckBox model.useMask ShadingAction.ToggleUseMask)
                    ]
                Html.row "Use Lighting: "
                    [
                        (Html.SemUi.iconCheckBox model.useLighting ShadingAction.ToggleLighting)
                    ]

                //Html.row "Ambient Light: "               
                //    [
                //        Numeric.view' [InputBox] model.ambient
                //          |> UI.map SetAmbient
                //        Numeric.view' [Slider] model.ambient
                //          |> UI.map SetAmbient
                //    ]
                Html.row "Shadow Intensity: "               
                    [
                        Numeric.view' [InputBox] model.ambientShadow
                          |> UI.map SetAmbientShadow
                        Numeric.view' [Slider] model.ambientShadow
                          |> UI.map SetAmbientShadow
                    ]
                //Html.row "Shadow Frustum: "               
                //    [
                //        Numeric.view' [InputBox] model.shadowFrustum
                //          |> UI.map SetShadowFrustum
                //        (Numeric.view' [Slider] model.shadowFrustum)
                //          |> UI.map SetShadowFrustum
                //    ]
                Html.row "Display Debug Objects: "
                    [
                        (Html.SemUi.iconCheckBox model.debug ShadingAction.ToggleDebug)
                    ]
                //Html.row "Set Shadow Projection: "
                //    [
                //        Html.Layout.boxH [ Html.SemUi.dropDown model.shadowProjection SetShadowProjection ]   
                //    ]
                Html.row "Show Light View: "
                    [
                        (Html.SemUi.iconCheckBox model.showShadowMap ShadingAction.ToggleShowShadowMap)
                    ]
                ]
            )

 