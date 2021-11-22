namespace PRo3D.Shading

open Adaptify
open Aardvark.UI
open Chiron
open PRo3D.Base

#nowarn "0686"

type ShadowProjection =
    | Orthographic = 0
    | Perspective = 1
    | Debug       = 2

[<ModelType>]
type ShadingApp = {
    version                 : int
    useLightLocation        : bool
    lightLocation           : V3dInput 
    lightDirection          : V3dInput
    normalizeLightDirection : bool
    lightDistance           : NumericInput
    useLighting             : bool
    useShadows              : bool
    useMask                 : bool
    ambient                 : NumericInput
    ambientShadow           : NumericInput
    shadowFrustum           : NumericInput
    shadowProjection        : ShadowProjection
    debug                   : bool
    showShadowMap           : bool
} with
    static member read0 = 
        json {
            let! lightLocation                    = Json.readWith Ext.fromJson<V3dInput,Ext> "lightLocation"
            let! lightDirection                   = Json.readWith Ext.fromJson<V3dInput,Ext> "lightDirection"
            let! (normalizeLightDirection : bool) = Json.read "normalizeLightDirection"       
            let! lightDistance                    = Json.readWith Ext.fromJson<NumericInput,Ext> "lightDistance"
            let! (useLighting : bool)             = Json.read "useLighting"           
            let! (useLightLocation : bool)        = Json.read "useLightLocation"           
            let! (useShadows : bool)              = Json.read "useShadows"           
            let! (useMask : bool)                 = Json.read "useMask"           
            let! ambient                          = Json.readWith Ext.fromJson<NumericInput,Ext> "ambient"
            let! (debug : bool)                   = Json.read "debug"           
            let! (showShadowMap : bool)           = Json.read "showShadowMap"           
            let! ambientShadow                    = Json.readWith Ext.fromJson<NumericInput,Ext> "ambientShadow"
            let! shadowFrustum                    = Json.readWith Ext.fromJson<NumericInput,Ext> "shadowFrustum"
            let! shadowProjection                 = Json.read "shadowProjection"
            //let! fieldOfView  = Json.readWith Ext.fromJson<NumericInput,Ext> "fieldOfView"
            return {  
                version         = 0
                lightLocation   = lightLocation
                lightDirection = lightDirection
                normalizeLightDirection = normalizeLightDirection
                lightDistance    = lightDistance
                useLightLocation = useLightLocation
                useLighting      = useLighting
                useShadows       = useShadows
                useMask          = useMask
                ambient          = ambient
                ambientShadow    = ambientShadow
                shadowFrustum    = shadowFrustum
                debug            = debug
                showShadowMap    = showShadowMap
                shadowProjection = shadowProjection |> enum<ShadowProjection>
            }
        } 
    static member FromJson(_ : ShadingApp) = //WIP rno
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ShadingApp.read0
            | _ -> return! v |> sprintf "don't know version %A  of ViewConfigModel" |> Json.error
        }
    static member ToJson (x : ShadingApp) =
        json {                    
            do! Json.writeWith Ext.toJson<V3dInput,Ext> "lightLocation"       x.lightLocation
            do! Json.writeWith Ext.toJson<V3dInput,Ext> "lightDirection"      x.lightDirection
            do! Json.write "normalizeLightDirection"                          x.normalizeLightDirection
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "lightDistance"   x.lightDistance
            do! Json.write "useLighting"                                      x.useLighting
            do! Json.write "useLightLocation"                                 x.useLightLocation
            do! Json.write "useShadows"                                       x.useShadows
            do! Json.write "useMask"                                          x.useMask
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "ambient"         x.ambient
            do! Json.write "debug"                                            x.debug
            do! Json.write "showShadowMap"                                    x.showShadowMap
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "ambientShadow"   x.ambientShadow
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "shadowFrustum"   x.shadowFrustum
            do! Json.write "shadowProjection"                                 (x.shadowProjection |> int)
            do! Json.write "version"                                          x.version
        }

