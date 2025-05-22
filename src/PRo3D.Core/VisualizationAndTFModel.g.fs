//9e80936a-418a-ed9e-a38b-897e7c24a266
//1ec0ff25-6ebc-474b-9f80-6e0dcdcbdfc5
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
type AdaptiveScalarLayer(value : ScalarLayer) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _label_ = FSharp.Data.Adaptive.cval(value.label)
    let _actualRange_ = FSharp.Data.Adaptive.cval(value.actualRange)
    let _definedRange_ = FSharp.Data.Adaptive.cval(value.definedRange)
    let _index_ = FSharp.Data.Adaptive.cval(value.index)
    let _colorLegend_ = PRo3D.Base.AdaptiveFalseColorsModel(value.colorLegend)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ScalarLayer) = AdaptiveScalarLayer(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ScalarLayer) -> AdaptiveScalarLayer(value)) (fun (adaptive : AdaptiveScalarLayer) (value : ScalarLayer) -> adaptive.Update(value))
    member __.Update(value : ScalarLayer) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ScalarLayer>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _label_.Value <- value.label
            _actualRange_.Value <- value.actualRange
            _definedRange_.Value <- value.definedRange
            _index_.Value <- value.index
            _colorLegend_.Update(value.colorLegend)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.label = _label_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.actualRange = _actualRange_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Range1d>
    member __.definedRange = _definedRange_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Range1d>
    member __.index = _index_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.colorLegend = _colorLegend_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ScalarLayerLenses = 
    type ScalarLayer with
        static member version_ = ((fun (self : ScalarLayer) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ScalarLayer) -> { self with version = value }))
        static member label_ = ((fun (self : ScalarLayer) -> self.label), (fun (value : Microsoft.FSharp.Core.string) (self : ScalarLayer) -> { self with label = value }))
        static member actualRange_ = ((fun (self : ScalarLayer) -> self.actualRange), (fun (value : Aardvark.Base.Range1d) (self : ScalarLayer) -> { self with actualRange = value }))
        static member definedRange_ = ((fun (self : ScalarLayer) -> self.definedRange), (fun (value : Aardvark.Base.Range1d) (self : ScalarLayer) -> { self with definedRange = value }))
        static member index_ = ((fun (self : ScalarLayer) -> self.index), (fun (value : Microsoft.FSharp.Core.int) (self : ScalarLayer) -> { self with index = value }))
        static member colorLegend_ = ((fun (self : ScalarLayer) -> self.colorLegend), (fun (value : PRo3D.Base.FalseColorsModel) (self : ScalarLayer) -> { self with colorLegend = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveContourLineModel(value : ContourLineModel) =
    let _enabled_ = FSharp.Data.Adaptive.cval(value.enabled)
    let _targetLayer_ = FSharp.Data.Adaptive.cval(value.targetLayer)
    let _distance_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.distance)
    let _width_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.width)
    let _border_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.border)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ContourLineModel) = AdaptiveContourLineModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ContourLineModel) -> AdaptiveContourLineModel(value)) (fun (adaptive : AdaptiveContourLineModel) (value : ContourLineModel) -> adaptive.Update(value))
    member __.Update(value : ContourLineModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ContourLineModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _enabled_.Value <- value.enabled
            _targetLayer_.Value <- value.targetLayer
            _distance_.Update(value.distance)
            _width_.Update(value.width)
            _border_.Update(value.border)
    member __.Current = __adaptive
    member __.enabled = _enabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.targetLayer = _targetLayer_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<TextureLayer>>
    member __.distance = _distance_
    member __.width = _width_
    member __.border = _border_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ContourLineModelLenses = 
    type ContourLineModel with
        static member enabled_ = ((fun (self : ContourLineModel) -> self.enabled), (fun (value : Microsoft.FSharp.Core.bool) (self : ContourLineModel) -> { self with enabled = value }))
        static member targetLayer_ = ((fun (self : ContourLineModel) -> self.targetLayer), (fun (value : Microsoft.FSharp.Core.Option<TextureLayer>) (self : ContourLineModel) -> { self with targetLayer = value }))
        static member distance_ = ((fun (self : ContourLineModel) -> self.distance), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ContourLineModel) -> { self with distance = value }))
        static member width_ = ((fun (self : ContourLineModel) -> self.width), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ContourLineModel) -> { self with width = value }))
        static member border_ = ((fun (self : ContourLineModel) -> self.border), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ContourLineModel) -> { self with border = value }))

