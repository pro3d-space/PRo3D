//4170be4c-46f5-619a-ce37-1affc8fc9e89
//2dfc2734-8d0c-d14a-e49c-3ccf8fc745b3
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveStatistics(value : Statistics) =
    let _average_ = FSharp.Data.Adaptive.cval(value.average)
    let _min_ = FSharp.Data.Adaptive.cval(value.min)
    let _max_ = FSharp.Data.Adaptive.cval(value.max)
    let _stdev_ = FSharp.Data.Adaptive.cval(value.stdev)
    let _sumOfSquares_ = FSharp.Data.Adaptive.cval(value.sumOfSquares)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Statistics) = AdaptiveStatistics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Statistics) -> AdaptiveStatistics(value)) (fun (adaptive : AdaptiveStatistics) (value : Statistics) -> adaptive.Update(value))
    member __.Update(value : Statistics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Statistics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _average_.Value <- value.average
            _min_.Value <- value.min
            _max_.Value <- value.max
            _stdev_.Value <- value.stdev
            _sumOfSquares_.Value <- value.sumOfSquares
    member __.Current = __adaptive
    member __.average = _average_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.min = _min_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.max = _max_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.stdev = _stdev_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.sumOfSquares = _sumOfSquares_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module StatisticsLenses = 
    type Statistics with
        static member average_ = ((fun (self : Statistics) -> self.average), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with average = value }))
        static member min_ = ((fun (self : Statistics) -> self.min), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with min = value }))
        static member max_ = ((fun (self : Statistics) -> self.max), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with max = value }))
        static member stdev_ = ((fun (self : Statistics) -> self.stdev), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with stdev = value }))
        static member sumOfSquares_ = ((fun (self : Statistics) -> self.sumOfSquares), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with sumOfSquares = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveOrientationCubeModel(value : OrientationCubeModel) =
    let _camera_ = Aardvark.UI.Primitives.AdaptiveCameraControllerState(value.camera)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : OrientationCubeModel) = AdaptiveOrientationCubeModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : OrientationCubeModel) -> AdaptiveOrientationCubeModel(value)) (fun (adaptive : AdaptiveOrientationCubeModel) (value : OrientationCubeModel) -> adaptive.Update(value))
    member __.Update(value : OrientationCubeModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<OrientationCubeModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _camera_.Update(value.camera)
    member __.Current = __adaptive
    member __.camera = _camera_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module OrientationCubeModelLenses = 
    type OrientationCubeModel with
        static member camera_ = ((fun (self : OrientationCubeModel) -> self.camera), (fun (value : Aardvark.UI.Primitives.CameraControllerState) (self : OrientationCubeModel) -> { self with camera = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptivePathProxy(value : PathProxy) =
    let _absolutePath_ = FSharp.Data.Adaptive.cval(value.absolutePath)
    let _relativePath_ = FSharp.Data.Adaptive.cval(value.relativePath)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : PathProxy) = AdaptivePathProxy(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : PathProxy) -> AdaptivePathProxy(value)) (fun (adaptive : AdaptivePathProxy) (value : PathProxy) -> adaptive.Update(value))
    member __.Update(value : PathProxy) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<PathProxy>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _absolutePath_.Value <- value.absolutePath
            _relativePath_.Value <- value.relativePath
    member __.Current = __adaptive
    member __.absolutePath = _absolutePath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.relativePath = _relativePath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module PathProxyLenses = 
    type PathProxy with
        static member absolutePath_ = ((fun (self : PathProxy) -> self.absolutePath), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : PathProxy) -> { self with absolutePath = value }))
        static member relativePath_ = ((fun (self : PathProxy) -> self.relativePath), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : PathProxy) -> { self with relativePath = value }))

