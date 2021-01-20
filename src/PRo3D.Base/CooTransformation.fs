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

module CooTransformation = 

    type private Self = Self

    type SphericalCoo = {
          longitude : double
          latitude  : double
          altitude  : double
          radian    : double
        }

    let init = 0.0

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
            if File.Exists path then
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
            Log.line "Successfully initialized CooTrafo"

    let deInitCooTrafo () = 
        Log.line "[CooTransformation] shutting down..."
        CooTransformation.DeInit()
        Log.line "[CooTransformation] down."

    let getLatLonAlt (p:V3d) (planet:Planet) : SphericalCoo = 
        match planet with
        | Planet.None | Planet.JPL ->
            { latitude = nan; longitude = nan; altitude = nan; radian = 0.0 }
        | _ ->
            let lat = ref init
            let lon = ref init
            let alt = ref init
            
            let errorCode = CooTransformation.Xyz2LatLonAlt(planet.ToString(), p.X, p.Y, p.Z, lat, lon, alt)
            
            if errorCode <> 0 then
                Log.line "cootrafo errorcode %A" errorCode
            
            {
                latitude  = !lat
                longitude = !lon
                altitude  = !alt
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
            latitude  = !lat
            longitude = !lon
            altitude  = 0.0
            radian    = !rad
        }

    let getXYZFromLatLonAlt (sc:SphericalCoo) (planet:Planet) : V3d = 
        match planet with
        | Planet.None | Planet.JPL -> V3d.NaN
        | _ ->
            let pX = ref init
            let pY = ref init
            let pZ = ref init
            let error = 
                CooTransformation.LatLonAlt2Xyz(planet.ToString(), sc.latitude, sc.longitude, sc.altitude, pX, pY, pZ )
            
            if error <> 0 then
                Log.line "cootrafo errorcode %A" error
            
            V3d(!pX, !pY, !pZ)

    let getHeight (p:V3d) (up:V3d) (planet:Planet) = 
        match planet with
        | Planet.None | Planet.JPL -> (p * up).Length // p.Z //
        | _ ->
            let sc = getLatLonAlt p planet
            sc.altitude

    let getAltitude (p:V3d) (up:V3d) (planet:Planet) = 
        match planet with
        | Planet.None | Planet.JPL -> (p * up).Z // p.Z //
        | _ ->
            let sc = getLatLonAlt p planet
            sc.altitude

    let getElevation' (planet : Planet) (p:V3d) =       
        let sc = getLatLonAlt p planet
        sc.altitude

    let getUpVector (p:V3d) (planet:Planet) = 
        match planet with
        | Planet.None -> V3d.ZAxis
        | Planet.JPL -> -V3d.ZAxis
        | _ ->
            let sc = getLatLonAlt p planet
            let height = sc.altitude + 100.0
            
            let v2 = getXYZFromLatLonAlt ({sc with altitude = height}) planet
            (v2 - p).Normalized

        
       