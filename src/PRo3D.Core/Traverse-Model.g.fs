//9e8fc73f-a4d2-21cf-91dd-9eddc45e1060
//85a14ba2-3f07-dd2e-bf5a-a83ab68d04fa
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
type AdaptiveSol(value : Sol) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _location_ = FSharp.Data.Adaptive.cval(value.location)
    let _solNumber_ = FSharp.Data.Adaptive.cval(value.solNumber)
    let _site_ = FSharp.Data.Adaptive.cval(value.site)
    let _yaw_ = FSharp.Data.Adaptive.cval(value.yaw)
    let _pitch_ = FSharp.Data.Adaptive.cval(value.pitch)
    let _roll_ = FSharp.Data.Adaptive.cval(value.roll)
    let _tilt_ = FSharp.Data.Adaptive.cval(value.tilt)
    let _note_ = FSharp.Data.Adaptive.cval(value.note)
    let _distanceM_ = FSharp.Data.Adaptive.cval(value.distanceM)
    let _totalDistanceM_ = FSharp.Data.Adaptive.cval(value.totalDistanceM)
    let _length_ = FSharp.Data.Adaptive.cval(value.length)
    let _RMC_ = FSharp.Data.Adaptive.cval(value.RMC)
    let _missionReference_ = FSharp.Data.Adaptive.cval(value.missionReference)
    let _fromRMC_ = FSharp.Data.Adaptive.cval(value.fromRMC)
    let _toRMC_ = FSharp.Data.Adaptive.cval(value.toRMC)
    let _sclkStart_ = FSharp.Data.Adaptive.cval(value.sclkStart)
    let _sclkEnd_ = FSharp.Data.Adaptive.cval(value.sclkEnd)
    let _RIMFAXImageMode_ = FSharp.Data.Adaptive.cval(value.RIMFAXImageMode)
    let _RIMFAXSurfaces_ =
        let inline __arg2 (m : PRo3D.Core.Surface.AdaptiveSgSurface) (v : PRo3D.Core.Surface.SgSurface) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.RIMFAXSurfaces, (fun (v : PRo3D.Core.Surface.SgSurface) -> PRo3D.Core.Surface.AdaptiveSgSurface(v)), __arg2, (fun (m : PRo3D.Core.Surface.AdaptiveSgSurface) -> m))
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Sol) = AdaptiveSol(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Sol) -> AdaptiveSol(value)) (fun (adaptive : AdaptiveSol) (value : Sol) -> adaptive.Update(value))
    member __.Update(value : Sol) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Sol>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _location_.Value <- value.location
            _solNumber_.Value <- value.solNumber
            _site_.Value <- value.site
            _yaw_.Value <- value.yaw
            _pitch_.Value <- value.pitch
            _roll_.Value <- value.roll
            _tilt_.Value <- value.tilt
            _note_.Value <- value.note
            _distanceM_.Value <- value.distanceM
            _totalDistanceM_.Value <- value.totalDistanceM
            _length_.Value <- value.length
            _RMC_.Value <- value.RMC
            _missionReference_.Value <- value.missionReference
            _fromRMC_.Value <- value.fromRMC
            _toRMC_.Value <- value.toRMC
            _sclkStart_.Value <- value.sclkStart
            _sclkEnd_.Value <- value.sclkEnd
            _RIMFAXImageMode_.Value <- value.RIMFAXImageMode
            _RIMFAXSurfaces_.Update(value.RIMFAXSurfaces)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.location = _location_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>>
    member __.solNumber = _solNumber_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.site = _site_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.yaw = _yaw_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.pitch = _pitch_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.roll = _roll_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.tilt = _tilt_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.note = _note_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.distanceM = _distanceM_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.totalDistanceM = _totalDistanceM_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.length = _length_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.RMC = _RMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.missionReference = _missionReference_ :> FSharp.Data.Adaptive.aval<System.Guid>
    member __.fromRMC = _fromRMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.toRMC = _toRMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.sclkStart = _sclkStart_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.sclkEnd = _sclkEnd_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.RIMFAXImageMode = _RIMFAXImageMode_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<RIMFAXImageMode>>
    member __.RIMFAXSurfaces = _RIMFAXSurfaces_ :> FSharp.Data.Adaptive.amap<System.Guid, PRo3D.Core.Surface.AdaptiveSgSurface>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SolLenses = 
    type Sol with
        static member version_ = ((fun (self : Sol) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Sol) -> { self with version = value }))
        static member location_ = ((fun (self : Sol) -> self.location), (fun (value : Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>) (self : Sol) -> { self with location = value }))
        static member solNumber_ = ((fun (self : Sol) -> self.solNumber), (fun (value : Microsoft.FSharp.Core.int) (self : Sol) -> { self with solNumber = value }))
        static member site_ = ((fun (self : Sol) -> self.site), (fun (value : Microsoft.FSharp.Core.int) (self : Sol) -> { self with site = value }))
        static member yaw_ = ((fun (self : Sol) -> self.yaw), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with yaw = value }))
        static member pitch_ = ((fun (self : Sol) -> self.pitch), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with pitch = value }))
        static member roll_ = ((fun (self : Sol) -> self.roll), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with roll = value }))
        static member tilt_ = ((fun (self : Sol) -> self.tilt), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with tilt = value }))
        static member note_ = ((fun (self : Sol) -> self.note), (fun (value : Microsoft.FSharp.Core.string) (self : Sol) -> { self with note = value }))
        static member distanceM_ = ((fun (self : Sol) -> self.distanceM), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with distanceM = value }))
        static member totalDistanceM_ = ((fun (self : Sol) -> self.totalDistanceM), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with totalDistanceM = value }))
        static member length_ = ((fun (self : Sol) -> self.length), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with length = value }))
        static member RMC_ = ((fun (self : Sol) -> self.RMC), (fun (value : Microsoft.FSharp.Core.string) (self : Sol) -> { self with RMC = value }))
        static member missionReference_ = ((fun (self : Sol) -> self.missionReference), (fun (value : System.Guid) (self : Sol) -> { self with missionReference = value }))
        static member fromRMC_ = ((fun (self : Sol) -> self.fromRMC), (fun (value : Microsoft.FSharp.Core.string) (self : Sol) -> { self with fromRMC = value }))
        static member toRMC_ = ((fun (self : Sol) -> self.toRMC), (fun (value : Microsoft.FSharp.Core.string) (self : Sol) -> { self with toRMC = value }))
        static member sclkStart_ = ((fun (self : Sol) -> self.sclkStart), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with sclkStart = value }))
        static member sclkEnd_ = ((fun (self : Sol) -> self.sclkEnd), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with sclkEnd = value }))
        static member RIMFAXImageMode_ = ((fun (self : Sol) -> self.RIMFAXImageMode), (fun (value : Microsoft.FSharp.Core.option<RIMFAXImageMode>) (self : Sol) -> { self with RIMFAXImageMode = value }))
        static member RIMFAXSurfaces_ = ((fun (self : Sol) -> self.RIMFAXSurfaces), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, PRo3D.Core.Surface.SgSurface>) (self : Sol) -> { self with RIMFAXSurfaces = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveTraverse(value : Traverse) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _sols_ = FSharp.Data.Adaptive.cval(value.sols)
    let _selectedSol_ = FSharp.Data.Adaptive.cval(value.selectedSol)
    let _showLines_ = FSharp.Data.Adaptive.cval(value.showLines)
    let _showText_ = FSharp.Data.Adaptive.cval(value.showText)
    let _tTextSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.tTextSize)
    let _tLineWidth_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.tLineWidth)
    let _showDots_ = FSharp.Data.Adaptive.cval(value.showDots)
    let _isVisibleT_ = FSharp.Data.Adaptive.cval(value.isVisibleT)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let _heightOffset_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.heightOffset)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Traverse) = AdaptiveTraverse(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Traverse) -> AdaptiveTraverse(value)) (fun (adaptive : AdaptiveTraverse) (value : Traverse) -> adaptive.Update(value))
    member __.Update(value : Traverse) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Traverse>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _sols_.Value <- value.sols
            _selectedSol_.Value <- value.selectedSol
            _showLines_.Value <- value.showLines
            _showText_.Value <- value.showText
            _tTextSize_.Update(value.tTextSize)
            _tLineWidth_.Update(value.tLineWidth)
            _showDots_.Value <- value.showDots
            _isVisibleT_.Value <- value.isVisibleT
            _color_.Update(value.color)
            _heightOffset_.Update(value.heightOffset)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = __value.guid
    member __.tName = __value.tName
    member __.sols = _sols_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<Sol>>
    member __.traverseType = __value.traverseType
    member __.selectedSol = _selectedSol_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>>
    member __.showLines = _showLines_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showText = _showText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.tTextSize = _tTextSize_
    member __.tLineWidth = _tLineWidth_
    member __.showDots = _showDots_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isVisibleT = _isVisibleT_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.color = _color_
    member __.heightOffset = _heightOffset_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TraverseLenses = 
    type Traverse with
        static member version_ = ((fun (self : Traverse) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Traverse) -> { self with version = value }))
        static member guid_ = ((fun (self : Traverse) -> self.guid), (fun (value : System.Guid) (self : Traverse) -> { self with guid = value }))
        static member tName_ = ((fun (self : Traverse) -> self.tName), (fun (value : Microsoft.FSharp.Core.string) (self : Traverse) -> { self with tName = value }))
        static member sols_ = ((fun (self : Traverse) -> self.sols), (fun (value : Microsoft.FSharp.Collections.List<Sol>) (self : Traverse) -> { self with sols = value }))
        static member traverseType_ = ((fun (self : Traverse) -> self.traverseType), (fun (value : TraverseType) (self : Traverse) -> { self with traverseType = value }))
        static member selectedSol_ = ((fun (self : Traverse) -> self.selectedSol), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>) (self : Traverse) -> { self with selectedSol = value }))
        static member showLines_ = ((fun (self : Traverse) -> self.showLines), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showLines = value }))
        static member showText_ = ((fun (self : Traverse) -> self.showText), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showText = value }))
        static member tTextSize_ = ((fun (self : Traverse) -> self.tTextSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with tTextSize = value }))
        static member tLineWidth_ = ((fun (self : Traverse) -> self.tLineWidth), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with tLineWidth = value }))
        static member showDots_ = ((fun (self : Traverse) -> self.showDots), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showDots = value }))
        static member isVisibleT_ = ((fun (self : Traverse) -> self.isVisibleT), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with isVisibleT = value }))
        static member color_ = ((fun (self : Traverse) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : Traverse) -> { self with color = value }))
        static member heightOffset_ = ((fun (self : Traverse) -> self.heightOffset), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with heightOffset = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveTraverseModel(value : TraverseModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _roverTraverses_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.roverTraverses, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
    let _strategicAnnotationTraverses_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.strategicAnnotationTraverses, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
    let _RIMFAXTraverses_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.RIMFAXTraverses, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
    let _plannedTargetsTraverses_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.plannedTargetsTraverses, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
    let _waypointsTraverses_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.waypointsTraverses, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
    let _selectedTraverse_ = FSharp.Data.Adaptive.cval(value.selectedTraverse)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : TraverseModel) = AdaptiveTraverseModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : TraverseModel) -> AdaptiveTraverseModel(value)) (fun (adaptive : AdaptiveTraverseModel) (value : TraverseModel) -> adaptive.Update(value))
    member __.Update(value : TraverseModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<TraverseModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _roverTraverses_.Update(value.roverTraverses)
            _strategicAnnotationTraverses_.Update(value.strategicAnnotationTraverses)
            _RIMFAXTraverses_.Update(value.RIMFAXTraverses)
            _plannedTargetsTraverses_.Update(value.plannedTargetsTraverses)
            _waypointsTraverses_.Update(value.waypointsTraverses)
            _selectedTraverse_.Value <- value.selectedTraverse
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.roverTraverses = _roverTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.strategicAnnotationTraverses = _strategicAnnotationTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.RIMFAXTraverses = _RIMFAXTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.plannedTargetsTraverses = _plannedTargetsTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.waypointsTraverses = _waypointsTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.selectedTraverse = _selectedTraverse_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TraverseModelLenses = 
    type TraverseModel with
        static member version_ = ((fun (self : TraverseModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : TraverseModel) -> { self with version = value }))
        static member roverTraverses_ = ((fun (self : TraverseModel) -> self.roverTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with roverTraverses = value }))
        static member strategicAnnotationTraverses_ = ((fun (self : TraverseModel) -> self.strategicAnnotationTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with strategicAnnotationTraverses = value }))
        static member RIMFAXTraverses_ = ((fun (self : TraverseModel) -> self.RIMFAXTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with RIMFAXTraverses = value }))
        static member plannedTargetsTraverses_ = ((fun (self : TraverseModel) -> self.plannedTargetsTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with plannedTargetsTraverses = value }))
        static member waypointsTraverses_ = ((fun (self : TraverseModel) -> self.waypointsTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with waypointsTraverses = value }))
        static member selectedTraverse_ = ((fun (self : TraverseModel) -> self.selectedTraverse), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : TraverseModel) -> { self with selectedTraverse = value }))

