#nowarn "9"
namespace PRo3D.Base.Gis

open System
open Chiron
open Aardvark.Base
open Adaptify
open Aardvark.Rendering

type EntitySpiceName = EntitySpiceName of string
with 
    member x.Value = 
        let (EntitySpiceName v) = x in v
    static member FromJson(_ : EntitySpiceName) = 
        json {
            let! v  = Json.read "EntitySpiceName"
            return (EntitySpiceName v)
        }
    static member ToJson (x : EntitySpiceName) =
        json {              
            do! Json.write "EntitySpiceName" x.Value
        }

module EntitySpiceName =
    let value (EntitySpiceName spiceName) =
        spiceName

type FrameSpiceName = FrameSpiceName of string
with 
    member x.Value = 
        let (FrameSpiceName v) = x in v
    static member FromJson(_ : FrameSpiceName) = 
        json {
            let! v  = Json.read "FrameSpiceName"
            return (FrameSpiceName v)
        }
    static member ToJson (x : FrameSpiceName) =
        json {              
            do! Json.write "FrameSpiceName" x.Value
        }

module FrameSpiceName =
    let value (FrameSpiceName spiceName) =
        spiceName

/// Reference Frames "A reference frame (or simply “frame”) is specified by an
/// ordered set of three mutually orthogonal, possibly time dependent, unit-length direction vectors"
/// https://naif.jpl.nasa.gov/pub/naif/toolkit_docs/Tutorials/pdf/individual_docs/17_frames_and_coordinate_systems.pdf
/// https://naif.jpl.nasa.gov/pub/naif/toolkit_docs/C/req/frames.html
[<ModelType>]
type ReferenceFrame =
    {
        [<NonAdaptive>]
        version     : int
        label       : string
        description : option<string>
        [<NonAdaptive>]
        spiceName   : FrameSpiceName
        spiceNameText : string
        isEditing   : bool
        entity      : option<EntitySpiceName>
    } 
with
    static member current = 0
    static member private readV0 = 
        json {
            let! label       = Json.read    "label"
            let! description = Json.tryRead "description"
            let! spiceName   = Json.read    "spiceName"
            let! entity      = Json.tryRead "entity"
            
            return {
                version      = ReferenceFrame.current
                label        = label      
                description  = description
                spiceName    = spiceName  
                spiceNameText = spiceName.Value
                isEditing    = false
                entity       = entity
            }
        }
    static member FromJson(_ : ReferenceFrame) = 
        json {
            let! v = Json.read "version"
            match v with            
            | 0 -> return! ReferenceFrame.readV0
            | _ -> return! v |> sprintf "don't know version %A  of ReferenceFrame" |> Json.error
        }
    static member ToJson (x : ReferenceFrame) =
        json {              
            do! Json.write      "version"      ReferenceFrame.current
            do! Json.write      "label"        x.label      
            if x.description.IsSome then
                do! Json.write  "description"  x.description.Value
            do! Json.write      "spiceName"    x.spiceName  
            if x.entity.IsSome then
                do! Json.write  "entity"       x.entity.Value
        }

/// Entities are natural bodies or spacecraft.
/// “Body” means a natural body: sun, planet, satellite, comet, asteroid.
/// https://cosmoguide.org/catalog-file-defining-a-natural-body/
[<ModelType>]
type Entity = {
    [<NonAdaptive>]
    version      : int
    [<NonAdaptive>]
    spiceName    : EntitySpiceName
    isEditing    : bool
    draw         : bool
    // adaptive spiceName text for creating new Entities
    spiceNameText : string
    label        : string
    color        : C4f
    radius       : float
    geometryPath : option<string>
    textureName  : option<string>
    showTrajectory : bool
    defaultFrame : option<FrameSpiceName>
} with
    static member current = 0
    static member private readV0 = 
        json {
            let! label        = Json.read    "label"       
            let! spiceName    = Json.read    "spiceName"   
            let! color        = Json.read    "color"       
            let! radius       = Json.read    "radius"      
            let! geometryPath = Json.tryRead "geometryPath"
            let! textureName  = Json.tryRead "textureName" 
            let! defaultFrame = Json.read    "defaultFrame"
            let! (draw : option<bool>) = Json.tryRead "draw"
            let draw = Option.defaultValue false draw
            let! showTrajectory = Json.tryRead "showTrajectory"
            
            return {
                version      = Entity.current
                label        = label       
                spiceName    = spiceName   
                spiceNameText = spiceName.Value
                isEditing    = false
                draw         = draw
                color        = C4f.Parse color       
                radius       = radius      
                geometryPath = geometryPath
                textureName  = textureName 
                defaultFrame = defaultFrame
                showTrajectory = Option.defaultValue false showTrajectory
            }
        }
    static member FromJson(_ : Entity) = 
        json {
            let! v = Json.read "version"
            match v with            
            | 0 -> return! Entity.readV0
            | _ -> return! v |> sprintf "don't know version %A  of ReferenceFrame" |> Json.error
        }
    static member ToJson (x : Entity) =
        json {              
            do! Json.write "version"      Entity.current
            do! Json.write "label"        x.label       
            do! Json.write "spiceName"    x.spiceName   
            do! Json.write "color"        (string x.color)
            do! Json.write "radius"       x.radius      
            do! Json.write "geometryPath" x.geometryPath
            do! Json.write "textureName"  x.textureName 
            do! Json.write "defaultFrame" x.defaultFrame
            do! Json.write "draw"         x.draw
            do! Json.write "showTrajectory" x.showTrajectory
        }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]    
