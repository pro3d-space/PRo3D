#nowarn "0686"
namespace PRo3D.Core

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Core

open PRo3D.Core.Surface

open Adaptify
open Chiron

type TraversePropertiesAction =
    | ToggleShowText
    | ToggleShowRIMFAXSurfaces
    | ToggleShowLines
    | ToggleShowDots
    | SetTraverseName of string
    | SetSolTextsize of Numeric.Action
    | SetLineWidth of Numeric.Action
    | SetTraverseColor of ColorPicker.Action
    | SetHeightOffset of Numeric.Action
    | SetPriority of Numeric.Action
    | TogglePriorityRenderingEnabled

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
    | LoadRIMFAXSurface of rootDirectory : list<string> * traverseID : Guid 
    | SetRIMFAXImageMode of mode : string * traverseID : Guid * solNumber : int
    | PickRIMFAXSurface of surfaceId : Guid * traverseId : Guid * solNumber : int

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
    | PlannedTargets with

    static member ToJson (t :TraverseType) =
        match t with
        | Rover -> ToJsonDefaults.ToJson "rover"
        | RIMFAX -> ToJsonDefaults.ToJson "rimfax"
        | WayPoints -> ToJsonDefaults.ToJson "waypoints"
        | StrategicAnnotations -> ToJsonDefaults.ToJson "strategicAnnotations"
        | PlannedTargets -> ToJsonDefaults.ToJson "plannedTargets" 

    static member FromJson (_ :TraverseType) = fun json -> 
        match json with
        | String "rover" -> Value Rover, json
        | String "rimfax" -> Value RIMFAX, json
        | String "waypoints" -> Value WayPoints, json
        | String "strategicAnnotations" -> Value StrategicAnnotations, json
        | String "plannedTargets" -> Value PlannedTargets, json
        | _ -> failwith (sprintf "Invalid Traverse Type '%A'" json)

[<ModelType>]
type Sol =
    { version: int
      location: list<V3d>
      solNumber: int
      // Rover properties
      site: option<int>
      yaw: option<float>
      pitch: option<float>
      roll: option<float>
      tilt: option<float>
      note: option<string>
      distanceM: option<float>
      totalDistanceM: option<float>
      length: option<float>
      RMC: option<string>
      missionReference: option<Guid>
      // RIMFAX properties
      fromRMC: option<string>
      toRMC: option<string>
      sclkStart: option<float>
      sclkEnd: option<float>
      RIMFAXImageModeOptions: option<List<string>>
      RIMFAXImageMode: option<string>
      RIMFAXSurfaces : option<HashMap<Guid, SgSurface>>
    } 

