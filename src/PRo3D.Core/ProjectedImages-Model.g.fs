//032c4b68-4eb1-7d27-3b6d-a3208d742797
//a8321013-277f-3a36-4c9f-2f2c1ced0b52
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.ImageMapping.Model

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.ImageMapping.Model
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveImage(value : Image) =
    let _colorMap_ = FSharp.Data.Adaptive.cval(value.colorMap)
    let _useFalseColor_ = FSharp.Data.Adaptive.cval(value.useFalseColor)
    let _selectedChannel_ = FSharp.Data.Adaptive.cval(value.selectedChannel)
    let _channelOptions_ = FSharp.Data.Adaptive.cval(value.channelOptions)
    let _dataType_ = FSharp.Data.Adaptive.cval(value.dataType)
    let _defaultMinValues_ = FSharp.Data.Adaptive.cval(value.defaultMinValues)
    let _defaultMaxValues_ = FSharp.Data.Adaptive.cval(value.defaultMaxValues)
    let _inputMinValue_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.inputMinValue)
    let _inputMaxValue_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.inputMaxValue)
    let _texture_ = FSharp.Data.Adaptive.cval(value.texture)
    let _distance_ = FSharp.Data.Adaptive.cval(value.distance)
    let _time_ = FSharp.Data.Adaptive.cval(value.time)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Image) = AdaptiveImage(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Image) -> AdaptiveImage(value)) (fun (adaptive : AdaptiveImage) (value : Image) -> adaptive.Update(value))
    member __.Update(value : Image) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Image>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _colorMap_.Value <- value.colorMap
            _useFalseColor_.Value <- value.useFalseColor
            _selectedChannel_.Value <- value.selectedChannel
            _channelOptions_.Value <- value.channelOptions
            _dataType_.Value <- value.dataType
            _defaultMinValues_.Value <- value.defaultMinValues
            _defaultMaxValues_.Value <- value.defaultMaxValues
            _inputMinValue_.Update(value.inputMinValue)
            _inputMaxValue_.Update(value.inputMaxValue)
            _texture_.Value <- value.texture
            _distance_.Value <- value.distance
            _time_.Value <- value.time
    member __.Current = __adaptive
    member __.colorMap = _colorMap_ :> FSharp.Data.Adaptive.aval<ColorMap>
    member __.useFalseColor = _useFalseColor_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.selectedChannel = _selectedChannel_ :> FSharp.Data.Adaptive.aval<Channel>
    member __.channelOptions = _channelOptions_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Channel>>
    member __.dataType = _dataType_ :> FSharp.Data.Adaptive.aval<DataType>
    member __.defaultMinValues = _defaultMinValues_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>>
    member __.defaultMaxValues = _defaultMaxValues_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>>
    member __.inputMinValue = _inputMinValue_
    member __.inputMaxValue = _inputMaxValue_
    member __.texture = _texture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.distance = _distance_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.time = _time_ :> FSharp.Data.Adaptive.aval<System.DateTime>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ImageLenses = 
    type Image with
        static member colorMap_ = ((fun (self : Image) -> self.colorMap), (fun (value : ColorMap) (self : Image) -> { self with colorMap = value }))
        static member useFalseColor_ = ((fun (self : Image) -> self.useFalseColor), (fun (value : Microsoft.FSharp.Core.bool) (self : Image) -> { self with useFalseColor = value }))
        static member selectedChannel_ = ((fun (self : Image) -> self.selectedChannel), (fun (value : Channel) (self : Image) -> { self with selectedChannel = value }))
        static member channelOptions_ = ((fun (self : Image) -> self.channelOptions), (fun (value : Microsoft.FSharp.Collections.list<Channel>) (self : Image) -> { self with channelOptions = value }))
        static member dataType_ = ((fun (self : Image) -> self.dataType), (fun (value : DataType) (self : Image) -> { self with dataType = value }))
        static member defaultMinValues_ = ((fun (self : Image) -> self.defaultMinValues), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>) (self : Image) -> { self with defaultMinValues = value }))
        static member defaultMaxValues_ = ((fun (self : Image) -> self.defaultMaxValues), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.float>) (self : Image) -> { self with defaultMaxValues = value }))
        static member inputMinValue_ = ((fun (self : Image) -> self.inputMinValue), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Image) -> { self with inputMinValue = value }))
        static member inputMaxValue_ = ((fun (self : Image) -> self.inputMaxValue), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Image) -> { self with inputMaxValue = value }))
        static member texture_ = ((fun (self : Image) -> self.texture), (fun (value : Microsoft.FSharp.Core.string) (self : Image) -> { self with texture = value }))
        static member distance_ = ((fun (self : Image) -> self.distance), (fun (value : Microsoft.FSharp.Core.float) (self : Image) -> { self with distance = value }))
        static member time_ = ((fun (self : Image) -> self.time), (fun (value : System.DateTime) (self : Image) -> { self with time = value }))
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
type AdaptiveProjectedImagesModel(value : ProjectedImagesModel) =
    let _images_ =
        let inline __arg2 (m : AdaptiveImage) (v : Image) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelList(value.images, (fun (v : Image) -> AdaptiveImage(v)), __arg2, (fun (m : AdaptiveImage) -> m))
    let _selectedImage_ = FSharp.Data.Adaptive.cval(value.selectedImage)
    let _editImages_ = FSharp.Data.Adaptive.cval(value.editImages)
    let _projectionOpacity_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.projectionOpacity)
    let _boresightAdjustment_ = AdaptiveBoresightAdjustment(value.boresightAdjustment)
    let _cameraState_ = Aardvark.UI.Primitives.AdaptiveOrbitState(value.cameraState)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ProjectedImagesModel) = AdaptiveProjectedImagesModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ProjectedImagesModel) -> AdaptiveProjectedImagesModel(value)) (fun (adaptive : AdaptiveProjectedImagesModel) (value : ProjectedImagesModel) -> adaptive.Update(value))
    member __.Update(value : ProjectedImagesModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ProjectedImagesModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _images_.Update(value.images)
            _selectedImage_.Value <- value.selectedImage
            _editImages_.Value <- value.editImages
            _projectionOpacity_.Update(value.projectionOpacity)
            _boresightAdjustment_.Update(value.boresightAdjustment)
            _cameraState_.Update(value.cameraState)
    member __.Current = __adaptive
    member __.images = _images_ :> FSharp.Data.Adaptive.alist<AdaptiveImage>
    member __.selectedImage = _selectedImage_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.Index>>
    member __.editImages = _editImages_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<FSharp.Data.Adaptive.Index>>
    member __.projectionOpacity = _projectionOpacity_
    member __.boresightAdjustment = _boresightAdjustment_
    member __.cameraState = _cameraState_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ProjectedImagesModelLenses = 
    type ProjectedImagesModel with
        static member images_ = ((fun (self : ProjectedImagesModel) -> self.images), (fun (value : FSharp.Data.Adaptive.IndexList<Image>) (self : ProjectedImagesModel) -> { self with images = value }))
        static member selectedImage_ = ((fun (self : ProjectedImagesModel) -> self.selectedImage), (fun (value : Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.Index>) (self : ProjectedImagesModel) -> { self with selectedImage = value }))
        static member editImages_ = ((fun (self : ProjectedImagesModel) -> self.editImages), (fun (value : Microsoft.FSharp.Collections.list<FSharp.Data.Adaptive.Index>) (self : ProjectedImagesModel) -> { self with editImages = value }))
        static member projectionOpacity_ = ((fun (self : ProjectedImagesModel) -> self.projectionOpacity), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ProjectedImagesModel) -> { self with projectionOpacity = value }))
        static member boresightAdjustment_ = ((fun (self : ProjectedImagesModel) -> self.boresightAdjustment), (fun (value : BoresightAdjustment) (self : ProjectedImagesModel) -> { self with boresightAdjustment = value }))
        static member cameraState_ = ((fun (self : ProjectedImagesModel) -> self.cameraState), (fun (value : Aardvark.UI.Primitives.OrbitState) (self : ProjectedImagesModel) -> { self with cameraState = value }))

