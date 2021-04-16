namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.Rendering
open Aardvark.VRVis
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open Aardvark.SceneGraph.IO
open Aardvark.SceneGraph.SgPrimitives
//open Aardvark.Base.Rendering
open Aardvark.UI.Trafos  
open PRo3D.Core.Surface
open PRo3D.Core.Drawing

open System
open System.IO
open System.Diagnostics

open Aardvark.UI.Primitives

open PRo3D.Base
//open CSharpUtils

module GeologicSurfacesUtils = 

    let mk  (name : string) 
            (points1 : IndexList<V3d> )
            (points2 : IndexList<V3d> ) 
            (view : CameraView) =  
            {
                version         = GeologicSurface.current
                guid            = Guid.NewGuid()
                name            = name
               
                isVisible       = true
                view            = view //FreeFlyController.initial.view

                points1         = points1  
                points2         = points2  

                color           = { c = C4b.Cyan }
                transparency    = InitGeologicSurfacesParams.transparency
                thickness       = InitGeologicSurfacesParams.thickness

                sgGeoSurface    = Sg.empty
            }

    let makeGeologicSurfaceFromAnnotations 
        (annotations : GroupsModel) 
        (model : GeologicSurfacesModel) =
        
        let selection = annotations.selectedLeaves
        if selection.Count = 2 then
            let ids = 
                selection 
                |> HashSet.map(fun x -> x.id)
                |> HashSet.toList
                
            let geoSurf = 
                match (annotations.flat.TryFind ids.[0]), (annotations.flat.TryFind ids.[1]) with
                | Some (Leaf.Annotations ann1), Some (Leaf.Annotations ann2) ->
                    Some (mk 
                            ("mesh"+ model.geologicSurfaces.Count.ToString())
                            ann1.points
                            ann2.points
                            ann1.view )
                | _,_-> None

            match geoSurf with
            | Some gs ->
                { model with geologicSurfaces = model.geologicSurfaces |> HashMap.add gs.guid gs; 
                             selectedGeologicSurface = Some gs.guid }
            | None -> model
        else
            failwith "select two annotations"
            model

    