module Sol =
    let current = 1

    let initial =
        { version = current
          location = []
          solNumber = -1
          site = None
          yaw = None
          pitch = None
          roll = None
          tilt = None
          note = None
          distanceM = None
          totalDistanceM = None
          length = None
          RMC = None
          missionReference = None
          fromRMC = None
          toRMC = None
          sclkStart = None
          sclkEnd = None
          RIMFAXImageModeOptions = None
          RIMFAXImageMode = None
          RIMFAXSurfaces = None
        }

    let readV0 =
        json {

            let! (location : string) = Json.read "location"
            let! solNumber = Json.read "solNumber"
            let! site = Json.readOrDefault "site" None
            let! yaw = Json.readOrDefault "yaw" None
            let! pitch = Json.readOrDefault "pitch" None
            let! roll = Json.readOrDefault "roll" None
            let! tilt = Json.readOrDefault "tilt" None
            let! note = Json.readOrDefault "note" None
            let! distanceM = Json.readOrDefault "distanceM" None
            let! totalDistanceM = Json.readOrDefault "totalDistanceM" None

            return
                { initial with
                      version = current
                      location = [location |> V3d.Parse]
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

    let readV1 =

        json {
            let! (location : list<string>) = Json.read "location"
            let! solNumber = Json.read "solNumber"
            let! site = Json.readOrDefault "site" None
            let! yaw = Json.readOrDefault "yaw" None
            let! pitch = Json.readOrDefault "pitch" None
            let! roll = Json.readOrDefault "roll" None
            let! tilt = Json.readOrDefault "tilt" None
            let! note = Json.readOrDefault "note" None
            let! distanceM = Json.readOrDefault "distanceM" None
            let! totalDistanceM = Json.readOrDefault "totalDistanceM" None
            let! length = Json.readOrDefault "length" None
            let! RMC = Json.readOrDefault "RMC" None
            let! missionReference = Json.readOrDefault "missionReference" None
            let! fromRMC = Json.readOrDefault "fromRMC" None
            let! toRMC = Json.readOrDefault "toRMC" None
            let! sclkStart = Json.readOrDefault "SCLK_START" None
            let! sclkEnd = Json.readOrDefault "SCLK_END" None
            let! (RIMFAXImageModeOptions : option<list<string>>) = Json.readOrDefault "RIMFAXImageModeOptions" None
            let! RIMFAXImageMode = Json.readOrDefault "RIMFAXImageMode" None

            return
                { version = current
                  location = location |> List.map V3d.Parse
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
                  RIMFAXImageModeOptions = RIMFAXImageModeOptions
                  RIMFAXImageMode = RIMFAXImageMode
                  RIMFAXSurfaces = None
                }
        }


type Sol with

    static member FromJson(_: Sol) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! Sol.readV0
            | 1 -> return! Sol.readV1
            | _ -> return! v |> sprintf "don't know version %d of Sol" |> Json.error
        }


    static member ToJson(x: Sol) =
        json {
            do! Json.write "version" Sol.current
            do! Json.write "location" (x.location |> List.map(fun x -> x.ToString()))
            do! Json.write "solNumber" x.solNumber
            do! Json.writeOptionInt "site" x.site
            do! Json.writeOptionFloat "yaw" x.yaw
            do! Json.writeOptionFloat "pitch" x.pitch
            do! Json.writeOptionFloat "roll" x.roll
            do! Json.writeOptionFloat "tilt" x.tilt
            do! Json.writeOption "note" x.note
            do! Json.writeOptionFloat "distanceM" x.distanceM
            do! Json.writeOptionFloat "totalDistanceM" x.totalDistanceM
            do! Json.writeOptionFloat "length" x.length
            do! Json.writeOption "RMC" x.RMC
            do! Json.writeOption "missionReference" x.missionReference
            do! Json.writeOption "fromRMC" x.fromRMC
            do! Json.writeOption "toRMC" x.toRMC
            do! Json.writeOptionFloat "sclkStart" x.sclkStart
            do! Json.writeOptionFloat "sclkEnd" x.sclkEnd
            do! Json.writeOptionList "RIMFAXImageModeOptions" x.RIMFAXImageModeOptions (fun options option -> Json.write option options)  
            do! Json.writeOption "RIMFAXImageMode" x.RIMFAXImageMode
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
      showRIMFAXSurfaces: bool
      tTextSize: NumericInput
      tLineWidth: NumericInput
      showDots: bool
      isVisibleT: bool
      color: ColorInput;
      heightOffset : NumericInput
      priority : NumericInput
      priorityEnabled : bool
      RIMFAXRootDirectory : string
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

    let current = 2

    let initialPriority = {
        value  = 0.0
        min    = 0.0
        max    = 10.0
        step   = 1.0
        format = "{0:0}"
    }

    let empty = {
        version = current
        guid = Guid.NewGuid()
        traverseType = TraverseType.Rover
        showRIMFAXSurfaces = true
        tName = ""
        sols = []
        selectedSol = None
        showLines = true
        showText = false
        tTextSize = InitTraverseParams.tText
        tLineWidth = InitTraverseParams.tLineW 1.5
        showDots = false
        isVisibleT = true
        color = { c = C4b.White }
        heightOffset = { Numeric.init with value = 0.0; min = -100.0; max = 100.0 }
        priority = initialPriority
        priorityEnabled = false
        RIMFAXRootDirectory = ""
    }

    let initial name sols =
        { empty with tName = name; sols = sols }

    let withTraverseType(traverseType: TraverseType) (t: Traverse) =
        { t with traverseType = traverseType }

    let withProperties(showLines: bool) (showText: bool) (showDots: bool) (t: Traverse) =
        { t with showLines = showLines; showText = showText; showDots = showDots }

    let withColor(color: C4b) (t: Traverse) =
        { t with color = { c = color } }

    let readV0 =
        json {
            let! sols = Json.read "sols"
            let! showLines = Json.read "showLines"
            let! showText = Json.read "showText"
            let! showDots = Json.read "showDots"

            return
                { empty with
                    version = current
                    guid = Guid.NewGuid()
                    tName = ""
                    sols = sols
                    selectedSol = None
                    showLines = showLines
                    showText = showText
                    tTextSize = InitTraverseParams.tText
                    tLineWidth = InitTraverseParams.tLineW 1.5
                    showDots = showDots
                    isVisibleT = true
                    color = { c = C4b.White } 
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
            let! priorityEnabled = Json.tryRead "priorityEnabled"

            let tLineWidth = 
                match tLWidth with
                | Some w -> InitTraverseParams.tLineW w
                | None -> InitTraverseParams.tLineW 1.5

            let! priority = Json.tryRead "priority" 

            return
                { empty with 
                    version = current
                    guid = guid |> Guid
                    tName = tName
                    sols = sols
                    selectedSol = None
                    showLines = showLines
                    showText = showText
                    tTextSize = tTextSize
                    tLineWidth = tLineWidth
                    showDots = showDots
                    isVisibleT = isVisibleT
                    color = color
                    heightOffset = { empty.heightOffset with value = Option.defaultValue 0.0 heightOffset }
                    priority = { empty.priority with value = Option.defaultValue 0.0 priority }
                    priorityEnabled = priorityEnabled |> Option.defaultValue false
                }
        }

    let readV2 =
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
            let! priorityEnabled = Json.tryRead "priorityEnabled"
            let! traverseType = Json.read "traverseType"
            let! showRIMFAXSurfaces = Json.read "showRIMFAXSurfaces"
            let! RIMFAXRootDirectory = Json.read "RIMFAXRootDirectory"

            let tLineWidth = 
                match tLWidth with
                | Some w -> InitTraverseParams.tLineW w
                | None -> InitTraverseParams.tLineW 1.5

            let! priority = Json.tryRead "priority" 

            return
                {   version = current
                    guid = guid |> Guid
                    tName = tName
                    sols = sols
                    selectedSol = None
                    showLines = showLines
                    showText = showText
                    tTextSize = tTextSize
                    tLineWidth = tLineWidth
                    showDots = showDots
                    isVisibleT = isVisibleT
                    color = color
                    heightOffset = { empty.heightOffset with value = Option.defaultValue 0.0 heightOffset }
                    priority = { empty.priority with value = Option.defaultValue 0.0 priority }
                    priorityEnabled = priorityEnabled |> Option.defaultValue false
                    traverseType = traverseType
                    showRIMFAXSurfaces = showRIMFAXSurfaces
                    RIMFAXRootDirectory = RIMFAXRootDirectory
                }
        }


type Traverse with

    static member FromJson(_: Traverse) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! Traverse.readV0
            | 1 -> return! Traverse.readV1
            | 2 -> return! Traverse.readV2
            | _ -> return! v |> sprintf "don't know version %d of Traverse" |> Json.error
        }

    static member ToJson(x: Traverse) =
        json {
            do! Json.write "version" x.version
            do! Json.write "guid" x.guid
            do! Json.write "tName" x.tName
            do! Json.write "sols" x.sols
            do! Json.write "selectedSol" x.selectedSol
            do! Json.write "showLines" x.showLines
            do! Json.write "showText" x.showText
            do! Json.writeWith (Ext.toJson<NumericInput, Ext>) "tTextSize" x.tTextSize
            do! Json.write "showDots" x.showDots
            do! Json.write "isVisibleT" x.isVisibleT
            do! Json.writeWith (Ext.toJson<ColorInput, Ext>) "color" x.color
            do! Json.write "tLineWidth" x.tLineWidth.value
            do! Json.write "heightOffset" x.heightOffset.value
            do! Json.write "priority" x.priority.value
            do! Json.write "priorityEnabled" x.priorityEnabled
            do! Json.write "traverseType" x.traverseType
            do! Json.write "showRIMFAXSurfaces" x.showRIMFAXSurfaces
            do! Json.write "RIMFAXRootDirectory" x.RIMFAXRootDirectory
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
      selectedRIMFAXSurface: Option<Guid>
      }

module TraverseModel =

    let current = 1

    let initial =
        { version = current
          roverTraverses = HashMap.empty
          strategicAnnotationTraverses = HashMap.empty
          RIMFAXTraverses = HashMap.empty
          plannedTargetsTraverses = HashMap.empty
          waypointsTraverses = HashMap.empty
          selectedTraverse = None
          selectedRIMFAXSurface = None 
        }

    let read0 =
        json {
            let! traverses = Json.read "traverses"

            let traverses =
                traverses |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! selected = Json.read "selectedTraverse"

            return
                { initial with
                      version = current
                      waypointsTraverses = traverses
                      selectedTraverse = selected }
        }

    let read1 =
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

            let! selectedTraverse = Json.readOrDefault "selectedTraverse" None
            let! selectedRIMFAXSurface = Json.readOrDefault "selectedRIMFAXSurface" None

            return
                { version = current
                  roverTraverses = roverTraverses
                  strategicAnnotationTraverses = strategicAnnotationTraverses
                  RIMFAXTraverses = RIMFAXTraverses
                  plannedTargetsTraverses = plannedTargetsTraverses
                  waypointsTraverses = waypointsTraverses
                  selectedTraverse = selectedTraverse
                  selectedRIMFAXSurface = selectedRIMFAXSurface}
        }


type TraverseModel with

    static member FromJson(_: TraverseModel) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! TraverseModel.read0
            | 1 -> return! TraverseModel.read1
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
            do! Json.write "selectedTraverse" x.selectedRIMFAXSurface
        }