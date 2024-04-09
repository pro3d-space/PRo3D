namespace PRo3D.Base.Gis

open Chiron
open Aardvark.Base
open Adaptify

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







