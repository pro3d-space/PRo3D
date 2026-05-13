#nowarn "9"
#nowarn "51"
namespace PRo3D.Base

open System
open Aardvark.Base
open JR
open System.IO
open System.IO.Compression

open PRo3D.Extensions

type Planet = 
| Earth = 0
| Mars  = 1
| None  = 2
| JPL   = 3
| ENU   = 4
| Moon = 5
| Phobos = 6
| Deimos = 7
| Didymos = 8
| Dimorphos = 9

module Planet =
    let inferCoordinateSystem (p : V3d) = //TODO rno
        // earth radius min max [6,357; 6,378]
        // mars equatorial radius [3396] 
        
        let earthRadiusRange = Range1d(5500000.0, 7000000.0)
        let marsRadiusRange = Range1d(2500000.0, 4000000.0)

        let distanceToOrigin = p.Length
        let coordinateSystem = 
            match distanceToOrigin with
            | d when marsRadiusRange.Contains(d) -> Planet.Mars
            | d when earthRadiusRange.Contains(d) -> Planet.Earth            
            | _ -> Planet.None

        Log.warn "[ReferenceSystem] Inferred Coordinate System: %s" (coordinateSystem.ToString ())
        coordinateSystem

    let suggestedSystem p currentSystem = 
        let inferredSystem = inferCoordinateSystem p

        match (inferredSystem, currentSystem) with
        | (Planet.Earth, Planet.Earth) -> Planet.Earth
        | (Planet.Mars, Planet.Mars)   -> Planet.Mars
        | (Planet.None, Planet.None)   -> Planet.None
        | (Planet.None, Planet.JPL)    -> Planet.JPL
        | (Planet.None, Planet.ENU)    -> Planet.ENU
        | _ ->
            Log.warn "[Scene] found reference system does not align with suggested system"
            Log.warn "[Scene] changing to %A" inferredSystem
            inferredSystem

