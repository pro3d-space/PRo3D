//52b50b77-282f-7864-5c40-10f5edd4298b
//d502d29b-902e-e80c-598e-b9fc438eee99
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Comparison

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Comparison
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveVertexStatistics(value : VertexStatistics) =
    let _avgDistance_ = FSharp.Data.Adaptive.cval(value.avgDistance)
    let _maxDistance_ = FSharp.Data.Adaptive.cval(value.maxDistance)
    let _minDistance_ = FSharp.Data.Adaptive.cval(value.minDistance)
    let _diffPoints1_ = FSharp.Data.Adaptive.cval(value.diffPoints1)
    let _diffPoints2_ = FSharp.Data.Adaptive.cval(value.diffPoints2)
    let _trafo1_ = FSharp.Data.Adaptive.cval(value.trafo1)
    let _trafo2_ = FSharp.Data.Adaptive.cval(value.trafo2)
    let _distances_ = FSharp.Data.Adaptive.cval(value.distances)
    let _colorLegend_ = PRo3D.Base.AdaptiveFalseColorsModel(value.colorLegend)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : VertexStatistics) = AdaptiveVertexStatistics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : VertexStatistics) -> AdaptiveVertexStatistics(value)) (fun (adaptive : AdaptiveVertexStatistics) (value : VertexStatistics) -> adaptive.Update(value))
    member __.Update(value : VertexStatistics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<VertexStatistics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _avgDistance_.Value <- value.avgDistance
            _maxDistance_.Value <- value.maxDistance
            _minDistance_.Value <- value.minDistance
            _diffPoints1_.Value <- value.diffPoints1
            _diffPoints2_.Value <- value.diffPoints2
            _trafo1_.Value <- value.trafo1
            _trafo2_.Value <- value.trafo2
            _distances_.Value <- value.distances
            _colorLegend_.Update(value.colorLegend)
    member __.Current = __adaptive
    member __.avgDistance = _avgDistance_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.maxDistance = _maxDistance_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.minDistance = _minDistance_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.diffPoints1 = _diffPoints1_ :> FSharp.Data.Adaptive.aval<(Aardvark.Base.V3d)[]>
    member __.diffPoints2 = _diffPoints2_ :> FSharp.Data.Adaptive.aval<(Aardvark.Base.V3d)[]>
    member __.trafo1 = _trafo1_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.trafo2 = _trafo2_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.distances = _distances_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>>
    member __.colorLegend = _colorLegend_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module VertexStatisticsLenses = 
    type VertexStatistics with
        static member avgDistance_ = ((fun (self : VertexStatistics) -> self.avgDistance), (fun (value : Microsoft.FSharp.Core.float) (self : VertexStatistics) -> { self with avgDistance = value }))
        static member maxDistance_ = ((fun (self : VertexStatistics) -> self.maxDistance), (fun (value : Microsoft.FSharp.Core.float) (self : VertexStatistics) -> { self with maxDistance = value }))
        static member minDistance_ = ((fun (self : VertexStatistics) -> self.minDistance), (fun (value : Microsoft.FSharp.Core.float) (self : VertexStatistics) -> { self with minDistance = value }))
        static member diffPoints1_ = ((fun (self : VertexStatistics) -> self.diffPoints1), (fun (value : (Aardvark.Base.V3d)[]) (self : VertexStatistics) -> { self with diffPoints1 = value }))
        static member diffPoints2_ = ((fun (self : VertexStatistics) -> self.diffPoints2), (fun (value : (Aardvark.Base.V3d)[]) (self : VertexStatistics) -> { self with diffPoints2 = value }))
        static member trafo1_ = ((fun (self : VertexStatistics) -> self.trafo1), (fun (value : Aardvark.Base.Trafo3d) (self : VertexStatistics) -> { self with trafo1 = value }))
        static member trafo2_ = ((fun (self : VertexStatistics) -> self.trafo2), (fun (value : Aardvark.Base.Trafo3d) (self : VertexStatistics) -> { self with trafo2 = value }))
        static member distances_ = ((fun (self : VertexStatistics) -> self.distances), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>) (self : VertexStatistics) -> { self with distances = value }))
        static member colorLegend_ = ((fun (self : VertexStatistics) -> self.colorLegend), (fun (value : PRo3D.Base.FalseColorsModel) (self : VertexStatistics) -> { self with colorLegend = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveAreaSelection(value : AreaSelection) =
    let _label_ = FSharp.Data.Adaptive.cval(value.label)
    let _radius_ = FSharp.Data.Adaptive.cval(value.radius)
    let _location_ = FSharp.Data.Adaptive.cval(value.location)
    let _highResolution_ = FSharp.Data.Adaptive.cval(value.highResolution)
    let _visible_ = FSharp.Data.Adaptive.cval(value.visible)
    let _surfaceTrafo_ = FSharp.Data.Adaptive.cval(value.surfaceTrafo)
    let _verticesSurf1_ = FSharp.Data.Adaptive.clist(value.verticesSurf1)
    let _verticesSurf2_ = FSharp.Data.Adaptive.clist(value.verticesSurf2)
    let _statistics_ =
        let inline __arg2 (o : System.Object) (v : VertexStatistics) =
            (unbox<AdaptiveVertexStatistics> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : VertexStatistics) =
            (unbox<AdaptiveVertexStatistics> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Comparison.VertexStatistics, PRo3D.Comparison.AdaptiveVertexStatistics, PRo3D.Comparison.AdaptiveVertexStatistics>(value.statistics, (fun (v : VertexStatistics) -> AdaptiveVertexStatistics(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveVertexStatistics> o), (fun (v : VertexStatistics) -> AdaptiveVertexStatistics(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveVertexStatistics> o))
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : AreaSelection) = AdaptiveAreaSelection(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : AreaSelection) -> AdaptiveAreaSelection(value)) (fun (adaptive : AdaptiveAreaSelection) (value : AreaSelection) -> adaptive.Update(value))
    member __.Update(value : AreaSelection) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AreaSelection>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _label_.Value <- value.label
            _radius_.Value <- value.radius
            _location_.Value <- value.location
            _highResolution_.Value <- value.highResolution
            _visible_.Value <- value.visible
            _surfaceTrafo_.Value <- value.surfaceTrafo
            _verticesSurf1_.Value <- value.verticesSurf1
            _verticesSurf2_.Value <- value.verticesSurf2
            _statistics_.Update(value.statistics)
    member __.Current = __adaptive
    member __.id = __value.id
    member __.label = _label_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.radius = _radius_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.location = _location_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.highResolution = _highResolution_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.visible = _visible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.surfaceTrafo = _surfaceTrafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.verticesSurf1 = _verticesSurf1_ :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>
    member __.verticesSurf2 = _verticesSurf2_ :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>
    member __.statistics = _statistics_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<VertexStatistics, AdaptiveVertexStatistics, AdaptiveVertexStatistics>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AreaSelectionLenses = 
    type AreaSelection with
        static member id_ = ((fun (self : AreaSelection) -> self.id), (fun (value : System.Guid) (self : AreaSelection) -> { self with id = value }))
        static member label_ = ((fun (self : AreaSelection) -> self.label), (fun (value : Microsoft.FSharp.Core.string) (self : AreaSelection) -> { self with label = value }))
        static member radius_ = ((fun (self : AreaSelection) -> self.radius), (fun (value : Microsoft.FSharp.Core.float) (self : AreaSelection) -> { self with radius = value }))
        static member location_ = ((fun (self : AreaSelection) -> self.location), (fun (value : Aardvark.Base.V3d) (self : AreaSelection) -> { self with location = value }))
        static member highResolution_ = ((fun (self : AreaSelection) -> self.highResolution), (fun (value : Microsoft.FSharp.Core.bool) (self : AreaSelection) -> { self with highResolution = value }))
        static member visible_ = ((fun (self : AreaSelection) -> self.visible), (fun (value : Microsoft.FSharp.Core.bool) (self : AreaSelection) -> { self with visible = value }))
        static member surfaceTrafo_ = ((fun (self : AreaSelection) -> self.surfaceTrafo), (fun (value : Aardvark.Base.Trafo3d) (self : AreaSelection) -> { self with surfaceTrafo = value }))
        static member verticesSurf1_ = ((fun (self : AreaSelection) -> self.verticesSurf1), (fun (value : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) (self : AreaSelection) -> { self with verticesSurf1 = value }))
        static member verticesSurf2_ = ((fun (self : AreaSelection) -> self.verticesSurf2), (fun (value : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) (self : AreaSelection) -> { self with verticesSurf2 = value }))
        static member statistics_ = ((fun (self : AreaSelection) -> self.statistics), (fun (value : Microsoft.FSharp.Core.option<VertexStatistics>) (self : AreaSelection) -> { self with statistics = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveComparisonApp(value : ComparisonApp) =
    let _state_ = FSharp.Data.Adaptive.cval(value.state)
    let _threads_ = FSharp.Data.Adaptive.cval(value.threads)
    let _showMeasurementsSg_ = FSharp.Data.Adaptive.cval(value.showMeasurementsSg)
    let _nrOfCreatedAreas_ = FSharp.Data.Adaptive.cval(value.nrOfCreatedAreas)
    let _originMode_ = FSharp.Data.Adaptive.cval(value.originMode)
    let _surface1_ = FSharp.Data.Adaptive.cval(value.surface1)
    let _surface2_ = FSharp.Data.Adaptive.cval(value.surface2)
    let _surfaceMeasurements_ = FSharp.Data.Adaptive.cval(value.surfaceMeasurements)
    let _annotationMeasurements_ = FSharp.Data.Adaptive.cval(value.annotationMeasurements)
    let _surfaceGeometryType_ = FSharp.Data.Adaptive.cval(value.surfaceGeometryType)
    let _initialAreaSize_ = Aardvark.UI.AdaptiveNumericInput(value.initialAreaSize)
    let _pointSizeFactor_ = Aardvark.UI.AdaptiveNumericInput(value.pointSizeFactor)
    let _selectedArea_ = FSharp.Data.Adaptive.cval(value.selectedArea)
    let _isEditingArea_ = FSharp.Data.Adaptive.cval(value.isEditingArea)
    let _areas_ =
        let inline __arg2 (m : AdaptiveAreaSelection) (v : AreaSelection) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.areas, (fun (v : AreaSelection) -> AdaptiveAreaSelection(v)), __arg2, (fun (m : AdaptiveAreaSelection) -> m))
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ComparisonApp) = AdaptiveComparisonApp(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ComparisonApp) -> AdaptiveComparisonApp(value)) (fun (adaptive : AdaptiveComparisonApp) (value : ComparisonApp) -> adaptive.Update(value))
    member __.Update(value : ComparisonApp) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ComparisonApp>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _state_.Value <- value.state
            _threads_.Value <- value.threads
            _showMeasurementsSg_.Value <- value.showMeasurementsSg
            _nrOfCreatedAreas_.Value <- value.nrOfCreatedAreas
            _originMode_.Value <- value.originMode
            _surface1_.Value <- value.surface1
            _surface2_.Value <- value.surface2
            _surfaceMeasurements_.Value <- value.surfaceMeasurements
            _annotationMeasurements_.Value <- value.annotationMeasurements
            _surfaceGeometryType_.Value <- value.surfaceGeometryType
            _initialAreaSize_.Update(value.initialAreaSize)
            _pointSizeFactor_.Update(value.pointSizeFactor)
            _selectedArea_.Value <- value.selectedArea
            _isEditingArea_.Value <- value.isEditingArea
            _areas_.Update(value.areas)
    member __.Current = __adaptive
    member __.state = _state_ :> FSharp.Data.Adaptive.aval<ComparisonAppState>
    member __.threads = _threads_ :> FSharp.Data.Adaptive.aval<FSharp.Data.Adaptive.ThreadPool<ComparisonAction>>
    member __.showMeasurementsSg = _showMeasurementsSg_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.nrOfCreatedAreas = _nrOfCreatedAreas_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.originMode = _originMode_ :> FSharp.Data.Adaptive.aval<OriginMode>
    member __.surface1 = _surface1_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.surface2 = _surface2_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.surfaceMeasurements = _surfaceMeasurements_ :> FSharp.Data.Adaptive.aval<SurfaceComparison>
    member __.annotationMeasurements = _annotationMeasurements_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<AnnotationComparison>>
    member __.surfaceGeometryType = _surfaceGeometryType_ :> FSharp.Data.Adaptive.aval<DistanceMode>
    member __.initialAreaSize = _initialAreaSize_
    member __.pointSizeFactor = _pointSizeFactor_
    member __.selectedArea = _selectedArea_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.Guid>>
    member __.isEditingArea = _isEditingArea_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.areas = _areas_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveAreaSelection>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ComparisonAppLenses = 
    type ComparisonApp with
        static member state_ = ((fun (self : ComparisonApp) -> self.state), (fun (value : ComparisonAppState) (self : ComparisonApp) -> { self with state = value }))
        static member threads_ = ((fun (self : ComparisonApp) -> self.threads), (fun (value : FSharp.Data.Adaptive.ThreadPool<ComparisonAction>) (self : ComparisonApp) -> { self with threads = value }))
        static member showMeasurementsSg_ = ((fun (self : ComparisonApp) -> self.showMeasurementsSg), (fun (value : Microsoft.FSharp.Core.bool) (self : ComparisonApp) -> { self with showMeasurementsSg = value }))
        static member nrOfCreatedAreas_ = ((fun (self : ComparisonApp) -> self.nrOfCreatedAreas), (fun (value : Microsoft.FSharp.Core.int) (self : ComparisonApp) -> { self with nrOfCreatedAreas = value }))
        static member originMode_ = ((fun (self : ComparisonApp) -> self.originMode), (fun (value : OriginMode) (self : ComparisonApp) -> { self with originMode = value }))
        static member surface1_ = ((fun (self : ComparisonApp) -> self.surface1), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : ComparisonApp) -> { self with surface1 = value }))
        static member surface2_ = ((fun (self : ComparisonApp) -> self.surface2), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : ComparisonApp) -> { self with surface2 = value }))
        static member surfaceMeasurements_ = ((fun (self : ComparisonApp) -> self.surfaceMeasurements), (fun (value : SurfaceComparison) (self : ComparisonApp) -> { self with surfaceMeasurements = value }))
        static member annotationMeasurements_ = ((fun (self : ComparisonApp) -> self.annotationMeasurements), (fun (value : Microsoft.FSharp.Collections.list<AnnotationComparison>) (self : ComparisonApp) -> { self with annotationMeasurements = value }))
        static member surfaceGeometryType_ = ((fun (self : ComparisonApp) -> self.surfaceGeometryType), (fun (value : DistanceMode) (self : ComparisonApp) -> { self with surfaceGeometryType = value }))
        static member initialAreaSize_ = ((fun (self : ComparisonApp) -> self.initialAreaSize), (fun (value : Aardvark.UI.NumericInput) (self : ComparisonApp) -> { self with initialAreaSize = value }))
        static member pointSizeFactor_ = ((fun (self : ComparisonApp) -> self.pointSizeFactor), (fun (value : Aardvark.UI.NumericInput) (self : ComparisonApp) -> { self with pointSizeFactor = value }))
        static member selectedArea_ = ((fun (self : ComparisonApp) -> self.selectedArea), (fun (value : Microsoft.FSharp.Core.option<System.Guid>) (self : ComparisonApp) -> { self with selectedArea = value }))
        static member isEditingArea_ = ((fun (self : ComparisonApp) -> self.isEditingArea), (fun (value : Microsoft.FSharp.Core.bool) (self : ComparisonApp) -> { self with isEditingArea = value }))
        static member areas_ = ((fun (self : ComparisonApp) -> self.areas), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, AreaSelection>) (self : ComparisonApp) -> { self with areas = value }))

