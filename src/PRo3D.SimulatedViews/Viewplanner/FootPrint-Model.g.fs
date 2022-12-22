//0a5b8430-6b4e-861b-9040-a788cec5621f
//81f925df-c82e-e235-bcfe-289a46ea0551
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.FootPrint

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.FootPrint
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSimulatedViewData(value : SimulatedViewData) =
    let _fileInfo_ = FSharp.Data.Adaptive.cval(value.fileInfo)
    let _calibration_ = FSharp.Data.Adaptive.cval(value.calibration)
    let _acquisition_ = FSharp.Data.Adaptive.cval(value.acquisition)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SimulatedViewData) = AdaptiveSimulatedViewData(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SimulatedViewData) -> AdaptiveSimulatedViewData(value)) (fun (adaptive : AdaptiveSimulatedViewData) (value : SimulatedViewData) -> adaptive.Update(value))
    member __.Update(value : SimulatedViewData) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SimulatedViewData>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _fileInfo_.Value <- value.fileInfo
            _calibration_.Value <- value.calibration
            _acquisition_.Value <- value.acquisition
    member __.Current = __adaptive
    member __.fileInfo = _fileInfo_ :> FSharp.Data.Adaptive.aval<FileInfo>
    member __.calibration = _calibration_ :> FSharp.Data.Adaptive.aval<Calibration>
    member __.acquisition = _acquisition_ :> FSharp.Data.Adaptive.aval<Acquisition>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SimulatedViewDataLenses = 
    type SimulatedViewData with
        static member fileInfo_ = ((fun (self : SimulatedViewData) -> self.fileInfo), (fun (value : FileInfo) (self : SimulatedViewData) -> { self with fileInfo = value }))
        static member calibration_ = ((fun (self : SimulatedViewData) -> self.calibration), (fun (value : Calibration) (self : SimulatedViewData) -> { self with calibration = value }))
        static member acquisition_ = ((fun (self : SimulatedViewData) -> self.acquisition), (fun (value : Acquisition) (self : SimulatedViewData) -> { self with acquisition = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveFootPrint(value : FootPrint) =
    let _vpId_ = FSharp.Data.Adaptive.cval(value.vpId)
    let _isVisible_ = FSharp.Data.Adaptive.cval(value.isVisible)
    let _projectionMatrix_ = FSharp.Data.Adaptive.cval(value.projectionMatrix)
    let _instViewMatrix_ = FSharp.Data.Adaptive.cval(value.instViewMatrix)
    let _projTex_ = FSharp.Data.Adaptive.cval(value.projTex)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : FootPrint) = AdaptiveFootPrint(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : FootPrint) -> AdaptiveFootPrint(value)) (fun (adaptive : AdaptiveFootPrint) (value : FootPrint) -> adaptive.Update(value))
    member __.Update(value : FootPrint) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<FootPrint>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _vpId_.Value <- value.vpId
            _isVisible_.Value <- value.isVisible
            _projectionMatrix_.Value <- value.projectionMatrix
            _instViewMatrix_.Value <- value.instViewMatrix
            _projTex_.Value <- value.projTex
    member __.Current = __adaptive
    member __.vpId = _vpId_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.Guid>>
    member __.isVisible = _isVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.projectionMatrix = _projectionMatrix_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.M44d>
    member __.instViewMatrix = _instViewMatrix_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.M44d>
    member __.projTex = _projTex_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.ITexture>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module FootPrintLenses = 
    type FootPrint with
        static member vpId_ = ((fun (self : FootPrint) -> self.vpId), (fun (value : Microsoft.FSharp.Core.option<System.Guid>) (self : FootPrint) -> { self with vpId = value }))
        static member isVisible_ = ((fun (self : FootPrint) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : FootPrint) -> { self with isVisible = value }))
        static member projectionMatrix_ = ((fun (self : FootPrint) -> self.projectionMatrix), (fun (value : Aardvark.Base.M44d) (self : FootPrint) -> { self with projectionMatrix = value }))
        static member instViewMatrix_ = ((fun (self : FootPrint) -> self.instViewMatrix), (fun (value : Aardvark.Base.M44d) (self : FootPrint) -> { self with instViewMatrix = value }))
        static member projTex_ = ((fun (self : FootPrint) -> self.projTex), (fun (value : Aardvark.Rendering.ITexture) (self : FootPrint) -> { self with projTex = value }))

