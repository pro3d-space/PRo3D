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
    | ToggleshowRimfaxSurfaces
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
    | IsVisibleRimfaxSurface of traverseId : Guid * solId : int
    | PlaceRoverAtSol of string * Trafo3d * V3d * ReferenceSystem //rotation and location
    | LoadTraverses of list<string>
    | FlyToTraverse of Guid
    | RemoveTraverse of Guid
    | IsVisibleT of Guid
    | SelectTraverse of Guid
    | TraversePropertiesMessage of TraversePropertiesAction
    | RemoveAllTraverses
    | LoadRimfaxSurface of rootDirectory : list<string> * traverseID : Guid 
    | SetRimfaxImageMode of mode : string * traverseID : Guid * solNumber : int
    | PickRimfaxSurface of surfaceId : Guid * traverseId : Guid * solNumber : int

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
    | Rimfax
    | WayPoints
    //| StrategicAnnotations
    //| PlannedTargets with

    static member ToJson (t :TraverseType) =
        match t with
        | Rover -> ToJsonDefaults.ToJson "rover"
        | Rimfax -> ToJsonDefaults.ToJson "rimfax"
        | WayPoints -> ToJsonDefaults.ToJson "waypoints"
        | StrategicAnnotations -> ToJsonDefaults.ToJson "strategicAnnotations"
        | PlannedTargets -> ToJsonDefaults.ToJson "plannedTargets" 

    static member FromJson (_ :TraverseType) = fun json -> 
        match json with
        | String "rover" -> Value Rover, json
        | String "rimfax" -> Value Rimfax, json
        | String "waypoints" -> Value WayPoints, json
        | String "strategicAnnotations" -> Value StrategicAnnotations, json
        | String "plannedTargets" -> Value PlannedTargets, json
        | _ -> failwith (sprintf "Invalid Traverse Type '%A'" json)

[<ModelType>]
type RoverMetrics =
    {
        version: int
        length: float
        fromRMC: string
        toRMC: string
        sclkStart: float
        sclkEnd: float
    }

module RoverMetrics =
    let current = 0

    let readV0 =
        json {

            let! fromRMC = Json.read "fromRMC"
            let! toRMC = Json.read "toRMC"
            let! sclkStart = Json.read "SCLK_START"
            let! sclkEnd = Json.read "SCLK_END"
            let! length = Json.read "length"

            return
                {
                    version = current
                    fromRMC = fromRMC
                    toRMC = toRMC
                    sclkStart = sclkStart
                    sclkEnd = sclkEnd
                    length = length
                }
        }


type RoverMetrics with

    static member FromJson(_: RoverMetrics) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! RoverMetrics.readV0
            | _ -> return! v |> sprintf "don't know version %d of RoverMetrics" |> Json.error
        }

    static member ToJson(x: RoverMetrics) =
        json {
            do! Json.write "version" RoverMetrics.current
            do! Json.write "fromRMC" x.fromRMC
            do! Json.write "toRMC" x.toRMC
            do! Json.writeFloat "SCLK_START" x.sclkStart
            do! Json.writeFloat "SCLK_END" x.sclkEnd
            do! Json.writeFloat "length" x.length
        }

[<ModelType>]
type RimfaxSurfaceMetrics =
    {
        version: int
        rimfaxImageModeOptions: List<string>
        rimfaxImageMode: string
        rimfaxSurfaces : HashMap<Guid, SgSurface>
        isVisibleS: bool
    }


module RimfaxSurfaceMetrics =
    let current = 0

    let readV0 =
        json {
            let! (rimfaxImageModeOptions : list<string>) = Json.read "rimfaxImageModeOptions"
            let! rimfaxImageMode = Json.read "rimfaxImageMode"
            let! isVisibleS = Json.read "isVisibleS"

            return
                {
                    version = current
                    rimfaxImageModeOptions = rimfaxImageModeOptions
                    rimfaxImageMode = rimfaxImageMode
                    rimfaxSurfaces = HashMap.Empty
                    isVisibleS = isVisibleS
                }
        }