module GeologicSurfaceProperties =        

    type Action =
        | SetName           of string
        | ToggleVisible 
        | SetThickness      of Numeric.Action
        | SetTransparency   of Numeric.Action
        | ChangeColor       of ColorPicker.Action

    let update (model : GeologicSurface) (act : Action) =
        match act with
        | SetName s ->
            { model with name = s }
        | ToggleVisible ->
            { model with isVisible = not model.isVisible }
        | SetThickness a ->
            { model with thickness = Numeric.update model.thickness a}
        | SetTransparency a ->
            { model with transparency = Numeric.update model.transparency a}
        | ChangeColor a ->
            { model with color = ColorPicker.update model.color a }
          
    let view (model : AdaptiveGeologicSurface) =        
      require GuiEx.semui (
        Html.table [               
          Html.row "Name:"          [Html.SemUi.textBox model.name SetName ]
          Html.row "Visible:"       [GuiEx.iconCheckBox model.isVisible ToggleVisible ]
          Html.row "Thickness:"     [Numeric.view' [NumericInputType.Slider]   model.thickness  |> UI.map SetThickness ]
          Html.row "Transparency:"  [Numeric.view' [NumericInputType.Slider]   model.transparency  |> UI.map SetTransparency ]
          Html.row "Color:"         [ColorPicker.view model.color |> UI.map ChangeColor ]
        ]
      )
 
type GeologicSurfaceAction =
    | FlyToGS               of Guid
    | RemoveGS              of Guid
    | IsVisible             of Guid
    | SelectGS              of Guid
    | AddGS                 
    | PropertiesMessage     of GeologicSurfaceProperties.Action

module GeologicSurfacesApp = 

    let update 
        (model : GeologicSurfacesModel) 
        (act : GeologicSurfaceAction) = 

        match act with
        | IsVisible id ->
            let geologicSurfaces =  
                model.geologicSurfaces 
                |> HashMap.alter id (function None -> None | Some o -> Some { o with isVisible = not o.isVisible })
            { model with geologicSurfaces = geologicSurfaces }
        | RemoveGS id -> 
            let selGS = 
                match model.selectedGeologicSurface with
                | Some so -> if so = id then None else Some so
                | None -> None

            let geologicSurfaces = HashMap.remove id model.geologicSurfaces
            { model with geologicSurfaces = geologicSurfaces; selectedGeologicSurface = selGS }
        
        | SelectGS id ->
            let so = model.geologicSurfaces |> HashMap.tryFind id
            match so, model.selectedGeologicSurface with
            | Some a, Some b ->
                if a.guid = b then 
                    { model with selectedGeologicSurface = None }
                else 
                    { model with selectedGeologicSurface = Some a.guid }
            | Some a, None -> 
                { model with selectedGeologicSurface = Some a.guid }
            | None, _ -> model
        | PropertiesMessage msg ->  
            match model.selectedGeologicSurface with
            | Some id -> 
                let geoSurf = model.geologicSurfaces |> HashMap.tryFind id
                match geoSurf with
                | Some gs ->
                    let geoSurf = (GeologicSurfaceProperties.update gs msg)
                    let geologicSurfaces = model.geologicSurfaces |> HashMap.alter gs.guid (function | Some _ -> Some geoSurf | None -> None )
                    { model with geologicSurfaces = geologicSurfaces} 
                | None -> model
            | None -> model
        |_-> model


    module UI =

        let viewHeader (m:AdaptiveGeologicSurface) (gsid:Guid) toggleMap = 
            [
                Incremental.text m.name; text " "

                i [clazz "home icon"; onClick (fun _ -> FlyToGS gsid) ][]
                |> UI.wrapToolTip DataPosition.Bottom "Fly to geologic surface" 

                Incremental.i toggleMap AList.empty 
                |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                i [clazz "Remove icon red"; onClick (fun _ -> RemoveGS gsid) ][] 
                |> UI.wrapToolTip DataPosition.Bottom "Remove"     
            ]    


        let viewGeologicSurfaces
            (m : AdaptiveGeologicSurfacesModel) =

            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div itemAttributes (
                alist {

                    let! selected = m.selectedGeologicSurface
                    let geologicSurfaces = m.geologicSurfaces |> AMap.toASetValues |> ASet.toAList// (fun a -> )
        
                    for gs in geologicSurfaces do
            
                        let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
            
                        let! scbid = gs.guid  
                        let toggleIcon = 
                            AVal.map( fun toggle -> if toggle then "unhide icon" else "hide icon") gs.isVisible

                        let toggleMap = 
                            amap {
                                let! toggleIcon = toggleIcon
                                yield clazz toggleIcon
                                yield onClick (fun _ -> IsVisible scbid)
                            } |> AttributeMap.ofAMap  

                       
                        let color =
                            match selected with
                              | Some sel -> 
                                AVal.constant (if sel = (gs.guid |> AVal.force) then C4b.VRVisGreen else C4b.Gray) 
                              | None -> AVal.constant C4b.Gray

                        let headerText = 
                            AVal.map (fun a -> sprintf "%s" a) gs.name

                        let headerAttributes =
                            amap {
                                yield onClick (fun _ -> SelectGS scbid)
                            } 
                            |> AttributeMap.ofAMap
            
                        let! c = color
                        let bgc = sprintf "color: %s" (Html.ofC4b c)
                        yield div [clazz "item"; style infoc] [
                            div [clazz "content"; style infoc] [                     
                                yield Incremental.div (AttributeMap.ofList [style infoc])(
                                    alist {
                                        //let! hc = headerColor
                                        yield div[clazz "header"; style bgc][
                                            Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                                         ]                
                                        //yield i [clazz "large cube middle aligned icon"; style bgc; onClick (fun _ -> SelectSO soid)][]           
            
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToGS scbid) ][]
                                            |> UI.wrapToolTip DataPosition.Bottom "Fly to geologic surface"          
            
                                        yield Incremental.i toggleMap AList.empty 
                                        |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                        yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveGS scbid) ][] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Remove"     
                                       
                                    } 
                                )                                     
                            ]
                        ]
                } )

        let viewProperties (model:AdaptiveGeologicSurfacesModel) =
            adaptive {
                let! guid = model.selectedGeologicSurface
                let empty = div[ style "font-style:italic"][ text "no geologic surface selected" ] |> UI.map PropertiesMessage 
                
                match guid with
                | Some id -> 
                    let! gs = model.geologicSurfaces |> AMap.tryFind id
                    match gs with
                    | Some s -> return (GeologicSurfaceProperties.view s |> UI.map PropertiesMessage)
                    | None -> return empty
                | None -> return empty
            } 
            
        let addMesh = 
            div [clazz "ui buttons inverted"] [
                       //onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                           button [clazz "ui icon button"; onMouseClick (fun _ -> AddGS )] [ //
                                   i [clazz "plus icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Add Bookmark"
                      // )
                   ] 

    //module Sg =

    //    let viewSingleSceneObject 
    //        (sgSurf : AdaptiveSgSurface) 
    //        (sceneObjs : amap<Guid,AdaptiveSceneObject>) 
    //        (refsys : AdaptiveReferenceSystem) 
    //        (selected : aval<Option<Guid>>) =

    //        adaptive {
    //            let! exists = (sceneObjs |> AMap.keys) |> ASet.contains sgSurf.surface
    //            if exists then
                  
    //                let sceneObj = sceneObjs |> AMap.find sgSurf.surface
    //                let! so = sceneObj

    //                let! selected' = selected
    //                let selected =
    //                    match selected' with
    //                    | Some sel -> sel = (so.guid |> AVal.force)
    //                    | None -> false

    //                let trafo =
    //                    adaptive {
    //                        let! s = sceneObj
    //                        let! rSys = refsys.Current
    //                        let! t = s.preTransform
    //                        let! fullTrafo = SceneObjectTransformations.fullTrafo s.transformation rSys
                            
    //                        let! sc = s.scaling.value
    //                        return Trafo3d.Scale(sc) * (fullTrafo * t) //(t * fullTrafo)
    //                    }

    //                let! sgSObj = sgSurf.sceneGraph
    //                let! bb = sgSurf.globalBB
    //                let bbTest = trafo |> AVal.map(fun t -> bb.Transformed(t))
                        
    //                let surfaceSg =
    //                    sgSObj
    //                    |> Sg.noEvents 
    //                    |> Sg.trafo trafo 
    //                    |> Sg.noEvents 
    //                    |> Sg.shader {
    //                        do! Shader.stableTrafo
    //                        do! DefaultSurfaces.diffuseTexture
    //                    }
    //                    |> Sg.onOff so.isVisible
    //                    |> Sg.andAlso (
    //                        (Sg.wireBox (C4b.VRVisGreen |> AVal.constant) bbTest) 
    //                        |> Sg.noEvents
    //                        |> Sg.effect [              
    //                            Shader.stableTrafo |> toEffect 
    //                            DefaultSurfaces.vertexColor |> toEffect
    //                        ] 
    //                        |> Sg.onOff (selected |> AVal.constant)
    //                        )                      
                                                        
    //                return surfaceSg
    //            else
    //                return Sg.empty
    //        } |> Sg.dynamic

    //    let view (sceneObjectsModel : AdaptiveSceneObjectsModel) (refsys : AdaptiveReferenceSystem) =
    //        let sgSObjs = sceneObjectsModel.sgSceneObjects
    //        let sceneObjs = sceneObjectsModel.sceneObjects
    //        let selected = sceneObjectsModel.selectedSceneObject

    //        let test =
    //            sgSObjs |> AMap.map( fun id sgsobj ->
    //                viewSingleSceneObject
    //                    sgsobj
    //                    sceneObjs
    //                    refsys
    //                    selected
    //                )
    //                |> AMap.toASet 
    //                |> ASet.map snd 
    //                |> Sg.set
                  
    //        test
    
    