module Entity =              
    let mars =
        {
            version       = Entity.current
            label         = "Mars"        
            spiceName     = EntitySpiceName "Mars"    
            spiceNameText = "Mars"
            isEditing     = false
            draw          = false
            color         = C4f.Red       
            geometryPath  = None
            radius        = 3376200.0 //polar radius in meter
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "IAU_MARS")
            showTrajectory = false
        }

    let deimos =
        {
            version       = Entity.current
            label         = "Deimos"        
            spiceName     = EntitySpiceName "deimos"    
            spiceNameText = "Deimos"
            isEditing     = false
            draw          = false
            color         = C4f.Gray       
            geometryPath  = None
            radius        = 6250.0 //polar radius in meter
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "ECLIPJ2000")
            showTrajectory = false
        }

    let phobos =
        {
            version       = Entity.current
            label         = "Phobos"        
            spiceName     = EntitySpiceName "Phobos"    
            spiceNameText = "Phobos"
            isEditing     = false
            draw          = false
            color         = C4f.DarkGoldenRod       
            geometryPath  = None
            radius        = 11266.5 //polar radius in meter
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "ECLIPJ2000")
            showTrajectory = false
        }

    let earth =
        {
            version       = Entity.current
            label         = "Earth"        
            spiceName     = EntitySpiceName "Earth"    
            spiceNameText = "Earth"
            isEditing     = false
            draw          = false
            color         = C4f.Blue       
            geometryPath  = None
            radius        = 6356800.0 // polar radius in meter
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "IAU_EARTH")
            showTrajectory = false
        }

    let moon =
        {
            version       = Entity.current
            label         = "Moon"        
            spiceName     = EntitySpiceName "Moon"    
            spiceNameText = "Moon"
            isEditing     = false
            draw          = false
            color         = C4f.Silver       
            geometryPath  = None
            radius        = 1736000.0 //polar radius in meter
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "IAU_MOON") // should maybe used different default?
            showTrajectory = false
        }

    let didymos =
        {
            version       = Entity.current
            label         = "Didymos"       
            spiceName     = EntitySpiceName "Didymos"  
            spiceNameText = "Didymos"
            isEditing     = false
            draw          = false
            color         = C4f.Grey       
            geometryPath  = None
            radius        = 382.5 //mean radius +/- 2.5m
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "ECLIPJ2000") 
            showTrajectory = false
        }

    let dimorphos =
        {
            version       = Entity.current
            label         = "Dimorphos"  
            spiceName     = EntitySpiceName "Dimorphos"  
            spiceNameText = "Dimorphos"
            isEditing     = false
            draw          = false
            color         = C4f.Grey       
            geometryPath  = None
            radius        = 75.5 //mean radius +/- 2.5m
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "ECLIPJ2000") 
            showTrajectory = false
        }
    let heraSpacecraft =
        {
            version       = Entity.current
            label         = "Hera Spacecraft"
            spiceName     = EntitySpiceName "HERA" // ?? Need to check!
            spiceNameText = "HERA"
            isEditing     = false
            draw          = false
            color         = C4f.Grey       
            geometryPath  = None
            radius        = 2.0 // ?
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "ECLIPJ2000") // DIMORPHOS_FIXED ?
            showTrajectory = false
        }
        
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]    
module ReferenceFrame =   
    ///Wikipedia: Defined with the Earth's Mean Equator and Mean Equinox (MEME) at 12:00 Terrestrial Time on 1 January 2000.
    /// https://en.wikipedia.org/wiki/Earth-centered_inertial
    let j2000 = 
        {
            version     = ReferenceFrame.current
            label       = "J2000"
            description = Some "Defined with Earth's Mean Equator and Mean Equinox (MEME) at 12:00 Terrestrial Time on 1 January 2000"
            spiceName   = FrameSpiceName "J2000"
            spiceNameText = "J2000"
            entity      = None
            isEditing   = false
        }
    let eclipJ2000 = 
        {
            version     = ReferenceFrame.current
            label       = "ECLIPJ2000"
            description = Some "Ecliptic coordinates based upon the J2000 frame."
            spiceName   = FrameSpiceName "ECLIPJ2000"
            spiceNameText = "ECLIPJ2000"
            entity      = None
            isEditing   = false
        }
    let iauMars = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_MARS"
            description = Some "Mars body-fixed frame"
            spiceName   = FrameSpiceName "IAU_MARS"
            spiceNameText = "IAU_MARS"
            entity      = Some (EntitySpiceName "Mars")
            isEditing   = false
        }
    let iauEarth = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_EARTH"
            description = Some "Earth body-fixed frame"
            spiceName   = FrameSpiceName "IAU_EARTH"
            spiceNameText = "IAU_EARTH"
            entity      = Some (EntitySpiceName "Earth")
            isEditing   = false
        }

