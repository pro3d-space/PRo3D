//4ecced35-ec41-e50c-2438-6ec981b816c2
//f9ed43ba-8f35-9076-9996-f992f2403b49
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
type AdaptiveSnapshotSettings(value : SnapshotSettings) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _numSnapshots_ = Aardvark.UI.AdaptiveNumericInput(value.numSnapshots)
    let _fieldOfView_ = Aardvark.UI.AdaptiveNumericInput(value.fieldOfView)
    let _renderMask_ = FSharp.Data.Adaptive.cval(value.renderMask)
    let _useObjectPlacements_ = FSharp.Data.Adaptive.cval(value.useObjectPlacements)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SnapshotSettings) = AdaptiveSnapshotSettings(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SnapshotSettings) -> AdaptiveSnapshotSettings(value)) (fun (adaptive : AdaptiveSnapshotSettings) (value : SnapshotSettings) -> adaptive.Update(value))
    member __.Update(value : SnapshotSettings) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SnapshotSettings>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _numSnapshots_.Update(value.numSnapshots)
            _fieldOfView_.Update(value.fieldOfView)
            _renderMask_.Value <- value.renderMask
            _useObjectPlacements_.Value <- value.useObjectPlacements
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.numSnapshots = _numSnapshots_
    member __.fieldOfView = _fieldOfView_
    member __.renderMask = _renderMask_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.bool>>
    member __.useObjectPlacements = _useObjectPlacements_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SnapshotSettingsLenses = 
    type SnapshotSettings with
        static member version_ = ((fun (self : SnapshotSettings) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : SnapshotSettings) -> { self with version = value }))
        static member numSnapshots_ = ((fun (self : SnapshotSettings) -> self.numSnapshots), (fun (value : Aardvark.UI.NumericInput) (self : SnapshotSettings) -> { self with numSnapshots = value }))
        static member fieldOfView_ = ((fun (self : SnapshotSettings) -> self.fieldOfView), (fun (value : Aardvark.UI.NumericInput) (self : SnapshotSettings) -> { self with fieldOfView = value }))
        static member renderMask_ = ((fun (self : SnapshotSettings) -> self.renderMask), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.bool>) (self : SnapshotSettings) -> { self with renderMask = value }))
        static member useObjectPlacements_ = ((fun (self : SnapshotSettings) -> self.useObjectPlacements), (fun (value : Microsoft.FSharp.Core.bool) (self : SnapshotSettings) -> { self with useObjectPlacements = value }))

