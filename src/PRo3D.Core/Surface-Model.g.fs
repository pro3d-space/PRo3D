//37f5b468-c181-4812-e1d8-274cc8ab8e6a
//bdb6543d-d70f-88b9-d38b-2463a0d1f7a6
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Core.Surface

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Core.Surface
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveScalarLayer(value : ScalarLayer) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _label_ = FSharp.Data.Adaptive.cval(value.label)
    let _actualRange_ = FSharp.Data.Adaptive.cval(value.actualRange)
    let _definedRange_ = FSharp.Data.Adaptive.cval(value.definedRange)
    let _index_ = FSharp.Data.Adaptive.cval(value.index)
    let _colorLegend_ = PRo3D.Base.AdaptiveFalseColorsModel(value.colorLegend)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ScalarLayer) = AdaptiveScalarLayer(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ScalarLayer) -> AdaptiveScalarLayer(value)) (fun (adaptive : AdaptiveScalarLayer) (value : ScalarLayer) -> adaptive.Update(value))
    member __.Update(value : ScalarLayer) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ScalarLayer>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _label_.Value <- value.label
            _actualRange_.Value <- value.actualRange
            _definedRange_.Value <- value.definedRange
            _index_.Value <- value.index
            _colorLegend_.Update(value.colorLegend)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.label = _label_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.actualRange = _actualRange_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Range1d>
    member __.definedRange = _definedRange_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Range1d>
    member __.index = _index_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.colorLegend = _colorLegend_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ScalarLayerLenses = 
    type ScalarLayer with
        static member version_ = ((fun (self : ScalarLayer) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ScalarLayer) -> { self with version = value }))
        static member label_ = ((fun (self : ScalarLayer) -> self.label), (fun (value : Microsoft.FSharp.Core.string) (self : ScalarLayer) -> { self with label = value }))
        static member actualRange_ = ((fun (self : ScalarLayer) -> self.actualRange), (fun (value : Aardvark.Base.Range1d) (self : ScalarLayer) -> { self with actualRange = value }))
        static member definedRange_ = ((fun (self : ScalarLayer) -> self.definedRange), (fun (value : Aardvark.Base.Range1d) (self : ScalarLayer) -> { self with definedRange = value }))
        static member index_ = ((fun (self : ScalarLayer) -> self.index), (fun (value : Microsoft.FSharp.Core.int) (self : ScalarLayer) -> { self with index = value }))
        static member colorLegend_ = ((fun (self : ScalarLayer) -> self.colorLegend), (fun (value : PRo3D.Base.FalseColorsModel) (self : ScalarLayer) -> { self with colorLegend = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveColorCorrection(value : ColorCorrection) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _contrast_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.contrast)
    let _useContrast_ = FSharp.Data.Adaptive.cval(value.useContrast)
    let _brightness_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.brightness)
    let _useBrightn_ = FSharp.Data.Adaptive.cval(value.useBrightn)
    let _gamma_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.gamma)
    let _useGamma_ = FSharp.Data.Adaptive.cval(value.useGamma)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let _useColor_ = FSharp.Data.Adaptive.cval(value.useColor)
    let _useGrayscale_ = FSharp.Data.Adaptive.cval(value.useGrayscale)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ColorCorrection) = AdaptiveColorCorrection(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ColorCorrection) -> AdaptiveColorCorrection(value)) (fun (adaptive : AdaptiveColorCorrection) (value : ColorCorrection) -> adaptive.Update(value))
    member __.Update(value : ColorCorrection) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ColorCorrection>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _contrast_.Update(value.contrast)
            _useContrast_.Value <- value.useContrast
            _brightness_.Update(value.brightness)
            _useBrightn_.Value <- value.useBrightn
            _gamma_.Update(value.gamma)
            _useGamma_.Value <- value.useGamma
            _color_.Update(value.color)
            _useColor_.Value <- value.useColor
            _useGrayscale_.Value <- value.useGrayscale
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.contrast = _contrast_
    member __.useContrast = _useContrast_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.brightness = _brightness_
    member __.useBrightn = _useBrightn_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.gamma = _gamma_
    member __.useGamma = _useGamma_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.color = _color_
    member __.useColor = _useColor_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.useGrayscale = _useGrayscale_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ColorCorrectionLenses = 
    type ColorCorrection with
        static member version_ = ((fun (self : ColorCorrection) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ColorCorrection) -> { self with version = value }))
        static member contrast_ = ((fun (self : ColorCorrection) -> self.contrast), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ColorCorrection) -> { self with contrast = value }))
        static member useContrast_ = ((fun (self : ColorCorrection) -> self.useContrast), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useContrast = value }))
        static member brightness_ = ((fun (self : ColorCorrection) -> self.brightness), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ColorCorrection) -> { self with brightness = value }))
        static member useBrightn_ = ((fun (self : ColorCorrection) -> self.useBrightn), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useBrightn = value }))
        static member gamma_ = ((fun (self : ColorCorrection) -> self.gamma), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ColorCorrection) -> { self with gamma = value }))
        static member useGamma_ = ((fun (self : ColorCorrection) -> self.useGamma), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useGamma = value }))
        static member color_ = ((fun (self : ColorCorrection) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : ColorCorrection) -> { self with color = value }))
        static member useColor_ = ((fun (self : ColorCorrection) -> self.useColor), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useColor = value }))
        static member useGrayscale_ = ((fun (self : ColorCorrection) -> self.useGrayscale), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useGrayscale = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRadiometry(value : Radiometry) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _useRadiometry_ = FSharp.Data.Adaptive.cval(value.useRadiometry)
    let _minR_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.minR)
    let _maxR_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.maxR)
    let _minG_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.minG)
    let _maxG_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.maxG)
    let _minB_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.minB)
    let _maxB_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.maxB)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Radiometry) = AdaptiveRadiometry(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Radiometry) -> AdaptiveRadiometry(value)) (fun (adaptive : AdaptiveRadiometry) (value : Radiometry) -> adaptive.Update(value))
    member __.Update(value : Radiometry) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Radiometry>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _useRadiometry_.Value <- value.useRadiometry
            _minR_.Update(value.minR)
            _maxR_.Update(value.maxR)
            _minG_.Update(value.minG)
            _maxG_.Update(value.maxG)
            _minB_.Update(value.minB)
            _maxB_.Update(value.maxB)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.useRadiometry = _useRadiometry_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.minR = _minR_
    member __.maxR = _maxR_
    member __.minG = _minG_
    member __.maxG = _maxG_
    member __.minB = _minB_
    member __.maxB = _maxB_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RadiometryLenses = 
    type Radiometry with
        static member version_ = ((fun (self : Radiometry) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Radiometry) -> { self with version = value }))
        static member useRadiometry_ = ((fun (self : Radiometry) -> self.useRadiometry), (fun (value : Microsoft.FSharp.Core.bool) (self : Radiometry) -> { self with useRadiometry = value }))
        static member minR_ = ((fun (self : Radiometry) -> self.minR), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Radiometry) -> { self with minR = value }))
        static member maxR_ = ((fun (self : Radiometry) -> self.maxR), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Radiometry) -> { self with maxR = value }))
        static member minG_ = ((fun (self : Radiometry) -> self.minG), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Radiometry) -> { self with minG = value }))
        static member maxG_ = ((fun (self : Radiometry) -> self.maxG), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Radiometry) -> { self with maxG = value }))
        static member minB_ = ((fun (self : Radiometry) -> self.minB), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Radiometry) -> { self with minB = value }))
        static member maxB_ = ((fun (self : Radiometry) -> self.maxB), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Radiometry) -> { self with maxB = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSurface(value : Surface) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _guid_ = FSharp.Data.Adaptive.cval(value.guid)
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _importPath_ = FSharp.Data.Adaptive.cval(value.importPath)
    let _opcNames_ = FSharp.Data.Adaptive.cval(value.opcNames)
    let _opcPaths_ = FSharp.Data.Adaptive.cval(value.opcPaths)
    let _relativePaths_ = FSharp.Data.Adaptive.cval(value.relativePaths)
    let _fillMode_ = FSharp.Data.Adaptive.cval(value.fillMode)
    let _cullMode_ = FSharp.Data.Adaptive.cval(value.cullMode)
    let _isVisible_ = FSharp.Data.Adaptive.cval(value.isVisible)
    let _isActive_ = FSharp.Data.Adaptive.cval(value.isActive)
    let _quality_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.quality)
    let _priority_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.priority)
    let _triangleSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.triangleSize)
    let _scaling_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.scaling)
    let _filterByDistance_ = FSharp.Data.Adaptive.cval(value.filterByDistance)
    let _filterDistance_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.filterDistance)
    let _preTransform_ = FSharp.Data.Adaptive.cval(value.preTransform)
    let _scalarLayers_ =
        let inline __arg2 (m : AdaptiveScalarLayer) (v : ScalarLayer) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.scalarLayers, (fun (v : ScalarLayer) -> AdaptiveScalarLayer(v)), __arg2, (fun (m : AdaptiveScalarLayer) -> m))
    let _selectedScalar_ =
        let inline __arg2 (o : System.Object) (v : ScalarLayer) =
            (unbox<AdaptiveScalarLayer> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : ScalarLayer) =
            (unbox<AdaptiveScalarLayer> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Core.Surface.ScalarLayer, PRo3D.Core.Surface.AdaptiveScalarLayer, PRo3D.Core.Surface.AdaptiveScalarLayer>(value.selectedScalar, (fun (v : ScalarLayer) -> AdaptiveScalarLayer(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveScalarLayer> o), (fun (v : ScalarLayer) -> AdaptiveScalarLayer(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveScalarLayer> o))
    let _textureLayers_ = FSharp.Data.Adaptive.clist(value.textureLayers)
    let _primaryTexture_ = FSharp.Data.Adaptive.cval(value.primaryTexture)
    let _secondaryTexture_ = FSharp.Data.Adaptive.cval(value.secondaryTexture)
    let _transferFunction_ = FSharp.Data.Adaptive.cval(value.transferFunction)
    let _opcxPath_ = FSharp.Data.Adaptive.cval(value.opcxPath)
    let _preferredLoader_ = FSharp.Data.Adaptive.cval(value.preferredLoader)
    let _colorCorrection_ = AdaptiveColorCorrection(value.colorCorrection)
    let _homePosition_ = FSharp.Data.Adaptive.cval(value.homePosition)
    let _transformation_ = AdaptiveTransformations(value.transformation)
    let _radiometry_ = AdaptiveRadiometry(value.radiometry)
    let _contourModel_ = PRo3D.Core.AdaptiveContourLineModel(value.contourModel)
    let _highlightSelected_ = FSharp.Data.Adaptive.cval(value.highlightSelected)
    let _highlightAlways_ = FSharp.Data.Adaptive.cval(value.highlightAlways)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Surface) = AdaptiveSurface(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Surface) -> AdaptiveSurface(value)) (fun (adaptive : AdaptiveSurface) (value : Surface) -> adaptive.Update(value))
    member __.Update(value : Surface) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Surface>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _guid_.Value <- value.guid
            _name_.Value <- value.name
            _importPath_.Value <- value.importPath
            _opcNames_.Value <- value.opcNames
            _opcPaths_.Value <- value.opcPaths
            _relativePaths_.Value <- value.relativePaths
            _fillMode_.Value <- value.fillMode
            _cullMode_.Value <- value.cullMode
            _isVisible_.Value <- value.isVisible
            _isActive_.Value <- value.isActive
            _quality_.Update(value.quality)
            _priority_.Update(value.priority)
            _triangleSize_.Update(value.triangleSize)
            _scaling_.Update(value.scaling)
            _filterByDistance_.Value <- value.filterByDistance
            _filterDistance_.Update(value.filterDistance)
            _preTransform_.Value <- value.preTransform
            _scalarLayers_.Update(value.scalarLayers)
            _selectedScalar_.Update(value.selectedScalar)
            _textureLayers_.Value <- value.textureLayers
            _primaryTexture_.Value <- value.primaryTexture
            _secondaryTexture_.Value <- value.secondaryTexture
            _transferFunction_.Value <- value.transferFunction
            _opcxPath_.Value <- value.opcxPath
            _preferredLoader_.Value <- value.preferredLoader
            _colorCorrection_.Update(value.colorCorrection)
            _homePosition_.Value <- value.homePosition
            _transformation_.Update(value.transformation)
            _radiometry_.Update(value.radiometry)
            _contourModel_.Update(value.contourModel)
            _highlightSelected_.Value <- value.highlightSelected
            _highlightAlways_.Value <- value.highlightAlways
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = _guid_ :> FSharp.Data.Adaptive.aval<SurfaceId>
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.importPath = _importPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.opcNames = _opcNames_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>>
    member __.opcPaths = _opcPaths_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>>
    member __.relativePaths = _relativePaths_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.fillMode = _fillMode_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.FillMode>
    member __.cullMode = _cullMode_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CullMode>
    member __.isVisible = _isVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isActive = _isActive_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.quality = _quality_
    member __.priority = _priority_
    member __.triangleSize = _triangleSize_
    member __.scaling = _scaling_
    member __.filterByDistance = _filterByDistance_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.filterDistance = _filterDistance_
    member __.preTransform = _preTransform_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.scalarLayers = _scalarLayers_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.int, AdaptiveScalarLayer>
    member __.selectedScalar = _selectedScalar_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<ScalarLayer, AdaptiveScalarLayer, AdaptiveScalarLayer>>
    member __.textureLayers = _textureLayers_ :> FSharp.Data.Adaptive.alist<TextureLayer>
    member __.primaryTexture = _primaryTexture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<TextureLayer>>
    member __.secondaryTexture = _secondaryTexture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<TextureLayer>>
    member __.transferFunction = _transferFunction_ :> FSharp.Data.Adaptive.aval<TransferFunction>
    member __.opcxPath = _opcxPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>>
    member __.surfaceType = __value.surfaceType
    member __.preferredLoader = _preferredLoader_ :> FSharp.Data.Adaptive.aval<MeshLoaderType>
    member __.colorCorrection = _colorCorrection_
    member __.homePosition = _homePosition_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Rendering.CameraView>>
    member __.transformation = _transformation_
    member __.radiometry = _radiometry_
    member __.contourModel = _contourModel_
    member __.highlightSelected = _highlightSelected_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.highlightAlways = _highlightAlways_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SurfaceLenses = 
    type Surface with
        static member version_ = ((fun (self : Surface) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Surface) -> { self with version = value }))
        static member guid_ = ((fun (self : Surface) -> self.guid), (fun (value : SurfaceId) (self : Surface) -> { self with guid = value }))
        static member name_ = ((fun (self : Surface) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : Surface) -> { self with name = value }))
        static member importPath_ = ((fun (self : Surface) -> self.importPath), (fun (value : Microsoft.FSharp.Core.string) (self : Surface) -> { self with importPath = value }))
        static member opcNames_ = ((fun (self : Surface) -> self.opcNames), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>) (self : Surface) -> { self with opcNames = value }))
        static member opcPaths_ = ((fun (self : Surface) -> self.opcPaths), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>) (self : Surface) -> { self with opcPaths = value }))
        static member relativePaths_ = ((fun (self : Surface) -> self.relativePaths), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with relativePaths = value }))
        static member fillMode_ = ((fun (self : Surface) -> self.fillMode), (fun (value : Aardvark.Rendering.FillMode) (self : Surface) -> { self with fillMode = value }))
        static member cullMode_ = ((fun (self : Surface) -> self.cullMode), (fun (value : Aardvark.Rendering.CullMode) (self : Surface) -> { self with cullMode = value }))
        static member isVisible_ = ((fun (self : Surface) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with isVisible = value }))
        static member isActive_ = ((fun (self : Surface) -> self.isActive), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with isActive = value }))
        static member quality_ = ((fun (self : Surface) -> self.quality), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Surface) -> { self with quality = value }))
        static member priority_ = ((fun (self : Surface) -> self.priority), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Surface) -> { self with priority = value }))
        static member triangleSize_ = ((fun (self : Surface) -> self.triangleSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Surface) -> { self with triangleSize = value }))
        static member scaling_ = ((fun (self : Surface) -> self.scaling), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Surface) -> { self with scaling = value }))
        static member filterByDistance_ = ((fun (self : Surface) -> self.filterByDistance), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with filterByDistance = value }))
        static member filterDistance_ = ((fun (self : Surface) -> self.filterDistance), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Surface) -> { self with filterDistance = value }))
        static member preTransform_ = ((fun (self : Surface) -> self.preTransform), (fun (value : Aardvark.Base.Trafo3d) (self : Surface) -> { self with preTransform = value }))
        static member scalarLayers_ = ((fun (self : Surface) -> self.scalarLayers), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.int, ScalarLayer>) (self : Surface) -> { self with scalarLayers = value }))
        static member selectedScalar_ = ((fun (self : Surface) -> self.selectedScalar), (fun (value : Microsoft.FSharp.Core.option<ScalarLayer>) (self : Surface) -> { self with selectedScalar = value }))
        static member textureLayers_ = ((fun (self : Surface) -> self.textureLayers), (fun (value : FSharp.Data.Adaptive.IndexList<TextureLayer>) (self : Surface) -> { self with textureLayers = value }))
        static member primaryTexture_ = ((fun (self : Surface) -> self.primaryTexture), (fun (value : Microsoft.FSharp.Core.Option<TextureLayer>) (self : Surface) -> { self with primaryTexture = value }))
        static member secondaryTexture_ = ((fun (self : Surface) -> self.secondaryTexture), (fun (value : Microsoft.FSharp.Core.Option<TextureLayer>) (self : Surface) -> { self with secondaryTexture = value }))
        static member transferFunction_ = ((fun (self : Surface) -> self.transferFunction), (fun (value : TransferFunction) (self : Surface) -> { self with transferFunction = value }))
        static member opcxPath_ = ((fun (self : Surface) -> self.opcxPath), (fun (value : Microsoft.FSharp.Core.Option<Microsoft.FSharp.Core.string>) (self : Surface) -> { self with opcxPath = value }))
        static member surfaceType_ = ((fun (self : Surface) -> self.surfaceType), (fun (value : SurfaceType) (self : Surface) -> { self with surfaceType = value }))
        static member preferredLoader_ = ((fun (self : Surface) -> self.preferredLoader), (fun (value : MeshLoaderType) (self : Surface) -> { self with preferredLoader = value }))
        static member colorCorrection_ = ((fun (self : Surface) -> self.colorCorrection), (fun (value : ColorCorrection) (self : Surface) -> { self with colorCorrection = value }))
        static member homePosition_ = ((fun (self : Surface) -> self.homePosition), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Rendering.CameraView>) (self : Surface) -> { self with homePosition = value }))
        static member transformation_ = ((fun (self : Surface) -> self.transformation), (fun (value : Transformations) (self : Surface) -> { self with transformation = value }))
        static member radiometry_ = ((fun (self : Surface) -> self.radiometry), (fun (value : Radiometry) (self : Surface) -> { self with radiometry = value }))
        static member contourModel_ = ((fun (self : Surface) -> self.contourModel), (fun (value : PRo3D.Core.ContourLineModel) (self : Surface) -> { self with contourModel = value }))
        static member highlightSelected_ = ((fun (self : Surface) -> self.highlightSelected), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with highlightSelected = value }))
        static member highlightAlways_ = ((fun (self : Surface) -> self.highlightAlways), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with highlightAlways = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSgSurface(value : SgSurface) =
    let _trafo_ = Aardvark.UI.Trafos.AdaptiveTransformation(value.trafo)
    let _globalBB_ = FSharp.Data.Adaptive.cval(value.globalBB)
    let _sceneGraph_ = FSharp.Data.Adaptive.cval(value.sceneGraph)
    let _picking_ = FSharp.Data.Adaptive.cval(value.picking)
    let _opcScene_ = FSharp.Data.Adaptive.cval(value.opcScene)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SgSurface) = AdaptiveSgSurface(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SgSurface) -> AdaptiveSgSurface(value)) (fun (adaptive : AdaptiveSgSurface) (value : SgSurface) -> adaptive.Update(value))
    member __.Update(value : SgSurface) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SgSurface>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _trafo_.Update(value.trafo)
            _globalBB_.Value <- value.globalBB
            _sceneGraph_.Value <- value.sceneGraph
            _picking_.Value <- value.picking
            _opcScene_.Value <- value.opcScene
            ()
    member __.Current = __adaptive
    member __.surface = __value.surface
    member __.trafo = _trafo_
    member __.globalBB = _globalBB_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Box3d>
    member __.sceneGraph = _sceneGraph_ :> FSharp.Data.Adaptive.aval<Aardvark.SceneGraph.ISg>
    member __.picking = _picking_ :> FSharp.Data.Adaptive.aval<Picking>
    member __.opcScene = _opcScene_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.GeoSpatial.Opc.Configurations.OpcScene>>
    member __.dataSource = __value.dataSource
    member __.isObj = __value.isObj
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SgSurfaceLenses = 
    type SgSurface with
        static member surface_ = ((fun (self : SgSurface) -> self.surface), (fun (value : System.Guid) (self : SgSurface) -> { self with surface = value }))
        static member trafo_ = ((fun (self : SgSurface) -> self.trafo), (fun (value : Aardvark.UI.Trafos.Transformation) (self : SgSurface) -> { self with trafo = value }))
        static member globalBB_ = ((fun (self : SgSurface) -> self.globalBB), (fun (value : Aardvark.Base.Box3d) (self : SgSurface) -> { self with globalBB = value }))
        static member sceneGraph_ = ((fun (self : SgSurface) -> self.sceneGraph), (fun (value : Aardvark.SceneGraph.ISg) (self : SgSurface) -> { self with sceneGraph = value }))
        static member picking_ = ((fun (self : SgSurface) -> self.picking), (fun (value : Picking) (self : SgSurface) -> { self with picking = value }))
        static member opcScene_ = ((fun (self : SgSurface) -> self.opcScene), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.GeoSpatial.Opc.Configurations.OpcScene>) (self : SgSurface) -> { self with opcScene = value }))
        static member dataSource_ = ((fun (self : SgSurface) -> self.dataSource), (fun (value : DataSource) (self : SgSurface) -> { self with dataSource = value }))
        static member isObj_ = ((fun (self : SgSurface) -> self.isObj), (fun (value : Microsoft.FSharp.Core.bool) (self : SgSurface) -> { self with isObj = value }))

