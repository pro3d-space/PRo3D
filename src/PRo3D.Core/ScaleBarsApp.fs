namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.VRVis
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open Aardvark.SceneGraph.IO
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.Rendering
open Aardvark.UI.Trafos  
open PRo3D.Core.Surface

open System
open System.IO
open System.Diagnostics

open Aardvark.UI.Primitives

open OpcViewer.Base

open PRo3D.Base


type ScaleBarDrawingAction =
    | SetOrientation of Orientation
    | SetPivot       of Pivot
    | SetUnit        of PRo3D.Core.Unit
    | SetLength      of Numeric.Action


module ScaleBarsDrawing =
    let update 
        (model : ScaleBarDrawing) 
        (act : ScaleBarDrawingAction) = 

        match act with
        | SetOrientation mode ->
            { model with orientation = mode } 
        | SetUnit mode ->
            { model with unit = mode } 
        | SetPivot mode ->
            { model with alignment = mode } 
        | SetLength a -> 
            let rLength = Numeric.update model.length a
            { model with length = { rLength with value = Math.Round(rLength.value) } }
        | _-> model

    module UI =

        let viewScaleBarToolsHorizontal (model:AdaptiveScaleBarDrawing) =
            Html.Layout.horizontal [
                Html.Layout.boxH [ i [clazz "large Write icon"][] ]
                Html.Layout.boxH [ Html.SemUi.dropDown model.orientation SetOrientation ]
                Html.Layout.boxH [ Html.SemUi.dropDown model.alignment SetPivot ]
                Html.Layout.boxH [ Numeric.view' [InputBox] model.length |> UI.map SetLength ]  
                Html.Layout.boxH [ Html.SemUi.dropDown model.unit SetUnit ]
            ]

module ScaleBarProperties =        

    type Action =
        | SetName        of string
        | SetTextsize    of Numeric.Action
        | ToggleTextVisible 
        | ToggleVisible 
        | SetLength      of Numeric.Action
        | SetThickness   of Numeric.Action
        | SetOrientation of Orientation
        | SetUnit        of PRo3D.Core.Unit
        | SetSubdivisions of Numeric.Action
        | TogglePriority
        | ToggleShowPivot
        | SetPivotSize   of Numeric.Action

    let update (model : ScaleBar) (act : Action) =
        match act with
        | SetName s ->
            { model with name = s }
        | SetTextsize s ->
            { model with textsize = Numeric.update model.textsize s}
        | ToggleTextVisible ->
            { model with textVisible = not model.textVisible }
        | ToggleVisible ->
            { model with isVisible = not model.isVisible }
        | SetLength a ->
            let rLength = Numeric.update model.length a
            let text' = Math.Round(rLength.value).ToString() + model.unit.ToString()
            { model with length = { rLength with value = Math.Round(rLength.value) }; text = text'}
        | SetThickness a ->
            { model with thickness = Numeric.update model.thickness a}
        | SetOrientation mode ->
            { model with orientation = mode } 
        | SetUnit mode ->
            let text' = model.length.value.ToString() + mode.ToString()
            { model with unit = mode; text = text'} 
        | SetSubdivisions a ->
            { model with subdivisions = Numeric.update model.subdivisions a}
        | TogglePriority ->
            { model with priority = not model.priority }
        | ToggleShowPivot ->
            { model with showPivot = not model.showPivot }
        | SetPivotSize a ->
            { model with pivotSize = Numeric.update model.pivotSize a}
          
    let view (model : AdaptiveScaleBar) =        
      require GuiEx.semui (
        Html.table [               
          Html.row "Name:"          [Html.SemUi.textBox model.name SetName ]
          Html.row "Visible:"       [GuiEx.iconCheckBox model.isVisible ToggleVisible ]
          Html.row "Textsize:"      [Numeric.view' [NumericInputType.InputBox] model.textsize |> UI.map SetTextsize ]  
          Html.row "TextVisible:"   [GuiEx.iconCheckBox model.textVisible ToggleTextVisible ]
          Html.row "Length:"        [Numeric.view' [NumericInputType.InputBox] model.length |> UI.map SetLength ]       
          Html.row "Thickness:"     [Numeric.view' [NumericInputType.InputBox]   model.thickness  |> UI.map SetThickness ]
          Html.row "Subdivisions:"  [Numeric.view' [NumericInputType.InputBox]   model.subdivisions  |> UI.map SetSubdivisions ]
          Html.row "Orientation:"   [Html.SemUi.dropDown model.orientation SetOrientation]
          Html.row "Unit:"          [Html.SemUi.dropDown model.unit SetUnit]
          Html.row "Priority:"      [GuiEx.iconCheckBox model.priority TogglePriority ]
          Html.row "PivotVisible:"  [GuiEx.iconCheckBox model.showPivot ToggleShowPivot ]
          Html.row "PivotSize:"     [Numeric.view' [NumericInputType.InputBox]   model.pivotSize  |> UI.map SetPivotSize ]
          //Html.row "Pivot:"         [Html.SemUi.dropDown model.alignment SetPivot]
        ]
      )

type ScaleBarsAction =
    
    | FlyToSB       of Guid
    | RemoveSB      of Guid
    | IsVisible     of Guid
    | SelectSB      of Guid
    | AddScaleBar   of (V3d*ScaleBarDrawing*CameraView)
    | TranslationMessage    of TranslationApp.Action
    | PropertiesMessage     of ScaleBarProperties.Action


module ScaleBarUtils = 

    let getLengthInMeter 
        (unit : PRo3D.Core.Unit)
        (length : float)  =
            match unit with
            | Unit.Undefined  -> length //todo
            | Unit.mm         -> length / 1000.0
            | Unit.cm         -> length / 100.0
            | Unit.m          -> length 
            | Unit.km         -> length * 1000.0
            |_                -> length

    let getDirectionVec 
        (orientation : Orientation) 
        (view : CameraView) =  
            match orientation with
            | Orientation.Horizontal -> view.Right
            | Orientation.Vertical   -> view.Up
            | Orientation.Sky        -> view.Sky
            |_                       -> view.Right

    let getP1
        (position : V3d) 
        (length : float) 
        (direction : V3d)
        (alignment : Pivot) =
            match alignment with
            | Pivot.Left -> position
            | Pivot.Right -> position - direction.Normalized * length
            | Pivot.Middle -> position - direction.Normalized * length * 0.5
            |_-> position

    //let getTextpos = 
    //    adaptive {
    //        let! orientation = scaleBar.orientation
    //        let! view = scaleBar.view
    //        let direction = ScaleBarUtils.getDirectionVec orientation view
    //        let! alignment = scaleBar.alignment
    //        let! position = scaleBar.position
    //        let! length = scaleBar.length.value

    //        match alignment with
    //        | Pivot.Left -> position - 
    //        | Pivot.Right ->
    //        | Pivot.Middle ->
    //    }

    let getSegments 
        (position : V3d) 
        (length : float) 
        (direction : V3d)
        (alignment : Pivot)
        (subdivisions : int) =
            let part = length/(float)subdivisions
            let pos = getP1 position length direction alignment
                
            let mutable segments = IndexList.Empty
            let segs = 
                for seg in 0 .. (subdivisions - 1) do
                    let startP = pos + direction.Normalized * ((float)seg*part)
                    let endP = pos + direction.Normalized * ((float)(seg+1)*part)
            
                    let color = 
                        match seg%2 with
                        | 0 -> C4b.Black
                        | _ -> C4b.White
            
                    let segment = {
                        startPoint = startP
                        endPoint   = endP
                        color      = color
                    }
                    segments <- segments |> IndexList.add segment
            segments

    let updateSegments (scaleBar : ScaleBar) =
        let direction = getDirectionVec scaleBar.orientation scaleBar.view
        let length = getLengthInMeter scaleBar.unit scaleBar.length.value
        getSegments scaleBar.position length direction scaleBar.alignment ((int)scaleBar.subdivisions.value)

    let mkScaleBar 
        (drawing : ScaleBarDrawing) 
        (position : V3d) 
        (view : CameraView) =  

            let subdivisions = InitScaleBarsParams.subdivisions // todo via gui
            let length = getLengthInMeter drawing.unit drawing.length.value
            let direction = getDirectionVec drawing.orientation view
            let segments = getSegments position length direction drawing.alignment ((int)subdivisions.value)
            let text = drawing.length.value.ToString() + drawing.unit.ToString()
            let thickness = length / 20.0

            {
                version         = ScaleBar.current
                guid            = Guid.NewGuid()
                name            = text + " " + drawing.orientation.ToString()
                
                text            = text
                textsize        = InitScaleBarsParams.text
                textVisible     = true
                   
                isVisible       = true
                position        = position // V3d.Zero    
                scSegments      = segments
                orientation     = drawing.orientation
                alignment       = drawing.alignment //Pivot.Left
                thickness       = InitScaleBarsParams.thickness thickness
                length          = drawing.length //{InitScaleBarsParams.length with value = length} //
                unit            = drawing.unit
                subdivisions    = subdivisions
                priority        = true

                showPivot       = true
                pivotSize       = InitScaleBarsParams.pivot
                
                view            = view //FreeFlyController.initial.view
                transformation  = Init.transformations
                preTransform    = Trafo3d.Identity
            }


module ScaleBarTransformations = 

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
        

module ScaleBarsApp = 

    let update 
        (model : ScaleBarsModel) 
        (act : ScaleBarsAction) = 

        match act with
        | IsVisible id ->
            let scaleBars =  
                model.scaleBars 
                |> HashMap.alter id (function None -> None | Some o -> Some { o with isVisible = not o.isVisible })
            { model with scaleBars = scaleBars }
        | RemoveSB id -> 
            let selScB = 
                match model.selectedScaleBar with
                | Some scb -> if scb = id then None else Some scb
                | None -> None

            let scaleBars = HashMap.remove id model.scaleBars
            { model with scaleBars = scaleBars; selectedScaleBar = selScB }
        | SelectSB id ->
            let scb = model.scaleBars |> HashMap.tryFind id
            match scb, model.selectedScaleBar with
            | Some a, Some b -> 
                if a.guid = b then 
                    { model with selectedScaleBar = None }
                else 
                    { model with selectedScaleBar = Some a.guid }
            | Some a, None -> 
                { model with selectedScaleBar = Some a.guid }
            | None, _ -> model

        | AddScaleBar (p,drawing, view) ->
            let scaleBar = ScaleBarUtils.mkScaleBar drawing p view 
            let scaleBars' =  HashMap.add scaleBar.guid scaleBar model.scaleBars
            { model with scaleBars = scaleBars'; selectedScaleBar = Some scaleBar.guid }

        | TranslationMessage msg ->  
            match model.selectedScaleBar with
            | Some id -> 
                let scB = model.scaleBars |> HashMap.tryFind id
                match scB with
                | Some sb ->
                    let transformation' = (TranslationApp.update sb.transformation msg)
                    let sb' = { sb with transformation = transformation' }
                    let scaleBars = model.scaleBars |> HashMap.alter sb.guid (function | Some _ -> Some sb' | None -> None )
                    { model with scaleBars = scaleBars} 
                | None -> model
            | None -> model
        | PropertiesMessage msg ->  
            match model.selectedScaleBar with
            | Some id -> 
                let scB = model.scaleBars |> HashMap.tryFind id
                match scB with
                | Some sb ->
                    let scaleBar = (ScaleBarProperties.update sb msg)
                    let scaleBar' = { scaleBar with scSegments = ScaleBarUtils.updateSegments scaleBar }
                    let scaleBars = model.scaleBars |> HashMap.alter sb.guid (function | Some _ -> Some scaleBar' | None -> None )
                    { model with scaleBars = scaleBars} 
                | None -> model
            | None -> model
       
        |_-> model


    module UI =
        let viewScaleBars
            (m : AdaptiveScaleBarsModel) =

            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div itemAttributes (
                alist {

                    let! selected = m.selectedScaleBar
                    let scaleBars = m.scaleBars |> AMap.toASetValues |> ASet.toAList// (fun a -> )
        
                    for scb in scaleBars do
            
                        let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
            
                        let! scbid = scb.guid  
                        let toggleIcon = 
                            AVal.map( fun toggle -> if toggle then "unhide icon" else "hide icon") scb.isVisible

                        let toggleMap = 
                            amap {
                                let! toggleIcon = toggleIcon
                                yield clazz toggleIcon
                                yield onClick (fun _ -> IsVisible scbid)
                            } |> AttributeMap.ofAMap  

                       
                        let color =
                            match selected with
                              | Some sel -> 
                                AVal.constant (if sel = (scb.guid |> AVal.force) then C4b.VRVisGreen else C4b.Gray) 
                              | None -> AVal.constant C4b.Gray

                        let headerText = 
                            AVal.map (fun a -> sprintf "%s" a) scb.name

                        let headerAttributes =
                            amap {
                                yield onClick (fun _ -> SelectSB scbid)
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
            
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToSB scbid) ][]
                                            |> UI.wrapToolTip DataPosition.Bottom "Fly to scene object"          
            
                                        yield Incremental.i toggleMap AList.empty 
                                        |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                        yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveSB scbid) ][] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Remove"     
                                       
                                    } 
                                )                                     
                            ]
                        ]
                } )

        let viewTranslationTools (model:AdaptiveScaleBarsModel) =
            adaptive {
                let! guid = model.selectedScaleBar
                let empty = div[ style "font-style:italic"][ text "no scene object selected" ] |> UI.map TranslationMessage 

               match guid with
               | Some id -> 
                 let! scB = model.scaleBars |> AMap.tryFind id
                 match scB with
                 | Some s -> return (TranslationApp.UI.view s.transformation |> UI.map TranslationMessage)
                 | None -> return empty
               | None -> return empty
            }  

        let viewProperties (model:AdaptiveScaleBarsModel) =
            adaptive {
                let! guid = model.selectedScaleBar
                let empty = div[ style "font-style:italic"][ text "no scale bar selected" ] |> UI.map PropertiesMessage 
                
                match guid with
                | Some id -> 
                    let! scB = model.scaleBars |> AMap.tryFind id
                    match scB with
                    | Some s -> return (ScaleBarProperties.view s |> UI.map PropertiesMessage)
                    | None -> return empty
                | None -> return empty
            }                

    module Sg =

        open PRo3D.Base
        open PRo3D.Core
        open PRo3D.Core.Drawing


        let getSgSegmentCylinder
            (segment : AdaptivescSegment)
            (thickness : aval<float>) =
            adaptive {
                let! p1 = segment.startPoint
                let! p2 = segment.endPoint

                let length = Vec.Distance(p1,p2)
                let dir = p2 - p1
                let trafo =  (Trafo3d.RotateInto(V3d.ZAxis, dir.Normalized) * Trafo3d.Translation(p1))
                return Sg.cylinder 30 segment.color thickness (AVal.constant length)             
                        |> Sg.noEvents
                        |> Sg.uniform "WorldPos" (segment.startPoint)
                        |> Sg.uniform "Size" (AVal.constant length)
                        |> Sg.shader {
                            //do! Shader.screenSpaceScale
                            do! Shader.StableTrafo.stableTrafo
                            do! DefaultSurfaces.vertexColor
                            do! Shader.StableLight.stableLight
                        }
                        |> Sg.trafo(AVal.constant trafo)
            } |> Sg.dynamic

        let getSgSegmentCylinderMask
            (segment : AdaptivescSegment)
            (thickness : aval<float>) =
            adaptive {
                let! p1 = segment.startPoint
                let! p2 = segment.endPoint

                let length = Vec.Distance(p1,p2)
                let dir = p2 - p1
                let trafo =  (Trafo3d.RotateInto(V3d.ZAxis, dir.Normalized) * Trafo3d.Translation(p1))
                return Sg.cylinder 30 segment.color thickness (AVal.constant length)             
                        |> Sg.noEvents
                        |> Sg.uniform "WorldPos" (segment.startPoint)
                        |> Sg.uniform "Size" (AVal.constant length)
                        |> Sg.trafo(AVal.constant trafo)
            } |> Sg.dynamic

        let getSgSegmentLine
            (segment : AdaptivescSegment) 
            //(trafo : aval<Trafo3d>)
            (thickness : aval<float>) =
            adaptive {
                let! p1 = segment.startPoint
                let! p2 = segment.endPoint
                return Sg.drawLines 
                        ([|p1;p2|]|> AVal.constant) 
                        (AVal.constant 0.0) 
                        segment.color 
                        thickness 
                        (AVal.constant(Trafo3d.Translation p1))   
            } |> Sg.dynamic

        let getP1P2 
            (scaleBar   : AdaptiveScaleBar) =
            aval {
                let! position = scaleBar.position
                let! length = scaleBar.length.value
                let! unit = scaleBar.unit
                let length' = ScaleBarUtils.getLengthInMeter unit length
                let! orientation = scaleBar.orientation
                let! view = scaleBar.view
                let direction = ScaleBarUtils.getDirectionVec orientation view
                let! alignment = scaleBar.alignment
                let p1 = ScaleBarUtils.getP1 position length' direction alignment
                    
                let p2 = p1 + direction.Normalized * length'
                return [|p1; p2|]
            }

        let viewSingleScaleBarLine 
            (scaleBar   : AdaptiveScaleBar) 
            (view       : aval<CameraView>)
            (near       : aval<float>)
            (selected   : aval<Option<Guid>>) =

            adaptive {
                    
                let! selected' = selected
                let selected =
                    match selected' with
                    | Some sel -> sel = (scaleBar.guid |> AVal.force)
                    | None -> false

                
                let trafo =
                    adaptive {
                        let! pos = scaleBar.position
                        return (Trafo3d.Translation pos)
                    }

                let text = 
                    Sg.text view near (AVal.constant 60.0) scaleBar.position trafo scaleBar.textsize.value scaleBar.text

                let drawingPosition =
                    Sg.dot (AVal.constant C4b.Yellow) (AVal.constant 3.0)  scaleBar.position
                
                // do this for all lineparts
                let sgSegments = 
                    scaleBar.scSegments 
                    |> AList.map( fun seg -> getSgSegmentLine seg scaleBar.thickness.value ) 
                    |> AList.toASet
                    |> Sg.set            

                let points = getP1P2 scaleBar

                let vm = view |> AVal.map (fun v -> (CameraView.viewTrafo v).Forward)

                let selectionSg = 
                    if selected then
                        OutlineEffect.createForLineOrPoint vm PRo3D.Base.OutlineEffect.Line (AVal.constant C4b.VRVisGreen) scaleBar.thickness.value 3.0 RenderPass.main trafo points
                    else Sg.empty

                        
                return Sg.ofList [
                        text
                        drawingPosition
                        sgSegments
                        selectionSg //|> Sg.dynamic
                    ] |> Sg.onOff scaleBar.isVisible
                
            } |> Sg.dynamic

        let viewSingleScaleBarCylinder
            (scaleBar   : AdaptiveScaleBar) 
            (view       : aval<CameraView>)
            (near       : aval<float>)
            (selected   : aval<Option<Guid>>) 
            (priority   : bool) =

            adaptive {
                
                let! sbPriority = scaleBar.priority
                if sbPriority = priority then
                    let! selected' = selected
                    let selected =
                        match selected' with
                        | Some sel -> sel = (scaleBar.guid |> AVal.force)
                        | None -> false

            
                    let trafo =
                        adaptive {
                            let! pos = scaleBar.position
                            return (Trafo3d.Translation pos)
                        }

                    let textpos = 
                        adaptive {
                            let! orientation = scaleBar.orientation
                            let! view = scaleBar.view
                            let direction = ScaleBarUtils.getDirectionVec orientation view
                            let! alignment = scaleBar.alignment
                            let! position = scaleBar.position

                            match alignment with
                            | Pivot.Left   -> return position - direction.Normalized * 0.25
                            | Pivot.Right  -> return position + direction.Normalized *0.25
                            | Pivot.Middle -> return position - view.Forward *0.25
                            | _            -> return position
                        }

                    let textTrafo =
                        adaptive {
                            let! pos = textpos
                            return (Trafo3d.Translation pos)
                        }
               
                    let pivot =
                        Sg.dot (AVal.constant C4b.Yellow) scaleBar.pivotSize.value scaleBar.position
                        |> Sg.onOff scaleBar.showPivot

                    let text = 
                        Sg.text view near (AVal.constant 60.0) textpos textTrafo scaleBar.textsize.value scaleBar.text
                        |> Sg.onOff scaleBar.textVisible

                    let pickFunc = Sg.pickEventsHelper scaleBar.guid (AVal.constant selected) scaleBar.thickness.value trafo
            
                    // do this for all lineparts
                    let sgSegments = 
                        scaleBar.scSegments 
                        |> AList.map( fun seg -> getSgSegmentCylinder seg scaleBar.thickness.value ) 
                        |> AList.toASet
                        |> Sg.set
               
            
                    // add picking
                    //let applicator =
                    //    test 
                    //    |> Sg.pickable' ((pickableContent points edges trafo pickingTolerance) |> AVal.map Pickable.ofShape)

                    //(applicator :> ISg) 
                    //|> Sg.noEvents
                    //|> Sg.withEvents [ pickFunc edges ]
                    //|> Sg.trafo trafo
                             
                    let selectionSg = 
                        if selected then
                            let cylinder = 
                                scaleBar.scSegments 
                                |> AList.map( fun seg -> getSgSegmentCylinderMask seg scaleBar.thickness.value ) 
                                |> AList.toASet
                                |> Sg.set
                            OutlineEffect.createForSg 2 RenderPass.main C4f.VRVisGreen cylinder
                        else Sg.empty
                
                    
                    return Sg.ofList [
                            //text
                            pivot
                            
                            sgSegments
                            selectionSg //|> Sg.dynamic
                            text
                        ] |> Sg.onOff scaleBar.isVisible
                else
                    return Sg.empty
            
            } |> Sg.dynamic

        let getPriorityScaleBars
            (priority : bool)
            (scaleBars : amap<Guid,AdaptiveScaleBar>) =
                seq {
                    let test =
                        scaleBars 
                        |> AMap.toASet
                        |> ASet.toAList
                        |> AList.map snd 
                        |> AList.map( fun x -> x.priority
                                                |> AVal.map(fun pr -> match (pr = priority) with
                                                                        | true ->  Some x
                                                                        | false -> None
                                                                )
                                                        )
                    yield test
                } 
                |> AList.concat

        let view 
            (scaleBarsModel : AdaptiveScaleBarsModel)
            (view : aval<CameraView>)
            (priority : bool)
            (mbigConfig : 'ma)
            (minnerConfig : MInnerConfig<'ma>) =

            let near      = minnerConfig.getNearDistance   mbigConfig

            let scaleBars = scaleBarsModel.scaleBars //|> getPriorityScaleBars priority
            let selected = scaleBarsModel.selectedScaleBar

            let test =
                scaleBars |> AMap.map( fun id sb ->
                    viewSingleScaleBarCylinder
                        sb
                        view
                        //refsys
                        near
                        selected
                        priority
                    ) 
                    |> AMap.toASet 
                    |> ASet.map snd
                    
                    |> Sg.set
                  
            test
    
    