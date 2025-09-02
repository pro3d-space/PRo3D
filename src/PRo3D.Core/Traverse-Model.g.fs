//51d4d1a9-ba73-454e-3606-11e1316a8238
//8f05c0c0-ec4f-d62c-ce3f-7ebf5a2e293b
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
type AdaptiveRoverMetrics(value : RoverMetrics) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _length_ = FSharp.Data.Adaptive.cval(value.length)
    let _fromRMC_ = FSharp.Data.Adaptive.cval(value.fromRMC)
    let _toRMC_ = FSharp.Data.Adaptive.cval(value.toRMC)
    let _sclkStart_ = FSharp.Data.Adaptive.cval(value.sclkStart)
    let _sclkEnd_ = FSharp.Data.Adaptive.cval(value.sclkEnd)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : RoverMetrics) = AdaptiveRoverMetrics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : RoverMetrics) -> AdaptiveRoverMetrics(value)) (fun (adaptive : AdaptiveRoverMetrics) (value : RoverMetrics) -> adaptive.Update(value))
    member __.Update(value : RoverMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RoverMetrics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _length_.Value <- value.length
            _fromRMC_.Value <- value.fromRMC
            _toRMC_.Value <- value.toRMC
            _sclkStart_.Value <- value.sclkStart
            _sclkEnd_.Value <- value.sclkEnd
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.length = _length_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.fromRMC = _fromRMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.toRMC = _toRMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.sclkStart = _sclkStart_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.sclkEnd = _sclkEnd_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RoverMetricsLenses = 
    type RoverMetrics with
        static member version_ = ((fun (self : RoverMetrics) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : RoverMetrics) -> { self with version = value }))
        static member length_ = ((fun (self : RoverMetrics) -> self.length), (fun (value : Microsoft.FSharp.Core.float) (self : RoverMetrics) -> { self with length = value }))
        static member fromRMC_ = ((fun (self : RoverMetrics) -> self.fromRMC), (fun (value : Microsoft.FSharp.Core.string) (self : RoverMetrics) -> { self with fromRMC = value }))
        static member toRMC_ = ((fun (self : RoverMetrics) -> self.toRMC), (fun (value : Microsoft.FSharp.Core.string) (self : RoverMetrics) -> { self with toRMC = value }))
        static member sclkStart_ = ((fun (self : RoverMetrics) -> self.sclkStart), (fun (value : Microsoft.FSharp.Core.float) (self : RoverMetrics) -> { self with sclkStart = value }))
        static member sclkEnd_ = ((fun (self : RoverMetrics) -> self.sclkEnd), (fun (value : Microsoft.FSharp.Core.float) (self : RoverMetrics) -> { self with sclkEnd = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRIMFAXSurfaceMetrics(value : RIMFAXSurfaceMetrics) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _RIMFAXImageModeOptions_ = FSharp.Data.Adaptive.cval(value.RIMFAXImageModeOptions)
    let _RIMFAXImageMode_ = FSharp.Data.Adaptive.cval(value.RIMFAXImageMode)
    let _RIMFAXSurfaces_ =
        let inline __arg2 (m : PRo3D.Core.Surface.AdaptiveSgSurface) (v : PRo3D.Core.Surface.SgSurface) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.RIMFAXSurfaces, (fun (v : PRo3D.Core.Surface.SgSurface) -> PRo3D.Core.Surface.AdaptiveSgSurface(v)), __arg2, (fun (m : PRo3D.Core.Surface.AdaptiveSgSurface) -> m))
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : RIMFAXSurfaceMetrics) = AdaptiveRIMFAXSurfaceMetrics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : RIMFAXSurfaceMetrics) -> AdaptiveRIMFAXSurfaceMetrics(value)) (fun (adaptive : AdaptiveRIMFAXSurfaceMetrics) (value : RIMFAXSurfaceMetrics) -> adaptive.Update(value))
    member __.Update(value : RIMFAXSurfaceMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RIMFAXSurfaceMetrics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _RIMFAXImageModeOptions_.Value <- value.RIMFAXImageModeOptions
            _RIMFAXImageMode_.Value <- value.RIMFAXImageMode
            _RIMFAXSurfaces_.Update(value.RIMFAXSurfaces)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.RIMFAXImageModeOptions = _RIMFAXImageModeOptions_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<Microsoft.FSharp.Core.string>>
    member __.RIMFAXImageMode = _RIMFAXImageMode_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.RIMFAXSurfaces = _RIMFAXSurfaces_ :> FSharp.Data.Adaptive.amap<System.Guid, PRo3D.Core.Surface.AdaptiveSgSurface>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RIMFAXSurfaceMetricsLenses = 
    type RIMFAXSurfaceMetrics with
        static member version_ = ((fun (self : RIMFAXSurfaceMetrics) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : RIMFAXSurfaceMetrics) -> { self with version = value }))
        static member RIMFAXImageModeOptions_ = ((fun (self : RIMFAXSurfaceMetrics) -> self.RIMFAXImageModeOptions), (fun (value : Microsoft.FSharp.Collections.List<Microsoft.FSharp.Core.string>) (self : RIMFAXSurfaceMetrics) -> { self with RIMFAXImageModeOptions = value }))
        static member RIMFAXImageMode_ = ((fun (self : RIMFAXSurfaceMetrics) -> self.RIMFAXImageMode), (fun (value : Microsoft.FSharp.Core.string) (self : RIMFAXSurfaceMetrics) -> { self with RIMFAXImageMode = value }))
        static member RIMFAXSurfaces_ = ((fun (self : RIMFAXSurfaceMetrics) -> self.RIMFAXSurfaces), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, PRo3D.Core.Surface.SgSurface>) (self : RIMFAXSurfaceMetrics) -> { self with RIMFAXSurfaces = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRIMFAXMetrics(value : RIMFAXMetrics) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _fromRMC_ = FSharp.Data.Adaptive.cval(value.fromRMC)
    let _toRMC_ = FSharp.Data.Adaptive.cval(value.toRMC)
    let _sclkStart_ = FSharp.Data.Adaptive.cval(value.sclkStart)
    let _sclkEnd_ = FSharp.Data.Adaptive.cval(value.sclkEnd)
    let _RIMFAXSurfaceProperties_ =
        let inline __arg2 (o : System.Object) (v : RIMFAXSurfaceMetrics) =
            (unbox<AdaptiveRIMFAXSurfaceMetrics> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : RIMFAXSurfaceMetrics) =
            (unbox<AdaptiveRIMFAXSurfaceMetrics> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Core.RIMFAXSurfaceMetrics, PRo3D.Core.AdaptiveRIMFAXSurfaceMetrics, PRo3D.Core.AdaptiveRIMFAXSurfaceMetrics>(value.RIMFAXSurfaceProperties, (fun (v : RIMFAXSurfaceMetrics) -> AdaptiveRIMFAXSurfaceMetrics(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveRIMFAXSurfaceMetrics> o), (fun (v : RIMFAXSurfaceMetrics) -> AdaptiveRIMFAXSurfaceMetrics(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveRIMFAXSurfaceMetrics> o))
    let _length_ = FSharp.Data.Adaptive.cval(value.length)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : RIMFAXMetrics) = AdaptiveRIMFAXMetrics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : RIMFAXMetrics) -> AdaptiveRIMFAXMetrics(value)) (fun (adaptive : AdaptiveRIMFAXMetrics) (value : RIMFAXMetrics) -> adaptive.Update(value))
    member __.Update(value : RIMFAXMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RIMFAXMetrics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _fromRMC_.Value <- value.fromRMC
            _toRMC_.Value <- value.toRMC
            _sclkStart_.Value <- value.sclkStart
            _sclkEnd_.Value <- value.sclkEnd
            _RIMFAXSurfaceProperties_.Update(value.RIMFAXSurfaceProperties)
            _length_.Value <- value.length
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.fromRMC = _fromRMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.toRMC = _toRMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.sclkStart = _sclkStart_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.sclkEnd = _sclkEnd_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.RIMFAXSurfaceProperties = _RIMFAXSurfaceProperties_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<RIMFAXSurfaceMetrics, AdaptiveRIMFAXSurfaceMetrics, AdaptiveRIMFAXSurfaceMetrics>>
    member __.length = _length_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RIMFAXMetricsLenses = 
    type RIMFAXMetrics with
        static member version_ = ((fun (self : RIMFAXMetrics) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : RIMFAXMetrics) -> { self with version = value }))
        static member fromRMC_ = ((fun (self : RIMFAXMetrics) -> self.fromRMC), (fun (value : Microsoft.FSharp.Core.string) (self : RIMFAXMetrics) -> { self with fromRMC = value }))
        static member toRMC_ = ((fun (self : RIMFAXMetrics) -> self.toRMC), (fun (value : Microsoft.FSharp.Core.string) (self : RIMFAXMetrics) -> { self with toRMC = value }))
        static member sclkStart_ = ((fun (self : RIMFAXMetrics) -> self.sclkStart), (fun (value : Microsoft.FSharp.Core.float) (self : RIMFAXMetrics) -> { self with sclkStart = value }))
        static member sclkEnd_ = ((fun (self : RIMFAXMetrics) -> self.sclkEnd), (fun (value : Microsoft.FSharp.Core.float) (self : RIMFAXMetrics) -> { self with sclkEnd = value }))
        static member RIMFAXSurfaceProperties_ = ((fun (self : RIMFAXMetrics) -> self.RIMFAXSurfaceProperties), (fun (value : Microsoft.FSharp.Core.option<RIMFAXSurfaceMetrics>) (self : RIMFAXMetrics) -> { self with RIMFAXSurfaceProperties = value }))
        static member length_ = ((fun (self : RIMFAXMetrics) -> self.length), (fun (value : Microsoft.FSharp.Core.float) (self : RIMFAXMetrics) -> { self with length = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveWaypointMetrics(value : WaypointMetrics) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _RMC_ = FSharp.Data.Adaptive.cval(value.RMC)
    let _site_ = FSharp.Data.Adaptive.cval(value.site)
    let _yaw_ = FSharp.Data.Adaptive.cval(value.yaw)
    let _pitch_ = FSharp.Data.Adaptive.cval(value.pitch)
    let _roll_ = FSharp.Data.Adaptive.cval(value.roll)
    let _tilt_ = FSharp.Data.Adaptive.cval(value.tilt)
    let _note_ = FSharp.Data.Adaptive.cval(value.note)
    let _distanceM_ = FSharp.Data.Adaptive.cval(value.distanceM)
    let _totalDistanceM_ = FSharp.Data.Adaptive.cval(value.totalDistanceM)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : WaypointMetrics) = AdaptiveWaypointMetrics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : WaypointMetrics) -> AdaptiveWaypointMetrics(value)) (fun (adaptive : AdaptiveWaypointMetrics) (value : WaypointMetrics) -> adaptive.Update(value))
    member __.Update(value : WaypointMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<WaypointMetrics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _RMC_.Value <- value.RMC
            _site_.Value <- value.site
            _yaw_.Value <- value.yaw
            _pitch_.Value <- value.pitch
            _roll_.Value <- value.roll
            _tilt_.Value <- value.tilt
            _note_.Value <- value.note
            _distanceM_.Value <- value.distanceM
            _totalDistanceM_.Value <- value.totalDistanceM
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.RMC = _RMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.site = _site_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.yaw = _yaw_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.pitch = _pitch_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.roll = _roll_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.tilt = _tilt_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.note = _note_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.distanceM = _distanceM_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.totalDistanceM = _totalDistanceM_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module WaypointMetricsLenses = 
    type WaypointMetrics with
        static member version_ = ((fun (self : WaypointMetrics) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : WaypointMetrics) -> { self with version = value }))
        static member RMC_ = ((fun (self : WaypointMetrics) -> self.RMC), (fun (value : Microsoft.FSharp.Core.string) (self : WaypointMetrics) -> { self with RMC = value }))
        static member site_ = ((fun (self : WaypointMetrics) -> self.site), (fun (value : Microsoft.FSharp.Core.int) (self : WaypointMetrics) -> { self with site = value }))
        static member yaw_ = ((fun (self : WaypointMetrics) -> self.yaw), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with yaw = value }))
        static member pitch_ = ((fun (self : WaypointMetrics) -> self.pitch), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with pitch = value }))
        static member roll_ = ((fun (self : WaypointMetrics) -> self.roll), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with roll = value }))
        static member tilt_ = ((fun (self : WaypointMetrics) -> self.tilt), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with tilt = value }))
        static member note_ = ((fun (self : WaypointMetrics) -> self.note), (fun (value : Microsoft.FSharp.Core.string) (self : WaypointMetrics) -> { self with note = value }))
        static member distanceM_ = ((fun (self : WaypointMetrics) -> self.distanceM), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with distanceM = value }))
        static member totalDistanceM_ = ((fun (self : WaypointMetrics) -> self.totalDistanceM), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with totalDistanceM = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSol(value : Sol) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _location_ = FSharp.Data.Adaptive.cval(value.location)
    let _solNumber_ = FSharp.Data.Adaptive.cval(value.solNumber)
    let _solMetrics_ = FSharp.Data.Adaptive.cval(value.solMetrics)
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
            _solMetrics_.Value <- value.solMetrics
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.location = _location_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>>
    member __.solNumber = _solNumber_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.solMetrics = _solMetrics_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<SolMetrics>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SolLenses = 
    type Sol with
        static member version_ = ((fun (self : Sol) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Sol) -> { self with version = value }))
        static member location_ = ((fun (self : Sol) -> self.location), (fun (value : Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>) (self : Sol) -> { self with location = value }))
        static member solNumber_ = ((fun (self : Sol) -> self.solNumber), (fun (value : Microsoft.FSharp.Core.int) (self : Sol) -> { self with solNumber = value }))
        static member solMetrics_ = ((fun (self : Sol) -> self.solMetrics), (fun (value : Microsoft.FSharp.Core.option<SolMetrics>) (self : Sol) -> { self with solMetrics = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveTraverse(value : Traverse) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _sols_ = FSharp.Data.Adaptive.cval(value.sols)
    let _selectedSol_ = FSharp.Data.Adaptive.cval(value.selectedSol)
    let _showLines_ = FSharp.Data.Adaptive.cval(value.showLines)
    let _showText_ = FSharp.Data.Adaptive.cval(value.showText)
    let _showRIMFAXSurfaces_ = FSharp.Data.Adaptive.cval(value.showRIMFAXSurfaces)
    let _tTextSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.tTextSize)
    let _tLineWidth_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.tLineWidth)
    let _showDots_ = FSharp.Data.Adaptive.cval(value.showDots)
    let _isVisibleT_ = FSharp.Data.Adaptive.cval(value.isVisibleT)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let _heightOffset_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.heightOffset)
    let _priority_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.priority)
    let _priorityEnabled_ = FSharp.Data.Adaptive.cval(value.priorityEnabled)
    let _RIMFAXRootDirectory_ = FSharp.Data.Adaptive.cval(value.RIMFAXRootDirectory)
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
            _showRIMFAXSurfaces_.Value <- value.showRIMFAXSurfaces
            _tTextSize_.Update(value.tTextSize)
            _tLineWidth_.Update(value.tLineWidth)
            _showDots_.Value <- value.showDots
            _isVisibleT_.Value <- value.isVisibleT
            _color_.Update(value.color)
            _heightOffset_.Update(value.heightOffset)
            _priority_.Update(value.priority)
            _priorityEnabled_.Value <- value.priorityEnabled
            _RIMFAXRootDirectory_.Value <- value.RIMFAXRootDirectory
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = __value.guid
    member __.tName = __value.tName
    member __.sols = _sols_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<Sol>>
    member __.traverseType = __value.traverseType
    member __.selectedSol = _selectedSol_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>>
    member __.showLines = _showLines_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showText = _showText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showRIMFAXSurfaces = _showRIMFAXSurfaces_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.tTextSize = _tTextSize_
    member __.tLineWidth = _tLineWidth_
    member __.showDots = _showDots_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isVisibleT = _isVisibleT_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.color = _color_
    member __.heightOffset = _heightOffset_
    member __.priority = _priority_
    member __.priorityEnabled = _priorityEnabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.RIMFAXRootDirectory = _RIMFAXRootDirectory_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
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
        static member showRIMFAXSurfaces_ = ((fun (self : Traverse) -> self.showRIMFAXSurfaces), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showRIMFAXSurfaces = value }))
        static member tTextSize_ = ((fun (self : Traverse) -> self.tTextSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with tTextSize = value }))
        static member tLineWidth_ = ((fun (self : Traverse) -> self.tLineWidth), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with tLineWidth = value }))
        static member showDots_ = ((fun (self : Traverse) -> self.showDots), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showDots = value }))
        static member isVisibleT_ = ((fun (self : Traverse) -> self.isVisibleT), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with isVisibleT = value }))
        static member color_ = ((fun (self : Traverse) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : Traverse) -> { self with color = value }))
        static member heightOffset_ = ((fun (self : Traverse) -> self.heightOffset), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with heightOffset = value }))
        static member priority_ = ((fun (self : Traverse) -> self.priority), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with priority = value }))
        static member priorityEnabled_ = ((fun (self : Traverse) -> self.priorityEnabled), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with priorityEnabled = value }))
        static member RIMFAXRootDirectory_ = ((fun (self : Traverse) -> self.RIMFAXRootDirectory), (fun (value : Microsoft.FSharp.Core.string) (self : Traverse) -> { self with RIMFAXRootDirectory = value }))
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
    let _selectedRIMFAXSurface_ = FSharp.Data.Adaptive.cval(value.selectedRIMFAXSurface)
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
            _selectedRIMFAXSurface_.Value <- value.selectedRIMFAXSurface
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.roverTraverses = _roverTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.strategicAnnotationTraverses = _strategicAnnotationTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.RIMFAXTraverses = _RIMFAXTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.plannedTargetsTraverses = _plannedTargetsTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.waypointsTraverses = _waypointsTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.selectedTraverse = _selectedTraverse_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
    member __.selectedRIMFAXSurface = _selectedRIMFAXSurface_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
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
        static member selectedRIMFAXSurface_ = ((fun (self : TraverseModel) -> self.selectedRIMFAXSurface), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : TraverseModel) -> { self with selectedRIMFAXSurface = value }))

