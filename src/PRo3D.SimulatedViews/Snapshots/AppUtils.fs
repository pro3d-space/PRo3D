namespace SimulatedViews

open System
open System.Threading
open System.Collections.Generic
open Aardvark.Base
open FSharp.Data.Adaptive
open System.Reactive.Subjects
open Aardvark.UI

type private Message<'msg> = { msgs : seq<'msg>; processed : Option<System.Threading.ManualResetEventSlim> }

/// waiting for integration in Aardvark Media
module AppExtension =
    type App<'model, 'mmodel, 'msg> 
        with member app.start'() =
                let l = obj()
                let initial = app.initial
                let state = AVal.init initial
                let mstate = app.unpersist.create initial
                let initialThreads = app.threads initial
                let node = app.view mstate

                let mutable running = true
                let messageQueue = List<Message<'msg>>(128)
                let subject = new Subject<'msg>()

                let mutable currentThreads = ThreadPool.empty
        

                let update (source : Guid) (msgs : seq<'msg>) =
                    //use mri = new System.Threading.ManualResetEventSlim()
                    lock messageQueue (fun () ->
                        messageQueue.Add { msgs = msgs; processed = None }
                        Monitor.Pulse messageQueue
                    )
                    //  mri.Wait()

                let rec updateSync (source : Guid) (msgs : seq<'msg>) =
                    doit [{ msgs = msgs; processed = None }] // TODO: gh, what can we do about this deadlock problem agains render service thread.

                and adjustThreads (newThreads : ThreadPool<'msg>) =
                    let merge (id : string) (oldThread : Option<Command<'msg>>) (newThread : Option<Command<'msg>>) : Option<Command<'msg>> =
                        match oldThread, newThread with
                            | Some o, None ->
                                o.Stop()
                                newThread
                            | None, Some n -> 
                                n.Start(emit)
                                newThread
                            | Some o, Some n ->
                                oldThread
                            | None, None -> 
                                None
            
                    currentThreads <- ThreadPool<'msg>(HashMap.choose2 merge currentThreads.store newThreads.store)


                and doit(msgs : list<Message<'msg>>) =
                    let messagesForward = System.Collections.Generic.List<_>()
                    lock l (fun () ->
                        if Config.shouldTimeUnpersistCalls then Log.startTimed "[Aardvark.UI] update/adjustThreads/unpersist"

                        if not (List.isEmpty msgs) then
                            transact (fun () ->
                                let mutable newState = state.Value
                                for msg in msgs do
                                    for msg in msg.msgs do
                                        newState <-
                                            try 
                                                app.update state.Value msg
                                            with e -> 
                                                Log.error "[media] update function failed with: %A" e
                                                state.Value

                                        let newThreads = app.threads newState
                                        adjustThreads newThreads
                        
                                        state.Value <- newState

                                        messagesForward.Add(msg)
                        
                                    // if somebody awaits message processing, trigger it
                                    msg.processed |> Option.iter (fun mri -> mri.Set())
                        
                                app.unpersist.update mstate newState
                            )

                        if Config.shouldTimeUnpersistCalls then Log.stop ()
                    )
                    for m in messagesForward do 
                        subject.OnNext(m)

                and emit (msg : 'msg) =
                    lock messageQueue (fun () ->
                        messageQueue.Add { msgs = Seq.singleton msg; processed = None }
                        Monitor.Pulse messageQueue
                    )


                // start initial threads
                adjustThreads initialThreads

                let updateThread =
                    let update () = 
                        while running do
                            Monitor.Enter(messageQueue)
                            while running && messageQueue.Count = 0 do
                                Monitor.Wait(messageQueue) |> ignore
                    
                            let messages = 
                                if running then 
                                    let messages = messageQueue |> CSharpList.toList
                                    messages
                                else      
                                    []      
                            
                            messageQueue.Clear()                 
                            
                            Monitor.Exit(messageQueue)

                            match messages with
                                | [] -> ()
                                | _ -> doit messages

                    Thread(ThreadStart update)

                updateThread.Name <- "[Aardvark.Media.App] updateThread"
                updateThread.IsBackground <- true
                updateThread.Start()

                let shutdown () =
                    running <- false
                    lock messageQueue (fun () -> Monitor.PulseAll messageQueue)
                    updateThread.Join()
                    subject.OnCompleted()
                    subject.Dispose()

                ({
                    lock = l
                    model = state
                    ui = node
                    update = update
                    updateSync = updateSync
                    shutdown = shutdown
                    messages = subject
                }, mstate)

    /// Exposes MModel. 
    /// (waiting for integration in Aardvark Media)
    let start' (app : App<'model, 'mmodel, 'msg>) = app.start'()
