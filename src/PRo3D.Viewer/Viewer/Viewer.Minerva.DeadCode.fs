this code was in Viewer.fs (update for KeyDown)


            //correlations
            let m = 
                match (k, m.interaction) with
                | (Aardvark.Application.Keys.Enter, Interactions.PickAnnotation) -> 
                        
                    //let selected =
                    //    m.drawing.annotations.selectedLeaves 
                    //    |> HashSet.map (fun x -> x.id)

                    //let correlationPlot = 
                    //    CorrelationPanelsApp.update
                    //        m.correlationPlot 
                    //        m.scene.referenceSystem
                    //        (LogAssignCrossbeds selected)

                    //{ m with correlationPlot = correlationPlot; pastCorrelation = Some m.correlationPlot } |> shortFeedback "crossbeds assigned"                       
                    m
                | (Aardvark.Application.Keys.Enter, Interactions.DrawLog) -> 
                    ////confirm when in logpick mode
                    //let correlationPlot = 
                    //    CorrelationPanelsApp.update 
                    //        m.correlationPlot 
                    //        m.scene.referenceSystem
                    //        (UpdateAnnotations m.drawing.annotations.flat)
                                                                           
                    //let correlationPlot, msg =
                    //    match m.correlationPlot.logginMode with
                    //    | LoggingMode.PickLoggingPoints ->                                                                  
                    //        CorrelationPlotAction.FinishLog
                    //        |> CorrelationPanelsMessage.CorrPlotMessage
                    //        |> CorrelationPanelsApp.update correlationPlot m.scene.referenceSystem, "finished log"                                
                    //    | LoggingMode.PickReferencePlane ->
                    //        correlationPlot, "reference plane selected"

                    //let correlationPlot = 
                    //    CorrelationPanelsApp.update 
                    //        correlationPlot 
                    //        m.scene.referenceSystem
                    //        LogConfirm
                            
                    //{ m with correlationPlot = correlationPlot; pastCorrelation = Some m.correlationPlot } |> shortFeedback msg
                    m
                | (Aardvark.Application.Keys.Escape,Interactions.DrawLog) -> 
                    //let panelUpdate = 
                    //    CorrelationPanelsApp.update 
                    //        m.correlationPlot
                    //        m.scene.referenceSystem
                    //        CorrelationPanelsMessage.LogCancel
                    //{ m with correlationPlot = panelUpdate } |> shortFeedback "cancel log"
                    m
                | (Aardvark.Application.Keys.Back, Interactions.DrawLog) ->                     
                    //let panelUpdate = 
                    //    CorrelationPanelsApp.update
                    //        m.correlationPlot
                    //        m.scene.referenceSystem
                    //        CorrelationPanelsMessage.RemoveLastPoint
                    //{ m with correlationPlot = panelUpdate } |> shortFeedback "removed last point"
                    m
                | (Aardvark.Application.Keys.B, Interactions.DrawLog) ->                     
                    //match m.pastCorrelation with
                    //| None -> m
                    //| Some past -> { m with correlationPlot = past; pastCorrelation = None} |> shortFeedback "undo last correlation"
                    m
                | _ -> m

            let m = 
                match k with 
                | Aardvark.Application.Keys.Space ->
                    //let wp = {
                    //    name = sprintf "wp %d" m.waypoints.Count
                    //    cv = (_camera.Get m).view
                    //}

                    //Serialization.save "./logbrush" m.correlationPlot.logBrush |> ignore

                    //let waypoints = IndexList.append wp m.waypoints
                    //Log.line "saving waypoints %A" waypoints
                    //Serialization.save "./waypoints.wps" waypoints |> ignore
                    //{ m with waypoints = waypoints }                                                                                  
                    m |> shortFeedback "Saved logbrush"
                | Aardvark.Application.Keys.F8 ->
                    { m with scene = { m.scene with dockConfig = DockConfigs.m2020 } }
                | _ -> m