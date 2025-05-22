//442bfe3f-802e-9ced-f327-3e199b517b27
//0d68e79f-1ac7-4d06-e1f4-f208c0412e00
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec Aardvark.UI

open System
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveColorInput(value : ColorInput) =
    let _c_ = FSharp.Data.Adaptive.cval(value.c)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ColorInput) = AdaptiveColorInput(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ColorInput) -> AdaptiveColorInput(value)) (fun (adaptive : AdaptiveColorInput) (value : ColorInput) -> adaptive.Update(value))
    member __.Update(value : ColorInput) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ColorInput>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _c_.Value <- value.c
    member __.Current = __adaptive
    member __.c = _c_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ColorInputLenses = 
    type ColorInput with
        static member c_ = ((fun (self : ColorInput) -> self.c), (fun (value : Aardvark.Base.C4b) (self : ColorInput) -> { self with c = value }))