[<Struct>]
type TransformedBody = 
    {
        lookAtBody : CameraView
        position : V3d
        alignBodyToObserverFrame : M33d
    } with
        member x.Trafo = 
            let shift = Trafo3d.Translation x.position
            let m44d = M44d x.alignBodyToObserverFrame
            let bodyToObserver = Trafo3d(m44d, m44d.Inverse)
            bodyToObserver * shift

module TransformedBody =
    let trafo (o : TransformedBody) = o.Trafo

module CooTransformation =
    open PRo3D.Extensions
    open PRo3D.Extensions.FSharp
    open System

    let getPositionTransformationMatrix (pcFrom : string) (pcTo : string) (time : DateTime) : Option<M33d> = 
        let m33d : array<double> = Array.zeroCreate 9
        let pdRotMat = fixed &m33d[0] 
        let result = CooTransformation.GetPositionTransformationMatrix(pcFrom, pcTo, CooTransformation.Time.toUtcFormat time, pdRotMat)
        if result <> 0 then 
            None
        else
            m33d |> M33d |> Some


    let transformBody (body : EntitySpiceName) (bodyFrame : Option<FrameSpiceName>) (observer : EntitySpiceName) (observerFrame : FrameSpiceName) (time : DateTime) =
        let (EntitySpiceName body), (EntitySpiceName observer), (FrameSpiceName observerFrame) = body, observer, observerFrame
        let bodyFrame = 
            match bodyFrame with
            | Some (FrameSpiceName bodyFrame) -> bodyFrame
            | None -> observerFrame

        let suportBody = "sun"
        let relState = CooTransformation.getRelState body suportBody observer time observerFrame 
        let rot = getPositionTransformationMatrix bodyFrame observerFrame time
        let switchToLeftHanded = Trafo3d.FromBasis(V3d.IOO, -V3d.OOI, -V3d.OIO, V3d.Zero)
        let flipZ = Trafo3d.FromBasis(V3d.IOO, V3d.OIO, -V3d.OOI, V3d.Zero)
        match relState, rot with
        | Some rel, Some rot -> 
            let relFrame = rel.rot 
            let t = Trafo3d.FromBasis(relFrame.C0, relFrame.C1, -relFrame.C2, V3d.Zero)
            Some { 
                lookAtBody = CameraView.ofTrafo t.Inverse
                position = rel.pos
                alignBodyToObserverFrame = rot  
            }
        | _ -> 
            Log.line $"[SPICE] failed to transform body (body = {body}, bodyFrame = {bodyFrame}, observer = {observer}, observerFrame = {observerFrame}, time = {time}."
            None



type SpiceReferenceSystem = { referenceFrame : FrameSpiceName; body : EntitySpiceName } 
type ObserverSystem = { referenceFrame : FrameSpiceName; body : EntitySpiceName; time : DateTime }