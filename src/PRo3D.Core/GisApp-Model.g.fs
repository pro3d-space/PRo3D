//a13a6c39-518a-06b3-6d1b-c3740f668152
//f59aeaac-abe3-5c03-c779-165143182d77
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
type AdaptiveMissionTimeEntry(value : MissionTimeEntry) =
    let _value_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.value)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : MissionTimeEntry) = AdaptiveMissionTimeEntry(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : MissionTimeEntry) -> AdaptiveMissionTimeEntry(value)) (fun (adaptive : AdaptiveMissionTimeEntry) (value : MissionTimeEntry) -> adaptive.Update(value))
    member __.Update(value : MissionTimeEntry) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<MissionTimeEntry>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _value_.Update(value.value)
    member __.Current = __adaptive
    member __.minDate = __value.minDate
    member __.maxDate = __value.maxDate
    member __.name = __value.name
    member __.value = _value_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module MissionTimeEntryLenses = 
    type MissionTimeEntry with
        static member minDate_ = ((fun (self : MissionTimeEntry) -> self.minDate), (fun (value : System.DateTime) (self : MissionTimeEntry) -> { self with minDate = value }))
        static member maxDate_ = ((fun (self : MissionTimeEntry) -> self.maxDate), (fun (value : System.DateTime) (self : MissionTimeEntry) -> { self with maxDate = value }))
        static member name_ = ((fun (self : MissionTimeEntry) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : MissionTimeEntry) -> { self with name = value }))
        static member value_ = ((fun (self : MissionTimeEntry) -> self.value), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : MissionTimeEntry) -> { self with value = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveProjectedImages(value : ProjectedImages) =
    let _images_ = FSharp.Data.Adaptive.clist(value.images)
    let _selectedImage_ = FSharp.Data.Adaptive.cval(value.selectedImage)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ProjectedImages) = AdaptiveProjectedImages(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ProjectedImages) -> AdaptiveProjectedImages(value)) (fun (adaptive : AdaptiveProjectedImages) (value : ProjectedImages) -> adaptive.Update(value))
    member __.Update(value : ProjectedImages) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ProjectedImages>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _images_.Value <- value.images
            _selectedImage_.Value <- value.selectedImage
    member __.Current = __adaptive
    member __.images = _images_ :> FSharp.Data.Adaptive.alist<ProjectedImage>
    member __.selectedImage = _selectedImage_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.Index>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ProjectedImagesLenses = 
    type ProjectedImages with
        static member images_ = ((fun (self : ProjectedImages) -> self.images), (fun (value : FSharp.Data.Adaptive.IndexList<ProjectedImage>) (self : ProjectedImages) -> { self with images = value }))
        static member selectedImage_ = ((fun (self : ProjectedImages) -> self.selectedImage), (fun (value : Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.Index>) (self : ProjectedImages) -> { self with selectedImage = value }))
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
    let _projectedImages_ = AdaptiveProjectedImages(value.projectedImages)
    let _showMarkers_ = FSharp.Data.Adaptive.cval(value.showMarkers)
    let _selectedMissionTimeRow_ = FSharp.Data.Adaptive.cval(value.selectedMissionTimeRow)
    let _missionTimesEntries_ =
        let inline __arg1 (v : FSharp.Data.Adaptive.IndexList<MissionTimeEntry>) =
            let inline __arg2 (m : AdaptiveMissionTimeEntry) (v : MissionTimeEntry) =
                m.Update(v)
                m
            FSharp.Data.Traceable.ChangeableModelList(v, (fun (v : MissionTimeEntry) -> AdaptiveMissionTimeEntry(v)), __arg2, (fun (m : AdaptiveMissionTimeEntry) -> m)) :> System.Object
        let inline __arg2 (o : System.Object) (v : FSharp.Data.Adaptive.IndexList<MissionTimeEntry>) =
            (unbox<FSharp.Data.Traceable.ChangeableModelList<MissionTimeEntry, AdaptiveMissionTimeEntry, AdaptiveMissionTimeEntry>> o).Update(v)
            o
        let inline __arg4 (v : FSharp.Data.Adaptive.IndexList<MissionTimeEntry>) =
            let inline __arg2 (m : AdaptiveMissionTimeEntry) (v : MissionTimeEntry) =
                m.Update(v)
                m
            FSharp.Data.Traceable.ChangeableModelList(v, (fun (v : MissionTimeEntry) -> AdaptiveMissionTimeEntry(v)), __arg2, (fun (m : AdaptiveMissionTimeEntry) -> m)) :> System.Object
        let inline __arg5 (o : System.Object) (v : FSharp.Data.Adaptive.IndexList<MissionTimeEntry>) =
            (unbox<FSharp.Data.Traceable.ChangeableModelList<MissionTimeEntry, AdaptiveMissionTimeEntry, AdaptiveMissionTimeEntry>> o).Update(v)
            o
        Adaptify.FSharp.Core.AdaptiveOption<FSharp.Data.Adaptive.IndexList<PRo3D.Core.Gis.MissionTimeEntry>, FSharp.Data.Adaptive.alist<PRo3D.Core.Gis.AdaptiveMissionTimeEntry>, FSharp.Data.Adaptive.alist<PRo3D.Core.Gis.AdaptiveMissionTimeEntry>>(value.missionTimesEntries, __arg1, __arg2, (fun (o : System.Object) -> unbox<FSharp.Data.Traceable.ChangeableModelList<MissionTimeEntry, AdaptiveMissionTimeEntry, AdaptiveMissionTimeEntry>> o :> FSharp.Data.Adaptive.alist<AdaptiveMissionTimeEntry>), __arg4, __arg5, (fun (o : System.Object) -> unbox<FSharp.Data.Traceable.ChangeableModelList<MissionTimeEntry, AdaptiveMissionTimeEntry, AdaptiveMissionTimeEntry>> o :> FSharp.Data.Adaptive.alist<AdaptiveMissionTimeEntry>))
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
            _projectedImages_.Update(value.projectedImages)
            _showMarkers_.Value <- value.showMarkers
            _selectedMissionTimeRow_.Value <- value.selectedMissionTimeRow
            _missionTimesEntries_.Update(value.missionTimesEntries)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.defaultObservationInfo = _defaultObservationInfo_
    member __.entities = _entities_ :> FSharp.Data.Adaptive.amap<PRo3D.Base.Gis.EntitySpiceName, PRo3D.Base.Gis.AdaptiveEntity>
    member __.newEntity = _newEntity_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<PRo3D.Base.Gis.Entity, PRo3D.Base.Gis.AdaptiveEntity, PRo3D.Base.Gis.AdaptiveEntity>>
    member __.newFrame = _newFrame_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<PRo3D.Base.Gis.ReferenceFrame, PRo3D.Base.Gis.AdaptiveReferenceFrame, PRo3D.Base.Gis.AdaptiveReferenceFrame>>
    member __.referenceFrames = _referenceFrames_ :> FSharp.Data.Adaptive.amap<PRo3D.Base.Gis.FrameSpiceName, PRo3D.Base.Gis.AdaptiveReferenceFrame>
    member __.gisSurfaces = _gisSurfaces_ :> FSharp.Data.Adaptive.amap<PRo3D.Core.Surface.SurfaceId, GisSurface>
    member __.spiceKernel = _spiceKernel_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<PRo3D.Base.CooTransformation.SPICEKernel>>
    member __.spiceKernelLoadSuccess = _spiceKernelLoadSuccess_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.cameraInObserver = _cameraInObserver_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.projectedImages = _projectedImages_
    member __.showMarkers = _showMarkers_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.selectedMissionTimeRow = _selectedMissionTimeRow_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.Index>>
    member __.missionTimesEntries = _missionTimesEntries_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<FSharp.Data.Adaptive.IndexList<MissionTimeEntry>, FSharp.Data.Adaptive.alist<AdaptiveMissionTimeEntry>, FSharp.Data.Adaptive.alist<AdaptiveMissionTimeEntry>>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module GisAppLenses = 
    type GisApp with
        static member version_ = ((fun (self : GisApp) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : GisApp) -> { self with version = value }))
        static member defaultObservationInfo_ = ((fun (self : GisApp) -> self.defaultObservationInfo), (fun (value : ObservationInfo) (self : GisApp) -> { self with defaultObservationInfo = value }))
        static member entities_ = ((fun (self : GisApp) -> self.entities), (fun (value : FSharp.Data.Adaptive.HashMap<PRo3D.Base.Gis.EntitySpiceName, PRo3D.Base.Gis.Entity>) (self : GisApp) -> { self with entities = value }))
        static member newEntity_ = ((fun (self : GisApp) -> self.newEntity), (fun (value : Microsoft.FSharp.Core.Option<PRo3D.Base.Gis.Entity>) (self : GisApp) -> { self with newEntity = value }))
        static member newFrame_ = ((fun (self : GisApp) -> self.newFrame), (fun (value : Microsoft.FSharp.Core.Option<PRo3D.Base.Gis.ReferenceFrame>) (self : GisApp) -> { self with newFrame = value }))
        static member referenceFrames_ = ((fun (self : GisApp) -> self.referenceFrames), (fun (value : FSharp.Data.Adaptive.HashMap<PRo3D.Base.Gis.FrameSpiceName, PRo3D.Base.Gis.ReferenceFrame>) (self : GisApp) -> { self with referenceFrames = value }))
        static member gisSurfaces_ = ((fun (self : GisApp) -> self.gisSurfaces), (fun (value : FSharp.Data.Adaptive.HashMap<PRo3D.Core.Surface.SurfaceId, GisSurface>) (self : GisApp) -> { self with gisSurfaces = value }))
        static member spiceKernel_ = ((fun (self : GisApp) -> self.spiceKernel), (fun (value : Microsoft.FSharp.Core.Option<PRo3D.Base.CooTransformation.SPICEKernel>) (self : GisApp) -> { self with spiceKernel = value }))
        static member spiceKernelLoadSuccess_ = ((fun (self : GisApp) -> self.spiceKernelLoadSuccess), (fun (value : Microsoft.FSharp.Core.bool) (self : GisApp) -> { self with spiceKernelLoadSuccess = value }))
        static member cameraInObserver_ = ((fun (self : GisApp) -> self.cameraInObserver), (fun (value : Microsoft.FSharp.Core.bool) (self : GisApp) -> { self with cameraInObserver = value }))
        static member projectedImages_ = ((fun (self : GisApp) -> self.projectedImages), (fun (value : ProjectedImages) (self : GisApp) -> { self with projectedImages = value }))
        static member showMarkers_ = ((fun (self : GisApp) -> self.showMarkers), (fun (value : Microsoft.FSharp.Core.bool) (self : GisApp) -> { self with showMarkers = value }))
        static member selectedMissionTimeRow_ = ((fun (self : GisApp) -> self.selectedMissionTimeRow), (fun (value : Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.Index>) (self : GisApp) -> { self with selectedMissionTimeRow = value }))
        static member missionTimesEntries_ = ((fun (self : GisApp) -> self.missionTimesEntries), (fun (value : Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.IndexList<MissionTimeEntry>>) (self : GisApp) -> { self with missionTimesEntries = value }))

