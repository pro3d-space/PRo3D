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
    } 
with
    static member current = 0
    static member private readV0 = 
        json {
            let! label       = Json.read    "label"
            let! description = Json.tryRead "description"
            let! spiceName   = Json.read    "spiceName"
            
            return {
                version      = ReferenceFrame.current
                label        = label      
                description  = description
                spiceName    = spiceName  
                spiceNameText = spiceName.Value
                isEditing    = false
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
    trajectoryLength : float
    textureName  : option<string>
    showTrajectory : bool
    defaultFrame : option<FrameSpiceName>
} with
    static member current = 0
    static member private readV0_1 = 
        json {
            let! label        = Json.read    "label"       
            let! spiceName    = Json.read    "spiceName"   
            let! color        = Json.read    "color"       
            let! radius       = Json.read    "radius" 
            let! trajectoryLength       = Json.tryRead "trajectoryLength" 
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
                trajectoryLength = Option.defaultValue 1.0 trajectoryLength
                textureName  = textureName 
                defaultFrame = defaultFrame
                showTrajectory = Option.defaultValue false showTrajectory
            }
        }
    static member FromJson(_ : Entity) = 
        json {
            let! v = Json.read "version"
            match v with            
            // 1 was introduced but is not really necessary, to increase compatibility we collapsed
            // 0 and 1, and use the 0_1 variant which optionally reads trajectory length (which was added in 1)
            | 0 | 1 -> return! Entity.readV0_1
            | _ -> return! v |> sprintf "don't know version %A  of ReferenceFrame" |> Json.error
        }
    static member ToJson (x : Entity) =
        json {              
            do! Json.write "version"      Entity.current
            do! Json.write "label"        x.label       
            do! Json.write "spiceName"    x.spiceName   
            do! Json.write "color"        (string x.color)
            do! Json.write "radius"       x.radius   
            do! Json.write "trajectoryLength" x.trajectoryLength   
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
            radius        = 3376200.0 //polar radius in meter
            trajectoryLength = 1.0
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
            radius        = 6250.0 //polar radius in meter
            trajectoryLength = 1.0
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "IAU_DEIMOS")
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
            trajectoryLength = 1.0
            radius        = 11266.5 //polar radius in meter
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "IAU_PHOBOS")
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
            radius        = 6356800.0 // polar radius in meter
            trajectoryLength = 1.0
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
            radius        = 1736000.0 //polar radius in meter
            trajectoryLength = 1.0
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
            radius        = 382.5 //mean radius +/- 2.5m
            trajectoryLength = 1.0
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "J2000") 
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
            radius        = 75.5 //mean radius +/- 2.5m
            trajectoryLength = 1.0
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "J2000") 
            showTrajectory = false
        }
    let heraSpacecraft =
        {
            version       = Entity.current
            label         = "Hera Spacecraft"
            spiceName     = EntitySpiceName "HERA" 
            spiceNameText = "HERA"
            isEditing     = false
            draw          = false
            color         = C4f.Grey       
            radius        = 2.0 
            trajectoryLength = 1.0
            textureName   = None
            defaultFrame  = Some (FrameSpiceName "HERA_SPACECRAFT") // DIMORPHOS_FIXED ?
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
            isEditing   = false
        }
    let eclipJ2000 = 
        {
            version     = ReferenceFrame.current
            label       = "ECLIPJ2000"
            description = Some "Ecliptic coordinates based upon the J2000 frame."
            spiceName   = FrameSpiceName "ECLIPJ2000"
            spiceNameText = "ECLIPJ2000"
            isEditing   = false
        }
    let iauMars = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_MARS"
            description = Some "Mars body-fixed frame"
            spiceName   = FrameSpiceName "IAU_MARS"
            spiceNameText = "IAU_MARS"
            isEditing   = false
        }
    let iauEarth = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_EARTH"
            description = Some "Earth body-fixed frame"
            spiceName   = FrameSpiceName "IAU_EARTH"
            spiceNameText = "IAU_EARTH"
            isEditing   = false
        }
    let heraSpacecraft = 
        {
            version     = ReferenceFrame.current
            label       = "HERA_SPACECRAFT"
            description = Some "Spacecraft body-fixed frame"
            spiceName   = FrameSpiceName "HERA_SPACECRAFT"
            spiceNameText = "HERA_SPACECRAFT"
            isEditing   = false
        }
    let iauDeimos = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_DEIMOS"
            description = Some "Deimos body-fixed frame"
            spiceName   = FrameSpiceName "IAU_DEIMOS"
            spiceNameText = "IAU_DEIMOS"
            isEditing   = false
        }
    let iauPhobos = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_PHOBOS"
            description = Some "Phobos body-fixed frame"
            spiceName   = FrameSpiceName "IAU_PHOBOS"
            spiceNameText = "IAU_PHOBOS"
            isEditing   = false
        }
    let iauMoon = 
        {
            version     = ReferenceFrame.current
            label       = "IAU_MOON"
            description = Some "Moon body-fixed frame"
            spiceName   = FrameSpiceName "IAU_MOON"
            spiceNameText = "IAU_MOON"
            isEditing   = false
        }
    // The Hera mission's frame kernels (hera_v14.tf and later) never define an
    // "IAU_DIDYMOS" frame -- that name was retired from an earlier kernel
    // version. The current body-fixed frame is "DIDYMOS_FIXED" (class 5,
    // two-vector, keyed to body -658030 -- matches hera_didymos_v06.tpc's
    // BODY-658030_POLE_RA/DEC/PM). Confirmed by direct query against the
    // hera_plan kernel set: IAU_DIDYMOS -> J2000 fails with
    // SPICE(FRAMEDATANOTFOUND), DIDYMOS_FIXED -> J2000 resolves cleanly.
    let didymosFixed =
        {
            version     = ReferenceFrame.current
            label       = "DIDYMOS_FIXED"
            description = Some "Didymos body-fixed frame"
            spiceName   = FrameSpiceName "DIDYMOS_FIXED"
            spiceNameText = "DIDYMOS_FIXED"
            isEditing   = false
        }
    // Same situation as Didymos: no "IAU_DIMORPHOS" frame exists. Dimorphos has
    // no stable PCK rotation model (post-DART tumbling means ESA never defined
    // POLE/PM for body -658031); the current frame is "DIMORPHOS_FIXED" (class
    // 5, two-vector, relative to DIDYMOS_FIXED).
    let dimorphosFixed =
        {
            version     = ReferenceFrame.current
            label       = "DIMORPHOS_FIXED"
            description = Some "Dimorphos body-fixed frame"
            spiceName   = FrameSpiceName "DIMORPHOS_FIXED"
            spiceNameText = "DIMORPHOS_FIXED"
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

module SpiceName =
    /// SPICE matches body and frame names ignoring case and surrounding blanks, so
    /// "Dimorphos" in a scene and "DIMORPHOS" in a batch file are the same body.
    let same (a : string) (b : string) =
        String.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)

