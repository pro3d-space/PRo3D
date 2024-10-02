namespace PRo3D.Core

open System
open System.IO

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open Aardvark.SceneGraph.Assimp
open Aardvark.Rendering
open Aardvark.UI.Trafos  
open PRo3D.Core.Surface


open PRo3D.Base
//open CSharpUtils

type SceneObjectAction =
    | FlyToSO               of Guid
    | RemoveSO              of Guid
    | IsVisible             of Guid
    | SelectSO              of Guid
    | PlaceSO               of V3d
    | TranslationMessage    of TransformationApp.Action
    | PlaceSceneObject      of V3d
    | ChangeSOImportDirectories of list<string>


module SceneObjectTransformations = 

    let fullTrafo'' (translation : V3d) (yaw : float) (pivot : V3d) (refsys : ReferenceSystem) = 
        let north = refsys.northO.Normalized
        
        let up = refsys.up.value.Normalized
        let east   = north.Cross(up)
              
        let refSysRotation = 
            Trafo3d.FromOrthoNormalBasis(north, east, up)
            
        //translation along north, east, up            
        let trans = translation |> Trafo3d.Translation
        let rot = Trafo3d.Rotation(up, yaw.RadiansFromDegrees())
        
        let originTrafo = -pivot |> Trafo3d.Translation //
        
        (originTrafo * rot * originTrafo.Inverse * refSysRotation.Inverse * trans * refSysRotation)
           
    
    let fullTrafo (tansform : AdaptiveTransformations) (refsys : ReferenceSystem) = 
        adaptive {
           let! translation = tansform.translation.value
           let! yaw = tansform.yaw.value
           let! pivot = tansform.pivot.value
            
           return fullTrafo'' translation yaw pivot refsys
        }

    let fullTrafo' (tansform : Transformations) (refsys : ReferenceSystem) = 
        let translation = tansform.translation.value
        let yaw = tansform.yaw.value
        let pivot = tansform.pivot.value
            
        fullTrafo'' translation yaw pivot refsys

module SceneObjectsUtils = 

    let mk (path : string) =  
            {
                version         = SceneObject.current
                guid            = Guid.NewGuid()
                name            = System.IO.Path.GetFileName path
                importPath      = path
               
                isVisible       = true
                position        = V3d.Zero       
                //scaling         = InitSceneObjectParams.scaling
                transformation  = InitSceneObjectParams.transformations
                preTransform    = Trafo3d.Identity 
            }

    let loadSceneObject (sObject : SceneObject) : SgSurface =
        Log.line "[SceneObject] Please wait while the file is being loaded." 

        let sceneObj = 
            Loader.Assimp.load sObject.importPath

        let bb = sceneObj.bounds//.Transformed(sObject.preTransform) 
        let pose = Pose.translate V3d.Zero // Pose.translate bb.Center // sceneObj.Center V3d.Zero
        let trafo = { TrafoController.initial with pose = pose; previewTrafo = Pose.toTrafo pose; mode = TrafoMode.Local }

        let filetype = Path.GetExtension sObject.name
        
        let sg =
            match filetype with
            | ".dae" ->
                sceneObj
                |> Sg.adapter
                // flip the z coordinates (since the model is upside down)
                |> Sg.transform (Trafo3d.Scale(1.0, 1.0, -1.0))
            | _ -> 
                sceneObj
                |> Sg.adapter
                
            //|> Sg.requirePicking
            |> Sg.noEvents

        {
            surface     = sObject.guid    
            trafo       = trafo
            globalBB    = bb
            sceneGraph  = sg
            picking     = Picking.NoPicking 
            isObj       = true 
            opcScene    = None
        }

             
    let createSgSceneObjects sceneObject =
        let sghs =
          sceneObject
            |> IndexList.toList 
            |> List.filter(fun (so : SceneObject) ->
                let dirExists = File.Exists so.importPath
                if dirExists |> not then 
                    Log.error "[SceneObject.Sg] could not find %s" so.importPath
                dirExists
            )
            |> List.map loadSceneObject

        let sgSceneObjects =
            sghs 
              |> List.map (fun d -> (d.surface, d))
              |> HashMap.ofList       

        sgSceneObjects


