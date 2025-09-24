//6ced26c2-0c4c-8369-022d-7845f96f6a13
//82d8464f-2ef8-6438-c496-17d7f8522390
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
    let _translationTrafo_ = FSharp.Data.Adaptive.cval(value.translationTrafo)
    let _rotationTrafo_ = FSharp.Data.Adaptive.cval(value.rotationTrafo)
    let _roverDirection_ = FSharp.Data.Adaptive.cval(value.roverDirection)
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
            _translationTrafo_.Value <- value.translationTrafo
            _rotationTrafo_.Value <- value.rotationTrafo
            _roverDirection_.Value <- value.roverDirection
    member __.Current = __adaptive
    member __.path = _path_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.roverTraverse = _roverTraverse_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
    member __.refSystem = _refSystem_
    member __.translationTrafo = _translationTrafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.rotationTrafo = _rotationTrafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.roverDirection = _roverDirection_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RoverModelLenses = 
    type RoverModel with
        static member path_ = ((fun (self : RoverModel) -> self.path), (fun (value : Microsoft.FSharp.Core.string) (self : RoverModel) -> { self with path = value }))
        static member roverTraverse_ = ((fun (self : RoverModel) -> self.roverTraverse), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : RoverModel) -> { self with roverTraverse = value }))
        static member refSystem_ = ((fun (self : RoverModel) -> self.refSystem), (fun (value : PRo3D.Core.ReferenceSystem) (self : RoverModel) -> { self with refSystem = value }))
        static member translationTrafo_ = ((fun (self : RoverModel) -> self.translationTrafo), (fun (value : Aardvark.Base.Trafo3d) (self : RoverModel) -> { self with translationTrafo = value }))
        static member rotationTrafo_ = ((fun (self : RoverModel) -> self.rotationTrafo), (fun (value : Aardvark.Base.Trafo3d) (self : RoverModel) -> { self with rotationTrafo = value }))
        static member roverDirection_ = ((fun (self : RoverModel) -> self.roverDirection), (fun (value : Aardvark.Base.V3d) (self : RoverModel) -> { self with roverDirection = value }))