module CooTransformation =
    open PRo3D.Extensions
    open PRo3D.Extensions.FSharp


    let transformBody (body : EntitySpiceName) (bodyFrame : Option<FrameSpiceName>) (observer : EntitySpiceName) (observerFrame : FrameSpiceName) (time : DateTime) =
        let (EntitySpiceName body), (EntitySpiceName observer), (FrameSpiceName observerFrame) = body, observer, observerFrame
        let bodyFrame = 
            match bodyFrame with
            | Some (FrameSpiceName bodyFrame) -> bodyFrame
            | None -> observerFrame

        // A body observed from itself in its own frame sits at the origin, unrotated, at
        // every time - that is the whole placement of a single-body scene (#758). Answer it
        // without SPICE: it holds even with no ephemeris loaded, where getRelState would
        // fail and log once per surface. lookAtBody has no meaningful value here (camera
        // and target coincide); it gets a finite stand-in instead of lookAt's NaNs.
        if SpiceName.same body observer && SpiceName.same bodyFrame observerFrame then
            Some {
                lookAtBody = CameraView.lookAt V3d.OOI V3d.Zero V3d.OIO
                position = V3d.Zero
                alignBodyToObserverFrame = M33d.Identity
            }
        else

        let suportBody = "sun"
        let relState = PRo3D.SPICE.CooTransformation.getRelState body suportBody observer time observerFrame 
        let rot = PRo3D.SPICE.CooTransformation.getRotationTrafo bodyFrame observerFrame time
        let switchToLeftHanded = Trafo3d.FromBasis(V3d.IOO, -V3d.OOI, -V3d.OIO, V3d.Zero)
        let flipZ = Trafo3d.FromBasis(V3d.IOO, V3d.OIO, -V3d.OOI, V3d.Zero)
        match relState, rot with
        | Some rel, Some rot -> 
            let relFrame = rel.rot 
            let t = Trafo3d.FromBasis(relFrame.C1, relFrame.C0, relFrame.C2, rel.pos)
            let camera = CameraView.ofTrafo t.Inverse
            let camera = CameraView.lookAt rel.pos V3d.Zero V3d.OOI
            Some { 
                lookAtBody = camera
                position = rel.pos
                alignBodyToObserverFrame = M33d rot.Forward
            }
        | _ -> 
            Log.line $"[SPICE] failed to transform body (body = {body}, bodyFrame = {bodyFrame}, observer = {observer}, observerFrame = {observerFrame}, time = {time}."
            None



