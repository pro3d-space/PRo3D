namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Animation
open Aardvark.UI.Primitives
open Aardvark.VRVis
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open Aardvark.SceneGraph.IO
open Aardvark.SceneGraph.SgPrimitives
open Aardvark.Rendering
open Aardvark.UI.Trafos

open System
open System.IO
open System.Diagnostics
open OpcViewer.Base
open PRo3D.Base

open Aether

module SequencedBookmarksProperties =  

    let update (model : Bookmark) (act : SequencedBookmarksPropertiesAction) =
        match act with
        | SetName s ->
            { model with name = s }

    let view (model:AdaptiveBookmark) =
        let view = model.cameraView
        require GuiEx.semui (
            Html.table [  
                Html.row "Change Name:"[Html.SemUi.textBox model.name SetName ]
                Html.row "Pos:"     [Incremental.text (view |> AVal.map (fun x -> x.Location.ToString("0.00")))] 
                Html.row "LookAt:"  [Incremental.text (view |> AVal.map (fun x -> x.Forward.ToString("0.00")))]
                Html.row "Up:"      [Incremental.text (view |> AVal.map (fun x -> x.Up.ToString("0.00")))]
                Html.row "Sky:"     [Incremental.text (view |> AVal.map (fun x -> x.Sky.ToString("0.00")))]
            ]
        )

