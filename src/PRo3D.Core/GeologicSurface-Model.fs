namespace PRo3D.Core

open System
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open PRo3D
open PRo3D.Base
open PRo3D.Core.Surface

open Chiron

open Aether
open Aether.Operators

#nowarn "0686"

[<ModelType>]
type GeologicSurface = {
    version         : int
    guid            : System.Guid
    name            : string

    isVisible       : bool
    view            : CameraView

    points1         : IndexList<V3d> 
    points2         : IndexList<V3d> 

    color           : ColorInput
    transparency    : NumericInput
    thickness       : NumericInput

    invertMeshing    : bool
    sgGeoSurface    : List<(Triangle3d * C4b)> //ISg
}


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module GeologicSurface =    
    
    let current = 0   
    let read0 =
        json {
            let! guid            = Json.read "guid"
            let! name            = Json.read "name"

            let! isVisible       = Json.read "isVisible"
            let! (cameraView : list<string>) = Json.read "view"
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])

            let! points1         = Json.readWith Ext.fromJson<list<V3d>,Ext> "points1" 
            let! points2         = Json.readWith Ext.fromJson<list<V3d>,Ext> "points2" 

            let! color           = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! transparency    = Json.readWith Ext.fromJson<NumericInput,Ext> "transparency"
            let! thickness       = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"

            let! invertMeshing   = Json.read "invertMeshing"
            
            return 
                {
                    version         = current
                    guid            = guid |> Guid
                    name            = name

                    isVisible       = isVisible
                    view            = cameraView

                    points1       = points1  |> IndexList.ofList 
                    points2       = points2  |> IndexList.ofList 

                    color        = color
                    transparency = transparency 
                    thickness    = thickness 

                    invertMeshing = invertMeshing
                    sgGeoSurface = List.empty 
                }
        }

type GeologicSurface with
    static member FromJson(_ : GeologicSurface) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! GeologicSurface.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of GeologicSurface"
                |> Json.error
        }
    static member ToJson(x : GeologicSurface) =
        json {
            do! Json.write "version" x.version
            do! Json.write "guid" x.guid
            do! Json.write "name" x.name

            let camView = x.view
            let camView = [camView.Sky; camView.Location; camView.Forward; camView.Up ; camView.Right] |> List.map(fun x -> x.ToString())      
            do! Json.write "view" camView
            do! Json.write "isVisible" x.isVisible 

            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points1" (x.points1 |> IndexList.toList)
            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points2" (x.points2 |> IndexList.toList)
           
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>) "color" x.color
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "transparency" x.transparency
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "thickness" x.thickness

            do! Json.write "invertMeshing" x.invertMeshing 
        }


[<ModelType>]
type GeologicSurfacesModel = {
    version                 : int
    geologicSurfaces        : HashMap<Guid,GeologicSurface>
    selectedGeologicSurface : Option<Guid> 
}

module GeologicSurfacesModel =
    
    let current = 0    
    let read0 = 
        json {
            let! geologicSurfaces = Json.read "geologicSurfaces"
            let geologicSurfaces = geologicSurfaces |> List.map(fun (a : GeologicSurface) -> (a.guid, a)) |> HashMap.ofList

            let! selected     = Json.read "selectedGeologicSurface"
            return 
                {
                    version                 = current
                    geologicSurfaces        = geologicSurfaces
                    selectedGeologicSurface = selected
                }
        }  
        
    let initial =
        {
            version                 = current
            geologicSurfaces        = HashMap.empty
            selectedGeologicSurface = None
        }

 
    
type GeologicSurfacesModel with
    static member FromJson (_ : GeologicSurfacesModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! GeologicSurfacesModel.read0
            | _ ->
                return! v
                |> sprintf "don't know version %A  of GeologicSurfacesModel"
                |> Json.error
        }

    static member ToJson (x : GeologicSurfacesModel) =
        json {
            do! Json.write "version"                    x.version
            do! Json.write "geologicSurfaces"           (x.geologicSurfaces |> HashMap.toList |> List.map snd)
            do! Json.write "selectedGeologicSurface"    x.selectedGeologicSurface
        }

module InitGeologicSurfacesParams =

    let thickness (value : float)  = {
        value   = value //3.0
        min     = 1.0
        max     = 20.0
        step    = 1.0
        format  = "{0:0}"
    }

    let transparency = {
        value   = 127.0
        min     = 0.0
        max     = 255.0
        step    = 1.0
        format  = "{0:0}"
    }

   