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
    | SetLineWidth of Numeric.Action
    | SetTraverseColor of ColorPicker.Action
    | SetHeightOffset of Numeric.Action

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
    | SetImportDirectory of list<string>
    | LoadRIMFAXSurface

module InitTraverseParams =

    let tText =
        { value = 0.05
          min = 0.001
          max = 5.0
          step = 0.001
          format = "{0:0.000}" }

    let tLineW (w : float) =
        { value = w
          min = 0.001
          max = 10.0
          step = 0.001
          format = "{0:0.000}" }


type TraverseType =
    | Rover
    | RIMFAX
    | WayPoints
    | StrategicAnnotations
    | PlannedTargets


type Sol =
    { version: int
      location: list<V3d>
      solNumber: int
      // Rover properties
      site: int
      yaw: float
      pitch: float
      roll: float
      tilt: float
      note: string
      distanceM: float
      totalDistanceM: float
      length: float
      RMC: string
      missionReference: Guid
      // RIMFAX properties
      fromRMC: string
      toRMC: string
      sclkStart: float
      sclkEnd: float
    } 

module Sol =
    let current = 0

    let initial =
        { version = current
          location = [V3d.NaN]
          solNumber = -1
          site = -1
          yaw = nan
          pitch = nan
          roll = nan
          tilt = nan
          note = ""
          distanceM = nan
          totalDistanceM = nan
          length = nan
          RMC = ""
          missionReference = Guid.Empty
          fromRMC = ""
          toRMC = "" 
          sclkStart = nan
          sclkEnd = nan      
        }

    let readV0 =
        json {

            //let! location = Json.read "location"
            let! solNumber = Json.read "solNumber"
            let! site = Json.read "site"
            let! yaw = Json.read "yaw"
            let! pitch = Json.read "pitch"
            let! roll = Json.read "roll"
            let! tilt = Json.read "tilt"
            let! note = Json.read "note"
            let! distanceM = Json.read "distanceM"
            let! totalDistanceM = Json.read "totalDistanceM"
            let! length = Json.read "length"
            let! RMC = Json.read "RMC"
            let! missionReference = Json.read "missionReference"
            let! fromRMC = Json.read "fromRMC"
            let! toRMC = Json.read "toRMC"
            let! sclkStart = Json.read "SCLK_START"
            let! sclkEnd = Json.read "SCLK_END"       

            return
                { version = current
                // !!!! needs fixing!
                  location = [new V3d(0.0, 0.0, 0.0)] //location |> V3d.Parse
                  solNumber = solNumber
                  site = site
                  yaw = yaw
                  pitch = pitch
                  roll = roll
                  tilt = tilt
                  note = note
                  distanceM = distanceM
                  totalDistanceM = totalDistanceM
                  length = length
                  RMC = RMC
                  missionReference = missionReference
                  fromRMC = fromRMC
                  toRMC = toRMC
                  sclkStart = sclkStart
                  sclkEnd = sclkEnd
                }
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
      [<NonAdaptive>]
      guid: System.Guid
      [<NonAdaptive>]
      tName: string
      sols: List<Sol>
      [<NonAdaptive>]
      traverseType: TraverseType
      selectedSol: option<int>
      showLines: bool
      showText: bool
      tTextSize: NumericInput
      tLineWidth: NumericInput
      showDots: bool
      isVisibleT: bool
      color: ColorInput;
      heightOffset : NumericInput
    }

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
          traverseType = TraverseType.Rover
          sols = sols //[]
          selectedSol = None
          showLines = true
          showText = false
          tTextSize = InitTraverseParams.tText
          tLineWidth = InitTraverseParams.tLineW 1.5
          showDots = false
          isVisibleT = true
          color = { c = C4b.White }
          heightOffset = { Numeric.init with value = 0.0 }
          }

    let withTraverseType(traverseType: TraverseType) (t: Traverse) =
        { t with traverseType = traverseType }

    let withColor(color: C4b) (t: Traverse) =
        { t with color = { c = color } }

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
                  traverseType = TraverseType.Rover
                  selectedSol = None
                  showLines = showLines
                  showText = showText
                  tTextSize = InitTraverseParams.tText
                  tLineWidth = InitTraverseParams.tLineW 1.5
                  showDots = showDots
                  isVisibleT = true
                  color = { c = C4b.White } 
                  heightOffset = { Numeric.init with value = 0.0}
                }
        }

    let readV1 =
        json {
            let! guid = Json.read "guid"
            let! tName = Json.read "tName"
            let! sols = Json.read "sols"
            let! showLines = Json.read "showLines"
            let! showText = Json.read "showText"
            let! tTextSize = Json.readWith Ext.fromJson<NumericInput, Ext> "tTextSize"
            let! tLWidth = Json.tryRead "tLineWidth"
            let! showDots = Json.read "showDots"
            let! isVisibleT = Json.read "isVisibleT"
            let! color = Json.readWith Ext.fromJson<ColorInput, Ext> "color"
            let! heightOffset = Json.tryRead "heightOffset"

            let tLineWidth = 
                match tLWidth with
                | Some w -> InitTraverseParams.tLineW w
                | None -> InitTraverseParams.tLineW 1.5

            return
                { version = current
                  guid = guid |> Guid
                  tName = tName
                  traverseType = TraverseType.Rover
                  sols = sols
                  selectedSol = None
                  showLines = showLines
                  showText = showText
                  tTextSize = tTextSize
                  tLineWidth = tLineWidth
                  showDots = showDots
                  isVisibleT = isVisibleT
                  color = color
                  heightOffset = { Numeric.init with value = Option.defaultValue 0.0 heightOffset }
                }
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
            do! Json.write "tLineWidth" x.tLineWidth.value
            do! Json.write "heightOffset" x.heightOffset.value
        }


