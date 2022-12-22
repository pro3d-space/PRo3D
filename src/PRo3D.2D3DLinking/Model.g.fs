//93fbd891-117a-a73c-7586-39c98ce970c0
//85ed4e70-22e4-d775-e619-62f1a52c2126
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec LinkingTestApp

open System
open FSharp.Data.Adaptive
open Adaptify
open LinkingTestApp
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveModel(value : Model) =
    let _cameraState_ = Aardvark.UI.Primitives.AdaptiveCameraControllerState(value.cameraState)
    let _mainFrustum_ = FSharp.Data.Adaptive.cval(value.mainFrustum)
    let _overlayFrustum_ = FSharp.Data.Adaptive.cval(value.overlayFrustum)
    let _fillMode_ = FSharp.Data.Adaptive.cval(value.fillMode)
    let _boxes_ = FSharp.Data.Adaptive.cval(value.boxes)
    let _opcInfos_ =
        let inline __arg2 (m : OpcViewer.Base.Picking.AdaptiveOpcData) (v : OpcViewer.Base.Picking.OpcData) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.opcInfos, (fun (v : OpcViewer.Base.Picking.OpcData) -> OpcViewer.Base.Picking.AdaptiveOpcData(v)), __arg2, (fun (m : OpcViewer.Base.Picking.AdaptiveOpcData) -> m))
    let _threads_ = FSharp.Data.Adaptive.cval(value.threads)
    let _dockConfig_ = FSharp.Data.Adaptive.cval(value.dockConfig)
    let _pickingModel_ = OpcViewer.Base.Picking.AdaptivePickingModel(value.pickingModel)
    let _pickedPoint_ = FSharp.Data.Adaptive.cval(value.pickedPoint)
    let _planePoints_ =
        let inline __arg2 (o : System.Object) (v : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) =
            (unbox<FSharp.Data.Adaptive.clist<Aardvark.Base.V3d>> o).Value <- v
            o
        let inline __arg5 (o : System.Object) (v : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) =
            (unbox<FSharp.Data.Adaptive.clist<Aardvark.Base.V3d>> o).Value <- v
            o
        Adaptify.FSharp.Core.AdaptiveOption<FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>, FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>, FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>>(value.planePoints, (fun (v : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) -> FSharp.Data.Adaptive.clist(v) :> System.Object), __arg2, (fun (o : System.Object) -> unbox<FSharp.Data.Adaptive.clist<Aardvark.Base.V3d>> o :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>), (fun (v : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>) -> FSharp.Data.Adaptive.clist(v) :> System.Object), __arg5, (fun (o : System.Object) -> unbox<FSharp.Data.Adaptive.clist<Aardvark.Base.V3d>> o :> FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>))
    let _pickingActive_ = FSharp.Data.Adaptive.cval(value.pickingActive)
    let _linkingModel_ = PRo3D.Linking.AdaptiveLinkingModel(value.linkingModel)
    let _minervaModel_ = PRo3D.Minerva.AdaptiveMinervaModel(value.minervaModel)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Model) = AdaptiveModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Model) -> AdaptiveModel(value)) (fun (adaptive : AdaptiveModel) (value : Model) -> adaptive.Update(value))
    member __.Update(value : Model) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Model>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _cameraState_.Update(value.cameraState)
            _mainFrustum_.Value <- value.mainFrustum
            _overlayFrustum_.Value <- value.overlayFrustum
            _fillMode_.Value <- value.fillMode
            _boxes_.Value <- value.boxes
            _opcInfos_.Update(value.opcInfos)
            _threads_.Value <- value.threads
            _dockConfig_.Value <- value.dockConfig
            _pickingModel_.Update(value.pickingModel)
            _pickedPoint_.Value <- value.pickedPoint
            _planePoints_.Update(value.planePoints)
            _pickingActive_.Value <- value.pickingActive
            _linkingModel_.Update(value.linkingModel)
            _minervaModel_.Update(value.minervaModel)
    member __.Current = __adaptive
    member __.cameraState = _cameraState_
    member __.mainFrustum = _mainFrustum_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.Frustum>
    member __.overlayFrustum = _overlayFrustum_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Rendering.Frustum>>
    member __.fillMode = _fillMode_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.FillMode>
    member __.patchHierarchies = __value.patchHierarchies
    member __.boxes = _boxes_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Aardvark.Base.Box3d>>
    member __.opcInfos = _opcInfos_ :> FSharp.Data.Adaptive.amap<Aardvark.Base.Box3d, OpcViewer.Base.Picking.AdaptiveOpcData>
    member __.threads = _threads_ :> FSharp.Data.Adaptive.aval<FSharp.Data.Adaptive.ThreadPool<Action>>
    member __.dockConfig = _dockConfig_ :> FSharp.Data.Adaptive.aval<Aardvark.UI.Primitives.DockConfig>
    member __.pickingModel = _pickingModel_
    member __.pickedPoint = _pickedPoint_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Base.V3d>>
    member __.planePoints = _planePoints_ :> FSharp.Data.Adaptive.aval<Adaptify.FSharp.Core.AdaptiveOptionCase<FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>, FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>, FSharp.Data.Adaptive.alist<Aardvark.Base.V3d>>>
    member __.pickingActive = _pickingActive_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.linkingModel = _linkingModel_
    member __.minervaModel = _minervaModel_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ModelLenses = 
    type Model with
        static member cameraState_ = ((fun (self : Model) -> self.cameraState), (fun (value : Aardvark.UI.Primitives.CameraControllerState) (self : Model) -> { self with cameraState = value }))
        static member mainFrustum_ = ((fun (self : Model) -> self.mainFrustum), (fun (value : Aardvark.Rendering.Frustum) (self : Model) -> { self with mainFrustum = value }))
        static member overlayFrustum_ = ((fun (self : Model) -> self.overlayFrustum), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Rendering.Frustum>) (self : Model) -> { self with overlayFrustum = value }))
        static member fillMode_ = ((fun (self : Model) -> self.fillMode), (fun (value : Aardvark.Rendering.FillMode) (self : Model) -> { self with fillMode = value }))
        static member patchHierarchies_ = ((fun (self : Model) -> self.patchHierarchies), (fun (value : Microsoft.FSharp.Collections.list<Aardvark.SceneGraph.Opc.PatchHierarchy>) (self : Model) -> { self with patchHierarchies = value }))
        static member boxes_ = ((fun (self : Model) -> self.boxes), (fun (value : Microsoft.FSharp.Collections.list<Aardvark.Base.Box3d>) (self : Model) -> { self with boxes = value }))
        static member opcInfos_ = ((fun (self : Model) -> self.opcInfos), (fun (value : FSharp.Data.Adaptive.HashMap<Aardvark.Base.Box3d, OpcViewer.Base.Picking.OpcData>) (self : Model) -> { self with opcInfos = value }))
        static member threads_ = ((fun (self : Model) -> self.threads), (fun (value : FSharp.Data.Adaptive.ThreadPool<Action>) (self : Model) -> { self with threads = value }))
        static member dockConfig_ = ((fun (self : Model) -> self.dockConfig), (fun (value : Aardvark.UI.Primitives.DockConfig) (self : Model) -> { self with dockConfig = value }))
        static member pickingModel_ = ((fun (self : Model) -> self.pickingModel), (fun (value : OpcViewer.Base.Picking.PickingModel) (self : Model) -> { self with pickingModel = value }))
        static member pickedPoint_ = ((fun (self : Model) -> self.pickedPoint), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.V3d>) (self : Model) -> { self with pickedPoint = value }))
        static member planePoints_ = ((fun (self : Model) -> self.planePoints), (fun (value : Microsoft.FSharp.Core.Option<FSharp.Data.Adaptive.IndexList<Aardvark.Base.V3d>>) (self : Model) -> { self with planePoints = value }))
        static member pickingActive_ = ((fun (self : Model) -> self.pickingActive), (fun (value : Microsoft.FSharp.Core.bool) (self : Model) -> { self with pickingActive = value }))
        static member linkingModel_ = ((fun (self : Model) -> self.linkingModel), (fun (value : PRo3D.Linking.LinkingModel) (self : Model) -> { self with linkingModel = value }))
        static member minervaModel_ = ((fun (self : Model) -> self.minervaModel), (fun (value : PRo3D.Minerva.MinervaModel) (self : Model) -> { self with minervaModel = value }))

