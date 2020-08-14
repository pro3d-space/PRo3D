namespace PRo3D.Base

open Aardvark.Base
open IPWrappers

type Planet = 
  | Earth = 0
  | Mars  = 1
  | None  = 2

module CooTransformation = 

    type SphericalCoo = {
          longitude : double
          latitude  : double
          altitude  : double
          radian    : double
        }

    let init = 0.0

    let initCooTrafo () = 
        let directory = @".\InstrumentStuff" 
        let test = @".\bin" 
        let t = System.IO.Path.GetFullPath(test)
        let errorCode = CooTrafo.Init(test, directory)
        printfn "%A" errorCode

    let deInitCooTrafo () = 
        CooTrafo.DeInit()

    let getLatLonAlt (p:V3d) (planet:Planet) : SphericalCoo = 
      match planet with
      | Planet.None ->
        { latitude = nan; longitude = nan; altitude = nan; radian = 0.0 }
      | _ ->
        let lat = ref init
        let lon = ref init
        let alt = ref init
      
        let bla = CooTrafo.Xyz2LatLonAlt(planet.ToString(), p.X, p.Y, p.Z, lat, lon, alt)
        
        if bla <> 0 then
            Log.line "cootrafo errorcode %A" bla

        {
            latitude = !lat
            longitude = !lon
            altitude = !alt
            radian = 0.0;
            }

    let getLatLonRad (p:V3d) : SphericalCoo = 
        let lat = ref init
        let lon = ref init
        let rad = ref init
        let bla = CooTrafo.Xyz2LatLonRad( p.X, p.Y, p.Z, lat, lon, rad)
        
        if bla <> 0 then
            Log.line "cootrafo errorcode %A" bla

        {
            latitude = !lat
            longitude = !lon
            altitude = 0.0
            radian = !rad
            }

    let getXYZFromLatLonAlt (sc:SphericalCoo) (planet:Planet) : V3d = 
      match planet with
      | Planet.None -> V3d.NaN
      | _ ->
        let pX = ref init
        let pY = ref init
        let pZ = ref init
        let error = CooTrafo.LatLonAlt2Xyz(planet.ToString(), sc.latitude, sc.longitude, sc.altitude, pX, pY, pZ )

        if error <> 0 then
            Log.line "cootrafo errorcode %A" error

        V3d(!pX, !pY, !pZ)

    let getHeight (p:V3d) (up:V3d) (planet:Planet) = 
      match planet with
      | Planet.None -> (p * up).Length // p.Z //
      | _ ->
        let sc = getLatLonAlt p planet
        sc.altitude

    let getAltitude (p:V3d) (up:V3d) (planet:Planet) = 
      match planet with
      | Planet.None -> (p * up).Z // p.Z //
      | _ ->
        let sc = getLatLonAlt p planet
        sc.altitude

    let getElevation' (planet : Planet) (p:V3d) =       
        let sc = getLatLonAlt p planet
        sc.altitude

    let getUpVector (p:V3d) (planet:Planet) = 
      match planet with
      | Planet.None -> V3d.ZAxis
      | _ ->
        let sc = getLatLonAlt p planet
        let height = sc.altitude + 100.0
        
        let v2 = getXYZFromLatLonAlt ({sc with altitude = height}) planet
        (v2 - p).Normalized

        
       