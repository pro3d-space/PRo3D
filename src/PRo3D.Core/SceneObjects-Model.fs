namespace PRo3D.Core

open System
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.Base
open Aardvark.UI
open PRo3D
open PRo3D.Base
open PRo3D.Core.Surface

open Chiron

open Aether
open Aether.Operators

#nowarn "0686"

[<ModelType>]
type SceneObject = {
    version         : int
    guid            : System.Guid
    name            : string
    importPath      : string
   
    isVisible       : bool
    position        : V3d        
    scaling         : NumericInput
    transformation  : Transformations
    preTransform    : Trafo3d
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SceneObject =    
    
    let current = 0   
    let read0 =
        json {
            let! guid            = Json.read "guid"
            let! name            = Json.read "name"
            let! importPath      = Json.read "importPath" 
            let! isVisible       = Json.read "isVisible"
            let! position        = Json.read "position"
            let! scaling         = Json.readWith Ext.fromJson<NumericInput,Ext> "scaling"
            let! transformation  = Json.read "transformation"
            let! preTransform    = Json.read "preTransform"
            
            return 
                {
                    version       = current
                    guid            = guid |> Guid
                    name            = name
                    importPath      = importPath
                    isVisible       = isVisible
                    position        = position |> V3d.Parse
                    scaling         = scaling
                    transformation  = transformation
                    preTransform    = preTransform |> Trafo3d.Parse
                }
        }

type SceneObject with
    static member FromJson(_ : SceneObject) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! SceneObject.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of SceneObject"
                |> Json.error
        }
    static member ToJson(x : SceneObject) =
        json {
            do! Json.write "version" x.version
            do! Json.write "guid" x.guid
            do! Json.write "name" x.name
            do! Json.write "importPath" x.importPath
            do! Json.write "isVisible" x.isVisible    
            do! Json.write "position" (x.position.ToString())
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "scaling" x.scaling
            do! Json.write "transformation" x.transformation  
            do! Json.write "preTransform" (x.preTransform.ToString())
        }


[<ModelType>]
type SceneObjectsModel = {
    version             : int
    sceneObjects        : HashMap<Guid,SceneObject>
    sgSceneObjects      : HashMap<Guid,SgSurface>
    selectedSceneObject : Option<Guid> 
}

module SceneObjectsModel =
    
    let current = 0    
    let read0 = 
        json {
            let! sceneObjects = Json.read "sceneObjects"
            let sceneObjects = sceneObjects |> List.map(fun (a : SceneObject) -> (a.guid, a)) |> HashMap.ofList

            let! selected     = Json.read "selectedSceneObject"
            return 
                {
                    version             = current
                    sceneObjects        = sceneObjects
                    sgSceneObjects      = HashMap.empty
                    selectedSceneObject = selected
                }
        }  
        
    let initial =
        {
            version             = current
            sceneObjects        = HashMap.empty
            sgSceneObjects      = HashMap.empty
            selectedSceneObject = None
        }

 
    
type SceneObjectsModel with
    static member FromJson (_ : SceneObjectsModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! SceneObjectsModel.read0
            | _ ->
                return! v
                |> sprintf "don't know version %A  of SceneObjectsModel"
                |> Json.error
        }

    static member ToJson (x : SceneObjectsModel) =
        json {
            do! Json.write "version"             x.version
            do! Json.write "sceneObjects"       (x.sceneObjects |> HashMap.toList |> List.map snd)
            do! Json.write "selectedSceneObject" x.selectedSceneObject
        }

//TODO refactor: do we really need the transformation init code multiple times?
module InitSceneObjectParams =

    let scaling = {
        value  = 1.00
        min    = 0.01
        max    = 50.00
        step   = 0.01
        format = "{0:0.00}"
    }

    let translationInput = {
        value   = 0.0
        min     = -10000000.0
        max     = 10000000.0
        step    = 0.01
        format  = "{0:0.00}"
    }

    let yaw = {
        value   = 0.0
        min     = -180.0
        max     = +180.0
        step    = 0.01
        format  = "{0:0.00}"
    }

    let initTranslation (v : V3d) = {
        x     = { translationInput with value = v.X }
        y     = { translationInput with value = v.Y }
        z     = { translationInput with value = v.Z }
        value = v    
    }

    let transformations = {
        version              = Transformations.current
        useTranslationArrows = false
        translation          = initTranslation (V3d.OOO)
        trafo                = Trafo3d.Identity
        pitch                = Transformations.Initial.pitch
        roll                 = Transformations.Initial.roll
        yaw                  = yaw
        pivot                = V3d.Zero
        flipZ                = false
        isSketchFab          = false
    }

    let initNoffset = {
        value   = float 0.0
        min     = float 0.0
        max     = float 360.0
        step    = float 1.0
        format  = "{0:0.0}"
    }