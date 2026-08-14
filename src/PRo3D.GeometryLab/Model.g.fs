//8b50ca9a-0d3a-70e8-cd5f-54cf0542e6db
//63897610-a109-2b72-10bc-caa7357dd412
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.GeometryLab

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.GeometryLab
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveModel(value : Model) =
    let _shapes_ = FSharp.Data.Adaptive.clist(value.shapes)
    let _nextId_ = FSharp.Data.Adaptive.cval(value.nextId)
    let _tool_ = FSharp.Data.Adaptive.cval(value.tool)
    let _drawing_ = FSharp.Data.Adaptive.clist(value.drawing)
    let _cursor_ = FSharp.Data.Adaptive.cval(value.cursor)
    let _cutFrom_ = FSharp.Data.Adaptive.cval(value.cutFrom)
    let _status_ = FSharp.Data.Adaptive.cval(value.status)
    let _past_ = FSharp.Data.Adaptive.cval(value.past)
    let _future_ = FSharp.Data.Adaptive.cval(value.future)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Model) = AdaptiveModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Model) -> AdaptiveModel(value)) (fun (adaptive : AdaptiveModel) (value : Model) -> adaptive.Update(value))
    member __.Update(value : Model) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Model>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _shapes_.Value <- value.shapes
            _nextId_.Value <- value.nextId
            _tool_.Value <- value.tool
            _drawing_.Value <- value.drawing
            _cursor_.Value <- value.cursor
            _cutFrom_.Value <- value.cutFrom
            _status_.Value <- value.status
            _past_.Value <- value.past
            _future_.Value <- value.future
    member __.Current = __adaptive
    member __.shapes = _shapes_ :> FSharp.Data.Adaptive.alist<Shape>
    member __.nextId = _nextId_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.tool = _tool_ :> FSharp.Data.Adaptive.aval<Tool>
    member __.drawing = _drawing_ :> FSharp.Data.Adaptive.alist<Aardvark.Base.V2d>
    member __.cursor = _cursor_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Base.V2d>>
    member __.cutFrom = _cutFrom_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Base.V2d>>
    member __.status = _status_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.past = _past_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Model>>
    member __.future = _future_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Model>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ModelLenses = 
    type Model with
        static member shapes_ = ((fun (self : Model) -> self.shapes), (fun (value : FSharp.Data.Adaptive.IndexList<Shape>) (self : Model) -> { self with shapes = value }))
        static member nextId_ = ((fun (self : Model) -> self.nextId), (fun (value : Microsoft.FSharp.Core.int) (self : Model) -> { self with nextId = value }))
        static member tool_ = ((fun (self : Model) -> self.tool), (fun (value : Tool) (self : Model) -> { self with tool = value }))
        static member drawing_ = ((fun (self : Model) -> self.drawing), (fun (value : FSharp.Data.Adaptive.IndexList<Aardvark.Base.V2d>) (self : Model) -> { self with drawing = value }))
        static member cursor_ = ((fun (self : Model) -> self.cursor), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.V2d>) (self : Model) -> { self with cursor = value }))
        static member cutFrom_ = ((fun (self : Model) -> self.cutFrom), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.V2d>) (self : Model) -> { self with cutFrom = value }))
        static member status_ = ((fun (self : Model) -> self.status), (fun (value : Microsoft.FSharp.Core.string) (self : Model) -> { self with status = value }))
        static member past_ = ((fun (self : Model) -> self.past), (fun (value : Microsoft.FSharp.Core.Option<Model>) (self : Model) -> { self with past = value }))
        static member future_ = ((fun (self : Model) -> self.future), (fun (value : Microsoft.FSharp.Core.Option<Model>) (self : Model) -> { self with future = value }))

