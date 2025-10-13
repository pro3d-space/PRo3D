//5f52137c-4504-3d0c-e38d-92b4e1fe881d
//011fab16-2e0f-378e-5edf-99b0128f7a93
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Core.Drawing

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Core.Drawing
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveAutomaticGeoJsonExport(value : AutomaticGeoJsonExport) =
    let _enabled_ = FSharp.Data.Adaptive.cval(value.enabled)
    let _lastGeoJsonPath_ = FSharp.Data.Adaptive.cval(value.lastGeoJsonPath)
    let _lastGeoJsonPathXyz_ = FSharp.Data.Adaptive.cval(value.lastGeoJsonPathXyz)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : AutomaticGeoJsonExport) = AdaptiveAutomaticGeoJsonExport(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : AutomaticGeoJsonExport) -> AdaptiveAutomaticGeoJsonExport(value)) (fun (adaptive : AdaptiveAutomaticGeoJsonExport) (value : AutomaticGeoJsonExport) -> adaptive.Update(value))
    member __.Update(value : AutomaticGeoJsonExport) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AutomaticGeoJsonExport>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _enabled_.Value <- value.enabled
            _lastGeoJsonPath_.Value <- value.lastGeoJsonPath
            _lastGeoJsonPathXyz_.Value <- value.lastGeoJsonPathXyz
    member __.Current = __adaptive
    member __.enabled = _enabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.lastGeoJsonPath = _lastGeoJsonPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>>
    member __.lastGeoJsonPathXyz = _lastGeoJsonPathXyz_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AutomaticGeoJsonExportLenses = 
    type AutomaticGeoJsonExport with
        static member enabled_ = ((fun (self : AutomaticGeoJsonExport) -> self.enabled), (fun (value : Microsoft.FSharp.Core.bool) (self : AutomaticGeoJsonExport) -> { self with enabled = value }))
        static member lastGeoJsonPath_ = ((fun (self : AutomaticGeoJsonExport) -> self.lastGeoJsonPath), (fun (value : Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>) (self : AutomaticGeoJsonExport) -> { self with lastGeoJsonPath = value }))
        static member lastGeoJsonPathXyz_ = ((fun (self : AutomaticGeoJsonExport) -> self.lastGeoJsonPathXyz), (fun (value : Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>) (self : AutomaticGeoJsonExport) -> { self with lastGeoJsonPathXyz = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveDrawingModel(value : DrawingModel) =
    let _draw_ = FSharp.Data.Adaptive.cval(value.draw)
    let _pick_ = FSharp.Data.Adaptive.cval(value.pick)
    let _multi_ = FSharp.Data.Adaptive.cval(value.multi)
    let _hoverPosition_ = FSharp.Data.Adaptive.cval(value.hoverPosition)
    let _working_ =
        let inline __arg2 (o : System.Object) (v : PRo3D.Base.Annotation.Annotation) =
            (unbox<PRo3D.Base.Annotation.AdaptiveAnnotation> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : PRo3D.Base.Annotation.Annotation) =
            (unbox<PRo3D.Base.Annotation.AdaptiveAnnotation> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Base.Annotation.Annotation, PRo3D.Base.Annotation.AdaptiveAnnotation, PRo3D.Base.Annotation.AdaptiveAnnotation>(value.working, (fun (v : PRo3D.Base.Annotation.Annotation) -> PRo3D.Base.Annotation.AdaptiveAnnotation(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<PRo3D.Base.Annotation.AdaptiveAnnotation> o), (fun (v : PRo3D.Base.Annotation.Annotation) -> PRo3D.Base.Annotation.AdaptiveAnnotation(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<PRo3D.Base.Annotation.AdaptiveAnnotation> o))
    let _projection_ = FSharp.Data.Adaptive.cval(value.projection)
    let _geometry_ = FSharp.Data.Adaptive.cval(value.geometry)
    let _semantic_ = FSharp.Data.Adaptive.cval(value.semantic)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let _thickness_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.thickness)
    let _samplingAmount_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.samplingAmount)
    let _samplingUnit_ = FSharp.Data.Adaptive.cval(value.samplingUnit)
    let _samplingDistance_ = FSharp.Data.Adaptive.cval(value.samplingDistance)
    let _annotations_ = PRo3D.Core.AdaptiveGroupsModel(value.annotations)
    let _exportPath_ = FSharp.Data.Adaptive.cval(value.exportPath)
    let _pendingIntersections_ = FSharp.Data.Adaptive.cval(value.pendingIntersections)
    let _past_ = FSharp.Data.Adaptive.cval(value.past)
    let _future_ = FSharp.Data.Adaptive.cval(value.future)
    let _dnsColorLegend_ = PRo3D.Base.AdaptiveFalseColorsModel(value.dnsColorLegend)
    let _haltonPoints_ = FSharp.Data.Adaptive.cval(value.haltonPoints)
    let _automaticGeoJsonExport_ = AdaptiveAutomaticGeoJsonExport(value.automaticGeoJsonExport)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : DrawingModel) = AdaptiveDrawingModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : DrawingModel) -> AdaptiveDrawingModel(value)) (fun (adaptive : AdaptiveDrawingModel) (value : DrawingModel) -> adaptive.Update(value))
    member __.Update(value : DrawingModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<DrawingModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _draw_.Value <- value.draw
            _pick_.Value <- value.pick
            _multi_.Value <- value.multi
            _hoverPosition_.Value <- value.hoverPosition
            _working_.Update(value.working)
            _projection_.Value <- value.projection
            _geometry_.Value <- value.geometry
            _semantic_.Value <- value.semantic
            _color_.Update(value.color)
            _thickness_.Update(value.thickness)
            _samplingAmount_.Update(value.samplingAmount)
            _samplingUnit_.Value <- value.samplingUnit
            _samplingDistance_.Value <- value.samplingDistance
            _annotations_.Update(value.annotations)
            _exportPath_.Value <- value.exportPath
            _pendingIntersections_.Value <- value.pendingIntersections
            _past_.Value <- value.past
            _future_.Value <- value.future
            _dnsColorLegend_.Update(value.dnsColorLegend)
            _haltonPoints_.Value <- value.haltonPoints
            _automaticGeoJsonExport_.Update(value.automaticGeoJsonExport)
    member __.Current = __adaptive
    member __.draw = _draw_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.pick = _pick_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.multi = _multi_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.hoverPosition = _hoverPosition_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Aardvark.Base.Trafo3d>>
    member __.working = _working_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<PRo3D.Base.Annotation.Annotation, PRo3D.Base.Annotation.AdaptiveAnnotation, PRo3D.Base.Annotation.AdaptiveAnnotation>>
    member __.projection = _projection_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.Projection>
    member __.geometry = _geometry_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.Geometry>
    member __.semantic = _semantic_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.Semantic>
    member __.color = _color_
    member __.thickness = _thickness_
    member __.samplingAmount = _samplingAmount_
    member __.samplingUnit = _samplingUnit_ :> FSharp.Data.Adaptive.aval<SamplingUnit>
    member __.samplingDistance = _samplingDistance_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.annotations = _annotations_
    member __.exportPath = _exportPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>>
    member __.pendingIntersections = _pendingIntersections_ :> FSharp.Data.Adaptive.aval<FSharp.Data.Adaptive.ThreadPool<DrawingAction>>
    member __.past = _past_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<DrawingModel>>
    member __.future = _future_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<DrawingModel>>
    member __.dnsColorLegend = _dnsColorLegend_
    member __.haltonPoints = _haltonPoints_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>>
    member __.automaticGeoJsonExport = _automaticGeoJsonExport_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module DrawingModelLenses = 
    type DrawingModel with
        static member draw_ = ((fun (self : DrawingModel) -> self.draw), (fun (value : Microsoft.FSharp.Core.bool) (self : DrawingModel) -> { self with draw = value }))
        static member pick_ = ((fun (self : DrawingModel) -> self.pick), (fun (value : Microsoft.FSharp.Core.bool) (self : DrawingModel) -> { self with pick = value }))
        static member multi_ = ((fun (self : DrawingModel) -> self.multi), (fun (value : Microsoft.FSharp.Core.bool) (self : DrawingModel) -> { self with multi = value }))
        static member hoverPosition_ = ((fun (self : DrawingModel) -> self.hoverPosition), (fun (value : Microsoft.FSharp.Core.option<Aardvark.Base.Trafo3d>) (self : DrawingModel) -> { self with hoverPosition = value }))
        static member working_ = ((fun (self : DrawingModel) -> self.working), (fun (value : Microsoft.FSharp.Core.Option<PRo3D.Base.Annotation.Annotation>) (self : DrawingModel) -> { self with working = value }))
        static member projection_ = ((fun (self : DrawingModel) -> self.projection), (fun (value : PRo3D.Base.Annotation.Projection) (self : DrawingModel) -> { self with projection = value }))
        static member geometry_ = ((fun (self : DrawingModel) -> self.geometry), (fun (value : PRo3D.Base.Annotation.Geometry) (self : DrawingModel) -> { self with geometry = value }))
        static member semantic_ = ((fun (self : DrawingModel) -> self.semantic), (fun (value : PRo3D.Base.Annotation.Semantic) (self : DrawingModel) -> { self with semantic = value }))
        static member color_ = ((fun (self : DrawingModel) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : DrawingModel) -> { self with color = value }))
        static member thickness_ = ((fun (self : DrawingModel) -> self.thickness), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : DrawingModel) -> { self with thickness = value }))
        static member samplingAmount_ = ((fun (self : DrawingModel) -> self.samplingAmount), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : DrawingModel) -> { self with samplingAmount = value }))
        static member samplingUnit_ = ((fun (self : DrawingModel) -> self.samplingUnit), (fun (value : SamplingUnit) (self : DrawingModel) -> { self with samplingUnit = value }))
        static member samplingDistance_ = ((fun (self : DrawingModel) -> self.samplingDistance), (fun (value : Microsoft.FSharp.Core.float) (self : DrawingModel) -> { self with samplingDistance = value }))
        static member annotations_ = ((fun (self : DrawingModel) -> self.annotations), (fun (value : PRo3D.Core.GroupsModel) (self : DrawingModel) -> { self with annotations = value }))
        static member exportPath_ = ((fun (self : DrawingModel) -> self.exportPath), (fun (value : Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>) (self : DrawingModel) -> { self with exportPath = value }))
        static member pendingIntersections_ = ((fun (self : DrawingModel) -> self.pendingIntersections), (fun (value : FSharp.Data.Adaptive.ThreadPool<DrawingAction>) (self : DrawingModel) -> { self with pendingIntersections = value }))
        static member past_ = ((fun (self : DrawingModel) -> self.past), (fun (value : Microsoft.FSharp.Core.Option<DrawingModel>) (self : DrawingModel) -> { self with past = value }))
        static member future_ = ((fun (self : DrawingModel) -> self.future), (fun (value : Microsoft.FSharp.Core.Option<DrawingModel>) (self : DrawingModel) -> { self with future = value }))
        static member dnsColorLegend_ = ((fun (self : DrawingModel) -> self.dnsColorLegend), (fun (value : PRo3D.Base.FalseColorsModel) (self : DrawingModel) -> { self with dnsColorLegend = value }))
        static member haltonPoints_ = ((fun (self : DrawingModel) -> self.haltonPoints), (fun (value : Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>) (self : DrawingModel) -> { self with haltonPoints = value }))
        static member automaticGeoJsonExport_ = ((fun (self : DrawingModel) -> self.automaticGeoJsonExport), (fun (value : AutomaticGeoJsonExport) (self : DrawingModel) -> { self with automaticGeoJsonExport = value }))

