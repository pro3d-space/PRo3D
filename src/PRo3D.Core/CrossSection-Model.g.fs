//f3b86bca-b862-27b1-3de7-d7dc86ebfe10
//050dad67-7b63-a06a-a78d-ee8e9d56e0e2
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Core

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Core
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveCrossSectionModel(value : CrossSectionModel) =
    let _crossSection_ = FSharp.Data.Adaptive.cval(value.crossSection)
    let _curtainEnabled_ = FSharp.Data.Adaptive.cval(value.curtainEnabled)
    let _curtainTexturePath_ = FSharp.Data.Adaptive.cval(value.curtainTexturePath)
    let _curtainExtrusionDepth_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.curtainExtrusionDepth)
    let _curtainAbsoluteMode_ = FSharp.Data.Adaptive.cval(value.curtainAbsoluteMode)
    let _curtainTargetAltitude_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.curtainTargetAltitude)
    let _curtainTextureDepth_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.curtainTextureDepth)
    let _curtainTextureStartAltitude_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.curtainTextureStartAltitude)
    let _curtainBaseColor_ = Aardvark.UI.AdaptiveColorInput(value.curtainBaseColor)
    let _clippingEnabled_ = FSharp.Data.Adaptive.cval(value.clippingEnabled)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : CrossSectionModel) = AdaptiveCrossSectionModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : CrossSectionModel) -> AdaptiveCrossSectionModel(value)) (fun (adaptive : AdaptiveCrossSectionModel) (value : CrossSectionModel) -> adaptive.Update(value))
    member __.Update(value : CrossSectionModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<CrossSectionModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _crossSection_.Value <- value.crossSection
            _curtainEnabled_.Value <- value.curtainEnabled
            _curtainTexturePath_.Value <- value.curtainTexturePath
            _curtainExtrusionDepth_.Update(value.curtainExtrusionDepth)
            _curtainAbsoluteMode_.Value <- value.curtainAbsoluteMode
            _curtainTargetAltitude_.Update(value.curtainTargetAltitude)
            _curtainTextureDepth_.Update(value.curtainTextureDepth)
            _curtainTextureStartAltitude_.Update(value.curtainTextureStartAltitude)
            _curtainBaseColor_.Update(value.curtainBaseColor)
            _clippingEnabled_.Value <- value.clippingEnabled
    member __.Current = __adaptive
    member __.crossSection = _crossSection_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<CrossSection>>
    member __.curtainEnabled = _curtainEnabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.curtainTexturePath = _curtainTexturePath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>>
    member __.curtainExtrusionDepth = _curtainExtrusionDepth_
    member __.curtainAbsoluteMode = _curtainAbsoluteMode_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.curtainTargetAltitude = _curtainTargetAltitude_
    member __.curtainTextureDepth = _curtainTextureDepth_
    member __.curtainTextureStartAltitude = _curtainTextureStartAltitude_
    member __.curtainBaseColor = _curtainBaseColor_
    member __.clippingEnabled = _clippingEnabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module CrossSectionModelLenses = 
    type CrossSectionModel with
        static member crossSection_ = ((fun (self : CrossSectionModel) -> self.crossSection), (fun (value : Microsoft.FSharp.Core.Option<CrossSection>) (self : CrossSectionModel) -> { self with crossSection = value }))
        static member curtainEnabled_ = ((fun (self : CrossSectionModel) -> self.curtainEnabled), (fun (value : Microsoft.FSharp.Core.bool) (self : CrossSectionModel) -> { self with curtainEnabled = value }))
        static member curtainTexturePath_ = ((fun (self : CrossSectionModel) -> self.curtainTexturePath), (fun (value : Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>) (self : CrossSectionModel) -> { self with curtainTexturePath = value }))
        static member curtainExtrusionDepth_ = ((fun (self : CrossSectionModel) -> self.curtainExtrusionDepth), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : CrossSectionModel) -> { self with curtainExtrusionDepth = value }))
        static member curtainAbsoluteMode_ = ((fun (self : CrossSectionModel) -> self.curtainAbsoluteMode), (fun (value : Microsoft.FSharp.Core.bool) (self : CrossSectionModel) -> { self with curtainAbsoluteMode = value }))
        static member curtainTargetAltitude_ = ((fun (self : CrossSectionModel) -> self.curtainTargetAltitude), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : CrossSectionModel) -> { self with curtainTargetAltitude = value }))
        static member curtainTextureDepth_ = ((fun (self : CrossSectionModel) -> self.curtainTextureDepth), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : CrossSectionModel) -> { self with curtainTextureDepth = value }))
        static member curtainTextureStartAltitude_ = ((fun (self : CrossSectionModel) -> self.curtainTextureStartAltitude), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : CrossSectionModel) -> { self with curtainTextureStartAltitude = value }))
        static member curtainBaseColor_ = ((fun (self : CrossSectionModel) -> self.curtainBaseColor), (fun (value : Aardvark.UI.ColorInput) (self : CrossSectionModel) -> { self with curtainBaseColor = value }))
        static member clippingEnabled_ = ((fun (self : CrossSectionModel) -> self.clippingEnabled), (fun (value : Microsoft.FSharp.Core.bool) (self : CrossSectionModel) -> { self with clippingEnabled = value }))

