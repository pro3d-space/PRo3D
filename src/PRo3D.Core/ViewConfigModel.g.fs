//0372000e-adff-7e7d-731b-8a46566ff201
//467f9d72-b605-3963-e059-0bc82710dfe9
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
type AdaptiveFrustumModel(value : FrustumModel) =
    let _toggleFocal_ = FSharp.Data.Adaptive.cval(value.toggleFocal)
    let _focal_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.focal)
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
        static member focal_ = ((fun (self : FrustumModel) -> self.focal), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : FrustumModel) -> { self with focal = value }))
        static member oldFrustum_ = ((fun (self : FrustumModel) -> self.oldFrustum), (fun (value : Aardvark.Rendering.Frustum) (self : FrustumModel) -> { self with oldFrustum = value }))
        static member frustum_ = ((fun (self : FrustumModel) -> self.frustum), (fun (value : Aardvark.Rendering.Frustum) (self : FrustumModel) -> { self with frustum = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveViewConfigModel(value : ViewConfigModel) =
    let _nearPlane_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.nearPlane)
    let _farPlane_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.farPlane)
    let _frustumModel_ = AdaptiveFrustumModel(value.frustumModel)
    let _navigationSensitivity_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.navigationSensitivity)
    let _importTriangleSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.importTriangleSize)
    let _arrowLength_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.arrowLength)
    let _arrowThickness_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.arrowThickness)
    let _dnsPlaneSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.dnsPlaneSize)
    let _offset_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.offset)
    let _pickingTolerance_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.pickingTolerance)
    let _lodColoring_ = FSharp.Data.Adaptive.cval(value.lodColoring)
    let _drawOrientationCube_ = FSharp.Data.Adaptive.cval(value.drawOrientationCube)
    let _filterTexture_ = FSharp.Data.Adaptive.cval(value.filterTexture)
    let _showExplorationPointGui_ = FSharp.Data.Adaptive.cval(value.showExplorationPointGui)
    let _showLeafLabels_ = FSharp.Data.Adaptive.cval(value.showLeafLabels)
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
            _frustumModel_.Update(value.frustumModel)
            _navigationSensitivity_.Update(value.navigationSensitivity)
            _importTriangleSize_.Update(value.importTriangleSize)
            _arrowLength_.Update(value.arrowLength)
            _arrowThickness_.Update(value.arrowThickness)
            _dnsPlaneSize_.Update(value.dnsPlaneSize)
            _offset_.Update(value.offset)
            _pickingTolerance_.Update(value.pickingTolerance)
            _lodColoring_.Value <- value.lodColoring
            _drawOrientationCube_.Value <- value.drawOrientationCube
            _filterTexture_.Value <- value.filterTexture
            _showExplorationPointGui_.Value <- value.showExplorationPointGui
            _showLeafLabels_.Value <- value.showLeafLabels
    member __.Current = __adaptive
    member __.version = __value.version
    member __.nearPlane = _nearPlane_
    member __.farPlane = _farPlane_
    member __.frustumModel = _frustumModel_
    member __.navigationSensitivity = _navigationSensitivity_
    member __.importTriangleSize = _importTriangleSize_
    member __.arrowLength = _arrowLength_
    member __.arrowThickness = _arrowThickness_
    member __.dnsPlaneSize = _dnsPlaneSize_
    member __.offset = _offset_
    member __.pickingTolerance = _pickingTolerance_
    member __.lodColoring = _lodColoring_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.drawOrientationCube = _drawOrientationCube_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.filterTexture = _filterTexture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showExplorationPointGui = _showExplorationPointGui_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showLeafLabels = _showLeafLabels_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ViewConfigModelLenses = 
    type ViewConfigModel with
        static member version_ = ((fun (self : ViewConfigModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ViewConfigModel) -> { self with version = value }))
        static member nearPlane_ = ((fun (self : ViewConfigModel) -> self.nearPlane), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with nearPlane = value }))
        static member farPlane_ = ((fun (self : ViewConfigModel) -> self.farPlane), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with farPlane = value }))
        static member frustumModel_ = ((fun (self : ViewConfigModel) -> self.frustumModel), (fun (value : FrustumModel) (self : ViewConfigModel) -> { self with frustumModel = value }))
        static member navigationSensitivity_ = ((fun (self : ViewConfigModel) -> self.navigationSensitivity), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with navigationSensitivity = value }))
        static member importTriangleSize_ = ((fun (self : ViewConfigModel) -> self.importTriangleSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with importTriangleSize = value }))
        static member arrowLength_ = ((fun (self : ViewConfigModel) -> self.arrowLength), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with arrowLength = value }))
        static member arrowThickness_ = ((fun (self : ViewConfigModel) -> self.arrowThickness), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with arrowThickness = value }))
        static member dnsPlaneSize_ = ((fun (self : ViewConfigModel) -> self.dnsPlaneSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with dnsPlaneSize = value }))
        static member offset_ = ((fun (self : ViewConfigModel) -> self.offset), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with offset = value }))
        static member pickingTolerance_ = ((fun (self : ViewConfigModel) -> self.pickingTolerance), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewConfigModel) -> { self with pickingTolerance = value }))
        static member lodColoring_ = ((fun (self : ViewConfigModel) -> self.lodColoring), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewConfigModel) -> { self with lodColoring = value }))
        static member drawOrientationCube_ = ((fun (self : ViewConfigModel) -> self.drawOrientationCube), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewConfigModel) -> { self with drawOrientationCube = value }))
        static member filterTexture_ = ((fun (self : ViewConfigModel) -> self.filterTexture), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewConfigModel) -> { self with filterTexture = value }))
        static member showExplorationPointGui_ = ((fun (self : ViewConfigModel) -> self.showExplorationPointGui), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewConfigModel) -> { self with showExplorationPointGui = value }))
        static member showLeafLabels_ = ((fun (self : ViewConfigModel) -> self.showLeafLabels), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewConfigModel) -> { self with showLeafLabels = value }))

