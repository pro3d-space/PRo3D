//aa165cd1-89ae-5440-937b-863b2bda7db2
//9c4d8ef0-96c1-49fd-4bd4-8e9fe83270cc
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Viewer

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Viewer
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveLayoutModel(value : LayoutModel) =
    let _golden_ = Aardvark.UI.Primitives.Golden.AdaptiveGoldenLayout(value.golden)
    let _current_ = FSharp.Data.Adaptive.cval(value.current)
    let _activeName_ = FSharp.Data.Adaptive.cval(value.activeName)
    let _activeShape_ = FSharp.Data.Adaptive.cval(value.activeShape)
    let _library_ = FSharp.Data.Adaptive.cval(value.library)
    let _dialog_ = FSharp.Data.Adaptive.cval(value.dialog)
    let _nameInput_ = FSharp.Data.Adaptive.cval(value.nameInput)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : LayoutModel) = AdaptiveLayoutModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : LayoutModel) -> AdaptiveLayoutModel(value)) (fun (adaptive : AdaptiveLayoutModel) (value : LayoutModel) -> adaptive.Update(value))
    member __.Update(value : LayoutModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<LayoutModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _golden_.Update(value.golden)
            _current_.Value <- value.current
            _activeName_.Value <- value.activeName
            _activeShape_.Value <- value.activeShape
            _library_.Value <- value.library
            _dialog_.Value <- value.dialog
            _nameInput_.Value <- value.nameInput
    member __.Current = __adaptive
    member __.golden = _golden_
    member __.current = _current_ :> FSharp.Data.Adaptive.aval<Aardvark.UI.Primitives.Golden.WindowLayout>
    member __.activeName = _activeName_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.activeShape = _activeShape_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.library = _library_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>>
    member __.dialog = _dialog_ :> FSharp.Data.Adaptive.aval<LayoutDialog>
    member __.nameInput = _nameInput_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module LayoutModelLenses = 
    type LayoutModel with
        static member golden_ = ((fun (self : LayoutModel) -> self.golden), (fun (value : Aardvark.UI.Primitives.Golden.GoldenLayout) (self : LayoutModel) -> { self with golden = value }))
        static member current_ = ((fun (self : LayoutModel) -> self.current), (fun (value : Aardvark.UI.Primitives.Golden.WindowLayout) (self : LayoutModel) -> { self with current = value }))
        static member activeName_ = ((fun (self : LayoutModel) -> self.activeName), (fun (value : Microsoft.FSharp.Core.string) (self : LayoutModel) -> { self with activeName = value }))
        static member activeShape_ = ((fun (self : LayoutModel) -> self.activeShape), (fun (value : Microsoft.FSharp.Core.string) (self : LayoutModel) -> { self with activeShape = value }))
        static member library_ = ((fun (self : LayoutModel) -> self.library), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>) (self : LayoutModel) -> { self with library = value }))
        static member dialog_ = ((fun (self : LayoutModel) -> self.dialog), (fun (value : LayoutDialog) (self : LayoutModel) -> { self with dialog = value }))
        static member nameInput_ = ((fun (self : LayoutModel) -> self.nameInput), (fun (value : Microsoft.FSharp.Core.string) (self : LayoutModel) -> { self with nameInput = value }))

