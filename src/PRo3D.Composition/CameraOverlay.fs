namespace PRo3D.Composition

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

/// The camera readout overlaid on the top left of the 3D view: frame, bearing, pitch,
/// position, latitude/longitude/altitude and the coordinate convention. Moved from the
/// Viewer's GUI so PRo3D.Lite shows the same overlay.
module CameraOverlay =

    let pitchAndBearing (r:AdaptiveReferenceSystem) (view:aval<CameraView>) =
        adaptive {
          let! up    = r.up.value
          let! north = r.northO//r.north.value   
          let! v     = view
        
          return (Calculations.pitch up v.Forward, Calculations.bearing up north v.Forward)
        }

    let view (m : AdaptiveReferenceSystem) (cv : aval<CameraView>) = 
        div [js "oncontextmenu" "event.preventDefault();"] [ 
            let planet = 
                m.planet 
                |> AVal.map(fun x -> 
                    match x with
                    | Planet.Mars  -> "Mars (IAU ellipsoid)"
                    | Planet.Earth -> "Earth (ellipsoid)"
                    | Planet.JPL   -> "JPL Rover Frame"
                    | Planet.None  -> "None xyz"          
                    | Planet.ENU   -> "ENU"
                    | Planet.Moon  -> "Moon"
                    | Planet.Deimos -> "Deimos"
                    | Planet.Phobos -> "Phobos"
                    | Planet.Dimorphos -> "Dimorphos"
                    | Planet.Didymos -> "Didymos"
                    | _ -> "[TextOverlays] missing text representation for selected planet."
                )  
            
            let pnb = pitchAndBearing m cv

            // Bearing/pitch suppressed on small bodies. The current math
            // (AnnotationHelpers.bearing/pitch + ReferenceSystem.northVector)
            // assumes world +Z is the body's north pole and computes pitch
            // against a global plane through origin -- both wrong for small
            // irregular bodies like Dimorphos (north pole is -Z in SHM, and
            // the camera sits a body-radius away from origin). Better to show
            // nothing than nonsense. See TODOS.md "small-body bearing/pitch
            // overlay" before re-enabling.
            let pitch =
                AVal.map2 (fun (p,_) planet ->
                    if CooTransformation.isSmallBody planet then "n/a"
                    else sprintf "%s deg" ((p : float).ToString("0.00"))) pnb m.planet
            let bearing =
                AVal.map2 (fun (_,b) planet ->
                    if CooTransformation.isSmallBody planet then "n/a"
                    else sprintf "%s deg" ((b : float).ToString("0.00"))) pnb m.planet
            
            let position = cv |> AVal.map(fun x -> x.Location.ToString("0.00"))
            
            let spericalc =
                AVal.map2 (fun (a : CameraView) b ->
                    CooTransformation.tryGetLatLonAlt b a.Location
                ) cv m.planet

            let altitude =
                AVal.map2 (fun (a : CameraView) b ->
                    CooTransformation.tryGetAltitude a.Location a.Up b) cv m.planet

            let formatCoo (project : CooTransformation.SphericalCoo -> string) =
                spericalc |> AVal.map (function
                    | Some sc -> project sc
                    | None    -> "conversion failed (set planet)")

            let lon = formatCoo (fun x -> sprintf "%s deg" ((360.0 - x.longitude).ToString()))
            let lat = formatCoo (fun x -> sprintf "%s deg" (x.latitude.ToString()))

            let alt2 =
                altitude |> AVal.map (function
                    | Some v -> sprintf "%s m" (v.ToString("0.00"))
                    | None   -> "conversion failed (set planet)")

            let conventionLabel =
                m.planet |> AVal.map (fun p ->
                    match CooTransformation.getConvention p with
                    | CooTransformation.Planetographic    -> "planetographic"
                    | CooTransformation.Spherical r       -> sprintf "spherical r=%.1fm" r
                    | CooTransformation.Ellipsoidal _     -> "ellipsoidal"
                    | CooTransformation.NonPlanetary      -> "n/a")
                                                   
            let style' = "color: white; font-family: Roboto Mono"
            
            yield div [
                clazz "ui"; 
                style "position: absolute; top: 15px; left: 15px; float:left; pointer-events:None" 
                ] [                
                yield table [] [
                    tr [] [
                        td [style style'] [Incremental.text planet]
                    ]
                    tr [] [
                        td [style style'] [text "Bearing: "]
                        td [style style'] [Incremental.text bearing]
                    ]
                    tr [] [
                        td [style style'] [text "Pitch: "]
                        td [style style'] [Incremental.text pitch]
                    ]
                    tr [] [
                        td [style style'] [text "Position: "]
                        td [style style'] [Incremental.text position]
                    ]
                    tr [] [
                        td [style style'] [text "Latitude: "]
                        td [style style'] [Incremental.text lat]
                    ]
                    tr [] [
                        td [style style'] [text "Longitude: "]
                        td [style style'] [Incremental.text lon]
                    ]
                    //tr[][
                    //    td[style style'][text "Altitude: "]
                    //    td[style style'][Incremental.text alt]
                    //]
                    tr [] [
                        td [style style'] [text "Altitude: "]
                        td [style style'] [Incremental.text alt2]
                    ]
                    tr [] [
                        td [style style'] [text "Convention: "]
                        td [style style'] [Incremental.text conventionLabel]
                    ]
                ]
            ]
        ]
