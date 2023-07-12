namespace SimulatedViews


open Aardvark.Base
open Aardvark.Base.Geometry
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open FShade
open Aardvark.Rendering
open Aardvark.UI

open PRo3D
open PRo3D.Viewer
open System.Collections.Concurrent

    module PRo3DUtils =

        let start 
            (runtime             : IRuntime) 
            (signature           : IFramebufferSignature)
            (startEmpty          : bool)
            (messagingMailbox    : MessagingMailbox)
            (sendQueue           : BlockingCollection<string>)
            (dumpFile            : string)
            (cacheFile           : string)
            (renderingUrl        : string)
            (dataSamples         : int)
            (screenshotDirectory : string)
            (viewerVersion       : string) =

            let startupArgs = 
                {StartupArgs.initArgs with isBatchRendering = true}
            let m = 
                if startEmpty |> not then
                    PRo3D.Viewer.Viewer.initial messagingMailbox startupArgs renderingUrl 
                                                dataSamples screenshotDirectory ViewerLenses._animator
                                                viewerVersion
                    |> SceneLoader.loadLastScene runtime signature                
                    |> SceneLoader.loadLogBrush
                    |> ViewerIO.loadRoverData                
                    |> ViewerIO.loadAnnotations
                    |> ViewerIO.loadCorrelations
                    |> ViewerIO.loadLastFootPrint
                    //|> ViewerIO.loadMinerva dumpFile cacheFile
                    //|> ViewerIO.loadLinking
                    |> SceneLoader.addScaleBarSegments
                    |> SceneLoader.addGeologicSurfaces
                else
                    PRo3D.Viewer.Viewer.initial messagingMailbox StartupArgs.initArgs renderingUrl
                                                dataSamples screenshotDirectory ViewerLenses._animator
                                                viewerVersion
                    |> ViewerIO.loadRoverData

            SimulatedViews.AppExtension.start' {
                unpersist = Unpersist.instance
                threads   = ViewerApp.threadPool
                view      = ViewerApp.view runtime //localhost
                update    = ViewerApp.updateInternal runtime signature sendQueue messagingMailbox
                initial   = m
            }