namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.Rendering
//open Aardvark.UI

open Adaptify
open Chiron
open PRo3D.Base
open PRo3D.Core

open PRo3D.Core.SequencedBookmarks
open System.Text.Json.Nodes

type PoseId = string

/// for Pose Interface github issue #257
type Pose =
    {
        version          : int
        key              : PoseId

        // relevant for rendering
        view             : SnapshotCamera
        cameraId         : int
        renderingSetId   : int
        filename         : string

        // metadata or not yet relevant for rendering
        layerKeys        : list<string>
        stereoMode       : string
        stereoBase       : float
        stereoRef        : string
        layoutKey        : string
    }

module Pose =
    let current = 0   

    let read0  = 
        json {
            let! key           = Json.read "key"

            let! view             = Json.read "view"            
            let! cameraId         = Json.read "cameraId"        
            let! renderingSetId   = Json.read "renderingSetId"
            let! filename         = Json.read "filename"        

            let! layerKeys        = Json.read "layerKeys"          
            let! stereoMode       = Json.read "stereoMode"      
            let! stereoBase       = Json.read "stereoBase"      
            let! stereoRef        = Json.read "stereoRef"       
            let! layoutKey        = Json.read "layoutKey"       

            return
                {
                    version        = current
                    key            = key

                    view             = view            
                    cameraId         = cameraId        
                    renderingSetId   = renderingSetId
                    filename         = filename        
                                     
                    layerKeys        = layerKeys          
                    stereoMode       = stereoMode      
                    stereoBase       = stereoBase      
                    stereoRef        = stereoRef       
                    layoutKey        = layoutKey       
                }
        }

    let dummyData key =
        {
            version          = 0
            key              = key
            
            view             = SnapshotCamera.TestData
            cameraId         = 1
            renderingSetId   = 1
            filename         = sprintf "Route12_Cam%s" key
                             
            layerKeys        = ["RGB";"IR1"]
            stereoMode       = "Stereo"
            stereoBase       = 0.24
            stereoRef        = "Symmetric"
            layoutKey        = "BottomLight"
        }

type Pose with 
    static member FromJson( _ : Pose) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Pose.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of Pose" 
                |> Json.error
        }

    static member ToJson(x : Pose) =
        json {
            do! Json.write "version"          Pose.current
            do! Json.write "key"              x.key
            do! Json.write "view"             x.view            
            do! Json.write "cameraId"         x.cameraId        
            do! Json.write "renderingSetId"   x.renderingSetId  
            do! Json.write "filename"         x.filename        
                                                
            do! Json.write "layerKeys"        x.layerKeys          
            do! Json.write "stereoMode"       x.stereoMode      
            do! Json.write "stereoBase"       x.stereoBase      
            do! Json.write "stereoRef"        x.stereoRef       
            do! Json.write "layoutKey"        x.layoutKey       
        }

/// not yet in use, planned for future extension
type PoseLayoutDefinition = 
    {
        key         : string
        text        : option<list<string>>
        position    : option<V2d>
        color       : option<V3d>
        font        : option<string>
    } 

module PoseLayoutDefinition =
    let current = 0   

    let read0 = 
        json {
            let! key         = Json.read "key"
            let! text        = Json.tryRead "text"
            let! position    = Json.tryRead "position"
            let! color       = Json.tryRead "color"
            let! font        = Json.tryRead "font"

            return {
                key         = key
                text        = text    
                position    = position |> Option.map V2d.Parse
                color       = color |> Option.map V3d.Parse
                font        = font    
            }
        }

    let dummyData =
        [
            {
                key      = "Plain"
                text     = None
                position = None
                color    = None
                font     = None
            }
            {
                key      = "TopFull"
                text     = Some ["Intent"; "Originator"; "Sol"; "Layers"]
                position = Some (V2d(0.1, 0.03))
                color    = Some ((255.0, 200.0, 0.0) |> V3d)
                font     = Some "Arial 14"
            }
        ]

