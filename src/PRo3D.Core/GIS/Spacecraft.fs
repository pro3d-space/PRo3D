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
module Spacecraft =

    let inital () =
        {
            id        = SpacecraftId.New ()
            label     = "New Spacecraft"
            spiceName = SpacecraftSpiceName ""
            referenceFrame = None
        }

    let update (m : Spacecraft) (msg : SpacecraftAction) =
        match msg with 
        | SetLabel label ->
            {m with label = label}
        | SetSpiceName name ->
            {m with spiceName = SpacecraftSpiceName name}
        | SetReferenceFrame frame ->
            {m with referenceFrame = frame}
        | Delete id ->
            Log.line "[Spacecraft] Delete action needs to be handled in parent."
            m

    let heraSpacecraft () =
        {
            id    = SpacecraftId.New ()
            label = "Hera Spacecraft"
            spiceName = SpacecraftSpiceName "HERA_SPACECRAFT" // ?? Need to check!
            referenceFrame = None
        }

    let private editView (m : AdaptiveSpacecraft)
                         (referenceFrames : amap<FrameSpiceName, ReferenceFrame>) =
        let refFramesSelectionGui =
            UI.dropDownWithEmptyText
                (referenceFrames 
                    |> AMap.keys
                    |> ASet.toAList)
                m.referenceFrame
                (fun x -> SetReferenceFrame x)  
                (fun x -> x.Value)
                "Select Frame"
        
        let labelTextbox = 
            Html.SemUi.textBox m.label SetLabel 

        let spiceNameTextBox =
            Html.SemUi.textBox 
                (m.spiceName |> AVal.map SpacecraftSpiceName.value)
                SetSpiceName 

        tr [] [
            td [] [labelTextbox]
            td [] [spiceNameTextBox]
            td [] [refFramesSelectionGui]
            td [] [
                i [
                    clazz "red remove icon"
                    onClick (fun _ -> SpacecraftAction.Delete m.id)
                ] []
            ]
        ]

    let private displayView (m : AdaptiveSpacecraft) =
        tr [] [
            td [] [Incremental.text m.label]
            td [] [
                Incremental.text (m.spiceName 
                |> AVal.map SpacecraftSpiceName.value)
            ]
            td [] [
                Incremental.text 
                    (m.referenceFrame
                     |> AVal.map (fun x -> 
                            match x with
                            | Some x -> x.Value
                            | None -> "---"
                    ))
            ]
        ]

    let viewAsTr (m : AdaptiveSpacecraft)
                 (referenceFrames : amap<FrameSpiceName, ReferenceFrame>) =
        editView m referenceFrames

            





