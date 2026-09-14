//547cbef5-1299-8806-026d-c86a82869aab
//7dbe8123-17bb-1c87-be06-237a9e69d935
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec RemoteControlModel

open System
open FSharp.Data.Adaptive
open Adaptify
open RemoteControlModel
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRemoteModel(value : RemoteModel) =
    let _selectedShot_ = FSharp.Data.Adaptive.cval(value.selectedShot)
    let _shots_ = FSharp.Data.Adaptive.clist(value.shots)
    let _platformShots_ = FSharp.Data.Adaptive.clist(value.platformShots)
    let _Rover_ = PRo3D.SimulatedViews.AdaptiveRoverModel(value.Rover)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : RemoteModel) = AdaptiveRemoteModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : RemoteModel) -> AdaptiveRemoteModel(value)) (fun (adaptive : AdaptiveRemoteModel) (value : RemoteModel) -> adaptive.Update(value))
    member __.Update(value : RemoteModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RemoteModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _selectedShot_.Value <- value.selectedShot
            _shots_.Value <- value.shots
            _platformShots_.Value <- value.platformShots
            _Rover_.Update(value.Rover)
    member __.Current = __adaptive
    member __.selectedShot = _selectedShot_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Shot>>
    member __.shots = _shots_ :> FSharp.Data.Adaptive.alist<Shot>
    member __.platformShots = _platformShots_ :> FSharp.Data.Adaptive.alist<PlatformShot>
    member __.Rover = _Rover_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RemoteModelLenses = 
    type RemoteModel with
        static member selectedShot_ = ((fun (self : RemoteModel) -> self.selectedShot), (fun (value : Microsoft.FSharp.Core.Option<Shot>) (self : RemoteModel) -> { self with selectedShot = value }))
        static member shots_ = ((fun (self : RemoteModel) -> self.shots), (fun (value : FSharp.Data.Adaptive.IndexList<Shot>) (self : RemoteModel) -> { self with shots = value }))
        static member platformShots_ = ((fun (self : RemoteModel) -> self.platformShots), (fun (value : FSharp.Data.Adaptive.IndexList<PlatformShot>) (self : RemoteModel) -> { self with platformShots = value }))
        static member Rover_ = ((fun (self : RemoteModel) -> self.Rover), (fun (value : PRo3D.SimulatedViews.RoverModel) (self : RemoteModel) -> { self with Rover = value }))

