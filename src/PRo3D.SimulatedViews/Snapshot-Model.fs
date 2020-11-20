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
      let writeOptionFloat (name : string) (x : option<float>) =
        match x with
        | Some a ->
            Json.write name a
        | None ->
            Json.writeNone name

      let writeOptionInt (name : string) (x : option<int>) =
        match x with
        | Some a ->
            Json.write name a
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

type SnapshotShattercone = {
    name         : string
    count        : int
    color        : option<C4b>
    contrast     : option<float>
    brightness   : option<float>
    gamma        : option<float>
    scale        : option<V2i> // [min,max]
    xRotation    : option<V2i> // [min,max] in degree?
    yRotation    : option<V2i> 
    zRotation    : option<V2i>
    maxDistance  : option<float>
    subsurface   : option<int>
    maskColor    : option<C4b>
} 
with 
  static member TestData =
    {
        name         = "02-Presqu-ile-LF.obj"//"Steinheim_Germany_Model_1.obj"
        count        = 4
        color        = None //Some (C4b(242, 198, 153, 255))
        contrast     = None
        brightness   = None
        gamma        = None
        scale        = Some (V2i(30, 100))
        xRotation    = None
        yRotation    = None //Some (V2i(0, 180))
        zRotation    = Some (V2i(0, 360))
        maxDistance  = None // Some 5.0
        subsurface   = Some 50 //None
        maskColor    = Some C4b.Green
    }
  static member current = 0
  static member private readV0 = 
      json {
        let! name        = Json.read "name"
        let! count       = Json.read "count"
        let! color       = Json.parseOption (Json.tryRead "color") C4b.Parse
        let! contrast    = Json.tryRead "contrast"
        let! brightness  = Json.tryRead "brightness"
        let! gamma       = Json.tryRead "gamma"
        let! scale       = Json.parseOption (Json.tryRead "scale") V2i.Parse 
        let! xRotation   = Json.parseOption (Json.tryRead "xRotation") V2i.Parse 
        let! yRotation   = Json.parseOption (Json.tryRead "yRotation") V2i.Parse 
        let! zRotation   = Json.parseOption (Json.tryRead "zRotation") V2i.Parse 
        let! maxDistance = Json.tryRead "maxDistance"
        let! subsurface  = Json.tryRead "subsurface"
        let! maskColor   = Json.parseOption (Json.tryRead "maskColor") C4b.Parse
        
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
            maxDistance  = maxDistance
            subsurface   = subsurface 
            maskColor    = maskColor            
        }
        return res
      }
  static member FromJson(_ : SnapshotShattercone) = 
    json {
        return! SnapshotShattercone.readV0
    }
  static member ToJson (x : SnapshotShattercone) =
    json {
      do! Json.write              "name"          (x.name.ToString())
      do! Json.write              "count"         x.count
      if x.color.IsSome then
        do! Json.writeOption      "color"       x.color
      if x.contrast.IsSome then
        do! Json.writeOptionFloat "contrast"    x.contrast
      if x.brightness.IsSome then
        do! Json.writeOptionFloat "brightness"  x.brightness
      if x.gamma.IsSome then
        do! Json.writeOptionFloat "gamma"       x.gamma
      do! Json.writeOption        "scale"         x.scale
      do! Json.writeOption        "xRotation"     x.xRotation
      do! Json.writeOption        "yRotation"     x.yRotation
      do! Json.writeOption        "zRotation"     x.zRotation
      do! Json.writeOptionFloat   "maxDistance"   x.maxDistance
      do! Json.writeOptionInt     "subsurface"    x.subsurface
      if x.maskColor.IsSome then
        do! Json.writeOption      "maskColor"   x.maskColor
    }