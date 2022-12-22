//0628b933-295e-08a7-8915-f1da10e95443
//ba8656d7-033a-270e-dbc8-4f90753f5351
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.SimulatedViews

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.SimulatedViews
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveScreenshotModel(value : ScreenshotModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _width_ = Aardvark.UI.AdaptiveNumericInput(value.width)
    let _height_ = Aardvark.UI.AdaptiveNumericInput(value.height)
    let _backgroundColor_ = Aardvark.UI.AdaptiveColorInput(value.backgroundColor)
    let _imageFormat_ = FSharp.Data.Adaptive.cval(value.imageFormat)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ScreenshotModel) = AdaptiveScreenshotModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ScreenshotModel) -> AdaptiveScreenshotModel(value)) (fun (adaptive : AdaptiveScreenshotModel) (value : ScreenshotModel) -> adaptive.Update(value))
    member __.Update(value : ScreenshotModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ScreenshotModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _width_.Update(value.width)
            _height_.Update(value.height)
            _backgroundColor_.Update(value.backgroundColor)
            _imageFormat_.Value <- value.imageFormat
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.width = _width_
    member __.height = _height_
    member __.backgroundColor = _backgroundColor_
    member __.imageFormat = _imageFormat_ :> FSharp.Data.Adaptive.aval<ImageFormat>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ScreenshotModelLenses = 
    type ScreenshotModel with
        static member version_ = ((fun (self : ScreenshotModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ScreenshotModel) -> { self with version = value }))
        static member width_ = ((fun (self : ScreenshotModel) -> self.width), (fun (value : Aardvark.UI.NumericInput) (self : ScreenshotModel) -> { self with width = value }))
        static member height_ = ((fun (self : ScreenshotModel) -> self.height), (fun (value : Aardvark.UI.NumericInput) (self : ScreenshotModel) -> { self with height = value }))
        static member backgroundColor_ = ((fun (self : ScreenshotModel) -> self.backgroundColor), (fun (value : Aardvark.UI.ColorInput) (self : ScreenshotModel) -> { self with backgroundColor = value }))
        static member imageFormat_ = ((fun (self : ScreenshotModel) -> self.imageFormat), (fun (value : ImageFormat) (self : ScreenshotModel) -> { self with imageFormat = value }))

