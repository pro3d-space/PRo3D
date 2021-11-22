namespace PRo3D.Viewer

open Aardvark.Base
open Adaptify
open Chiron
open PRo3D.Base

type TraverseAction =
| LoadTraverse of string
| SelectSol    of int
| FlyToSol     of V3d * V3d * V3d //forward * sky * location
| ToggleShowText
| ToggleShowLines
| ToggleShowDots

[<ModelType>]
type Sol =
    {
        version        : int
        location       : V3d
        solNumber      : int
        site           : int
        yaw            : float
        pitch          : float
        roll           : float
        tilt           : float
        note           : string
        distanceM      : float
        totalDistanceM : float
    }

module Sol = 
    let current = 0 
    let initial = 
        { 
            version        = current
            location       = V3d.NaN
            solNumber      = -1
            site           = -1
            yaw            = nan
            pitch          = nan
            roll           = nan
            tilt           = nan
            note           = ""
            distanceM      = nan
            totalDistanceM = nan
        }

    let readV0 = 
        json {                           

            let! location       = Json.read "location"
            let! solNumber      = Json.read "solNumber"
            let! site           = Json.read "site"
            let! yaw            = Json.read "yaw"
            let! pitch          = Json.read "pitch"
            let! roll           = Json.read "roll"
            let! tilt           = Json.read "tilt"
            let! note           = Json.read "note"
            let! distanceM      = Json.read "distanceM"
            let! totalDistanceM = Json.read "totalDistanceM"

            return {
                version        = current
                location       = location |> V3d.Parse
                solNumber      = solNumber
                site           = site          
                yaw            = yaw           
                pitch          = pitch         
                roll           = roll          
                tilt           = tilt          
                note           = note          
                distanceM      = distanceM     
                totalDistanceM = totalDistanceM            
            }            
        }    

type Sol with
    static member FromJson(_:Sol) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Sol.readV0
            | _ -> return! v |> sprintf "don't know version %d of Traverse" |> Json.error
        }
    static member ToJson (x : Sol) =
        json {
            do! Json.write "version"        Sol.current            
            do! Json.write "location"       (x.location |> string)
            do! Json.write "solNumber"      x.solNumber
            do! Json.write "site"           x.site          
            do! Json.write "yaw"            x.yaw           
            do! Json.write "pitch"          x.pitch         
            do! Json.write "roll"           x.roll          
            do! Json.write "tilt"           x.tilt          
            do! Json.write "note"           x.note          
            do! Json.write "distanceM"      x.distanceM     
            do! Json.write "totalDistanceM" x.totalDistanceM
        }

[<ModelType>]
type Traverse = 
    {
        version     : int
        sols        : List<Sol>
        selectedSol : option<int>
        showLines   : bool
        showText    : bool
        showDots    : bool
    }

module Traverse =
    let colors = [
        C4b(166,206,227)
        C4b(31,120,180)
        C4b(178,223,138)
        C4b(51,160,44)
        C4b(251,154,153)
        C4b(227,26,28)
        C4b(253,191,111)
        C4b(255,127,0)
        C4b(202,178,214)
        C4b(106,61,154)
        C4b(255,255,153)
        C4b(177,89,40)
    ]

    let current = 0 
    let initial = 
        { 
            version     = current
            sols        = [] 
            selectedSol = None
            showLines = true
            showText  = true
            showDots  = true
        }

    let readV0 = 
        json {               
            let! sols       = Json.read "sols"
            let! showLines  = Json.read "showLines"
            let! showText   = Json.read "showText"
            let! showDots   = Json.read "showDots"

            return 
                {
                    version     = current
                    sols        = sols
                    selectedSol = None
                    showLines   = showLines
                    showText    = showText 
                    showDots    = showDots 
                }
        }    

type Traverse with
    static member FromJson(_:Traverse) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Traverse.readV0
            | _ -> return! v |> sprintf "don't know version %d of Traverse" |> Json.error
        }
    static member ToJson (x : Traverse) =
           json {
               do! Json.write "version"   x.version
               do! Json.write "sols"      x.sols
               do! Json.write "showLines" x.showLines
               do! Json.write "showText"  x.showText
               do! Json.write "showDots"  x.showDots
           }