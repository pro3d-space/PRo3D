//c6250a53-235a-6766-b971-1a2859e6892a
//14366f9b-f6cd-6c2e-9589-b2430ce9134d
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
type AdaptiveSol(value : Sol) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _location_ = FSharp.Data.Adaptive.cval(value.location)
    let _solNumber_ = FSharp.Data.Adaptive.cval(value.solNumber)
    let _site_ = FSharp.Data.Adaptive.cval(value.site)
    let _yaw_ = FSharp.Data.Adaptive.cval(value.yaw)
    let _pitch_ = FSharp.Data.Adaptive.cval(value.pitch)
    let _roll_ = FSharp.Data.Adaptive.cval(value.roll)
    let _tilt_ = FSharp.Data.Adaptive.cval(value.tilt)
    let _note_ = FSharp.Data.Adaptive.cval(value.note)
    let _distanceM_ = FSharp.Data.Adaptive.cval(value.distanceM)
    let _totalDistanceM_ = FSharp.Data.Adaptive.cval(value.totalDistanceM)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Sol) = AdaptiveSol(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Sol) -> AdaptiveSol(value)) (fun (adaptive : AdaptiveSol) (value : Sol) -> adaptive.Update(value))
    member __.Update(value : Sol) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Sol>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _location_.Value <- value.location
            _solNumber_.Value <- value.solNumber
            _site_.Value <- value.site
            _yaw_.Value <- value.yaw
            _pitch_.Value <- value.pitch
            _roll_.Value <- value.roll
            _tilt_.Value <- value.tilt
            _note_.Value <- value.note
            _distanceM_.Value <- value.distanceM
            _totalDistanceM_.Value <- value.totalDistanceM
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.location = _location_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.solNumber = _solNumber_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.site = _site_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.yaw = _yaw_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.pitch = _pitch_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.roll = _roll_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.tilt = _tilt_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.note = _note_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.distanceM = _distanceM_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.totalDistanceM = _totalDistanceM_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SolLenses = 
    type Sol with
        static member version_ = ((fun (self : Sol) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Sol) -> { self with version = value }))
        static member location_ = ((fun (self : Sol) -> self.location), (fun (value : Aardvark.Base.V3d) (self : Sol) -> { self with location = value }))
        static member solNumber_ = ((fun (self : Sol) -> self.solNumber), (fun (value : Microsoft.FSharp.Core.int) (self : Sol) -> { self with solNumber = value }))
        static member site_ = ((fun (self : Sol) -> self.site), (fun (value : Microsoft.FSharp.Core.int) (self : Sol) -> { self with site = value }))
        static member yaw_ = ((fun (self : Sol) -> self.yaw), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with yaw = value }))
        static member pitch_ = ((fun (self : Sol) -> self.pitch), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with pitch = value }))
        static member roll_ = ((fun (self : Sol) -> self.roll), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with roll = value }))
        static member tilt_ = ((fun (self : Sol) -> self.tilt), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with tilt = value }))
        static member note_ = ((fun (self : Sol) -> self.note), (fun (value : Microsoft.FSharp.Core.string) (self : Sol) -> { self with note = value }))
        static member distanceM_ = ((fun (self : Sol) -> self.distanceM), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with distanceM = value }))
        static member totalDistanceM_ = ((fun (self : Sol) -> self.totalDistanceM), (fun (value : Microsoft.FSharp.Core.float) (self : Sol) -> { self with totalDistanceM = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveTraverse(value : Traverse) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _guid_ = FSharp.Data.Adaptive.cval(value.guid)
    let _tName_ = FSharp.Data.Adaptive.cval(value.tName)
    let _sols_ = FSharp.Data.Adaptive.cval(value.sols)
    let _selectedSol_ = FSharp.Data.Adaptive.cval(value.selectedSol)
    let _showLines_ = FSharp.Data.Adaptive.cval(value.showLines)
    let _showText_ = FSharp.Data.Adaptive.cval(value.showText)
    let _tTextSize_ = Aardvark.UI.AdaptiveNumericInput(value.tTextSize)
    let _showDots_ = FSharp.Data.Adaptive.cval(value.showDots)
    let _isVisibleT_ = FSharp.Data.Adaptive.cval(value.isVisibleT)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Traverse) = AdaptiveTraverse(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Traverse) -> AdaptiveTraverse(value)) (fun (adaptive : AdaptiveTraverse) (value : Traverse) -> adaptive.Update(value))
    member __.Update(value : Traverse) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Traverse>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _guid_.Value <- value.guid
            _tName_.Value <- value.tName
            _sols_.Value <- value.sols
            _selectedSol_.Value <- value.selectedSol
            _showLines_.Value <- value.showLines
            _showText_.Value <- value.showText
            _tTextSize_.Update(value.tTextSize)
            _showDots_.Value <- value.showDots
            _isVisibleT_.Value <- value.isVisibleT
            _color_.Update(value.color)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = _guid_ :> FSharp.Data.Adaptive.aval<System.Guid>
    member __.tName = _tName_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.sols = _sols_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<Sol>>
    member __.selectedSol = _selectedSol_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>>
    member __.showLines = _showLines_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showText = _showText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.tTextSize = _tTextSize_
    member __.showDots = _showDots_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isVisibleT = _isVisibleT_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.color = _color_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TraverseLenses = 
    type Traverse with
        static member version_ = ((fun (self : Traverse) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Traverse) -> { self with version = value }))
        static member guid_ = ((fun (self : Traverse) -> self.guid), (fun (value : System.Guid) (self : Traverse) -> { self with guid = value }))
        static member tName_ = ((fun (self : Traverse) -> self.tName), (fun (value : Microsoft.FSharp.Core.string) (self : Traverse) -> { self with tName = value }))
        static member sols_ = ((fun (self : Traverse) -> self.sols), (fun (value : Microsoft.FSharp.Collections.List<Sol>) (self : Traverse) -> { self with sols = value }))
        static member selectedSol_ = ((fun (self : Traverse) -> self.selectedSol), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>) (self : Traverse) -> { self with selectedSol = value }))
        static member showLines_ = ((fun (self : Traverse) -> self.showLines), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showLines = value }))
        static member showText_ = ((fun (self : Traverse) -> self.showText), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showText = value }))
        static member tTextSize_ = ((fun (self : Traverse) -> self.tTextSize), (fun (value : Aardvark.UI.NumericInput) (self : Traverse) -> { self with tTextSize = value }))
        static member showDots_ = ((fun (self : Traverse) -> self.showDots), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showDots = value }))
        static member isVisibleT_ = ((fun (self : Traverse) -> self.isVisibleT), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with isVisibleT = value }))
        static member color_ = ((fun (self : Traverse) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : Traverse) -> { self with color = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveTraverseModel(value : TraverseModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _traverses_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.traverses, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
    let _selectedTraverse_ = FSharp.Data.Adaptive.cval(value.selectedTraverse)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : TraverseModel) = AdaptiveTraverseModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : TraverseModel) -> AdaptiveTraverseModel(value)) (fun (adaptive : AdaptiveTraverseModel) (value : TraverseModel) -> adaptive.Update(value))
    member __.Update(value : TraverseModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<TraverseModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _traverses_.Update(value.traverses)
            _selectedTraverse_.Value <- value.selectedTraverse
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.traverses = _traverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.selectedTraverse = _selectedTraverse_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TraverseModelLenses = 
    type TraverseModel with
        static member version_ = ((fun (self : TraverseModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : TraverseModel) -> { self with version = value }))
        static member traverses_ = ((fun (self : TraverseModel) -> self.traverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with traverses = value }))
        static member selectedTraverse_ = ((fun (self : TraverseModel) -> self.selectedTraverse), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : TraverseModel) -> { self with selectedTraverse = value }))

