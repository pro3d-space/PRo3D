//44bf46e2-d6e6-d47a-5b6d-1479bc4a3205
//3eec39aa-da15-48fe-7fd8-30f0785fd7ed
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
type AdaptiveSceneObject(value : SceneObject) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _guid_ = FSharp.Data.Adaptive.cval(value.guid)
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _importPath_ = FSharp.Data.Adaptive.cval(value.importPath)
    let _isVisible_ = FSharp.Data.Adaptive.cval(value.isVisible)
    let _position_ = FSharp.Data.Adaptive.cval(value.position)
    let _transformation_ = PRo3D.Core.Surface.AdaptiveTransformations(value.transformation)
    let _preTransform_ = FSharp.Data.Adaptive.cval(value.preTransform)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SceneObject) = AdaptiveSceneObject(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SceneObject) -> AdaptiveSceneObject(value)) (fun (adaptive : AdaptiveSceneObject) (value : SceneObject) -> adaptive.Update(value))
    member __.Update(value : SceneObject) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SceneObject>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _guid_.Value <- value.guid
            _name_.Value <- value.name
            _importPath_.Value <- value.importPath
            _isVisible_.Value <- value.isVisible
            _position_.Value <- value.position
            _transformation_.Update(value.transformation)
            _preTransform_.Value <- value.preTransform
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.guid = _guid_ :> FSharp.Data.Adaptive.aval<System.Guid>
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.importPath = _importPath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.isVisible = _isVisible_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.position = _position_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.transformation = _transformation_
    member __.preTransform = _preTransform_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SceneObjectLenses = 
    type SceneObject with
        static member version_ = ((fun (self : SceneObject) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : SceneObject) -> { self with version = value }))
        static member guid_ = ((fun (self : SceneObject) -> self.guid), (fun (value : System.Guid) (self : SceneObject) -> { self with guid = value }))
        static member name_ = ((fun (self : SceneObject) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : SceneObject) -> { self with name = value }))
        static member importPath_ = ((fun (self : SceneObject) -> self.importPath), (fun (value : Microsoft.FSharp.Core.string) (self : SceneObject) -> { self with importPath = value }))
        static member isVisible_ = ((fun (self : SceneObject) -> self.isVisible), (fun (value : Microsoft.FSharp.Core.bool) (self : SceneObject) -> { self with isVisible = value }))
        static member position_ = ((fun (self : SceneObject) -> self.position), (fun (value : Aardvark.Base.V3d) (self : SceneObject) -> { self with position = value }))
        static member transformation_ = ((fun (self : SceneObject) -> self.transformation), (fun (value : PRo3D.Core.Surface.Transformations) (self : SceneObject) -> { self with transformation = value }))
        static member preTransform_ = ((fun (self : SceneObject) -> self.preTransform), (fun (value : Aardvark.Base.Trafo3d) (self : SceneObject) -> { self with preTransform = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSceneObjectsModel(value : SceneObjectsModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _sceneObjects_ =
        let inline __arg2 (m : AdaptiveSceneObject) (v : SceneObject) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.sceneObjects, (fun (v : SceneObject) -> AdaptiveSceneObject(v)), __arg2, (fun (m : AdaptiveSceneObject) -> m))
    let _sgSceneObjects_ =
        let inline __arg2 (m : PRo3D.Core.Surface.AdaptiveSgSurface) (v : PRo3D.Core.Surface.SgSurface) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelMap(value.sgSceneObjects, (fun (v : PRo3D.Core.Surface.SgSurface) -> PRo3D.Core.Surface.AdaptiveSgSurface(v)), __arg2, (fun (m : PRo3D.Core.Surface.AdaptiveSgSurface) -> m))
    let _selectedSceneObject_ = FSharp.Data.Adaptive.cval(value.selectedSceneObject)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SceneObjectsModel) = AdaptiveSceneObjectsModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SceneObjectsModel) -> AdaptiveSceneObjectsModel(value)) (fun (adaptive : AdaptiveSceneObjectsModel) (value : SceneObjectsModel) -> adaptive.Update(value))
    member __.Update(value : SceneObjectsModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SceneObjectsModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _sceneObjects_.Update(value.sceneObjects)
            _sgSceneObjects_.Update(value.sgSceneObjects)
            _selectedSceneObject_.Value <- value.selectedSceneObject
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.sceneObjects = _sceneObjects_ :> FSharp.Data.Adaptive.amap<System.Guid, AdaptiveSceneObject>
    member __.sgSceneObjects = _sgSceneObjects_ :> FSharp.Data.Adaptive.amap<System.Guid, PRo3D.Core.Surface.AdaptiveSgSurface>
    member __.selectedSceneObject = _selectedSceneObject_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<System.Guid>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SceneObjectsModelLenses = 
    type SceneObjectsModel with
        static member version_ = ((fun (self : SceneObjectsModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : SceneObjectsModel) -> { self with version = value }))
        static member sceneObjects_ = ((fun (self : SceneObjectsModel) -> self.sceneObjects), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, SceneObject>) (self : SceneObjectsModel) -> { self with sceneObjects = value }))
        static member sgSceneObjects_ = ((fun (self : SceneObjectsModel) -> self.sgSceneObjects), (fun (value : FSharp.Data.Adaptive.HashMap<System.Guid, PRo3D.Core.Surface.SgSurface>) (self : SceneObjectsModel) -> { self with sgSceneObjects = value }))
        static member selectedSceneObject_ = ((fun (self : SceneObjectsModel) -> self.selectedSceneObject), (fun (value : Microsoft.FSharp.Core.Option<System.Guid>) (self : SceneObjectsModel) -> { self with selectedSceneObject = value }))

