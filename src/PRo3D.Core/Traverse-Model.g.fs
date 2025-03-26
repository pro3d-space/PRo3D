//033d7929-4c59-87d6-75c3-6bc9f1b44d96
//94be513e-5555-8d91-0bbd-5be4f8452e86
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
type AdaptiveTraverse(value : Traverse) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _sols_ = FSharp.Data.Adaptive.cval(value.sols)
    let _selectedSol_ = FSharp.Data.Adaptive.cval(value.selectedSol)
    let _showLines_ = FSharp.Data.Adaptive.cval(value.showLines)
    let _showText_ = FSharp.Data.Adaptive.cval(value.showText)
    let _tTextSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.tTextSize)
    let _tLineWidth_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.tLineWidth)
    let _showDots_ = FSharp.Data.Adaptive.cval(value.showDots)
    let _isVisibleT_ = FSharp.Data.Adaptive.cval(value.isVisibleT)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let _heightOffset_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.heightOffset)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Traverse) = AdaptiveTraverse(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Traverse) -> AdaptiveTraverse(value)) (fun (adaptive : AdaptiveTraverse) (value : Traverse) -> adaptive.Update(value))
    member __.Update(value : Traverse) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Traverse>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _sols_.Value <- value.sols
            _selectedSol_.Value <- value.selectedSol
            _showLines_.Value <- value.showLines
            _showText_.Value <- value.showText
            _tTextSize_.Update(value.tTextSize)
            _tLineWidth_.Update(value.tLineWidth)
            _showDots_.Value <- value.showDots
            _isVisibleT_.Value <- value.isVisibleT
            _color_.Update(value.color)
            _heightOffset_.Update(value.heightOffset)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = __value.guid
    member __.tName = __value.tName
    member __.sols = _sols_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.List<Sol>>
    member __.traverseType = __value.traverseType
    member __.selectedSol = _selectedSol_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>>
    member __.showLines = _showLines_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.showText = _showText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.tTextSize = _tTextSize_
    member __.tLineWidth = _tLineWidth_
    member __.showDots = _showDots_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.isVisibleT = _isVisibleT_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.color = _color_
    member __.heightOffset = _heightOffset_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TraverseLenses = 
    type Traverse with
        static member version_ = ((fun (self : Traverse) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Traverse) -> { self with version = value }))
        static member guid_ = ((fun (self : Traverse) -> self.guid), (fun (value : System.Guid) (self : Traverse) -> { self with guid = value }))
        static member tName_ = ((fun (self : Traverse) -> self.tName), (fun (value : Microsoft.FSharp.Core.string) (self : Traverse) -> { self with tName = value }))
        static member sols_ = ((fun (self : Traverse) -> self.sols), (fun (value : Microsoft.FSharp.Collections.List<Sol>) (self : Traverse) -> { self with sols = value }))
        static member traverseType_ = ((fun (self : Traverse) -> self.traverseType), (fun (value : TraverseType) (self : Traverse) -> { self with traverseType = value }))
        static member selectedSol_ = ((fun (self : Traverse) -> self.selectedSol), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.int>) (self : Traverse) -> { self with selectedSol = value }))
        static member showLines_ = ((fun (self : Traverse) -> self.showLines), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showLines = value }))
        static member showText_ = ((fun (self : Traverse) -> self.showText), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showText = value }))
        static member tTextSize_ = ((fun (self : Traverse) -> self.tTextSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with tTextSize = value }))
        static member tLineWidth_ = ((fun (self : Traverse) -> self.tLineWidth), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with tLineWidth = value }))
        static member showDots_ = ((fun (self : Traverse) -> self.showDots), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with showDots = value }))
        static member isVisibleT_ = ((fun (self : Traverse) -> self.isVisibleT), (fun (value : Microsoft.FSharp.Core.bool) (self : Traverse) -> { self with isVisibleT = value }))
        static member color_ = ((fun (self : Traverse) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : Traverse) -> { self with color = value }))
        static member heightOffset_ = ((fun (self : Traverse) -> self.heightOffset), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : Traverse) -> { self with heightOffset = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveTraverseModel(value : TraverseModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _traverses_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.traverses, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
    let _missions_ =
        let inline __arg2 (m : AdaptiveTraverse) (v : Traverse) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.missions, (fun (v : Traverse) -> AdaptiveTraverse(v)), __arg2, (fun (m : AdaptiveTraverse) -> m))
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
            _missions_.Update(value.missions)
            _selectedTraverse_.Value <- value.selectedTraverse
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.traverses = _traverses_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.missions = _missions_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveTraverse>
    member __.selectedTraverse = _selectedTraverse_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module TraverseModelLenses = 
    type TraverseModel with
        static member version_ = ((fun (self : TraverseModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : TraverseModel) -> { self with version = value }))
        static member traverses_ = ((fun (self : TraverseModel) -> self.traverses), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with traverses = value }))
        static member missions_ = ((fun (self : TraverseModel) -> self.missions), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, Traverse>) (self : TraverseModel) -> { self with missions = value }))
        static member selectedTraverse_ = ((fun (self : TraverseModel) -> self.selectedTraverse), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : TraverseModel) -> { self with selectedTraverse = value }))

