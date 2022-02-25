module TopLevelApp

open System.Collections.Concurrent

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Application

open PRo3D
open PRo3D.Viewer

open Model
open Provenance
open Abstraction.State
open Abstraction.Message
open Story
open Annotations
open View
open Preview

[<AutoOpen>]
module private Helpers =

    let getModelFromHovered (p : Provenance) (model : AppModel) =
        let s = p.hovered |> Option.defaultValue p.tree
                          |> ZTree.value
                          |> Node.state

        s |> State.restore model

    let ignoreMessage (a : AppAction) (model : Model) =
        Model.isAnimating model ||
        Model.isPreview model ||
        (model.story.presentation && not (OMessage.isCamera a))

[<AutoOpen>]
module private Events =
    let onResize' (cb : list<V2i -> 'msg>) =
        onEvent' "onresize" ["{ X: $(document).width(), Y: $(document).height() }"] (fun args ->
            args |> List.head |> Pickler.json.UnPickleOfString
                 |> (fun s -> cb |> List.map (fun f -> f s))
                 |> Seq.ofList
        )

    let onFocus' (cb : list<V2i -> 'msg>) =
        onEvent' "onfocus" ["{ X: $(document).width(), Y: $(document).height()  }"] (fun args ->
            args |> List.head |> Pickler.json.UnPickleOfString
                 |> (fun s -> cb |> List.map (fun f -> f s))
                 |> Seq.ofList
        )

let presentationModeConfig =
    config {
        content (element { id "render"; isCloseable false })
        useCachedConfig false
    }

let initial (runtime: IRuntime) (signature: IFramebufferSignature) (startEmpty : bool) messagingMailbox minervaMailbox =
    let model = ViewerApp.initial runtime signature startEmpty messagingMailbox minervaMailbox in {
        inner = { current = model; preview = None; output = model }
        dockConfig = Viewer.dockConfigInitial
        dockConfigAuthoring = Viewer.dockConfigInitial
        provenance = ProvenanceApp.init <| State.create model
        story = StoryApp.init
        view = ViewApp.init <| AppModel.getViewParams model
        bookmarks = BookmarkApp.init
        renderControlSize = V2i.One
    }

let update (runtime : IRuntime) (signature: IFramebufferSignature) 
                (sendQueue : BlockingCollection<string>) (mailbox : MessagingMailbox) 
                (model : Model) (act : Action) =

    let rec processMessage (act : Action) (model : Model) =
        match act with
            | ViewAction a ->
                model |> ViewApp.update a

            | ProvenanceAction a ->
                let p = model.provenance |> ProvenanceApp.update model.story model.bookmarks a

                match a with
                    | Goto _ ->
                        let m = p |> Provenance.state |> State.restore model.inner.current

                        model |> StoryApp.update DeselectSlide
                              |> Lens.set (Model.Lens.inner |. InnerModel.Lens.current) m
                              |> ViewApp.update Move

                    | MouseEnter _ ->
                        let m = model.inner.current |> getModelFromHovered p
                        let preview = Preview.model m

                        model |> PreviewApp.update (Start preview)

                    | MouseLeave ->
                        model |> PreviewApp.update Stop

                    | _ ->
                        model

                    |> Lens.set Model.Lens.provenance p
        
            | StoryAction a ->
                model |> StoryApp.update a

            | BookmarkAction a ->
                model |> BookmarkApp.update a

            | SessionAction a ->
                model |> SessionApp.update a
                      |> ViewApp.update Set
                      |> StoryApp.update UpdateFrame

            | AppAction a when not <| ignoreMessage a model ->
                let s = ViewerApp.update runtime signature sendQueue mailbox model.inner.current a

                // Find all possibly relevant messages that were processed
                let messages = s.drawing.processedMessages
                                    |> List.map DrawingMessage
                                    |> fun t -> a :: t
                                    |> List.filter OMessage.isRelevant

                let s = { s with drawing = { s.drawing with processedMessages = [] } }

                let p =
                    match messages with
                        | [] -> 
                            model.provenance

                        | x::_ ->
                            let next = State.create s
                            let current = Provenance.state model.provenance
                            let msg = Message.create current next x

                            model.provenance |> ProvenanceApp.update model.story model.bookmarks (Update (next, msg))
            
                model |> Lens.set (Model.Lens.inner |. InnerModel.Lens.current) s
                      |> Lens.set Model.Lens.provenance p
                      |> ViewApp.update Set
                      |> StoryApp.update UpdateFrame

            | KeyDown Keys.R ->
                { model with dockConfig = Viewer.dockConfigInitial }

            | KeyDown Keys.P when Story.length model.story > 0 ->
                { model with dockConfig = presentationModeConfig }
                    |> StoryApp.update StartPresentation

            | KeyDown Keys.Escape ->
                { model with dockConfig = model.dockConfigAuthoring }
                    |> StoryApp.update EndPresentation

            | KeyDown Keys.Right
            | KeyDown Keys.Enter ->
                model |> StoryApp.update Forward

            | KeyDown Keys.Left
            | KeyDown Keys.Back ->
                model |> StoryApp.update Backward

            | Click ->
                model |> StoryApp.update (model |> (Model.getSceneHit >> SaveTarget >> AnnotationAction))

            | RenderControlResized s ->
                { model with renderControlSize = s }

            | UpdateConfig cfg ->
                { model with dockConfig = cfg
                             dockConfigAuthoring = cfg }

            | _ ->
                model

    // Compute next state
    let next = model |> processMessage act

    // Copy the correct application model to the
    // output field; this is either the current state or a preview
    let outputModel =
        let view = next |> Model.getActiveModel View 
                        |> AppModel.getViewParams

        next |> Model.getActiveModel Model
             |> AppModel.setViewParams view

    next |> Lens.set (Model.Lens.inner |. InnerModel.Lens.output) outputModel

