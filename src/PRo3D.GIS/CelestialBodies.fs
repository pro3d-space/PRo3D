namespace PRo3D.SPICE

open System
open System.IO
open FSharp.Data.Adaptive
open Aardvark.Base

[<AutoOpen>]
module CelestialBodies =

    [<Measure>] type m
    [<Measure>] type km
    [<Measure>] type s
    [<Measure>] type mm
    let meterToKilometers (m : float<m>) = m / 1000.0<m / km> 
    let kmToMeters (m : float<km>) = m * 1000.0<m / km> 
    let mmToMeters (m : float<mm>) = m / 1000.0<m / mm>

    type BodyDesc = 
        {
            // body name in spice nomenclature
            name: string
            // visual appearance ;)
            color : C4f
            // diameter
            diameter : float<km>
            // good observer (when setting the camera the body, another body which can be used to look at the body.
            goodObserver : string

            diffuseMap  : Option<string>
            normalMap   : Option<string>
            specularMap : Option<string>

            referenceFrame : Option<string>
        }

    let bodySources = 
        let getTexturePath (name : string) = 
            let path = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "resources")
            let path = @"C:\Users\haral\Desktop\pro3d\PRo3D.SPICE\resources"
            Path.Combine(path, name) |> Some

        [|  
            { name = "sun"        ; color = C4f.White;     diameter = 1391016.0<km>;  goodObserver = "mercury"; diffuseMap = None; normalMap = None; specularMap = None; referenceFrame = None }
            { name = "mercury"    ; color = C4f.Gray;      diameter = 4879.4<km>;     goodObserver = "earth"  ; diffuseMap = None; normalMap = None; specularMap = None; referenceFrame = None }
            { name = "venus"      ; color = C4f.AliceBlue; diameter = 12104.0<km>;    goodObserver = "earth"  ; diffuseMap = None; normalMap = None; specularMap = None; referenceFrame = None }
            { name = "earth"      ; color = C4f.Blue;      diameter = 12742.0<km>;    goodObserver = "moon"   ; 
                    diffuseMap = getTexturePath "MODIS_Map.jpg"; 
                    normalMap = getTexturePath "NormalMap2.png"; 
                    specularMap = getTexturePath "EarthSpec.png";
                    referenceFrame = Some "IAU_EARTH"
            }
            { name = "moon"       ; color = C4f.DarkGray;  diameter = 3474.8<km>;     goodObserver = "earth"  ; diffuseMap = None; normalMap = None; specularMap = None; referenceFrame = None }
            { name = "mars"       ; color = C4f.Red;       diameter = 6779.0<km>;     goodObserver = "phobos" ; 
                    //diffuseMap = getTexturePath "OIP.jpg";
                    diffuseMap = getTexturePath "mar0kuu2.jpg"; 
                    normalMap = None; specularMap = None; 
                    referenceFrame = Some "IAU_MARS" }
            { name = "phobos"     ; color = C4f.Red;       diameter = 22.4<km>;       goodObserver = "mars"   ; 
                    diffuseMap = None; normalMap = None; specularMap = None; 
                    referenceFrame = Some "IAU_PHOBOS" }
            { name = "deimos"     ; color = C4f.Red;       diameter = 12.4<km>;       goodObserver = "mars"   ; diffuseMap = None; normalMap = None; specularMap = None; referenceFrame = None }
            { name = "HERA"       ; color = C4f.Magenta;   diameter = 0.00001<km>;    goodObserver = "mars"   ; diffuseMap = None; normalMap = None; specularMap = None; referenceFrame = None }
        |]   


    let getBodySource (name : string) = bodySources |> Array.tryFind (fun s -> s.name.ToLower() = name.ToLower())

    let distanceSunPluto = 5906380000.0 * 1000.0

    let defaultSupportBodyWhenIrrelevant = "SUN"



    let orbitLength =
        Map.ofList [ 
            "mercury", 87.969
            "venus", 224.701
            "earth", 365.256
            "mars", 686.971
            "jupiter", 4332.59
            "saturn", 10759.22
            "uranus", 30688.5
            "neptune", 60182.0
            "phobos", 0.31891
            "deimos", 1.263
            "hera", 300.0
        ]

    let getOrbitLength (bodyName : string) =
        match Map.tryFind (bodyName.ToLower()) orbitLength with
        | Some lengthInDays -> TimeSpan.FromDays(lengthInDays)
        | None -> TimeSpan.FromDays(12)