type PoseLayoutDefinition with
    static member FromJson( _ : PoseLayoutDefinition) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! PoseLayoutDefinition.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of PoseLayoutDefinition" 
                |> Json.error
        }        

    static member ToJson(x : PoseLayoutDefinition) =
        json {
            do! Json.write "key"         x.key
            if x.text.IsSome then
                do! Json.write "text" x.text.Value
            if x.position.IsSome then
                do! Json.write "position" (string x.position.Value)
            if x.color.IsSome then
                do! Json.write "color" (string x.color.Value)
            if x.font.IsSome then
                do! Json.write "font" x.font.Value
        }

/// not yet in use, planned for future extension
type PoseLayerDefinition = 
    {
        key         : string
        lookupTable : string
        source      : string
    } 

module PoseLayerDefinition =
    let current = 0   

    let read0 = 
        json {
            let! key         = Json.read "key"
            let! lookupTable = Json.read "lookupTable"
            let! source      = Json.read "key"

            return {
                key         = key
                lookupTable = lookupTable
                source      = source
            }
        }

    let dummyData =
        [
            {
                key         = "RGB"
                lookupTable = "default"
                source      = "LR0"            
            }
            {
                key         = "IR1"
                lookupTable = "default"
                source      = "L456R789"
            }
            {
                key         = "Pan"
                lookupTable = "default"
                source      = "LR"
            }
        ]

type PoseLayerDefinition with
    static member FromJson( _ : PoseLayerDefinition) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! PoseLayerDefinition.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of PoseLayerDefinition" 
                |> Json.error
        }        

    static member ToJson(x : PoseLayerDefinition) =
        json {
            do! Json.write "key"         x.key
            do! Json.write "lookupTable" x.lookupTable
            do! Json.write "source"      x.source
        }

type PoseCameraDefinition = 
    {
        version     : int
        cameraId    : int

        cameraName  : string
        fieldOfView : float
        resolution  : V2i
    }

module PoseCameraDefinition =
    let current = 0   

    let read0  = 
        json {
            let! cameraId      = Json.read "cameraId"   
            let! cameraName    = Json.read "cameraName" 
            let! fieldOfView   = Json.read "fieldOfView"
            let! resolution    = Json.read "resolution" 

            return
                {
                    version        = current
                    cameraId       = cameraId    
                    cameraName     = cameraName 
                    fieldOfView    = fieldOfView
                    resolution     = resolution |> V2i.Parse
                }
        }

    let dummyData =
        [
            {
                version          = 0
                cameraId         = 1
                cameraName       = "MCZL-110"
                fieldOfView      = 6.543
                resolution       = V2i (1648, 1200)
            }
            {
                version          = 0
                cameraId         = 2
                cameraName       = "MCZR-110"
                fieldOfView      = 6.543
                resolution       = V2i (1648, 1200)
            }
        ]

type PoseCameraDefinition with
    static member FromJson( _ : PoseCameraDefinition) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! PoseCameraDefinition.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of PoseCameraDefinition" 
                |> Json.error
        }

    static member ToJson(x : PoseCameraDefinition) =
        json {
            do! Json.write "version"          PoseCameraDefinition.current
            do! Json.write "cameraId"         x.cameraId      
            do! Json.write "cameraName"       x.cameraName 
            do! Json.write "fieldOfView"      x.fieldOfView
            do! Json.write "resolution"       (x.resolution.ToString ())
        }

type PoseRenderingSettings =
    {
        version          : int
        renderingSetId   : int
        settingsName     : string
        farplane         : float
        nearplane        : float
        coordinateSystem : string
    }

module PoseRenderingSettings =
    let current = 0   

    let read0  = 
        json {
            let! renderingSetId   = Json.read "renderingSetId"
            let! settingsName     = Json.read "settingsName"  
            let! farplane         = Json.read "farplane"      
            let! nearplane        = Json.read "nearplane"     
            let! coordinateSystem = Json.read "coordinateSystem"     

            return
                {
                    version        = current       
                    renderingSetId = renderingSetId
                    settingsName   = settingsName  
                    farplane       = farplane      
                    nearplane      = nearplane     
                    coordinateSystem = coordinateSystem
                }
        }

    let dummyData =
        [
            {
                version          = 0
                renderingSetId   = 1
                settingsName     = "CloseRange"
                farplane         = 100000.0
                nearplane        = 0.0002
                coordinateSystem = "IAU-Mars"
            }
            {
                version          = 0
                renderingSetId   = 2
                settingsName     = "MediumRange"
                farplane         = 100000.0
                nearplane        = 0.2
                coordinateSystem = "IAU-Mars"
            }
        ]

