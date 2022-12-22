//fe3abf61-ece7-74b1-e36c-0237c4701dbf
//ee286ef1-4a26-7925-e4f5-492a5605833f
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Base

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveFalseColorsModel(value : FalseColorsModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _useFalseColors_ = FSharp.Data.Adaptive.cval(value.useFalseColors)
    let _lowerBound_ = Aardvark.UI.AdaptiveNumericInput(value.lowerBound)
    let _upperBound_ = Aardvark.UI.AdaptiveNumericInput(value.upperBound)
    let _interval_ = Aardvark.UI.AdaptiveNumericInput(value.interval)
    let _invertMapping_ = FSharp.Data.Adaptive.cval(value.invertMapping)
    let _lowerColor_ = Aardvark.UI.AdaptiveColorInput(value.lowerColor)
    let _upperColor_ = Aardvark.UI.AdaptiveColorInput(value.upperColor)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : FalseColorsModel) = AdaptiveFalseColorsModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : FalseColorsModel) -> AdaptiveFalseColorsModel(value)) (fun (adaptive : AdaptiveFalseColorsModel) (value : FalseColorsModel) -> adaptive.Update(value))
    member __.Update(value : FalseColorsModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<FalseColorsModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _useFalseColors_.Value <- value.useFalseColors
            _lowerBound_.Update(value.lowerBound)
            _upperBound_.Update(value.upperBound)
            _interval_.Update(value.interval)
            _invertMapping_.Value <- value.invertMapping
            _lowerColor_.Update(value.lowerColor)
            _upperColor_.Update(value.upperColor)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.useFalseColors = _useFalseColors_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lowerBound = _lowerBound_
    member __.upperBound = _upperBound_
    member __.interval = _interval_
    member __.invertMapping = _invertMapping_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lowerColor = _lowerColor_
    member __.upperColor = _upperColor_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module FalseColorsModelLenses = 
    type FalseColorsModel with
        static member version_ = ((fun (self : FalseColorsModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : FalseColorsModel) -> { self with version = value }))
        static member useFalseColors_ = ((fun (self : FalseColorsModel) -> self.useFalseColors), (fun (value : Microsoft.FSharp.Core.bool) (self : FalseColorsModel) -> { self with useFalseColors = value }))
        static member lowerBound_ = ((fun (self : FalseColorsModel) -> self.lowerBound), (fun (value : Aardvark.UI.NumericInput) (self : FalseColorsModel) -> { self with lowerBound = value }))
        static member upperBound_ = ((fun (self : FalseColorsModel) -> self.upperBound), (fun (value : Aardvark.UI.NumericInput) (self : FalseColorsModel) -> { self with upperBound = value }))
        static member interval_ = ((fun (self : FalseColorsModel) -> self.interval), (fun (value : Aardvark.UI.NumericInput) (self : FalseColorsModel) -> { self with interval = value }))
        static member invertMapping_ = ((fun (self : FalseColorsModel) -> self.invertMapping), (fun (value : Microsoft.FSharp.Core.bool) (self : FalseColorsModel) -> { self with invertMapping = value }))
        static member lowerColor_ = ((fun (self : FalseColorsModel) -> self.lowerColor), (fun (value : Aardvark.UI.ColorInput) (self : FalseColorsModel) -> { self with lowerColor = value }))
        static member upperColor_ = ((fun (self : FalseColorsModel) -> self.upperColor), (fun (value : Aardvark.UI.ColorInput) (self : FalseColorsModel) -> { self with upperColor = value }))

