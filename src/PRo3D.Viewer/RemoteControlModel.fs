namespace RemoteControlModel

open System
open System.Runtime.Serialization

open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.Base.Incremental
open PRo3D
open PRo3D.Viewer
open PRo3D.Viewplanner


//PTU values sequence
//isntrument model
//instrument pose

[<DataContract>]
type Xyz = {
    [<field: DataMember(Name = "x")>]
    x : float
    [<field: DataMember(Name = "y")>]
    y : float
    [<field: DataMember(Name = "z")>]
    z : float
}

module Xyz =
    let fromV3d (v : V3d) =
        { x = v.X; y = v.Y; z = v.Z }

    let toV3d (v : Xyz) : V3d =
        V3d(v.x, v.y, v.z)
 
 
//TODO TO: remove data contracts and use chiron
[<DataContract>]
type PlatformShot = {
    [<field: DataMember(Name = "pos")>]
    pos     : Xyz
    [<field: DataMember(Name = "lookAt")>]
    lookAt  : Xyz
    [<field: DataMember(Name = "up")>]
    up      : Xyz
    [<field: DataMember(Name = "pan")>] //"Pan Axis"
    pan        : double
    [<field: DataMember(Name = "tilt")>] //"Tilt Axis"
    tilt       : double
    [<field: DataMember(Name = "focal")>]
    focal      : double
    [<field: DataMember(Name = "instrument")>]
    instrument : string
    [<field: DataMember(Name = "rover")>]
    rover      : string
    [<field: DataMember(Name = "near")>]
    near    : float
    [<field: DataMember(Name = "far")>]
    far     : float
    [<field: DataMember(Name = "id")>]
    id      : string
    [<field: DataMember(Name = "folder")>]
    folder  : string   
}

[<DataContract>]
type Shot = {    
    [<field: DataMember(Name = "pos")>]
    pos     : Xyz
    [<field: DataMember(Name = "lookAt")>]
    lookAt  : Xyz
    [<field: DataMember(Name = "up")>]
    up      : Xyz
    [<field: DataMember(Name = "col")>]    
    col     : int
    [<field: DataMember(Name = "row")>]
    row     : int
    [<field: DataMember(Name = "pph")>]
    pph     : int
    [<field: DataMember(Name = "ppv")>]
    ppv     : int
    [<field: DataMember(Name = "hfov")>]
    hfov     : float
    [<field: DataMember(Name = "near")>]
    near    : float
    [<field: DataMember(Name = "far")>]
    far     : float
    [<field: DataMember(Name = "id")>]
    id      : string
    [<field: DataMember(Name = "folder")>]
    folder  : string   
}

