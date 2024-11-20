//34ba5506-7401-8363-b9e1-dbd8a232bf39
//f21c771d-62d0-ad0d-2849-1c5e5dd6ea4d
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
type AdaptiveReferenceSystem(value : ReferenceSystem) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _origin_ = FSharp.Data.Adaptive.cval(value.origin)
    let _north_ = Aardvark.UI.Primitives.AdaptiveV3dInput(value.north)
    let _noffset_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.noffset)
    let _northO_ = FSharp.Data.Adaptive.cval(value.northO)
    let _up_ = Aardvark.UI.Primitives.AdaptiveV3dInput(value.up)
    let _isVisible_ = FSharp.Data.Adaptive.cval(value.isVisible)
    let _size_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.size)
    let _scaleChart_ = FSharp.Data.Adaptive.clist(value.scaleChart)
    let _selectedScale_ = FSharp.Data.Adaptive.cval(value.selectedScale)
    let _planet_ = FSharp.Data.Adaptive.cval(value.planet)
    let _textsize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.textsize)
    let _textcolor_ = Aardvark.UI.AdaptiveColorInput(value.textcolor)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ReferenceSystem) = AdaptiveReferenceSystem(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ReferenceSystem) -> AdaptiveReferenceSystem(value)) (fun (adaptive : AdaptiveReferenceSystem) (value : ReferenceSystem) -> adaptive.Update(value))
    member __.Update(value : ReferenceSystem) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ReferenceSystem>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _origin_.Value <- value.origin
            _north_.Update(value.north)
            _noffset_.Update(value.noffset)
            _northO_.Value <- value.northO
            _up_.Update(value.up)
            _isVisible_.Value <- value.isVisible
            _size_.Update(value.size)
            _scaleChart_.Value <- value.scaleChart
            _selectedScale_.Value <- value.selectedScale
            _planet_.Value <- value.planet
            _textsize_.Update(value.textsize)
            _textcolor_.Update(value.textcolor)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.origin = _origin_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.north = _north_
    member __.noffset = _noffset_
    member __.northO = _northO_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.up = _up_
    member __.isVisible = _isVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.size = _size_
    member __.scaleChart = _scaleChart_ :> FSharp.Data.Adaptive.alist<Microsoft.FSharp.Core.string>
    member __.selectedScale = _selectedScale_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.planet = _planet_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Planet>
    member __.textsize = _textsize_
    member __.textcolor = _textcolor_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ReferenceSystemLenses = 
    type ReferenceSystem with
        static member version_ = ((fun (self : ReferenceSystem) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ReferenceSystem) -> { self with version = value }))
        static member origin_ = ((fun (self : ReferenceSystem) -> self.origin), (fun (value : Aardvark.Base.V3d) (self : ReferenceSystem) -> { self with origin = value }))
        static member north_ = ((fun (self : ReferenceSystem) -> self.north), (fun (value : Aardvark.UI.Primitives.V3dInput) (self : ReferenceSystem) -> { self with north = value }))
        static member noffset_ = ((fun (self : ReferenceSystem) -> self.noffset), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ReferenceSystem) -> { self with noffset = value }))
        static member northO_ = ((fun (self : ReferenceSystem) -> self.northO), (fun (value : Aardvark.Base.V3d) (self : ReferenceSystem) -> { self with northO = value }))
        static member up_ = ((fun (self : ReferenceSystem) -> self.up), (fun (value : Aardvark.UI.Primitives.V3dInput) (self : ReferenceSystem) -> { self with up = value }))
        static member isVisible_ = ((fun (self : ReferenceSystem) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : ReferenceSystem) -> { self with isVisible = value }))
        static member size_ = ((fun (self : ReferenceSystem) -> self.size), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ReferenceSystem) -> { self with size = value }))
        static member scaleChart_ = ((fun (self : ReferenceSystem) -> self.scaleChart), (fun (value : FSharp.Data.Adaptive.IndexList<Microsoft.FSharp.Core.string>) (self : ReferenceSystem) -> { self with scaleChart = value }))
        static member selectedScale_ = ((fun (self : ReferenceSystem) -> self.selectedScale), (fun (value : Microsoft.FSharp.Core.string) (self : ReferenceSystem) -> { self with selectedScale = value }))
        static member planet_ = ((fun (self : ReferenceSystem) -> self.planet), (fun (value : PRo3D.Base.Planet) (self : ReferenceSystem) -> { self with planet = value }))
        static member textsize_ = ((fun (self : ReferenceSystem) -> self.textsize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ReferenceSystem) -> { self with textsize = value }))
        static member textcolor_ = ((fun (self : ReferenceSystem) -> self.textcolor), (fun (value : Aardvark.UI.ColorInput) (self : ReferenceSystem) -> { self with textcolor = value }))

