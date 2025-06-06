﻿namespace PRo3D.SimulatedViews


open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI

open Adaptify
open Chiron
open PRo3D.Base
open PRo3D.Core

#nowarn "0686"

type SnapshotType = 
  | CameraAndSurface
  | Bookmark
  | Panomarama

type SnapshotCamera = {
        location      : V3d
        forward       : V3d
        up            : V3d
    }
    with 
      member this.view = 
        CameraView.look this.location 
                        this.forward.Normalized 
                        this.up.Normalized 
      static member fromCamera (camera : CameraView) =
        {
            location = camera.Location
            forward  = camera.Forward
            up       = camera.Up
        }
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
        let! trafo       = PRo3D.Base.Json.parseOption (Json.tryRead "trafo") Trafo3d.Parse
        let! visible     = Json.tryRead "visible"
        let! translation = PRo3D.Base.Json.parseOption (Json.tryRead "translation") V3d.Parse
        
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
      do! PRo3D.Base.Json.writeOption  "trafo"     x.trafo
      do! Json.write  "visible"   x.visible
      do! PRo3D.Base.Json.writeOption "translation" x.translation
    }

/// Defines different types of panoramas
type PanoramaKind =
    | Perspective
    | Spherical
    | Cylindric
with 
    static member parse str =
        match str with
        | str when str = "Perspective" -> PanoramaKind.Perspective
        | str when str = "Spherical"   -> PanoramaKind.Spherical
        | str when str = "Cylindric"   -> PanoramaKind.Cylindric
        | _ ->
            PanoramaKind.Spherical

/// A Snapshot type for panomaramas see 
/// https://github.com/pro3d-space/PRo3D/issues/412
type PanoramaSnapshot = {
    filename      : string
    camera        : SnapshotCamera
} with    
    static member FromJson(_ : PanoramaSnapshot) = 
        json {
            let! filename = Json.read "filename"
            let! camera   = Json.read "camera"

            return {
                filename = filename 
                camera   = camera   
            }
        }
    static member ToJson (x : PanoramaSnapshot) =
        json {
            do! Json.write "filename" x.filename
            do! Json.write "camera"   x.camera
        }
    static member dummyData =
        {
            filename = "panorama001"
            camera   = {location = V3d.OOO; forward = V3d.IOO; up = V3d.OIO}
        }


/// A type for batch rendering multiple panoramas with the given settings
/// see https://github.com/pro3d-space/PRo3D/issues/412
type PanoramaSnapshotCollection = {
    fieldOfView             : float
    nearplane               : float
    farplane                : float
    resolution              : V2i    
    panoramaKind            : PanoramaKind
    renderRgbWithoutOverlay : bool
    renderDepth             : bool
    renderRgbWithOverlay    : bool
    snapshots               : list<PanoramaSnapshot>
} with
    static member dummyData : PanoramaSnapshotCollection = {
        fieldOfView              = 0.0
        nearplane                = 0.0
        farplane                 = 0.0
        resolution               = V2i.II
        panoramaKind             = PanoramaKind.Spherical
        renderRgbWithoutOverlay  = true
        renderDepth              = true
        renderRgbWithOverlay     = true
        snapshots                = [PanoramaSnapshot.dummyData]
    }
        
    static member private readV0 = 
        json {
            let! fieldOfView    = Json.read "fieldOfView"
            let! resolution     = Json.read "resolution"
            let! nearplane      = Json.read "nearplane"
            let! farplane       = Json.read "farplane"
            let! snapshots      = Json.read "snapshots"
            let! panoramaKindString   = Json.read "panoramaKind"
            let  panoramaKind = PanoramaKind.parse panoramaKindString
            let! renderRgbWithoutOverlay = Json.read "renderRgbWithoutOverlay"
            let! renderDepth             = Json.read "renderDepth"
            let! renderRgbWithOverlay    = Json.read "renderRgbWithOverlay"

            let a : PanoramaSnapshotCollection = 
                {
                    fieldOfView = fieldOfView
                    resolution  = resolution |> V2i.Parse
                    nearplane   = nearplane
                    farplane    = farplane
                    snapshots   = snapshots
                    panoramaKind = panoramaKind
                    renderRgbWithoutOverlay = renderRgbWithoutOverlay
                    renderDepth             = renderDepth            
                    renderRgbWithOverlay    = renderRgbWithOverlay   
                }
            return a
      }
    static member FromJson(_ : PanoramaSnapshotCollection) = 
      json {
          let! v = Json.read "version"
          match v with            
              | 0 -> return! PanoramaSnapshotCollection.readV0
              | _ -> return! v |> sprintf "don't know version %A  of HeraAnimation" |> Json.error
      }
    static member ToJson (x : PanoramaSnapshotCollection) =
      json {
          do! Json.write "version"        0
          do! Json.write "fieldOfView"    x.fieldOfView
          do! Json.write "resolution"     (x.resolution.ToString ())
          do! Json.write "nearplane"      x.nearplane
          do! Json.write "farplane"       x.farplane
          do! Json.write "panoramaKind"   (x.panoramaKind.ToString ())
          
          do! Json.write "renderRgbWithoutOverlay" x.renderRgbWithoutOverlay
          do! Json.write "renderDepth"             x.renderDepth
          do! Json.write "renderRgbWithOverlay"    x.renderRgbWithOverlay
          
          do! Json.write "snapshots"      x.snapshots
      }  


