//7ee60322-ffc9-9bed-080f-d357fa699a22
//98622bb4-7bf2-1f2e-b011-a4b0e555e5e7
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Base.Annotation

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base.Annotation
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSegment(value : Segment) =
    let _startPoint_ = FSharp.Data.Adaptive.cval(value.startPoint)
    let _endPoint_ = FSharp.Data.Adaptive.cval(value.endPoint)
    let _points_ = FSharp.Data.Adaptive.clist(value.points)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Segment) = AdaptiveSegment(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Segment) -> AdaptiveSegment(value)) (fun (adaptive : AdaptiveSegment) (value : Segment) -> adaptive.Update(value))
    member __.Update(value : Segment) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Segment>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _startPoint_.Value <- value.startPoint
            _endPoint_.Value <- value.endPoint
            _points_.Value <- value.points
    member __.Current = __adaptive
    member __.startPoint = _startPoint_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.endPoint = _endPoint_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.points = _points_ :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SegmentLenses = 
    type Segment with
        static member startPoint_ = ((fun (self : Segment) -> self.startPoint), (fun (value : Aardvark.Base.V3d) (self : Segment) -> { self with startPoint = value }))
        static member endPoint_ = ((fun (self : Segment) -> self.endPoint), (fun (value : Aardvark.Base.V3d) (self : Segment) -> { self with endPoint = value }))
        static member points_ = ((fun (self : Segment) -> self.points), (fun (value : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) (self : Segment) -> { self with points = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveStatistics(value : Statistics) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
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
            _version_.Value <- value.version
            _average_.Value <- value.average
            _min_.Value <- value.min
            _max_.Value <- value.max
            _stdev_.Value <- value.stdev
            _sumOfSquares_.Value <- value.sumOfSquares
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.average = _average_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.min = _min_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.max = _max_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.stdev = _stdev_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.sumOfSquares = _sumOfSquares_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module StatisticsLenses = 
    type Statistics with
        static member version_ = ((fun (self : Statistics) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Statistics) -> { self with version = value }))
        static member average_ = ((fun (self : Statistics) -> self.average), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with average = value }))
        static member min_ = ((fun (self : Statistics) -> self.min), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with min = value }))
        static member max_ = ((fun (self : Statistics) -> self.max), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with max = value }))
        static member stdev_ = ((fun (self : Statistics) -> self.stdev), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with stdev = value }))
        static member sumOfSquares_ = ((fun (self : Statistics) -> self.sumOfSquares), (fun (value : Microsoft.FSharp.Core.float) (self : Statistics) -> { self with sumOfSquares = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveDipAndStrikeResults(value : DipAndStrikeResults) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _plane_ = FSharp.Data.Adaptive.cval(value.plane)
    let _dipAngle_ = FSharp.Data.Adaptive.cval(value.dipAngle)
    let _dipDirection_ = FSharp.Data.Adaptive.cval(value.dipDirection)
    let _strikeDirection_ = FSharp.Data.Adaptive.cval(value.strikeDirection)
    let _dipAzimuth_ = FSharp.Data.Adaptive.cval(value.dipAzimuth)
    let _strikeAzimuth_ = FSharp.Data.Adaptive.cval(value.strikeAzimuth)
    let _centerOfMass_ = FSharp.Data.Adaptive.cval(value.centerOfMass)
    let _error_ = AdaptiveStatistics(value.error)
    let _regressionInfo_ = FSharp.Data.Adaptive.cval(value.regressionInfo)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : DipAndStrikeResults) = AdaptiveDipAndStrikeResults(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : DipAndStrikeResults) -> AdaptiveDipAndStrikeResults(value)) (fun (adaptive : AdaptiveDipAndStrikeResults) (value : DipAndStrikeResults) -> adaptive.Update(value))
    member __.Update(value : DipAndStrikeResults) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<DipAndStrikeResults>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _plane_.Value <- value.plane
            _dipAngle_.Value <- value.dipAngle
            _dipDirection_.Value <- value.dipDirection
            _strikeDirection_.Value <- value.strikeDirection
            _dipAzimuth_.Value <- value.dipAzimuth
            _strikeAzimuth_.Value <- value.strikeAzimuth
            _centerOfMass_.Value <- value.centerOfMass
            _error_.Update(value.error)
            _regressionInfo_.Value <- value.regressionInfo
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.plane = _plane_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Plane3d>
    member __.dipAngle = _dipAngle_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.dipDirection = _dipDirection_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.strikeDirection = _strikeDirection_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.dipAzimuth = _dipAzimuth_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.strikeAzimuth = _strikeAzimuth_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.centerOfMass = _centerOfMass_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.error = _error_
    member __.regressionInfo = _regressionInfo_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Aardvark.Geometry.RegressionInfo3d>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module DipAndStrikeResultsLenses = 
    type DipAndStrikeResults with
        static member version_ = ((fun (self : DipAndStrikeResults) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : DipAndStrikeResults) -> { self with version = value }))
        static member plane_ = ((fun (self : DipAndStrikeResults) -> self.plane), (fun (value : Aardvark.Base.Plane3d) (self : DipAndStrikeResults) -> { self with plane = value }))
        static member dipAngle_ = ((fun (self : DipAndStrikeResults) -> self.dipAngle), (fun (value : Microsoft.FSharp.Core.float) (self : DipAndStrikeResults) -> { self with dipAngle = value }))
        static member dipDirection_ = ((fun (self : DipAndStrikeResults) -> self.dipDirection), (fun (value : Aardvark.Base.V3d) (self : DipAndStrikeResults) -> { self with dipDirection = value }))
        static member strikeDirection_ = ((fun (self : DipAndStrikeResults) -> self.strikeDirection), (fun (value : Aardvark.Base.V3d) (self : DipAndStrikeResults) -> { self with strikeDirection = value }))
        static member dipAzimuth_ = ((fun (self : DipAndStrikeResults) -> self.dipAzimuth), (fun (value : Microsoft.FSharp.Core.float) (self : DipAndStrikeResults) -> { self with dipAzimuth = value }))
        static member strikeAzimuth_ = ((fun (self : DipAndStrikeResults) -> self.strikeAzimuth), (fun (value : Microsoft.FSharp.Core.float) (self : DipAndStrikeResults) -> { self with strikeAzimuth = value }))
        static member centerOfMass_ = ((fun (self : DipAndStrikeResults) -> self.centerOfMass), (fun (value : Aardvark.Base.V3d) (self : DipAndStrikeResults) -> { self with centerOfMass = value }))
        static member error_ = ((fun (self : DipAndStrikeResults) -> self.error), (fun (value : Statistics) (self : DipAndStrikeResults) -> { self with error = value }))
        static member regressionInfo_ = ((fun (self : DipAndStrikeResults) -> self.regressionInfo), (fun (value : Microsoft.FSharp.Core.option<Aardvark.Geometry.RegressionInfo3d>) (self : DipAndStrikeResults) -> { self with regressionInfo = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveAnnotationResults(value : AnnotationResults) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _height_ = FSharp.Data.Adaptive.cval(value.height)
    let _heightDelta_ = FSharp.Data.Adaptive.cval(value.heightDelta)
    let _avgAltitude_ = FSharp.Data.Adaptive.cval(value.avgAltitude)
    let _length_ = FSharp.Data.Adaptive.cval(value.length)
    let _wayLength_ = FSharp.Data.Adaptive.cval(value.wayLength)
    let _bearing_ = FSharp.Data.Adaptive.cval(value.bearing)
    let _slope_ = FSharp.Data.Adaptive.cval(value.slope)
    let _trueThickness_ = FSharp.Data.Adaptive.cval(value.trueThickness)
    let _verticalThickness_ = FSharp.Data.Adaptive.cval(value.verticalThickness)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : AnnotationResults) = AdaptiveAnnotationResults(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : AnnotationResults) -> AdaptiveAnnotationResults(value)) (fun (adaptive : AdaptiveAnnotationResults) (value : AnnotationResults) -> adaptive.Update(value))
    member __.Update(value : AnnotationResults) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AnnotationResults>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _height_.Value <- value.height
            _heightDelta_.Value <- value.heightDelta
            _avgAltitude_.Value <- value.avgAltitude
            _length_.Value <- value.length
            _wayLength_.Value <- value.wayLength
            _bearing_.Value <- value.bearing
            _slope_.Value <- value.slope
            _trueThickness_.Value <- value.trueThickness
            _verticalThickness_.Value <- value.verticalThickness
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.height = _height_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.heightDelta = _heightDelta_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.avgAltitude = _avgAltitude_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.length = _length_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.wayLength = _wayLength_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.bearing = _bearing_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.slope = _slope_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.trueThickness = _trueThickness_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.verticalThickness = _verticalThickness_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AnnotationResultsLenses = 
    type AnnotationResults with
        static member version_ = ((fun (self : AnnotationResults) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : AnnotationResults) -> { self with version = value }))
        static member height_ = ((fun (self : AnnotationResults) -> self.height), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with height = value }))
        static member heightDelta_ = ((fun (self : AnnotationResults) -> self.heightDelta), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with heightDelta = value }))
        static member avgAltitude_ = ((fun (self : AnnotationResults) -> self.avgAltitude), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with avgAltitude = value }))
        static member length_ = ((fun (self : AnnotationResults) -> self.length), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with length = value }))
        static member wayLength_ = ((fun (self : AnnotationResults) -> self.wayLength), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with wayLength = value }))
        static member bearing_ = ((fun (self : AnnotationResults) -> self.bearing), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with bearing = value }))
        static member slope_ = ((fun (self : AnnotationResults) -> self.slope), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with slope = value }))
        static member trueThickness_ = ((fun (self : AnnotationResults) -> self.trueThickness), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with trueThickness = value }))
        static member verticalThickness_ = ((fun (self : AnnotationResults) -> self.verticalThickness), (fun (value : Microsoft.FSharp.Core.float) (self : AnnotationResults) -> { self with verticalThickness = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveAnnotation(value : Annotation) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _modelTrafo_ = FSharp.Data.Adaptive.cval(value.modelTrafo)
    let _referenceSystem_ = FSharp.Data.Adaptive.cval(value.referenceSystem)
    let _geometry_ = FSharp.Data.Adaptive.cval(value.geometry)
    let _projection_ = FSharp.Data.Adaptive.cval(value.projection)
    let _bookmarkId_ = FSharp.Data.Adaptive.cval(value.bookmarkId)
    let _semantic_ = FSharp.Data.Adaptive.cval(value.semantic)
    let _points_ = FSharp.Data.Adaptive.clist(value.points)
    let _segments_ =
        let inline __arg2 (m : AdaptiveSegment) (v : Segment) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelList(value.segments, (fun (v : Segment) -> AdaptiveSegment(v)), __arg2, (fun (m : AdaptiveSegment) -> m))
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let _thickness_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.thickness)
    let _results_ =
        let inline __arg2 (o : System.Object) (v : AnnotationResults) =
            (unbox<AdaptiveAnnotationResults> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : AnnotationResults) =
            (unbox<AdaptiveAnnotationResults> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Base.Annotation.AnnotationResults, PRo3D.Base.Annotation.AdaptiveAnnotationResults, PRo3D.Base.Annotation.AdaptiveAnnotationResults>(value.results, (fun (v : AnnotationResults) -> AdaptiveAnnotationResults(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveAnnotationResults> o), (fun (v : AnnotationResults) -> AdaptiveAnnotationResults(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveAnnotationResults> o))
    let _dnsResults_ =
        let inline __arg2 (o : System.Object) (v : DipAndStrikeResults) =
            (unbox<AdaptiveDipAndStrikeResults> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : DipAndStrikeResults) =
            (unbox<AdaptiveDipAndStrikeResults> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Base.Annotation.DipAndStrikeResults, PRo3D.Base.Annotation.AdaptiveDipAndStrikeResults, PRo3D.Base.Annotation.AdaptiveDipAndStrikeResults>(value.dnsResults, (fun (v : DipAndStrikeResults) -> AdaptiveDipAndStrikeResults(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveDipAndStrikeResults> o), (fun (v : DipAndStrikeResults) -> AdaptiveDipAndStrikeResults(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveDipAndStrikeResults> o))
    let _visible_ = FSharp.Data.Adaptive.cval(value.visible)
    let _showDns_ = FSharp.Data.Adaptive.cval(value.showDns)
    let _text_ = FSharp.Data.Adaptive.cval(value.text)
    let _textsize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.textsize)
    let _showText_ = FSharp.Data.Adaptive.cval(value.showText)
    let _manualDipAngle_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.manualDipAngle)
    let _manualDipAzimuth_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.manualDipAzimuth)
    let _surfaceName_ = FSharp.Data.Adaptive.cval(value.surfaceName)
    let _view_ = FSharp.Data.Adaptive.cval(value.view)
    let _semanticId_ = FSharp.Data.Adaptive.cval(value.semanticId)
    let _semanticType_ = FSharp.Data.Adaptive.cval(value.semanticType)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Annotation) = AdaptiveAnnotation(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Annotation) -> AdaptiveAnnotation(value)) (fun (adaptive : AdaptiveAnnotation) (value : Annotation) -> adaptive.Update(value))
    member __.Update(value : Annotation) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Annotation>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _modelTrafo_.Value <- value.modelTrafo
            _referenceSystem_.Value <- value.referenceSystem
            _geometry_.Value <- value.geometry
            _projection_.Value <- value.projection
            _bookmarkId_.Value <- value.bookmarkId
            _semantic_.Value <- value.semantic
            _points_.Value <- value.points
            _segments_.Update(value.segments)
            _color_.Update(value.color)
            _thickness_.Update(value.thickness)
            _results_.Update(value.results)
            _dnsResults_.Update(value.dnsResults)
            _visible_.Value <- value.visible
            _showDns_.Value <- value.showDns
            _text_.Value <- value.text
            _textsize_.Update(value.textsize)
            _showText_.Value <- value.showText
            _manualDipAngle_.Update(value.manualDipAngle)
            _manualDipAzimuth_.Update(value.manualDipAzimuth)
            _surfaceName_.Value <- value.surfaceName
            _view_.Value <- value.view
            _semanticId_.Value <- value.semanticId
            _semanticType_.Value <- value.semanticType
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.key = __value.key
    member __.modelTrafo = _modelTrafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.referenceSystem = _referenceSystem_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<PRo3D.Base.Gis.SpiceReferenceSystem>>
    member __.geometry = _geometry_ :> FSharp.Data.Adaptive.aval<Geometry>
    member __.projection = _projection_ :> FSharp.Data.Adaptive.aval<Projection>
    member __.bookmarkId = _bookmarkId_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.Guid>>
    member __.semantic = _semantic_ :> FSharp.Data.Adaptive.aval<Semantic>
    member __.points = _points_ :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>
    member __.segments = _segments_ :> FSharp.Data.Adaptive.alist<AdaptiveSegment>
    member __.color = _color_
    member __.thickness = _thickness_
    member __.results = _results_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<AnnotationResults, AdaptiveAnnotationResults, AdaptiveAnnotationResults>>
    member __.dnsResults = _dnsResults_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<DipAndStrikeResults, AdaptiveDipAndStrikeResults, AdaptiveDipAndStrikeResults>>
    member __.visible = _visible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showDns = _showDns_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.text = _text_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.textsize = _textsize_
    member __.showText = _showText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.manualDipAngle = _manualDipAngle_
    member __.manualDipAzimuth = _manualDipAzimuth_
    member __.surfaceName = _surfaceName_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.view = _view_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.semanticId = _semanticId_ :> FSharp.Data.Adaptive.aval<SemanticId>
    member __.semanticType = _semanticType_ :> FSharp.Data.Adaptive.aval<SemanticType>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AnnotationLenses = 
    type Annotation with
        static member version_ = ((fun (self : Annotation) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Annotation) -> { self with version = value }))
        static member key_ = ((fun (self : Annotation) -> self.key), (fun (value : System.Guid) (self : Annotation) -> { self with key = value }))
        static member modelTrafo_ = ((fun (self : Annotation) -> self.modelTrafo), (fun (value : Aardvark.Base.Trafo3d) (self : Annotation) -> { self with modelTrafo = value }))
        static member referenceSystem_ = ((fun (self : Annotation) -> self.referenceSystem), (fun (value : Microsoft.FSharp.Core.Option<PRo3D.Base.Gis.SpiceReferenceSystem>) (self : Annotation) -> { self with referenceSystem = value }))
        static member geometry_ = ((fun (self : Annotation) -> self.geometry), (fun (value : Geometry) (self : Annotation) -> { self with geometry = value }))
        static member projection_ = ((fun (self : Annotation) -> self.projection), (fun (value : Projection) (self : Annotation) -> { self with projection = value }))
        static member bookmarkId_ = ((fun (self : Annotation) -> self.bookmarkId), (fun (value : Microsoft.FSharp.Core.option<System.Guid>) (self : Annotation) -> { self with bookmarkId = value }))
        static member semantic_ = ((fun (self : Annotation) -> self.semantic), (fun (value : Semantic) (self : Annotation) -> { self with semantic = value }))
        static member points_ = ((fun (self : Annotation) -> self.points), (fun (value : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) (self : Annotation) -> { self with points = value }))
        static member segments_ = ((fun (self : Annotation) -> self.segments), (fun (value : FSharp.Data.Adaptive.IndexList<Segment>) (self : Annotation) -> { self with segments = value }))
        static member color_ = ((fun (self : Annotation) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : Annotation) -> { self with color = value }))
        static member thickness_ = ((fun (self : Annotation) -> self.thickness), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Annotation) -> { self with thickness = value }))
        static member results_ = ((fun (self : Annotation) -> self.results), (fun (value : Microsoft.FSharp.Core.Option<AnnotationResults>) (self : Annotation) -> { self with results = value }))
        static member dnsResults_ = ((fun (self : Annotation) -> self.dnsResults), (fun (value : Microsoft.FSharp.Core.Option<DipAndStrikeResults>) (self : Annotation) -> { self with dnsResults = value }))
        static member visible_ = ((fun (self : Annotation) -> self.visible), (fun (value : Microsoft.FSharp.Core.bool) (self : Annotation) -> { self with visible = value }))
        static member showDns_ = ((fun (self : Annotation) -> self.showDns), (fun (value : Microsoft.FSharp.Core.bool) (self : Annotation) -> { self with showDns = value }))
        static member text_ = ((fun (self : Annotation) -> self.text), (fun (value : Microsoft.FSharp.Core.string) (self : Annotation) -> { self with text = value }))
        static member textsize_ = ((fun (self : Annotation) -> self.textsize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Annotation) -> { self with textsize = value }))
        static member showText_ = ((fun (self : Annotation) -> self.showText), (fun (value : Microsoft.FSharp.Core.bool) (self : Annotation) -> { self with showText = value }))
        static member manualDipAngle_ = ((fun (self : Annotation) -> self.manualDipAngle), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Annotation) -> { self with manualDipAngle = value }))
        static member manualDipAzimuth_ = ((fun (self : Annotation) -> self.manualDipAzimuth), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Annotation) -> { self with manualDipAzimuth = value }))
        static member surfaceName_ = ((fun (self : Annotation) -> self.surfaceName), (fun (value : Microsoft.FSharp.Core.string) (self : Annotation) -> { self with surfaceName = value }))
        static member view_ = ((fun (self : Annotation) -> self.view), (fun (value : Aardvark.Rendering.CameraView) (self : Annotation) -> { self with view = value }))
        static member semanticId_ = ((fun (self : Annotation) -> self.semanticId), (fun (value : SemanticId) (self : Annotation) -> { self with semanticId = value }))
        static member semanticType_ = ((fun (self : Annotation) -> self.semanticType), (fun (value : SemanticType) (self : Annotation) -> { self with semanticType = value }))

