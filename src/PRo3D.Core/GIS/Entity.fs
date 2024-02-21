namespace PRo3D.Core.Gis


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

    let inital () =
        {
            version       = Entity.current
            label         = "New Entity"        
            isEditing     = true
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
        | SetLabel label ->
            {m with label = label}
        | SetSpiceName name ->
            {m with spiceName = EntitySpiceName name}
        | SetSpiceNameText name ->
            {m with spiceNameText = name}
        | SetReferenceFrame frame ->
            {m with defaultFrame = frame}
        | Delete id ->
            Log.line "[Entity] Delete action needs to be handled in parent."
            m
        | Cancel ->
            Log.line "[Entity] Cancel action needs to be handled in parent."
            m
        | Save ->
            {m with isEditing = false
                    spiceName = EntitySpiceName m.spiceNameText}

    let private refFramesSelectionGui 
            (referenceFrames : amap<FrameSpiceName, ReferenceFrame>) 
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

    let private editView 
            (m : AdaptiveEntity)
            (referenceFrames : amap<FrameSpiceName, ReferenceFrame>) = 
        let actions = 
            [
                i [
                    clazz "red remove icon"
                    onClick (fun _ -> EntityAction.Cancel)
                ] []
                i [
                    clazz "green save icon"
                    onClick (fun _ -> EntityAction.Save)
                ] []
            ]

        [
            td [] [Html.SemUi.textBox m.label SetLabel]
            td [] [Html.SemUi.textBox m.spiceNameText SetSpiceNameText ]
            td [] [refFramesSelectionGui referenceFrames m]
            td [] actions
        ] 

    let private displayView 
            (m : AdaptiveEntity)
            (referenceFrames : amap<FrameSpiceName, ReferenceFrame>) = 
        let actions =
            [
                clazz "red remove icon"
                onClick (fun _ -> EntityAction.Delete m.spiceName)
            ] 

        [
            td [] [Html.SemUi.textBox m.label SetLabel]
            td [] [text m.spiceName.Value]
            td [] [refFramesSelectionGui referenceFrames m]
            td [] [
                i actions []
            ]
        ] 

    let viewAsTr (m : AdaptiveEntity)
                 (referenceFrames : amap<FrameSpiceName, ReferenceFrame>) 
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
        

            





