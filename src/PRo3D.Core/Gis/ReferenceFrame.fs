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
module ReferenceFrame =

    let initial () : ReferenceFrame =
        {
            version       = ReferenceFrame.current
            label         = "New Frame"        
            description   = None
            isEditing     = true
            spiceName     = FrameSpiceName "New Frame"    
            spiceNameText = "New Frame"
            entity        = None
        }

    let update (m : ReferenceFrame) (msg : ReferenceFrameAction) =
        match msg with 
        | ReferenceFrameAction.SetLabel label ->
            {m with label = label}
        | ReferenceFrameAction.SetSpiceName name ->
            {m with spiceName = FrameSpiceName name}
        | ReferenceFrameAction.SetSpiceNameText name ->
            {m with spiceNameText = name}
        | ReferenceFrameAction.SetEntity entity ->
            {m with entity = entity}
        | ReferenceFrameAction.Delete id ->
            Log.line "[Entity] Delete action needs to be handled in parent."
            m
        | ReferenceFrameAction.Cancel ->
            Log.line "[Entity] Cancel action needs to be handled in parent."
            m
        | ReferenceFrameAction.Save ->
            {m with isEditing = false
                    spiceName = FrameSpiceName m.spiceNameText}

    let private entitySelectionGui 
            (entities : amap<EntitySpiceName, AdaptiveEntity>) 
            (m : AdaptiveReferenceFrame) =
        UI.dropDownWithEmptyText
            (entities 
                |> AMap.toASet
                |> ASet.toAList
                |> AList.map fst)
            m.entity
            (fun x -> ReferenceFrameAction.SetEntity x)  
            (fun x -> x.Value)
            "Select Entity"

    let private editView 
            (m : AdaptiveReferenceFrame)
            (entites : amap<EntitySpiceName, AdaptiveEntity>) = 
        let actions = 
            [
                i [
                    clazz "red remove icon"
                    onClick (fun _ -> ReferenceFrameAction.Cancel)
                ] []
                i [
                    clazz "green save icon"
                    onClick (fun _ -> ReferenceFrameAction.Save)
                ] []
            ]

        [
            //td [] [Html.SemUi.textBox m.label ReferenceFrameAction.SetLabel]
            td [] [Html.SemUi.textBox m.spiceNameText ReferenceFrameAction.SetSpiceNameText ]
            td [] [entitySelectionGui entites m]
            td [] actions
        ] 

    let private displayView 
            (m : AdaptiveReferenceFrame)
            (entities : amap<EntitySpiceName, AdaptiveEntity>) = 
        let actions =
            [
                clazz "red remove icon"
                onClick (fun _ -> ReferenceFrameAction.Delete m.spiceName)
            ] 

        [
            //td [] [Html.SemUi.textBox m.label ReferenceFrameAction.SetLabel]
            td [] [text m.spiceName.Value]
            td [] [entitySelectionGui entities m]
            td [] [
                i actions []
            ]
        ] 

    let viewAsTr (m : AdaptiveReferenceFrame)
                 (entites : amap<EntitySpiceName, AdaptiveEntity>) 
                 mapper =

        let editOrDisplayView =
            alist {
                let! isEditing = m.isEditing
                if isEditing then
                    yield! (editView m entites)
                else
                    yield! (displayView m entites)
            }

        Incremental.tr AttributeMap.empty editOrDisplayView
        |> UI.map (fun msg -> mapper msg m.spiceName)
        

            





