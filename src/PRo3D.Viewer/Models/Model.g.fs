//2ef1cb93-aefb-a2e8-fd54-49b0a45d2cd9
//22027587-635f-7aca-c676-eb4b7ef489a7
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
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveViewConfigModel(value : ViewConfigModel) =
    let _nearPlane_ = Aardvark.UI.AdaptiveNumericInput(value.nearPlane)
    let _farPlane_ = Aardvark.UI.AdaptiveNumericInput(value.farPlane)
    let _navigationSensitivity_ = Aardvark.UI.AdaptiveNumericInput(value.navigationSensitivity)
    let _importTriangleSize_ = Aardvark.UI.AdaptiveNumericInput(value.importTriangleSize)
    let _arrowLength_ = Aardvark.UI.AdaptiveNumericInput(value.arrowLength)
    let _arrowThickness_ = Aardvark.UI.AdaptiveNumericInput(value.arrowThickness)
    let _dnsPlaneSize_ = Aardvark.UI.AdaptiveNumericInput(value.dnsPlaneSize)
    let _offset_ = Aardvark.UI.AdaptiveNumericInput(value.offset)
    let _pickingTolerance_ = Aardvark.UI.AdaptiveNumericInput(value.pickingTolerance)
    let _lodColoring_ = FSharp.Data.Adaptive.cval(value.lodColoring)
    let _drawOrientationCube_ = FSharp.Data.Adaptive.cval(value.drawOrientationCube)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ViewConfigModel) = AdaptiveViewConfigModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ViewConfigModel) -> AdaptiveViewConfigModel(value)) (fun (adaptive : AdaptiveViewConfigModel) (value : ViewConfigModel) -> adaptive.Update(value))
    member __.Update(value : ViewConfigModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ViewConfigModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _nearPlane_.Update(value.nearPlane)
            _farPlane_.Update(value.farPlane)
            _navigationSensitivity_.Update(value.navigationSensitivity)
            _importTriangleSize_.Update(value.importTriangleSize)
            _arrowLength_.Update(value.arrowLength)
            _arrowThickness_.Update(value.arrowThickness)
            _dnsPlaneSize_.Update(value.dnsPlaneSize)
            _offset_.Update(value.offset)
            _pickingTolerance_.Update(value.pickingTolerance)
            _lodColoring_.Value <- value.lodColoring
            _drawOrientationCube_.Value <- value.drawOrientationCube
    member __.Current = __adaptive
    member __.version = __value.version
    member __.nearPlane = _nearPlane_
    member __.farPlane = _farPlane_
    member __.navigationSensitivity = _navigationSensitivity_
    member __.importTriangleSize = _importTriangleSize_
    member __.arrowLength = _arrowLength_
    member __.arrowThickness = _arrowThickness_
    member __.dnsPlaneSize = _dnsPlaneSize_
    member __.offset = _offset_
    member __.pickingTolerance = _pickingTolerance_
    member __.lodColoring = _lodColoring_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.drawOrientationCube = _drawOrientationCube_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ViewConfigModelLenses = 
    type ViewConfigModel with
        static member version_ = ((fun (self : ViewConfigModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ViewConfigModel) -> { self with version = value }))
        static member nearPlane_ = ((fun (self : ViewConfigModel) -> self.nearPlane), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with nearPlane = value }))
        static member farPlane_ = ((fun (self : ViewConfigModel) -> self.farPlane), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with farPlane = value }))
        static member navigationSensitivity_ = ((fun (self : ViewConfigModel) -> self.navigationSensitivity), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with navigationSensitivity = value }))
        static member importTriangleSize_ = ((fun (self : ViewConfigModel) -> self.importTriangleSize), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with importTriangleSize = value }))
        static member arrowLength_ = ((fun (self : ViewConfigModel) -> self.arrowLength), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with arrowLength = value }))
        static member arrowThickness_ = ((fun (self : ViewConfigModel) -> self.arrowThickness), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with arrowThickness = value }))
        static member dnsPlaneSize_ = ((fun (self : ViewConfigModel) -> self.dnsPlaneSize), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with dnsPlaneSize = value }))
        static member offset_ = ((fun (self : ViewConfigModel) -> self.offset), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with offset = value }))
        static member pickingTolerance_ = ((fun (self : ViewConfigModel) -> self.pickingTolerance), (fun (value : Aardvark.UI.NumericInput) (self : ViewConfigModel) -> { self with pickingTolerance = value }))
        static member lodColoring_ = ((fun (self : ViewConfigModel) -> self.lodColoring), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewConfigModel) -> { self with lodColoring = value }))
        static member drawOrientationCube_ = ((fun (self : ViewConfigModel) -> self.drawOrientationCube), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewConfigModel) -> { self with drawOrientationCube = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveFrustumModel(value : FrustumModel) =
    let _toggleFocal_ = FSharp.Data.Adaptive.cval(value.toggleFocal)
    let _focal_ = Aardvark.UI.AdaptiveNumericInput(value.focal)
    let _oldFrustum_ = FSharp.Data.Adaptive.cval(value.oldFrustum)
    let _frustum_ = FSharp.Data.Adaptive.cval(value.frustum)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : FrustumModel) = AdaptiveFrustumModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : FrustumModel) -> AdaptiveFrustumModel(value)) (fun (adaptive : AdaptiveFrustumModel) (value : FrustumModel) -> adaptive.Update(value))
    member __.Update(value : FrustumModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<FrustumModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _toggleFocal_.Value <- value.toggleFocal
            _focal_.Update(value.focal)
            _oldFrustum_.Value <- value.oldFrustum
            _frustum_.Value <- value.frustum
    member __.Current = __adaptive
    member __.toggleFocal = _toggleFocal_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.focal = _focal_
    member __.oldFrustum = _oldFrustum_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.Frustum>
    member __.frustum = _frustum_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.Frustum>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module FrustumModelLenses = 
    type FrustumModel with
        static member toggleFocal_ = ((fun (self : FrustumModel) -> self.toggleFocal), (fun (value : Microsoft.FSharp.Core.bool) (self : FrustumModel) -> { self with toggleFocal = value }))
        static member focal_ = ((fun (self : FrustumModel) -> self.focal), (fun (value : Aardvark.UI.NumericInput) (self : FrustumModel) -> { self with focal = value }))
        static member oldFrustum_ = ((fun (self : FrustumModel) -> self.oldFrustum), (fun (value : Aardvark.Rendering.Frustum) (self : FrustumModel) -> { self with oldFrustum = value }))
        static member frustum_ = ((fun (self : FrustumModel) -> self.frustum), (fun (value : Aardvark.Rendering.Frustum) (self : FrustumModel) -> { self with frustum = value }))

