﻿namespace PRo3D.SimulatedViews

open Aardvark.Base
open PRo3D.CommandLineUtils

module CommandLine =
    

    let printHelp () = 
        Log.line @"PRo3D COMMANDLINE OPTIONS\n"
        Log.line @"--help                                         show help"
        Log.line @"--scn [path\pro3dscene.pro3d]                  load scene"
        Log.line @"--obj [path\file.obj];[path\file.obj];[...]    load OBJ(s) from one or more paths"
        Log.line @"--opc [path];[path];[...]                      load OPC(s) from one or more paths"
        Log.line @"--asnap [path\snapshot.json]                   path to a snapshot file, refer to PRo3D User Manual for the correct format"
        Log.line @"--out [path]                                   path to a folder where output images will be saved; if the folder does not exist it will be created"

        Log.line @""
        Log.line @"--renderDepth                                  render the depth map as well and save it as an additional image file"
        Log.line @"--exitOnFinish                                 quit PRo3D once all screenshots have been saved"
        Log.line @"--verbose                                      use verbose mode"      
        Log.line @"--excentre                                     show exploration centre"
        Log.line @"--refsystem                                    show reference system"
        Log.line @"--noMagFilter                                  turn off linear texture magnification filtering"
                                                                  
        //Log.line @"--snap [path\snapshot.json]                    path to a snapshot file containing camera views (old format)" // not in use anymore
        Log.line @""
        Log.line @"Examples:"
        Log.line @"PRo3D.Snapshots.exe --opc c:\Users\myname\Desktop\myOpc --asnap c:\Users\myname\Desktop\mySnapshotFile.json"
        Log.line @"PRo3D.Snapshots.exe --opc c:\Users\myname\Desktop\firstOpc;c:\Users\myname\Desktop\secondOpc --asnap c:\Users\myname\Desktop\mySnapshotFile.json --out c:\Users\myname\Desktop\images"
        Log.line @"PRo3D.Snapshots.exe --opc \myOpc\ --obj myObj.obj --asnap mySnapshotFile.json --out tmp --renderDepth --exitOnFinish"

    /// parse commandline arguments
    let parseArguments (argv : array<string>) : CLStartupArgs =    
        let isHelp flag = 
            String.equalsCaseInsensitive flag "--help"
            || (flag |> String.equalsCaseInsensitive "--h")
            || (flag |> String.equalsCaseInsensitive "-h")
            || (flag |> String.equalsCaseInsensitive "-help")
        
        match argv with 
        | argv when Array.isEmpty argv -> { CLStartupArgs.initArgs with areValid = true}
        | argv when argv |> Array.length = 1 && argv.[0] |> isHelp ->
            printHelp ()
            { CLStartupArgs.initArgs with areValid = false}

        | argv when argv |> hasFlag "printJson" ->
            SnapshotAnimation.writeTestAnimation () |> ignore
            { CLStartupArgs.initArgs with areValid = false}
        | _ ->

            let b2str b =
                match b with
                | true -> ": yes"
                | false -> ": no"

            let sargs = 
                let oneOpc              = parseMultiple "--opc" ';' argv 
                let manyOpcs            = parseMultiple "--opcs" ';' argv
                let objs                = parseMultiple "--obj" ';' argv
                let scene               = parseArg "--scn" argv
                let sceneOk =
                   scene |> Option.map (fun s -> checkPath s)
                let snapshot            = parseArg "--snap" argv 
                let animationSnapshot   = parseArg "--asnap" argv 
                let outFolder           = checkAndCreateDir (parseArg "--out" argv)
                let exitOnFinish        = (argv |> hasFlag "exitOnFinish")
                let renderDepth         = (argv |> hasFlag "renderDepth")
                //let renderDepthTif      = (argv |> hasFlag "renderDepthTif")
                let renderMask          = (argv |> hasFlag "renderMask")
                let frameId             = parseInt "--frameId" argv 
                let frameCount          = parseInt "--frameCount" argv 
                let useAsyncLoading     = (argv |> hasFlag "sync" |> not)
                let startEmpty          = (argv |> hasFlag "empty")
                let magFilter           = not (argv |> hasFlag "noMagFilter")
                let opcs = 
                    match oneOpc, manyOpcs with
                    | Some opc, Some opcs -> opcs@opc |> Some
                    | Some opc, None -> Some opc
                    | None, Some opcs -> Some opcs
                    | None, None -> None
                let showExplorationCentre = (argv |> Array.contains "--excentre")
                let showReferenceSystem = (argv |> Array.contains "--refsystem")
                let verbose = (argv |> Array.contains "--verbose")

                let ok = 
                    match (checkPaths opcs), (checkPaths objs), sceneOk with
                    | Some c, Some j, None -> Some (c && j)
                    | Some c, None, None -> Some c
                    | None, Some j, None -> Some j
                    | _, _, Some s -> sceneOk
                    | _,_,_ -> Some false
                    

                //let check = Option.map2 (fun c1 p -> c1 && (checkPath p)) check snapshot
                let sPath, sType, snapPathValid = 
                    match animationSnapshot with
                    | Some sa -> 
                        animationSnapshot, Some SnapshotType.CameraAndSurface, checkPath sa
                    | _ -> None, None, true
                Log.line "[Arguments] Exit on finish%s" (b2str exitOnFinish)
                Log.line "[Arguments] Render depth%s" (b2str renderDepth)
                Log.line "[Arguments] Using linear magnification filtering%s" (b2str magFilter)
                Log.line "[Arguments] Show exploration centre%s" (b2str showExplorationCentre)
                Log.line "[Arguments] Show reference system%s" (b2str showReferenceSystem)
                Log.line "[Arguments] Output folder: %s" outFolder
                let args = 
                    match ok with
                    | Some surfPathsValid -> 
                        Log.line "[Arguments] Surface paths are valid%s" (b2str surfPathsValid)
                        Log.line "[Arguments] Snapshot path is valid%s" (b2str snapPathValid)
                        {
                            opcPaths              = opcs
                            objPaths              = objs
                            scenePath             = scene
                            snapshotPath          = sPath
                            snapshotType          = sType
                            showExplorationPoint  = showExplorationCentre
                            showReferenceSystem   = showReferenceSystem
                            renderDepth           = renderDepth
                            //renderDepthTif        = renderDepthTif
                            renderMask            = renderMask
                            exitOnFinish          = exitOnFinish
                            areValid              = surfPathsValid && snapPathValid
                            verbose               = verbose
                            outFolder             = outFolder
                            startEmpty            = startEmpty
                            useAsyncLoading       = false
                            magnificationFilter   = magFilter
                            frameId               = frameId
                            frameCount            = frameCount
                            remoteApp             = false
                            serverMode            = false
                        }
                    | None -> 
                        Log.line "[Arguments] Invalid command line arguments."
                        {
                            opcPaths              = None
                            objPaths              = None
                            scenePath             = None
                            snapshotPath          = None
                            snapshotType          = None
                            showExplorationPoint  = showExplorationCentre
                            showReferenceSystem   = showReferenceSystem
                            renderDepth           = false
                            //renderDepthTif        = false
                            renderMask            = false
                            exitOnFinish          = false
                            areValid              = true
                            verbose               = verbose
                            outFolder             = outFolder
                            startEmpty            = startEmpty
                            useAsyncLoading       = useAsyncLoading
                            magnificationFilter   = false
                            frameId               = None
                            frameCount            = None
                            remoteApp             = false
                            serverMode            = false
                        }
                args
            sargs
