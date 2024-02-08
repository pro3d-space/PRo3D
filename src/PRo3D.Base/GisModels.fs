namespace PRo3D.Base

open Chiron
open Aardvark.Base

module GisModels =

    type SpiceName = SpiceName of string
    with 
        member x.Value = 
            let (SpiceName v) = x in v
        static member FromJson(_ : SpiceName) = 
            json {
                let! v  = Json.read "SpiceName"
                return (SpiceName v)
            }
        static member ToJson (x : SpiceName) =
            json {              
                do! Json.write "SpiceName" x.Value
            }

    module SpiceName =
        let value (SpiceName spiceName) =
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
            spiceName   : SpiceName
            body        : option<SpiceName>
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
        spiceName    : SpiceName
        color        : C4f
        radius       : float
        geometryPath : option<string>
        textureName  : option<string>
        defaultFrame : SpiceName
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
                do! Json.write "spiceName"    x.spiceName   
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
                spiceName     = SpiceName "Mars"    
                color         = C4f.Red       
                geometryPath  = None
                radius        = 3376200.0 //polar radius in meter
                textureName   = None
                defaultFrame  = SpiceName "IAU_MARS"
            }

        let earth =
            {
                version       = Body.current
                label         = "Earth"        
                spiceName     = SpiceName "Earth"    
                color         = C4f.Blue       
                geometryPath  = None
                radius        = 6356800.0 // polar radius in meter
                textureName   = None
                defaultFrame  = SpiceName "IAU_EARTH"
            }

        let moon =
            {
                version       = Body.current
                label         = "Moon"        
                spiceName     = SpiceName "Moon"    
                color         = C4f.Silver       
                geometryPath  = None
                radius        = 1736000.0 //polar radius in meter
                textureName   = None
                defaultFrame  = SpiceName "IAU_MOON" // should maybe used different default?
            }

        let didymos =
            {
                version       = Body.current
                label         = "Didymos"       
                spiceName     = SpiceName "Didymos"  
                color         = C4f.Grey       
                geometryPath  = None
                radius        = 382.5 //mean radius +/- 2.5m
                textureName   = None
                defaultFrame  = SpiceName "IAU_DIDYMOS" // "DIDYMOS_FIXED" ?
            }

        let dimorphos =
            {
                version       = Body.current
                label         = "Dimorphos"  
                spiceName     = SpiceName "Dimorphos"  
                color         = C4f.Grey       
                geometryPath  = None
                radius        = 75.5 //mean radius +/- 2.5m
                textureName   = None
                defaultFrame  = SpiceName "IAU_DIMORPHOS" // DIMORPHOS_FIXED ?
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
                spiceName   = SpiceName "J2000"
                body        = None
            }
        let iauMars = 
            {
                version     = ReferenceFrame.current
                label       = "IAU_MARS"
                description = Some "Mars body-fixed frame"
                spiceName   = SpiceName "IAU_MARS"
                body        = Some (SpiceName "Mars")
            }
        let iauEarth = 
            {
                version     = ReferenceFrame.current
                label       = "IAU_EARTH"
                description = Some "Earth body-fixed frame"
                spiceName   = SpiceName "IAU_EARTH"
                body        = Some (SpiceName "Earth")
            }


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

    type Spacecraft =
        {
            label          : string
            spiceName      : SpiceName
            referenceFrame : SpiceName
        }

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]    
    module Spacecraft =   
        let heraSpacecraft =
            {
                label = "Hera Spacecraft"
                spiceName = SpiceName "HERA_SPACECRAFT" // ?? Need to check!
                referenceFrame = SpiceName "HERA_SPACECRAFT" // ?? Need to check!
            }
