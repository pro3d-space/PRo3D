#nowarn "0686"
namespace PRo3D.Core

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Core

open Adaptify
open Chiron

type TraversePropertiesAction =
    | ToggleShowText
    | ToggleShowLines
    | ToggleShowDots
    | SetTraverseName of string
    | SetSolTextsize of Numeric.Action
    | SetTraverseColor of ColorPicker.Action

type TraverseAction =
    | SelectSol of int
    | FlyToSol of V3d * V3d * V3d //forward * sky * location
    | PlaceRoverAtSol of string * Trafo3d * V3d * ReferenceSystem //rotation and location
    | LoadTraverses of list<string>
    | FlyToTraverse of Guid
    | RemoveTraverse of Guid
    | IsVisibleT of Guid
    | SelectTraverse of Guid
    | TraversePropertiesMessage of TraversePropertiesAction
    | RemoveAllTraverses

module InitTraverseParams =

    let tText =
        { value = 0.05
          min = 0.001
          max = 5.0
          step = 0.001
          format = "{0:0.000}" }


type Sol =
    { version: int
      location: V3d
      solNumber: int
      site: int
      yaw: float
      pitch: float
      roll: float
      tilt: float
      note: string
      distanceM: float
      totalDistanceM: float }

module Sol =
    let current = 0

    let initial =
        { version = current
          location = V3d.NaN
          solNumber = -1
          site = -1
          yaw = nan
          pitch = nan
          roll = nan
          tilt = nan
          note = ""
          distanceM = nan
          totalDistanceM = nan }

    let readV0 =
        json {

            let! location = Json.read "location"
            let! solNumber = Json.read "solNumber"
            let! site = Json.read "site"
            let! yaw = Json.read "yaw"
            let! pitch = Json.read "pitch"
            let! roll = Json.read "roll"
            let! tilt = Json.read "tilt"
            let! note = Json.read "note"
            let! distanceM = Json.read "distanceM"
            let! totalDistanceM = Json.read "totalDistanceM"

            return
                { version = current
                  location = location |> V3d.Parse
                  solNumber = solNumber
                  site = site
                  yaw = yaw
                  pitch = pitch
                  roll = roll
                  tilt = tilt
                  note = note
                  distanceM = distanceM
                  totalDistanceM = totalDistanceM }
        }

type Sol with

    static member FromJson(_: Sol) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! Sol.readV0
            | _ -> return! v |> sprintf "don't know version %d of Traverse" |> Json.error
        }

    static member ToJson(x: Sol) =
        json {
            do! Json.write "version" Sol.current
            do! Json.write "location" (x.location |> string)
            do! Json.write "solNumber" x.solNumber
            do! Json.write "site" x.site
            do! Json.write "yaw" x.yaw
            do! Json.write "pitch" x.pitch
            do! Json.write "roll" x.roll
            do! Json.write "tilt" x.tilt
            do! Json.write "note" x.note
            do! Json.write "distanceM" x.distanceM
            do! Json.write "totalDistanceM" x.totalDistanceM
        }

[<ModelType>]
type Traverse =
    { version: int
      guid: System.Guid
      tName: string
      sols: List<Sol>
      selectedSol: option<int>
      showLines: bool
      showText: bool
      tTextSize: NumericInput
      showDots: bool
      isVisibleT: bool
      color: ColorInput }