/// a snapshot that uses camera and surface updates
type SurfaceSnapshot = {
  filename       : string
  camera         : SnapshotCamera
  sunPosition    : option<V3d>
  lightDirection : option<V3d>
  surfaceUpdates : option<list<SnapshotSurfaceUpdate>>
  placementParameters   : option<list<ObjectPlacementParameters>>
  renderMask     : option<bool>
}
with 
  static member fromNameAndCamera filename camera =
    {
        filename             = filename
        camera               = camera
        sunPosition          = None
        lightDirection       = None
        surfaceUpdates       = None
        placementParameters  = None
        renderMask           = None

    }
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
        let! sunPosition    = PRo3D.Base.Json.parseOption (Json.tryRead "sunPosition") V3d.Parse
        let! lightDirection    = PRo3D.Base.Json.parseOption (Json.tryRead "lightDirection") V3d.Parse
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
  static member FromJson(_ : SurfaceSnapshot) = 
    json {
        return! SurfaceSnapshot.readV0
    }
  static member ToJson (x : SurfaceSnapshot) =
    json {
      do! Json.write            "filename"           x.filename
      do! Json.write            "view"               x.camera
      if x.lightDirection.IsSome then
        do! PRo3D.Base.Json.writeOption      "lightDirection"     x.lightDirection
      if x.sunPosition.IsSome then
        do! PRo3D.Base.Json.writeOption      "sunPosition"        x.sunPosition 
      if x.surfaceUpdates.IsSome then
        do! PRo3D.Base.Json.writeOptionList  "surfaceUpdates"     x.surfaceUpdates (fun x n -> Json.write n x)
      if x.placementParameters.IsSome then
        do! PRo3D.Base.Json.writeOptionList  "shattercones"       x.placementParameters (fun x n -> Json.write n x)
      if x.renderMask.IsSome then
        do! Json.write            "renderMask"         x.renderMask 
    }  

type CameraConfiguration =
    {
        camera  : SnapshotCamera
        frustum : Frustum
    } 
    static member ToJson (x : CameraConfiguration) =
        json {
          do! Json.write      "camera"  x.camera
          do! Json.writeWith  (Ext.toJson<Frustum, Ext>) "frustum" x.frustum    
        }
    static member FromJson (x : CameraConfiguration) =
        json {
            let! camera  = Json.read "camera"
            let! frustum = Json.readWith Ext.fromJson<Frustum, Ext> "frustum"   

            return {
                camera  = camera 
                frustum = frustum     
            }
        }

/// a snapshot that uses sequenced bookmarks for updates
/// uses bookmarks once when they start, and camera-only
/// updates between bookmarks
type BookmarkTransformation = 
    | Bookmark of SequencedBookmarks.SequencedBookmarkModel
    | Camera   of SnapshotCamera
    | Configuration of CameraConfiguration
with 
    static member ToJson x =
        match x with
        | BookmarkTransformation.Bookmark x -> 
            Json.write "Bookmark" x
        | BookmarkTransformation.Camera x -> 
            Json.write "Camera" x
        | BookmarkTransformation.Configuration x -> 
            Json.write "Configuration" x
    static member FromJson(_ : BookmarkTransformation) = 
        json { 
            let! camera = Json.tryRead "Camera"
            match camera with
            | Some camera -> 
                return BookmarkTransformation.Camera camera
            | None ->
                let! config = Json.tryRead "Configuration"
                match config with
                | Some config ->
                    return BookmarkTransformation.Configuration config
                | None ->
                    let! bookmark = Json.read "Bookmark"
                    return BookmarkTransformation.Bookmark bookmark
        }
    member this.camera =
        match this with
        | BookmarkTransformation.Bookmark x -> 
            SnapshotCamera.fromCamera x.cameraView
        | BookmarkTransformation.Camera x -> 
            x
        | BookmarkTransformation.Configuration x -> 
            x.camera
type BookmarkSnapshot = {
    filename       : string
    transformation : BookmarkTransformation
} with 
    static member FromJson ( _ : BookmarkSnapshot) = 
        json {
            let! filename = Json.read "filename"
            let! transformation = Json.read "transformation"

            return {
                filename       = filename
                transformation = transformation
            }
        }
    
    static member ToJson (x : BookmarkSnapshot) =
        json {
            do! Json.write "filename"       x.filename    
            do! Json.write "transformation" x.transformation                    
        }