type RimfaxSurfaceMetrics with

    static member FromJson(_: RimfaxSurfaceMetrics) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! RimfaxSurfaceMetrics.readV0
            | _ -> return! v |> sprintf "don't know version %d of RimfaxSurfaceMetrics" |> Json.error
        }

    static member ToJson(x: RimfaxSurfaceMetrics) =
        json {
            do! Json.write "version" RimfaxSurfaceMetrics.current
            do! Json.write "rimfaxImageModeOptions" x.rimfaxImageModeOptions 
            do! Json.write "rimfaxImageMode" x.rimfaxImageMode
            do! Json.write "isVisibleS" x.isVisibleS
        }


[<ModelType>]
type RimfaxMetrics =
    {
        version: int
        fromRMC: string
        toRMC: string
        sclkStart: float
        sclkEnd: float
        rimfaxSurfaceProperties: option<RimfaxSurfaceMetrics>
        length: float
    }

module RimfaxMetrics =
    let current = 0

    let readV0 =
        json {

            let! fromRMC = Json.read "fromRMC"
            let! toRMC = Json.read "toRMC"
            let! sclkStart = Json.read "SCLK_START"
            let! sclkEnd = Json.read "SCLK_END"
            let! rimfaxSurfaceProperties = Json.read "rimfaxSurfaceProperties"
            let! length = Json.read "length"

            return
                {
                    version = current
                    fromRMC = fromRMC
                    toRMC = toRMC
                    sclkStart = sclkStart
                    sclkEnd = sclkEnd
                    rimfaxSurfaceProperties = rimfaxSurfaceProperties
                    length = length
                }
        }

type RimfaxMetrics with

    static member FromJson(_: RimfaxMetrics) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! RimfaxMetrics.readV0
            | _ -> return! v |> sprintf "don't know version %d of RimfaxMetrics" |> Json.error
        }

    static member ToJson(x: RimfaxMetrics) =
        json {
            do! Json.write "version" RimfaxMetrics.current
            do! Json.write "fromRMC" x.fromRMC
            do! Json.write "toRMC" x.toRMC
            do! Json.writeFloat "SCLK_START" x.sclkStart
            do! Json.writeFloat "SCLK_END" x.sclkEnd
            do! Json.write "rimfaxSurfaceProperties" x.rimfaxSurfaceProperties
            do! Json.writeFloat "length" x.length
        }

[<ModelType>]
type WaypointMetrics =
    {
        version: int
        RMC: string
        site: int
        yaw: float
        pitch: float
        roll: float
        tilt: float
        note: string
        distanceM: float
        totalDistanceM: float
    }

module WaypointMetrics =
    let current = 1

    let readV0 =
        json {
            let! site = Json.read "site"
            let! yaw = Json.read "yaw"
            let! pitch = Json.read "pitch"
            let! roll = Json.read "roll"
            let! tilt = Json.read "tilt"
            let! note = Json.read "note"
            let! distanceM = Json.read "distanceM"
            let! totalDistanceM = Json.read "totalDistanceM"

            return
                {
                    version = current
                    RMC = ""
                    site = site
                    yaw = yaw
                    pitch = pitch
                    roll = roll
                    tilt = tilt
                    note = note
                    distanceM = distanceM
                    totalDistanceM = totalDistanceM 
                }
        }

    let readV1 =
        json {

            let! RMC = Json.read "RMC"
            let! site = Json.read "site" 
            let! yaw = Json.read "yaw" 
            let! pitch = Json.read "pitch" 
            let! roll = Json.read "roll" 
            let! tilt = Json.read "tilt" 
            let! note = Json.read "note" 
            let! distanceM = Json.read "dist_m" 
            let! totalDistanceM = Json.read "dist_total_m"

            return
                {
                    version = current
                    RMC = RMC
                    site = site
                    yaw = yaw
                    pitch = pitch
                    roll = roll
                    tilt = tilt
                    note = note
                    distanceM = distanceM
                    totalDistanceM = totalDistanceM 
                }
        }

