//709f4c2f-935d-e8f4-1335-8ac01c418d48
//3afe8c84-5e74-4045-1f98-656bf40ae708
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Lite

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Lite
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSurface(value : Surface) =
    let _opcs_ = FSharp.Data.Adaptive.cmap(value.opcs)
    let _trafo_ = FSharp.Data.Adaptive.cval(value.trafo)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Surface) = AdaptiveSurface(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Surface) -> AdaptiveSurface(value)) (fun (adaptive : AdaptiveSurface) (value : Surface) -> adaptive.Update(value))
    member __.Update(value : Surface) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Surface>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _opcs_.Value <- value.opcs
            _trafo_.Value <- value.trafo
    member __.Current = __adaptive
    member __.opcs = _opcs_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, Opc>
    member __.trafo = _trafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SurfaceLenses = 
    type Surface with
        static member opcs_ = ((fun (self : Surface) -> self.opcs), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Opc>) (self : Surface) -> { self with opcs = value }))
        static member trafo_ = ((fun (self : Surface) -> self.trafo), (fun (value : Aardvark.Base.Trafo3d) (self : Surface) -> { self with trafo = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveState(value : State) =
    let _surfaces_ =
        let inline __arg2 (m : AdaptiveSurface) (v : Surface) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.surfaces, (fun (v : Surface) -> AdaptiveSurface(v)), __arg2, (fun (m : AdaptiveSurface) -> m))
    let _planet_ = FSharp.Data.Adaptive.cval(value.planet)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : State) = AdaptiveState(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : State) -> AdaptiveState(value)) (fun (adaptive : AdaptiveState) (value : State) -> adaptive.Update(value))
    member __.Update(value : State) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<State>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _surfaces_.Update(value.surfaces)
            _planet_.Value <- value.planet
    member __.Current = __adaptive
    member __.surfaces = _surfaces_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, AdaptiveSurface>
    member __.planet = _planet_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Planet>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module StateLenses = 
    type State with
        static member surfaces_ = ((fun (self : State) -> self.surfaces), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Surface>) (self : State) -> { self with surfaces = value }))
        static member planet_ = ((fun (self : State) -> self.planet), (fun (value : PRo3D.Base.Planet) (self : State) -> { self with planet = value }))

