namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.UI.Animation
open System
open Aardvark.Rendering
open PRo3D.Base
open MBrace.FsPickler.Json   

open Aardvark.UI
open Chiron

/////////////////////
type Snapshot = {
    filename       : string
    camera         : SnapshotCamera
    sunPosition    : option<V3d>
    surfaceUpdates : option<list<SnapshotSurfaceUpdate>>
    shattercones   : option<list<SnapshotShattercone>>
}
with 
  static member TestData =
    {
        filename        = "testname"
        camera          = SnapshotCamera.TestData
        sunPosition     = Some (V3d(10.0))
        surfaceUpdates  = Some [SnapshotSurfaceUpdate.TestData]
        shattercones    = Some [SnapshotShattercone.TestData]
    }
  member this.view = 
    CameraView.look this.camera.location 
                    this.camera.forward.Normalized 
                    this.camera.up.Normalized
  member this.toActions frustum =
    let actions = 
        [
            failwith "SnapshotAnimation.fs" //ViewerAction.SetCameraAndFrustum2 (this.view,frustum);
        ]
    let sunAction =
        match this.sunPosition with
        | Some p -> [] //TODO transform uniform
        | None -> []        
    let surfAction =
        match this.surfaceUpdates with
        | Some s ->
            match s.IsEmptyOrNull () with
            | false ->
                //[ViewerAction.TransformAllSurfaces s] //TODO MarsDL
                []
            | true -> []
        | None -> []
    let shatterConeAction =
        match this.shattercones with
        | Some sc ->
            match sc.IsEmptyOrNull () with
            | false ->
                //[ViewerAction.UpdateShatterCones sc] //TODO MarsDL
                []
            | true -> []
        | None -> []
    // ADD ACTIONS FOR NEW SNAPSHOT MEMBERS HERE
    actions@sunAction@surfAction@shatterConeAction |> List.toSeq    
  static member current = 0
  static member private readV0 = 
      json {
        let! filename       = Json.read "filename"
        let! view           = Json.read "view"
        let! sunPosition    = Json.parseOption (Json.tryRead "sunPosition") V3d.Parse
        let! surfaceUpdates = Json.tryRead "surfaceUpdates"
        let! shattercones   = Json.tryRead "shattercones"
  
        return {
            filename        = filename
            camera          = view 
            sunPosition     = sunPosition
            surfaceUpdates  = surfaceUpdates 
            shattercones    = shattercones
        }
      }
  static member FromJson(_ : Snapshot) = 
    json {
        return! Snapshot.readV0
    }
  static member ToJson (x : Snapshot) =
    json {
      do! Json.write            "filename"           x.filename
      do! Json.write            "view"               x.camera
      do! Json.writeOption      "sunPosition"        x.sunPosition 
      do! Json.writeOptionList  "surfaceUpdates"     x.surfaceUpdates (fun x n -> Json.write n x)
      do! Json.writeOptionList  "shattercones"       x.shattercones (fun x n -> Json.write n x)
    }  


/// Camera and Surface animation
type SnapshotAnimation = {
    fieldOfView   : double
    resolution    : V2i
    snapshots     : list<Snapshot>
}
with 
    static member TestData =
        {
            fieldOfView = 5.47
            resolution  = V2i(1024)
            snapshots   = [Snapshot.TestData]
        }
    static member private readV0 = 
        json {
            let! fieldOfView    = Json.read "fieldOfView"
            let! resolution     = Json.read "resolution"
            let! snapshots      = Json.read "snapshots"
            
            let a : SnapshotAnimation = 
                {
                    fieldOfView = fieldOfView
                    resolution  = resolution |> V2i.Parse
                    snapshots   = snapshots
                }
            return a
        }
    static member FromJson(_ : SnapshotAnimation) = 
        json {
            let! v = Json.read "version"
            match v with            
                | 0 -> return! SnapshotAnimation.readV0
                | _ -> return! v |> sprintf "don't know version %A  of HeraAnimation" |> Json.error
        }
    static member ToJson (x : SnapshotAnimation) =
        json {
            do! Json.write      "version"      0
            do! Json.write      "fieldOfView"  x.fieldOfView
            do! Json.write      "resolution"   (x.resolution.ToString())
            do! Json.write      "snapshots"    x.snapshots
        }  

