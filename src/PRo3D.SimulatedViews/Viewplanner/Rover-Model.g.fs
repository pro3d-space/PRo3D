//296cb7a5-e19b-f937-81d2-1441ae6c0a3d
//d58b2316-e3d0-86a4-c27f-6ead0e34cb7b
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.SimulatedViews

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.SimulatedViews
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveExtrinsics(value : Extrinsics) =
    let _position_ = FSharp.Data.Adaptive.cval(value.position)
    let _camUp_ = FSharp.Data.Adaptive.cval(value.camUp)
    let _camLookAt_ = FSharp.Data.Adaptive.cval(value.camLookAt)
    let _box_ = FSharp.Data.Adaptive.cval(value.box)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Extrinsics) = AdaptiveExtrinsics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Extrinsics) -> AdaptiveExtrinsics(value)) (fun (adaptive : AdaptiveExtrinsics) (value : Extrinsics) -> adaptive.Update(value))
    member __.Update(value : Extrinsics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Extrinsics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _position_.Value <- value.position
            _camUp_.Value <- value.camUp
            _camLookAt_.Value <- value.camLookAt
            _box_.Value <- value.box
    member __.Current = __adaptive
    member __.position = _position_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.camUp = _camUp_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.camLookAt = _camLookAt_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.box = _box_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Box3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ExtrinsicsLenses = 
    type Extrinsics with
        static member position_ = ((fun (self : Extrinsics) -> self.position), (fun (value : Aardvark.Base.V3d) (self : Extrinsics) -> { self with position = value }))
        static member camUp_ = ((fun (self : Extrinsics) -> self.camUp), (fun (value : Aardvark.Base.V3d) (self : Extrinsics) -> { self with camUp = value }))
        static member camLookAt_ = ((fun (self : Extrinsics) -> self.camLookAt), (fun (value : Aardvark.Base.V3d) (self : Extrinsics) -> { self with camLookAt = value }))
        static member box_ = ((fun (self : Extrinsics) -> self.box), (fun (value : Aardvark.Base.Box3d) (self : Extrinsics) -> { self with box = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveInstrument(value : Instrument) =
    let _id_ = FSharp.Data.Adaptive.cval(value.id)
    let _iType_ = FSharp.Data.Adaptive.cval(value.iType)
    let _calibratedFocalLengths_ = FSharp.Data.Adaptive.cval(value.calibratedFocalLengths)
    let _focal_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.focal)
    let _intrinsics_ = FSharp.Data.Adaptive.cval(value.intrinsics)
    let _extrinsics_ = AdaptiveExtrinsics(value.extrinsics)
    let _index_ = FSharp.Data.Adaptive.cval(value.index)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Instrument) = AdaptiveInstrument(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Instrument) -> AdaptiveInstrument(value)) (fun (adaptive : AdaptiveInstrument) (value : Instrument) -> adaptive.Update(value))
    member __.Update(value : Instrument) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Instrument>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _id_.Value <- value.id
            _iType_.Value <- value.iType
            _calibratedFocalLengths_.Value <- value.calibratedFocalLengths
            _focal_.Update(value.focal)
            _intrinsics_.Value <- value.intrinsics
            _extrinsics_.Update(value.extrinsics)
            _index_.Value <- value.index
    member __.Current = __adaptive
    member __.id = _id_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.iType = _iType_ :> FSharp.Data.Adaptive.aval<InstrumentType>
    member __.calibratedFocalLengths = _calibratedFocalLengths_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.double>>
    member __.focal = _focal_
    member __.intrinsics = _intrinsics_ :> FSharp.Data.Adaptive.aval<Intrinsics>
    member __.extrinsics = _extrinsics_
    member __.index = _index_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module InstrumentLenses = 
    type Instrument with
        static member id_ = ((fun (self : Instrument) -> self.id), (fun (value : Microsoft.FSharp.Core.string) (self : Instrument) -> { self with id = value }))
        static member iType_ = ((fun (self : Instrument) -> self.iType), (fun (value : InstrumentType) (self : Instrument) -> { self with iType = value }))
        static member calibratedFocalLengths_ = ((fun (self : Instrument) -> self.calibratedFocalLengths), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.double>) (self : Instrument) -> { self with calibratedFocalLengths = value }))
        static member focal_ = ((fun (self : Instrument) -> self.focal), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Instrument) -> { self with focal = value }))
        static member intrinsics_ = ((fun (self : Instrument) -> self.intrinsics), (fun (value : Intrinsics) (self : Instrument) -> { self with intrinsics = value }))
        static member extrinsics_ = ((fun (self : Instrument) -> self.extrinsics), (fun (value : Extrinsics) (self : Instrument) -> { self with extrinsics = value }))
        static member index_ = ((fun (self : Instrument) -> self.index), (fun (value : Microsoft.FSharp.Core.int) (self : Instrument) -> { self with index = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveAxis(value : Axis) =
    let _id_ = FSharp.Data.Adaptive.cval(value.id)
    let _description_ = FSharp.Data.Adaptive.cval(value.description)
    let _startPoint_ = FSharp.Data.Adaptive.cval(value.startPoint)
    let _endPoint_ = FSharp.Data.Adaptive.cval(value.endPoint)
    let _index_ = FSharp.Data.Adaptive.cval(value.index)
    let _angle_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.angle)
    let _degreesMapped_ = FSharp.Data.Adaptive.cval(value.degreesMapped)
    let _degreesNegated_ = FSharp.Data.Adaptive.cval(value.degreesNegated)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Axis) = AdaptiveAxis(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Axis) -> AdaptiveAxis(value)) (fun (adaptive : AdaptiveAxis) (value : Axis) -> adaptive.Update(value))
    member __.Update(value : Axis) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Axis>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _id_.Value <- value.id
            _description_.Value <- value.description
            _startPoint_.Value <- value.startPoint
            _endPoint_.Value <- value.endPoint
            _index_.Value <- value.index
            _angle_.Update(value.angle)
            _degreesMapped_.Value <- value.degreesMapped
            _degreesNegated_.Value <- value.degreesNegated
    member __.Current = __adaptive
    member __.id = _id_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.description = _description_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.startPoint = _startPoint_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.endPoint = _endPoint_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.index = _index_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.angle = _angle_
    member __.degreesMapped = _degreesMapped_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.degreesNegated = _degreesNegated_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AxisLenses = 
    type Axis with
        static member id_ = ((fun (self : Axis) -> self.id), (fun (value : Microsoft.FSharp.Core.string) (self : Axis) -> { self with id = value }))
        static member description_ = ((fun (self : Axis) -> self.description), (fun (value : Microsoft.FSharp.Core.string) (self : Axis) -> { self with description = value }))
        static member startPoint_ = ((fun (self : Axis) -> self.startPoint), (fun (value : Aardvark.Base.V3d) (self : Axis) -> { self with startPoint = value }))
        static member endPoint_ = ((fun (self : Axis) -> self.endPoint), (fun (value : Aardvark.Base.V3d) (self : Axis) -> { self with endPoint = value }))
        static member index_ = ((fun (self : Axis) -> self.index), (fun (value : Microsoft.FSharp.Core.int) (self : Axis) -> { self with index = value }))
        static member angle_ = ((fun (self : Axis) -> self.angle), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Axis) -> { self with angle = value }))
        static member degreesMapped_ = ((fun (self : Axis) -> self.degreesMapped), (fun (value : Microsoft.FSharp.Core.bool) (self : Axis) -> { self with degreesMapped = value }))
        static member degreesNegated_ = ((fun (self : Axis) -> self.degreesNegated), (fun (value : Microsoft.FSharp.Core.bool) (self : Axis) -> { self with degreesNegated = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRover(value : Rover) =
    let _id_ = FSharp.Data.Adaptive.cval(value.id)
    let _platform2Ground_ = FSharp.Data.Adaptive.cval(value.platform2Ground)
    let _wheelPositions_ = FSharp.Data.Adaptive.cval(value.wheelPositions)
    let _instruments_ =
        let inline __arg2 (m : AdaptiveInstrument) (v : Instrument) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.instruments, (fun (v : Instrument) -> AdaptiveInstrument(v)), __arg2, (fun (m : AdaptiveInstrument) -> m))
    let _axes_ =
        let inline __arg2 (m : AdaptiveAxis) (v : Axis) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.axes, (fun (v : Axis) -> AdaptiveAxis(v)), __arg2, (fun (m : AdaptiveAxis) -> m))
    let _box_ = FSharp.Data.Adaptive.cval(value.box)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Rover) = AdaptiveRover(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Rover) -> AdaptiveRover(value)) (fun (adaptive : AdaptiveRover) (value : Rover) -> adaptive.Update(value))
    member __.Update(value : Rover) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Rover>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _id_.Value <- value.id
            _platform2Ground_.Value <- value.platform2Ground
            _wheelPositions_.Value <- value.wheelPositions
            _instruments_.Update(value.instruments)
            _axes_.Update(value.axes)
            _box_.Value <- value.box
    member __.Current = __adaptive
    member __.id = _id_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.platform2Ground = _platform2Ground_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.M44d>
    member __.wheelPositions = _wheelPositions_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>>
    member __.instruments = _instruments_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, AdaptiveInstrument>
    member __.axes = _axes_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, AdaptiveAxis>
    member __.box = _box_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Box3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RoverLenses = 
    type Rover with
        static member id_ = ((fun (self : Rover) -> self.id), (fun (value : Microsoft.FSharp.Core.string) (self : Rover) -> { self with id = value }))
        static member platform2Ground_ = ((fun (self : Rover) -> self.platform2Ground), (fun (value : Aardvark.Base.M44d) (self : Rover) -> { self with platform2Ground = value }))
        static member wheelPositions_ = ((fun (self : Rover) -> self.wheelPositions), (fun (value : Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>) (self : Rover) -> { self with wheelPositions = value }))
        static member instruments_ = ((fun (self : Rover) -> self.instruments), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Instrument>) (self : Rover) -> { self with instruments = value }))
        static member axes_ = ((fun (self : Rover) -> self.axes), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Axis>) (self : Rover) -> { self with axes = value }))
        static member box_ = ((fun (self : Rover) -> self.box), (fun (value : Aardvark.Base.Box3d) (self : Rover) -> { self with box = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRoverModel(value : RoverModel) =
    let _rovers_ =
        let inline __arg2 (m : AdaptiveRover) (v : Rover) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.rovers, (fun (v : Rover) -> AdaptiveRover(v)), __arg2, (fun (m : AdaptiveRover) -> m))
    let _platforms_ = FSharp.Data.Adaptive.cmap(value.platforms)
    let _selectedRover_ =
        let inline __arg2 (o : System.Object) (v : Rover) =
            (unbox<AdaptiveRover> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : Rover) =
            (unbox<AdaptiveRover> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.SimulatedViews.Rover, PRo3D.SimulatedViews.AdaptiveRover, PRo3D.SimulatedViews.AdaptiveRover>(value.selectedRover, (fun (v : Rover) -> AdaptiveRover(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveRover> o), (fun (v : Rover) -> AdaptiveRover(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveRover> o))
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : RoverModel) = AdaptiveRoverModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : RoverModel) -> AdaptiveRoverModel(value)) (fun (adaptive : AdaptiveRoverModel) (value : RoverModel) -> adaptive.Update(value))
    member __.Update(value : RoverModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RoverModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _rovers_.Update(value.rovers)
            _platforms_.Value <- value.platforms
            _selectedRover_.Update(value.selectedRover)
    member __.Current = __adaptive
    member __.rovers = _rovers_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, AdaptiveRover>
    member __.platforms = _platforms_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, JR.InstrumentPlatforms.SPlatform>
    member __.selectedRover = _selectedRover_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<Rover, AdaptiveRover, AdaptiveRover>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RoverModelLenses = 
    type RoverModel with
        static member rovers_ = ((fun (self : RoverModel) -> self.rovers), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Rover>) (self : RoverModel) -> { self with rovers = value }))
        static member platforms_ = ((fun (self : RoverModel) -> self.platforms), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, JR.InstrumentPlatforms.SPlatform>) (self : RoverModel) -> { self with platforms = value }))
        static member selectedRover_ = ((fun (self : RoverModel) -> self.selectedRover), (fun (value : Microsoft.FSharp.Core.option<Rover>) (self : RoverModel) -> { self with selectedRover = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSimulatedViewData(value : SimulatedViewData) =
    let _fileInfo_ = FSharp.Data.Adaptive.cval(value.fileInfo)
    let _calibration_ = FSharp.Data.Adaptive.cval(value.calibration)
    let _acquisition_ = FSharp.Data.Adaptive.cval(value.acquisition)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SimulatedViewData) = AdaptiveSimulatedViewData(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SimulatedViewData) -> AdaptiveSimulatedViewData(value)) (fun (adaptive : AdaptiveSimulatedViewData) (value : SimulatedViewData) -> adaptive.Update(value))
    member __.Update(value : SimulatedViewData) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SimulatedViewData>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _fileInfo_.Value <- value.fileInfo
            _calibration_.Value <- value.calibration
            _acquisition_.Value <- value.acquisition
    member __.Current = __adaptive
    member __.fileInfo = _fileInfo_ :> FSharp.Data.Adaptive.aval<FileInfo>
    member __.calibration = _calibration_ :> FSharp.Data.Adaptive.aval<Calibration>
    member __.acquisition = _acquisition_ :> FSharp.Data.Adaptive.aval<Acquisition>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SimulatedViewDataLenses = 
    type SimulatedViewData with
        static member fileInfo_ = ((fun (self : SimulatedViewData) -> self.fileInfo), (fun (value : FileInfo) (self : SimulatedViewData) -> { self with fileInfo = value }))
        static member calibration_ = ((fun (self : SimulatedViewData) -> self.calibration), (fun (value : Calibration) (self : SimulatedViewData) -> { self with calibration = value }))
        static member acquisition_ = ((fun (self : SimulatedViewData) -> self.acquisition), (fun (value : Acquisition) (self : SimulatedViewData) -> { self with acquisition = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveFootPrint(value : FootPrint) =
    let _vpId_ = FSharp.Data.Adaptive.cval(value.vpId)
    let _isVisible_ = FSharp.Data.Adaptive.cval(value.isVisible)
    let _projectionMatrix_ = FSharp.Data.Adaptive.cval(value.projectionMatrix)
    let _instViewMatrix_ = FSharp.Data.Adaptive.cval(value.instViewMatrix)
    let _projTex_ = FSharp.Data.Adaptive.cval(value.projTex)
    let _globalToLocalPos_ = FSharp.Data.Adaptive.cval(value.globalToLocalPos)
    let _depthTexture_ = FSharp.Data.Adaptive.cval(value.depthTexture)
    let _isDepthVisible_ = FSharp.Data.Adaptive.cval(value.isDepthVisible)
    let _depthColorLegend_ = PRo3D.Base.AdaptiveFalseColorsModel(value.depthColorLegend)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : FootPrint) = AdaptiveFootPrint(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : FootPrint) -> AdaptiveFootPrint(value)) (fun (adaptive : AdaptiveFootPrint) (value : FootPrint) -> adaptive.Update(value))
    member __.Update(value : FootPrint) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<FootPrint>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _vpId_.Value <- value.vpId
            _isVisible_.Value <- value.isVisible
            _projectionMatrix_.Value <- value.projectionMatrix
            _instViewMatrix_.Value <- value.instViewMatrix
            _projTex_.Value <- value.projTex
            _globalToLocalPos_.Value <- value.globalToLocalPos
            _depthTexture_.Value <- value.depthTexture
            _isDepthVisible_.Value <- value.isDepthVisible
            _depthColorLegend_.Update(value.depthColorLegend)
    member __.Current = __adaptive
    member __.vpId = _vpId_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.Guid>>
    member __.isVisible = _isVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.projectionMatrix = _projectionMatrix_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.M44d>
    member __.instViewMatrix = _instViewMatrix_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.M44d>
    member __.projTex = _projTex_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.ITexture>
    member __.globalToLocalPos = _globalToLocalPos_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.depthTexture = _depthTexture_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Aardvark.Rendering.IBackendTexture>>
    member __.isDepthVisible = _isDepthVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.depthColorLegend = _depthColorLegend_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module FootPrintLenses = 
    type FootPrint with
        static member vpId_ = ((fun (self : FootPrint) -> self.vpId), (fun (value : Microsoft.FSharp.Core.option<System.Guid>) (self : FootPrint) -> { self with vpId = value }))
        static member isVisible_ = ((fun (self : FootPrint) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : FootPrint) -> { self with isVisible = value }))
        static member projectionMatrix_ = ((fun (self : FootPrint) -> self.projectionMatrix), (fun (value : Aardvark.Base.M44d) (self : FootPrint) -> { self with projectionMatrix = value }))
        static member instViewMatrix_ = ((fun (self : FootPrint) -> self.instViewMatrix), (fun (value : Aardvark.Base.M44d) (self : FootPrint) -> { self with instViewMatrix = value }))
        static member projTex_ = ((fun (self : FootPrint) -> self.projTex), (fun (value : Aardvark.Rendering.ITexture) (self : FootPrint) -> { self with projTex = value }))
        static member globalToLocalPos_ = ((fun (self : FootPrint) -> self.globalToLocalPos), (fun (value : Aardvark.Base.V3d) (self : FootPrint) -> { self with globalToLocalPos = value }))
        static member depthTexture_ = ((fun (self : FootPrint) -> self.depthTexture), (fun (value : Microsoft.FSharp.Core.option<Aardvark.Rendering.IBackendTexture>) (self : FootPrint) -> { self with depthTexture = value }))
        static member isDepthVisible_ = ((fun (self : FootPrint) -> self.isDepthVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : FootPrint) -> { self with isDepthVisible = value }))
        static member depthColorLegend_ = ((fun (self : FootPrint) -> self.depthColorLegend), (fun (value : PRo3D.Base.FalseColorsModel) (self : FootPrint) -> { self with depthColorLegend = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveViewPlan(value : ViewPlan) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _position_ = FSharp.Data.Adaptive.cval(value.position)
    let _lookAt_ = FSharp.Data.Adaptive.cval(value.lookAt)
    let _viewerState_ = FSharp.Data.Adaptive.cval(value.viewerState)
    let _vectorsVisible_ = FSharp.Data.Adaptive.cval(value.vectorsVisible)
    let _rover_ = AdaptiveRover(value.rover)
    let _roverTrafo_ = FSharp.Data.Adaptive.cval(value.roverTrafo)
    let _isVisible_ = FSharp.Data.Adaptive.cval(value.isVisible)
    let _selectedInstrument_ =
        let inline __arg2 (o : System.Object) (v : Instrument) =
            (unbox<AdaptiveInstrument> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : Instrument) =
            (unbox<AdaptiveInstrument> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.SimulatedViews.Instrument, PRo3D.SimulatedViews.AdaptiveInstrument, PRo3D.SimulatedViews.AdaptiveInstrument>(value.selectedInstrument, (fun (v : Instrument) -> AdaptiveInstrument(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveInstrument> o), (fun (v : Instrument) -> AdaptiveInstrument(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveInstrument> o))
    let _selectedAxis_ =
        let inline __arg2 (o : System.Object) (v : Axis) =
            (unbox<AdaptiveAxis> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : Axis) =
            (unbox<AdaptiveAxis> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.SimulatedViews.Axis, PRo3D.SimulatedViews.AdaptiveAxis, PRo3D.SimulatedViews.AdaptiveAxis>(value.selectedAxis, (fun (v : Axis) -> AdaptiveAxis(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveAxis> o), (fun (v : Axis) -> AdaptiveAxis(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveAxis> o))
    let _currentAngle_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.currentAngle)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ViewPlan) = AdaptiveViewPlan(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ViewPlan) -> AdaptiveViewPlan(value)) (fun (adaptive : AdaptiveViewPlan) (value : ViewPlan) -> adaptive.Update(value))
    member __.Update(value : ViewPlan) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ViewPlan>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _name_.Value <- value.name
            _position_.Value <- value.position
            _lookAt_.Value <- value.lookAt
            _viewerState_.Value <- value.viewerState
            _vectorsVisible_.Value <- value.vectorsVisible
            _rover_.Update(value.rover)
            _roverTrafo_.Value <- value.roverTrafo
            _isVisible_.Value <- value.isVisible
            _selectedInstrument_.Update(value.selectedInstrument)
            _selectedAxis_.Update(value.selectedAxis)
            _currentAngle_.Update(value.currentAngle)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.id = __value.id
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.position = _position_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.lookAt = _lookAt_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.viewerState = _viewerState_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.vectorsVisible = _vectorsVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.rover = _rover_
    member __.roverTrafo = _roverTrafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.isVisible = _isVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.selectedInstrument = _selectedInstrument_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<Instrument, AdaptiveInstrument, AdaptiveInstrument>>
    member __.selectedAxis = _selectedAxis_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<Axis, AdaptiveAxis, AdaptiveAxis>>
    member __.currentAngle = _currentAngle_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ViewPlanLenses = 
    type ViewPlan with
        static member version_ = ((fun (self : ViewPlan) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ViewPlan) -> { self with version = value }))
        static member id_ = ((fun (self : ViewPlan) -> self.id), (fun (value : System.Guid) (self : ViewPlan) -> { self with id = value }))
        static member name_ = ((fun (self : ViewPlan) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : ViewPlan) -> { self with name = value }))
        static member position_ = ((fun (self : ViewPlan) -> self.position), (fun (value : Aardvark.Base.V3d) (self : ViewPlan) -> { self with position = value }))
        static member lookAt_ = ((fun (self : ViewPlan) -> self.lookAt), (fun (value : Aardvark.Base.V3d) (self : ViewPlan) -> { self with lookAt = value }))
        static member viewerState_ = ((fun (self : ViewPlan) -> self.viewerState), (fun (value : Aardvark.Rendering.CameraView) (self : ViewPlan) -> { self with viewerState = value }))
        static member vectorsVisible_ = ((fun (self : ViewPlan) -> self.vectorsVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewPlan) -> { self with vectorsVisible = value }))
        static member rover_ = ((fun (self : ViewPlan) -> self.rover), (fun (value : Rover) (self : ViewPlan) -> { self with rover = value }))
        static member roverTrafo_ = ((fun (self : ViewPlan) -> self.roverTrafo), (fun (value : Aardvark.Base.Trafo3d) (self : ViewPlan) -> { self with roverTrafo = value }))
        static member isVisible_ = ((fun (self : ViewPlan) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : ViewPlan) -> { self with isVisible = value }))
        static member selectedInstrument_ = ((fun (self : ViewPlan) -> self.selectedInstrument), (fun (value : Microsoft.FSharp.Core.option<Instrument>) (self : ViewPlan) -> { self with selectedInstrument = value }))
        static member selectedAxis_ = ((fun (self : ViewPlan) -> self.selectedAxis), (fun (value : Microsoft.FSharp.Core.option<Axis>) (self : ViewPlan) -> { self with selectedAxis = value }))
        static member currentAngle_ = ((fun (self : ViewPlan) -> self.currentAngle), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : ViewPlan) -> { self with currentAngle = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveViewPlanModel(value : ViewPlanModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _viewPlans_ =
        let inline __arg2 (m : AdaptiveViewPlan) (v : ViewPlan) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.viewPlans, (fun (v : ViewPlan) -> AdaptiveViewPlan(v)), __arg2, (fun (m : AdaptiveViewPlan) -> m))
    let _selectedViewPlan_ = FSharp.Data.Adaptive.cval(value.selectedViewPlan)
    let _working_ = FSharp.Data.Adaptive.cval(value.working)
    let _roverModel_ = AdaptiveRoverModel(value.roverModel)
    let _instrumentCam_ = FSharp.Data.Adaptive.cval(value.instrumentCam)
    let _instrumentFrustum_ = FSharp.Data.Adaptive.cval(value.instrumentFrustum)
    let _footPrint_ = AdaptiveFootPrint(value.footPrint)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ViewPlanModel) = AdaptiveViewPlanModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ViewPlanModel) -> AdaptiveViewPlanModel(value)) (fun (adaptive : AdaptiveViewPlanModel) (value : ViewPlanModel) -> adaptive.Update(value))
    member __.Update(value : ViewPlanModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ViewPlanModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _viewPlans_.Update(value.viewPlans)
            _selectedViewPlan_.Value <- value.selectedViewPlan
            _working_.Value <- value.working
            _roverModel_.Update(value.roverModel)
            _instrumentCam_.Value <- value.instrumentCam
            _instrumentFrustum_.Value <- value.instrumentFrustum
            _footPrint_.Update(value.footPrint)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.viewPlans = _viewPlans_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveViewPlan>
    member __.selectedViewPlan = _selectedViewPlan_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
    member __.working = _working_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>>
    member __.roverModel = _roverModel_
    member __.instrumentCam = _instrumentCam_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.instrumentFrustum = _instrumentFrustum_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.Frustum>
    member __.footPrint = _footPrint_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ViewPlanModelLenses = 
    type ViewPlanModel with
        static member version_ = ((fun (self : ViewPlanModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ViewPlanModel) -> { self with version = value }))
        static member viewPlans_ = ((fun (self : ViewPlanModel) -> self.viewPlans), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, ViewPlan>) (self : ViewPlanModel) -> { self with viewPlans = value }))
        static member selectedViewPlan_ = ((fun (self : ViewPlanModel) -> self.selectedViewPlan), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : ViewPlanModel) -> { self with selectedViewPlan = value }))
        static member working_ = ((fun (self : ViewPlanModel) -> self.working), (fun (value : Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>) (self : ViewPlanModel) -> { self with working = value }))
        static member roverModel_ = ((fun (self : ViewPlanModel) -> self.roverModel), (fun (value : RoverModel) (self : ViewPlanModel) -> { self with roverModel = value }))
        static member instrumentCam_ = ((fun (self : ViewPlanModel) -> self.instrumentCam), (fun (value : Aardvark.Rendering.CameraView) (self : ViewPlanModel) -> { self with instrumentCam = value }))
        static member instrumentFrustum_ = ((fun (self : ViewPlanModel) -> self.instrumentFrustum), (fun (value : Aardvark.Rendering.Frustum) (self : ViewPlanModel) -> { self with instrumentFrustum = value }))
        static member footPrint_ = ((fun (self : ViewPlanModel) -> self.footPrint), (fun (value : FootPrint) (self : ViewPlanModel) -> { self with footPrint = value }))