module Traverse =
    let colorsSoft12 =
        [ C4b(166, 206, 227)
          C4b(31, 120, 180)
          C4b(178, 223, 138)
          C4b(51, 160, 44)
          C4b(251, 154, 153)
          C4b(227, 26, 28)
          C4b(253, 191, 111)
          C4b(255, 127, 0)
          C4b(202, 178, 214)
          C4b(106, 61, 154)
          C4b(255, 255, 153)
          C4b(177, 89, 40) ]

    let generateBrightColor ()=
        let hue = System.Random().NextDouble() * 360.0  // Random hue between 0 and 360
        let saturation = System.Random().NextDouble() * 0.5 + 0.5  // Saturation between 0.5 and 1 for vibrant colors
        let value = System.Random().NextDouble() * 0.3 + 0.7  // Value between 0.7 and 1 for bright colors
        
        C3f.FromHSV((float32)hue, (float32)saturation, (float32)value)|> C4b.FromC3f

    let current = 1

    let initial name sols =
        { version = current
          guid = Guid.NewGuid()
          tName = name
          sols = sols //[]
          selectedSol = None
          showLines = true
          showText = false
          tTextSize = InitTraverseParams.tText
          showDots = false
          isVisibleT = true
          color = { c = C4b.White }
          }

    let readV0 =
        json {
            let! sols = Json.read "sols"
            let! showLines = Json.read "showLines"
            let! showText = Json.read "showText"
            let! showDots = Json.read "showDots"

            return
                { version = current
                  guid = Guid.NewGuid()
                  tName = ""
                  sols = sols
                  selectedSol = None
                  showLines = showLines
                  showText = showText
                  tTextSize = InitTraverseParams.tText
                  showDots = showDots
                  isVisibleT = true
                  color = { c = C4b.White } }
        }

    let readV1 =
        json {
            let! guid = Json.read "guid"
            let! tName = Json.read "tName"
            let! sols = Json.read "sols"
            let! showLines = Json.read "showLines"
            let! showText = Json.read "showText"
            let! tTextSize = Json.readWith Ext.fromJson<NumericInput, Ext> "tTextSize"
            let! showDots = Json.read "showDots"
            let! isVisibleT = Json.read "isVisibleT"
            let! color = Json.readWith Ext.fromJson<ColorInput, Ext> "color"

            return
                { version = current
                  guid = guid |> Guid
                  tName = tName
                  sols = sols
                  selectedSol = None
                  showLines = showLines
                  showText = showText
                  tTextSize = tTextSize
                  showDots = showDots
                  isVisibleT = isVisibleT
                  color = color }
        }

type Traverse with

    static member FromJson(_: Traverse) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! Traverse.readV0
            | 1 -> return! Traverse.readV1
            | _ -> return! v |> sprintf "don't know version %d of Traverse" |> Json.error
        }

    static member ToJson(x: Traverse) =
        json {
            do! Json.write "version" x.version
            do! Json.write "guid" x.guid
            do! Json.write "tName" x.tName
            do! Json.write "sols" x.sols
            do! Json.write "showLines" x.showLines
            do! Json.write "showText" x.showText
            do! Json.writeWith (Ext.toJson<NumericInput, Ext>) "tTextSize" x.tTextSize
            do! Json.write "showDots" x.showDots
            do! Json.write "isVisibleT" x.isVisibleT
            do! Json.writeWith (Ext.toJson<ColorInput, Ext>) "color" x.color
        }

[<ModelType>]
type TraverseModel =
    { version: int
      traverses: HashMap<Guid, Traverse>
      selectedTraverse: Option<Guid> }

module TraverseModel =

    let current = 0

    let read0 =
        json {
            let! traverses = Json.read "traverses"

            let traverses =
                traverses |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! selected = Json.read "selectedTraverse"

            return
                { version = current
                  traverses = traverses
                  selectedTraverse = selected }
        }

    let initial =
        { version = current
          traverses = HashMap.empty
          selectedTraverse = None }



type TraverseModel with

    static member FromJson(_: TraverseModel) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! TraverseModel.read0
            | _ -> return! v |> sprintf "don't know version %A  of TraverseModel" |> Json.error
        }

    static member ToJson(x: TraverseModel) =
        json {
            do! Json.write "version" x.version
            do! Json.write "traverses" (x.traverses |> HashMap.toList |> List.map snd)
            do! Json.write "selectedTraverse" x.selectedTraverse
        }
