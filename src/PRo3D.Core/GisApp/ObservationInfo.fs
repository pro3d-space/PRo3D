namespace PRo3D.Core.Gis


open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
open PRo3D.Core.Surface
open PRo3D.Base.Gis
open Aether

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ObservationInfo =

    let update (m : ObservationInfo) (msg : ObservationInfoAction) =
        match msg with
        | ObservationInfoAction.CalendarMessage msg ->
            {m with time = Calendar.update m.time msg}
        | ObservationInfoAction.SetTarget target ->
            {m with target = target}
        | ObservationInfoAction.SetObserver observer ->
            // A body PRo3D knows is observed in its own fixed frame: that is what makes the
            // scene body-fixed, so the planet-based features apply (#758). For now the frame
            // follows the body; choosing another scene frame is a later step.
            match observer |> Option.bind SceneBody.tryFixedFrame with
            | Some fixedFrame -> {m with observer = observer; referenceFrame = Some fixedFrame}
            | None            -> {m with observer = observer}
        | ObservationInfoAction.SetTime time ->
            {m with time = {m.time with date = Calendar.toUtc time}}
        | ObservationInfoAction.SetReferenceFrame frame ->
            {m with referenceFrame = frame}
        | ObservationInfoAction.Reset ->
            // Nothing to change: the Viewer re-aims the camera on Reset (GisApp.reaimsCamera),
            // which is all "Re-use settings above" asks for.
            m

    /// `sceneRows`: show the observed body and the reference frame. Only the scene's own
    /// observation has them - they are the scene body (#758); a bookmark contributes its
    /// time and camera source only.
    let view (sceneRows : bool)
             (m : AdaptiveObservationInfo)
             (entities : amap<EntitySpiceName, AdaptiveEntity>) 
             (referenceFrames : amap<FrameSpiceName, AdaptiveReferenceFrame>) =

        let observerDropdown =
            UI.dropDownWithEmptyText
                (entities 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map fst)
                m.observer
                (fun x -> SetObserver x)  
                (fun x -> x.Value)
                "Select Observer"
        let targetDropdown =
            UI.dropDownWithEmptyText
                (entities 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map fst)
                m.target
                (fun x -> SetTarget x)  
                (fun x -> x.Value)
                "Select Target"

        let frameDropdown () =
            UI.dropDownWithEmptyText
                (referenceFrames 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map fst)
                m.referenceFrame
                (fun x -> ObservationInfoAction.SetReferenceFrame x)
                (fun x -> x.Value)
                "Select Frame"

        // A body PRo3D knows is shown in its fixed frame, not offered a choice (#758). A
        // scene saved in another frame (e.g. J2000) keeps it and says what that costs,
        // with the switch one click away. Any other observed body keeps the free choice.
        let frameCell =
            Incremental.div AttributeMap.empty (
                alist {
                    let! observer = m.observer
                    match observer |> Option.bind (fun o -> SceneBody.tryFixedFrame o |> Option.map (fun f -> o, f)) with
                    | None ->
                        yield frameDropdown ()
                    | Some (body, fixedFrame) ->
                        let! frame = m.referenceFrame
                        match frame with
                        | Some frame when SceneBody.isFixedFrameOf body frame ->
                            yield text (sprintf "%s (body-fixed)" frame.Value)
                        | _ ->
                            let current = frame |> Option.map (fun f -> f.Value) |> Option.defaultValue "No frame"
                            yield div [clazz "ui inverted orange segment"; style "padding: 5px; margin: 0"] [
                                text (sprintf "%s is not the body-fixed frame of %s: the planet-based features (map view, lat/lon, up/north) cannot read this scene correctly. " current body.Value)
                                button [clazz "ui mini button"; onClick (fun _ -> ObservationInfoAction.SetReferenceFrame (Some fixedFrame))] [
                                    text (sprintf "Use %s" fixedFrame.Value)
                                ]
                            ]
                }
            )

        require GuiEx.semui (
            Html.table [                                                
                if sceneRows then
                    Html.row "Observed body:" [observerDropdown]
                Html.row "Camera source Body"   [targetDropdown]
                Html.row "Time:"     
                    [
                        Calendar.view m.time false false 
                                      Calendar.CalendarType.DateTime
                    ] |> UI.map CalendarMessage
                if sceneRows then
                    Html.row "Reference Frame:" [frameCell]
                Html.row "Reset" [button [onClick (fun _ -> Reset)] [text "Re-use settings above"]]
            ]
        )

    let initial = 
        {
            target         = None
            observer       = None
            // UTC, like every observation time. It used to be parsed without a zone,
            // which SPICE then read as local time: the default epoch moved with the
            // machine's time zone.
            time           = { Calendar.init with date = System.DateTime(2025, 3, 10, 19, 8, 12, 600, System.DateTimeKind.Utc) }
            referenceFrame = None
        }

