//c3f9fa59-b2b1-a1eb-dc1d-13ffac8de925
//88cb8d05-5460-c36e-efc2-4f9a2bd3ca70
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
type AdaptiveRover3DModel(value : Rover3DModel) =
    let _path_ = FSharp.Data.Adaptive.cval(value.path)
    let _roverTraverse_ = FSharp.Data.Adaptive.cval(value.roverTraverse)
    let _refSystem_ = AdaptiveReferenceSystem(value.refSystem)
    let _trafo_ = FSharp.Data.Adaptive.cval(value.trafo)
    let _forwardVector_ = FSharp.Data.Adaptive.cval(value.forwardVector)
    let _upVector_ = FSharp.Data.Adaptive.cval(value.upVector)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Rover3DModel) = AdaptiveRover3DModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Rover3DModel) -> AdaptiveRover3DModel(value)) (fun (adaptive : AdaptiveRover3DModel) (value : Rover3DModel) -> adaptive.Update(value))
    member __.Update(value : Rover3DModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Rover3DModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _path_.Value <- value.path
            _roverTraverse_.Value <- value.roverTraverse
            _refSystem_.Update(value.refSystem)
            _trafo_.Value <- value.trafo
            _forwardVector_.Value <- value.forwardVector
            _upVector_.Value <- value.upVector
    member __.Current = __adaptive
    member __.path = _path_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.roverTraverse = _roverTraverse_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
    member __.refSystem = _refSystem_
    member __.trafo = _trafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.forwardVector = _forwardVector_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.upVector = _upVector_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module Rover3DModelLenses = 
    type Rover3DModel with
        static member path_ = ((fun (self : Rover3DModel) -> self.path), (fun (value : Microsoft.FSharp.Core.string) (self : Rover3DModel) -> { self with path = value }))
        static member roverTraverse_ = ((fun (self : Rover3DModel) -> self.roverTraverse), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : Rover3DModel) -> { self with roverTraverse = value }))
        static member refSystem_ = ((fun (self : Rover3DModel) -> self.refSystem), (fun (value : ReferenceSystem) (self : Rover3DModel) -> { self with refSystem = value }))
        static member trafo_ = ((fun (self : Rover3DModel) -> self.trafo), (fun (value : Aardvark.Base.Trafo3d) (self : Rover3DModel) -> { self with trafo = value }))
        static member forwardVector_ = ((fun (self : Rover3DModel) -> self.forwardVector), (fun (value : Aardvark.Base.V3d) (self : Rover3DModel) -> { self with forwardVector = value }))
        static member upVector_ = ((fun (self : Rover3DModel) -> self.upVector), (fun (value : Aardvark.Base.V3d) (self : Rover3DModel) -> { self with upVector = value }))

