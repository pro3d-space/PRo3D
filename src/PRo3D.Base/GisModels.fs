namespace PRo3D.Base.Gis

open Chiron
open Aardvark.Base


type BodySpiceName = BodySpiceName of string
with 
    member x.Value = 
        let (BodySpiceName v) = x in v
    static member FromJson(_ : BodySpiceName) = 
        json {
            let! v  = Json.read "BodySpiceName"
            return (BodySpiceName v)
        }
    static member ToJson (x : BodySpiceName) =
        json {              
            do! Json.write "BodySpiceName" x.Value
        }

module BodySpiceName =
    let value (BodySpiceName spiceName) =
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

type SpacecraftSpiceName = SpacecraftSpiceName of string
with 
    member x.Value = 
        let (SpacecraftSpiceName v) = x in v
    static member FromJson(_ : SpacecraftSpiceName) = 
        json {
            let! v  = Json.read "SpacecraftSpiceName"
            return (SpacecraftSpiceName v)
        }
    static member ToJson (x : SpacecraftSpiceName) =
        json {              
            do! Json.write "SpacecraftSpiceName" x.Value
        }
module SpacecraftSpiceName =
    let value (SpacecraftSpiceName spiceName) =
        spiceName

/// Reference Frames "A reference frame (or simply “frame”) is specified by an
/// ordered set of three mutually orthogonal, possibly time dependent, unit-length direction vectors"
/// https://naif.jpl.nasa.gov/pub/naif/toolkit_docs/Tutorials/pdf/individual_docs/17_frames_and_coordinate_systems.pdf
/// https://naif.jpl.nasa.gov/pub/naif/toolkit_docs/C/req/frames.html
type ReferenceFrame =
    {
        version     : int
        label       : string
        description : option<string>
        spiceName   : FrameSpiceName
        body        : option<BodySpiceName>
    } 
with
    static member current = 0
    static member private readV0 = 
        json {
            let! label       = Json.read    "label"
            let! description = Json.tryRead "description"
            let! spiceName   = Json.read    "spiceName"
            let! body        = Json.tryRead "body"
            
            return {
                version      = ReferenceFrame.current
                label        = label      
                description  = description
                spiceName    = spiceName  
                body         = body
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
            do! Json.write      "description"  x.description
            do! Json.write      "spiceName"    x.spiceName  
        }

/// “Body” means a natural body: sun, planet, satellite, comet, asteroid.
/// https://cosmoguide.org/catalog-file-defining-a-natural-body/
type Body = {
    version      : int
    label        : string
    spiceName    : BodySpiceName
    color        : C4f
    radius       : float
    geometryPath : option<string>
    textureName  : option<string>
    defaultFrame : FrameSpiceName
} with
    static member current = 0
    static member private readV0 = 
        json {
            let! label        = Json.read    "label"       
            let! spiceName    = Json.read    "frameSpiceName"   
            let! color        = Json.read    "color"       
            let! radius       = Json.read    "radius"      
            let! geometryPath = Json.tryRead "geometryPath"
            let! textureName  = Json.tryRead "textureName" 
            let! defaultFrame = Json.read    "defaultFrame"
            
            return {
                version      = Body.current
                label        = label       
                spiceName    = spiceName   
                color        = C4f.Parse color       
                radius       = radius      
                geometryPath = geometryPath
                textureName  = textureName 
                defaultFrame = defaultFrame
            }
        }
    static member FromJson(_ : Body) = 
        json {
            let! v = Json.read "version"
            match v with            
            | 0 -> return! Body.readV0
            | _ -> return! v |> sprintf "don't know version %A  of ReferenceFrame" |> Json.error
        }
    static member ToJson (x : Body) =
        json {              
            do! Json.write "version"      Body.current
            do! Json.write "label"        x.label       
            do! Json.write "frameSpiceName"    x.spiceName   
            do! Json.write "color"        (string x.color)
            do! Json.write "radius"       x.radius      
            do! Json.write "geometryPath" x.geometryPath
            do! Json.write "textureName"  x.textureName 
            do! Json.write "defaultFrame" x.defaultFrame
        }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]    
module Body =              
    let mars =
        {
            version       = Body.current
            label         = "Mars"        
            spiceName     = BodySpiceName "Mars"    
            color         = C4f.Red       
            geometryPath  = None
            radius        = 3376200.0 //polar radius in meter
            textureName   = None
            defaultFrame  = FrameSpiceName "IAU_MARS"
        }

    let earth =
        {
            version       = Body.current
            label         = "Earth"        
            spiceName     = BodySpiceName "Earth"    
            color         = C4f.Blue       
            geometryPath  = None
            radius        = 6356800.0 // polar radius in meter
            textureName   = None
            defaultFrame  = FrameSpiceName "IAU_EARTH"
        }

    let moon =
        {
            version       = Body.current
            label         = "Moon"        
            spiceName     = BodySpiceName "Moon"    
            color         = C4f.Silver       
            geometryPath  = None
            radius        = 1736000.0 //polar radius in meter
            textureName   = None
            defaultFrame  = FrameSpiceName "IAU_MOON" // should maybe used different default?
        }

    let didymos =
        {
            version       = Body.current
            label         = "Didymos"       
            spiceName     = BodySpiceName "Didymos"  
            color         = C4f.Grey       
            geometryPath  = None
            radius        = 382.5 //mean radius +/- 2.5m
            textureName   = None
            defaultFrame  = FrameSpiceName "IAU_DIDYMOS" // "DIDYMOS_FIXED" ?
        }

    let dimorphos =
        {
            version       = Body.current
            label         = "Dimorphos"  
            spiceName     = BodySpiceName "Dimorphos"  
            color         = C4f.Grey       
            geometryPath  = None
            radius        = 75.5 //mean radius +/- 2.5m
            textureName   = None
            defaultFrame  = FrameSpiceName "IAU_DIMORPHOS" // DIMORPHOS_FIXED ?
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
            body        = None
        }
    let iauMars = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_MARS"
            description = Some "Mars body-fixed frame"
            spiceName   = FrameSpiceName "IAU_MARS"
            body        = Some (BodySpiceName "Mars")
        }
    let iauEarth = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_EARTH"
            description = Some "Earth body-fixed frame"
            spiceName   = FrameSpiceName "IAU_EARTH"
            body        = Some (BodySpiceName "Earth")
        }

/// sugggestion for IDs
type SampleId = SampleId of System.Guid
with 
    static member New () =
        SampleId (System.Guid.NewGuid ())
    member x.Value = 
        let (SampleId v) = x in v
    static member FromJson(_ : SampleId) = 
        json {
            let! v  = Json.read "SampleId"
            return (SampleId v)
        }
    static member ToJson (x : SampleId) =
        json {              
            do! Json.write "SampleId" x.Value
        }
module SampleId =
    let value (SampleId id) =
        id

type SpacecraftId = SpacecraftId of System.Guid
with 
    static member New () =
        SpacecraftId (System.Guid.NewGuid ())
    member x.Value = 
        let (SpacecraftId v) = x in v
    static member FromJson(_ : SpacecraftId) = 
        json {
            let! v  = Json.read "SpacecraftId"
            return (SpacecraftId v)
        }
    static member ToJson (x : SpacecraftId) =
        json {              
            do! Json.write "SpacecraftId" x.Value
        }
module SpacecraftId =
    let value (SpacecraftId id) =
        id