let threads (model : Model) =
    
    // Thread pool for actual application
    let appThreads = model.inner.current |> ViewerApp.threadPool
                                         |> ThreadPool.map AppAction

    // Thread pool for view
    let viewThreads = model |> ViewApp.threads
                            |> ThreadPool.map ViewAction

    // Thread pool for story module                                                 
    let storyThreads = model |> StoryApp.threads
                             |> ThreadPool.map StoryAction

    [appThreads; viewThreads; storyThreads]
        |> ThreadPool.unionMany

let renderView (model : MModel) =
    let showOverlays = model.story.selected |> Mod.map Option.isNone

    let resizeInterval = "window.setInterval(() => $(document).trigger('resize'), 100);"

    onBoot resizeInterval (
        body [ 
            style "background: #1B1C1E"; 
            onKeyDown KeyDown; onKeyUp KeyUp
            onClick (fun _ -> Click)
            onResize' [RenderControlResized; ViewerAction.OnResize >> AppAction]
            onFocus' [ViewerAction.OnResize >> AppAction]
        ] [
            model.inner.output
                |> ViewerApp.view (Some "render") showOverlays
                |> UI.map AppAction

            model |> StoryApp.overlayView
                  |> UI.map StoryAction
        ]
    )

let provenanceView (model : MModel) =
    body [] [
        model.provenance
            |> ProvenanceApp.view model.story model.bookmarks
            |> UI.map ProvenanceAction
    ]

let storyboardView (model : MModel) =
    body [
        onMouseEnter (fun _ -> Story |> Some |> SetNodeReferenceSpace |> ProvenanceAction)
        onMouseLeave (fun _ -> None |> SetNodeReferenceSpace |> ProvenanceAction)
    ] [
        model |> StoryApp.storyboardView
              |> UI.map StoryAction
    ]

let bookmarksView (model : MModel) =
    body [
        onMouseEnter (fun _ -> Bookmarks |> Some |> SetNodeReferenceSpace |> ProvenanceAction)
        onMouseLeave (fun _ -> None |> SetNodeReferenceSpace |> ProvenanceAction)
    ] [
        model |> BookmarkApp.view |> UI.map BookmarkAction  
    ] 

let view (model : MModel) =
    page (fun request ->
        match Map.tryFind "page" request.queryParams with
            | Some "render" -> 
                renderView model     
            | Some "provenance" ->
                provenanceView model
            | Some "storyboard" ->
                storyboardView model
            | Some "bookmarks" ->
                bookmarksView model
            | Some _ as p ->
                model.inner.output |> ViewerApp.view p (Mod.constant true)
                                   |> UI.map AppAction
            | None ->
                div [
                    style "width:100%; height:100%; overflow:hidden"
                ] [
                    Incremental.div AttributeMap.Empty <| alist {
                        let! p = model.story.presentation
                        if not p then
                            yield model.inner.output |> ViewerApp.view None (Mod.constant true)
                                                     |> UI.map AppAction

                            yield SessionApp.view |> UI.map SessionAction
                    }

                    model.dockConfig |> docking [
                        style "width:100%; height:100%; overflow:hidden"
                        onLayoutChanged UpdateConfig
                    ]
                ]
    )
 
let start (runtime: IRuntime) (signature: IFramebufferSignature) (startEmpty : bool) messagingMailbox minervaMailbox sendQueue =       
    App.start {
        unpersist = Unpersist.instance
        threads   = threads
        view      = view //localhost
        update    = update runtime signature sendQueue messagingMailbox
        initial   = initial runtime signature startEmpty messagingMailbox minervaMailbox
    }
