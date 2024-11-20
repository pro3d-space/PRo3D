//79504c33-c6ca-c23d-20b3-97ccdd52f992
//1b17acb3-6955-9b6f-af28-4e00eda169b5
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Core.Surface

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Core.Surface
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveTransformations(value : Transformations) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _useTranslationArrows_ = FSharp.Data.Adaptive.cval(value.useTranslationArrows)
    let _translation_ = Aardvark.UI.Primitives.AdaptiveV3dInput(value.translation)
    let _yaw_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.yaw)
    let _pitch_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.pitch)
    let _roll_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.roll)
    let _trafo_ = FSharp.Data.Adaptive.cval(value.trafo)
    let _pivot_ = Aardvark.UI.Primitives.AdaptiveV3dInput(value.pivot)
    let _oldPivot_ = FSharp.Data.Adaptive.cval(value.oldPivot)
    let _showPivot_ = FSharp.Data.Adaptive.cval(value.showPivot)
    let _pivotChanged_ = FSharp.Data.Adaptive.cval(value.pivotChanged)
    let _flipZ_ = FSharp.Data.Adaptive.cval(value.flipZ)
    let _isSketchFab_ = FSharp.Data.Adaptive.cval(value.isSketchFab)
    let _scaling_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.scaling)
    let _trafoChanged_ = FSharp.Data.Adaptive.cval(value.trafoChanged)
    let _usePivot_ = FSharp.Data.Adaptive.cval(value.usePivot)
    let _pivotSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.pivotSize)
    let _eulerMode_ = FSharp.Data.Adaptive.cval(value.eulerMode)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Transformations) = AdaptiveTransformations(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Transformations) -> AdaptiveTransformations(value)) (fun (adaptive : AdaptiveTransformations) (value : Transformations) -> adaptive.Update(value))
    member __.Update(value : Transformations) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Transformations>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _useTranslationArrows_.Value <- value.useTranslationArrows
            _translation_.Update(value.translation)
            _yaw_.Update(value.yaw)
            _pitch_.Update(value.pitch)
            _roll_.Update(value.roll)
            _trafo_.Value <- value.trafo
            _pivot_.Update(value.pivot)
            _oldPivot_.Value <- value.oldPivot
            _showPivot_.Value <- value.showPivot
            _pivotChanged_.Value <- value.pivotChanged
            _flipZ_.Value <- value.flipZ
            _isSketchFab_.Value <- value.isSketchFab
            _scaling_.Update(value.scaling)
            _trafoChanged_.Value <- value.trafoChanged
            _usePivot_.Value <- value.usePivot
            _pivotSize_.Update(value.pivotSize)
            _eulerMode_.Value <- value.eulerMode
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.useTranslationArrows = _useTranslationArrows_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.translation = _translation_
    member __.yaw = _yaw_
    member __.pitch = _pitch_
    member __.roll = _roll_
    member __.trafo = _trafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.pivot = _pivot_
    member __.oldPivot = _oldPivot_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.showPivot = _showPivot_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.pivotChanged = _pivotChanged_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.flipZ = _flipZ_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isSketchFab = _isSketchFab_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.scaling = _scaling_
    member __.trafoChanged = _trafoChanged_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.usePivot = _usePivot_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.pivotSize = _pivotSize_
    member __.eulerMode = _eulerMode_ :> FSharp.Data.Adaptive.aval<EulerMode>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TransformationsLenses = 
    type Transformations with
        static member version_ = ((fun (self : Transformations) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Transformations) -> { self with version = value }))
        static member useTranslationArrows_ = ((fun (self : Transformations) -> self.useTranslationArrows), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with useTranslationArrows = value }))
        static member translation_ = ((fun (self : Transformations) -> self.translation), (fun (value : Aardvark.UI.Primitives.V3dInput) (self : Transformations) -> { self with translation = value }))
        static member yaw_ = ((fun (self : Transformations) -> self.yaw), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Transformations) -> { self with yaw = value }))
        static member pitch_ = ((fun (self : Transformations) -> self.pitch), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Transformations) -> { self with pitch = value }))
        static member roll_ = ((fun (self : Transformations) -> self.roll), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Transformations) -> { self with roll = value }))
        static member trafo_ = ((fun (self : Transformations) -> self.trafo), (fun (value : Aardvark.Base.Trafo3d) (self : Transformations) -> { self with trafo = value }))
        static member pivot_ = ((fun (self : Transformations) -> self.pivot), (fun (value : Aardvark.UI.Primitives.V3dInput) (self : Transformations) -> { self with pivot = value }))
        static member oldPivot_ = ((fun (self : Transformations) -> self.oldPivot), (fun (value : Aardvark.Base.V3d) (self : Transformations) -> { self with oldPivot = value }))
        static member showPivot_ = ((fun (self : Transformations) -> self.showPivot), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with showPivot = value }))
        static member pivotChanged_ = ((fun (self : Transformations) -> self.pivotChanged), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with pivotChanged = value }))
        static member flipZ_ = ((fun (self : Transformations) -> self.flipZ), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with flipZ = value }))
        static member isSketchFab_ = ((fun (self : Transformations) -> self.isSketchFab), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with isSketchFab = value }))
        static member scaling_ = ((fun (self : Transformations) -> self.scaling), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Transformations) -> { self with scaling = value }))
        static member trafoChanged_ = ((fun (self : Transformations) -> self.trafoChanged), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with trafoChanged = value }))
        static member usePivot_ = ((fun (self : Transformations) -> self.usePivot), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with usePivot = value }))
        static member pivotSize_ = ((fun (self : Transformations) -> self.pivotSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Transformations) -> { self with pivotSize = value }))
        static member eulerMode_ = ((fun (self : Transformations) -> self.eulerMode), (fun (value : EulerMode) (self : Transformations) -> { self with eulerMode = value }))

