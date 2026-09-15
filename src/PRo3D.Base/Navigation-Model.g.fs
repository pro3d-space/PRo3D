//ae04a1ae-527b-e27e-d1d0-c3e2d4cc86eb
//6dcd4dc8-9877-63ac-7023-ffc2c248198e
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
type AdaptiveNavigationModel(value : NavigationModel) =
    let _camera_ = Aardvark.UI.Primitives.AdaptiveCameraControllerState(value.camera)
    let _navigationMode_ = FSharp.Data.Adaptive.cval(value.navigationMode)
    let _exploreCenter_ = FSharp.Data.Adaptive.cval(value.exploreCenter)
    let _updatePerFrame_ = FSharp.Data.Adaptive.cval(value.updatePerFrame)
    let _lockedAxis_ = FSharp.Data.Adaptive.cval(value.lockedAxis)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : NavigationModel) = AdaptiveNavigationModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : NavigationModel) -> AdaptiveNavigationModel(value)) (fun (adaptive : AdaptiveNavigationModel) (value : NavigationModel) -> adaptive.Update(value))
    member __.Update(value : NavigationModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<NavigationModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _camera_.Update(value.camera)
            _navigationMode_.Value <- value.navigationMode
            _exploreCenter_.Value <- value.exploreCenter
            _updatePerFrame_.Value <- value.updatePerFrame
            _lockedAxis_.Value <- value.lockedAxis
    member __.Current = __adaptive
    member __.camera = _camera_
    member __.navigationMode = _navigationMode_ :> FSharp.Data.Adaptive.aval<NavigationMode>
    member __.exploreCenter = _exploreCenter_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.updatePerFrame = _updatePerFrame_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lockedAxis = _lockedAxis_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<NavigationAxis>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module NavigationModelLenses = 
    type NavigationModel with
        static member camera_ = ((fun (self : NavigationModel) -> self.camera), (fun (value : Aardvark.UI.Primitives.CameraControllerState) (self : NavigationModel) -> { self with camera = value }))
        static member navigationMode_ = ((fun (self : NavigationModel) -> self.navigationMode), (fun (value : NavigationMode) (self : NavigationModel) -> { self with navigationMode = value }))
        static member exploreCenter_ = ((fun (self : NavigationModel) -> self.exploreCenter), (fun (value : Aardvark.Base.V3d) (self : NavigationModel) -> { self with exploreCenter = value }))
        static member updatePerFrame_ = ((fun (self : NavigationModel) -> self.updatePerFrame), (fun (value : Microsoft.FSharp.Core.bool) (self : NavigationModel) -> { self with updatePerFrame = value }))
        static member lockedAxis_ = ((fun (self : NavigationModel) -> self.lockedAxis), (fun (value : Microsoft.FSharp.Core.Option<NavigationAxis>) (self : NavigationModel) -> { self with lockedAxis = value }))

