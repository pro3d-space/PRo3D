namespace PRo3D.SimulatedViews

open Aardvark.Base
open Aardvark.UI.Animation
open System
open PRo3D.Base
open MBrace.FsPickler.Json   

open Aardvark.UI
open Chiron


type Snapshot = {
    filename       : string
    camera         : SnapshotCamera
    sunPosition    : option<V3d>
    lightDirection : option<V3d>
    surfaceUpdates : option<list<SnapshotSurfaceUpdate>>
    shattercones   : option<list<SnapshotShattercone>>
    renderMask     : option<bool>
}
with 
  static member TestData =
    {
        filename        = "testname"
        camera          = SnapshotCamera.TestData
        sunPosition     = None
        lightDirection  = Some (V3d(0.0,1.0,0.0))
        surfaceUpdates  = Some [SnapshotSurfaceUpdate.TestData]
        shattercones    = Some [SnapshotShattercone.TestData]
        renderMask      = Some false
    }
  member this.view = 
    CameraView.look this.camera.location 
                    this.camera.forward.Normalized 
                    this.camera.up.Normalized
  member this.toActions frustum filename =
    let actions = 
        [
             //TODO rno refactor
            //ViewerAction.SetCameraAndFrustum2 (this.view,frustum); //X
            //ViewerAction.SetMaskObjs this.renderMask
        ]
    let sunAction =
        match this.lightDirection with
        | Some p -> [
                        //TODO rno refactor
                       //ViewerAction.ShadingMessage (Shading.ShadingActions.SetLightDirectionV3d p)
                    ]
        | None -> []        
    let surfAction =
        match this.surfaceUpdates with
        | Some s ->
            match s.IsEmptyOrNull () with
            | false ->
                [
                    //ViewerAction.TransformAllSurfaces s
                    //TODO rno refactor
                ]
            | true -> []
        | None -> []
    let shatterConeAction =
        match this.shattercones with
        | Some sc ->
            match sc.IsEmptyOrNull () with
            | false ->
                [
                    //ViewerAction.UpdateShatterCones (sc, frustum, filename)
                    //TODO rno refactor
                ] //XX
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
        let! lightDirection    = Json.parseOption (Json.tryRead "lightDirection") V3d.Parse
        let! surfaceUpdates = Json.tryRead "surfaceUpdates"
        let! shattercones   = Json.tryRead "shattercones"
        let! renderMask     = Json.tryRead "renderMask"
  
        return {
            filename        = filename
            camera          = view 
            sunPosition     = sunPosition
            lightDirection  = lightDirection
            surfaceUpdates  = surfaceUpdates 
            shattercones    = shattercones
            renderMask      = renderMask
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
      do! Json.writeOption      "lightDirection"     x.lightDirection
      if x.sunPosition.IsSome then
        do! Json.writeOption      "sunPosition"        x.sunPosition 
      if x.surfaceUpdates.IsSome then
        do! Json.writeOptionList  "surfaceUpdates"     x.surfaceUpdates (fun x n -> Json.write n x)
      if x.shattercones.IsSome then
        do! Json.writeOptionList  "shattercones"       x.shattercones (fun x n -> Json.write n x)
      if x.renderMask.IsSome then
        do! Json.write            "renderMask"         x.renderMask 
    }  


