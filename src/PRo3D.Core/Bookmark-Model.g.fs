//452979fa-0d8b-94b1-92ba-1d4b904b46e0
//dc72a6fb-d3e0-19ec-1b3b-e50ecb94c42c
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
type AdaptiveBookmark(value : Bookmark) =
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _cameraView_ = FSharp.Data.Adaptive.cval(value.cameraView)
    let _exploreCenter_ = FSharp.Data.Adaptive.cval(value.exploreCenter)
    let _navigationMode_ = FSharp.Data.Adaptive.cval(value.navigationMode)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Bookmark) = AdaptiveBookmark(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Bookmark) -> AdaptiveBookmark(value)) (fun (adaptive : AdaptiveBookmark) (value : Bookmark) -> adaptive.Update(value))
    member __.Update(value : Bookmark) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Bookmark>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _name_.Value <- value.name
            _cameraView_.Value <- value.cameraView
            _exploreCenter_.Value <- value.exploreCenter
            _navigationMode_.Value <- value.navigationMode
    member __.Current = __adaptive
    member __.version = __value.version
    member __.key = __value.key
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.cameraView = _cameraView_ :> FSharp.Data.Adaptive.aval<Aardvark.Rendering.CameraView>
    member __.exploreCenter = _exploreCenter_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.navigationMode = _navigationMode_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.NavigationMode>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module BookmarkLenses = 
    type Bookmark with
        static member version_ = ((fun (self : Bookmark) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Bookmark) -> { self with version = value }))
        static member key_ = ((fun (self : Bookmark) -> self.key), (fun (value : System.Guid) (self : Bookmark) -> { self with key = value }))
        static member name_ = ((fun (self : Bookmark) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : Bookmark) -> { self with name = value }))
        static member cameraView_ = ((fun (self : Bookmark) -> self.cameraView), (fun (value : Aardvark.Rendering.CameraView) (self : Bookmark) -> { self with cameraView = value }))
        static member exploreCenter_ = ((fun (self : Bookmark) -> self.exploreCenter), (fun (value : Aardvark.Base.V3d) (self : Bookmark) -> { self with exploreCenter = value }))
        static member navigationMode_ = ((fun (self : Bookmark) -> self.navigationMode), (fun (value : PRo3D.Base.NavigationMode) (self : Bookmark) -> { self with navigationMode = value }))

