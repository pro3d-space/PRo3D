namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Animation
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open Aardvark.Rendering
open Aardvark.UI.Anewmation

open System
open System.IO
open PRo3D.Base
open PRo3D.Core.SequencedBookmarks
open PRo3D.Core.BookmarkUtils
open PRo3D.Core.BookmarkAnimations

open Aether
open Aether.Operators

module SequencedBookmark =

    let update (m : SequencedBookmarkModel) (msg : SequencedBookmarkAction) =
        match msg with
        | SetName name ->
            {m with bookmark = {m.bookmark with name = name}}
        | SetDelay msg ->
            {m with delay = Numeric.update m.delay msg}
        | SetDuration msg ->
            {m with duration = Numeric.update m.duration msg}

module AnimationSettings =
    let update (m : AnimationSettings) (msg : AnimationSettingsAction)  =
        match msg with
        | SetGlobalDuration  msg ->
           let globalDuration = Numeric.update m.globalDuration msg
           {m with globalDuration = globalDuration}
        | ToggleGlobalAnimation ->
            let useGlobalAnimation = not m.useGlobalAnimation
            {m with useGlobalAnimation = useGlobalAnimation}
        | SetLoopMode loopMode ->
            {m with loopMode = loopMode}
        | ToggleApplyStateOnSelect ->
            let applyStateOnSelect = not m.applyStateOnSelect
            {m with applyStateOnSelect = applyStateOnSelect}
        | ToggleUseEasing ->
            let useEasing = not m.useEasing
            {m with useEasing = useEasing}
        | ToggleUseSmoothing ->
            let smoothPath = not m.smoothPath
            {m with smoothPath = smoothPath}
        | SetSmoothingFactor  msg ->
           let smoothingFactor = Numeric.update m.smoothingFactor msg
           {m with smoothingFactor = smoothingFactor}