module SceneObjectsApp = 

    let getFolderForObject (model : SceneObjectsModel) (id : Guid) =
        match HashMap.tryFind id model.sceneObjects with
        | None -> None
        | Some sceneObject -> 
            if File.Exists(sceneObject.importPath) then 
                Some sceneObject.importPath
            else 
                None

    let changeSOImportDirectories (model:SceneObjectsModel) (selectedPaths:list<string>) =
        let sceneObjs =        
            model.sceneObjects
            |> HashMap.map(fun id so -> 
                let newPath = 
                    selectedPaths
                    |> List.map(fun p -> 
                        let name = p |> IO.Path.GetFileName
                        match name = so.name with
                        | true -> Some p
                        | false -> None
                    )
                    |> List.choose( fun np -> np) 
                match newPath.IsEmpty with
                | true -> so
                | false -> { so with importPath = newPath.Head } 
            )
              
        { model with sceneObjects = sceneObjs }

    let update 
        (model : SceneObjectsModel) 
        (act : SceneObjectAction) 
        (refSys : ReferenceSystem) = 

        match act with
        | IsVisible id ->
            let sceneObjects =  
                model.sceneObjects 
                |> HashMap.alter id (function None -> None | Some o -> Some { o with isVisible = not o.isVisible })
            { model with sceneObjects = sceneObjects }
        | RemoveSO id -> 
            let selSO = 
                match model.selectedSceneObject with
                | Some so -> if so = id then None else Some so
                | None -> None

            let sceneObjects = HashMap.remove id model.sceneObjects
            { model with sceneObjects = sceneObjects; selectedSceneObject = selSO }
        | SelectSO id ->
            let so = model.sceneObjects |> HashMap.tryFind id
            match so, model.selectedSceneObject with
            | Some a, Some b ->
                if a.guid = b then 
                    { model with selectedSceneObject = None }
                else 
                    { model with selectedSceneObject = Some a.guid }
            | Some a, None -> 
                { model with selectedSceneObject = Some a.guid }
            | None, _ -> model
        | TranslationMessage msg ->  
            match model.selectedSceneObject with
            | Some id -> 
                let sobj = model.sceneObjects |> HashMap.tryFind id
                match sobj with
                | Some so ->
                    let transformation' = (TransformationApp.update so.transformation msg refSys)
                    let selSO = { so with transformation = transformation' }
                    let sceneObjs = model.sceneObjects |> HashMap.alter so.guid (function | Some _ -> Some selSO | None -> None )
                    { model with sceneObjects = sceneObjs} 
                | None -> model
            | None -> model
        | PlaceSceneObject p ->
            match model.selectedSceneObject with
            | Some sel -> 
                match model.sceneObjects.TryFind(sel) with 
                | Some so -> 

                    //reset gui transformation (keep only yaw)
                    let so' = { so with preTransform = Trafo3d.Translation(p); 
                                        transformation = {InitSceneObjectParams.transformations with yaw = so.transformation.yaw} }
                    let sceneObjs = 
                        model.sceneObjects 
                        |> HashMap.alter so.guid (function | Some _ -> Some so' | None -> None ) 
                            
                    { model with sceneObjects = sceneObjs} 
                | None -> model
            | None -> model
        | ChangeSOImportDirectories sl ->
            match sl with
            | [] -> model
            | paths ->
                let selectedPaths = paths |> List.choose( fun p -> if File.Exists p then Some p else None)
                changeSOImportDirectories model selectedPaths  
        |_-> model
          


    module UI =

        let getOpenFolderAttributes (so : AdaptiveSceneObject) =
            amap {
                yield clazz "folder icon"
                let! path = so.importPath
                yield clientEvent "onclick" (Electron.openPath path)
            } |>  AttributeMap.ofAMap

        let viewHeader (m:AdaptiveSceneObject) (soid:Guid) toggleMap= 
            [
                Incremental.text m.name; text " "

                i [clazz "home icon"; onClick (fun _ -> FlyToSO soid)] []
                |> UI.wrapToolTip DataPosition.Bottom "Fly to scene object"                                                     
            
                Incremental.i (getOpenFolderAttributes m) AList.empty
                |> UI.wrapToolTip DataPosition.Bottom "Open Folder"                             
            
                Incremental.i toggleMap AList.empty 
                |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                i [clazz "Remove icon red"; onClick (fun _ -> RemoveSO soid) ] [] 
                |> UI.wrapToolTip DataPosition.Bottom "Remove"     
            ]    


        let viewSceneObjects
            (m : AdaptiveSceneObjectsModel) =

            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div itemAttributes (
                alist {

                    let! selected = m.selectedSceneObject
                    let sceneObjects = m.sceneObjects |> AMap.toASetValues |> ASet.toAList// (fun a -> )
        
                    for so in sceneObjects do
            
                        let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
            
                        let! soid = so.guid  
                        let toggleIcon = 
                            AVal.map( fun toggle -> if toggle then "unhide icon" else "hide icon") so.isVisible

                        let toggleMap = 
                            amap {
                                let! toggleIcon = toggleIcon
                                yield clazz toggleIcon
                                yield onClick (fun _ -> IsVisible soid)
                            } |> AttributeMap.ofAMap  

                        let color =
                            match selected with
                              | Some sel -> 
                                AVal.constant (if sel = (so.guid |> AVal.force) then C4b.VRVisGreen else C4b.Gray) 
                              | None -> AVal.constant C4b.Gray
                            

                        let headerText = 
                            AVal.map (fun a -> sprintf "%s" a) so.name

                        let headerAttributes =
                            amap {
                                yield onClick (fun _ -> SelectSO soid)
                            } 
                            |> AttributeMap.ofAMap

                        let! c = color
                        let bgc = sprintf "color: %s" (Html.ofC4b c)

                
                                     
                        yield Incremental.div (AttributeMap.ofList [style infoc])(
                            alist {
                                yield 
                                    div [clazz "header"; style bgc] [
                                        Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                                    ]            
            
                                yield i [clazz "home icon"; onClick (fun _ -> FlyToSO soid)] []
                                    |> UI.wrapToolTip DataPosition.Bottom "Fly to scene object"                                                     
            
                                yield Incremental.i (getOpenFolderAttributes so) AList.empty
                                    |> UI.wrapToolTip DataPosition.Bottom "Open Folder"                             
            
                                yield Incremental.i toggleMap AList.empty 
                                    |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveSO soid)] [] 
                                    |> UI.wrapToolTip DataPosition.Bottom "Remove"     
                                       
                            } 
                        )    
                } )

        let viewTranslationTools (model:AdaptiveSceneObjectsModel) =
            adaptive {
                let! guid = model.selectedSceneObject
                let empty = div [style "font-style:italic"] [text "no scene object selected"] |> UI.map TranslationMessage 

                match guid with
                | Some id -> 
                  let! so = model.sceneObjects |> AMap.tryFind id
                  match so with
                  | Some s -> return (TransformationApp.UI.view s.transformation |> UI.map TranslationMessage)
                  | None -> return empty
                | None -> return empty
            }  

    module Sg =

        let viewSingleSceneObject 
            (sgSurf     : AdaptiveSgSurface) 
            (sceneObjs  : amap<Guid,AdaptiveSceneObject>) 
            (refsys     : AdaptiveReferenceSystem) 
            (selected   : aval<Option<Guid>>) =

            adaptive {
                let! exists = (sceneObjs |> AMap.keys) |> ASet.contains sgSurf.surface
                if exists then
                  
                    let sceneObj = sceneObjs |> AMap.find sgSurf.surface
                    let! so = sceneObj

                    let! selected' = selected
                    let selected =
                        match selected' with
                        | Some sel -> sel = (so.guid |> AVal.force)
                        | None -> false

                    let trafo =
                        adaptive {
                            let! s = sceneObj
                            let! rSys = refsys.Current
                            let! t = s.preTransform
                            let! so = s.Current
                            let fullTrafo = TransformationApp.fullTrafo' so.transformation rSys 
                            
                            //let! sc = s.transformation.scaling.value // s.scaling.value
                            let! flipZ = s.transformation.flipZ
                            let! sketchFab = s.transformation.isSketchFab

                            if flipZ then 
                                return Trafo3d.Scale(1.0, 1.0, -1.0) * (fullTrafo * t)
                            else if sketchFab then
                                return Trafo3d.Scale(1.0, 1.0, -1.0) * (fullTrafo * t)
                            else
                                return (fullTrafo * t) //(t * fullTrafo)
                        }

                    let! sgSObj = sgSurf.sceneGraph
                    let! bb = sgSurf.globalBB
                    let bbTest = trafo |> AVal.map(fun t -> bb.Transformed(t))
                        
                    let surfaceSg =
                        sgSObj
                        |> Sg.noEvents 
                        |> Sg.trafo trafo 
                        |> Sg.noEvents 
                        |> Sg.shader {
                            do! Shader.stableTrafo
                            do! DefaultSurfaces.diffuseTexture
                        }
                        |> Sg.onOff so.isVisible
                        |> Sg.andAlso (
                            (Sg.wireBox (C4b.VRVisGreen |> AVal.constant) bbTest) 
                            |> Sg.noEvents
                            |> Sg.effect [              
                                Shader.stableTrafo |> toEffect 
                                DefaultSurfaces.vertexColor |> toEffect
                            ] 
                            |> Sg.onOff (selected |> AVal.constant)
                            )
                        |> Sg.andAlso ( 
                            // pivot point
                            so.transformation 
                            |> TransformationApp.Sg.view
                            )     
                                                        
                    return surfaceSg
                else
                    return Sg.empty
            } |> Sg.dynamic

        let view (sceneObjectsModel : AdaptiveSceneObjectsModel) (refsys : AdaptiveReferenceSystem) =
            let sgSObjs = sceneObjectsModel.sgSceneObjects
            let sceneObjs = sceneObjectsModel.sceneObjects
            let selected = sceneObjectsModel.selectedSceneObject

            let test =
                sgSObjs |> AMap.map( fun id sgsobj ->
                    viewSingleSceneObject
                        sgsobj
                        sceneObjs
                        refsys
                        selected
                    )
                    |> AMap.toASet 
                    |> ASet.map snd 
                    |> Sg.set
                  
            test
    
    