/// Camera Animation
type ArnoldSnapshot = {
    location      : V3d
    forward       : V3d
    up            : V3d
    filename      : string
}
  with
  member this.view = 
    CameraView.look this.location this.forward.Normalized this.up.Normalized
  member this.toActions frustum =
        let actions = 
            [
                failwith "SnapshotAnimation.fs" //ViewerAction.SetCameraAndFrustum2 (this.view,frustum);
            ] |> List.toSeq
        actions    
  member this.toSnapshot () =
    {
        filename       = this.filename
        camera         = {
                            location = this.location
                            forward  = this.forward
                            up       = this.up
                         }
        sunPosition    = None
        surfaceUpdates = None
        shattercones   = None
    }
  static member current = 0
  static member private readV0 = 
      json {
        let! location    = Json.read "location"
        let! forward     = Json.read "forward"
        let! up          = Json.read "up"
        let! filename    = Json.read "filename"
  
        return {
          location    = location |> V3d.Parse
          forward     = forward  |> V3d.Parse
          up          = up       |> V3d.Parse
          filename    = filename
        }
      }
  static member FromJson(_ : ArnoldSnapshot) = 
    json {
        return! ArnoldSnapshot.readV0
        //let! v = Json.read "version"
        //match v with            
        //  | 0 -> return! ArnoldSnapshot.readV0
        //  | _ -> return! v |> sprintf "don't know version %A  of ArnoldSnapshot" |> Json.error
    }
  static member ToJson (x : ArnoldSnapshot) =
    json {
      do! Json.write      "location"  (x.location.ToString())
      do! Json.write      "forward"   (x.forward.ToString())
      do! Json.write      "up"        (x.up.ToString())
      do! Json.write      "filename"  (x.filename.ToString())
    }

type ArnoldAnimation = {
    fieldOfView   : double
    resolution    : V2i
    snapshots     : list<ArnoldSnapshot>
}
with 
  member this.toSnapshotAnimation () : SnapshotAnimation =
    {
        fieldOfView   = this.fieldOfView
        resolution    = this.resolution
        snapshots     = this.snapshots |> List.map (fun x -> x.toSnapshot ())
    }
  static member current = 0
  static member private readV0 = 
      json {
        let! fieldOfView    = Json.read "fieldOfView"
        let! resolution     = Json.read "resolution"
        let! snapshots      = Json.read "snapshots"

        //let snapshots' = snapshots |> List.map 
  
        return {
          fieldOfView    = fieldOfView
          resolution     = resolution |> V2i.Parse
          snapshots      = snapshots  //|> Serialization.jsonSerializer.UnPickleOfString
        }
      }
  static member FromJson(_ : ArnoldAnimation) = 
    json {
        let! v = Json.read "version"
        match v with            
          | 0 -> return! ArnoldAnimation.readV0
          | _ -> return! v |> sprintf "don't know version %A  of ArnoldAnimation" |> Json.error
    }
  static member ToJson (x : ArnoldAnimation) =
    json {
        do! Json.write      "version"        0
        do! Json.write      "fieldOfView"  (x.fieldOfView)
        do! Json.write      "resolution"   (x.resolution.ToString())
        do! Json.write      "snapshots"    (x.snapshots)
    }

