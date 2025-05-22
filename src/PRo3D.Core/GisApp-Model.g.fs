//aa12ae5e-5fa3-0a42-74b9-ec4392ae7ad1
//690b63cb-8351-abec-b049-09c7eeb3d072
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Core.Gis

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Core.Gis
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveObservationInfo(value : ObservationInfo) =
    let mutable _valuesIfComplete_ = FSharp.Data.Adaptive.cval(value.valuesIfComplete)
    let _target_ = FSharp.Data.Adaptive.cval(value.target)
    let _observer_ = FSharp.Data.Adaptive.cval(value.observer)
    let _time_ = PRo3D.Base.AdaptiveCalendar(value.time)
    let _referenceFrame_ = FSharp.Data.Adaptive.cval(value.referenceFrame)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ObservationInfo) = AdaptiveObservationInfo(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ObservationInfo) -> AdaptiveObservationInfo(value)) (fun (adaptive : AdaptiveObservationInfo) (value : ObservationInfo) -> adaptive.Update(value))
    member __.Update(value : ObservationInfo) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ObservationInfo>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _valuesIfComplete_.Value <- value.valuesIfComplete
            _target_.Value <- value.target
            _observer_.Value <- value.observer
            _time_.Update(value.time)
            _referenceFrame_.Value <- value.referenceFrame
    member __.Current = __adaptive
    member __.valuesIfComplete = _valuesIfComplete_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<(PRo3D.Base.Gis.EntitySpiceName * PRo3D.Base.Gis.EntitySpiceName * PRo3D.Base.Gis.FrameSpiceName)>>
    member __.target = _target_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<PRo3D.Base.Gis.EntitySpiceName>>
    member __.observer = _observer_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<PRo3D.Base.Gis.EntitySpiceName>>
    member __.time = _time_
    member __.referenceFrame = _referenceFrame_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<PRo3D.Base.Gis.FrameSpiceName>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ObservationInfoLenses = 
    type ObservationInfo with
        static member target_ = ((fun (self : ObservationInfo) -> self.target), (fun (value : Microsoft.FSharp.Core.option<PRo3D.Base.Gis.EntitySpiceName>) (self : ObservationInfo) -> { self with target = value }))
        static member observer_ = ((fun (self : ObservationInfo) -> self.observer), (fun (value : Microsoft.FSharp.Core.option<PRo3D.Base.Gis.EntitySpiceName>) (self : ObservationInfo) -> { self with observer = value }))
        static member time_ = ((fun (self : ObservationInfo) -> self.time), (fun (value : PRo3D.Base.Calendar) (self : ObservationInfo) -> { self with time = value }))
        static member referenceFrame_ = ((fun (self : ObservationInfo) -> self.referenceFrame), (fun (value : Microsoft.FSharp.Core.option<PRo3D.Base.Gis.FrameSpiceName>) (self : ObservationInfo) -> { self with referenceFrame = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveGisApp(value : GisApp) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _defaultObservationInfo_ = AdaptiveObservationInfo(value.defaultObservationInfo)
    let _entities_ =
        let inline __arg2 (m : PRo3D.Base.Gis.AdaptiveEntity) (v : PRo3D.Base.Gis.Entity) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.entities, (fun (v : PRo3D.Base.Gis.Entity) -> PRo3D.Base.Gis.AdaptiveEntity(v)), __arg2, (fun (m : PRo3D.Base.Gis.AdaptiveEntity) -> m))
    let _newEntity_ =
        let inline __arg2 (o : System.Object) (v : PRo3D.Base.Gis.Entity) =
            (unbox<PRo3D.Base.Gis.AdaptiveEntity> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : PRo3D.Base.Gis.Entity) =
            (unbox<PRo3D.Base.Gis.AdaptiveEntity> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Base.Gis.Entity, PRo3D.Base.Gis.AdaptiveEntity, PRo3D.Base.Gis.AdaptiveEntity>(value.newEntity, (fun (v : PRo3D.Base.Gis.Entity) -> PRo3D.Base.Gis.AdaptiveEntity(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<PRo3D.Base.Gis.AdaptiveEntity> o), (fun (v : PRo3D.Base.Gis.Entity) -> PRo3D.Base.Gis.AdaptiveEntity(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<PRo3D.Base.Gis.AdaptiveEntity> o))
    let _newFrame_ =
        let inline __arg2 (o : System.Object) (v : PRo3D.Base.Gis.ReferenceFrame) =
            (unbox<PRo3D.Base.Gis.AdaptiveReferenceFrame> o).Update(v)
            o
        let inline __arg5 (o : System.Object) (v : PRo3D.Base.Gis.ReferenceFrame) =
            (unbox<PRo3D.Base.Gis.AdaptiveReferenceFrame> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<PRo3D.Base.Gis.ReferenceFrame, PRo3D.Base.Gis.AdaptiveReferenceFrame, PRo3D.Base.Gis.AdaptiveReferenceFrame>(value.newFrame, (fun (v : PRo3D.Base.Gis.ReferenceFrame) -> PRo3D.Base.Gis.AdaptiveReferenceFrame(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<PRo3D.Base.Gis.AdaptiveReferenceFrame> o), (fun (v : PRo3D.Base.Gis.ReferenceFrame) -> PRo3D.Base.Gis.AdaptiveReferenceFrame(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<PRo3D.Base.Gis.AdaptiveReferenceFrame> o))
    let _referenceFrames_ =
        let inline __arg2 (m : PRo3D.Base.Gis.AdaptiveReferenceFrame) (v : PRo3D.Base.Gis.ReferenceFrame) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.referenceFrames, (fun (v : PRo3D.Base.Gis.ReferenceFrame) -> PRo3D.Base.Gis.AdaptiveReferenceFrame(v)), __arg2, (fun (m : PRo3D.Base.Gis.AdaptiveReferenceFrame) -> m))
    let _gisSurfaces_ = FSharp.Data.Adaptive.cmap(value.gisSurfaces)
    let _spiceKernel_ = FSharp.Data.Adaptive.cval(value.spiceKernel)
    let _spiceKernelLoadSuccess_ = FSharp.Data.Adaptive.cval(value.spiceKernelLoadSuccess)
    let _cameraInObserver_ = FSharp.Data.Adaptive.cval(value.cameraInObserver)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : GisApp) = AdaptiveGisApp(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : GisApp) -> AdaptiveGisApp(value)) (fun (adaptive : AdaptiveGisApp) (value : GisApp) -> adaptive.Update(value))
    member __.Update(value : GisApp) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<GisApp>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _defaultObservationInfo_.Update(value.defaultObservationInfo)
            _entities_.Update(value.entities)
            _newEntity_.Update(value.newEntity)
            _newFrame_.Update(value.newFrame)
            _referenceFrames_.Update(value.referenceFrames)
            _gisSurfaces_.Value <- value.gisSurfaces
            _spiceKernel_.Value <- value.spiceKernel
            _spiceKernelLoadSuccess_.Value <- value.spiceKernelLoadSuccess
            _cameraInObserver_.Value <- value.cameraInObserver
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.defaultObservationInfo = _defaultObservationInfo_
    member __.entities = _entities_ :> FSharp.Data.Adaptive.amap<PRo3D.Base.Gis.EntitySpiceName, PRo3D.Base.Gis.AdaptiveEntity>
    member __.newEntity = _newEntity_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<PRo3D.Base.Gis.Entity, PRo3D.Base.Gis.AdaptiveEntity, PRo3D.Base.Gis.AdaptiveEntity>>
    member __.newFrame = _newFrame_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<PRo3D.Base.Gis.ReferenceFrame, PRo3D.Base.Gis.AdaptiveReferenceFrame, PRo3D.Base.Gis.AdaptiveReferenceFrame>>
    member __.referenceFrames = _referenceFrames_ :> FSharp.Data.Adaptive.amap<PRo3D.Base.Gis.FrameSpiceName, PRo3D.Base.Gis.AdaptiveReferenceFrame>
    member __.gisSurfaces = _gisSurfaces_ :> FSharp.Data.Adaptive.amap<PRo3D.Core.Surface.SurfaceId, GisSurface>
    member __.spiceKernel = _spiceKernel_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<PRo3D.Base.CooTransformation.SPICEKernel>>
    member __.spiceKernelLoadSuccess = _spiceKernelLoadSuccess_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.cameraInObserver = _cameraInObserver_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module GisAppLenses = 
    type GisApp with
        static member version_ = ((fun (self : GisApp) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : GisApp) -> { self with version = value }))
        static member defaultObservationInfo_ = ((fun (self : GisApp) -> self.defaultObservationInfo), (fun (value : ObservationInfo) (self : GisApp) -> { self with defaultObservationInfo = value }))
        static member entities_ = ((fun (self : GisApp) -> self.entities), (fun (value : FSharp.Data.Adaptive.HashMap<PRo3D.Base.Gis.EntitySpiceName, PRo3D.Base.Gis.Entity>) (self : GisApp) -> { self with entities = value }))
        static member newEntity_ = ((fun (self : GisApp) -> self.newEntity), (fun (value : Microsoft.FSharp.Core.option<PRo3D.Base.Gis.Entity>) (self : GisApp) -> { self with newEntity = value }))
        static member newFrame_ = ((fun (self : GisApp) -> self.newFrame), (fun (value : Microsoft.FSharp.Core.option<PRo3D.Base.Gis.ReferenceFrame>) (self : GisApp) -> { self with newFrame = value }))
        static member referenceFrames_ = ((fun (self : GisApp) -> self.referenceFrames), (fun (value : FSharp.Data.Adaptive.HashMap<PRo3D.Base.Gis.FrameSpiceName, PRo3D.Base.Gis.ReferenceFrame>) (self : GisApp) -> { self with referenceFrames = value }))
        static member gisSurfaces_ = ((fun (self : GisApp) -> self.gisSurfaces), (fun (value : FSharp.Data.Adaptive.HashMap<PRo3D.Core.Surface.SurfaceId, GisSurface>) (self : GisApp) -> { self with gisSurfaces = value }))
        static member spiceKernel_ = ((fun (self : GisApp) -> self.spiceKernel), (fun (value : Microsoft.FSharp.Core.option<PRo3D.Base.CooTransformation.SPICEKernel>) (self : GisApp) -> { self with spiceKernel = value }))
        static member spiceKernelLoadSuccess_ = ((fun (self : GisApp) -> self.spiceKernelLoadSuccess), (fun (value : Microsoft.FSharp.Core.bool) (self : GisApp) -> { self with spiceKernelLoadSuccess = value }))
        static member cameraInObserver_ = ((fun (self : GisApp) -> self.cameraInObserver), (fun (value : Microsoft.FSharp.Core.bool) (self : GisApp) -> { self with cameraInObserver = value }))

