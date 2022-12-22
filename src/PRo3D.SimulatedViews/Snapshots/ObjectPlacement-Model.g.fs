//ace9c9ca-99ab-3566-150e-1df4530bbc8a
//efc319d7-d72b-186e-9b33-bd656110d26a
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.SimulatedViews

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.SimulatedViews
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveObjectPlacementApp(value : ObjectPlacementApp) =
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _count_ = Aardvark.UI.AdaptiveNumericInput(value.count)
    let _scaleFrom_ = Aardvark.UI.AdaptiveNumericInput(value.scaleFrom)
    let _scaleTo_ = Aardvark.UI.AdaptiveNumericInput(value.scaleTo)
    let _xRotationFrom_ = Aardvark.UI.AdaptiveNumericInput(value.xRotationFrom)
    let _xRotationTo_ = Aardvark.UI.AdaptiveNumericInput(value.xRotationTo)
    let _yRotationFrom_ = Aardvark.UI.AdaptiveNumericInput(value.yRotationFrom)
    let _yRotationTo_ = Aardvark.UI.AdaptiveNumericInput(value.yRotationTo)
    let _zRotationFrom_ = Aardvark.UI.AdaptiveNumericInput(value.zRotationFrom)
    let _zRotationTo_ = Aardvark.UI.AdaptiveNumericInput(value.zRotationTo)
    let _maxDistance_ = Aardvark.UI.AdaptiveNumericInput(value.maxDistance)
    let _subsurface_ = Aardvark.UI.AdaptiveNumericInput(value.subsurface)
    let _maskColor_ = Aardvark.UI.AdaptiveColorInput(value.maskColor)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ObjectPlacementApp) = AdaptiveObjectPlacementApp(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ObjectPlacementApp) -> AdaptiveObjectPlacementApp(value)) (fun (adaptive : AdaptiveObjectPlacementApp) (value : ObjectPlacementApp) -> adaptive.Update(value))
    member __.Update(value : ObjectPlacementApp) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ObjectPlacementApp>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _name_.Value <- value.name
            _count_.Update(value.count)
            _scaleFrom_.Update(value.scaleFrom)
            _scaleTo_.Update(value.scaleTo)
            _xRotationFrom_.Update(value.xRotationFrom)
            _xRotationTo_.Update(value.xRotationTo)
            _yRotationFrom_.Update(value.yRotationFrom)
            _yRotationTo_.Update(value.yRotationTo)
            _zRotationFrom_.Update(value.zRotationFrom)
            _zRotationTo_.Update(value.zRotationTo)
            _maxDistance_.Update(value.maxDistance)
            _subsurface_.Update(value.subsurface)
            _maskColor_.Update(value.maskColor)
    member __.Current = __adaptive
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.count = _count_
    member __.scaleFrom = _scaleFrom_
    member __.scaleTo = _scaleTo_
    member __.xRotationFrom = _xRotationFrom_
    member __.xRotationTo = _xRotationTo_
    member __.yRotationFrom = _yRotationFrom_
    member __.yRotationTo = _yRotationTo_
    member __.zRotationFrom = _zRotationFrom_
    member __.zRotationTo = _zRotationTo_
    member __.maxDistance = _maxDistance_
    member __.subsurface = _subsurface_
    member __.maskColor = _maskColor_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ObjectPlacementAppLenses = 
    type ObjectPlacementApp with
        static member name_ = ((fun (self : ObjectPlacementApp) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : ObjectPlacementApp) -> { self with name = value }))
        static member count_ = ((fun (self : ObjectPlacementApp) -> self.count), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with count = value }))
        static member scaleFrom_ = ((fun (self : ObjectPlacementApp) -> self.scaleFrom), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with scaleFrom = value }))
        static member scaleTo_ = ((fun (self : ObjectPlacementApp) -> self.scaleTo), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with scaleTo = value }))
        static member xRotationFrom_ = ((fun (self : ObjectPlacementApp) -> self.xRotationFrom), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with xRotationFrom = value }))
        static member xRotationTo_ = ((fun (self : ObjectPlacementApp) -> self.xRotationTo), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with xRotationTo = value }))
        static member yRotationFrom_ = ((fun (self : ObjectPlacementApp) -> self.yRotationFrom), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with yRotationFrom = value }))
        static member yRotationTo_ = ((fun (self : ObjectPlacementApp) -> self.yRotationTo), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with yRotationTo = value }))
        static member zRotationFrom_ = ((fun (self : ObjectPlacementApp) -> self.zRotationFrom), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with zRotationFrom = value }))
        static member zRotationTo_ = ((fun (self : ObjectPlacementApp) -> self.zRotationTo), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with zRotationTo = value }))
        static member maxDistance_ = ((fun (self : ObjectPlacementApp) -> self.maxDistance), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with maxDistance = value }))
        static member subsurface_ = ((fun (self : ObjectPlacementApp) -> self.subsurface), (fun (value : Aardvark.UI.NumericInput) (self : ObjectPlacementApp) -> { self with subsurface = value }))
        static member maskColor_ = ((fun (self : ObjectPlacementApp) -> self.maskColor), (fun (value : Aardvark.UI.ColorInput) (self : ObjectPlacementApp) -> { self with maskColor = value }))

