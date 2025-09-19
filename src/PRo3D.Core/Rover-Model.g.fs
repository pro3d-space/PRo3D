//1e211ef0-bdde-e088-b796-eb855920b16b
//ba8834fb-c0fb-0974-4702-7447cdb66b06
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
    let _roverLocation_ = FSharp.Data.Adaptive.cval(value.roverLocation)
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
            _roverLocation_.Value <- value.roverLocation
            _roverDirection_.Value <- value.roverDirection
    member __.Current = __adaptive
    member __.path = _path_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.roverTraverse = _roverTraverse_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
    member __.roverLocation = _roverLocation_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.roverDirection = _roverDirection_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module RoverModelLenses = 
    type RoverModel with
        static member path_ = ((fun (self : RoverModel) -> self.path), (fun (value : Microsoft.FSharp.Core.string) (self : RoverModel) -> { self with path = value }))
        static member roverTraverse_ = ((fun (self : RoverModel) -> self.roverTraverse), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : RoverModel) -> { self with roverTraverse = value }))
        static member roverLocation_ = ((fun (self : RoverModel) -> self.roverLocation), (fun (value : Aardvark.Base.V3d) (self : RoverModel) -> { self with roverLocation = value }))
        static member roverDirection_ = ((fun (self : RoverModel) -> self.roverDirection), (fun (value : Aardvark.Base.V3d) (self : RoverModel) -> { self with roverDirection = value }))