module SequencedBookmarksApp = 
    let mutable (snapshotProcess : option<System.Diagnostics.Process>) = None

    let outputPath (m : SequencedBookmarks) = 
        Path.combine [m.outputPath;"batchRendering.json"]

    let update 
        (m                : SequencedBookmarks) 
        (act              : SequencedBookmarksAction) 
        (lenses           : BookmarkLenses<'a>)
        (outerModel       : 'a) : ('a * SequencedBookmarks) =

        match act with
        | AnimationSettingsMessage msg ->
            let animationSettings = AnimationSettings.update m.animationSettings msg
            let m = {m with animationSettings = animationSettings}
            outerModel, m
        | LoadBookmarks -> 
            outerModel, BookmarkUtils.loadAll m
        | SaveAnimation ->
            // do in outer model
            outerModel, m
        | AddSBookmark ->
            let nav = Optic.get lenses.navigationModel_ outerModel
            let state = Optic.get lenses.sceneState_ outerModel
            let newSBm = 
                getNewSBookmark nav state  m.bookmarks.Count
            let oderList' = m.orderList@[newSBm.key]
            let bookmarks = 
                m.bookmarks 
                |> HashMap.add newSBm.key (SequencedBookmark.LoadedBookmark newSBm)
            let m = {m with bookmarks = bookmarks
                            orderList = oderList';
                            selectedBookmark = Some newSBm.key}
            outerModel, m

        | SequencedBookmarksAction.FlyToSBM id ->
            toBookmarkFromView m outerModel lenses (tryFind id)
        | RemoveSBM id -> 
            let selSBm = 
                match m.selectedBookmark with
                | Some key -> if key = id then None else Some id
                | None -> None

            let bookmarks' = m.bookmarks |> HashMap.remove id
            let index = m.orderList |> List.toSeq |> Seq.findIndex(fun x -> x = id)
            let orderList' = removeGuid index m.orderList 
            outerModel, { m with bookmarks = bookmarks'
                                 orderList = orderList'
                                 selectedBookmark = selSBm         
                        }
       
        | SelectSBM id ->
            match m.animationSettings.applyStateOnSelect with
            | true ->
                let m = selectSBookmark m id 
                let selected = selected m
                let outerModel = 
                    match selected with
                    | Some sel -> // apply bookmark state
                        Optic.set lenses.setModel_ (SequencedBookmark.LoadedBookmark sel)
                                                   outerModel
                    | None ->
                        outerModel
                outerModel, m
            | false ->
                outerModel, selectSBookmark m id 
        | SetSceneState id ->
            let updState bm =
                {bm with sceneState = Some (Optic.get lenses.sceneState_ outerModel)}
            let m = loadAndUpdate m id updState
            Log.line "[Sequenced Bookmarks] Updated scene state."
            outerModel, m
        | SaveSceneState ->
            let m = {m with savedSceneState = Some (Optic.get lenses.sceneState_ outerModel)}
            Log.line "[SequencedBookmarks] Saved scene state."
            outerModel, m
        | RestoreSceneState ->
            let outerModel = 
                match m.savedSceneState with
                | Some state ->
                    Log.line "[SequencedBookmarks] Restoring scene state."
                    Optic.set lenses.sceneState_ state outerModel
                | None ->
                    Log.line "[SequencedBookmarks] No scene state to restore."
                    outerModel
            outerModel, m
        | MoveUp id ->
            let index = m.orderList |> List.toSeq |> Seq.findIndex(fun x -> x = id)
            let orderList' =
                if index > 0 then
                    if (index-1) > 0 then
                        let part0 = [for x in 0..(index-2) do yield m.orderList.[x]]
                        if index < (m.orderList.Length-1) then
                            let part1 = [for x in (index+1)..(m.orderList.Length-1) do 
                                            yield m.orderList.[x]]
                            part0@[m.orderList.[index]]@[m.orderList.[index-1]]@part1
                        else
                            part0@[m.orderList.[index]]@[m.orderList.[index-1]]
                    else
                        if index < (m.orderList.Length-1) then
                            let part1 = [for x in (index+1)..(m.orderList.Length-1) do 
                                            yield m.orderList.[x]]
                            [m.orderList.[index]]@[m.orderList.[index-1]]@part1
                        else
                            [m.orderList.[index]]@[m.orderList.[index-1]]
                else
                    m.orderList
            outerModel, { m with orderList = orderList' }

        | MoveDown id ->
            let index = m.orderList |> List.toSeq |> Seq.findIndex(fun x -> x = id)
            let orderList' =
                if index < (m.orderList.Length-1) then
                    if (index+1) < (m.orderList.Length-1) then
                        let partMax = [for x in (index+2)..(m.orderList.Length-1) 
                                            do yield m.orderList.[x]]
                        if (index) > 0 then
                            let partMin = [for x in 0..(index-1) do yield m.orderList.[x]]
                            partMin@[m.orderList.[index+1]]@[m.orderList.[index]]@partMax
                        else
                            [m.orderList.[index+1]]@[m.orderList.[index]]@partMax
                    else
                        if (index) > 0 then
                            let partMin = [for x in 0..(index-1) do yield m.orderList.[x]]
                            partMin@[m.orderList.[index+1]]@[m.orderList.[index]]
                        else
                            [m.orderList.[index+1]]@[m.orderList.[index]]
                else
                    m.orderList
            outerModel, { m with orderList = orderList' }
        | SequencedBookmarkMessage (key, msg) ->  
            let m = loadAndUpdate m key (fun sb -> SequencedBookmark.update sb msg)
            outerModel, m
        | Play ->
            if Animator.exists AnimationSlot.camera outerModel 
                && Animator.isPaused AnimationSlot.camera outerModel then
                Animator.startOrResume AnimationSlot.camera outerModel, m
            else 
                match (List.tryHead m.orderList) with
                | Some firstBookmark ->
                    let m = selectSBookmark m firstBookmark
                    match m.animationSettings.useGlobalAnimation with
                    | true ->
                        smoothPathAllBookmarks m lenses outerModel
                    | false ->
                        pathWithPausing m lenses outerModel
                | None ->
                    Log.line "[SequencedBookmarks] There are no sequenced bookmarks."
                    outerModel, m
        | StepForward -> 
            toBookmarkFromView m outerModel lenses next
        | StepBackward ->
            toBookmarkFromView m outerModel lenses previous
        | Pause ->
            let outerModel = Animator.pause AnimationSlot.camera outerModel
            outerModel, m 
        | Stop ->
            let outerModel = Animator.stop AnimationSlot.camera outerModel
            outerModel, m 
        | StartRecording -> 
            outerModel, {m with 
                            savedTimeSteps = []
                            lastSavedBookmark = None
                            isRecording = true
                            currentFps = None
                         }
        | StopRecording -> 
            outerModel, {m with isRecording = false}
        | ToggleGenerateOnStop ->
            outerModel, {m with generateOnStop = not m.generateOnStop}
        | ToggleUpdateJsonBeforeRendering ->
            outerModel, {m with updateJsonBeforeRendering = not m.updateJsonBeforeRendering}
        | ToggleDebug ->
            outerModel, {m with debug = not m.debug}
        | UpdateJson ->
            // currently updated in Viewer.fs
            outerModel, m
        | GenerateSnapshots -> 
            outerModel, {m with isGenerating = true}
        | CancelSnapshots ->
            outerModel, {m with isCancelled = true}
        | SetResolutionX msg ->
            outerModel, {m with resolutionX = Numeric.update m.resolutionX msg}
        | SetResolutionY msg ->
            outerModel, {m with resolutionY = Numeric.update m.resolutionY msg}
        | SetOutputPath str -> 
            let str = 
                match str with
                | [] -> SequencedBookmarks.defaultOutputPath ()
                | head::tail -> head
            outerModel, {m with outputPath = str}
        | SetFpsSetting setting ->
            outerModel, {m with fpsSetting = setting}
        | CheckSnapshotsProcess id ->
            match snapshotProcess with 
            | Some p -> 
                let m = 
                    match p.HasExited , m.isCancelled with
                    | true, _ -> 
                        Log.warn "[Snapshots] Snapshot generation finished."
                        //let m = shortFeedback "Snapshot generation finished." m 
                        let m = {m with snapshotThreads = ThreadPool.remove id m.snapshotThreads
                                        isGenerating = false
                                }
                                
                        m
                    | false, false ->
                        m
                    | false, true ->
                        Log.warn "[Snapshots] Snapshot generation cancelled."
                        p.Kill ()
                        //let m = shortFeedback "Snapshot generation cancelled." m 
                        let m = {m with snapshotThreads = ThreadPool.remove id m.snapshotThreads
                                        isGenerating = false
                                        isCancelled  = false
                                }
                        m
                outerModel, m
            | None -> 
                outerModel, m

    let generateSnapshots m runProcess scenePath =
        let jsonPathName = Path.combine [m.outputPath;"batchRendering.json"]
        if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform
                (System.Runtime.InteropServices.OSPlatform.Windows) then
                    
            let exeName = "PRo3D.Snapshots.exe"
            match File.Exists exeName with
            | true -> 
                let args = 
                    match m.debug with
                    | true ->
                        sprintf "--scn \"%s\" --asnap \"%s\" --out \"%s\" --verbose" 
                                        scenePath jsonPathName m.outputPath
                    | false ->
                        sprintf "--scn \"%s\" --asnap \"%s\" --out \"%s\" --exitOnFinish" 
                                        scenePath jsonPathName m.outputPath
                Log.line "[Viewer] Starting snapshot rendering with arguments: %s" args
                snapshotProcess <- Some (runProcess exeName args None)
                let id = System.Guid.NewGuid () |> string
                let proclst =
                    proclist {
                        for i in 0..4000 do
                            do! Proc.Sleep 4000
                            yield CheckSnapshotsProcess id
                    }              
                {m with snapshotThreads = ThreadPool.add id proclst m.snapshotThreads}
            | false -> 
                Log.warn "[Snapshots] Could not find %s" exeName
                m
        else 
            Log.warn "[Viewer] This feature is only available for Windows."
            m


    let threads (m : SequencedBookmarks) = 
        m.snapshotThreads
       // ThreadPool.union m.snapshotThreads m.animationThreads


    module UI =
        
        let viewSequencedBookmarks (m : AdaptiveSequencedBookmarks) =
            let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            let content = 
                alist {
                    let! selected = m.selectedBookmark
                    let! order = m.orderList
                    
                    for id in order do 
                        let color =
                            match selected with
                            | Some sel -> 
                                (if sel = id then C4b.VRVisGreen else C4b.Gray) 
                            | None -> 
                                C4b.Gray
                        let bgc = sprintf "color: %s" (Html.ofC4b color)

                        let headerAttributes =
                            amap {
                                yield onClick (fun _ -> SelectSBM id)
                            } 
                            |> AttributeMap.ofAMap

                        let! bookmark = m.bookmarks |> AMap.tryFind id

                        let headerText = 
                            match bookmark with
                            | Some (AdaptiveSequencedBookmark.AdaptiveLoadedBookmark bookmark) ->
                                AVal.map (fun a -> sprintf "%s" (a)) bookmark.name
                            | Some (AdaptiveSequencedBookmark.AdaptiveNotYetLoaded bm) ->
                                AVal.map (fun x -> sprintf "Not Loaded: %s" (string x)) bm.key
                            | None ->
                                Log.error "[SequencedBookmarksApp] Could not find bookmark %s" (string id)
                                AVal.constant "Bookmark not found"
                        
                        yield div [clazz "item"; style infoc] [
                            div [clazz "content"; style infoc] [                     
                                yield Incremental.div (AttributeMap.ofList [style infoc])(
                                    alist {
                                        yield div [clazz "header"; style bgc] [
                                            Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                                            ]                
        
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToSBM id)] []
                                            |> UI.wrapToolTip DataPosition.Bottom "fly to bookmark"          

                                        yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveSBM id)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Remove"     
                                        yield i [clazz "globe icon"; onClick (fun _ -> SetSceneState id)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom 
                                                "Replace scene state for this bookmark with current state. Does not replace view."

                                        yield i [clazz "arrow alternate circle up outline icon"; 
                                                 onClick (fun _ -> MoveUp id)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Move up"
                                        
                                        yield i [clazz "arrow alternate circle down outline icon"; 
                                                 onClick (fun _ -> MoveDown id)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Move down"
                                    } 
                                )                                     
                            ]
                        ]
                } 

            Incremental.div itemAttributes content
                

        let private stateLabel (state : option<SceneState>) =
            match state with
            | Some state ->
                sprintf "%i-%02i-%02i %02i:%02i:%02i" 
                        state.timestamp.Year state.timestamp.Month state.timestamp.Day
                        state.timestamp.Hour state.timestamp.Minute state.timestamp.Second
            | None -> 
                "No saved scene state"

        let viewGUI  (model : AdaptiveSequencedBookmarks) = 
            div [clazz "ui vertical buttons inverted"] [
                button [
                    clazz "ui labeled icon button"; 
                    onMouseClick (fun _ -> AddSBookmark )
                    style "margin: 5px"
                ] [ 
                    i [clazz "plus icon"] [] 
                    text "Add Bookmark"
                ] 
                div [clazz "ui labeled button";style "margin: 5px"] [
                    div [
                        clazz "ui button"
                        onMouseClick (fun _ -> SaveSceneState )
                    ] [ 
                        text "Save Scene State"
                    ]
                    div [clazz "ui basic label"] [
                        i [clazz "globe icon"] [] 
                        Incremental.text (model.savedSceneState 
                                          |> AVal.map (fun x -> stateLabel x))
                    ]
                    div [
                        clazz "ui button"
                        onMouseClick (fun _ -> RestoreSceneState )
                    ] [ 
                        text "Restore to Saved"
                    ]
                ]
            ] 

        // RNO should be moved and renamed
        let viewPropsBmModel (m : AdaptiveSequencedBookmarkModel) 
                             (useGlobalAnimation : aval<bool>) =
            let view = m.bookmark.cameraView
            let notWithGlobalAnimation content =
                alist {
                    let! useGlobal = useGlobalAnimation
                    if useGlobal then
                        yield div [] [text "Deselect global animation to edit"]
                    else yield content
                }

            let duration = 
                Numeric.view' [InputBox] m.duration
                    |> UI.map  (fun msg -> 
                        SequencedBookmarkMessage 
                            (m.bookmark.key, SequencedBookmarkAction.SetDuration msg)) 
                    |> notWithGlobalAnimation
                
            let delay =
                Numeric.view' [InputBox] m.delay 
                    |> UI.map  (fun msg -> 
                        SequencedBookmarkMessage 
                            (m.bookmark.key, SequencedBookmarkAction.SetDelay msg)) 
                    |> notWithGlobalAnimation
                


            let stateUpdateButton id =
                div [] [
                    
                    Incremental.text (m.sceneState |> AVal.map (fun x -> stateLabel x))
                    
                    div [
                        style "margin-left: 5px"
                        clazz "ui button"
                        onClick (fun _ -> SetSceneState id)
                    ] [
                        i [clazz "globe icon"] []
                        text "update"
                    ]
                ]

            require GuiEx.semui (
                Html.table [  
                    Html.row "Change Name:" [Html.SemUi.textBox m.bookmark.name SetName ]
                        |> UI.map  (fun msg ->  SequencedBookmarkMessage (m.bookmark.key, msg)) 
                    Html.row "Duration" [Incremental.div AttributeMap.empty duration]
                    Html.row "Delay"    [Incremental.div AttributeMap.empty delay]
                    Html.row "Pos:"     [Incremental.text (view |> AVal.map (fun x -> x.Location.ToString("0.00")))] 
                    Html.row "LookAt:"  [Incremental.text (view |> AVal.map (fun x -> x.Forward.ToString("0.00")))]
                    Html.row "Up:"      [Incremental.text (view |> AVal.map (fun x -> x.Up.ToString("0.00")))]
                    Html.row "Sky:"     [Incremental.text (view |> AVal.map (fun x -> x.Sky.ToString("0.00")))]
                    Html.row "SceneState:" [
                        stateUpdateButton m.bookmark.key
                    ]
                ]
            )

        let viewProperties (model : AdaptiveSequencedBookmarks) =
            adaptive {
                let! selBm = model.selectedBookmark
                //let! delay = HashMap.tryFind selBm. model.animationInfo
                let empty = div [ style "font-style:italic"] [ text "no bookmark selected" ] 
                
                match selBm with
                | Some id -> 
                    let! scB = model.bookmarks |> AMap.tryFind id 
                    match scB with
                    | Some s -> 
                        match s with
                        | AdaptiveSequencedBookmark.AdaptiveLoadedBookmark s ->
                            let bmProps = (viewPropsBmModel s )
                            return div [] [bmProps model.animationSettings.useGlobalAnimation]
                        | AdaptiveSequencedBookmark.AdaptiveNotYetLoaded s ->
                            return div [ style "font-style:italic"] 
                                       [button [style "inverted";onClick (fun _ -> LoadBookmarks)] 
                                               [text "Load All Bookmarks"]
                                       ]
                    | None -> 
                        return empty
                | None -> return empty
            }  
            
        let viewAnimationGUI (model:AdaptiveSequencedBookmarks) = 
            let onlyWithGlobalAnimation content =
                alist {
                    let! useGlobal = model.animationSettings.useGlobalAnimation
                    if not useGlobal then
                        yield div [] [text "Select global animation to edit"]
                    else yield content
                }

            let smoothingFactor =
                Numeric.view' [InputBox] model.animationSettings.smoothingFactor
                    |> UI.map SetSmoothingFactor
                    |> UI.map AnimationSettingsMessage
                    |> onlyWithGlobalAnimation

            let duration = 
                Numeric.view' [InputBox] model.animationSettings.globalDuration
                    |> UI.map SetGlobalDuration
                    |> UI.map AnimationSettingsMessage
                    |> onlyWithGlobalAnimation

            require GuiEx.semui (
                Html.table [               
                  Html.row "Animation:"   [div [clazz "ui buttons inverted"] [
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> StepBackward )] [ //
                                                  i [clazz "step backward icon"] [] ] 
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> Play )] [ //
                                                  i [clazz "play icon"] [] ] 
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> Pause )] [ //
                                                  i [clazz "pause icon"] [] ] 
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> Stop )] [ //
                                                  i [clazz "stop icon"] [] ] 
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> StepForward )] [ //
                                                  i [clazz "step forward icon"] [] ] 
                                          ] ]
                  Html.row "Global Animation:" 
                    [GuiEx.iconCheckBox model.animationSettings.useGlobalAnimation ToggleGlobalAnimation
                        |> UI.map AnimationSettingsMessage;
                                    i [clazz "info icon"] [] 
                                        |> UI.wrapToolTip DataPosition.Bottom "Use a smooth path past all bookmarks, duration and delay cannot be set for individual bookmarks if this is selected."
                    ]
                  Html.row "Duration:" [Incremental.div AttributeMap.empty duration]
                  Html.row "Smoothing Factor" [Incremental.div AttributeMap.empty smoothingFactor]

                  Html.row "Loop:"   [Html.SemUi.dropDown model.animationSettings.loopMode SetLoopMode
                                        |> UI.map AnimationSettingsMessage ]
                  Html.row "Use Easing:" 
                           [GuiEx.iconCheckBox model.animationSettings.useEasing ToggleUseEasing
                                |> UI.map AnimationSettingsMessage]
                  //Html.row "Smooth Path:" 
                  //         [GuiEx.iconCheckBox model.animationSettings.smoothPath ToggleUseSmoothing
                  //              |> UI.map AnimationSettingsMessage]
                  
                                
                  Html.row "Apply State on Select:" [
                        GuiEx.iconCheckBox model.animationSettings.applyStateOnSelect ToggleApplyStateOnSelect
                            |> UI.map AnimationSettingsMessage
                    ]

                ]
              )

        let viewSnapshotGUI (model:AdaptiveSequencedBookmarks) = 
            let startRecordingButton =
                button [clazz "ui icon button"; onMouseClick (fun _ -> StartRecording )] [ 
                        i [clazz "red circle icon"] [] ] 
                    

            let stopRecordingButton = 
                button [clazz "ui icon button"; onMouseClick (fun _ -> StopRecording )] [ 
                        i [clazz "red stop icon"] [] ] 
                    

            let recordingButton =
                model.isRecording |> AVal.map (fun r -> if r then stopRecordingButton else startRecordingButton)
                                  |> AList.ofAValSingle

            let generateButton = 
                button [clazz "ui icon button"; onMouseClick (fun _ -> GenerateSnapshots )] [ 
                    i [clazz "camera icon"] [] ] 
                    

            let cancelButton = 
                button [clazz "ui icon button"; onMouseClick (fun _ -> CancelSnapshots )] 
                       [i [clazz "remove icon"] []]
                            
               

            let generateToggleButton =
                model.isGenerating |> AVal.map (fun b -> if b then cancelButton else  generateButton)
                                   |> AList.ofAValSingle
            
            let fpsText =
                model.currentFps |> AVal.map (fun fps -> 
                                            match fps with
                                            | Some fps -> sprintf "%i" fps
                                            | None -> "No frames recorded."
                                         )
            let updateJsonButton =
                let info =
                    i [clazz "info icon"] [] 
                    |> UI.wrapToolTip DataPosition.Bottom "Only available if \"Alllow JSON Editing\" is selected. 
                                                           Updates the settings for image generation in the JSON file. 
                                                           Manual changes to the JSON file will be overwritten!"
                let onlyButton = 
                    model.updateJsonBeforeRendering
                        |> AVal.map (fun b ->
                                        match b with
                                        | false ->  button [onMouseClick (fun _ -> UpdateJson)] [text "Update"]
                                        | true  -> div [] []
                                    )
                let alst = 
                    alist {
                            let! b = onlyButton
                            yield b
                            yield info
                          }

                Incremental.div ([] |> AttributeMap.ofList ) alst
                

            let outputFolderGui =
                let attributes = 
                    alist {
                        let! outputPath = model.outputPath
                        yield Dialogs.onChooseFiles SetOutputPath;
                        yield clientEvent "onclick" (Dialogs.jsSelectPathDialogWithPath outputPath)
                        yield (style "word-break: break-all")
                    } |> AttributeMap.ofAList

                let content =
                    alist {
                        yield i [clazz "write icon"] []
                        yield Incremental.text model.outputPath
                    }

                Incremental.div attributes content


            require GuiEx.semui (
                div [] [
                    Html.table [            
                        Html.row "Save Camera Animation:" 
                            [   
                                button [
                                    onMouseClick (fun _ -> StopRecording )
                                ] [text "Save"]
                                i [clazz "info icon"] [] 
                                    |> UI.wrapToolTip DataPosition.Bottom "No smooth path, no easing, no global animation."     
                            ]
                        Html.row "Generate Images:" 
                            [
                                Incremental.div ([] |> AttributeMap.ofList) generateToggleButton         
                            ]

                        Html.row "Allow JSON Editing" 
                            [GuiEx.iconCheckBox (model.updateJsonBeforeRendering |> AVal.map not) ToggleUpdateJsonBeforeRendering;
                                i [clazz "info icon"] [] 
                                    |> UI.wrapToolTip DataPosition.Bottom "If selected, the JSON file will NOT be updated before rendering. 
                                                                           If you change settings in the user interface after recording, 
                                                                           they will not be reflected in the JSON file."
                            ]
                        Html.row "Update JSON" 
                            [
                                updateJsonButton
                                
                            ]
                        Html.row "Debug" 
                            [GuiEx.iconCheckBox model.debug ToggleDebug;
                                    i [clazz "info icon"] [] 
                                        |> UI.wrapToolTip DataPosition.Bottom "Leaves the window with information about the rendering
                                                                               process open after rendering and uses verbose mode."
                            ]

                        Html.row "Image Resolution:"  
                            [
                                Numeric.view' [NumericInputType.InputBox]  model.resolutionX |> UI.map SetResolutionX 
                                Numeric.view' [NumericInputType.InputBox]  model.resolutionY |> UI.map SetResolutionY
                            ]

                        Html.row "Current FPS" [text "60"] //[Incremental.text fpsText]
                        Html.row "FPS Setting" [Html.SemUi.dropDown model.fpsSetting SetFpsSetting]
                        Html.row "Output Path" 
                            [
                                outputFolderGui
                            ]
                    ]

                ]
            )            
       

  