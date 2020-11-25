namespace PRo3D.Shading

open Adaptify
open Aardvark.UI

type ShadowProjection =
    | Othographic = 0
    | Perspective = 1
    | Debug       = 2

[<ModelType>]
type ShadingProperties = {
    useLightLocation : bool
    lightLocation   : V3dInput 
    lightDirection  : V3dInput
    normalizeLightDirection : bool
    lightDistance   : NumericInput
    useLighting     : bool
    useShadows      : bool
    useMask         : bool
    ambient         : NumericInput
    ambientShadow   : NumericInput
    shadowFrustum   : NumericInput
    shadowProjection: ShadowProjection
    debug           : bool
    showShadowMap   : bool
}