[<ModelType>]
type TraverseModel =
    { version: int
      roverTraverses: HashMap<Guid, Traverse>
      strategicAnnotationTraverses: HashMap<Guid, Traverse>
      RIMFAXTraverses: HashMap<Guid, Traverse>
      plannedTargetsTraverses: HashMap<Guid, Traverse>
      waypointsTraverses: HashMap<Guid, Traverse>
      selectedTraverse: Option<Guid>
      RIMFAXRootDirectory: string
      }

module TraverseModel =

    let current = 0

    let read0 =
        json {
            let! roverTraverses' = Json.read "roverTraverses"
            let roverTraverses =
                roverTraverses' |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! strategicAnnotationTraverses = Json.read "strategicAnnotationTraverses"
            let strategicAnnotationTraverses =
                strategicAnnotationTraverses |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! RIMFAXTraverses' = Json.read "RIMFAXTraverses"
            let RIMFAXTraverses =
                RIMFAXTraverses' |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! plannedTargetsTraverses' = Json.read "plannedTargetsTraverses"
            let plannedTargetsTraverses =
                plannedTargetsTraverses' |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! waypointsTraverses' = Json.read "waypointsTraverses"
            let waypointsTraverses =
                waypointsTraverses' |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! selected = Json.read "selectedTraverse"

            let! RIMFAXRootDirectory = Json.read "RIMFAXRootDirectory"

            return
                { version = current
                  roverTraverses = roverTraverses
                  strategicAnnotationTraverses = strategicAnnotationTraverses
                  RIMFAXTraverses = RIMFAXTraverses
                  plannedTargetsTraverses = plannedTargetsTraverses
                  waypointsTraverses = waypointsTraverses
                  selectedTraverse = selected 
                  RIMFAXRootDirectory = RIMFAXRootDirectory}
        }

    let initial =
        { version = current
          roverTraverses = HashMap.empty
          strategicAnnotationTraverses = HashMap.empty
          RIMFAXTraverses = HashMap.empty
          plannedTargetsTraverses = HashMap.empty
          waypointsTraverses = HashMap.empty
          selectedTraverse = None 
          RIMFAXRootDirectory = ""}


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
            do! Json.write "roverTraverses" (x.roverTraverses |> HashMap.toList |> List.map snd)
            do! Json.write "strategicAnnotationTraverses" (x.strategicAnnotationTraverses |> HashMap.toList |> List.map snd)
            do! Json.write "RIMFAXTraverses" (x.RIMFAXTraverses |> HashMap.toList |> List.map snd)
            do! Json.write "plannedTargetsTraverses" (x.plannedTargetsTraverses |> HashMap.toList |> List.map snd)
            do! Json.write "waypointsTraverses" (x.waypointsTraverses |> HashMap.toList |> List.map snd)
            do! Json.write "selectedTraverse" x.selectedTraverse
            do! Json.write "RIMFAXRootDirectory" x.RIMFAXRootDirectory
        }