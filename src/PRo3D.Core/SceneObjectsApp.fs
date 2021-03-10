namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.VRVis
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open Aardvark.SceneGraph.IO
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.Base.Rendering
open Aardvark.UI.Trafos  
open PRo3D.Core.Surface

open System
open System.IO
open System.Diagnostics

open Aardvark.UI.Primitives
//open CSharpUtils

type SceneObjectAction =
    | FlyToSO               of Guid
    | RemoveSO              of Guid
    | OpenFolder            of Guid
    | IsVisible             of Guid
    | SelectSO              of Guid
    | PlaceSO               of V3d
    | TranslationMessage    of TranslationApp.Action
    | PlaceSceneObject      of V3d

module SceneObjectsUtils = 

    let mk (path : string) =  
            {
                version         = SceneObject.current
                guid            = Guid.NewGuid()
                name            = System.IO.Path.GetFileName path
                importPath      = path
               
                isVisible       = true
                position        = V3d.Zero       
                scaling         = InitSceneObjectParams.scaling
                transformation  = InitSceneObjectParams.transformations
                preTransform    = Trafo3d.Identity 
            }

    let loadSceneObject (sObject : SceneObject) : SgSurface =
        Log.line "[SceneObject] Please wait while the file is being loaded." 

        let sceneObj = 
            Loader.Assimp.load sObject.importPath

        let bb = sceneObj.bounds
        let pose = Pose.translate bb.Center //Pose.translate V3d.Zero // sceneObj.Center V3d.Zero
        let trafo = { TrafoController.initial with pose = pose; previewTrafo = Pose.toTrafo pose; mode = TrafoMode.Local }
        
        let sg =
            sceneObj
                |> Sg.adapter
                // flip the z coordinates (since the model is upside down)
                |> Sg.transform (Trafo3d.Scale(1.0, 1.0, -1.0))
                //|> Sg.requirePicking
                |> Sg.noEvents

        {
            surface     = sObject.guid    
            trafo       = trafo
            globalBB    = bb
            sceneGraph  = sg
            picking     = Picking.NoPicking //KdTree(kdTrees |> HashMap.ofList) //Picking.PickMesh meshes
        }

             
    let createSgSceneObjects sceneObject =
        let sghs =
          sceneObject
            |> IndexList.toList 
            |> List.map loadSceneObject

        let sgSceneObjects =
            sghs 
              |> List.map (fun d -> (d.surface, d))
              |> HashMap.ofList       

        sgSceneObjects

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
        
        let originTrafo = -pivot |> Trafo3d.Translation
        
        (originTrafo * rot * originTrafo.Inverse * refSysRotation.Inverse * trans * refSysRotation)
           
    
    let fullTrafo (tansform : AdaptiveTransformations) (refsys : ReferenceSystem) = 
        adaptive {
           let! translation = tansform.translation.value
           let! yaw = tansform.yaw.value
           let! pivot = tansform.pivot
            
           return fullTrafo'' translation yaw pivot refsys
        }

    let fullTrafo' (tansform : Transformations) (refsys : ReferenceSystem) = 
        let translation = tansform.translation.value
        let yaw = tansform.yaw.value
        let pivot = tansform.pivot
            
        fullTrafo'' translation yaw pivot refsys
        

