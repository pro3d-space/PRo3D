//e9b0ad77-1aff-2a9a-d329-0660b7178ebf
//7069481c-0139-0cc3-07de-bcc546e1e2e9
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
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
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
            _version_.Value <- value.version
            _enabled_.Value <- value.enabled
            _targetLayer_.Value <- value.targetLayer
            _distance_.Update(value.distance)
            _width_.Update(value.width)
            _border_.Update(value.border)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.enabled = _enabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.targetLayer = _targetLayer_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<TextureLayer>>
    member __.distance = _distance_
    member __.width = _width_
    member __.border = _border_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ContourLineModelLenses = 
    type ContourLineModel with
        static member version_ = ((fun (self : ContourLineModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ContourLineModel) -> { self with version = value }))
        static member enabled_ = ((fun (self : ContourLineModel) -> self.enabled), (fun (value : Microsoft.FSharp.Core.bool) (self : ContourLineModel) -> { self with enabled = value }))
        static member targetLayer_ = ((fun (self : ContourLineModel) -> self.targetLayer), (fun (value : Microsoft.FSharp.Core.Option<TextureLayer>) (self : ContourLineModel) -> { self with targetLayer = value }))
        static member distance_ = ((fun (self : ContourLineModel) -> self.distance), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ContourLineModel) -> { self with distance = value }))
        static member width_ = ((fun (self : ContourLineModel) -> self.width), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ContourLineModel) -> { self with width = value }))
        static member border_ = ((fun (self : ContourLineModel) -> self.border), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ContourLineModel) -> { self with border = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveLatLonShaderModel(value : LatLonShaderModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _enabled_ = FSharp.Data.Adaptive.cval(value.enabled)
    let _lat1_ = FSharp.Data.Adaptive.cval(value.lat1)
    let _lat5_ = FSharp.Data.Adaptive.cval(value.lat5)
    let _lat15_ = FSharp.Data.Adaptive.cval(value.lat15)
    let _lon1_ = FSharp.Data.Adaptive.cval(value.lon1)
    let _lon5_ = FSharp.Data.Adaptive.cval(value.lon5)
    let _lon15_ = FSharp.Data.Adaptive.cval(value.lon15)
    let _lineColor_ = Aardvark.UI.AdaptiveColorInput(value.lineColor)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : LatLonShaderModel) = AdaptiveLatLonShaderModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : LatLonShaderModel) -> AdaptiveLatLonShaderModel(value)) (fun (adaptive : AdaptiveLatLonShaderModel) (value : LatLonShaderModel) -> adaptive.Update(value))
    member __.Update(value : LatLonShaderModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<LatLonShaderModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _enabled_.Value <- value.enabled
            _lat1_.Value <- value.lat1
            _lat5_.Value <- value.lat5
            _lat15_.Value <- value.lat15
            _lon1_.Value <- value.lon1
            _lon5_.Value <- value.lon5
            _lon15_.Value <- value.lon15
            _lineColor_.Update(value.lineColor)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.enabled = _enabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lat1 = _lat1_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lat5 = _lat5_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lat15 = _lat15_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lon1 = _lon1_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lon5 = _lon5_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lon15 = _lon15_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lineColor = _lineColor_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module LatLonShaderModelLenses = 
    type LatLonShaderModel with
        static member version_ = ((fun (self : LatLonShaderModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : LatLonShaderModel) -> { self with version = value }))
        static member enabled_ = ((fun (self : LatLonShaderModel) -> self.enabled), (fun (value : Microsoft.FSharp.Core.bool) (self : LatLonShaderModel) -> { self with enabled = value }))
        static member lat1_ = ((fun (self : LatLonShaderModel) -> self.lat1), (fun (value : Microsoft.FSharp.Core.bool) (self : LatLonShaderModel) -> { self with lat1 = value }))
        static member lat5_ = ((fun (self : LatLonShaderModel) -> self.lat5), (fun (value : Microsoft.FSharp.Core.bool) (self : LatLonShaderModel) -> { self with lat5 = value }))
        static member lat15_ = ((fun (self : LatLonShaderModel) -> self.lat15), (fun (value : Microsoft.FSharp.Core.bool) (self : LatLonShaderModel) -> { self with lat15 = value }))
        static member lon1_ = ((fun (self : LatLonShaderModel) -> self.lon1), (fun (value : Microsoft.FSharp.Core.bool) (self : LatLonShaderModel) -> { self with lon1 = value }))
        static member lon5_ = ((fun (self : LatLonShaderModel) -> self.lon5), (fun (value : Microsoft.FSharp.Core.bool) (self : LatLonShaderModel) -> { self with lon5 = value }))
        static member lon15_ = ((fun (self : LatLonShaderModel) -> self.lon15), (fun (value : Microsoft.FSharp.Core.bool) (self : LatLonShaderModel) -> { self with lon15 = value }))
        static member lineColor_ = ((fun (self : LatLonShaderModel) -> self.lineColor), (fun (value : Aardvark.UI.ColorInput) (self : LatLonShaderModel) -> { self with lineColor = value }))

