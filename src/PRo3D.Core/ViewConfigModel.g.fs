//fb42f2d5-50e6-3a96-7dce-a016af9a6e96
//4e19262f-62f0-9207-7c3d-d8346bb66f8c
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
    let _filterTexture_ = FSharp.Data.Adaptive.cval(value.filterTexture)
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
            _filterTexture_.Value <- value.filterTexture
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
    member __.filterTexture = _filterTexture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
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
        static member filterTexture_ = ((fun (self : ViewConfigModel) -> self.filterTexture), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewConfigModel) -> { self with filterTexture = value }))