type ViewSpecification = {
    principalPoint : V2d
    focal          : V2d
    focalMm        : double
    resolution     : V2i
    near           : double
    far            : double
    fovH           : double
    view           : CameraView
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Shot =
    open System

    let instInit = 
        {
            rover      = ""
            instrument = ""
            focal      = 0.0
            pan        = 0.0
            tilt       = 0.0
            pos     = V3d.III |> Xyz.fromV3d
            lookAt  = V3d.OOO |> Xyz.fromV3d
            up      = V3d.OOI |> Xyz.fromV3d            
            near    = 0.1
            far     = 10000.0
            id      = Guid.NewGuid().ToString()
            folder  = "./shots" 
        }

    let init = 
        {
            pos     = V3d.III |> Xyz.fromV3d
            lookAt  = V3d.OOO |> Xyz.fromV3d
            up      = V3d.OOI |> Xyz.fromV3d
            col     = 1024
            row     = 768
            pph     = 0
            ppv     = 0
            hfov    = 60.0
            near    = 0.1
            far     = 10000.0
            id      = Guid.NewGuid().ToString()
            folder  = "./shots"           
        }

    let norm (v:V3d) = 
        v.Normalized


    let getCamera (s : Shot) : CameraView =

        let pos = s.pos |> Xyz.toV3d
        let forw = s.lookAt |> Xyz.toV3d
        let lookAt = pos + forw

        CameraView.LookAt(pos, lookAt , s.up |> Xyz.toV3d)
    
    let getViewSpec (s : Shot) : ViewSpecification =

        let cv = s |> getCamera

        let res = V2i (s.col, s.row)
        let pph = (float s.pph /float res.X) - 0.5
        let ppv = (float s.ppv /float res.Y) - 0.5

        {
            view = cv
            fovH = s.hfov
            focalMm = 0.0
            focal = V2d.Zero
            principalPoint = V2d (pph, ppv)
            resolution = res
            near = s.near
            far = s.far
        }        

    let fromCamera (cv : CameraView) : Shot =
        { init with 
                pos     = cv.Location |> Xyz.fromV3d
                up      = cv.Up |> Xyz.fromV3d
                lookAt  = cv.Forward |> Xyz.fromV3d  }

    let fromWp (wp : WayPoint) : Shot =        
        { fromCamera wp.cv with id = wp.name }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PlatformShot =        
    

    let init = 
        {
            rover      = ""
            instrument = ""
            focal      = 0.0
            pan        = 0.0
            tilt       = 0.0
            pos     = V3d.III |> Xyz.fromV3d
            lookAt  = V3d.OOO |> Xyz.fromV3d
            up      = V3d.OOI |> Xyz.fromV3d            
            near    = 0.1
            far     = 10000.0
            id      = Guid.NewGuid().ToString()
            folder  = "./shots" 
        }

    let withShot (sh : Shot) (psh : PlatformShot) : PlatformShot =
        { psh with 
            pos = sh.pos 
            lookAt = sh.lookAt
            up = sh.up            
            id = sh.id
            folder = sh.folder
        }

    let trafoFromRoverBase (forw:V3d) (up:V3d) (pos:V3d) =        
        let right = forw.Cross up
        let right = -right

        let rotTrafo =
            Trafo3d(
                M44d(
                    forw.X, right.X, up.X, 0.0,
                    forw.Y, right.Y, up.Y, 0.0,
                    forw.Z, right.Z, up.Z, 0.0,
                    0.0, 0.0, 0.0, 1.0
                ),
                M44d(
                    forw.X, forw.Y, forw.Z, 0.0,
                    right.X, right.Y, right.Z, 0.0,
                    up.X, up.Y, up.Z, 0.0,
                    0.0, 0.0, 0.0, 1.0
                )
            )

        Trafo3d(rotTrafo * Trafo3d.Translation(pos))     

    let trafoFromPlatformShot (psh : PlatformShot) = 
        let forw = psh.lookAt |> Xyz.toV3d
        let pos = psh.pos |> Xyz.toV3d
        let up = psh.up |> Xyz.toV3d

        trafoFromRoverBase forw up pos
    
    let fromRoverModel (rm : RoverModel) (sh : Shot): option<PlatformShot> =
        
        let getRover (rover : option<Rover>) (result : PlatformShot)  : option<PlatformShot> =
            rover |> Option.map (fun r -> { result with rover = r.id })

        let getInstrument (instrument : option<Instrument>) (result : PlatformShot) : option<PlatformShot> =
            instrument |> Option.map (fun i -> 
                { result with 
                    instrument = i.id; 
                    focal = i.focal.value                                    
                })

        let getAxes (rover : option<Rover>) (result : PlatformShot) : option<PlatformShot> =
            match rover with 
              | None -> None
              | Some r ->

                let panAx =
                    r.axes.TryFind "Pan Axis" |> Option.map(fun x ->  { result with pan = x.angle.value } )

                let tiltAx = panAx |> Option.bind(fun x -> 
                    r.axes.TryFind "Tilt Axis" |> Option.map(fun y -> { x with tilt = y.angle.value } ))
                    
                tiltAx
                

        let result = 
            Some init 
              |> Option.bind(fun x -> getRover rm.selectedRover x)
              //|> Option.bind(fun x -> getInstrument rm.selectedInstrument x)
              |> Option.bind(fun x -> getAxes rm.selectedRover x)
              |> Option.map(fun x -> x |> withShot sh)
        
        result

    let getCameraAnfFov (m : RoverModel) (p : PlatformShot)  =
        let trafo = trafoFromPlatformShot p
        let r = m.rovers |> HMap.find p.rover
        let inst = r.instruments |> HMap.find p.instrument

        let trans = inst.extrinsics |> Extrinsics.transformed trafo.Forward                                                    
                                                                      
        let cv = CameraView.LookAt(trans.position, trans.position + trans.camLookAt, trans.camUp)                            
        let hfov = inst.intrinsics.horizontalFieldOfView.DegreesFromGons()

        printfn "focal length: %A" inst.focal

        cv, hfov

    let updateFocus (p : PlatformShot) (m : RoverModel) =
        let r = m.rovers |> HMap.find p.rover
        let inst = r.instruments |> HMap.find p.instrument
       
        if inst.calibratedFocalLengths.Length > 1 then
            let up = {
                roverId = p.rover
                instrumentId = p.instrument
                focal = p.focal
            }
            RoverApp.updateFocusPlatform up m
        else
            m

    let getViewSpec (m : RoverModel) (p : PlatformShot) : ViewSpecification =

        let m = m |> updateFocus p

        let trafo = trafoFromPlatformShot p
        let r = m.rovers |> HMap.find p.rover
        let inst = r.instruments |> HMap.find p.instrument
        
        let trans = inst.extrinsics |> Extrinsics.transformed trafo.Forward                         
                                                                      
        let cv = CameraView.LookAt(trans.position, trans.position + trans.camLookAt, trans.camUp)     
        let hfov = inst.intrinsics.horizontalFieldOfView.DegreesFromGons()

        let res = V2i (int inst.intrinsics.horizontalResolution,int inst.intrinsics.verticalResolution)
        let pph = (inst.intrinsics.horizontalPrinciplePoint / float res.X) - 0.5
        let ppv = (inst.intrinsics.verticalPrinciplePoint / float res.Y) - 0.5

        {
            view = cv
            fovH = hfov
            focalMm = inst.focal.value
            focal = V2d (inst.intrinsics.horizontalFocalLengthPerPixel, inst.intrinsics.verticalFocalLengthPerPixel)
            principalPoint = V2d (pph, ppv)
            resolution = res
            near = p.near
            far = p.far
        }

[<DomainType>]
type RemoteModel =
    {
        //viewPoints : plist<WayPoint>
        selectedShot : Option<Shot>
        shots            : plist<Shot>
        platformShots    : plist<PlatformShot>
        Rover            : RoverModel
    }

type RemoteAction = 
    | SetCameraView      of CameraView    
    | SetView            of ViewSpecification
    

type Action = 
    | CaptureShot  of Shot
    | UpdateCameraTest    of Shot
    | CapturePlatform of PlatformShot
    | UpdatePlatformTest   of Shot
    | SelectShot      of Shot
    | Play
    | Load
    | SaveModel
    | RemoveModel
    | OpenFolder     of string
    | RoverMessage   of RoverApp.Action