type WaypointMetrics with

    static member FromJson(_: WaypointMetrics) =
        json {
            let! v = Json.read "version"

            match v with
            | 0 -> return! WaypointMetrics.readV0
            | 1 -> return! WaypointMetrics.readV1
            | _ -> return! v |> sprintf "don't know version %d of WaypointMetrics" |> Json.error
        }

    static member ToJson(x: WaypointMetrics) =
        json {
            do! Json.write "version" WaypointMetrics.current
            do! Json.write "RMC" x.RMC
            do! Json.write "site" x.site
            do! Json.writeFloat "yaw" x.yaw
            do! Json.writeFloat "pitch" x.pitch
            do! Json.writeFloat "roll" x.roll
            do! Json.writeFloat "tilt" x.tilt
            do! Json.write "note" x.note
            do! Json.writeFloat "dist_m" x.distanceM
            do! Json.writeFloat "dist_total_m" x.totalDistanceM
        }

[<ModelType>]
type SolMetrics =
    | RoverM of RoverMetrics
    | RimfaxM of RimfaxMetrics
    | WaypointM of WaypointMetrics

type SolMetrics with

    static member ToJson (x: SolMetrics) =
        match x with
        | RoverM m -> 
            json { 
                do! Json.write "type" "Rover"
                do! Json.write "metrics" m
            }
        | RimfaxM m -> 
            json { 
                do! Json.write "type" "Rimfax"
                do! Json.write "metrics" m
            }
        | WaypointM m ->
            json { 
                do! Json.write "type" "Waypoint"
                do! Json.write "metrics" m
            }

    static member FromJson (_: SolMetrics) =
        json {
            let! case = Json.read "type"
            match case with
            | "Rover" ->
                let! metrics = Json.read "metrics"
                return RoverM metrics
            | "Rimfax" ->
                let! metrics = Json.read "metrics"
                return RimfaxM metrics
            | "Waypoint" ->
                let! metrics = Json.read "metrics"
                return WaypointM metrics
            | v ->
                return! Json.error (sprintf "don't know SolMetric type: %s" v)
        }

[<ModelType>]
type Sol =
    { 
      version: int
      location: list<V3d>
      solNumber: int
      solMetrics: option<SolMetrics>
    } 

