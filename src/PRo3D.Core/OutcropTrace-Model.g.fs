//438cbe0b-8ce9-4ce0-6b9c-d8b13600e7f1
//78c23b15-628d-c828-1fb9-72818ace421b
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
type AdaptiveOutcropTraceModel(value : OutcropTraceModel) =
    let _enabled_ = FSharp.Data.Adaptive.cval(value.enabled)
    let _usePolyline_ = FSharp.Data.Adaptive.cval(value.usePolyline)
    let _useDnS_ = FSharp.Data.Adaptive.cval(value.useDnS)
    let _bedThickness_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.bedThickness)
    let _traceWidth_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.traceWidth)
    let _traceSmoothing_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.traceSmoothing)
    let _projectionFactor_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.projectionFactor)
    let _projectionFloor_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.projectionFloor)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : OutcropTraceModel) = AdaptiveOutcropTraceModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : OutcropTraceModel) -> AdaptiveOutcropTraceModel(value)) (fun (adaptive : AdaptiveOutcropTraceModel) (value : OutcropTraceModel) -> adaptive.Update(value))
    member __.Update(value : OutcropTraceModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<OutcropTraceModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _enabled_.Value <- value.enabled
            _usePolyline_.Value <- value.usePolyline
            _useDnS_.Value <- value.useDnS
            _bedThickness_.Update(value.bedThickness)
            _traceWidth_.Update(value.traceWidth)
            _traceSmoothing_.Update(value.traceSmoothing)
            _projectionFactor_.Update(value.projectionFactor)
            _projectionFloor_.Update(value.projectionFloor)
            _color_.Update(value.color)
    member __.Current = __adaptive
    member __.enabled = _enabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.usePolyline = _usePolyline_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.useDnS = _useDnS_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.bedThickness = _bedThickness_
    member __.traceWidth = _traceWidth_
    member __.traceSmoothing = _traceSmoothing_
    member __.projectionFactor = _projectionFactor_
    member __.projectionFloor = _projectionFloor_
    member __.color = _color_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module OutcropTraceModelLenses = 
    type OutcropTraceModel with
        static member enabled_ = ((fun (self : OutcropTraceModel) -> self.enabled), (fun (value : Microsoft.FSharp.Core.bool) (self : OutcropTraceModel) -> { self with enabled = value }))
        static member usePolyline_ = ((fun (self : OutcropTraceModel) -> self.usePolyline), (fun (value : Microsoft.FSharp.Core.bool) (self : OutcropTraceModel) -> { self with usePolyline = value }))
        static member useDnS_ = ((fun (self : OutcropTraceModel) -> self.useDnS), (fun (value : Microsoft.FSharp.Core.bool) (self : OutcropTraceModel) -> { self with useDnS = value }))
        static member bedThickness_ = ((fun (self : OutcropTraceModel) -> self.bedThickness), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : OutcropTraceModel) -> { self with bedThickness = value }))
        static member traceWidth_ = ((fun (self : OutcropTraceModel) -> self.traceWidth), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : OutcropTraceModel) -> { self with traceWidth = value }))
        static member traceSmoothing_ = ((fun (self : OutcropTraceModel) -> self.traceSmoothing), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : OutcropTraceModel) -> { self with traceSmoothing = value }))
        static member projectionFactor_ = ((fun (self : OutcropTraceModel) -> self.projectionFactor), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : OutcropTraceModel) -> { self with projectionFactor = value }))
        static member projectionFloor_ = ((fun (self : OutcropTraceModel) -> self.projectionFloor), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : OutcropTraceModel) -> { self with projectionFloor = value }))
        static member color_ = ((fun (self : OutcropTraceModel) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : OutcropTraceModel) -> { self with color = value }))

