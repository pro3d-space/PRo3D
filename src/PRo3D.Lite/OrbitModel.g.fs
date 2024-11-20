//abce7b4f-0cc5-e7b1-bb7c-7a997e65a75e
//f6c8aeec-7409-71ff-6185-c0a549162d02
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
type AdaptiveOrbitState(value : OrbitState) =
    let _sky_ = FSharp.Data.Adaptive.cval(value.sky)
    let _right_ = FSharp.Data.Adaptive.cval(value.right)
    let _center_ = FSharp.Data.Adaptive.cval(value.center)
    let _phi_ = FSharp.Data.Adaptive.cval(value.phi)
    let _theta_ = FSharp.Data.Adaptive.cval(value.theta)
    let _radius_ = FSharp.Data.Adaptive.cval(value.radius)
    let _targetPhi_ = FSharp.Data.Adaptive.cval(value.targetPhi)
    let _targetTheta_ = FSharp.Data.Adaptive.cval(value.targetTheta)
    let _targetRadius_ = FSharp.Data.Adaptive.cval(value.targetRadius)
    let _targetCenter_ = FSharp.Data.Adaptive.cval(value.targetCenter)
    let _dragStart_ = FSharp.Data.Adaptive.cval(value.dragStart)
    let _panning_ = FSharp.Data.Adaptive.cval(value.panning)
    let _pan_ = FSharp.Data.Adaptive.cval(value.pan)
    let _targetPan_ = FSharp.Data.Adaptive.cval(value.targetPan)
    let _view_ = FSharp.Data.Adaptive.cval(value.view)
    let _radiusRange_ = FSharp.Data.Adaptive.cval(value.radiusRange)
    let _thetaRange_ = FSharp.Data.Adaptive.cval(value.thetaRange)
    let _moveSensitivity_ = FSharp.Data.Adaptive.cval(value.moveSensitivity)
    let _zoomSensitivity_ = FSharp.Data.Adaptive.cval(value.zoomSensitivity)
    let _speed_ = FSharp.Data.Adaptive.cval(value.speed)
    let _config_ = FSharp.Data.Adaptive.cval(value.config)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : OrbitState) = AdaptiveOrbitState(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : OrbitState) -> AdaptiveOrbitState(value)) (fun (adaptive : AdaptiveOrbitState) (value : OrbitState) -> adaptive.Update(value))
    member __.Update(value : OrbitState) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<OrbitState>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _sky_.Value <- value.sky
            _right_.Value <- value.right
            _center_.Value <- value.center
            _phi_.Value <- value.phi
            _theta_.Value <- value.theta
            _radius_.Value <- value.radius
            _targetPhi_.Value <- value.targetPhi
            _targetTheta_.Value <- value.targetTheta
            _targetRadius_.Value <- value.targetRadius
            _targetCenter_.Value <- value.targetCenter
            _dragStart_.Value <- value.dragStart
            _panning_.Value <- value.panning
            _pan_.Value <- value.pan
            _targetPan_.Value <- value.targetPan
            _view_.Value <- value.view
            _radiusRange_.Value <- value.radiusRange
            _thetaRange_.Value <- value.thetaRange
            _moveSensitivity_.Value <- value.moveSensitivity
            _zoomSensitivity_.Value <- value.zoomSensitivity
            _speed_.Value <- value.speed
            _config_.Value <- value.config
    member __.Current = __adaptive
    member __.sky = _sky_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.right = _right_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.center = _center_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.phi = _phi_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.theta = _theta_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.radius = _radius_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.targetPhi = _targetPhi_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.targetTheta = _targetTheta_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.targetRadius = _targetRadius_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.targetCenter = _targetCenter_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.dragStart = _dragStart_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Base.V2i>>
    member __.panning = _panning_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.pan = _pan_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V2d>
    member __.targetPan = _targetPan_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V2d>
    member __.lastRender = __value.lastRender
    member __.view = _view_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.radiusRange = _radiusRange_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Range1d>
    member __.thetaRange = _thetaRange_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Range1d>
    member __.moveSensitivity = _moveSensitivity_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.zoomSensitivity = _zoomSensitivity_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.speed = _speed_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.config = _config_ :> FSharp.Data.Adaptive.aval<OrbitControllerConfig>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module OrbitStateLenses = 
    type OrbitState with
        static member sky_ = ((fun (self : OrbitState) -> self.sky), (fun (value : Aardvark.Base.V3d) (self : OrbitState) -> { self with sky = value }))
        static member right_ = ((fun (self : OrbitState) -> self.right), (fun (value : Aardvark.Base.V3d) (self : OrbitState) -> { self with right = value }))
        static member center_ = ((fun (self : OrbitState) -> self.center), (fun (value : Aardvark.Base.V3d) (self : OrbitState) -> { self with center = value }))
        static member phi_ = ((fun (self : OrbitState) -> self.phi), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with phi = value }))
        static member theta_ = ((fun (self : OrbitState) -> self.theta), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with theta = value }))
        static member radius_ = ((fun (self : OrbitState) -> self.radius), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with radius = value }))
        static member targetPhi_ = ((fun (self : OrbitState) -> self.targetPhi), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with targetPhi = value }))
        static member targetTheta_ = ((fun (self : OrbitState) -> self.targetTheta), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with targetTheta = value }))
        static member targetRadius_ = ((fun (self : OrbitState) -> self.targetRadius), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with targetRadius = value }))
        static member targetCenter_ = ((fun (self : OrbitState) -> self.targetCenter), (fun (value : Aardvark.Base.V3d) (self : OrbitState) -> { self with targetCenter = value }))
        static member dragStart_ = ((fun (self : OrbitState) -> self.dragStart), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.V2i>) (self : OrbitState) -> { self with dragStart = value }))
        static member panning_ = ((fun (self : OrbitState) -> self.panning), (fun (value : Microsoft.FSharp.Core.bool) (self : OrbitState) -> { self with panning = value }))
        static member pan_ = ((fun (self : OrbitState) -> self.pan), (fun (value : Aardvark.Base.V2d) (self : OrbitState) -> { self with pan = value }))
        static member targetPan_ = ((fun (self : OrbitState) -> self.targetPan), (fun (value : Aardvark.Base.V2d) (self : OrbitState) -> { self with targetPan = value }))
        static member lastRender_ = ((fun (self : OrbitState) -> self.lastRender), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.MicroTime>) (self : OrbitState) -> { self with lastRender = value }))
        static member view_ = ((fun (self : OrbitState) -> self.view), (fun (value : Aardvark.Rendering.CameraView) (self : OrbitState) -> { self with view = value }))
        static member radiusRange_ = ((fun (self : OrbitState) -> self.radiusRange), (fun (value : Aardvark.Base.Range1d) (self : OrbitState) -> { self with radiusRange = value }))
        static member thetaRange_ = ((fun (self : OrbitState) -> self.thetaRange), (fun (value : Aardvark.Base.Range1d) (self : OrbitState) -> { self with thetaRange = value }))
        static member moveSensitivity_ = ((fun (self : OrbitState) -> self.moveSensitivity), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with moveSensitivity = value }))
        static member zoomSensitivity_ = ((fun (self : OrbitState) -> self.zoomSensitivity), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with zoomSensitivity = value }))
        static member speed_ = ((fun (self : OrbitState) -> self.speed), (fun (value : Microsoft.FSharp.Core.float) (self : OrbitState) -> { self with speed = value }))
        static member config_ = ((fun (self : OrbitState) -> self.config), (fun (value : OrbitControllerConfig) (self : OrbitState) -> { self with config = value }))

