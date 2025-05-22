//61934f3c-641f-582e-427b-774528827c7e
//eb968a52-4bf6-313b-884b-e6e9c00f3a22
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Shading

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Shading
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveShadingApp(value : ShadingApp) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _useLightLocation_ = FSharp.Data.Adaptive.cval(value.useLightLocation)
    let _lightLocation_ = Aardvark.UI.Primitives.AdaptiveV3dInput(value.lightLocation)
    let _lightDirection_ = Aardvark.UI.Primitives.AdaptiveV3dInput(value.lightDirection)
    let _normalizeLightDirection_ = FSharp.Data.Adaptive.cval(value.normalizeLightDirection)
    let _lightDistance_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.lightDistance)
    let _useLighting_ = FSharp.Data.Adaptive.cval(value.useLighting)
    let _useShadows_ = FSharp.Data.Adaptive.cval(value.useShadows)
    let _useMask_ = FSharp.Data.Adaptive.cval(value.useMask)
    let _ambient_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.ambient)
    let _ambientShadow_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.ambientShadow)
    let _shadowFrustum_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.shadowFrustum)
    let _shadowProjection_ = FSharp.Data.Adaptive.cval(value.shadowProjection)
    let _debug_ = FSharp.Data.Adaptive.cval(value.debug)
    let _showShadowMap_ = FSharp.Data.Adaptive.cval(value.showShadowMap)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ShadingApp) = AdaptiveShadingApp(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ShadingApp) -> AdaptiveShadingApp(value)) (fun (adaptive : AdaptiveShadingApp) (value : ShadingApp) -> adaptive.Update(value))
    member __.Update(value : ShadingApp) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ShadingApp>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _useLightLocation_.Value <- value.useLightLocation
            _lightLocation_.Update(value.lightLocation)
            _lightDirection_.Update(value.lightDirection)
            _normalizeLightDirection_.Value <- value.normalizeLightDirection
            _lightDistance_.Update(value.lightDistance)
            _useLighting_.Value <- value.useLighting
            _useShadows_.Value <- value.useShadows
            _useMask_.Value <- value.useMask
            _ambient_.Update(value.ambient)
            _ambientShadow_.Update(value.ambientShadow)
            _shadowFrustum_.Update(value.shadowFrustum)
            _shadowProjection_.Value <- value.shadowProjection
            _debug_.Value <- value.debug
            _showShadowMap_.Value <- value.showShadowMap
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.useLightLocation = _useLightLocation_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lightLocation = _lightLocation_
    member __.lightDirection = _lightDirection_
    member __.normalizeLightDirection = _normalizeLightDirection_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lightDistance = _lightDistance_
    member __.useLighting = _useLighting_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.useShadows = _useShadows_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.useMask = _useMask_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.ambient = _ambient_
    member __.ambientShadow = _ambientShadow_
    member __.shadowFrustum = _shadowFrustum_
    member __.shadowProjection = _shadowProjection_ :> FSharp.Data.Adaptive.aval<ShadowProjection>
    member __.debug = _debug_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showShadowMap = _showShadowMap_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ShadingAppLenses = 
    type ShadingApp with
        static member version_ = ((fun (self : ShadingApp) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ShadingApp) -> { self with version = value }))
        static member useLightLocation_ = ((fun (self : ShadingApp) -> self.useLightLocation), (fun (value : Microsoft.FSharp.Core.bool) (self : ShadingApp) -> { self with useLightLocation = value }))
        static member lightLocation_ = ((fun (self : ShadingApp) -> self.lightLocation), (fun (value : Aardvark.UI.Primitives.V3dInput) (self : ShadingApp) -> { self with lightLocation = value }))
        static member lightDirection_ = ((fun (self : ShadingApp) -> self.lightDirection), (fun (value : Aardvark.UI.Primitives.V3dInput) (self : ShadingApp) -> { self with lightDirection = value }))
        static member normalizeLightDirection_ = ((fun (self : ShadingApp) -> self.normalizeLightDirection), (fun (value : Microsoft.FSharp.Core.bool) (self : ShadingApp) -> { self with normalizeLightDirection = value }))
        static member lightDistance_ = ((fun (self : ShadingApp) -> self.lightDistance), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ShadingApp) -> { self with lightDistance = value }))
        static member useLighting_ = ((fun (self : ShadingApp) -> self.useLighting), (fun (value : Microsoft.FSharp.Core.bool) (self : ShadingApp) -> { self with useLighting = value }))
        static member useShadows_ = ((fun (self : ShadingApp) -> self.useShadows), (fun (value : Microsoft.FSharp.Core.bool) (self : ShadingApp) -> { self with useShadows = value }))
        static member useMask_ = ((fun (self : ShadingApp) -> self.useMask), (fun (value : Microsoft.FSharp.Core.bool) (self : ShadingApp) -> { self with useMask = value }))
        static member ambient_ = ((fun (self : ShadingApp) -> self.ambient), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ShadingApp) -> { self with ambient = value }))
        static member ambientShadow_ = ((fun (self : ShadingApp) -> self.ambientShadow), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ShadingApp) -> { self with ambientShadow = value }))
        static member shadowFrustum_ = ((fun (self : ShadingApp) -> self.shadowFrustum), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ShadingApp) -> { self with shadowFrustum = value }))
        static member shadowProjection_ = ((fun (self : ShadingApp) -> self.shadowProjection), (fun (value : ShadowProjection) (self : ShadingApp) -> { self with shadowProjection = value }))
        static member debug_ = ((fun (self : ShadingApp) -> self.debug), (fun (value : Microsoft.FSharp.Core.bool) (self : ShadingApp) -> { self with debug = value }))
        static member showShadowMap_ = ((fun (self : ShadingApp) -> self.showShadowMap), (fun (value : Microsoft.FSharp.Core.bool) (self : ShadingApp) -> { self with showShadowMap = value }))

