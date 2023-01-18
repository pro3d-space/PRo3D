namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.Rendering
//open Aardvark.UI

open Adaptify
open Chiron
open PRo3D.Base.Json
open PRo3D.Core

type PoseId = string

type PoseMetadata = 
    {
        key   : string
        value : string
    } with
    static member FromJson( _ : PoseMetadata) =
        json {
            let! key   = Json.read "key"
            let! value = Json.read "value"

            return {
                key   = key
                value = value
            }
        }

    static member ToJson(x : PoseMetadata) =
        json {
            do! Json.write "value" x.value
            do! Json.write "key"   x.key
        }

module PoseMetadata =
    let dummyData =
        [
            {key = "Sol"         ; value = "0345"}
            {key = "Route"       ; value = "00000012"}
            {key = "Intent"      ; value = "Viewing Towards Cape Sable"}
            {key = "Originator"  ; value = "Andreas Bechtold"}
            {key = "SpiceVersion"; value = "3.908"}
        ]

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
        coordinateSystem : string
        metadata         : list<PoseMetadata>
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
            let! coordinateSystem = Json.read "coordinateSystem"
            let! metadata         = Json.read "metadata"

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
                    coordinateSystem = coordinateSystem
                    metadata         = metadata
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
            coordinateSystem = "IAU-Mars"
            metadata         = PoseMetadata.dummyData
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
            do! Json.write "coordinateSystem" x.coordinateSystem
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

    let dummyData id =
        {
            version          = 0
            cameraId         = id
            cameraName       = "MCZL-110"
            fieldOfView      = 6.543
            resolution       = V2i (1648, 1200)
        }

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
        version        : int
        renderingSetId : int
        settingsName   : string
        farplane       : float
        nearplane      : float
    }

module PoseRenderingSettings =
    let current = 0   

    let read0  = 
        json {
            let! version        = Json.read "version"  
            let! renderingSetId = Json.read "renderingSetId"
            let! settingsName   = Json.read "settingsName"  
            let! farplane       = Json.read "farplane"      
            let! nearplane      = Json.read "nearplane"     

            return
                {
                    version        = version       
                    renderingSetId = renderingSetId
                    settingsName   = settingsName  
                    farplane       = farplane      
                    nearplane      = nearplane     
                }
        }

    let dummyData id =
        {
            version          = 0
            renderingSetId   = id
            settingsName     = "CloseRange"
            farplane         = 100000.0
            nearplane        = 0.0002
        }

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
        }

type PoseData =
    {
        version             : int
        poses               : list<Pose>
        cameraDefinitions   : list<PoseCameraDefinition>
        renderingSettings   : list<PoseRenderingSettings>
    }

module PoseData =
    let current = 0   

    let read0  = 
        json {
            let! poses             = Json.read "poses"
            let! cameraDefinitions = Json.read "cameraDefinitions"
            let! renderingSettings = Json.read "renderingSettings"

            return
                {
                    version        = current
                    poses          = poses
                    cameraDefinitions = cameraDefinitions
                    renderingSettings = renderingSettings
                }
        }

    let dummyData =
        let p0 = Pose.dummyData "Pose0"
        let p1 = Pose.dummyData "Pose1"
        let p2 = Pose.dummyData "Pose2"
        let p3 = Pose.dummyData "Pose3"

        {
            version             = 0
            poses               = [p0;p1;p2;p3]
            cameraDefinitions   = [PoseCameraDefinition.dummyData 1; 
                                    PoseCameraDefinition.dummyData 2]
            renderingSettings   = [PoseRenderingSettings.dummyData 1; 
                                    PoseRenderingSettings.dummyData 2]
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
        }