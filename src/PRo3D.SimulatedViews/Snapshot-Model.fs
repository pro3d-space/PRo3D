namespace PRo3D.SimulatedViews

open Chiron
open Aardvark.Base

module Json =
      let parseOption (x : Json<Option<'a>>) (f : 'a -> 'b) = 
          x |> Json.map (fun x -> x |> Option.map (fun y -> f y))
      let writeOption (name : string) (x : option<'a>) =
        match x with
        | Some a ->
            Json.write name (a.ToString ())
        | None ->
            Json.writeNone name
      let writeOptionList (name : string) 
                       (x : option<List<'a>>) 
                       (f : List<'a> -> string -> Json<unit>) = //when 'a : (static member ToJson : () -> () )>>) =
        match x with
        | Some a ->
            f a name
        | None ->
            Json.writeNone name

type HeraCam = {
    location      : V3d
    forward       : V3d
    up            : V3d
    }
with 
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
  static member FromJson(_ : HeraCam) = 
    json {
        return! HeraCam.readV0
    }
  static member ToJson (x : HeraCam) =
    json {
      do! Json.write      "location"  (x.location.ToString())
      do! Json.write      "forward"   (x.forward.ToString())
      do! Json.write      "up"        (x.up.ToString())
    }

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
    trafo         : option<Trafo3d>
    visible       : option<bool>
} 
with 
  static member TestData =
    {
        surfname = "testname"
        trafo    = Some Trafo3d.Identity
        visible  = Some true
    }
  static member current = 0
  static member private readV0 = 
      json {
        let! opcname    = Json.read "opcname"
        let! trafo       = Json.parseOption (Json.tryRead "trafo") Trafo3d.Parse
        let! visible     = Json.tryRead "visible"
        
        let res = {
            surfname = opcname
            trafo    = trafo
            visible  = visible
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
    }

type SnapshotShattercone = {
    name        : string
    count       : int
    color       : option<C4b>
    contrast    : option<float>
    brightness  : option<float>
    gamma       : option<float>
    scale       : option<V2i> // [min,max]
    xRotation   : option<V2i> // [min,max] in degree?
    yRotation   : option<V2i> 
    zRotation   : option<V2i>
} 
with 
  static member TestData =
    {
        name       = "Steinheim_Germany_Model_1.obj"
        count      = 10
        color      = Some C4b.Green //(C4b(242, 198, 153, 255))
        contrast   = None
        brightness = None
        gamma      = None
        scale      = Some (V2i(1, 5))
        xRotation  = None
        yRotation  = Some (V2i(0, 180))
        zRotation  = Some (V2i(0, 360))
    }
  static member current = 0
  static member private readV0 = 
      json {
        let! name       = Json.read "name"
        let! count      = Json.read "count"
        let! color      = Json.parseOption (Json.tryRead "color") C4b.Parse
        let! contrast   = Json.tryRead "contrast"
        let! brightness = Json.tryRead "brightness"
        let! gamma      = Json.tryRead "gamma"
        let! scale      = Json.parseOption (Json.tryRead "scale") V2i.Parse 
        let! xRotation  = Json.parseOption (Json.tryRead "xRotation") V2i.Parse 
        let! yRotation  = Json.parseOption (Json.tryRead "yRotation") V2i.Parse 
        let! zRotation  = Json.parseOption (Json.tryRead "zRotation") V2i.Parse 
        
        let res = {
            name        = name
            count       = count
            color       = color
            contrast    = contrast
            brightness  = brightness
            gamma       = gamma
            scale       = scale 
            xRotation   = xRotation 
            yRotation   = yRotation 
            zRotation   = zRotation   
        }
        return res
      }
  static member FromJson(_ : SnapshotShattercone) = 
    json {
        return! SnapshotShattercone.readV0
    }
  static member ToJson (x : SnapshotShattercone) =
    json {
      do! Json.write        "name"          (x.name.ToString())
      do! Json.write        "count"         x.count
      do! Json.writeOption  "color"         x.color
      do! Json.writeOption  "contrast"      x.contrast
      do! Json.writeOption  "brightness"    x.brightness
      do! Json.writeOption  "gamma"         x.gamma
      do! Json.writeOption  "scale"         x.scale
      do! Json.writeOption  "xRotation"     x.xRotation
      do! Json.writeOption  "yRotation"     x.yRotation
      do! Json.writeOption  "zRotation"     x.zRotation
    }