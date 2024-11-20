//bdf92b9f-17b8-902a-8852-0b1247a964a8
//2cd279cd-1d6f-a673-b71e-edd330c1a64e
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
type AdaptiveSnapshotApp<'model, '_primmodel, '_amodel, 'aModel, '_primaModel, '_aaModel, 'msg, '_primmsg, '_amsg>(value : SnapshotApp<'model, 'aModel, 'msg>, _primmodelinit : 'model -> System.Object, _primmodelupdate : System.Object -> 'model -> System.Object, _primmodelview : System.Object -> '_primmodel, _modelinit : 'model -> System.Object, _modelupdate : System.Object -> 'model -> System.Object, _modelview : System.Object -> '_amodel, _primaModelinit : 'aModel -> System.Object, _primaModelupdate : System.Object -> 'aModel -> System.Object, _primaModelview : System.Object -> '_primaModel, _aModelinit : 'aModel -> System.Object, _aModelupdate : System.Object -> 'aModel -> System.Object, _aModelview : System.Object -> '_aaModel, _primmsginit : 'msg -> System.Object, _primmsgupdate : System.Object -> 'msg -> System.Object, _primmsgview : System.Object -> '_primmsg, _msginit : 'msg -> System.Object, _msgupdate : System.Object -> 'msg -> System.Object, _msgview : System.Object -> '_amsg) =
    let _mutableApp_ = FSharp.Data.Adaptive.cval(value.mutableApp)
    let _adaptiveModel_ = _aModelinit value.adaptiveModel
    let _sg_ = FSharp.Data.Adaptive.cval(value.sg)
    let _snapshotAnimation_ = FSharp.Data.Adaptive.cval(value.snapshotAnimation)
    let _getAnimationActions_ = FSharp.Data.Adaptive.cval(value.getAnimationActions)
    let _getSnapshotActions_ = FSharp.Data.Adaptive.cval(value.getSnapshotActions)
    let _runtime_ = FSharp.Data.Adaptive.cval(value.runtime)
    let _renderRange_ = FSharp.Data.Adaptive.cval(value.renderRange)
    let _outputFolder_ = FSharp.Data.Adaptive.cval(value.outputFolder)
    let _renderMask_ = FSharp.Data.Adaptive.cval(value.renderMask)
    let _renderDepth_ = FSharp.Data.Adaptive.cval(value.renderDepth)
    let _verbose_ = FSharp.Data.Adaptive.cval(value.verbose)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SnapshotApp<'model, 'aModel, 'msg>, _primmodelinit : 'model -> System.Object, _primmodelupdate : System.Object -> 'model -> System.Object, _primmodelview : System.Object -> '_primmodel, _modelinit : 'model -> System.Object, _modelupdate : System.Object -> 'model -> System.Object, _modelview : System.Object -> '_amodel, _primaModelinit : 'aModel -> System.Object, _primaModelupdate : System.Object -> 'aModel -> System.Object, _primaModelview : System.Object -> '_primaModel, _aModelinit : 'aModel -> System.Object, _aModelupdate : System.Object -> 'aModel -> System.Object, _aModelview : System.Object -> '_aaModel, _primmsginit : 'msg -> System.Object, _primmsgupdate : System.Object -> 'msg -> System.Object, _primmsgview : System.Object -> '_primmsg, _msginit : 'msg -> System.Object, _msgupdate : System.Object -> 'msg -> System.Object, _msgview : System.Object -> '_amsg) = AdaptiveSnapshotApp<'model, '_primmodel, '_amodel, 'aModel, '_primaModel, '_aaModel, 'msg, '_primmsg, '_amsg>(value, _primmodelinit, _primmodelupdate, _primmodelview, _modelinit, _modelupdate, _modelview, _primaModelinit, _primaModelupdate, _primaModelview, _aModelinit, _aModelupdate, _aModelview, _primmsginit, _primmsgupdate, _primmsgview, _msginit, _msgupdate, _msgview)
    member __.Update(value : SnapshotApp<'model, 'aModel, 'msg>) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SnapshotApp<'model, 'aModel, 'msg>>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _mutableApp_.Value <- value.mutableApp
            ignore (_aModelupdate _adaptiveModel_ value.adaptiveModel)
            _sg_.Value <- value.sg
            _snapshotAnimation_.Value <- value.snapshotAnimation
            _getAnimationActions_.Value <- value.getAnimationActions
            _getSnapshotActions_.Value <- value.getSnapshotActions
            _runtime_.Value <- value.runtime
            _renderRange_.Value <- value.renderRange
            _outputFolder_.Value <- value.outputFolder
            _renderMask_.Value <- value.renderMask
            _renderDepth_.Value <- value.renderDepth
            _verbose_.Value <- value.verbose
    member __.Current = __adaptive
    member __.mutableApp = _mutableApp_ :> FSharp.Data.Adaptive.aval<Aardvark.UI.MutableApp<'model, 'msg>>
    member __.adaptiveModel = _aModelview _adaptiveModel_
    member __.sg = _sg_ :> FSharp.Data.Adaptive.aval<Aardvark.SceneGraph.ISg>
    member __.snapshotAnimation = _snapshotAnimation_ :> FSharp.Data.Adaptive.aval<SnapshotAnimation>
    member __.getAnimationActions = _getAnimationActions_ :> FSharp.Data.Adaptive.aval<SnapshotAnimation -> Microsoft.FSharp.Collections.seq<'msg>>
    member __.getSnapshotActions = _getSnapshotActions_ :> FSharp.Data.Adaptive.aval<Snapshot -> NearFarRecalculation -> Microsoft.FSharp.Core.string -> Microsoft.FSharp.Collections.seq<'msg>>
    member __.runtime = _runtime_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.IRuntime>
    member __.renderRange = _renderRange_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<RenderRange>>
    member __.outputFolder = _outputFolder_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.renderMask = _renderMask_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.renderDepth = _renderDepth_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.verbose = _verbose_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SnapshotAppLenses = 
    type SnapshotApp<'model, 'aModel, 'msg> with
        static member mutableApp_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.mutableApp), (fun (value : Aardvark.UI.MutableApp<'model, 'msg>) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with mutableApp = value }))
        static member adaptiveModel_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.adaptiveModel), (fun (value : 'aModel) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with adaptiveModel = value }))
        static member sg_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.sg), (fun (value : Aardvark.SceneGraph.ISg) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with sg = value }))
        static member snapshotAnimation_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.snapshotAnimation), (fun (value : SnapshotAnimation) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with snapshotAnimation = value }))
        static member getAnimationActions_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.getAnimationActions), (fun (value : SnapshotAnimation -> Microsoft.FSharp.Collections.seq<'msg>) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with getAnimationActions = value }))
        static member getSnapshotActions_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.getSnapshotActions), (fun (value : Snapshot -> NearFarRecalculation -> Microsoft.FSharp.Core.string -> Microsoft.FSharp.Collections.seq<'msg>) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with getSnapshotActions = value }))
        static member runtime_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.runtime), (fun (value : Aardvark.Rendering.IRuntime) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with runtime = value }))
        static member renderRange_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.renderRange), (fun (value : Microsoft.FSharp.Core.option<RenderRange>) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with renderRange = value }))
        static member outputFolder_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.outputFolder), (fun (value : Microsoft.FSharp.Core.string) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with outputFolder = value }))
        static member renderMask_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.renderMask), (fun (value : Microsoft.FSharp.Core.bool) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with renderMask = value }))
        static member renderDepth_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.renderDepth), (fun (value : Microsoft.FSharp.Core.bool) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with renderDepth = value }))
        static member verbose_ = ((fun (self : SnapshotApp<'model, 'aModel, 'msg>) -> self.verbose), (fun (value : Microsoft.FSharp.Core.bool) (self : SnapshotApp<'model, 'aModel, 'msg>) -> { self with verbose = value }))