module SequencedBookmarksApp = 
    let mutable collectedViews = List.Empty
    let mutable (snapshotProcess : option<System.Diagnostics.Process>) = None
    let mutable timestamps = List<TimeSpan>.Empty

    let private animateFowardAndLocation (pos: V3d) (dir: V3d) (up:V3d) (duration: RelativeTime) (name: string) (record : bool) = 
        {
            (CameraAnimations.initial name) with 
                sample = fun (localTime, globalTime) (state : CameraView) -> // given the state and t since start of the animation, compute a state and the cameraview
                    if localTime < duration then                  
                        let rot      = Rot3d.RotateInto(state.Forward, dir) * localTime / duration |> Rot3d |> Trafo3d
                        let forward  = rot.Forward.TransformDir state.Forward

                        let uprot     = Rot3d.RotateInto(state.Up, up) * localTime / duration |> Rot3d |> Trafo3d
                        let up        = uprot.Forward.TransformDir state.Up
                      
                        let vec       = pos - state.Location
                        let velocity  = vec.Length / duration                  
                        let dir       = vec.Normalized
                        let location  = state.Location + dir * velocity * localTime
                        let view = 
                            state 
                            |> CameraView.withForward forward
                            |> CameraView.withUp up
                            |> CameraView.withLocation location      
                        if record then
                          collectedViews <- collectedViews@[view]
                          timestamps <- timestamps@[System.DateTime.Now.TimeOfDay]
                        Some (state,view)
                    else None
        }

    
    type ProcListBuilder with   
        member x.While(predicate : unit -> bool, body : ProcList<'m,unit>) : ProcList<'m,unit> =
            proclist {
                let p = predicate()
                if p then 
                    yield! body
                    yield! x.While(predicate,body)
                else ()
            }

    //let createWorkerPlay (m : SequencedBookmarks) =
    //    proclist {
    //        while (not m.stopAnimation) do
    //            for i in 0 .. (m.orderList.Length-1) do
    //                do! Proc.Sleep 3000
    //                yield SequencedBookmarksAction.SelectSBM m.orderList.[i]  
    //                yield SequencedBookmarksAction.FlyToSBM m.orderList.[i] 

    //        do! Proc.Sleep 3000
    //        yield SequencedBookmarksAction.AnimationThreadsDone "animationSBPlay"
    //    } 
    let findDelayOrDefault (m : SequencedBookmarks) (id : Guid) =
        match HashMap.tryFind id m.animationInfo with
        | Some info -> 
            info.delay.value
        | None ->
            Log.warn "[Sequenced Bookmarks] No animation info found for bookmark %s" (id |> string)
            2.0

    let createWorkerPlay (m : SequencedBookmarks) =
        proclist {

            if m.stopAnimation then
                for i in 0 .. (m.orderList.Length-1) do

                    m.blockingCollection.Enqueue (SequencedBookmarksAction.AnimStep m.orderList.[i])
                    

            while not m.blockingCollection.IsCompleted do
                let! action = m.blockingCollection.TakeAsync()
                Log.line "[animationSB] take async"
                match action with
                | Some (SequencedBookmarksAction.AnimStep id) -> 
                    let delay = findDelayOrDefault m id
                    yield (SequencedBookmarksAction.AnimStep id)
                    do! Proc.Sleep ((int)(m.animationSpeed.value + delay) * 1000) //3000
                    ()
                | Some a ->
                    Log.line "[Sequenced Bookmarks] Animation step with default delay."
                    yield a
                    do! Proc.Sleep ((int)(m.animationSpeed.value + 1.0) * 1000) //3000
                    ()

                | None -> ()

            //do! Proc.Sleep 3000
            yield SequencedBookmarksAction.AnimationThreadsDone "animationSBPlay"
        } 



    let createWorkerForward (m : SequencedBookmarks) =
         proclist {
            match m.selectedBookmark with
            | Some id ->
                let index = m.orderList |> List.toSeq |> Seq.findIndex(fun x -> x = id)
                if ((index+1) > (m.orderList.Length-1)) then
                    yield SequencedBookmarksAction.AnimStep m.orderList.[0]
                else
                    yield SequencedBookmarksAction.AnimStep m.orderList.[index+1]
                let delay = findDelayOrDefault m id
                  
                do! Proc.Sleep ((int)(m.animationSpeed.value + delay) * 1000) //3000
                yield SequencedBookmarksAction.AnimationThreadsDone "animationSBForward"
            | None -> ()
        }

    let createWorkerBackward (m : SequencedBookmarks) =
        proclist {
           match m.selectedBookmark with
           | Some id ->
               let index = m.orderList |> List.toSeq |> Seq.findIndex(fun x -> x = id)
               if ((index-1) < 0) then
                   yield SequencedBookmarksAction.AnimStep m.orderList.[m.orderList.Length-1]
               else
                   yield SequencedBookmarksAction.AnimStep m.orderList.[index-1] 
               let delay = findDelayOrDefault m id
               do! Proc.Sleep ((int)(m.animationSpeed.value + delay) * 1000) //3000
               yield SequencedBookmarksAction.AnimationThreadsDone "animationSBBackward"
           | None -> ()
       }

    let getNewBookmark (camState : CameraView) (navigationMode : NavigationMode) (exploreCenter : V3d) (count:int) =
        let name = sprintf "Bookmark #%d" count //todo to make useful unique names
        {
            version        = Bookmark.current
            key            = System.Guid.NewGuid()
            name           = name
            cameraView     = camState
            navigationMode = navigationMode
            exploreCenter  = exploreCenter
        }

    let insertGuid (id: Guid) (index : int) (orderList: List<Guid>) =

        let rec insert v i l =
            match i, l with
            | 0, xs -> v::xs
            | i, x::xs -> x::insert v (i - 1) xs
            | i, [] -> failwith "index out of range"
        insert id index orderList

    let removeGuid (index : int) (orderList: List<Guid>) =

        let rec remove i l =
            match i, l with
            | 0, x::xs -> xs
            | i, x::xs -> x::remove (i - 1) xs
            | i, [] -> failwith "index out of range"
        remove index orderList

    let selectSBookmark (m : SequencedBookmarks) (id : Guid) =
        let sbm = m.bookmarks |> HashMap.tryFind id
        match sbm, m.selectedBookmark with
        | Some a, Some b ->
            if a.key = b then 
                { m with selectedBookmark = None }
            else 
                { m with selectedBookmark = Some a.key }
        | Some a, None -> 
            { m with selectedBookmark = Some a.key }
        | None, _ -> m

    let update 
        (m               : SequencedBookmarks) 
        (act             : SequencedBookmarksAction) 
        (navigationModel : Lens<'a,NavigationModel>) 
        (animationModel  : Lens<'a,AnimationModel>)     
        (outerModel      : 'a) : ('a * SequencedBookmarks) =

        match act with
        | AddSBookmark ->
            let nav = Optic.get navigationModel outerModel
            let newSBm = 
                getNewBookmark nav.camera.view nav.navigationMode nav.exploreCenter m.bookmarks.Count
            let oderList' = m.orderList@[newSBm.key]
            let m = {m with bookmarks = m.bookmarks |> HashMap.add newSBm.key newSBm;
                            animationInfo = m.animationInfo 
                                                |> HashMap.add newSBm.key 
                                                               {bookmark = newSBm.key; delay = SequencedBookmarks.initDelay}
                            orderList = oderList';
                            selectedBookmark = Some newSBm.key}
            outerModel, m

        | SequencedBookmarksAction.FlyToSBM id ->
            let _bm = m.bookmarks |> HashMap.tryFind id
            match _bm with 
            | Some bm ->
                let anim = Optic.get animationModel outerModel
                let animationMessage = 
                    animateFowardAndLocation bm.cameraView.Location bm.cameraView.Forward
                                             bm.cameraView.Up 2.0 "ForwardAndLocation2s" m.isRecording
                let anim' = AnimationApp.update anim (AnimationAction.PushAnimation(animationMessage))
                let newOuterModel = Optic.set animationModel anim' outerModel
                newOuterModel, m
            | None -> outerModel, m

        | RemoveSBM id -> 
            let selSBm = 
                match m.selectedBookmark with
                | Some key -> if key = id then None else Some id
                | None -> None

            let bookmarks' = m.bookmarks |> HashMap.remove id
            let index = m.orderList |> List.toSeq |> Seq.findIndex(fun x -> x = id)
            let orderList' = removeGuid index m.orderList 
            outerModel, { m with bookmarks = bookmarks'; orderList = orderList'; selectedBookmark = selSBm; }
       
        | SelectSBM id ->
            outerModel, selectSBookmark m id 
        | MoveUp id ->
            let index = m.orderList |> List.toSeq |> Seq.findIndex(fun x -> x = id)
            let orderList' =
                if index > 0 then
                    if (index-1) > 0 then
                        let part0 = [for x in 0..(index-2) do yield m.orderList.[x]]
                        if index < (m.orderList.Length-1) then
                            let part1 = [for x in (index+1)..(m.orderList.Length-1) do yield m.orderList.[x]]
                            part0@[m.orderList.[index]]@[m.orderList.[index-1]]@part1
                        else
                            part0@[m.orderList.[index]]@[m.orderList.[index-1]]
                    else
                        if index < (m.orderList.Length-1) then
                            let part1 = [for x in (index+1)..(m.orderList.Length-1) do yield m.orderList.[x]]
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
                        let partMax = [for x in (index+2)..(m.orderList.Length-1) do yield m.orderList.[x]]
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
        | SequencedBookmarksAction.PropertiesMessage msg ->  
            match m.selectedBookmark with
            | Some id -> 
                let sbm = m.bookmarks |> HashMap.tryFind id
                match sbm with
                | Some sb ->
                    let bookmark = (SequencedBookmarksProperties.update sb msg)
                    let bookmarks' = m.bookmarks |> HashMap.alter sb.key (function | Some _ -> Some bookmark | None -> None )
                    outerModel, { m with bookmarks = bookmarks'} 
                | None -> outerModel, m
            | None -> outerModel, m
        | Play ->
            if m.stopAnimation then 
                m.blockingCollection.Start()
            else
                m.blockingCollection.Restart() 
            outerModel, { m with animationThreads   = ThreadPool.start ( m |> createWorkerPlay) m.animationThreads; stopAnimation = true}
        | StepForward -> 
            outerModel, { m with animationThreads = ThreadPool.start ( m |> createWorkerForward) m.animationThreads} //; stopAnimation = false}
        | StepBackward -> 
            outerModel, { m with animationThreads = ThreadPool.start ( m |> createWorkerBackward) m.animationThreads}//; stopAnimation = false}
        | AnimationThreadsDone id ->  
            let m' = 
                { m with animationThreads = ThreadPool.remove id m.animationThreads }
            outerModel, m'
        | Pause ->
            m.blockingCollection.CompleteAdding() 
            outerModel, { m with stopAnimation = false}
        | Stop ->
            m.blockingCollection.CompleteAdding() 
            outerModel, { m with stopAnimation = true}
        | AnimStep id ->
            let m' = selectSBookmark m id 
            let _bm = m'.bookmarks |> HashMap.tryFind id
            match _bm with 
            | Some bm ->
                let anim = Optic.get animationModel outerModel
                let animationMessage = 
                    animateFowardAndLocation bm.cameraView.Location bm.cameraView.Forward 
                                             bm.cameraView.Up m'.animationSpeed.value 
                                             "ForwardAndLocation2s" m.isRecording
                let anim' = AnimationApp.update anim (AnimationAction.PushAnimation(animationMessage))
                let newOuterModel = Optic.set animationModel anim' outerModel
                newOuterModel, m'
            | None -> outerModel, m
        | SetDelay (id, s) -> 
            let updInfo (info : option<BookmarkAnimationInfo>) =
                match info with
                | Some info -> 
                    let delay = Numeric.update info.delay s
                    let info = {info with delay = delay}
                    info 
                | None -> {bookmark = id; delay = Numeric.init} 
            let infos = HashMap.update id updInfo m.animationInfo
                //if delay.value < m.animationSpeed.value then
                //    outerModel, { m with delay = delay; animationSpeed = Numeric.update m.animationSpeed s}
                //else
            outerModel, { m with animationInfo = infos}

        | SetAnimationSpeed s -> 
            let duration = Numeric.update m.animationSpeed s
            //if m.delay.value < duration.value then
            //    outerModel, { m with animationSpeed = duration; delay = Numeric.update m.delay s}
            //else
            outerModel, { m with animationSpeed = duration}
        | StartRecording -> 
            collectedViews <- List.empty
            outerModel, {m with isRecording = true}
        | StopRecording -> 
            outerModel, {m with isRecording = false}
        | ToggleGenerateOnStop ->
            outerModel, {m with generateOnStop = not m.generateOnStop}
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
                | [] -> "Please click to select output path"
                | head::tail -> head
            outerModel, {m with outputPath = str}
        |_-> outerModel, m


    let threads (m : SequencedBookmarks) = m.animationThreads


    module UI =
        
        let viewSequencedBookmarks
            (m : AdaptiveSequencedBookmarks) =

            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div itemAttributes (
                alist {

                    let! selected = m.selectedBookmark
                    
                    let! order = m.orderList
                    
                    for id in order do 
                        
                        let! bookmark = m.bookmarks |> AMap.find id
                        let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
                   
                        let color =
                            match selected with
                              | Some sel -> 
                                AVal.constant (if sel = id then C4b.VRVisGreen else C4b.Gray) 
                              | None -> AVal.constant C4b.Gray

                        let index = order |> List.findIndex(fun x -> x = id)
                        let headerText = 
                            //AVal.map (fun a -> sprintf "%s" (a + " Index:" + index.ToString())) bookmark.name
                            AVal.map (fun a -> sprintf "%s" (a)) bookmark.name

                        let headerAttributes =
                            amap {
                                yield onClick (fun _ -> SelectSBM id)
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
        
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToSBM id) ][]
                                            |> UI.wrapToolTip DataPosition.Bottom "fly to bookmark"          
        
                                        //yield Incremental.i toggleMap AList.empty 
                                        //|> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                        yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveSBM id) ][] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Remove"     

                                        yield i [clazz "arrow alternate circle up outline icon"; onClick (fun _ -> MoveUp id) ][] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Move up"
                                        
                                        yield i [clazz "arrow alternate circle down outline icon"; onClick (fun _ -> MoveDown id) ][] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Move down"
                                   
                                    } 
                                )                                     
                            ]
                        ]
                     
                } )

        let viewGUI  (model : AdaptiveSequencedBookmarks) = 

            div [clazz "ui buttons inverted"] [
                        //onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                            button [clazz "ui icon button"; onMouseClick (fun _ -> AddSBookmark )] [ //
                                    i [clazz "plus icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Add Bookmark"
                            
                        // )
                    ] 

        let viewDelay (animationInfo : option<AdaptiveBookmarkAnimationInfo>) = 
            match animationInfo with
            | Some animationInfo ->
                let delayGui = 
                    animationInfo.bookmark 
                        |> AVal.map (fun bm -> 
                                        require GuiEx.semui (
                                            Html.table [  
                                                Html.row "Delay (s):"  [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]  
                                                                                      animationInfo.delay 
                                                                            |> UI.map (fun x -> SetDelay (bm, x) )]
                                            ]))
                        |> AList.ofAValSingle
                Incremental.div ([] |> AttributeMap.ofList) delayGui
            | None -> div [] []

        let viewProperties (model : AdaptiveSequencedBookmarks) =
            adaptive {
                let! selBm = model.selectedBookmark
                //let! delay = HashMap.tryFind selBm. model.animationInfo
                let empty = div[ style "font-style:italic"][ text "no bookmark selected" ] |> UI.map SequencedBookmarksAction.PropertiesMessage 
                
                match selBm with
                | Some id -> 
                    let! scB = model.bookmarks |> AMap.tryFind id 
                    match scB with
                    | Some s -> 
                        let bmProps = (SequencedBookmarksProperties.view s |> UI.map SequencedBookmarksAction.PropertiesMessage)
                        let! info = AMap.tryFind id model.animationInfo
                        let delayGui = viewDelay info
                        return div [] [bmProps;delayGui]
                    | None -> 
                        return empty
                | None -> return empty
            }  

        
            
        let viewAnimationGUI (model:AdaptiveSequencedBookmarks) = 
            require GuiEx.semui (
                Html.table [               
                  Html.row "Animation:"   [div [clazz "ui buttons inverted"] [
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> StepBackward )] [ //
                                                  i [clazz "step backward icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Back"
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> Play )] [ //
                                                  i [clazz "play icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Start Animation"
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> Pause )] [ //
                                                  i [clazz "pause icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Pause Animation"
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> Stop )] [ //
                                                  i [clazz "stop icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Stop Animation"
                                              button [clazz "ui icon button"; onMouseClick (fun _ -> StepForward )] [ //
                                                  i [clazz "step forward icon"] [] ] |> UI.wrapToolTip DataPosition.Bottom "Forward"
                                          ] ]
                  Html.row "Duration (s):"   [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]  model.animationSpeed |> UI.map SetAnimationSpeed ]
                  //Html.row "Delay (s):"  [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]  model.delay |> UI.map SetDelay ]
                  
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
                    
            require GuiEx.semui (
                div [] [
                    Html.table [            
                        Html.row "Record camera animation:" [
                                Incremental.div ([] |> AttributeMap.ofList) recordingButton          
                            ]
                        Html.row "Generate images:" 
                            [
                                Incremental.div ([] |> AttributeMap.ofList) generateToggleButton         
                            ]
                        
                        Html.row "Always generate images after recording:"  
                            [
                                GuiEx.iconCheckBox model.generateOnStop ToggleGenerateOnStop; 
                                    i [clazz "info icon"] [] 
                                        |> UI.wrapToolTip DataPosition.Bottom "Automatically starts image generation with default parameters when clicking on the red stop recording button."
                            ]                        

                        Html.row "Image Resolution:"  
                            [
                                Numeric.view' [NumericInputType.InputBox]  model.resolutionX |> UI.map SetResolutionX 
                                Numeric.view' [NumericInputType.InputBox]  model.resolutionY |> UI.map SetResolutionY
                            ]


      
                    ]
                    div [   style "word-break: break-all"
                            
                            Dialogs.onChooseFiles SetOutputPath;
                            clientEvent "onclick" (Dialogs.jsSelectPathDialog)
                    ] [Incremental.text model.outputPath]
                ]
            )            
       

  