//a3a398a4-803b-88c2-4fca-843184f5803e
//5311ac9c-9671-f671-249b-030c5b84bcb2
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
type AdaptiveGeologicSurface(value : GeologicSurface) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _guid_ = FSharp.Data.Adaptive.cval(value.guid)
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _isVisible_ = FSharp.Data.Adaptive.cval(value.isVisible)
    let _view_ = FSharp.Data.Adaptive.cval(value.view)
    let _points1_ = FSharp.Data.Adaptive.clist(value.points1)
    let _points2_ = FSharp.Data.Adaptive.clist(value.points2)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let _transparency_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.transparency)
    let _thickness_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.thickness)
    let _invertMeshing_ = FSharp.Data.Adaptive.cval(value.invertMeshing)
    let _sgGeoSurface_ = FSharp.Data.Adaptive.cval(value.sgGeoSurface)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : GeologicSurface) = AdaptiveGeologicSurface(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : GeologicSurface) -> AdaptiveGeologicSurface(value)) (fun (adaptive : AdaptiveGeologicSurface) (value : GeologicSurface) -> adaptive.Update(value))
    member __.Update(value : GeologicSurface) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<GeologicSurface>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _guid_.Value <- value.guid
            _name_.Value <- value.name
            _isVisible_.Value <- value.isVisible
            _view_.Value <- value.view
            _points1_.Value <- value.points1
            _points2_.Value <- value.points2
            _color_.Update(value.color)
            _transparency_.Update(value.transparency)
            _thickness_.Update(value.thickness)
            _invertMeshing_.Value <- value.invertMeshing
            _sgGeoSurface_.Value <- value.sgGeoSurface
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = _guid_ :> FSharp.Data.Adaptive.aval<System.Guid>
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.isVisible = _isVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.view = _view_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.points1 = _points1_ :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>
    member __.points2 = _points2_ :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>
    member __.color = _color_
    member __.transparency = _transparency_
    member __.thickness = _thickness_
    member __.invertMeshing = _invertMeshing_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.sgGeoSurface = _sgGeoSurface_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<(Aardvark.Base.Triangle3d * Aardvark.Base.C4b)>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module GeologicSurfaceLenses = 
    type GeologicSurface with
        static member version_ = ((fun (self : GeologicSurface) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : GeologicSurface) -> { self with version = value }))
        static member guid_ = ((fun (self : GeologicSurface) -> self.guid), (fun (value : System.Guid) (self : GeologicSurface) -> { self with guid = value }))
        static member name_ = ((fun (self : GeologicSurface) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : GeologicSurface) -> { self with name = value }))
        static member isVisible_ = ((fun (self : GeologicSurface) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : GeologicSurface) -> { self with isVisible = value }))
        static member view_ = ((fun (self : GeologicSurface) -> self.view), (fun (value : Aardvark.Rendering.CameraView) (self : GeologicSurface) -> { self with view = value }))
        static member points1_ = ((fun (self : GeologicSurface) -> self.points1), (fun (value : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) (self : GeologicSurface) -> { self with points1 = value }))
        static member points2_ = ((fun (self : GeologicSurface) -> self.points2), (fun (value : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) (self : GeologicSurface) -> { self with points2 = value }))
        static member color_ = ((fun (self : GeologicSurface) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : GeologicSurface) -> { self with color = value }))
        static member transparency_ = ((fun (self : GeologicSurface) -> self.transparency), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : GeologicSurface) -> { self with transparency = value }))
        static member thickness_ = ((fun (self : GeologicSurface) -> self.thickness), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : GeologicSurface) -> { self with thickness = value }))
        static member invertMeshing_ = ((fun (self : GeologicSurface) -> self.invertMeshing), (fun (value : Microsoft.FSharp.Core.bool) (self : GeologicSurface) -> { self with invertMeshing = value }))
        static member sgGeoSurface_ = ((fun (self : GeologicSurface) -> self.sgGeoSurface), (fun (value : Microsoft.FSharp.Collections.List<(Aardvark.Base.Triangle3d * Aardvark.Base.C4b)>) (self : GeologicSurface) -> { self with sgGeoSurface = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveGeologicSurfacesModel(value : GeologicSurfacesModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _geologicSurfaces_ =
        let inline __arg2 (m : AdaptiveGeologicSurface) (v : GeologicSurface) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.geologicSurfaces, (fun (v : GeologicSurface) -> AdaptiveGeologicSurface(v)), __arg2, (fun (m : AdaptiveGeologicSurface) -> m))
    let _selectedGeologicSurface_ = FSharp.Data.Adaptive.cval(value.selectedGeologicSurface)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : GeologicSurfacesModel) = AdaptiveGeologicSurfacesModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : GeologicSurfacesModel) -> AdaptiveGeologicSurfacesModel(value)) (fun (adaptive : AdaptiveGeologicSurfacesModel) (value : GeologicSurfacesModel) -> adaptive.Update(value))
    member __.Update(value : GeologicSurfacesModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<GeologicSurfacesModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _geologicSurfaces_.Update(value.geologicSurfaces)
            _selectedGeologicSurface_.Value <- value.selectedGeologicSurface
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.geologicSurfaces = _geologicSurfaces_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveGeologicSurface>
    member __.selectedGeologicSurface = _selectedGeologicSurface_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module GeologicSurfacesModelLenses = 
    type GeologicSurfacesModel with
        static member version_ = ((fun (self : GeologicSurfacesModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : GeologicSurfacesModel) -> { self with version = value }))
        static member geologicSurfaces_ = ((fun (self : GeologicSurfacesModel) -> self.geologicSurfaces), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, GeologicSurface>) (self : GeologicSurfacesModel) -> { self with geologicSurfaces = value }))
        static member selectedGeologicSurface_ = ((fun (self : GeologicSurfacesModel) -> self.selectedGeologicSurface), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : GeologicSurfacesModel) -> { self with selectedGeologicSurface = value }))