module SnapshotAnimation = 
    ///////////////////// WRITE TESTDATA
    let writeTestAnimation () =
        SnapshotAnimation.TestData
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile "animationTest.json"

    //////////////////////    

    let readLegacyFile path =
        try
            let foo =
                path
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : ArnoldAnimation) =
                foo
                |> Json.deserialize
            Some (bar.toSnapshotAnimation ())
        with e ->
            Log.line "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            None

    let read path = 
        try
            let foo =
                path
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : SnapshotAnimation) =
                foo
                |> Json.deserialize
            Some bar
        with e ->
            Log.line "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            None

    let readTestAnimation () =
        try
            let foo =
                @"./animationTest.json"
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : SnapshotAnimation) =
                foo
                |> Json.deserialize
            Some bar
        with e ->
            Log.line "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            None

    let updateSnapshotCam (location : V3d) (forward : V3d) (up : V3d) = 
        CameraView.look location forward.Normalized up.Normalized

    let updateArnoldSnapshotCam (extr:ArnoldSnapshot)  = 
        CameraView.look extr.location extr.forward.Normalized extr.up.Normalized

    let updateSnapshotFrustum (frust:Frustum) (fieldOfView : double) (resolution : V2i)  =
        let resh  = float(resolution.X)
        let resv  = float(resolution.Y)

        Frustum.perspective fieldOfView frust.near frust.far (resh/resv)

    let updateArnoldSnapshotFrustum (frust:Frustum) (dataAnimation:ArnoldAnimation)  =
        updateSnapshotFrustum frust dataAnimation.fieldOfView dataAnimation.resolution
        
    let loadData (path : string) =
        try 
             let (arnoldanims : ArnoldAnimation) =
                    path 
                        |> Serialization.readFromFile
                        |> Json.parse 
                        |> Json.deserialize
             arnoldanims |> Some    
        with e ->        
            Log.error "[ViewerIO] couldn't load %A" e
            None

    //let writeDataToJsonFile =   
    //    //let path = "C:\Users\laura\VRVis\Data\Donut_medium_georef_compl\donut.snapshots.json"
    //    //let ex1 =
    //    //     {
    //    //        location = V3d(-36924.38, 272515.17, 587.71)
    //    //        forward  = V3d(0.44, -0.31, -0.84)
    //    //        up       = V3d(-0.67, 0.51, -0.54)
    //    //    }
           
    //    //let ex2 =
    //    //    {
    //    //        location = V3d(-36930.36, 272519.67, 569.74)
    //    //        forward  = V3d(0.46, -0.34, 0.82)
    //    //        up       = V3d(-0.74, -0.66, 0.15)
    //    //    }
            
    //    //let ex3 =
    //    //    {
    //    //        location = V3d(-36926.17, 272516.11, 584.91)
    //    //        forward  = V3d(0.83, -0.51, -0.24)
    //    //        up       = V3d(-0.39, -0.82, 0.41)
    //    //    }
            
    //    //let testdata = 
    //    //    {
    //    //        fieldOfView = 26.49
    //    //        resolution  = V2i(1600, 1200)
    //    //        snapshots   = [ex1; ex2; ex3]
    //    //    }

    //    let path = "C:\Users\laura\VRVis\Data\GardenCitySmall\GardenCitySmall\gardenCity.snapshots.json"
    //    let ex1 =
    //         {
    //            location = V3d(-2486974.23, 2288926.50, -275792.76)
    //            forward  = V3d(0.42, -0.63, -0.65)
    //            up       = V3d(-0.54, 0.40, -0.74)
    //            filename = ""
    //        }
           
    //    let ex2 =
    //        {
    //            location = V3d(-2486973.65, 2288925.99, -275793.60)
    //            forward = V3d(0.34, -0.85, 0.41)
    //            up       = V3d(-0.55, 0.18, -0.82)
    //            filename = ""
    //        }
            
    //    let ex3 =
    //        {
    //            location = V3d(-2486973.72, 2288924.03, -275796.63)
    //            forward  = V3d(0.05, 0.51, 0.86)
    //            up       = V3d(-0.07, -0.86, 0.51)
    //            filename = ""
    //        }

    //    let ex4 =
    //        {
    //            location = V3d(-2486971.72, 2288922.95, -275794.30)
    //            forward  = V3d(-0.54, 0.82, 0.17)
    //            up       = V3d(-0.34, -0.40, 0.85)
    //            filename = ""
    //        }

    //    let ex5 =
    //        {
    //            location = V3d(-2486972.34, 2288940.76, -275790.74)
    //            forward  = V3d(-0.08, -0.98, -0.19)
    //            up       = V3d(0.09, -0.20, 0.98)
    //            filename = ""
    //        }
            
    //    let testdata = 
    //        {
    //            fieldOfView = 26.49
    //            resolution  = V2i(1600, 1200)
    //            snapshots   = [ex1; ex2; ex3; ex4; ex5]
    //        }
          
    //    testdata
    //     |> Json.serialize 
    //     |> Json.formatWith JsonFormattingOptions.Pretty 
    //     |> Serialization.writeToFile path
        
    
    let snapshot (fpPath:string) (filename:string) (width : int) (height : int) =
        let pngName =
            if filename.IsEmpty() then
                let now = DateTime.Now
                System.String.Format(
                        "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}{6:00}",
                        now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Millisecond)
            else filename
        try Utilities.takeScreenshotFromAllViews "http://localhost:54321" width height pngName fpPath ".jpg" with e -> printfn "error: %A" e
       
    
    let startSnapshots (scenepath : string) (filename:string) (width : int) (height : int) = 
        let fpPath = FootPrint.getFootprintsPath scenepath
        snapshot fpPath filename width height 

    let animateAndScreenshot (scenePath:string) (ex: Extrinsics) (duration : RelativeTime) (name : string) (width : int) (height : int) = 
      let fpPath = FootPrint.getFootprintsPath scenePath
      {
        (CameraAnimations.initial name) with 
          sample = fun (localTime, globalTime) (state : CameraView) -> // given the state and t since start of the animation, compute a state and the cameraview
            if localTime < duration then 
                
              let now = DateTime.Now
              let pngName = System.String.Format(
                                        "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}_{6}",
                                        now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Millisecond)
               
               
              //try Utilities.takeScreenshot "http://localhost:54321" width height pngName fpPath with e -> printfn "error: %A" e

              let rot      = Rot3d.RotateInto(state.Forward, ex.camLookAt) * localTime / duration
              let forward' = (Rot3d rot).Transform(state.Forward)

              let uprot     = Rot3d.RotateInto(state.Up, ex.camUp) * localTime / duration
              let up'       = (Rot3d uprot).Transform(state.Up)
              
              let vec       = ex.position - state.Location
              let velocity  = vec.Length / duration                  
              let dir       = vec.Normalized
              let location' = state.Location + dir * velocity * localTime

              let view = 
                state 
                  |> CameraView.withForward forward'
                  |> CameraView.withUp up'
                  |> CameraView.withLocation location'
      
              Some (state,view)
            else None
      }