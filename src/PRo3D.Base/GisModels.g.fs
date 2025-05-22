//69a9b5f0-1793-558f-2627-01633cb01ff7
//3a1757c3-e4f2-208b-2a9c-d34a65623c3b
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Base.Gis

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base.Gis
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveReferenceFrame(value : ReferenceFrame) =
    let _label_ = FSharp.Data.Adaptive.cval(value.label)
    let _description_ = FSharp.Data.Adaptive.cval(value.description)
    let _spiceNameText_ = FSharp.Data.Adaptive.cval(value.spiceNameText)
    let _isEditing_ = FSharp.Data.Adaptive.cval(value.isEditing)
    let _entity_ = FSharp.Data.Adaptive.cval(value.entity)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ReferenceFrame) = AdaptiveReferenceFrame(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ReferenceFrame) -> AdaptiveReferenceFrame(value)) (fun (adaptive : AdaptiveReferenceFrame) (value : ReferenceFrame) -> adaptive.Update(value))
    member __.Update(value : ReferenceFrame) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ReferenceFrame>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _label_.Value <- value.label
            _description_.Value <- value.description
            _spiceNameText_.Value <- value.spiceNameText
            _isEditing_.Value <- value.isEditing
            _entity_.Value <- value.entity
    member __.Current = __adaptive
    member __.version = __value.version
    member __.label = _label_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.description = _description_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.spiceName = __value.spiceName
    member __.spiceNameText = _spiceNameText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.isEditing = _isEditing_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.entity = _entity_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<EntitySpiceName>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ReferenceFrameLenses = 
    type ReferenceFrame with
        static member version_ = ((fun (self : ReferenceFrame) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ReferenceFrame) -> { self with version = value }))
        static member label_ = ((fun (self : ReferenceFrame) -> self.label), (fun (value : Microsoft.FSharp.Core.string) (self : ReferenceFrame) -> { self with label = value }))
        static member description_ = ((fun (self : ReferenceFrame) -> self.description), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : ReferenceFrame) -> { self with description = value }))
        static member spiceName_ = ((fun (self : ReferenceFrame) -> self.spiceName), (fun (value : FrameSpiceName) (self : ReferenceFrame) -> { self with spiceName = value }))
        static member spiceNameText_ = ((fun (self : ReferenceFrame) -> self.spiceNameText), (fun (value : Microsoft.FSharp.Core.string) (self : ReferenceFrame) -> { self with spiceNameText = value }))
        static member isEditing_ = ((fun (self : ReferenceFrame) -> self.isEditing), (fun (value : Microsoft.FSharp.Core.bool) (self : ReferenceFrame) -> { self with isEditing = value }))
        static member entity_ = ((fun (self : ReferenceFrame) -> self.entity), (fun (value : Microsoft.FSharp.Core.option<EntitySpiceName>) (self : ReferenceFrame) -> { self with entity = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveEntity(value : Entity) =
    let _isEditing_ = FSharp.Data.Adaptive.cval(value.isEditing)
    let _draw_ = FSharp.Data.Adaptive.cval(value.draw)
    let _spiceNameText_ = FSharp.Data.Adaptive.cval(value.spiceNameText)
    let _label_ = FSharp.Data.Adaptive.cval(value.label)
    let _color_ = FSharp.Data.Adaptive.cval(value.color)
    let _radius_ = FSharp.Data.Adaptive.cval(value.radius)
    let _geometryPath_ = FSharp.Data.Adaptive.cval(value.geometryPath)
    let _textureName_ = FSharp.Data.Adaptive.cval(value.textureName)
    let _showTrajectory_ = FSharp.Data.Adaptive.cval(value.showTrajectory)
    let _defaultFrame_ = FSharp.Data.Adaptive.cval(value.defaultFrame)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Entity) = AdaptiveEntity(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Entity) -> AdaptiveEntity(value)) (fun (adaptive : AdaptiveEntity) (value : Entity) -> adaptive.Update(value))
    member __.Update(value : Entity) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Entity>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _isEditing_.Value <- value.isEditing
            _draw_.Value <- value.draw
            _spiceNameText_.Value <- value.spiceNameText
            _label_.Value <- value.label
            _color_.Value <- value.color
            _radius_.Value <- value.radius
            _geometryPath_.Value <- value.geometryPath
            _textureName_.Value <- value.textureName
            _showTrajectory_.Value <- value.showTrajectory
            _defaultFrame_.Value <- value.defaultFrame
    member __.Current = __adaptive
    member __.version = __value.version
    member __.spiceName = __value.spiceName
    member __.isEditing = _isEditing_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.draw = _draw_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.spiceNameText = _spiceNameText_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.label = _label_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.color = _color_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4f>
    member __.radius = _radius_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.geometryPath = _geometryPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.textureName = _textureName_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.showTrajectory = _showTrajectory_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.defaultFrame = _defaultFrame_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<FrameSpiceName>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module EntityLenses = 
    type Entity with
        static member version_ = ((fun (self : Entity) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Entity) -> { self with version = value }))
        static member spiceName_ = ((fun (self : Entity) -> self.spiceName), (fun (value : EntitySpiceName) (self : Entity) -> { self with spiceName = value }))
        static member isEditing_ = ((fun (self : Entity) -> self.isEditing), (fun (value : Microsoft.FSharp.Core.bool) (self : Entity) -> { self with isEditing = value }))
        static member draw_ = ((fun (self : Entity) -> self.draw), (fun (value : Microsoft.FSharp.Core.bool) (self : Entity) -> { self with draw = value }))
        static member spiceNameText_ = ((fun (self : Entity) -> self.spiceNameText), (fun (value : Microsoft.FSharp.Core.string) (self : Entity) -> { self with spiceNameText = value }))
        static member label_ = ((fun (self : Entity) -> self.label), (fun (value : Microsoft.FSharp.Core.string) (self : Entity) -> { self with label = value }))
        static member color_ = ((fun (self : Entity) -> self.color), (fun (value : Aardvark.Base.C4f) (self : Entity) -> { self with color = value }))
        static member radius_ = ((fun (self : Entity) -> self.radius), (fun (value : Microsoft.FSharp.Core.float) (self : Entity) -> { self with radius = value }))
        static member geometryPath_ = ((fun (self : Entity) -> self.geometryPath), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : Entity) -> { self with geometryPath = value }))
        static member textureName_ = ((fun (self : Entity) -> self.textureName), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : Entity) -> { self with textureName = value }))
        static member showTrajectory_ = ((fun (self : Entity) -> self.showTrajectory), (fun (value : Microsoft.FSharp.Core.bool) (self : Entity) -> { self with showTrajectory = value }))
        static member defaultFrame_ = ((fun (self : Entity) -> self.defaultFrame), (fun (value : Microsoft.FSharp.Core.option<FrameSpiceName>) (self : Entity) -> { self with defaultFrame = value }))