module Sol =
    let current = 1

    let initial =
        { 
          version = current
          location = []
          solNumber = -1
          solMetrics = None
        }

    let readV0 =
        json {

            let! (location : string) = Json.read "location"
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
                { initial with
                    version = current
                    location = [location |> V3d.Parse]
                    solNumber = solNumber
                    solMetrics = 
                        Some (WaypointM {
                            version = 0
                            RMC = ""
                            site = site
                            yaw = yaw
                            pitch = pitch
                            roll = roll
                            tilt = tilt
                            note = note
                            distanceM = distanceM
                            totalDistanceM = totalDistanceM 
                        })
                }
        }

    let readV1 =

        json {
            let! (location : list<string>) = Json.read "location"
            let! solNumber = Json.read "solNumber"
            let! solMetrics = Json.readOrDefault "solMetrics" None

            return
                { version = current
                  location = location |> List.map V3d.Parse
                  solNumber = solNumber
                  solMetrics = solMetrics
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
            // Sophie: this should actually be Json.writeOption... but Json.writeOption leads to ignoring the downstream ToJson methods,
            // Therefore i here implement Json.write, as we always do have metrics at the moment, but we might have to investigate this
            do! Json.write "solMetrics" x.solMetrics
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
      showRimfaxSurfaces: bool
      tTextSize: NumericInput
      tLineWidth: NumericInput
      showDots: bool
      isVisibleT: bool
      color: ColorInput;
      heightOffset : NumericInput
      priority : NumericInput
      priorityEnabled : bool
      rimfaxRootDirectory : string
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

    let empty() = {
        version = current
        guid = Guid.NewGuid()
        traverseType = TraverseType.Rover
        showRimfaxSurfaces = true
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
        rimfaxRootDirectory = ""
    }

    let initial name sols =
        { (empty ()) with tName = name; sols = sols }

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
                { ( empty () ) with
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
                { ( empty () ) with 
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
                    heightOffset = { ( empty () ).heightOffset with value = Option.defaultValue 0.0 heightOffset }
                    priority = { ( empty () ).priority with value = Option.defaultValue 0.0 priority }
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
            let! showRimfaxSurfaces = Json.read "showRimfaxSurfaces"
            let! rimfaxRootDirectory = Json.read "rimfaxRootDirectory"

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
                    heightOffset = { ( empty () ).heightOffset with value = Option.defaultValue 0.0 heightOffset }
                    priority = { ( empty () ).priority with value = Option.defaultValue 0.0 priority }
                    priorityEnabled = priorityEnabled |> Option.defaultValue false
                    traverseType = traverseType
                    showRimfaxSurfaces = showRimfaxSurfaces
                    rimfaxRootDirectory = rimfaxRootDirectory
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
            do! Json.write "showRimfaxSurfaces" x.showRimfaxSurfaces
            do! Json.write "rimfaxRootDirectory" x.rimfaxRootDirectory
        }


[<ModelType>]
type TraverseModel =
    { version: int
      roverTraverses: HashMap<Guid, Traverse>
      strategicAnnotationTraverses: HashMap<Guid, Traverse>
      rimfaxTraverses: HashMap<Guid, Traverse>
      plannedTargetsTraverses: HashMap<Guid, Traverse>
      waypointsTraverses: HashMap<Guid, Traverse>
      selectedTraverse: Option<Guid>
      selectedRimfaxSurface: Option<Guid>
      }

module TraverseModel =

    let current = 1

    let initial =
        { version = current
          roverTraverses = HashMap.empty
          strategicAnnotationTraverses = HashMap.empty
          rimfaxTraverses = HashMap.empty
          plannedTargetsTraverses = HashMap.empty
          waypointsTraverses = HashMap.empty
          selectedTraverse = None
          selectedRimfaxSurface = None 
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

            let! rimfaxTraverses' = Json.read "rimfaxTraverses"
            let rimfaxTraverses = 
                rimfaxTraverses' |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! plannedTargetsTraverses' = Json.read "plannedTargetsTraverses"
            let plannedTargetsTraverses =
                plannedTargetsTraverses' |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! waypointsTraverses' = Json.read "waypointsTraverses"
            let waypointsTraverses =
                waypointsTraverses' |> List.map (fun (a: Traverse) -> (a.guid, a)) |> HashMap.ofList

            let! selectedTraverse = Json.readOrDefault "selectedTraverse" None
            let! selectedRimfaxSurface = Json.readOrDefault "selectedRimfaxSurface" None

            return
                { version = current
                  roverTraverses = roverTraverses
                  strategicAnnotationTraverses = strategicAnnotationTraverses
                  rimfaxTraverses = rimfaxTraverses
                  plannedTargetsTraverses = plannedTargetsTraverses
                  waypointsTraverses = waypointsTraverses
                  selectedTraverse = selectedTraverse
                  selectedRimfaxSurface = selectedRimfaxSurface}
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
            do! Json.write "rimfaxTraverses" (x.rimfaxTraverses |> HashMap.toList |> List.map snd)
            do! Json.write "plannedTargetsTraverses" (x.plannedTargetsTraverses |> HashMap.toList |> List.map snd)
            do! Json.write "waypointsTraverses" (x.waypointsTraverses |> HashMap.toList |> List.map snd)
            do! Json.write "selectedTraverse" x.selectedTraverse
            do! Json.write "selectedRimfaxSurface" x.selectedRimfaxSurface
        }