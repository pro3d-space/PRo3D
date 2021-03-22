namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open Adaptify
open Chiron

type SnapshotType = 
  | Camera
  | CameraAndSurface

type SnapshotCamera = {
        location      : V3d
        forward       : V3d
        up            : V3d
    }
    with 
      static member TestData =
        {
            location = V3d(0.0)
            forward  = V3d(0.0,0.0,1.0)
            up       = V3d(0.0,1.0,0.0)
        }
      static member current = 0
      static member private readV0 = 
          json {
                let! location    = Json.read "location"
                let! forward     = Json.read "forward"
                let! up          = Json.read "up"
  
                return {
                  location    = location |> V3d.Parse
                  forward     = forward  |> V3d.Parse
                  up          = up       |> V3d.Parse
                }
          }
      static member FromJson(_ : SnapshotCamera) = 
        json {
            return! SnapshotCamera.readV0
        }
      static member ToJson (x : SnapshotCamera) =
        json {
          do! Json.write      "location"  (x.location.ToString())
          do! Json.write      "forward"   (x.forward.ToString())
          do! Json.write      "up"        (x.up.ToString())
        }

type SnapshotSurfaceUpdate = {
    surfname      : string
    translation   : option<V3d>
    trafo         : option<Trafo3d>
    visible       : option<bool>
} 
with 
  static member TestData =
    {
        surfname = "testname"
        translation =  0.0 |> V3d |> Some
        trafo    = Some Trafo3d.Identity
        visible  = Some true
    }
  static member current = 0
  static member private readV0 = 
      json {
        let! opcname    = Json.read "opcname"
        let! trafo       = Json.parseOption (Json.tryRead "trafo") Trafo3d.Parse
        let! visible     = Json.tryRead "visible"
        let! translation = Json.parseOption (Json.tryRead "translation") V3d.Parse
        
        let res = {
            surfname = opcname
            trafo    = trafo
            visible  = visible
            translation = translation
        }
        return res
      }
  static member FromJson(_ : SnapshotSurfaceUpdate) = 
    json {
        return! SnapshotSurfaceUpdate.readV0
    }
  static member ToJson (x : SnapshotSurfaceUpdate) =
    json {
      do! Json.write        "opcname"  (x.surfname.ToString())
      do! Json.writeOption  "trafo"     x.trafo
      do! Json.write  "visible"   x.visible
      do! Json.writeOption "translation" x.translation
    }

type Snapshot = {
  filename       : string
  camera         : SnapshotCamera
  sunPosition    : option<V3d>
  lightDirection : option<V3d>
  surfaceUpdates : option<list<SnapshotSurfaceUpdate>>
  placementParameters   : option<list<ObjectPlacementParameters>>
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
        placementParameters    = Some [ObjectPlacementParameters.TestData]
        renderMask      = Some false
    }
  member this.view = 
    CameraView.look this.camera.location 
                    this.camera.forward.Normalized 
                    this.camera.up.Normalized 
  static member current = 0
  static member private readV0 = 
      json {
        let! filename       = Json.read "filename"
        let! view           = Json.read "view"
        let! sunPosition    = Json.parseOption (Json.tryRead "sunPosition") V3d.Parse
        let! lightDirection    = Json.parseOption (Json.tryRead "lightDirection") V3d.Parse
        let! surfaceUpdates = Json.tryRead "surfaceUpdates"
        let! placementParameters   = Json.tryRead "shattercones"
        let! renderMask     = Json.tryRead "renderMask"

        return {
            filename        = filename
            camera          = view 
            sunPosition     = sunPosition
            lightDirection  = lightDirection
            surfaceUpdates  = surfaceUpdates 
            placementParameters    = placementParameters
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
      if x.placementParameters.IsSome then
        do! Json.writeOptionList  "shattercones"       x.placementParameters (fun x n -> Json.write n x)
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
  static member defaultNearplane = 0.01
  static member defaultFarplane  = 100000.0
  static member TestData =
      {
          fieldOfView = Some 5.47
          resolution  = V2i(4096)
          nearplane   = Some 0.1
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
          let! renderMask   = Json.tryRead "renderMask"
          
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
            do! Json.writeOptionBool "renderMask" x.renderMask
          do! Json.write              "snapshots"      x.snapshots
      }  

/// The functionality of this format can be achieved with the new Snapshot format. As long as users still use this older format it should be maintained.
type LegacySnapshot = {
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
          placementParameters   = None
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
              placementParameters   = 
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
              placementParameters   = None
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
  static member FromJson(_ : LegacySnapshot) = 
    json {
        return! LegacySnapshot.readV0
        //let! v = Json.read "version"
        //match v with            
        //  | 0 -> return! ArnoldSnapshot.readV0
        //  | _ -> return! v |> sprintf "don't know version %A  of ArnoldSnapshot" |> Json.error
    }
  static member ToJson (x : LegacySnapshot) =
    json {
      do! Json.write      "location"  (x.location.ToString())
      do! Json.write      "forward"   (x.forward.ToString())
      do! Json.write      "up"        (x.up.ToString())
      do! Json.write      "filename"  (x.filename.ToString())
    }

/// The functionality of this format can be achieved with the new SnapshotAnimation format. As long as users still use this older format it should be maintained.
type LegacyAnimation = {
    fieldOfView   : double
    resolution    : V2i
    snapshots     : list<LegacySnapshot>
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
  member this.generateAnimation () : SnapshotAnimation =
    {
        fieldOfView   = Some this.fieldOfView
        resolution    = this.resolution
        nearplane     = Some 0.01
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
  static member FromJson(_ : LegacyAnimation) = 
    json {
        let! v = Json.read "version"
        match v with            
          | 0 -> return! LegacyAnimation.readV0
          | _ -> return! v |> sprintf "don't know version %A  of ArnoldAnimation" |> Json.error
    }
  static member ToJson (x : LegacyAnimation) =
    json {
        do! Json.write      "version"        0
        do! Json.write      "fieldOfView"  (x.fieldOfView)
        do! Json.write      "resolution"   (x.resolution.ToString())
        do! Json.write      "snapshots"    (x.snapshots)
    }