type BookmarkSnapshotAnimation = {
  fieldOfView   : option<float>
  nearplane     : float
  farplane      : float
  resolution    : V2i
  snapshots     : list<BookmarkSnapshot>
} with
    static member defaultNearplane = 0.1
    static member defaultFarplane  = 100000.0
    static member FromJson(_ : BookmarkSnapshotAnimation) = 
      json {
          let! fieldOfView    = Json.tryRead "fieldOfView"
          let! resolution     = Json.read "resolution"
          let! snapshots      = Json.read "snapshots"
          let! nearplane      = Json.tryRead "nearplane"
          let nearplane =
            match nearplane with
            | Some np -> np
            | None    -> BookmarkSnapshotAnimation.defaultNearplane

          let! farplane       = Json.tryRead "farplane"
          let farplane =
            match farplane with
            | Some fp -> fp
            | None    -> BookmarkSnapshotAnimation.defaultFarplane
          
          let a : BookmarkSnapshotAnimation = 
              {
                  fieldOfView = fieldOfView
                  resolution  = resolution |> V2i.Parse
                  nearplane   = nearplane
                  farplane    = farplane
                  snapshots   = snapshots
              }
          return a
      }
    static member ToJson (x : BookmarkSnapshotAnimation) =
      json {
          do! PRo3D.Base.Json.writeOptionFloat "fieldOfView"    x.fieldOfView
          do! Json.write                       "resolution"     (x.resolution.ToString ())
          do! Json.write                       "snapshots"      x.snapshots
          do! Json.write                       "nearplane"      x.nearplane
          do! Json.write                       "farplane"       x.farplane
      }  

module BookmarkSnapshotAnimation =
    let tryFirst (m : BookmarkSnapshotAnimation) =
        let opt = List.tryHead m.snapshots
        match opt with
        | Some t ->
            match t.transformation with
            | BookmarkTransformation.Bookmark b ->
                Some b
            | _ -> None
        | None -> None


type Snapshot =
    | Bookmark of BookmarkSnapshot
    | Surface of SurfaceSnapshot
    | Panorama of PanoramaSnapshot
with 
    static member ToJson x =
        match x with
        | Snapshot.Bookmark x -> 
            Json.write "Bookmark" x
        | Snapshot.Surface x -> 
            Json.write "Surface" x
        | Snapshot.Panorama x -> 
            Json.write "Panorama" x

    static member FromJson(_ : Snapshot) = 
        json { 
            let! surface = Json.tryRead "Surface"
            match surface with
            | Some surface -> 
                return Snapshot.Surface surface
            | None ->
                let! bookmark = Json.tryRead "Bookmark"
                match bookmark with
                | Some bookmark -> 
                    return Snapshot.Bookmark bookmark
                | None ->
                    let! panorama = Json.read "Panorama"
                    return Snapshot.Panorama panorama
        }