type SpiceReferenceSystem = { referenceFrame : FrameSpiceName; body : EntitySpiceName } 
type ObserverSystem = { referenceFrame : FrameSpiceName; body : EntitySpiceName; time : DateTime }

/// The scene body (#758): the one body a single-body scene is expressed in. The global
/// planet (`ReferenceSystem.planet`) and the GIS observation (observed body + reference
/// frame) are two views of it.
///
/// Every planet-based computation - up/north, lat/lon, bearing, MapView's pole and radius
/// - reads world coordinates as that planet's body-fixed coordinates. That holds exactly
/// when the GIS observes the body in the body's own fixed frame: the scene body's surfaces
/// then get the identity placement. A scene observed in another frame (e.g. J2000) is not
/// body-fixed; it keeps working as before, without the planet-based features, until the
/// scene frame can be chosen freely.
module SceneBody =

    /// The SPICE body and body-fixed frame of the bodies `Planet` knows. None for the
    /// frames that are no body (ENU, JPL, None). Body names are the default entities'
    /// (GisApp.initial), so they are keys of the GIS entity map.
    let trySpice (planet : PRo3D.Base.Planet) : Option<EntitySpiceName * FrameSpiceName> =
        match planet with
        | PRo3D.Base.Planet.Mars      -> Some (Entity.mars.spiceName,      ReferenceFrame.iauMars.spiceName)
        | PRo3D.Base.Planet.Earth     -> Some (Entity.earth.spiceName,     ReferenceFrame.iauEarth.spiceName)
        | PRo3D.Base.Planet.Moon      -> Some (Entity.moon.spiceName,      ReferenceFrame.iauMoon.spiceName)
        | PRo3D.Base.Planet.Phobos    -> Some (Entity.phobos.spiceName,    ReferenceFrame.iauPhobos.spiceName)
        | PRo3D.Base.Planet.Deimos    -> Some (Entity.deimos.spiceName,    ReferenceFrame.iauDeimos.spiceName)
        | PRo3D.Base.Planet.Didymos   -> Some (Entity.didymos.spiceName,   ReferenceFrame.didymosFixed.spiceName)
        | PRo3D.Base.Planet.Dimorphos -> Some (Entity.dimorphos.spiceName, ReferenceFrame.dimorphosFixed.spiceName)
        | _ -> None

    /// The Planet a SPICE body stands for, if it is one `Planet` knows.
    let tryPlanet (EntitySpiceName name) : Option<PRo3D.Base.Planet> =
        PRo3D.Base.CooTransformation.planetFromString (name.Trim())

    /// The body-fixed frame of a body `Planet` knows.
    let tryFixedFrame (body : EntitySpiceName) : Option<FrameSpiceName> =
        tryPlanet body |> Option.bind trySpice |> Option.map snd

    /// Whether `frame` is the body-fixed frame of `body`.
    let isFixedFrameOf (body : EntitySpiceName) (FrameSpiceName frame) =
        match tryFixedFrame body with
        | Some (FrameSpiceName fixedFrame) -> SpiceName.same frame fixedFrame
        | None -> false

    /// The planet of a GIS observation that is body-fixed: `observer` is a body `Planet`
    /// knows and `frame` is its fixed frame. None otherwise - no observation, a body such
    /// as a spacecraft, or a scene in another frame.
    let tryBodyFixedPlanet (observer : Option<EntitySpiceName>) (frame : Option<FrameSpiceName>) : Option<PRo3D.Base.Planet> =
        match observer, frame with
        | Some observer, Some frame when isFixedFrameOf observer frame -> tryPlanet observer
        | _ -> None

    /// The scene body as a surface reference system (body + its fixed frame), for a
    /// body-fixed observation; see `tryBodyFixedPlanet`.
    let tryReferenceSystem (observer : Option<EntitySpiceName>) (frame : Option<FrameSpiceName>) : Option<SpiceReferenceSystem> =
        match observer, frame with
        | Some body, Some referenceFrame when isFixedFrameOf body referenceFrame ->
            Some { body = body; referenceFrame = referenceFrame }
        | _ -> None