//f22b473e-4f0a-ba1c-6f4f-14dfbd9b73d3
//8d869436-a92c-4ddb-e48f-1a1985793f31
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.MapProjection

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.MapProjection
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveMapProjectionModel(value : MapProjectionModel) =
    let _kind_ = FSharp.Data.Adaptive.cval(value.kind)
    let _center_ = FSharp.Data.Adaptive.cval(value.center)
    let _zoom_ = FSharp.Data.Adaptive.cval(value.zoom)
    let _viewport_ = FSharp.Data.Adaptive.cval(value.viewport)
    let _dragFrom_ = FSharp.Data.Adaptive.cval(value.dragFrom)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : MapProjectionModel) = AdaptiveMapProjectionModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : MapProjectionModel) -> AdaptiveMapProjectionModel(value)) (fun (adaptive : AdaptiveMapProjectionModel) (value : MapProjectionModel) -> adaptive.Update(value))
    member __.Update(value : MapProjectionModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<MapProjectionModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _kind_.Value <- value.kind
            _center_.Value <- value.center
            _zoom_.Value <- value.zoom
            _viewport_.Value <- value.viewport
            _dragFrom_.Value <- value.dragFrom
    member __.Current = __adaptive
    member __.kind = _kind_ :> FSharp.Data.Adaptive.aval<MapProjectionKind>
    member __.center = _center_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V2d>
    member __.zoom = _zoom_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.viewport = _viewport_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V2i>
    member __.dragFrom = _dragFrom_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Base.V2d>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module MapProjectionModelLenses = 
    type MapProjectionModel with
        static member kind_ = ((fun (self : MapProjectionModel) -> self.kind), (fun (value : MapProjectionKind) (self : MapProjectionModel) -> { self with kind = value }))
        static member center_ = ((fun (self : MapProjectionModel) -> self.center), (fun (value : Aardvark.Base.V2d) (self : MapProjectionModel) -> { self with center = value }))
        static member zoom_ = ((fun (self : MapProjectionModel) -> self.zoom), (fun (value : Microsoft.FSharp.Core.float) (self : MapProjectionModel) -> { self with zoom = value }))
        static member viewport_ = ((fun (self : MapProjectionModel) -> self.viewport), (fun (value : Aardvark.Base.V2i) (self : MapProjectionModel) -> { self with viewport = value }))
        static member dragFrom_ = ((fun (self : MapProjectionModel) -> self.dragFrom), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.V2d>) (self : MapProjectionModel) -> { self with dragFrom = value }))