/// Camera and Surface animation
type CameraSnapshotAnimation = {
  fieldOfView   : option<float>
  resolution    : V2i
  nearplane     : option<float>
  farplane      : option<float>
  lightLocation : option<V3d>
  renderMask    : option<bool>
  snapshots     : list<SurfaceSnapshot>
}
with 
  static member defaultNearplane = 0.001
  static member defaultFarplane  = 10000000.0
  static member defaultFoV = 30.0
  static member fromPanoramaCollection (pc : PanoramaSnapshotCollection) =
    {
        fieldOfView   = Some pc.fieldOfView  
        resolution    = pc.resolution   
        nearplane     = Some pc.nearplane    
        farplane      = Some pc.farplane     
        lightLocation = None
        renderMask    = None
        snapshots     = 
            pc.snapshots    
            |> List.collect (fun p -> 
                // Mars3D-AI: create 6 camera snapshots in each direction for each panorama entry
                let cam v postfix =
                    SurfaceSnapshot.fromNameAndCamera 
                        (p.filename + postfix)
                        (SnapshotCamera.fromCamera (CameraView.withForward v p.camera.view))

                let letstry = p.camera.view.Orientation //normalized quaternion
                    
                let directions = [ 
                     cam p.camera.forward "_FORWARD"                                                    
                     cam p.camera.view.Backward "_BACKWARD"
                     cam p.camera.view.Up "_UP"
                     cam p.camera.view.Down "_DOWN"
                     cam p.camera.view.Left "_LEFT"
                     cam p.camera.view.Right "_RIGHT"
                    ]
                directions)
    }
  member  snapshotAnimation.Frustum = 
      let resolution = V3i (snapshotAnimation.resolution.X, snapshotAnimation.resolution.Y, 1)

      let foV = 
          match snapshotAnimation.fieldOfView with
          | Some fov -> fov
          | None -> CameraSnapshotAnimation.defaultFoV

      let near =
          match snapshotAnimation.nearplane with
          | Some near -> near
          | None -> CameraSnapshotAnimation.defaultNearplane

      let far =
          match snapshotAnimation.farplane with
          | Some far -> far
          | None -> CameraSnapshotAnimation.defaultFarplane
      let frustum =
          Frustum.perspective foV near far (float(resolution.X)/float(resolution.Y))
      frustum

  static member TestData =
      {
          fieldOfView = Some 5.47
          resolution  = V2i(4096)
          nearplane   = Some 0.1
          farplane    = Some 1000000.0
          lightLocation = 5.0 |> V3d |> Some
          snapshots   = [SurfaceSnapshot.TestData]
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
          
          let a : CameraSnapshotAnimation = 
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
  static member FromJson(_ : CameraSnapshotAnimation) = 
      json {
          let! v = Json.read "version"
          match v with            
              | 0 -> return! CameraSnapshotAnimation.readV0
              | _ -> return! v |> sprintf "don't know version %A  of HeraAnimation" |> Json.error
      }
  static member ToJson (x : CameraSnapshotAnimation) =
      json {
          do! Json.write              "version"        0
          do! PRo3D.Base.Json.writeOptionFloat   "fieldOfView"    x.fieldOfView
          do! Json.write              "resolution"     (x.resolution.ToString ())
          if x.lightLocation.IsSome then
            do! PRo3D.Base.Json.writeOption        "lightLocation"  (x.lightLocation)
          do! PRo3D.Base.Json.writeOptionFloat   "nearplane"      (x.nearplane)
          do! PRo3D.Base.Json.writeOptionFloat   "farplane"       (x.farplane )
          if x.renderMask.IsSome then
            do! PRo3D.Base.Json.writeOptionBool "renderMask" x.renderMask
          do! Json.write              "snapshots"      x.snapshots
      }  

type SnapshotAnimation =
    | BookmarkAnimation of BookmarkSnapshotAnimation
    | CameraAnimation   of CameraSnapshotAnimation
    | PanoramaCollection of PanoramaSnapshotCollection
    //| LegacyAnimation   of LegacyAnimation
with 
    static member ToJson x =
        match x with
        | SnapshotAnimation.BookmarkAnimation x -> 
            Json.write "BookmarkAnimation" x
        | SnapshotAnimation.CameraAnimation x -> 
            Json.write "CameraAnimation" x
        | SnapshotAnimation.PanoramaCollection x -> 
            Json.write "PanoramaCollection" x
        //| SnapshotAnimation.LegacyAnimation x ->
        //    Json.write "LegacyAnimation" x

    static member FromJson(_ : SnapshotAnimation) = 
        json { 
            //let! legacy = Json.tryRead "LegacyAnimation"
            //match legacy with
            //| Some legacy -> 
            //    return SnapshotAnimation.LegacyAnimation legacy
            //| None ->
            let! animation = Json.tryRead "CameraAnimation"
            match animation with
            | Some cameraAnimation ->
                return SnapshotAnimation.CameraAnimation cameraAnimation
            | None ->
                let! bookmarkAnimation = Json.tryRead "BookmarkAnimation"
                match bookmarkAnimation with
                | Some bookmarkAnimation ->
                    return SnapshotAnimation.BookmarkAnimation bookmarkAnimation
                | None ->
                    let! panorama = Json.read "PanoramaCollection"
                    return 
                        SnapshotAnimation.PanoramaCollection panorama
        }

// image pose output for panorama computation
type PanoramaPose =
    {
        panoramaPose   : Affine3d
        fieldOfView    : float
        principalPoint : V2d
        //euclidPose    : Euclidean3d
    } 
    static member ToJson (x : PanoramaPose) = 
        json {
          do! Json.writeWith Ext.toJson<Affine3d,Ext> "panoramaPose"   x.panoramaPose
          do! Json.write                              "fieldOfView"    x.fieldOfView
          do! Json.write                              "principalPoint" (x.principalPoint.ToString ())
          //do! Json.writeWith Ext.toJson<Euclidean3d,Ext> "euclideanPose" x.euclidPose
        }
    static member FromJson (x : PanoramaPose) =
        json {
             let! panoramaPose   = Json.readWith Ext.fromJson<Affine3d,Ext> "panoramaPose"
             let! fieldOfView    = Json.read "fieldOfView"
             let! principalPoint = Json.read "principalPoint"
             //let! euclidPose = Json.readWith Ext.fromJson<Euclidean3d,Ext> "euclideanPose"

            return {
                panoramaPose    = panoramaPose 
                fieldOfView     = fieldOfView
                principalPoint  = principalPoint |> V2d.Parse
                //euclidPose = euclidPose
            }
        }