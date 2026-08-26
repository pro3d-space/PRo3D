//45d2776f-40f4-6075-fb90-1f7cd01103b1
//5c650142-1a65-2db3-8cd0-0b1f67e10ce4
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
type AdaptiveRimfaxSurfaceMetrics(value : RimfaxSurfaceMetrics) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _rimfaxImageModeOptions_ = FSharp.Data.Adaptive.cval(value.rimfaxImageModeOptions)
    let _rimfaxImageMode_ = FSharp.Data.Adaptive.cval(value.rimfaxImageMode)
    let _rimfaxSurfaces_ =
        let inline __arg2 (m : PRo3D.Core.Surface.AdaptiveSgSurface) (v : PRo3D.Core.Surface.SgSurface) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.rimfaxSurfaces, (fun (v : PRo3D.Core.Surface.SgSurface) -> PRo3D.Core.Surface.AdaptiveSgSurface(v)), __arg2, (fun (m : PRo3D.Core.Surface.AdaptiveSgSurface) -> m))
    let _isVisibleS_ = FSharp.Data.Adaptive.cval(value.isVisibleS)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : RimfaxSurfaceMetrics) = AdaptiveRimfaxSurfaceMetrics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : RimfaxSurfaceMetrics) -> AdaptiveRimfaxSurfaceMetrics(value)) (fun (adaptive : AdaptiveRimfaxSurfaceMetrics) (value : RimfaxSurfaceMetrics) -> adaptive.Update(value))
    member __.Update(value : RimfaxSurfaceMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RimfaxSurfaceMetrics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _rimfaxImageModeOptions_.Value <- value.rimfaxImageModeOptions
            _rimfaxImageMode_.Value <- value.rimfaxImageMode
            _rimfaxSurfaces_.Update(value.rimfaxSurfaces)
            _isVisibleS_.Value <- value.isVisibleS
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.rimfaxImageModeOptions = _rimfaxImageModeOptions_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<Microsoft.FSharp.Core.string>>
    member __.rimfaxImageMode = _rimfaxImageMode_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.rimfaxSurfaces = _rimfaxSurfaces_ :> FSharp.Data.Adaptive.amap<System.Guid, PRo3D.Core.Surface.AdaptiveSgSurface>
    member __.isVisibleS = _isVisibleS_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RimfaxSurfaceMetricsLenses = 
    type RimfaxSurfaceMetrics with
        static member version_ = ((fun (self : RimfaxSurfaceMetrics) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : RimfaxSurfaceMetrics) -> { self with version = value }))
        static member rimfaxImageModeOptions_ = ((fun (self : RimfaxSurfaceMetrics) -> self.rimfaxImageModeOptions), (fun (value : Microsoft.FSharp.Collections.List<Microsoft.FSharp.Core.string>) (self : RimfaxSurfaceMetrics) -> { self with rimfaxImageModeOptions = value }))
        static member rimfaxImageMode_ = ((fun (self : RimfaxSurfaceMetrics) -> self.rimfaxImageMode), (fun (value : Microsoft.FSharp.Core.string) (self : RimfaxSurfaceMetrics) -> { self with rimfaxImageMode = value }))
        static member rimfaxSurfaces_ = ((fun (self : RimfaxSurfaceMetrics) -> self.rimfaxSurfaces), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, PRo3D.Core.Surface.SgSurface>) (self : RimfaxSurfaceMetrics) -> { self with rimfaxSurfaces = value }))
        static member isVisibleS_ = ((fun (self : RimfaxSurfaceMetrics) -> self.isVisibleS), (fun (value : Microsoft.FSharp.Core.bool) (self : RimfaxSurfaceMetrics) -> { self with isVisibleS = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRimfaxMetrics(value : RimfaxMetrics) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _fromRMC_ = FSharp.Data.Adaptive.cval(value.fromRMC)
    let _toRMC_ = FSharp.Data.Adaptive.cval(value.toRMC)
    let _sclkStart_ = FSharp.Data.Adaptive.cval(value.sclkStart)
    let _sclkEnd_ = FSharp.Data.Adaptive.cval(value.sclkEnd)
    let _rimfaxSurfaceProperties_ =
        let inline __arg2 (o : System.Object) (v : RimfaxSurfaceMetrics) =
            (unbox<AdaptiveRimfaxSurfaceMetrics> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : RimfaxSurfaceMetrics) =
            (unbox<AdaptiveRimfaxSurfaceMetrics> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Core.RimfaxSurfaceMetrics, PRo3D.Core.AdaptiveRimfaxSurfaceMetrics, PRo3D.Core.AdaptiveRimfaxSurfaceMetrics>(value.rimfaxSurfaceProperties, (fun (v : RimfaxSurfaceMetrics) -> AdaptiveRimfaxSurfaceMetrics(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<AdaptiveRimfaxSurfaceMetrics> o), (fun (v : RimfaxSurfaceMetrics) -> AdaptiveRimfaxSurfaceMetrics(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveRimfaxSurfaceMetrics> o))
    let _length_ = FSharp.Data.Adaptive.cval(value.length)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : RimfaxMetrics) = AdaptiveRimfaxMetrics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : RimfaxMetrics) -> AdaptiveRimfaxMetrics(value)) (fun (adaptive : AdaptiveRimfaxMetrics) (value : RimfaxMetrics) -> adaptive.Update(value))
    member __.Update(value : RimfaxMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RimfaxMetrics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _fromRMC_.Value <- value.fromRMC
            _toRMC_.Value <- value.toRMC
            _sclkStart_.Value <- value.sclkStart
            _sclkEnd_.Value <- value.sclkEnd
            _rimfaxSurfaceProperties_.Update(value.rimfaxSurfaceProperties)
            _length_.Value <- value.length
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.fromRMC = _fromRMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.toRMC = _toRMC_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.sclkStart = _sclkStart_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.sclkEnd = _sclkEnd_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.rimfaxSurfaceProperties = _rimfaxSurfaceProperties_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<RimfaxSurfaceMetrics, AdaptiveRimfaxSurfaceMetrics, AdaptiveRimfaxSurfaceMetrics>>
    member __.length = _length_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RimfaxMetricsLenses = 
    type RimfaxMetrics with
        static member version_ = ((fun (self : RimfaxMetrics) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : RimfaxMetrics) -> { self with version = value }))
        static member fromRMC_ = ((fun (self : RimfaxMetrics) -> self.fromRMC), (fun (value : Microsoft.FSharp.Core.string) (self : RimfaxMetrics) -> { self with fromRMC = value }))
        static member toRMC_ = ((fun (self : RimfaxMetrics) -> self.toRMC), (fun (value : Microsoft.FSharp.Core.string) (self : RimfaxMetrics) -> { self with toRMC = value }))
        static member sclkStart_ = ((fun (self : RimfaxMetrics) -> self.sclkStart), (fun (value : Microsoft.FSharp.Core.float) (self : RimfaxMetrics) -> { self with sclkStart = value }))
        static member sclkEnd_ = ((fun (self : RimfaxMetrics) -> self.sclkEnd), (fun (value : Microsoft.FSharp.Core.float) (self : RimfaxMetrics) -> { self with sclkEnd = value }))
        static member rimfaxSurfaceProperties_ = ((fun (self : RimfaxMetrics) -> self.rimfaxSurfaceProperties), (fun (value : Microsoft.FSharp.Core.option<RimfaxSurfaceMetrics>) (self : RimfaxMetrics) -> { self with rimfaxSurfaceProperties = value }))
        static member length_ = ((fun (self : RimfaxMetrics) -> self.length), (fun (value : Microsoft.FSharp.Core.float) (self : RimfaxMetrics) -> { self with length = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveWaypointMetrics(value : WaypointMetrics) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _rmc_ = FSharp.Data.Adaptive.cval(value.rmc)
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
            _rmc_.Value <- value.rmc
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
    member __.rmc = _rmc_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
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
        static member rmc_ = ((fun (self : WaypointMetrics) -> self.rmc), (fun (value : Microsoft.FSharp.Core.string) (self : WaypointMetrics) -> { self with rmc = value }))
        static member site_ = ((fun (self : WaypointMetrics) -> self.site), (fun (value : Microsoft.FSharp.Core.int) (self : WaypointMetrics) -> { self with site = value }))
        static member yaw_ = ((fun (self : WaypointMetrics) -> self.yaw), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with yaw = value }))
        static member pitch_ = ((fun (self : WaypointMetrics) -> self.pitch), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with pitch = value }))
        static member roll_ = ((fun (self : WaypointMetrics) -> self.roll), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with roll = value }))
        static member tilt_ = ((fun (self : WaypointMetrics) -> self.tilt), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with tilt = value }))
        static member note_ = ((fun (self : WaypointMetrics) -> self.note), (fun (value : Microsoft.FSharp.Core.string) (self : WaypointMetrics) -> { self with note = value }))
        static member distanceM_ = ((fun (self : WaypointMetrics) -> self.distanceM), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with distanceM = value }))
        static member totalDistanceM_ = ((fun (self : WaypointMetrics) -> self.totalDistanceM), (fun (value : Microsoft.FSharp.Core.float) (self : WaypointMetrics) -> { self with totalDistanceM = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSolMetricsCase =
    abstract member Update : SolMetrics -> AdaptiveSolMetricsCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type private AdaptiveSolMetricsRoverM(Item : RoverMetrics) =
    let _Item_ = AdaptiveRoverMetrics(Item)
    let mutable __Item = Item
    member __.Update(Item : RoverMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RoverMetrics>.ShallowEquals(Item, __Item))) then
            __Item <- Item
            _Item_.Update(Item)
    member __.Item = _Item_
    interface AdaptiveSolMetricsCase with
        member x.Update(value : SolMetrics) =
            match value with
            | SolMetrics.RoverM(Item) ->
                x.Update(Item)
                x :> AdaptiveSolMetricsCase
            | SolMetrics.RimfaxM(Item) -> AdaptiveSolMetricsRimfaxM(Item) :> AdaptiveSolMetricsCase
            | SolMetrics.WaypointM(Item) -> AdaptiveSolMetricsWaypointM(Item) :> AdaptiveSolMetricsCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type private AdaptiveSolMetricsRimfaxM(Item : RimfaxMetrics) =
    let _Item_ = AdaptiveRimfaxMetrics(Item)
    let mutable __Item = Item
    member __.Update(Item : RimfaxMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RimfaxMetrics>.ShallowEquals(Item, __Item))) then
            __Item <- Item
            _Item_.Update(Item)
    member __.Item = _Item_
    interface AdaptiveSolMetricsCase with
        member x.Update(value : SolMetrics) =
            match value with
            | SolMetrics.RoverM(Item) -> AdaptiveSolMetricsRoverM(Item) :> AdaptiveSolMetricsCase
            | SolMetrics.RimfaxM(Item) ->
                x.Update(Item)
                x :> AdaptiveSolMetricsCase
            | SolMetrics.WaypointM(Item) -> AdaptiveSolMetricsWaypointM(Item) :> AdaptiveSolMetricsCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type private AdaptiveSolMetricsWaypointM(Item : WaypointMetrics) =
    let _Item_ = AdaptiveWaypointMetrics(Item)
    let mutable __Item = Item
    member __.Update(Item : WaypointMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<WaypointMetrics>.ShallowEquals(Item, __Item))) then
            __Item <- Item
            _Item_.Update(Item)
    member __.Item = _Item_
    interface AdaptiveSolMetricsCase with
        member x.Update(value : SolMetrics) =
            match value with
            | SolMetrics.RoverM(Item) -> AdaptiveSolMetricsRoverM(Item) :> AdaptiveSolMetricsCase
            | SolMetrics.RimfaxM(Item) -> AdaptiveSolMetricsRimfaxM(Item) :> AdaptiveSolMetricsCase
            | SolMetrics.WaypointM(Item) ->
                x.Update(Item)
                x :> AdaptiveSolMetricsCase
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSolMetrics(value : SolMetrics) =
    inherit Adaptify.AdaptiveValue<AdaptiveSolMetricsCase>()
    let mutable __value = value
    let mutable __current =
        match value with
        | SolMetrics.RoverM(Item) -> AdaptiveSolMetricsRoverM(Item) :> AdaptiveSolMetricsCase
        | SolMetrics.RimfaxM(Item) -> AdaptiveSolMetricsRimfaxM(Item) :> AdaptiveSolMetricsCase
        | SolMetrics.WaypointM(Item) -> AdaptiveSolMetricsWaypointM(Item) :> AdaptiveSolMetricsCase
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (t : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member CreateAdaptiveCase(value : SolMetrics) =
        match value with
        | SolMetrics.RoverM(Item) -> AdaptiveSolMetricsRoverM(Item) :> AdaptiveSolMetricsCase
        | SolMetrics.RimfaxM(Item) -> AdaptiveSolMetricsRimfaxM(Item) :> AdaptiveSolMetricsCase
        | SolMetrics.WaypointM(Item) -> AdaptiveSolMetricsWaypointM(Item) :> AdaptiveSolMetricsCase
    static member Create(value : SolMetrics) = AdaptiveSolMetrics(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SolMetrics) -> AdaptiveSolMetrics(value)) (fun (adaptive : AdaptiveSolMetrics) (value : SolMetrics) -> adaptive.Update(value))
    member __.Current = __adaptive
    member __.Update(value : SolMetrics) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SolMetrics>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            let __n = __current.Update(value)
            if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AdaptiveSolMetricsCase>.ShallowEquals(__n, __current))) then
                __current <- __n
                __.MarkOutdated()
    override __.Compute(t : FSharp.Data.Adaptive.AdaptiveToken) = __current
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AdaptiveSolMetrics = 
    let (|AdaptiveRoverM|AdaptiveRimfaxM|AdaptiveWaypointM|) (value : AdaptiveSolMetricsCase) =
        match value with
        | (:? AdaptiveSolMetricsRoverM as roverm) -> AdaptiveRoverM(roverm.Item)
        | (:? AdaptiveSolMetricsRimfaxM as rimfaxm) -> AdaptiveRimfaxM(rimfaxm.Item)
        | (:? AdaptiveSolMetricsWaypointM as waypointm) -> AdaptiveWaypointM(waypointm.Item)
        | _ -> failwith "unreachable"
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSol(value : Sol) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _location_ = FSharp.Data.Adaptive.cval(value.location)
    let _solNumber_ = FSharp.Data.Adaptive.cval(value.solNumber)
    let _solMetrics_ =
        let inline __arg5 (o : System.Object) (v : SolMetrics) =
            (unbox<AdaptiveSolMetrics> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Core.SolMetrics, PRo3D.Core.AdaptiveSolMetricsCase, FSharp.Data.Adaptive.aval<PRo3D.Core.AdaptiveSolMetricsCase>>(value.solMetrics, (fun (v : SolMetrics) -> AdaptiveSolMetrics.CreateAdaptiveCase(v) :> System.Object), (fun (o : System.Object) (v : SolMetrics) -> (unbox<AdaptiveSolMetricsCase> o).Update(v) :> System.Object), (fun (o : System.Object) -> unbox<AdaptiveSolMetricsCase> o), (fun (v : SolMetrics) -> AdaptiveSolMetrics(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<AdaptiveSolMetrics> o :> FSharp.Data.Adaptive.aval<AdaptiveSolMetricsCase>))
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
            _solMetrics_.Update(value.solMetrics)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.location = _location_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Aardvark.Base.V3d>>
    member __.solNumber = _solNumber_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.solMetrics = _solMetrics_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<SolMetrics, AdaptiveSolMetricsCase, FSharp.Data.Adaptive.aval<AdaptiveSolMetricsCase>>>
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
    let _fastText_ = FSharp.Data.Adaptive.cval(value.fastText)
    let _showRimfaxSurfaces_ = FSharp.Data.Adaptive.cval(value.showRimfaxSurfaces)
    let _tTextSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.tTextSize)
    let _tLineWidth_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.tLineWidth)
    let _showDots_ = FSharp.Data.Adaptive.cval(value.showDots)
    let _isVisibleT_ = FSharp.Data.Adaptive.cval(value.isVisibleT)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let _heightOffset_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.heightOffset)
    let _priority_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.priority)
    let _priorityEnabled_ = FSharp.Data.Adaptive.cval(value.priorityEnabled)
    let _rimfaxRootDirectory_ = FSharp.Data.Adaptive.cval(value.rimfaxRootDirectory)
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
            _fastText_.Value <- value.fastText
            _showRimfaxSurfaces_.Value <- value.showRimfaxSurfaces
            _tTextSize_.Update(value.tTextSize)
            _tLineWidth_.Update(value.tLineWidth)
            _showDots_.Value <- value.showDots
            _isVisibleT_.Value <- value.isVisibleT
            _color_.Update(value.color)
            _heightOffset_.Update(value.heightOffset)
            _priority_.Update(value.priority)
            _priorityEnabled_.Value <- value.priorityEnabled
            _rimfaxRootDirectory_.Value <- value.rimfaxRootDirectory
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = __value.guid
    member __.tName = __value.tName
    member __.sols = _sols_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<Sol>>
    member __.traverseType = __value.traverseType
    member __.selectedSol = _selectedSol_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>>
    member __.showLines = _showLines_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showText = _showText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.fastText = _fastText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showRimfaxSurfaces = _showRimfaxSurfaces_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.tTextSize = _tTextSize_
    member __.tLineWidth = _tLineWidth_
    member __.showDots = _showDots_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isVisibleT = _isVisibleT_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.color = _color_
    member __.heightOffset = _heightOffset_
    member __.priority = _priority_
    member __.priorityEnabled = _priorityEnabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.rimfaxRootDirectory = _rimfaxRootDirectory_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
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
        static member fastText_ = ((fun (self : Traverse) -> self.fastText), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with fastText = value }))
        static member showRimfaxSurfaces_ = ((fun (self : Traverse) -> self.showRimfaxSurfaces), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showRimfaxSurfaces = value }))
        static member tTextSize_ = ((fun (self : Traverse) -> self.tTextSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with tTextSize = value }))
        static member tLineWidth_ = ((fun (self : Traverse) -> self.tLineWidth), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with tLineWidth = value }))
        static member showDots_ = ((fun (self : Traverse) -> self.showDots), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showDots = value }))
        static member isVisibleT_ = ((fun (self : Traverse) -> self.isVisibleT), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with isVisibleT = value }))
        static member color_ = ((fun (self : Traverse) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : Traverse) -> { self with color = value }))
        static member heightOffset_ = ((fun (self : Traverse) -> self.heightOffset), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with heightOffset = value }))
        static member priority_ = ((fun (self : Traverse) -> self.priority), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with priority = value }))
        static member priorityEnabled_ = ((fun (self : Traverse) -> self.priorityEnabled), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with priorityEnabled = value }))
        static member rimfaxRootDirectory_ = ((fun (self : Traverse) -> self.rimfaxRootDirectory), (fun (value : Microsoft.FSharp.Core.string) (self : Traverse) -> { self with rimfaxRootDirectory = value }))
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
    let _rimfaxTraverses_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.rimfaxTraverses, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
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
    let _selectedRimfaxSurface_ = FSharp.Data.Adaptive.cval(value.selectedRimfaxSurface)
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
            _rimfaxTraverses_.Update(value.rimfaxTraverses)
            _plannedTargetsTraverses_.Update(value.plannedTargetsTraverses)
            _waypointsTraverses_.Update(value.waypointsTraverses)
            _selectedTraverse_.Value <- value.selectedTraverse
            _selectedRimfaxSurface_.Value <- value.selectedRimfaxSurface
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.roverTraverses = _roverTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.strategicAnnotationTraverses = _strategicAnnotationTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.rimfaxTraverses = _rimfaxTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.plannedTargetsTraverses = _plannedTargetsTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.waypointsTraverses = _waypointsTraverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.selectedTraverse = _selectedTraverse_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
    member __.selectedRimfaxSurface = _selectedRimfaxSurface_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TraverseModelLenses = 
    type TraverseModel with
        static member version_ = ((fun (self : TraverseModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : TraverseModel) -> { self with version = value }))
        static member roverTraverses_ = ((fun (self : TraverseModel) -> self.roverTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with roverTraverses = value }))
        static member strategicAnnotationTraverses_ = ((fun (self : TraverseModel) -> self.strategicAnnotationTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with strategicAnnotationTraverses = value }))
        static member rimfaxTraverses_ = ((fun (self : TraverseModel) -> self.rimfaxTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with rimfaxTraverses = value }))
        static member plannedTargetsTraverses_ = ((fun (self : TraverseModel) -> self.plannedTargetsTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with plannedTargetsTraverses = value }))
        static member waypointsTraverses_ = ((fun (self : TraverseModel) -> self.waypointsTraverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with waypointsTraverses = value }))
        static member selectedTraverse_ = ((fun (self : TraverseModel) -> self.selectedTraverse), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : TraverseModel) -> { self with selectedTraverse = value }))
        static member selectedRimfaxSurface_ = ((fun (self : TraverseModel) -> self.selectedRimfaxSurface), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : TraverseModel) -> { self with selectedRimfaxSurface = value }))

