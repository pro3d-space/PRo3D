﻿namespace PRo3D.Core.Gis


open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
open PRo3D.Core.Surface
open PRo3D.Base.Gis
open Aether

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Entity =

    let initial () =
        {
            version       = Entity.current
            label         = "New Entity"        
            isEditing     = true
            draw          = false
            spiceName     = EntitySpiceName "New Entity"    
            spiceNameText = "New Entity"
            color         = C4f.White       
            geometryPath  = None
            radius        = 1.0
            textureName   = None
            defaultFrame  = None
        }

    let update (m : Entity) (msg : EntityAction) =
        match msg with 
        | EntityAction.SetLabel label ->
            {m with label = label}
        | EntityAction.SetSpiceName name ->
            {m with spiceName = EntitySpiceName name}
        | EntityAction.SetSpiceNameText name ->
            {m with spiceNameText = name}
        | EntityAction.SetReferenceFrame frame ->
            {m with defaultFrame = frame}
        | EntityAction.SetGeometryPath geometryPath ->
            {m with geometryPath = Some geometryPath}
        | EntityAction.SetRadius radius ->
            {m with radius = radius}
        | EntityAction.SetTextureName textureName ->
            {m with textureName = Some textureName}
        | EntityAction.ToggleDraw ->
            {m with draw = not m.draw}
        | EntityAction.Edit spiceName ->
            {m with isEditing = true}
        | EntityAction.Delete id ->
            Log.line "[Entity] Delete action needs to be handled in parent."
            m
        | EntityAction.Cancel spicename ->
            Log.line "[Entity] Cancel action needs to be handled in parent."
            m
        | EntityAction.Save spicename ->
            {m with isEditing = false
                    spiceName = EntitySpiceName m.spiceNameText}

    let private refFramesSelectionGui 
            (referenceFrames : amap<FrameSpiceName, AdaptiveReferenceFrame>) 
            (m : AdaptiveEntity) =
        UI.dropDownWithEmptyText
            (referenceFrames 
                |> AMap.toASet
                |> ASet.toAList
                |> AList.map fst)
            m.defaultFrame
            (fun x -> SetReferenceFrame x)  
            (fun x -> x.Value)
            "Select Frame"

    let newViewAsTr 
            (m : AdaptiveEntity)
            (referenceFrames : amap<FrameSpiceName, AdaptiveReferenceFrame>)
            mapper =
        let actions = 
            [
                i [
                    clazz "red remove icon"
                    onClick (fun _ -> EntityAction.Cancel m.spiceName)
                ] []
                i [
                    clazz "green save icon"
                    onClick (fun _ -> EntityAction.Save m.spiceName)
                ] []
            ]
        let cells = 
            [
                td [] [Html.SemUi.textBox m.label EntityAction.SetLabel]
                td [] [Html.SemUi.textBox m.spiceNameText EntityAction.SetSpiceNameText ]
                td [] [refFramesSelectionGui referenceFrames m]
                td [] actions
            ] 

        tr [] cells
        |> UI.map (fun msg -> mapper msg m.spiceName)

    let private editView 
            (m : AdaptiveEntity)
            (referenceFrames : amap<FrameSpiceName, AdaptiveReferenceFrame>) = 
        let actions = 
            tr [] [
                td [attribute "colspan" "2";style "text-align: right"] [
                    i [
                        clazz "green save icon"
                        onClick (fun _ -> EntityAction.Save m.spiceName)
                    ] []
                ]
            ]
        let radiusInput = 
            (Aardvark.UI.NoSemUi.numeric 
                { min = 0.0000001; max = System.Double.MaxValue; smallStep = 0.1; largeStep= 1.0 } 
                "text"
                ([clazz "ui inverted input"] |> AttributeMap.ofList)
                m.radius 
                SetRadius)
        let rows = 
            [
                //Html.row "Spice name" [text m.spiceName.Value]
                tr [] [
                    td [attribute "colspan" "2"
                        style "text-align: center; font-weight: bold;"]
                       [text ("Spice Name: " + m.spiceName.Value)]
                ]
                Html.row "Label" [Html.SemUi.textBox m.label EntityAction.SetLabel]
                Html.row "Reference Frame" [refFramesSelectionGui referenceFrames m]
                Html.row "Geometry Path" 
                         [Html.SemUi.textBox 
                            (m.geometryPath 
                                |> AVal.map (fun x ->
                                Option.defaultValue "" x
                            )) EntityAction.SetGeometryPath]
                Html.row "Texture Name" 
                         [Html.SemUi.textBox 
                            (m.textureName 
                            |> AVal.map (fun x ->
                                Option.defaultValue "" x
                            ))
                            EntityAction.SetTextureName]
                Html.row "Radius" [radiusInput]
                Html.row "Draw Entity" [GuiEx.iconCheckBox m.draw EntityAction.ToggleDraw]
                actions
            ]

        [td [attribute "colspan" "4"] [
            table [clazz "ui unstackable inverted table"
                   style "border-color: rgba(255,255,255,.9);border-width: 3px;border-style: solid;"
            ] rows
        ]]

    let private displayView 
            (m : AdaptiveEntity)
            (referenceFrames : amap<FrameSpiceName, AdaptiveReferenceFrame>) = 
        let actions =
            [
                i [
                    clazz "red remove icon"
                    onClick (fun _ -> EntityAction.Delete m.spiceName)
                ] []
                i [
                    clazz "edit icon"
                    onClick (fun _ -> EntityAction.Edit m.spiceName)
                ] []
            ]

        [
            td [] [Html.SemUi.textBox m.label EntityAction.SetLabel]
            td [] [text m.spiceName.Value]
            td [] [refFramesSelectionGui referenceFrames m]
            td [] actions
        ] 

    let viewAsTr (m : AdaptiveEntity)
                 (referenceFrames : amap<FrameSpiceName, AdaptiveReferenceFrame>) 
                 mapper =
        let editOrDisplayView =
            alist {
                let! isEditing = m.isEditing
                if isEditing then
                    yield! (editView m referenceFrames)
                else
                    yield! (displayView m referenceFrames)
            }

        Incremental.tr AttributeMap.empty editOrDisplayView
        |> UI.map (fun msg -> mapper msg m.spiceName)


