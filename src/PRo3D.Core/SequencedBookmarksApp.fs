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

open Aether
open Aether.Operators

module SequencedBookmarksProperties =  

    let update (model : Bookmark) (act : SequencedBookmarksPropertiesAction) =
        match act with
        | SetName s ->
            { model with name = s }

    let view (model:AdaptiveBookmark) =
        let view = model.cameraView
        require GuiEx.semui (
            Html.table [  
                Html.row "Change Name:" [Html.SemUi.textBox model.name SetName ]
                Html.row "Pos:"     [Incremental.text (view |> AVal.map (fun x -> x.Location.ToString("0.00")))] 
                Html.row "LookAt:"  [Incremental.text (view |> AVal.map (fun x -> x.Forward.ToString("0.00")))]
                Html.row "Up:"      [Incremental.text (view |> AVal.map (fun x -> x.Up.ToString("0.00")))]
                Html.row "Sky:"     [Incremental.text (view |> AVal.map (fun x -> x.Sky.ToString("0.00")))]
            ]
        )

module SequencedBookmarksApp = 
    let mutable collectedViews = List<CameraView>.Empty
    let mutable (snapshotProcess : option<System.Diagnostics.Process>) = None
    let mutable timestamps = List<TimeSpan>.Empty
    let mutable names = List<string>.Empty
    let mutable stillFrames = List<int * Guid>.Empty

    module private AnimationSlot =
        let private getName (slot : string) (entity : V2i) =
            Sym.ofString <| sprintf "%A/%s" entity slot

        let camera = Sym.ofString "camera"
        let caption = Sym.ofString "caption"
        //let appearance = getName "appearance"

    type ProcListBuilder with   
        member x.While(predicate : unit -> bool, body : ProcList<'m,unit>) : ProcList<'m,unit> =
            proclist {
                let p = predicate()
                if p then 
                    yield! body
                    yield! x.While(predicate,body)
                else ()
            }

    /// Calculates the frames per second of recorded views based on timestamps
    /// recorded at the same time each view was animated.
    //let calculateFpsOfCurrentTimestamps () =
    //    match timestamps with
    //    | [] -> None
    //    | timestamps ->
    //        let millisPerFrame = 
    //            timestamps 
    //                    |> List.pairwise
    //                    |> List.map (fun (first, second) -> second - first)
    //                    |> List.map (fun time -> time.TotalMilliseconds)
                  
    //        let mediumMpf = millisPerFrame |> List.map (fun x -> x |> round |> int)
    //                                       |> List.sort
    //                                       |> List.item ((millisPerFrame.Length / 2))   

    //        let fps = 
    //            if mediumMpf > 0 then 
    //                1000 / mediumMpf
    //            else
    //                Log.line "[Debug] Medium FPS 0. Number of recorded Frames: %d" timestamps.Length
    //                let debugMillis = millisPerFrame |> List.fold (fun a b -> a + ";" + (string b)) ""
    //                Log.line "[Debug] Millis per frame %s" debugMillis  
    //                Log.line "[Debug] Using default value"  
    //                60
    //        //Log.line "FPS = %i" fps
    //        fps |> Some

    /// Calculate the number of indentical frames that should be generated for
    /// a given bookmark. The number of frames is based on the FPS of the recorded
    /// views and on the delay set for the bookmark.
    //let calculateNrOfStillFrames (m : SequencedBookmarks) =
    //    let fps = calculateFpsOfCurrentTimestamps () 
    //    match fps with
    //    | Some fps ->
    //        let toNrOfFrames (index, id) =
    //            // take delay of previous bookmark
    //            match List.tryFindIndex (fun x -> x = id) m.orderList with
    //            | Some ind -> 
    //                match List.tryItem (ind - 1) m.orderList with
    //                | Some nextId -> 
    //                    match m.animationInfo.TryFind nextId with
    //                    | Some info ->
    //                        (index, int (info.delay.value * (float fps)))
    //                            |> Some
    //                    | None -> None
    //                | None -> None
    //            | None -> None
            
    //        let nrOfFrames =    
    //            stillFrames
    //                |> List.map toNrOfFrames
    //                |> List.filter Option.isSome
    //                |> List.map (fun x -> x.Value)
    //        let nrOfFrames = 
    //            nrOfFrames 
    //                |> List.map (fun (ind, count) -> (ind, {index = ind;repetitions=count}))
    //                |> HashMap.ofList
    //        nrOfFrames
    //    | None -> HashMap.empty

    /// Returns the delay of a bookmark if its id is found in animationInfo. 
    /// Otherwise returns a default value.
    let findDelayOrDefault (m : SequencedBookmarks) (id : Guid) =
        match HashMap.tryFind id m.animationInfo with
        | Some info -> 
            info.delay.value
        | None ->
            Log.warn "[Sequenced Bookmarks] No animation info found for bookmark %s" (id |> string)
            SequencedBookmarks.initDelay.value

    /// Returns the duration of a bookmark if its id is found in animationInfo. 
    /// Otherwise returns a default value.
    let findDurationOrDefault (m : SequencedBookmarks) (id : Guid) =
        match HashMap.tryFind id m.animationInfo with
        | Some info -> 
            info.duration.value
        | None ->
            Log.warn "[Sequenced Bookmarks] No animation info found for bookmark %s" (id |> string)
            SequencedBookmarks.initDuration.value

    let getNewBookmark (camState : CameraView) (navigationMode : NavigationMode) (exploreCenter : V3d) (count:int) =
        let name = sprintf "Bookmark_%d" count //todo to make useful unique names
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

    let moveCursor (f : int -> int) (m : SequencedBookmarks) =
        match m.selectedBookmark with
        | Some key -> 
            let index = List.findIndex (fun x -> x = key) m.orderList
            let nextIndex =
                let x = (f index)
                let x = x % (List.length m.orderList)
                if x < 0 then
                    List.length m.orderList + x
                else x
            HashMap.tryFind m.orderList.[nextIndex] m.bookmarks
        | None -> None

    let next (m : SequencedBookmarks) =
        moveCursor (fun x -> x + 1) m

    let previous (m : SequencedBookmarks) =
        moveCursor (fun x -> x - 1) m

    let selected (m : SequencedBookmarks) =
        match m.selectedBookmark with
        | Some sel ->
            HashMap.tryFind sel m.bookmarks
        | None -> None

    /////// ANEWMATIONS ///////////         
    let smoothPath (views : seq<CameraView>)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) =
        let view_       = navigationModel 
                            >-> NavigationModel.camera_
                            >-> CameraControllerState.view_
        let animate views =
            let durationInSeconds = 5
        
            AnimationCameraPrimitives.Animation.Camera.smoothPath 0.1 views
                    |> Animation.seconds durationInSeconds
                    |> Animation.ease (Easing.InOut EasingFunction.Quadratic)
                    |> Animation.onFinalize (fun name _ m -> 
                                                            Log.line "[Animation] Finished animation."
                                                            m
                                            )
                    |> Animation.onProgress (fun name value model ->
                             Optic.set view_ value model 
                        )

        let animation =
            animate views 

        outerModel
        |> Animator.createAndStart AnimationSlot.camera animation

    let smoothPathAllBookmarks (m : SequencedBookmarks)
                               (navigationModel : Lens<'a,NavigationModel>) 
                               (outerModel      : 'a) =
        let views = 
            m.orderList
                |> List.map (fun id -> HashMap.find id m.bookmarks)
                |> List.map (fun (bm : PRo3D.Base.Bookmark) -> bm.cameraView)

        smoothPath views navigationModel outerModel

    let toBookmark (m : SequencedBookmarks)
                   (navigationModel : Lens<'a,NavigationModel>) 
                   (outerModel      : 'a) 
                   (f : SequencedBookmarks -> option<Bookmark>)=
        match selected m, f m with
        | Some selected, Some next -> 
            let outerModel = smoothPath [selected.cameraView;next.cameraView] navigationModel outerModel
            outerModel, {m with selectedBookmark = Some next.key}
        | _,_ -> 
            Log.line "[SequencedBookmarks] No bookmark selected."
            outerModel, m
                        

    /////// ANEWMATIONS ///////////

    let update 
        (m               : SequencedBookmarks) 
        (act             : SequencedBookmarksAction) 
        (navigationModel_ : Lens<'a,NavigationModel>) 
        (animator_       : Lens<'a,Animator<'a>>)     
        (outerModel      : 'a) : ('a * SequencedBookmarks) =

        match act with
        | AddSBookmark ->
            let nav = Optic.get navigationModel_ outerModel
            let newSBm = 
                getNewBookmark nav.camera.view nav.navigationMode nav.exploreCenter m.bookmarks.Count
            let oderList' = m.orderList@[newSBm.key]
            let m = {m with bookmarks = m.bookmarks |> HashMap.add newSBm.key newSBm;
                            animationInfo = m.animationInfo 
                                                |> HashMap.add newSBm.key 
                                                               {bookmark = newSBm.key
                                                                delay = SequencedBookmarks.initDelay
                                                                duration = SequencedBookmarks.initDuration}
                            orderList = oderList';
                            selectedBookmark = Some newSBm.key}
            outerModel, m

        | SequencedBookmarksAction.FlyToSBM id ->
            let _bm = m.bookmarks |> HashMap.tryFind id
            match _bm with 
            | Some bm ->
                //TODO

                //let anim = Optic.get animationModel outerModel
                //let animationMessage = 
                //    animateFowardAndLocation bm.cameraView.Location bm.cameraView.Forward
                //                             bm.cameraView.Up 2.0 "ForwardAndLocation2s" m.isRecording 
                //                             bm.name bm.key
                //let anim' = AnimationApp.update anim (AnimationAction.PushAnimation(animationMessage))
                //let newOuterModel = Optic.set animationModel anim' outerModel
                outerModel, m
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
            let outerModel = 
                if Animator.exists AnimationSlot.camera outerModel then
                    Animator.startOrResume AnimationSlot.camera outerModel
                else 
                    smoothPathAllBookmarks m navigationModel_ outerModel
            outerModel, m
        | StepForward -> 
            toBookmark m navigationModel_ outerModel next
        | StepBackward ->
            toBookmark m navigationModel_ outerModel previous
        | Pause ->
            let outerModel = Animator.pause AnimationSlot.camera outerModel
            outerModel, m 
        | Stop ->
            let outerModel = Animator.stop AnimationSlot.camera outerModel
            outerModel, m 
        | SetDelay (id, s) -> 
            let updInfo (info : option<BookmarkAnimationInfo>) =
                match info with
                | Some info -> 
                    let delay = Numeric.update info.delay s
                    let info = {info with delay = delay}
                    info 
                | None -> { bookmark = id
                            delay    = SequencedBookmarks.initDelay
                            duration = SequencedBookmarks.initDuration
                          } 
            let infos = HashMap.update id updInfo m.animationInfo
                //if delay.value < m.animationSpeed.value then
                //    outerModel, { m with delay = delay; animationSpeed = Numeric.update m.animationSpeed s}
                //else
            outerModel, { m with animationInfo = infos}
        | SetDuration (id, s) ->
            let updInfo (info : option<BookmarkAnimationInfo>) =
                match info with
                | Some info -> 
                    let duration = Numeric.update info.duration s
                    let info = {info with duration = duration}
                    info 
                | None -> { bookmark = id
                            delay    = SequencedBookmarks.initDelay
                            duration = SequencedBookmarks.initDuration
                          } 
            let infos = HashMap.update id updInfo m.animationInfo
                //if delay.value < m.animationSpeed.value then
                //    outerModel, { m with delay = delay; animationSpeed = Numeric.update m.animationSpeed s}
                //else
            outerModel, { m with animationInfo = infos}
        | StartRecording -> 
            //collectedViews <- List.empty
            //timestamps <- List<TimeSpan>.Empty
            //names <- List<string>.Empty
            //stillFrames <- List<int * Guid>.Empty
            outerModel, {m with isRecording = true}
        | StopRecording -> 
            //let currentFps  = 
            //    if timestamps.Length > 0 then
            //        calculateFpsOfCurrentTimestamps ()
            //    else m.currentFps
            outerModel, {m with isRecording = false
                                //currentFps  = currentFps           
                        }
        | ToggleGenerateOnStop ->
            outerModel, {m with generateOnStop = not m.generateOnStop}
        | ToggleRenderStillFrames ->
            outerModel, {m with renderStillFrames = not m.renderStillFrames}
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
        
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToSBM id)] []
                                            |> UI.wrapToolTip DataPosition.Bottom "fly to bookmark"          
        
                                        //yield Incremental.i toggleMap AList.empty 
                                        //|> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                        yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveSBM id)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Remove"     

                                        yield i [clazz "arrow alternate circle up outline icon"; onClick (fun _ -> MoveUp id)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Move up"
                                        
                                        yield i [clazz "arrow alternate circle down outline icon"; onClick (fun _ -> MoveDown id)] [] 
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
                                                Html.row "Duration (s):" [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]  
                                                                            animationInfo.duration 
                                                                                |> UI.map (fun x -> SetDuration (bm, x) )]
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
                    |> UI.wrapToolTip DataPosition.Bottom "Only available if \"Alllow JSON Editing\" is selected. Updates the settings for image generation in the JSON file. Manual changes to the JSON file will be overwritten!"
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
                        Html.row "Record Camera Animation:" 
                            [
                                Incremental.div ([] |> AttributeMap.ofList) recordingButton          
                            ]
                        Html.row "Generate Images:" 
                            [
                                Incremental.div ([] |> AttributeMap.ofList) generateToggleButton         
                            ]
                        //Html.row "Always generate images after recording:"  
                        //    [
                        //        GuiEx.iconCheckBox model.generateOnStop ToggleGenerateOnStop; 
                        //            i [clazz "info icon"] [] 
                        //                |> UI.wrapToolTip DataPosition.Bottom "Automatically starts image generation with default parameters when clicking on the red stop recording button."
                        //    ]     
                            
                        Html.row "Generate Still Frames" 
                            [GuiEx.iconCheckBox model.renderStillFrames ToggleRenderStillFrames;
                                    i [clazz "info icon"] [] 
                                        |> UI.wrapToolTip DataPosition.Bottom "Renders the appropriate number of identical images when the camera is standing still."
                            ]
                        Html.row "Allow JSON Editing" 
                            [GuiEx.iconCheckBox (model.updateJsonBeforeRendering |> AVal.map not) ToggleUpdateJsonBeforeRendering;
                                i [clazz "info icon"] [] 
                                    |> UI.wrapToolTip DataPosition.Bottom "If selected, the JSON file will NOT be updated before rendering. If you change settings in the user interface after recording, they will not be reflected in the JSON file."
                            ]
                        Html.row "Update JSON" 
                            [
                                updateJsonButton
                                
                            ]
                        Html.row "Debug" 
                            [GuiEx.iconCheckBox model.debug ToggleDebug;
                                    i [clazz "info icon"] [] 
                                        |> UI.wrapToolTip DataPosition.Bottom "Leaves the window with information about the rendering process open after rendering and uses verbose mode."
                            ]

                        Html.row "Image Resolution:"  
                            [
                                Numeric.view' [NumericInputType.InputBox]  model.resolutionX |> UI.map SetResolutionX 
                                Numeric.view' [NumericInputType.InputBox]  model.resolutionY |> UI.map SetResolutionY
                            ]

                        Html.row "Current FPS" [Incremental.text fpsText]
                        Html.row "FPS Setting" [Html.SemUi.dropDown model.fpsSetting SetFpsSetting]
                        Html.row "Output Path" 
                            [
                                outputFolderGui
                            ]
                    ]

                ]
            )            
       

  