module CooTransformation = 

    type private Self = Self

    /// Spherical / geographic coordinate carrier.
    ///
    /// The `altitude` field's meaning depends on the body's `ConventionKind`
    /// (see `getConvention` below):
    ///
    /// - Planetographic — height above the spheroid surface (metres).
    /// - Ellipsoidal    — height above the tri-axial ellipsoid surface (metres);
    ///                    reads 0 on the surface, positive above.
    /// - Spherical      — RADIAL DISTANCE from the body centre (metres),
    ///                    matching SPICE's `reclat` convention. NOT a height
    ///                    above any surface. The reference sphere radius in
    ///                    the `Spherical` payload is informative only (used
    ///                    by the GUI label) and is not in the math.
    ///
    /// `radian` is populated only by `tryGetLatLonRad` (body-agnostic polar
    /// coordinates); it is zero in every other path.
    type SphericalCoo = {
          longitude : double
          latitude  : double
          altitude  : double
          radian    : double
    } with
        member x.asV4d =
            V4d(x.longitude, x.latitude, x.altitude, x.radian)


    type SPICEKernel = { name : string; directory : string;  }

    type SPICEKernel with
        member x.FullPath = Path.Combine(x.directory, x.name)

    module SPICEKernel =
        let ofPath (path : string) =
            { name = Path.GetFileName path; directory = Path.GetDirectoryName path }
        let toPath (s : SPICEKernel) = s.FullPath

    let tryLoadKernel (kernelDirectory : string) (name : string) = 
        let currentDir = try Directory.GetCurrentDirectory() |> Some with e -> Log.warn "could not set directory, which might be needed for loading spice kernels"; None
        let __ = { new IDisposable with member x.Dispose() = match currentDir with None -> () | Some d -> try Directory.SetCurrentDirectory d with e -> Log.warn "%A" e;  }
        Directory.SetCurrentDirectory(kernelDirectory)
        let fullPath = Path.GetFullPath(Path.Combine(kernelDirectory,name))
        if File.Exists fullPath then () else failwith ("spice kernel file does not exist: " + fullPath)
        let r = CooTransformation.AddSpiceKernel(fullPath)
        if r <> 0 then
            Log.warn "could not load spice kernel: %s in %s." name kernelDirectory
            None
        else 
            Some { name = name; directory = kernelDirectory } 

    let initCooTrafo (customSpiceKernelPath : Option<string>) (appData : string) = 

        let jrDir = Path.combine [appData; "JR";]
        let cooTransformationDir = Path.combine [jrDir; "CooTransformationConfig"]
        if not (Directory.Exists cooTransformationDir) then
            Log.line "[CooTransformation] no instrument dir found, creating one"
            Directory.CreateDirectory cooTransformationDir |> ignore

        use fs = typeof<Self>.Assembly.GetManifestResourceStream("PRo3D.Base.resources.CooTransformationConfig.zip")
        use archive = new ZipArchive(fs, ZipArchiveMode.Read)
        for e in archive.Entries do
            let path = Path.combine [cooTransformationDir; e.Name]
            if File.Exists path && false then
                Log.line "[CooTransformation] Skipping installation of %s" e.Name
            else
                Log.line "[CooTransformation] installing %s" e.Name
                use s = File.OpenWrite(path)
                e.Open().CopyTo(s)

        let configDir = cooTransformationDir
        let logDir = Path.combine [jrDir; "logs"]

        if not (Directory.Exists logDir) then
            Directory.CreateDirectory logDir |> ignore

        Log.line "[CooTransformation] initializing at %s, logging to %s" configDir logDir
        let errorCode = CooTransformation.Init(true, Path.Combine(logDir, "CooTransformation.log"), 1, 2)
        if errorCode <> 0 then 
            failwithf "[CooTransformation] could not initialize library, config dir: %s, return code: %d" configDir errorCode
        else 
            Log.line "[CooTransformation] Successfully initialized CooTrafo"

      

        let spiceDirectory, spiceFileName = 
            let defaultKernel = configDir, "pck00010.tpc"
            match customSpiceKernelPath with
            | None -> defaultKernel
            | Some filePath -> 
                let fullPath = Path.GetFullPath(filePath)
                if not (File.Exists(fullPath)) then
                    defaultKernel
                else
                    Path.GetDirectoryName(fullPath), Path.GetFileName fullPath

        Log.line $"[SPICE] kernel location: {spiceDirectory}, kernel file name: {spiceFileName}."
        match tryLoadKernel spiceDirectory spiceFileName with
        | None -> 
            Log.warn "running without default spice kernel."
        | Some s -> 
            Log.line "[SPICE] loaded kernel: %s" s.FullPath

        try
            let error = JR.InstrumentPlatforms.Init(configDir,logDir)
            if error <> 0 then 
                Log.error "[InstrumentPlatforms] Instrument dll return error %d" error
            else
                Log.line "[InstrumentPlatforms] Instrument dll sucessfully initialized"
        with e -> 
            if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) ||  System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)  then
                Log.warn "Instrument platform failed to initialize - not yet supported? https://github.com/pro3d-space/PRo3D/issues/196 --> %A" e

    let deInitCooTrafo () = 
        Log.line "[CooTransformation] shutting down..."
        CooTransformation.DeInit()
        Log.line "[CooTransformation] down."

    /// Coordinate convention used per body.
    ///
    /// From ESA SPICE-team documentation and direct communication: PGRREC is
    /// only mathematically valid for spheroidal bodies (rotationally
    /// symmetric oblate) AND requires POLE/PM in the kernel pool. Some Hera
    /// bodies do not satisfy these conditions — Dimorphos is a tri-axial
    /// ellipsoid (radii (89.5, 84.5, 57.5) m) AND has no stable PCK rotation
    /// model (post-DART tumbling means ESA intentionally never defined
    /// POLE/PM for body -658031). For those bodies the appropriate
    /// convention is planetocentric (LATREC). The JR.CooTransformation
    /// native wrapper exposes PGRREC but not LATREC, so we implement the
    /// LATREC paths directly in F# below.
    ///
    /// - Planetographic: PGRREC via the JR.CooTransformation native wrapper
    ///   (Xyz2LatLonAlt / LatLonAlt2Xyz). For spheroidal bodies with POLE/PM
    ///   (Mars, Earth, Moon, Phobos, Deimos, Didymos).
    /// - Spherical: F# LATREC math against a single reference sphere of the
    ///   given mean radius. Lat/lon are exact planetocentric polar
    ///   coordinates of the input; altitude is the radial offset from the
    ///   reference sphere. Standard SPICE-community convention for small
    ///   bodies. Simple, body-shape-agnostic.
    /// - Ellipsoidal: F# math against a tri-axial reference ellipsoid (three
    ///   radii). Same (lat, lon) as Spherical (always exact), but altitude
    ///   is computed against the true ellipsoid surface via ray-intersection
    ///   from the body centre. Reads altitude ≈ 0 on the actual ellipsoid
    ///   surface, positive above. Use when altitude precision over the true
    ///   surface matters more than matching SPICE's LATREC convention.
    /// - NonPlanetary: Planet.None / .JPL / .ENU, where lat/lon do not apply.
    type ConventionKind =
        | Planetographic
        | Spherical of meanRadius:double
        | Ellipsoidal of radii:V3d
        | NonPlanetary

    /// Returns the coordinate convention for the given body. Radii are in
    /// metres (matching the native wrapper's xyz convention).
    let getConvention (planet : Planet) : ConventionKind =
        match planet with
        | Planet.Mars
        | Planet.Earth
        | Planet.Moon
        | Planet.Phobos
        | Planet.Deimos
        | Planet.Didymos    -> Planetographic
        // Dimorphos: tri-axial, no PCK rotation pole. Default to Spherical
        // (LATREC convention) matching ESA's SPICE-team guidance.
        // Switch to Ellipsoidal (V3d(89.5, 84.5, 57.5)) here if altitude
        // referenced to the true tri-axial surface is preferred.
        | Planet.Dimorphos  -> Spherical 77.166666666666667     // (89.5 + 84.5 + 57.5) / 3
        | Planet.None
        | Planet.JPL
        | Planet.ENU        -> NonPlanetary
        | _                 -> NonPlanetary

    /// Bodies where typical OPC data spans most/all of the body, so camera
    /// setup should use a body-independent sky (world Z) rather than the
    /// reference-system's radial up. Avoids gimbal-lock when the camera
    /// viewing direction runs near-parallel to the radial.
    let isSmallBody (planet : Planet) =
        match planet with
        | Planet.Phobos | Planet.Deimos | Planet.Didymos | Planet.Dimorphos -> true
        | _ -> false

    // F# LATREC: planetocentric polar coordinates of `p`.
    //
    // Matches SPICE's `reclat`: (latitude, longitude, radial-distance). The
    // SphericalCoo.altitude field stores |p| — i.e. the radial distance from
    // the body centre, NOT a height above any reference surface. The
    // `_meanRadius` parameter is informative only (kept on the `Spherical`
    // ConventionKind payload for the GUI label) and does not enter the math.
    let private latLonAltOnSphere (_meanRadius : double) (p : V3d) : SphericalCoo option =
        let r = p.Length
        if r = 0.0 then None
        else
            let n = p / r
            Some {
                latitude  = (asin n.Z)      * Constant.DegreesPerRadian
                longitude = (atan2 n.Y n.X) * Constant.DegreesPerRadian
                altitude  = r
                radian    = 0.0
            }

    let private xyzFromLatLonAltOnSphere (_meanRadius : double) (sc : SphericalCoo) : V3d =
        let latR = sc.latitude  * Constant.RadiansPerDegree
        let lonR = sc.longitude * Constant.RadiansPerDegree
        let cosLat = cos latR
        let r = sc.altitude
        V3d(r * cosLat * cos lonR, r * cosLat * sin lonR, r * sin latR)

    // Tri-axial ellipsoidal variant: same lat/lon as the spherical path
    // (planetocentric polar coordinates), but altitude is referenced to the
    // true ellipsoid surface via ray-intersection from the origin:
    //   surface point at  t·p̂  with  (t·n̂.X)²/a² + (t·n̂.Y)²/b² + (t·n̂.Z)²/c² = 1
    //   →  t_surface = 1 / sqrt( (n̂.X/a)² + (n̂.Y/b)² + (n̂.Z/c)² )
    let private latLonAltOnEllipsoid (radii : V3d) (p : V3d) : SphericalCoo option =
        let r = p.Length
        if r = 0.0 then None
        else
            let n = p / r
            let a, b, c = radii.X, radii.Y, radii.Z
            let kx, ky, kz = n.X / a, n.Y / b, n.Z / c
            let rSurface = 1.0 / sqrt (kx * kx + ky * ky + kz * kz)
            Some {
                latitude  = (asin n.Z)      * Constant.DegreesPerRadian
                longitude = (atan2 n.Y n.X) * Constant.DegreesPerRadian
                altitude  = r - rSurface
                radian    = 0.0
            }

    let private xyzFromLatLonAltOnEllipsoid (radii : V3d) (sc : SphericalCoo) : V3d =
        let latR = sc.latitude  * Constant.RadiansPerDegree
        let lonR = sc.longitude * Constant.RadiansPerDegree
        let cosLat = cos latR
        let dir = V3d(cosLat * cos lonR, cosLat * sin lonR, sin latR)
        let a, b, c = radii.X, radii.Y, radii.Z
        let kx, ky, kz = dir.X / a, dir.Y / b, dir.Z / c
        let rSurface = 1.0 / sqrt (kx * kx + ky * ky + kz * kz)
        dir * (rSurface + sc.altitude)

    // Native PGRREC paths. Out-params are nan-seeded so that a misbehaving
    // wrapper that does not write on failure cannot produce silent zeros.
    let private tryPgrXyz2LatLonAlt (planetName : string) (p : V3d) : SphericalCoo option =
        let mutable lat = nan
        let mutable lon = nan
        let mutable alt = nan
        let errorCode = CooTransformation.Xyz2LatLonAlt(planetName, p.X, p.Y, p.Z, &lat, &lon, &alt)
        if errorCode <> 0 then None
        else Some { latitude = lat; longitude = lon; altitude = alt; radian = 0.0 }

    let private tryPgrLatLonAlt2Xyz (planetName : string) (sc : SphericalCoo) : V3d option =
        let mutable pX = nan
        let mutable pY = nan
        let mutable pZ = nan
        let errorCode =
            CooTransformation.LatLonAlt2Xyz(planetName, sc.latitude, sc.longitude, sc.altitude, &pX, &pY, &pZ)
        if errorCode <> 0 then None
        else Some (V3d(pX, pY, pZ))

    /// xyz → lat/lon/alt, picking the right convention for the body.
    /// Returns None for bodies the wrapper cannot handle (NonPlanetary, or
    /// Planetographic bodies whose native call fails).
    let tryGetLatLonAlt (planet : Planet) (p : V3d) : SphericalCoo option =
        match getConvention planet with
        | NonPlanetary       -> None
        | Spherical r        -> latLonAltOnSphere r p
        | Ellipsoidal radii  -> latLonAltOnEllipsoid radii p
        | Planetographic     -> tryPgrXyz2LatLonAlt (planet.ToString()) p

    /// Direct PGRREC by SPICE body name; no convention dispatch.
    /// Use when you have a raw SPICE name and explicitly want the
    /// planetographic transform. For bodies that have a Planet enum value,
    /// prefer `tryGetLatLonAlt` so the right convention is applied.
    let tryGetLatLonAltPlanet (planetName : string) (p : V3d) : SphericalCoo option =
        tryPgrXyz2LatLonAlt planetName p

    /// Body-agnostic spherical (lat, lon, radial distance) of `p`.
    let tryGetLatLonRad (p : V3d) : SphericalCoo option =
        let mutable lat = nan
        let mutable lon = nan
        let mutable rad = nan
        let errorCode = CooTransformation.Xyz2LatLonRad(p.X, p.Y, p.Z, &&lat, &&lon, &&rad)
        if errorCode <> 0 then None
        else Some { latitude = lat; longitude = lon; altitude = 0.0; radian = rad }

    /// lat/lon/alt → xyz, picking the right convention for the body.
    let tryGetXYZFromLatLonAlt (sc : SphericalCoo) (planet : Planet) : V3d option =
        match getConvention planet with
        | NonPlanetary       -> None
        | Spherical r        -> Some (xyzFromLatLonAltOnSphere r sc)
        | Ellipsoidal radii  -> Some (xyzFromLatLonAltOnEllipsoid radii sc)
        | Planetographic     -> tryPgrLatLonAlt2Xyz (planet.ToString()) sc

    let tryGetXYZFromLatLonAltPlanet (sc : SphericalCoo) (planetName : string) : V3d option =
        tryPgrLatLonAlt2Xyz planetName sc

    /// Convenience overload: V3d holding (lat, lon, alt) instead of a SphericalCoo.
    let tryGetXYZFromLatLonAlt' (coordinate : V3d) (planet : Planet) : V3d option =
        let sc = { latitude = coordinate.X; longitude = coordinate.Y; altitude = coordinate.Z; radian = 0.0 }
        tryGetXYZFromLatLonAlt sc planet

    let tryGetHeight (p : V3d) (up : V3d) (planet : Planet) : double option =
        match planet with
        | Planet.None | Planet.JPL | Planet.ENU -> Some (p * up).Length
        | _ -> tryGetLatLonAlt planet p |> Option.map (fun sc -> sc.altitude)

    let tryGetAltitude (p : V3d) (up : V3d) (planet : Planet) : double option =
        match planet with
        | Planet.None | Planet.JPL | Planet.ENU -> Some (p * up).Z
        | _ -> tryGetLatLonAlt planet p |> Option.map (fun sc -> sc.altitude)

    let tryGetElevation (planet : Planet) (p : V3d) : double option =
        tryGetLatLonAlt planet p |> Option.map (fun sc -> sc.altitude)

    /// Body-relative "up" direction at point `p`. Total: when SPICE refuses
    /// (Dimorphos's planetocentric path always succeeds; PGRREC bodies may
    /// fail) the function falls back to `p.Normalized` (radial-from-centre).
    /// This is geometrically sound regardless of frame and avoids the
    /// previous failure-as-(-p) accident.
    let getUpVector (p : V3d) (planet : Planet) : V3d =
        let radial () =
            if p.LengthSquared > 0.0 then p.Normalized else V3d.ZAxis
        match planet with
        | Planet.None ->  V3d.ZAxis
        | Planet.JPL  -> -V3d.ZAxis
        | Planet.ENU  ->  V3d.ZAxis
        | _ ->
            match tryGetLatLonAlt planet p with
            | None -> radial ()
            | Some sc ->
                // +100 m altitude trick: the radial offset of two surface
                // points differing only in altitude gives the local up.
                match tryGetXYZFromLatLonAlt { sc with altitude = sc.altitude + 100.0 } planet with
                | Some v2 -> (v2 - p).Normalized
                | None    -> radial ()


    let planetFromString (spiceBodyName : string) = 
        match spiceBodyName.ToLower() with
        | "earth" -> Some Planet.Earth
        | "mars"  -> Some Planet.Mars
        | "moon"  -> Some Planet.Moon
        | "phobos" -> Some Planet.Phobos
        | "deimos" -> Some Planet.Deimos
        | "didymos" -> Some Planet.Didymos
        | "dimorphos" -> Some Planet.Dimorphos
        | _       -> None

    module SphericalCoo =
        let toV3d (spherical : SphericalCoo) =
            V3d(spherical.latitude, spherical.longitude, spherical.altitude)
            
        
       