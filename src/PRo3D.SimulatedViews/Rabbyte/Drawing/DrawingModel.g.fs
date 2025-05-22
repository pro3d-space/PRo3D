//11849af6-c57e-3e10-661b-b0b8e3801979
//b23b43bc-0ee4-55bc-ae39-010243007705
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec Rabbyte.Drawing

open System
open FSharp.Data.Adaptive
open Adaptify
open Rabbyte.Drawing
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveBrushStyle(value : BrushStyle) =
    let _primary_ = Aardvark.UI.AdaptiveColorInput(value.primary)
    let _secondary_ = Aardvark.UI.AdaptiveColorInput(value.secondary)
    let _lineStyle_ = FSharp.Data.Adaptive.cval(value.lineStyle)
    let _areaStyle_ = FSharp.Data.Adaptive.cval(value.areaStyle)
    let _thickness_ = FSharp.Data.Adaptive.cval(value.thickness)
    let _samplingRate_ = FSharp.Data.Adaptive.cval(value.samplingRate)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : BrushStyle) = AdaptiveBrushStyle(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : BrushStyle) -> AdaptiveBrushStyle(value)) (fun (adaptive : AdaptiveBrushStyle) (value : BrushStyle) -> adaptive.Update(value))
    member __.Update(value : BrushStyle) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<BrushStyle>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _primary_.Update(value.primary)
            _secondary_.Update(value.secondary)
            _lineStyle_.Value <- value.lineStyle
            _areaStyle_.Value <- value.areaStyle
            _thickness_.Value <- value.thickness
            _samplingRate_.Value <- value.samplingRate
    member __.Current = __adaptive
    member __.primary = _primary_
    member __.secondary = _secondary_
    member __.lineStyle = _lineStyle_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<LineStyle>>
    member __.areaStyle = _areaStyle_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<AreaStyle>>
    member __.thickness = _thickness_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.samplingRate = _samplingRate_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module BrushStyleLenses = 
    type BrushStyle with
        static member primary_ = ((fun (self : BrushStyle) -> self.primary), (fun (value : Aardvark.UI.ColorInput) (self : BrushStyle) -> { self with primary = value }))
        static member secondary_ = ((fun (self : BrushStyle) -> self.secondary), (fun (value : Aardvark.UI.ColorInput) (self : BrushStyle) -> { self with secondary = value }))
        static member lineStyle_ = ((fun (self : BrushStyle) -> self.lineStyle), (fun (value : Microsoft.FSharp.Core.Option<LineStyle>) (self : BrushStyle) -> { self with lineStyle = value }))
        static member areaStyle_ = ((fun (self : BrushStyle) -> self.areaStyle), (fun (value : Microsoft.FSharp.Core.Option<AreaStyle>) (self : BrushStyle) -> { self with areaStyle = value }))
        static member thickness_ = ((fun (self : BrushStyle) -> self.thickness), (fun (value : Microsoft.FSharp.Core.float) (self : BrushStyle) -> { self with thickness = value }))
        static member samplingRate_ = ((fun (self : BrushStyle) -> self.samplingRate), (fun (value : Microsoft.FSharp.Core.float) (self : BrushStyle) -> { self with samplingRate = value }))

