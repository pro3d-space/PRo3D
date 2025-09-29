//a821716c-2c13-f830-b68a-ec072b5fb477
//c5a9871e-7e87-b83f-0a9c-1d34cb0cfa89
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec Pro3d.Core

open System
open FSharp.Data.Adaptive
open Adaptify
open Pro3d.Core
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveRoverModel(value : RoverModel) =
    let _path_ = FSharp.Data.Adaptive.cval(value.path)
    let _roverTraverse_ = FSharp.Data.Adaptive.cval(value.roverTraverse)
    let _refSystem_ = PRo3D.Core.AdaptiveReferenceSystem(value.refSystem)
    let _trafo_ = FSharp.Data.Adaptive.cval(value.trafo)
    let _forwardVector_ = FSharp.Data.Adaptive.cval(value.forwardVector)
    let _upVector_ = FSharp.Data.Adaptive.cval(value.upVector)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : RoverModel) = AdaptiveRoverModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : RoverModel) -> AdaptiveRoverModel(value)) (fun (adaptive : AdaptiveRoverModel) (value : RoverModel) -> adaptive.Update(value))
    member __.Update(value : RoverModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<RoverModel>.ShallowEquals(value, __value))) then
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
module RoverModelLenses = 
    type RoverModel with
        static member path_ = ((fun (self : RoverModel) -> self.path), (fun (value : Microsoft.FSharp.Core.string) (self : RoverModel) -> { self with path = value }))
        static member roverTraverse_ = ((fun (self : RoverModel) -> self.roverTraverse), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : RoverModel) -> { self with roverTraverse = value }))
        static member refSystem_ = ((fun (self : RoverModel) -> self.refSystem), (fun (value : PRo3D.Core.ReferenceSystem) (self : RoverModel) -> { self with refSystem = value }))
        static member trafo_ = ((fun (self : RoverModel) -> self.trafo), (fun (value : Aardvark.Base.Trafo3d) (self : RoverModel) -> { self with trafo = value }))
        static member forwardVector_ = ((fun (self : RoverModel) -> self.forwardVector), (fun (value : Aardvark.Base.V3d) (self : RoverModel) -> { self with forwardVector = value }))
        static member upVector_ = ((fun (self : RoverModel) -> self.upVector), (fun (value : Aardvark.Base.V3d) (self : RoverModel) -> { self with upVector = value }))

