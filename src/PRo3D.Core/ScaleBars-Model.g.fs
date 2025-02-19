//c7479a03-2c13-9324-6c2c-e5cb5781e53b
//6e02a380-6769-a0ff-1f49-6d0a6d57d862
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
type AdaptivescSegment(value : scSegment) =
    let _startPoint_ = FSharp.Data.Adaptive.cval(value.startPoint)
    let _endPoint_ = FSharp.Data.Adaptive.cval(value.endPoint)
    let _color_ = FSharp.Data.Adaptive.cval(value.color)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : scSegment) = AdaptivescSegment(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : scSegment) -> AdaptivescSegment(value)) (fun (adaptive : AdaptivescSegment) (value : scSegment) -> adaptive.Update(value))
    member __.Update(value : scSegment) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<scSegment>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _startPoint_.Value <- value.startPoint
            _endPoint_.Value <- value.endPoint
            _color_.Value <- value.color
    member __.Current = __adaptive
    member __.startPoint = _startPoint_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.endPoint = _endPoint_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.color = _color_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module scSegmentLenses = 
    type scSegment with
        static member startPoint_ = ((fun (self : scSegment) -> self.startPoint), (fun (value : Aardvark.Base.V3d) (self : scSegment) -> { self with startPoint = value }))
        static member endPoint_ = ((fun (self : scSegment) -> self.endPoint), (fun (value : Aardvark.Base.V3d) (self : scSegment) -> { self with endPoint = value }))
        static member color_ = ((fun (self : scSegment) -> self.color), (fun (value : Aardvark.Base.C4b) (self : scSegment) -> { self with color = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveScaleVisualization(value : ScaleVisualization) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _guid_ = FSharp.Data.Adaptive.cval(value.guid)
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _text_ = FSharp.Data.Adaptive.cval(value.text)
    let _textsize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.textsize)
    let _textVisible_ = FSharp.Data.Adaptive.cval(value.textVisible)
    let _isVisible_ = FSharp.Data.Adaptive.cval(value.isVisible)
    let _position_ = FSharp.Data.Adaptive.cval(value.position)
    let _scSegments_ =
        let inline __arg2 (m : AdaptivescSegment) (v : scSegment) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelList(value.scSegments, (fun (v : scSegment) -> AdaptivescSegment(v)), __arg2, (fun (m : AdaptivescSegment) -> m))
    let _orientation_ = FSharp.Data.Adaptive.cval(value.orientation)
    let _alignment_ = FSharp.Data.Adaptive.cval(value.alignment)
    let _thickness_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.thickness)
    let _length_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.length)
    let _unit_ = FSharp.Data.Adaptive.cval(value.unit)
    let _subdivisions_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.subdivisions)
    let _view_ = FSharp.Data.Adaptive.cval(value.view)
    let _transformation_ = PRo3D.Core.Surface.AdaptiveTransformations(value.transformation)
    let _preTransform_ = FSharp.Data.Adaptive.cval(value.preTransform)
    let _representation_ = FSharp.Data.Adaptive.cval(value.representation)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ScaleVisualization) = AdaptiveScaleVisualization(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ScaleVisualization) -> AdaptiveScaleVisualization(value)) (fun (adaptive : AdaptiveScaleVisualization) (value : ScaleVisualization) -> adaptive.Update(value))
    member __.Update(value : ScaleVisualization) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ScaleVisualization>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _guid_.Value <- value.guid
            _name_.Value <- value.name
            _text_.Value <- value.text
            _textsize_.Update(value.textsize)
            _textVisible_.Value <- value.textVisible
            _isVisible_.Value <- value.isVisible
            _position_.Value <- value.position
            _scSegments_.Update(value.scSegments)
            _orientation_.Value <- value.orientation
            _alignment_.Value <- value.alignment
            _thickness_.Update(value.thickness)
            _length_.Update(value.length)
            _unit_.Value <- value.unit
            _subdivisions_.Update(value.subdivisions)
            _view_.Value <- value.view
            _transformation_.Update(value.transformation)
            _preTransform_.Value <- value.preTransform
            _representation_.Value <- value.representation
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = _guid_ :> FSharp.Data.Adaptive.aval<System.Guid>
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.text = _text_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.textsize = _textsize_
    member __.textVisible = _textVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isVisible = _isVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.position = _position_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.scSegments = _scSegments_ :> FSharp.Data.Adaptive.alist<AdaptivescSegment>
    member __.orientation = _orientation_ :> FSharp.Data.Adaptive.aval<Orientation>
    member __.alignment = _alignment_ :> FSharp.Data.Adaptive.aval<Pivot>
    member __.thickness = _thickness_
    member __.length = _length_
    member __.unit = _unit_ :> FSharp.Data.Adaptive.aval<Unit>
    member __.subdivisions = _subdivisions_
    member __.view = _view_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.transformation = _transformation_
    member __.preTransform = _preTransform_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.representation = _representation_ :> FSharp.Data.Adaptive.aval<ScaleRepresentation>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ScaleVisualizationLenses = 
    type ScaleVisualization with
        static member version_ = ((fun (self : ScaleVisualization) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ScaleVisualization) -> { self with version = value }))
        static member guid_ = ((fun (self : ScaleVisualization) -> self.guid), (fun (value : System.Guid) (self : ScaleVisualization) -> { self with guid = value }))
        static member name_ = ((fun (self : ScaleVisualization) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : ScaleVisualization) -> { self with name = value }))
        static member text_ = ((fun (self : ScaleVisualization) -> self.text), (fun (value : Microsoft.FSharp.Core.string) (self : ScaleVisualization) -> { self with text = value }))
        static member textsize_ = ((fun (self : ScaleVisualization) -> self.textsize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ScaleVisualization) -> { self with textsize = value }))
        static member textVisible_ = ((fun (self : ScaleVisualization) -> self.textVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : ScaleVisualization) -> { self with textVisible = value }))
        static member isVisible_ = ((fun (self : ScaleVisualization) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : ScaleVisualization) -> { self with isVisible = value }))
        static member position_ = ((fun (self : ScaleVisualization) -> self.position), (fun (value : Aardvark.Base.V3d) (self : ScaleVisualization) -> { self with position = value }))
        static member scSegments_ = ((fun (self : ScaleVisualization) -> self.scSegments), (fun (value : FSharp.Data.Adaptive.IndexList<scSegment>) (self : ScaleVisualization) -> { self with scSegments = value }))
        static member orientation_ = ((fun (self : ScaleVisualization) -> self.orientation), (fun (value : Orientation) (self : ScaleVisualization) -> { self with orientation = value }))
        static member alignment_ = ((fun (self : ScaleVisualization) -> self.alignment), (fun (value : Pivot) (self : ScaleVisualization) -> { self with alignment = value }))
        static member thickness_ = ((fun (self : ScaleVisualization) -> self.thickness), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ScaleVisualization) -> { self with thickness = value }))
        static member length_ = ((fun (self : ScaleVisualization) -> self.length), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ScaleVisualization) -> { self with length = value }))
        static member unit_ = ((fun (self : ScaleVisualization) -> self.unit), (fun (value : Unit) (self : ScaleVisualization) -> { self with unit = value }))
        static member subdivisions_ = ((fun (self : ScaleVisualization) -> self.subdivisions), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ScaleVisualization) -> { self with subdivisions = value }))
        static member view_ = ((fun (self : ScaleVisualization) -> self.view), (fun (value : Aardvark.Rendering.CameraView) (self : ScaleVisualization) -> { self with view = value }))
        static member transformation_ = ((fun (self : ScaleVisualization) -> self.transformation), (fun (value : PRo3D.Core.Surface.Transformations) (self : ScaleVisualization) -> { self with transformation = value }))
        static member preTransform_ = ((fun (self : ScaleVisualization) -> self.preTransform), (fun (value : Aardvark.Base.Trafo3d) (self : ScaleVisualization) -> { self with preTransform = value }))
        static member representation_ = ((fun (self : ScaleVisualization) -> self.representation), (fun (value : ScaleRepresentation) (self : ScaleVisualization) -> { self with representation = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveScaleBarDrawing(value : ScaleBarDrawing) =
    let _orientation_ = FSharp.Data.Adaptive.cval(value.orientation)
    let _alignment_ = FSharp.Data.Adaptive.cval(value.alignment)
    let _thickness_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.thickness)
    let _length_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.length)
    let _unit_ = FSharp.Data.Adaptive.cval(value.unit)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ScaleBarDrawing) = AdaptiveScaleBarDrawing(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ScaleBarDrawing) -> AdaptiveScaleBarDrawing(value)) (fun (adaptive : AdaptiveScaleBarDrawing) (value : ScaleBarDrawing) -> adaptive.Update(value))
    member __.Update(value : ScaleBarDrawing) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ScaleBarDrawing>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _orientation_.Value <- value.orientation
            _alignment_.Value <- value.alignment
            _thickness_.Update(value.thickness)
            _length_.Update(value.length)
            _unit_.Value <- value.unit
    member __.Current = __adaptive
    member __.orientation = _orientation_ :> FSharp.Data.Adaptive.aval<Orientation>
    member __.alignment = _alignment_ :> FSharp.Data.Adaptive.aval<Pivot>
    member __.thickness = _thickness_
    member __.length = _length_
    member __.unit = _unit_ :> FSharp.Data.Adaptive.aval<Unit>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ScaleBarDrawingLenses = 
    type ScaleBarDrawing with
        static member orientation_ = ((fun (self : ScaleBarDrawing) -> self.orientation), (fun (value : Orientation) (self : ScaleBarDrawing) -> { self with orientation = value }))
        static member alignment_ = ((fun (self : ScaleBarDrawing) -> self.alignment), (fun (value : Pivot) (self : ScaleBarDrawing) -> { self with alignment = value }))
        static member thickness_ = ((fun (self : ScaleBarDrawing) -> self.thickness), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ScaleBarDrawing) -> { self with thickness = value }))
        static member length_ = ((fun (self : ScaleBarDrawing) -> self.length), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ScaleBarDrawing) -> { self with length = value }))
        static member unit_ = ((fun (self : ScaleBarDrawing) -> self.unit), (fun (value : Unit) (self : ScaleBarDrawing) -> { self with unit = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveScaleBarsModel(value : ScaleBarsModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _scaleBars_ =
        let inline __arg2 (m : AdaptiveScaleVisualization) (v : ScaleVisualization) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.scaleBars, (fun (v : ScaleVisualization) -> AdaptiveScaleVisualization(v)), __arg2, (fun (m : AdaptiveScaleVisualization) -> m))
    let _selectedScaleBar_ = FSharp.Data.Adaptive.cval(value.selectedScaleBar)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ScaleBarsModel) = AdaptiveScaleBarsModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ScaleBarsModel) -> AdaptiveScaleBarsModel(value)) (fun (adaptive : AdaptiveScaleBarsModel) (value : ScaleBarsModel) -> adaptive.Update(value))
    member __.Update(value : ScaleBarsModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ScaleBarsModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _scaleBars_.Update(value.scaleBars)
            _selectedScaleBar_.Value <- value.selectedScaleBar
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.scaleBars = _scaleBars_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveScaleVisualization>
    member __.selectedScaleBar = _selectedScaleBar_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ScaleBarsModelLenses = 
    type ScaleBarsModel with
        static member version_ = ((fun (self : ScaleBarsModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ScaleBarsModel) -> { self with version = value }))
        static member scaleBars_ = ((fun (self : ScaleBarsModel) -> self.scaleBars), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, ScaleVisualization>) (self : ScaleBarsModel) -> { self with scaleBars = value }))
        static member selectedScaleBar_ = ((fun (self : ScaleBarsModel) -> self.selectedScaleBar), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : ScaleBarsModel) -> { self with selectedScaleBar = value }))

