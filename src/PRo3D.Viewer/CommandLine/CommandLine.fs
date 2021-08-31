namespace PRo3D

open Aardvark.Base
//open PRo3D.SimulatedViews
open PRo3D.CommandLineUtils

module CommandLine =
    
    /// parse commandline arguments
    let parseArguments (argv : array<string>) : StartupArgs =    
        
            let b2str b =
                match b with
                | true -> ": yes"
                | false -> ": no"

            
            let useAsyncLoading     = (argv |> hasFlag "sync" |> not)
            let startEmpty          = (argv |> hasFlag "empty")
            let remoteApp           = (argv |> hasFlag "remoteControl")
            let server              = (argv |> hasFlag "server") 
            let magFilter           = not (argv |> hasFlag "noMagFilter")

            let showExplorationCentre = (argv |> Array.contains "--excentre")
            let showReferenceSystem = (argv |> Array.contains "--refsystem")
            let verbose = (argv |> Array.contains "--verbose")
            Log.line "[Arguments] Server mode: %s" (b2str server)
            Log.line "[Arguments] Using linear magnification filtering%s" (b2str magFilter)
            Log.line "[Arguments] Show exploration centre%s" (b2str showExplorationCentre)
            Log.line "[Arguments] Show reference system%s" (b2str showReferenceSystem)
            Log.line "[Arguments] Remote control app%s" (b2str remoteApp)

            let args : StartupArgs = 
                    {
                            
                        showExplorationPoint  = showExplorationCentre
                        verbose               = verbose
                        startEmpty            = startEmpty
                        useAsyncLoading       = false
                        magnificationFilter   = magFilter
                        remoteApp             = remoteApp
                        serverMode            = server
                    }
            args
