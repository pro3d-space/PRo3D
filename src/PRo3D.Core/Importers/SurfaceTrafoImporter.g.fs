//1bdd6296-4a81-c324-945b-705776448366
//80ee6db4-a8a6-2cf1-f1f0-378b2f7d2752
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
type AdaptiveSurfaceTrafoImporterModel(value : SurfaceTrafoImporterModel) =
    let _trafos_ = FSharp.Data.Adaptive.clist(value.trafos)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SurfaceTrafoImporterModel) = AdaptiveSurfaceTrafoImporterModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SurfaceTrafoImporterModel) -> AdaptiveSurfaceTrafoImporterModel(value)) (fun (adaptive : AdaptiveSurfaceTrafoImporterModel) (value : SurfaceTrafoImporterModel) -> adaptive.Update(value))
    member __.Update(value : SurfaceTrafoImporterModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SurfaceTrafoImporterModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _trafos_.Value <- value.trafos
    member __.Current = __adaptive
    member __.trafos = _trafos_ :> FSharp.Data.Adaptive.alist<PRo3D.Core.Surface.SurfaceTrafo>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SurfaceTrafoImporterModelLenses = 
    type SurfaceTrafoImporterModel with
        static member trafos_ = ((fun (self : SurfaceTrafoImporterModel) -> self.trafos), (fun (value : FSharp.Data.Adaptive.IndexList<PRo3D.Core.Surface.SurfaceTrafo>) (self : SurfaceTrafoImporterModel) -> { self with trafos = value }))