/// Camera and Surface animation
type SnapshotAnimation = {
    fieldOfView   : option<float>
    resolution    : V2i
    nearplane     : option<float>
    farplane      : option<float>
    lightLocation : option<V3d>
    renderMask  : option<bool>
    snapshots     : list<Snapshot>
}
with 
    static member defaultNearplane = 0.001
    static member defaultFarplane  = 1000000.0
    member this.actions =
        let setNearplane =
            match this.nearplane with
            | Some np -> []
                //TODO rno refactor
                //[(ConfigProperties.Action.SetNearPlane (Numeric.SetValue np))
                //  |> ViewerAction.ConfigPropertiesMessage]
            | None -> []
        let setFarplane =
            match this.farplane with
            | Some fp -> []
                //TODO rno refactor
                //[(ConfigProperties.Action.SetFarPlane (Numeric.SetValue fp))
                //  |> ViewerAction.ConfigPropertiesMessage]
            | None -> []
        let lightActions = 
            match this.lightLocation with
            | Some loc -> 
                [
                    //TODO rno refactor
                    //ViewerAction.ShadingMessage (Shading.ShadingActions.SetLightPositionV3d loc)
                    //ViewerAction.ShadingMessage (Shading.ShadingActions.SetUseLighting true)
                ]
            | None -> []
        setNearplane@setFarplane@lightActions |> List.toSeq

    static member TestData =
        {
            fieldOfView = Some 5.47
            resolution  = V2i(4096)
            nearplane   = Some 0.00001
            farplane    = Some 1000000.0
            lightLocation = 5.0 |> V3d |> Some
            snapshots   = [Snapshot.TestData]
            renderMask = None
        }

    static member private readV0 = 
        json {
            let! fieldOfView    = Json.tryRead "fieldOfView"
            let! resolution     = Json.read "resolution"
            let! nearplane      = Json.tryRead "nearplane"
            let! farplane       = Json.tryRead "farplane"
            let! snapshots      = Json.read "snapshots"
            let! lightLocation  = Json.tryRead "lightLocation"
            let! renderMask   = Json.tryRead "lightLocation"
            
            let a : SnapshotAnimation = 
                {
                    fieldOfView = fieldOfView
                    resolution  = resolution |> V2i.Parse
                    nearplane   = nearplane
                    farplane    = farplane
                    lightLocation = lightLocation |> Option.map (fun loc -> loc |> V3d.Parse)
                    snapshots   = snapshots
                    renderMask  = renderMask
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
            do! Json.write              "version"        0
            do! Json.writeOptionFloat   "fieldOfView"    x.fieldOfView
            do! Json.write              "resolution"     (x.resolution.ToString ())
            if x.lightLocation.IsSome then
              do! Json.writeOption        "lightLocation"  (x.lightLocation)
            do! Json.writeOptionFloat   "nearplane"      (x.nearplane)
            do! Json.writeOptionFloat   "farplane"       (x.farplane )
            if x.renderMask.IsSome then
              do! Json.writeOption "renderMask" x.renderMask
            do! Json.write              "snapshots"      x.snapshots
        }  

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Snapshot =

    let writeToFile (snapshot : Snapshot) (path : string) =
        snapshot
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile (Path.combine [path; "snapshot.json"])

    let readSCPlacement (filepath : string) =
        try
            let json =
                filepath
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (scPlacement : SnapshotShattercone) =
                json |> Json.deserialize
            scPlacement
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let writeSCPlacementToFile (scPlacement : SnapshotShattercone) 
                               (filepath : string) =
        scPlacement
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile filepath

    let toSnapshotCamera (camView : CameraView) : SnapshotCamera =
        {
            location = camView.Location
            forward  = camView.Forward
            up       = camView.Up
        }

    let importSnapshotCamera (filepath : string) : SnapshotCamera =
        try
            let json =
                filepath
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (cam : SnapshotCamera) =
                json |> Json.deserialize
            cam
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e


    let fromViews (camViews : seq<CameraView>) (sp : List<SnapshotShattercone>) 
                  (lightDirection : V3d) =
        let foo = Seq.zip camViews [0 .. (Seq.length camViews - 1)]
        seq {
            for (v, i) in foo do
               yield {
                        filename        = sprintf "%06i" i
                        camera          = v |> toSnapshotCamera
                        sunPosition     = None
                        lightDirection  = Some lightDirection
                        surfaceUpdates  = None
                        shattercones    = Some sp
                        renderMask      = None         
                      }
               //yield {
               //         filename        = sprintf "%06i_mask" i
               //         camera          = v |> toSnapshotCamera
               //         sunPosition     = None
               //         lightDirection  = Some lightDirection
               //         surfaceUpdates  = None
               //         shattercones    = None
               //         renderMask      = Some true     
               //      }
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
              //TODO rno refactor
              //ViewerAction.SetCameraAndFrustum2 (this.view,frustum);
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
          lightDirection = None
          surfaceUpdates = None
          shattercones   = None
          renderMask     = None
      }
  member this.toSCPlacement () =
      let plain = 
          {
              filename       = this.filename
              camera         = {
                                  location = this.location
                                  forward  = this.forward
                                  up       = this.up
                               }
              sunPosition    = None //Some (V3d(0.1842, -0.93675, -0.29759))
              lightDirection = Some (V3d(0.1842, -0.93675, -0.29759))
              surfaceUpdates = None
              shattercones   = 
                   [{
                      name         = "02-Presqu-ile-LF"
                      count        = 1
                      color        = None
                      contrast     = None
                      brightness   = None
                      gamma        = None
                      scale        = Some (V2i(1,3))
                      xRotation    = None
                      yRotation    = None 
                      zRotation    = Some (V2i(90,90)) //Some (V2i(0,360))
                      maxDistance  = None
                      subsurface   = None
                      maskColor    = None
                   }] |> Some
              renderMask     = None
          }
      let mask =
          {
              filename       = sprintf "%s_mask" this.filename
              camera         = {
                                  location = this.location
                                  forward  = this.forward
                                  up       = this.up
                               }
              lightDirection = Some (V3d(0.1842, -0.93675, -0.29759))
              sunPosition    = None
              surfaceUpdates = None
              shattercones   = None
              renderMask     = Some true
          }
      [plain; mask]
        
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
        fieldOfView   = Some this.fieldOfView
        resolution    = this.resolution
        nearplane     = Some 0.00001
        farplane      = Some 1000000.0
        lightLocation = None
        snapshots     = this.snapshots |> List.map (fun x -> x.toSnapshot ())
        renderMask    = None
    }
  member this.generateSCAnimation () : SnapshotAnimation =
    {
        fieldOfView   = Some this.fieldOfView
        resolution    = this.resolution
        nearplane     = Some 0.00001
        farplane      = Some 1000000.0
        lightLocation = Some (V3d (0.1842, -0.93675, -0.29759))
        renderMask    = None
        snapshots     = [for s in this.snapshots do yield! s.toSCPlacement ()]
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
        SnapshotAnimation.TestData
    //////////////////////    

    let writeToDir (animation : SnapshotAnimation) (dirpath : string) =
        animation
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile (Path.combine [dirpath; "snapshots.json"])

    let writeToFile (animation : SnapshotAnimation) (filepath : string) =
        animation
            |> Json.serialize 
            |> Json.formatWith JsonFormattingOptions.Pretty 
            |> Serialization.writeToFile filepath

    let readArnoldAnimation path =
        try
            let foo =
                path
                    |> Serialization.readFromFile
                    |> Json.parse 
            let (bar : ArnoldAnimation) =
                foo
                |> Json.deserialize
            bar
        with e ->
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let readLegacyFile path =
        let aa = readArnoldAnimation path
        Some (aa.toSnapshotAnimation ())

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
            Log.error "[SNAPSHOTS] Could not read json File. Please check the format is correct."
            Log.line "%s" e.Message
            Log.line "%s" e.StackTrace 
            raise e

    let generate (snapshots : seq<Snapshot>) (foV : float) (renderMask : bool) =
        {
            fieldOfView = Some foV //Some 30.0
            resolution  = V2i(4096)
            nearplane   = Some 0.001
            farplane    = Some 100.0
            lightLocation = None
            snapshots   = snapshots |> Seq.toList
            renderMask  = Some renderMask
        }

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