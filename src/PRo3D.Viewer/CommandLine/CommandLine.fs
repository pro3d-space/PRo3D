namespace PRo3D

open Aardvark.Base
//open PRo3D.SimulatedViews
open PRo3D.CommandLineUtils

module CommandLine =
    

    let printHelp () = 
        Log.line @"PRo3D COMMANDLINE OPTIONS\n"
        Log.line @"--help                              show help"
        Log.line @"--obj [path];[path];[path];[...]    load OBJ(s) from one or more paths"
        Log.line @"--opc [path];[path];[path];[...]    load OPC(s) from one or more paths"
        Log.line @"--asnap [path\snapshot.json]        path to a snapshot file, refer to PRo3D User Manual for the correct format"
        Log.line @"--out [path]                        path to a folder where output images will be saved; if the folder does not exist it will be created"
        Log.line @""
        //Log.lin@e "--gui [none/small/complete]         show no gui / show only render view / show complete gui; default is small"
        Log.line @"--renderDepth                       render the depth map as well and save it as an additional image file"      
        Log.line @"--exitOnFinish                      quit PRo3D once all screenshots have been saved"
        Log.line @"--verbose                           use verbose mode"      
        Log.line @"--excentre                          show exploration centre"
        Log.line @"--refsystem                         show reference system"
        Log.line @"--noMagFilter                       turn off linear texture magnification filtering"
        Log.line @"--runRemoteControl                  turn on remote control app"
        Log.line @"--server                            do not spawn aardium browser"
        Log.line @"--noMapping                         use mapped render target"
        Log.line @"--backgroundColor cssColor          use another background color"
        Log.line @"--samples count                     specify multisampling count"
        Log.line @"--noMapping                         use mapped render target"
        Log.line @"--backgroundColor cssColor          use another background color"
        Log.line @"--port port                         specify port for main app explicitly. By default a free port is choosen automatically."
        Log.line @"--disableCors                       disables CORS for local remote apps"
        Log.line @"--remoteApi                         enables PRo3D REST API"
        
        Log.line @"--snap [path\snapshot.json]         path to a snapshot file containing camera views (old format)"
        Log.line @""
        Log.line @"Examples:"
        Log.line @"PRo3D.Viewer.exe --opc c:\Users\myname\Desktop\myOpc --asnap c:\Users\myname\Desktop\mySnapshotFile.json"
        Log.line @"PRo3D.Viewer.exe --opc c:\Users\myname\Desktop\firstOpc;c:\Users\myname\Desktop\secondOpc --asnap c:\Users\myname\Desktop\mySnapshotFile.json --out c:\Users\myname\Desktop\images"
        Log.line @"PRo3D.Viewer.exe --opc \myOpc\ --obj myObj.obj --asnap mySnapshotFile.json --out tmp --renderDepth --exitOnFinish"


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
            let port                = parseArg "--port" argv
            let disableCors         = argv |> hasFlag "disableCors"
            let enableRemoteApi     = argv |> hasFlag "remoteApi"    
            

            let samples             = parseArg "--samples" argv
            let backgroundColor     = parseArg "--backgroundColor" argv
            let noMapping           = argv |> hasFlag "noMapping"

            let showExplorationCentre = (argv |> Array.contains "--excentre")
            let showReferenceSystem = (argv |> Array.contains "--refsystem")
            let verbose = (argv |> Array.contains "--verbose")

            Log.line "[Arguments] Server mode: %s" (b2str server)
            Log.line "[Arguments] Using linear magnification filtering%s" (b2str magFilter)
            Log.line "[Arguments] Show exploration centre%s" (b2str showExplorationCentre)
            Log.line "[Arguments] Show reference system%s" (b2str showReferenceSystem)
            Log.line "[Arguments] Remote control app%s" (b2str remoteApp)

            Log.line "[Arguments] render control config %A" (samples, backgroundColor, noMapping)
            let args : StartupArgs = 
                    {
                            
                        showExplorationPoint  = showExplorationCentre
                        verbose               = verbose
                        startEmpty            = startEmpty
                        useAsyncLoading       = false
                        magnificationFilter   = magFilter
                        remoteApp             = remoteApp
                        serverMode            = server
                        port                  = port
                        disableCors           = disableCors
                        enableRemoteApi       = enableRemoteApi
                    
                        useMapping            = if noMapping then "false" else "true"
                        data_samples          = samples
                        backgroundColor       = match backgroundColor with Some b -> b | None -> StartupArgs.initArgs.backgroundColor
                    }
            args
