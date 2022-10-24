namespace PRo3D.Base

open System
open Aardvark.Base
open JR
open System.IO
open System.IO.Compression

type Planet = 
| Earth = 0
| Mars  = 1
| None  = 2
| JPL   = 3
| ENU   = 4

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

    type SphericalCoo = {
          longitude : double
          latitude  : double
          altitude  : double
          radian    : double
    } with
        member x.asV4d =
            V4d(x.longitude, x.latitude, x.altitude, x.radian)


    let initCooTrafo (appData : string) = 

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
        let errorCode = CooTransformation.Init(configDir, logDir)
        if errorCode <> 0 then 
            failwithf "[CooTransformation] could not initialize library, config dir: %s, return code: %d" configDir errorCode
        else 
            Log.line "[CooTransformation] Successfully initialized CooTrafo"
        
        try
            let error = JR.InstrumentPlatforms.Init(configDir,logDir)
            if error <> 0 then 
                Log.error "[InstrumentPlatforms] Instrument dll return error %d" error
            else
                Log.line "[InstrumentPlatforms] Instrument dll sucessfully initialized"
        with e -> 
            if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) then
                Log.warn "Instrument platform failed to initialize - not yet supported? https://github.com/pro3d-space/PRo3D/issues/196 --> %A" e

    let deInitCooTrafo () = 
        Log.line "[CooTransformation] shutting down..."
        CooTransformation.DeInit()
        Log.line "[CooTransformation] down."

    let private init = 0.0

    let getLatLonAlt (planet:Planet) (p:V3d) : SphericalCoo = 
        match planet with
        | Planet.None | Planet.JPL | Planet.ENU ->
            { latitude = nan; longitude = nan; altitude = nan; radian = 0.0 }
        | _ ->
            let lat = ref init
            let lon = ref init
            let alt = ref init
            
            let errorCode = CooTransformation.Xyz2LatLonAlt(planet.ToString(), p.X, p.Y, p.Z, lat, lon, alt)
            
            if errorCode <> 0 then
                Log.line "cootrafo errorcode %A" errorCode
            
            {
                latitude  = lat.Value
                longitude = lon.Value
                altitude  = alt.Value
                radian    = 0.0
            }

    let getLatLonRad (p:V3d) : SphericalCoo = 
        let lat = ref init
        let lon = ref init
        let rad = ref init
        let errorCode = CooTransformation.Xyz2LatLonRad( p.X, p.Y, p.Z, lat, lon, rad)
        
        if errorCode <> 0 then
            Log.line "cootrafo errorcode %A" errorCode

        {
            latitude  = lat.Value
            longitude = lon.Value
            altitude  = 0.0
            radian    = rad.Value
        }

    let getXYZFromLatLonAlt (sc:SphericalCoo) (planet:Planet) : V3d = 
        match planet with
        | Planet.None | Planet.JPL | Planet.ENU -> V3d.NaN
        | _ ->
            let pX = ref init
            let pY = ref init
            let pZ = ref init
            let error = 
                CooTransformation.LatLonAlt2Xyz(planet.ToString(), sc.latitude, sc.longitude, sc.altitude, pX, pY, pZ )
            
            if error <> 0 then
                Log.line "cootrafo errorcode %A" error
            
            V3d(pX.Value, pY.Value, pZ.Value)

    let getXYZFromLatLonAlt' (coordinate :V3d) (planet:Planet) : V3d = 
        match planet with
        | Planet.None | Planet.JPL | Planet.ENU -> V3d.NaN
        | _ ->
            let pX = ref init
            let pY = ref init
            let pZ = ref init
            let error = 
                CooTransformation.LatLonAlt2Xyz(planet.ToString(), coordinate.X, coordinate.Y, coordinate.Z, pX, pY, pZ )
            
            if error <> 0 then
                Log.line "cootrafo errorcode %A" error
            
            V3d(pX.Value, pY.Value, pZ.Value)

    let getHeight (p:V3d) (up:V3d) (planet:Planet) = 
        match planet with
        | Planet.None | Planet.JPL | Planet.ENU -> (p * up).Length // p.Z //
        | _ ->
            let sc = getLatLonAlt planet p
            sc.altitude

    let getAltitude (p:V3d) (up:V3d) (planet:Planet) = 
        match planet with
        | Planet.None | Planet.JPL | Planet.ENU -> (p * up).Z // p.Z //
        | _ ->
            let sc = getLatLonAlt planet p
            sc.altitude

    let getElevation' (planet : Planet) (p:V3d) =       
        let sc = getLatLonAlt planet p
        sc.altitude

    let getUpVector (p:V3d) (planet:Planet) = 
        match planet with
        | Planet.None ->  V3d.ZAxis
        | Planet.JPL  -> -V3d.ZAxis
        | Planet.ENU  ->  V3d.ZAxis
        | _ ->
            let sc = getLatLonAlt planet p
            let height = sc.altitude + 100.0
            
            let v2 = getXYZFromLatLonAlt ({sc with altitude = height}) planet
            (v2 - p).Normalized

    module SphericalCoo =
        let toV3d (spherical : SphericalCoo) =
            V3d(spherical.latitude, spherical.longitude, spherical.altitude)
            
        
       