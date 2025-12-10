//510420a8-3e59-8258-66a4-fd8954056ec7
//246a9246-109e-7d34-0634-ab14a32def2a
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.ImageMapping

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.ImageMapping
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveProjectedImageModel(value : ProjectedImageModel) =
    let _colorMap_ = FSharp.Data.Adaptive.cval(value.colorMap)
    let _useFalseColor_ = FSharp.Data.Adaptive.cval(value.useFalseColor)
    let _selectedChannel_ = FSharp.Data.Adaptive.cval(value.selectedChannel)
    let _channelOptions_ = FSharp.Data.Adaptive.cval(value.channelOptions)
    let _dataType_ = FSharp.Data.Adaptive.cval(value.dataType)
    let _defaultMinValues_ = FSharp.Data.Adaptive.cval(value.defaultMinValues)
    let _defaultMaxValues_ = FSharp.Data.Adaptive.cval(value.defaultMaxValues)
    let _texture_ = FSharp.Data.Adaptive.cval(value.texture)
    let _distance_ = FSharp.Data.Adaptive.cval(value.distance)
    let _time_ = FSharp.Data.Adaptive.cval(value.time)
    let _falseColorModel_ = PRo3D.Base.AdaptiveFalseColorsModel(value.falseColorModel)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ProjectedImageModel) = AdaptiveProjectedImageModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ProjectedImageModel) -> AdaptiveProjectedImageModel(value)) (fun (adaptive : AdaptiveProjectedImageModel) (value : ProjectedImageModel) -> adaptive.Update(value))
    member __.Update(value : ProjectedImageModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ProjectedImageModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _colorMap_.Value <- value.colorMap
            _useFalseColor_.Value <- value.useFalseColor
            _selectedChannel_.Value <- value.selectedChannel
            _channelOptions_.Value <- value.channelOptions
            _dataType_.Value <- value.dataType
            _defaultMinValues_.Value <- value.defaultMinValues
            _defaultMaxValues_.Value <- value.defaultMaxValues
            _texture_.Value <- value.texture
            _distance_.Value <- value.distance
            _time_.Value <- value.time
            _falseColorModel_.Update(value.falseColorModel)
    member __.Current = __adaptive
    member __.colorMap = _colorMap_ :> FSharp.Data.Adaptive.aval<ColorMap>
    member __.useFalseColor = _useFalseColor_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.selectedChannel = _selectedChannel_ :> FSharp.Data.Adaptive.aval<Channel>
    member __.channelOptions = _channelOptions_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Channel>>
    member __.dataType = _dataType_ :> FSharp.Data.Adaptive.aval<DataType>
    member __.defaultMinValues = _defaultMinValues_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>>
    member __.defaultMaxValues = _defaultMaxValues_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>>
    member __.texture = _texture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.distance = _distance_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.time = _time_ :> FSharp.Data.Adaptive.aval<System.DateTime>
    member __.falseColorModel = _falseColorModel_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ProjectedImageModelLenses = 
    type ProjectedImageModel with
        static member colorMap_ = ((fun (self : ProjectedImageModel) -> self.colorMap), (fun (value : ColorMap) (self : ProjectedImageModel) -> { self with colorMap = value }))
        static member useFalseColor_ = ((fun (self : ProjectedImageModel) -> self.useFalseColor), (fun (value : Microsoft.FSharp.Core.bool) (self : ProjectedImageModel) -> { self with useFalseColor = value }))
        static member selectedChannel_ = ((fun (self : ProjectedImageModel) -> self.selectedChannel), (fun (value : Channel) (self : ProjectedImageModel) -> { self with selectedChannel = value }))
        static member channelOptions_ = ((fun (self : ProjectedImageModel) -> self.channelOptions), (fun (value : Microsoft.FSharp.Collections.list<Channel>) (self : ProjectedImageModel) -> { self with channelOptions = value }))
        static member dataType_ = ((fun (self : ProjectedImageModel) -> self.dataType), (fun (value : DataType) (self : ProjectedImageModel) -> { self with dataType = value }))
        static member defaultMinValues_ = ((fun (self : ProjectedImageModel) -> self.defaultMinValues), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>) (self : ProjectedImageModel) -> { self with defaultMinValues = value }))
        static member defaultMaxValues_ = ((fun (self : ProjectedImageModel) -> self.defaultMaxValues), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>) (self : ProjectedImageModel) -> { self with defaultMaxValues = value }))
        static member texture_ = ((fun (self : ProjectedImageModel) -> self.texture), (fun (value : Microsoft.FSharp.Core.string) (self : ProjectedImageModel) -> { self with texture = value }))
        static member distance_ = ((fun (self : ProjectedImageModel) -> self.distance), (fun (value : Microsoft.FSharp.Core.float) (self : ProjectedImageModel) -> { self with distance = value }))
        static member time_ = ((fun (self : ProjectedImageModel) -> self.time), (fun (value : System.DateTime) (self : ProjectedImageModel) -> { self with time = value }))
        static member falseColorModel_ = ((fun (self : ProjectedImageModel) -> self.falseColorModel), (fun (value : PRo3D.Base.FalseColorsModel) (self : ProjectedImageModel) -> { self with falseColorModel = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveBoresightAdjustment(value : BoresightAdjustment) =
    let _roll_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.roll)
    let _pitch_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.pitch)
    let _yaw_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.yaw)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : BoresightAdjustment) = AdaptiveBoresightAdjustment(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : BoresightAdjustment) -> AdaptiveBoresightAdjustment(value)) (fun (adaptive : AdaptiveBoresightAdjustment) (value : BoresightAdjustment) -> adaptive.Update(value))
    member __.Update(value : BoresightAdjustment) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<BoresightAdjustment>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _roll_.Update(value.roll)
            _pitch_.Update(value.pitch)
            _yaw_.Update(value.yaw)
    member __.Current = __adaptive
    member __.roll = _roll_
    member __.pitch = _pitch_
    member __.yaw = _yaw_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module BoresightAdjustmentLenses = 
    type BoresightAdjustment with
        static member roll_ = ((fun (self : BoresightAdjustment) -> self.roll), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : BoresightAdjustment) -> { self with roll = value }))
        static member pitch_ = ((fun (self : BoresightAdjustment) -> self.pitch), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : BoresightAdjustment) -> { self with pitch = value }))
        static member yaw_ = ((fun (self : BoresightAdjustment) -> self.yaw), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : BoresightAdjustment) -> { self with yaw = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveProjectedImageListModel(value : ProjectedImageListModel) =
    let _images_ =
        let inline __arg2 (m : AdaptiveProjectedImageModel) (v : ProjectedImageModel) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelList(value.images, (fun (v : ProjectedImageModel) -> AdaptiveProjectedImageModel(v)), __arg2, (fun (m : AdaptiveProjectedImageModel) -> m))
    let _selectedImage_ = FSharp.Data.Adaptive.cval(value.selectedImage)
    let _editImages_ = FSharp.Data.Adaptive.cval(value.editImages)
    let _projectionOpacity_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.projectionOpacity)
    let _boresightAdjustment_ = AdaptiveBoresightAdjustment(value.boresightAdjustment)
    let _cameraState_ = Aardvark.UI.Primitives.AdaptiveOrbitState(value.cameraState)
    let _instrumentVisibility_ = FSharp.Data.Adaptive.cval(value.instrumentVisibility)
    let _lightingMode_ = FSharp.Data.Adaptive.cval(value.lightingMode)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ProjectedImageListModel) = AdaptiveProjectedImageListModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ProjectedImageListModel) -> AdaptiveProjectedImageListModel(value)) (fun (adaptive : AdaptiveProjectedImageListModel) (value : ProjectedImageListModel) -> adaptive.Update(value))
    member __.Update(value : ProjectedImageListModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ProjectedImageListModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _images_.Update(value.images)
            _selectedImage_.Value <- value.selectedImage
            _editImages_.Value <- value.editImages
            _projectionOpacity_.Update(value.projectionOpacity)
            _boresightAdjustment_.Update(value.boresightAdjustment)
            _cameraState_.Update(value.cameraState)
            _instrumentVisibility_.Value <- value.instrumentVisibility
            _lightingMode_.Value <- value.lightingMode
    member __.Current = __adaptive
    member __.images = _images_ :> FSharp.Data.Adaptive.alist<AdaptiveProjectedImageModel>
    member __.selectedImage = _selectedImage_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.Index>>
    member __.editImages = _editImages_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<FSharp.Data.Adaptive.Index>>
    member __.projectionOpacity = _projectionOpacity_
    member __.boresightAdjustment = _boresightAdjustment_
    member __.cameraState = _cameraState_
    member __.instrumentVisibility = _instrumentVisibility_ :> FSharp.Data.Adaptive.aval<InstrumentVisibilityMode>
    member __.lightingMode = _lightingMode_ :> FSharp.Data.Adaptive.aval<LightingMode>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ProjectedImageListModelLenses = 
    type ProjectedImageListModel with
        static member images_ = ((fun (self : ProjectedImageListModel) -> self.images), (fun (value : FSharp.Data.Adaptive.IndexList<ProjectedImageModel>) (self : ProjectedImageListModel) -> { self with images = value }))
        static member selectedImage_ = ((fun (self : ProjectedImageListModel) -> self.selectedImage), (fun (value : Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.Index>) (self : ProjectedImageListModel) -> { self with selectedImage = value }))
        static member editImages_ = ((fun (self : ProjectedImageListModel) -> self.editImages), (fun (value : Microsoft.FSharp.Collections.list<FSharp.Data.Adaptive.Index>) (self : ProjectedImageListModel) -> { self with editImages = value }))
        static member projectionOpacity_ = ((fun (self : ProjectedImageListModel) -> self.projectionOpacity), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ProjectedImageListModel) -> { self with projectionOpacity = value }))
        static member boresightAdjustment_ = ((fun (self : ProjectedImageListModel) -> self.boresightAdjustment), (fun (value : BoresightAdjustment) (self : ProjectedImageListModel) -> { self with boresightAdjustment = value }))
        static member cameraState_ = ((fun (self : ProjectedImageListModel) -> self.cameraState), (fun (value : Aardvark.UI.Primitives.OrbitState) (self : ProjectedImageListModel) -> { self with cameraState = value }))
        static member instrumentVisibility_ = ((fun (self : ProjectedImageListModel) -> self.instrumentVisibility), (fun (value : InstrumentVisibilityMode) (self : ProjectedImageListModel) -> { self with instrumentVisibility = value }))
        static member lightingMode_ = ((fun (self : ProjectedImageListModel) -> self.lightingMode), (fun (value : LightingMode) (self : ProjectedImageListModel) -> { self with lightingMode = value }))