type PoseRenderingSettings with
    static member FromJson( _ : PoseRenderingSettings) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! PoseRenderingSettings.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of PoseRenderingSettings" 
                |> Json.error
        }

    static member ToJson(x : PoseRenderingSettings) =
        json {
            do! Json.write "version"            PoseRenderingSettings.current
            do! Json.write "renderingSetId"     x.renderingSetId      
            do! Json.write "settingsName"       x.settingsName 
            do! Json.write "farplane"           x.farplane
            do! Json.write "nearplane"          x.nearplane
            do! Json.write "coordinateSystem" x.coordinateSystem
        }

type PoseData =
    {
        version             : int
        path                : string
        poses               : list<Pose>
        cameraDefinitions   : list<PoseCameraDefinition>
        renderingSettings   : list<PoseRenderingSettings>
        layerDefinitions    : list<PoseLayerDefinition>
        layoutDefinitions   : list<PoseLayoutDefinition>
    }

module PoseData =
    let current = 0   

    let read0 = 
        json {
            let! poses             = Json.read "poses"
            let! cameraDefinitions = Json.read "cameraDefinitions"
            let! renderingSettings = Json.read "renderingSettings"

            return
                {
                    version        = current
                    path           = ""
                    poses          = poses
                    cameraDefinitions = cameraDefinitions
                    renderingSettings = renderingSettings
                    layerDefinitions  = [] // not in use yet, for future extension
                    layoutDefinitions = [] // not in use yet, for future extension
                }
        }

    let dummyData =
        let p0 = Pose.dummyData "Pose0"
        let p1 = Pose.dummyData "Pose1"
        let p2 = Pose.dummyData "Pose2"
        let p3 = Pose.dummyData "Pose3"

        {
            version             = 0
            path                = ""
            poses               = [p0;p1;p2;p3]
            cameraDefinitions   = PoseCameraDefinition.dummyData
            renderingSettings   = PoseRenderingSettings.dummyData
            layerDefinitions    = PoseLayerDefinition.dummyData // not in use yet, for future extension
            layoutDefinitions    = PoseLayoutDefinition.dummyData // not in use yet, for future extension
        }

    let toSequencedBookmarks (m : PoseData) (sceneState : SceneState) =
        seq {
            for pose in m.poses do
                let camPose = List.tryFind 
                                (fun  (x : PoseCameraDefinition) -> x.cameraId = pose.cameraId)  
                                m.cameraDefinitions

                let ren =  List.tryFind 
                                (fun  (x : PoseRenderingSettings) -> x.renderingSetId = pose.renderingSetId)  
                                m.renderingSettings

                let frustumParameters =
                    match camPose, ren with
                    | Some c, Some r ->
                        {
                            resolution  = c.resolution
                            fieldOfView = c.fieldOfView
                            nearplane   = r.nearplane
                            farplane    = r.farplane
                        } |> Some
                    | _ -> None

                let bookmark =
                    Bookmark.init
                        pose.key
                        pose.view.view
                        V3d.Zero // seq bookmarks currently only support free fly mode
                        NavigationMode.FreeFly // seq bookmarks currently only support free fly mode

                let sceneState = 
                    Some sceneState 
                        
                yield (SequencedBookmarkModel.init' 
                        bookmark sceneState frustumParameters (Some m.path))
                      |> SequencedBookmark.LoadedBookmark

        }
            


type PoseData with 
    static member FromJson( _ : PoseData) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! PoseData.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of Pose" 
                |> Json.error
        }

    static member ToJson(x : PoseData) =
        json {
            do! Json.write "version"           x.version
            do! Json.write "poses"             x.poses
            do! Json.write "cameraDefinitions" x.cameraDefinitions
            do! Json.write "renderingSettings" x.renderingSettings
            do! Json.write "layerDefinitions"  x.layerDefinitions
            do! Json.write "layoutDefinitions"  x.layoutDefinitions
        }