module SceneObjectsApp = 

    let update 
        (model : SceneObjectsModel) 
        (act : SceneObjectAction) 
        (refSys    : ReferenceSystem) = 

        match act with
        | IsVisible id ->
            let sceneObjects =  
                model.sceneObjects 
                |> HashMap.alter id (function None -> None | Some o -> Some { o with isVisible = not o.isVisible })
            { model with sceneObjects = sceneObjects }
        | RemoveSO id -> 
            let selSO = 
                match model.selectedSceneObject with
                | Some so -> if so.guid = id then None else Some so
                | None -> None

            let sceneObjects = HashMap.remove id model.sceneObjects
            { model with sceneObjects = sceneObjects; selectedSceneObject = selSO }
        | OpenFolder id ->
            let so = model.sceneObjects |> HashMap.find id
            let test = Path.GetDirectoryName so.importPath
            match File.Exists(so.importPath) with
            | true -> 
                Process.Start("explorer.exe", test) |> ignore
                model
            | false -> model
        | SelectSO id ->
            let so = model.sceneObjects |> HashMap.tryFind id
            match so, model.selectedSceneObject with
            | Some a, Some b -> 
                if a.guid = b.guid then 
                    { model with selectedSceneObject = None }
                else 
                    { model with selectedSceneObject = Some a }
            | Some a, None -> 
                { model with selectedSceneObject = Some a }
            | None, _ -> model
        | TranslationMessage msg ->  
            match model.selectedSceneObject with
            | Some so -> 
                //let t =  { so.transformation with pivot = refSys.origin }
                let transformation' = (TranslationApp.update so.transformation msg)
                let selSO = { so with transformation = transformation' }
                let sceneObjs = model.sceneObjects |> HashMap.alter so.guid (function | Some _ -> Some selSO | None -> None )
                { model with sceneObjects = sceneObjs; selectedSceneObject = (Some selSO) }
            | None -> model
        | PlaceSceneObject p ->
            match model.selectedSceneObject with
            | Some sel -> 
                match model.sceneObjects.TryFind(sel.guid) with 
                | Some so -> 
                    let sceneObjs = 
                        model.sceneObjects 
                        |> HashMap.alter so.guid (function | Some _ -> Some { so with preTransform = Trafo3d.Translation(p) } | None -> None ) 
                    let sgs' = 
                        model.sgSceneObjects
                        |> HashMap.update sel.guid (fun x -> 
                            match x with 
                            | Some sg ->    
                                let pose = Pose.translate p 
                                let trafo' = { 
                                  TrafoController.initial with 
                                    pose = pose
                                    previewTrafo = Trafo3d.Translation(p)
                                    mode = TrafoMode.Local 
                                }
                                { sg with trafo = trafo'; globalBB = (sg.globalBB.Transformed trafo'.previewTrafo) } 
                            | None   -> failwith "scene object not found")                             
                    { model with sgSceneObjects = sgs'; sceneObjects = sceneObjs} 
                | None -> model
            | None -> model
        |_-> model


    module UI =

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
                              | AdaptiveSome sel -> 
                                AVal.constant (if (sel.guid |> AVal.force) = (so.guid |> AVal.force) then C4b.VRVisGreen else C4b.Gray) 
                              | AdaptiveNone -> AVal.constant C4b.Gray

                        let headerText = 
                            AVal.map (fun a -> sprintf "%s" a) so.name

                        let headerAttributes =
                            amap {
                                yield onClick (fun _ -> SelectSO soid)
                            } 
                            |> AttributeMap.ofAMap
            
                       // let bgc = sprintf "color: %s" (Html.ofC4b color)
                        yield div [clazz "item"; style infoc] [
                            div [clazz "content"; style infoc] [                     
                                yield Incremental.div (AttributeMap.ofList [style infoc])(
                                    alist {
                                        let! c = color
                                        let headerC = c |> Html.ofC4b |> sprintf "color: %s"
                                        yield div[clazz "header"; style headerC][
                                            Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                                         ]                
                                        //yield i [clazz "large cube middle aligned icon"; style bgc; onClick (fun _ -> SelectSO soid)][]           
            
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToSO soid) ][]
                                            |> UI.wrapToolTip DataPosition.Bottom "Fly to scene object"                                                     
            
                                        yield i [clazz "folder icon"; onClick (fun _ -> OpenFolder soid) ][] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Open Folder"                             
            
                                        yield Incremental.i toggleMap AList.empty 
                                        |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                        yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveSO soid) ][] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Remove"     
                                       
                                    } 
                                )                                     
                            ]
                        ]
                } )

        let viewTranslationTools (model:AdaptiveSceneObjectsModel) =
            adaptive {
                let! guid = model.selectedSceneObject
                let empty = div[ style "font-style:italic"][ text "no scene object selected" ] |> UI.map TranslationMessage 

                match guid with
                  | AdaptiveSome i -> 
                    let! id = i.guid
                    let! so = model.sceneObjects |> AMap.tryFind id
                    match so with
                    | Some s -> return (TranslationApp.UI.view s.transformation |> UI.map TranslationMessage)
                    | None -> return empty
                  | AdaptiveNone -> return empty
            }  

    module Sg =

        let viewSingleSceneObject 
            (sgSurf : AdaptiveSgSurface) 
            (sceneObj : amap<Guid,AdaptiveSceneObject>) 
            (refsys : AdaptiveReferenceSystem) 
            (selected : aval<AdaptiveOptionCase<SceneObject,AdaptiveSceneObject,AdaptiveSceneObject>>) =

            adaptive {
                let! exists = (sceneObj |> AMap.keys) |> ASet.contains sgSurf.surface
                if exists then
                  
                    let sceneObj = sceneObj |> AMap.find sgSurf.surface
                    let! so = sceneObj

                    let! selected' = selected
                    let selected =
                        match selected' with
                        | AdaptiveSome sel -> (sel.guid = so.guid)
                        | AdaptiveNone -> false

                    let trafo =
                        adaptive {
                            let! s = sceneObj
                            let! rSys = refsys.Current
                            let! fullTrafo = SceneObjectTransformations.fullTrafo s.transformation rSys

                            let! t = s.preTransform
                            
                            let! sc = s.scaling.value
                            return Trafo3d.Scale(sc) * (t * fullTrafo)
                        }

                    let! sgSObj = sgSurf.sceneGraph
                        
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
    
    