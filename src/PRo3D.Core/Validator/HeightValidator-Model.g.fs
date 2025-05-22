//1958a528-bebb-81e8-71c4-2b4dc82ae50a
//60111e35-d8df-9ec2-4833-e44a292133f7
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
type AdaptiveValidator(value : Validator) =
    let _location_ = FSharp.Data.Adaptive.cval(value.location)
    let _lower_ = FSharp.Data.Adaptive.cval(value.lower)
    let _upper_ = FSharp.Data.Adaptive.cval(value.upper)
    let _upVector_ = FSharp.Data.Adaptive.cval(value.upVector)
    let _northVector_ = FSharp.Data.Adaptive.cval(value.northVector)
    let _height_ = FSharp.Data.Adaptive.cval(value.height)
    let _inclination_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.inclination)
    let _tiltedPlane_ = FSharp.Data.Adaptive.cval(value.tiltedPlane)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Validator) = AdaptiveValidator(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Validator) -> AdaptiveValidator(value)) (fun (adaptive : AdaptiveValidator) (value : Validator) -> adaptive.Update(value))
    member __.Update(value : Validator) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Validator>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _location_.Value <- value.location
            _lower_.Value <- value.lower
            _upper_.Value <- value.upper
            _upVector_.Value <- value.upVector
            _northVector_.Value <- value.northVector
            _height_.Value <- value.height
            _inclination_.Update(value.inclination)
            _tiltedPlane_.Value <- value.tiltedPlane
    member __.Current = __adaptive
    member __.location = _location_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.lower = _lower_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.upper = _upper_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.upVector = _upVector_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.northVector = _northVector_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.height = _height_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.double>
    member __.inclination = _inclination_
    member __.tiltedPlane = _tiltedPlane_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Plane3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ValidatorLenses = 
    type Validator with
        static member location_ = ((fun (self : Validator) -> self.location), (fun (value : Aardvark.Base.V3d) (self : Validator) -> { self with location = value }))
        static member lower_ = ((fun (self : Validator) -> self.lower), (fun (value : Aardvark.Base.V3d) (self : Validator) -> { self with lower = value }))
        static member upper_ = ((fun (self : Validator) -> self.upper), (fun (value : Aardvark.Base.V3d) (self : Validator) -> { self with upper = value }))
        static member upVector_ = ((fun (self : Validator) -> self.upVector), (fun (value : Aardvark.Base.V3d) (self : Validator) -> { self with upVector = value }))
        static member northVector_ = ((fun (self : Validator) -> self.northVector), (fun (value : Aardvark.Base.V3d) (self : Validator) -> { self with northVector = value }))
        static member height_ = ((fun (self : Validator) -> self.height), (fun (value : Microsoft.FSharp.Core.double) (self : Validator) -> { self with height = value }))
        static member inclination_ = ((fun (self : Validator) -> self.inclination), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Validator) -> { self with inclination = value }))
        static member tiltedPlane_ = ((fun (self : Validator) -> self.tiltedPlane), (fun (value : Aardvark.Base.Plane3d) (self : Validator) -> { self with tiltedPlane = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveValidatorResult(value : ValidatorResult) =
    let _pointDistance_ = FSharp.Data.Adaptive.cval(value.pointDistance)
    let _cooTrafoThickness_geographic_ = FSharp.Data.Adaptive.cval(value.cooTrafoThickness_geographic)
    let _cooTrafoThickness_true_ = FSharp.Data.Adaptive.cval(value.cooTrafoThickness_true)
    let _heightOverPlaneThickness_true_ = FSharp.Data.Adaptive.cval(value.heightOverPlaneThickness_true)
    let _heightOverHorizontal_ = FSharp.Data.Adaptive.cval(value.heightOverHorizontal)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ValidatorResult) = AdaptiveValidatorResult(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ValidatorResult) -> AdaptiveValidatorResult(value)) (fun (adaptive : AdaptiveValidatorResult) (value : ValidatorResult) -> adaptive.Update(value))
    member __.Update(value : ValidatorResult) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ValidatorResult>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _pointDistance_.Value <- value.pointDistance
            _cooTrafoThickness_geographic_.Value <- value.cooTrafoThickness_geographic
            _cooTrafoThickness_true_.Value <- value.cooTrafoThickness_true
            _heightOverPlaneThickness_true_.Value <- value.heightOverPlaneThickness_true
            _heightOverHorizontal_.Value <- value.heightOverHorizontal
    member __.Current = __adaptive
    member __.pointDistance = _pointDistance_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.double>
    member __.cooTrafoThickness_geographic = _cooTrafoThickness_geographic_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.double>
    member __.cooTrafoThickness_true = _cooTrafoThickness_true_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.double>
    member __.heightOverPlaneThickness_true = _heightOverPlaneThickness_true_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.double>
    member __.heightOverHorizontal = _heightOverHorizontal_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.double>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ValidatorResultLenses = 
    type ValidatorResult with
        static member pointDistance_ = ((fun (self : ValidatorResult) -> self.pointDistance), (fun (value : Microsoft.FSharp.Core.double) (self : ValidatorResult) -> { self with pointDistance = value }))
        static member cooTrafoThickness_geographic_ = ((fun (self : ValidatorResult) -> self.cooTrafoThickness_geographic), (fun (value : Microsoft.FSharp.Core.double) (self : ValidatorResult) -> { self with cooTrafoThickness_geographic = value }))
        static member cooTrafoThickness_true_ = ((fun (self : ValidatorResult) -> self.cooTrafoThickness_true), (fun (value : Microsoft.FSharp.Core.double) (self : ValidatorResult) -> { self with cooTrafoThickness_true = value }))
        static member heightOverPlaneThickness_true_ = ((fun (self : ValidatorResult) -> self.heightOverPlaneThickness_true), (fun (value : Microsoft.FSharp.Core.double) (self : ValidatorResult) -> { self with heightOverPlaneThickness_true = value }))
        static member heightOverHorizontal_ = ((fun (self : ValidatorResult) -> self.heightOverHorizontal), (fun (value : Microsoft.FSharp.Core.double) (self : ValidatorResult) -> { self with heightOverHorizontal = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveHeightValidatorModel(value : HeightValidatorModel) =
    let _validatorBrush_ = FSharp.Data.Adaptive.clist(value.validatorBrush)
    let _validator_ = AdaptiveValidator(value.validator)
    let _result_ = AdaptiveValidatorResult(value.result)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : HeightValidatorModel) = AdaptiveHeightValidatorModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : HeightValidatorModel) -> AdaptiveHeightValidatorModel(value)) (fun (adaptive : AdaptiveHeightValidatorModel) (value : HeightValidatorModel) -> adaptive.Update(value))
    member __.Update(value : HeightValidatorModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<HeightValidatorModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _validatorBrush_.Value <- value.validatorBrush
            _validator_.Update(value.validator)
            _result_.Update(value.result)
    member __.Current = __adaptive
    member __.validatorBrush = _validatorBrush_ :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>
    member __.validator = _validator_
    member __.result = _result_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module HeightValidatorModelLenses = 
    type HeightValidatorModel with
        static member validatorBrush_ = ((fun (self : HeightValidatorModel) -> self.validatorBrush), (fun (value : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) (self : HeightValidatorModel) -> { self with validatorBrush = value }))
        static member validator_ = ((fun (self : HeightValidatorModel) -> self.validator), (fun (value : Validator) (self : HeightValidatorModel) -> { self with validator = value }))
        static member result_ = ((fun (self : HeightValidatorModel) -> self.result), (fun (value : ValidatorResult) (self : HeightValidatorModel) -> { self with result = value }))

