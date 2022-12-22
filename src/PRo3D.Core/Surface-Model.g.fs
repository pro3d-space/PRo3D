//dabcc7cc-7f86-fd62-f016-c9143c8e8d35
//876be93d-77db-a6e2-9108-3a112169eaed
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
    let _contrast_ = Aardvark.UI.AdaptiveNumericInput(value.contrast)
    let _useContrast_ = FSharp.Data.Adaptive.cval(value.useContrast)
    let _brightness_ = Aardvark.UI.AdaptiveNumericInput(value.brightness)
    let _useBrightn_ = FSharp.Data.Adaptive.cval(value.useBrightn)
    let _gamma_ = Aardvark.UI.AdaptiveNumericInput(value.gamma)
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
        static member contrast_ = ((fun (self : ColorCorrection) -> self.contrast), (fun (value : Aardvark.UI.NumericInput) (self : ColorCorrection) -> { self with contrast = value }))
        static member useContrast_ = ((fun (self : ColorCorrection) -> self.useContrast), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useContrast = value }))
        static member brightness_ = ((fun (self : ColorCorrection) -> self.brightness), (fun (value : Aardvark.UI.NumericInput) (self : ColorCorrection) -> { self with brightness = value }))
        static member useBrightn_ = ((fun (self : ColorCorrection) -> self.useBrightn), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useBrightn = value }))
        static member gamma_ = ((fun (self : ColorCorrection) -> self.gamma), (fun (value : Aardvark.UI.NumericInput) (self : ColorCorrection) -> { self with gamma = value }))
        static member useGamma_ = ((fun (self : ColorCorrection) -> self.useGamma), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useGamma = value }))
        static member color_ = ((fun (self : ColorCorrection) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : ColorCorrection) -> { self with color = value }))
        static member useColor_ = ((fun (self : ColorCorrection) -> self.useColor), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useColor = value }))
        static member useGrayscale_ = ((fun (self : ColorCorrection) -> self.useGrayscale), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorCorrection) -> { self with useGrayscale = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveTransformations(value : Transformations) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _useTranslationArrows_ = FSharp.Data.Adaptive.cval(value.useTranslationArrows)
    let _translation_ = Aardvark.UI.AdaptiveV3dInput(value.translation)
    let _yaw_ = Aardvark.UI.AdaptiveNumericInput(value.yaw)
    let _pitch_ = Aardvark.UI.AdaptiveNumericInput(value.pitch)
    let _roll_ = Aardvark.UI.AdaptiveNumericInput(value.roll)
    let _trafo_ = FSharp.Data.Adaptive.cval(value.trafo)
    let _pivot_ = FSharp.Data.Adaptive.cval(value.pivot)
    let _flipZ_ = FSharp.Data.Adaptive.cval(value.flipZ)
    let _isSketchFab_ = FSharp.Data.Adaptive.cval(value.isSketchFab)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Transformations) = AdaptiveTransformations(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Transformations) -> AdaptiveTransformations(value)) (fun (adaptive : AdaptiveTransformations) (value : Transformations) -> adaptive.Update(value))
    member __.Update(value : Transformations) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Transformations>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _useTranslationArrows_.Value <- value.useTranslationArrows
            _translation_.Update(value.translation)
            _yaw_.Update(value.yaw)
            _pitch_.Update(value.pitch)
            _roll_.Update(value.roll)
            _trafo_.Value <- value.trafo
            _pivot_.Value <- value.pivot
            _flipZ_.Value <- value.flipZ
            _isSketchFab_.Value <- value.isSketchFab
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.useTranslationArrows = _useTranslationArrows_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.translation = _translation_
    member __.yaw = _yaw_
    member __.pitch = _pitch_
    member __.roll = _roll_
    member __.trafo = _trafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.pivot = _pivot_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.flipZ = _flipZ_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isSketchFab = _isSketchFab_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TransformationsLenses = 
    type Transformations with
        static member version_ = ((fun (self : Transformations) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Transformations) -> { self with version = value }))
        static member useTranslationArrows_ = ((fun (self : Transformations) -> self.useTranslationArrows), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with useTranslationArrows = value }))
        static member translation_ = ((fun (self : Transformations) -> self.translation), (fun (value : Aardvark.UI.V3dInput) (self : Transformations) -> { self with translation = value }))
        static member yaw_ = ((fun (self : Transformations) -> self.yaw), (fun (value : Aardvark.UI.NumericInput) (self : Transformations) -> { self with yaw = value }))
        static member pitch_ = ((fun (self : Transformations) -> self.pitch), (fun (value : Aardvark.UI.NumericInput) (self : Transformations) -> { self with pitch = value }))
        static member roll_ = ((fun (self : Transformations) -> self.roll), (fun (value : Aardvark.UI.NumericInput) (self : Transformations) -> { self with roll = value }))
        static member trafo_ = ((fun (self : Transformations) -> self.trafo), (fun (value : Aardvark.Base.Trafo3d) (self : Transformations) -> { self with trafo = value }))
        static member pivot_ = ((fun (self : Transformations) -> self.pivot), (fun (value : Aardvark.Base.V3d) (self : Transformations) -> { self with pivot = value }))
        static member flipZ_ = ((fun (self : Transformations) -> self.flipZ), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with flipZ = value }))
        static member isSketchFab_ = ((fun (self : Transformations) -> self.isSketchFab), (fun (value : Microsoft.FSharp.Core.bool) (self : Transformations) -> { self with isSketchFab = value }))
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
    let _quality_ = Aardvark.UI.AdaptiveNumericInput(value.quality)
    let _priority_ = Aardvark.UI.AdaptiveNumericInput(value.priority)
    let _triangleSize_ = Aardvark.UI.AdaptiveNumericInput(value.triangleSize)
    let _scaling_ = Aardvark.UI.AdaptiveNumericInput(value.scaling)
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
    let _selectedTexture_ = FSharp.Data.Adaptive.cval(value.selectedTexture)
    let _colorCorrection_ = AdaptiveColorCorrection(value.colorCorrection)
    let _homePosition_ = FSharp.Data.Adaptive.cval(value.homePosition)
    let _transformation_ = AdaptiveTransformations(value.transformation)
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
            _preTransform_.Value <- value.preTransform
            _scalarLayers_.Update(value.scalarLayers)
            _selectedScalar_.Update(value.selectedScalar)
            _textureLayers_.Value <- value.textureLayers
            _selectedTexture_.Value <- value.selectedTexture
            _colorCorrection_.Update(value.colorCorrection)
            _homePosition_.Value <- value.homePosition
            _transformation_.Update(value.transformation)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = _guid_ :> FSharp.Data.Adaptive.aval<System.Guid>
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
    member __.preTransform = _preTransform_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.scalarLayers = _scalarLayers_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.int, AdaptiveScalarLayer>
    member __.selectedScalar = _selectedScalar_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<ScalarLayer, AdaptiveScalarLayer, AdaptiveScalarLayer>>
    member __.textureLayers = _textureLayers_ :> FSharp.Data.Adaptive.alist<TextureLayer>
    member __.selectedTexture = _selectedTexture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<TextureLayer>>
    member __.surfaceType = __value.surfaceType
    member __.colorCorrection = _colorCorrection_
    member __.homePosition = _homePosition_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Rendering.CameraView>>
    member __.transformation = _transformation_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SurfaceLenses = 
    type Surface with
        static member version_ = ((fun (self : Surface) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Surface) -> { self with version = value }))
        static member guid_ = ((fun (self : Surface) -> self.guid), (fun (value : System.Guid) (self : Surface) -> { self with guid = value }))
        static member name_ = ((fun (self : Surface) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : Surface) -> { self with name = value }))
        static member importPath_ = ((fun (self : Surface) -> self.importPath), (fun (value : Microsoft.FSharp.Core.string) (self : Surface) -> { self with importPath = value }))
        static member opcNames_ = ((fun (self : Surface) -> self.opcNames), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>) (self : Surface) -> { self with opcNames = value }))
        static member opcPaths_ = ((fun (self : Surface) -> self.opcPaths), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>) (self : Surface) -> { self with opcPaths = value }))
        static member relativePaths_ = ((fun (self : Surface) -> self.relativePaths), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with relativePaths = value }))
        static member fillMode_ = ((fun (self : Surface) -> self.fillMode), (fun (value : Aardvark.Rendering.FillMode) (self : Surface) -> { self with fillMode = value }))
        static member cullMode_ = ((fun (self : Surface) -> self.cullMode), (fun (value : Aardvark.Rendering.CullMode) (self : Surface) -> { self with cullMode = value }))
        static member isVisible_ = ((fun (self : Surface) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with isVisible = value }))
        static member isActive_ = ((fun (self : Surface) -> self.isActive), (fun (value : Microsoft.FSharp.Core.bool) (self : Surface) -> { self with isActive = value }))
        static member quality_ = ((fun (self : Surface) -> self.quality), (fun (value : Aardvark.UI.NumericInput) (self : Surface) -> { self with quality = value }))
        static member priority_ = ((fun (self : Surface) -> self.priority), (fun (value : Aardvark.UI.NumericInput) (self : Surface) -> { self with priority = value }))
        static member triangleSize_ = ((fun (self : Surface) -> self.triangleSize), (fun (value : Aardvark.UI.NumericInput) (self : Surface) -> { self with triangleSize = value }))
        static member scaling_ = ((fun (self : Surface) -> self.scaling), (fun (value : Aardvark.UI.NumericInput) (self : Surface) -> { self with scaling = value }))
        static member preTransform_ = ((fun (self : Surface) -> self.preTransform), (fun (value : Aardvark.Base.Trafo3d) (self : Surface) -> { self with preTransform = value }))
        static member scalarLayers_ = ((fun (self : Surface) -> self.scalarLayers), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.int, ScalarLayer>) (self : Surface) -> { self with scalarLayers = value }))
        static member selectedScalar_ = ((fun (self : Surface) -> self.selectedScalar), (fun (value : Microsoft.FSharp.Core.option<ScalarLayer>) (self : Surface) -> { self with selectedScalar = value }))
        static member textureLayers_ = ((fun (self : Surface) -> self.textureLayers), (fun (value : FSharp.Data.Adaptive.IndexList<TextureLayer>) (self : Surface) -> { self with textureLayers = value }))
        static member selectedTexture_ = ((fun (self : Surface) -> self.selectedTexture), (fun (value : Microsoft.FSharp.Core.option<TextureLayer>) (self : Surface) -> { self with selectedTexture = value }))
        static member surfaceType_ = ((fun (self : Surface) -> self.surfaceType), (fun (value : SurfaceType) (self : Surface) -> { self with surfaceType = value }))
        static member colorCorrection_ = ((fun (self : Surface) -> self.colorCorrection), (fun (value : ColorCorrection) (self : Surface) -> { self with colorCorrection = value }))
        static member homePosition_ = ((fun (self : Surface) -> self.homePosition), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Rendering.CameraView>) (self : Surface) -> { self with homePosition = value }))
        static member transformation_ = ((fun (self : Surface) -> self.transformation), (fun (value : Transformations) (self : Surface) -> { self with transformation = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSgSurface(value : SgSurface) =
    let _trafo_ = Aardvark.UI.Trafos.AdaptiveTransformation(value.trafo)
    let _globalBB_ = FSharp.Data.Adaptive.cval(value.globalBB)
    let _sceneGraph_ = FSharp.Data.Adaptive.cval(value.sceneGraph)
    let _picking_ = FSharp.Data.Adaptive.cval(value.picking)
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
            ()
    member __.Current = __adaptive
    member __.surface = __value.surface
    member __.trafo = _trafo_
    member __.globalBB = _globalBB_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Box3d>
    member __.sceneGraph = _sceneGraph_ :> FSharp.Data.Adaptive.aval<Aardvark.SceneGraph.ISg>
    member __.picking = _picking_ :> FSharp.Data.Adaptive.aval<Picking>
    member __.isObj = __value.isObj
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SgSurfaceLenses = 
    type SgSurface with
        static member surface_ = ((fun (self : SgSurface) -> self.surface), (fun (value : System.Guid) (self : SgSurface) -> { self with surface = value }))
        static member trafo_ = ((fun (self : SgSurface) -> self.trafo), (fun (value : Aardvark.UI.Trafos.Transformation) (self : SgSurface) -> { self with trafo = value }))
        static member globalBB_ = ((fun (self : SgSurface) -> self.globalBB), (fun (value : Aardvark.Base.Box3d) (self : SgSurface) -> { self with globalBB = value }))
        static member sceneGraph_ = ((fun (self : SgSurface) -> self.sceneGraph), (fun (value : Aardvark.SceneGraph.ISg) (self : SgSurface) -> { self with sceneGraph = value }))
        static member picking_ = ((fun (self : SgSurface) -> self.picking), (fun (value : Picking) (self : SgSurface) -> { self with picking = value }))
        static member isObj_ = ((fun (self : SgSurface) -> self.isObj), (fun (value : Microsoft.FSharp.Core.bool) (self : SgSurface) -> { self with isObj = value }))

