//818ff77d-1857-b8f2-33da-e67b72ccfc79
//405fba53-1dcc-2870-8aa0-161b09a02434
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Lite

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Lite
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveModel(value : Model) =
    let _orbitState_ = PRo3D.Base.AdaptiveOrbitState(value.orbitState)
    let _freeFlyState_ = Aardvark.UI.Primitives.AdaptiveCameraControllerState(value.freeFlyState)
    let _cameraMode_ = FSharp.Data.Adaptive.cval(value.cameraMode)
    let _mousePos_ = FSharp.Data.Adaptive.cval(value.mousePos)
    let _cursor_ = FSharp.Data.Adaptive.cval(value.cursor)
    let _cursorWorldSphereSize_ = FSharp.Data.Adaptive.cval(value.cursorWorldSphereSize)
    let _state_ = AdaptiveState(value.state)
    let _background_ = FSharp.Data.Adaptive.cval(value.background)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Model) = AdaptiveModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Model) -> AdaptiveModel(value)) (fun (adaptive : AdaptiveModel) (value : Model) -> adaptive.Update(value))
    member __.Update(value : Model) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Model>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _orbitState_.Update(value.orbitState)
            _freeFlyState_.Update(value.freeFlyState)
            _cameraMode_.Value <- value.cameraMode
            _mousePos_.Value <- value.mousePos
            _cursor_.Value <- value.cursor
            _cursorWorldSphereSize_.Value <- value.cursorWorldSphereSize
            _state_.Update(value.state)
            _background_.Value <- value.background
    member __.Current = __adaptive
    member __.orbitState = _orbitState_
    member __.freeFlyState = _freeFlyState_
    member __.cameraMode = _cameraMode_ :> FSharp.Data.Adaptive.aval<CameraMode>
    member __.mousePos = _mousePos_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Base.V2i>>
    member __.cursor = _cursor_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Base.V3d>>
    member __.cursorWorldSphereSize = _cursorWorldSphereSize_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.state = _state_
    member __.background = _background_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ModelLenses = 
    type Model with
        static member orbitState_ = ((fun (self : Model) -> self.orbitState), (fun (value : PRo3D.Base.OrbitState) (self : Model) -> { self with orbitState = value }))
        static member freeFlyState_ = ((fun (self : Model) -> self.freeFlyState), (fun (value : Aardvark.UI.Primitives.CameraControllerState) (self : Model) -> { self with freeFlyState = value }))
        static member cameraMode_ = ((fun (self : Model) -> self.cameraMode), (fun (value : CameraMode) (self : Model) -> { self with cameraMode = value }))
        static member mousePos_ = ((fun (self : Model) -> self.mousePos), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.V2i>) (self : Model) -> { self with mousePos = value }))
        static member cursor_ = ((fun (self : Model) -> self.cursor), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.V3d>) (self : Model) -> { self with cursor = value }))
        static member cursorWorldSphereSize_ = ((fun (self : Model) -> self.cursorWorldSphereSize), (fun (value : Microsoft.FSharp.Core.float) (self : Model) -> { self with cursorWorldSphereSize = value }))
        static member state_ = ((fun (self : Model) -> self.state), (fun (value : State) (self : Model) -> { self with state = value }))
        static member background_ = ((fun (self : Model) -> self.background), (fun (value : Aardvark.Base.C4b) (self : Model) -> { self with background = value }))

