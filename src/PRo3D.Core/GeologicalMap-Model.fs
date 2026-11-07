namespace PRo3D.Core

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open Adaptify
open Chiron

open PRo3D.Base

// Geological maps (#843, docs/GeologicalMap.md): a map is derived from the contacts (polylines)
// and label points (points with a geoUnit) of one annotation group. Only the map's settings
// and its legend are stored; the units themselves are recomputed whenever the group changes.
// Persisted with the annotations (Annotations.geologicalMaps), under keys older releases skip.

/// Panel actions (GeologicalMapApp.update); the drawing app handles the two that touch
/// annotations.
[<RequireQualifiedAccess>]
type GeologicalMapAction =
    /// the feature is experimental: off (panel notice only, no maps drawn, no tools) until
    /// opened; session only
    | SetEnabled      of bool
    /// a new map over the annotation group with this Node.key, made active
    | NewMap          of group : Guid * name : string
    | RemoveMap       of Guid
    | SetActive       of Guid
    | SetVisible      of Guid * bool
    | SetName         of Guid * string
    | SetOpacity      of Guid * Numeric.Action
    | AddUnit         of Guid
    | RemoveUnit      of map : Guid * unit : Guid
    | SetUnitName     of map : Guid * unit : Guid * string
    | SetUnitColor    of map : Guid * unit : Guid * ColorPicker.Action
    | SetActiveUnit   of Guid
    /// Handled by the drawing app, which owns the annotations: renames the code on the
    /// map's label points too.
    | SetUnitCode     of map : Guid * unit : Guid * string
    /// Handled by the drawing app: the active unit onto the selected label points.
    | AssignToSelected
    /// Handled by the drawing app (the Assign unit tool): the active unit to the unit under
    /// the cursor - onto its label points, or a new label point at `position` if it has none.
    | AssignAt        of map : Guid * labels : Guid list * position : V3d
    /// eyedropper: the legend entry with this code becomes the active unit
    | PickUnit        of map : Guid * code : string
    /// Handled by the viewer: the camera to this point (a problem of the map)
    | FlyTo           of V3d
    /// Handled by the viewer: the camera to the active map's next problem
    | NextProblem

/// One entry of a map's legend.
[<ModelType>]
type GeoUnit = {
    [<NonAdaptive>]
    id          : Guid
    /// what label points carry in Annotation.geoUnit
    code        : string
    name        : string
    color       : ColorInput
}

[<ModelType>]
type GeologicalMap = {
    [<NonAdaptive>]
    key         : Guid
    name        : string
    /// Node.key of the annotation group holding the contacts and label points
    sourceGroup : Guid
    units       : IndexList<GeoUnit>
    visible     : bool
    opacity     : NumericInput
}

[<ModelType>]
type GeologicalMapsModel = {
    maps        : IndexList<GeologicalMap>
    /// the map the tools work on
    active      : Option<Guid>
    /// the legend entry Assign gives to selected label points
    activeUnit  : Option<Guid>
    /// which problem NextProblem flew to last; session only, not persisted
    problemCursor : int
    /// opened past the panel's experimental notice; session only, not persisted. Off, no map
    /// is drawn and no tool works on one (`tryActive` is None)
    enabled     : bool
}

/// Reading maps never fails the annotation file they ride in: an entry this release cannot
/// read - a newer release's version, a malformed id or colour - is skipped, not an error that
/// takes all annotations down (scene compatibility, docs/GeologicalMap.md).
module internal JsonTolerant =

    /// The entries of the array under `key` that deserialize; the others are skipped.
    let inline readSome (key : string) : Json<list<'a>> =
        fun json ->
            let items =
                match json with
                | Json.Object o ->
                    match Map.tryFind key o with
                    | Some (Json.Array js) ->
                        js |> List.choose (fun j ->
                            match Json.tryDeserialize j with
                            | Choice1Of2 x -> Some x
                            | Choice2Of2 _ -> None)
                    | _ -> []
                | _ -> []
            Value items, json

    let guid (s : string) : Json<Guid> =
        match Guid.TryParse s with
        | true, g -> Json.init g
        | _ -> Json.error (sprintf "not a guid: %s" s)

    let color (s : string) : Json<C4b> =
        try Json.init (C4b.Parse s)
        with _ -> Json.error (sprintf "not a colour: %s" s)

module GeoUnit =

    /// A qualitative palette (ColorBrewer Set3/Paired-like), cycled for new units.
    let palette =
        [| C4b(141, 211, 199); C4b(255, 255, 179); C4b(190, 186, 218); C4b(251, 128, 114)
           C4b(128, 177, 211); C4b(253, 180,  98); C4b(179, 222, 105); C4b(252, 205, 229)
           C4b(188, 128, 189); C4b(204, 235, 197); C4b(255, 237, 111); C4b(166, 206, 227) |]

    let make (code : string) (color : C4b) =
        { id = Guid.NewGuid(); code = code; name = code; color = { c = color } }

    let private read0 =
        json {
            let! id    = Json.read "id"
            let! code  = Json.read "code"
            let! name  = Json.read "name"
            let! color = Json.read "color"
            let! id    = JsonTolerant.guid id
            let! color = JsonTolerant.color color
            return { id = id; code = code; name = name; color = { c = color } }
        }

    let toJson (x : GeoUnit) =
        json {
            do! Json.write "version" 0
            do! Json.write "id"      (x.id.ToString())
            do! Json.write "code"    x.code
            do! Json.write "name"    x.name
            do! Json.write "color"   (x.color.c.ToString())
        }

    let fromJson =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! read0
            | _ -> return! v |> sprintf "don't know version %d of GeoUnit" |> Json.error
        }

type GeoUnit with
    static member FromJson (_ : GeoUnit) = GeoUnit.fromJson
    static member ToJson (x : GeoUnit) = GeoUnit.toJson x

module GeologicalMap =

    let initialOpacity = {
        value  = 0.5
        min    = 0.0
        max    = 1.0
        step   = 0.05
        format = "{0:0.00}"
    }

    let make (name : string) (sourceGroup : Guid) =
        {
            key         = Guid.NewGuid()
            name        = name
            sourceGroup = sourceGroup
            units       = IndexList.empty
            visible     = true
            opacity     = initialOpacity
        }

    let private read0 =
        json {
            let! key         = Json.read "key"
            let! name        = Json.read "name"
            let! sourceGroup = Json.read "sourceGroup"
            let! units       = JsonTolerant.readSome "units"
            let! visible     = Json.read "visible"
            let! opacity     = Json.readFloat "opacity"
            let! key         = JsonTolerant.guid key
            let! sourceGroup = JsonTolerant.guid sourceGroup
            return {
                key         = key
                name        = name
                sourceGroup = sourceGroup
                units       = (units : list<GeoUnit>) |> IndexList.ofList
                visible     = visible
                opacity     = { initialOpacity with value = if Double.IsFinite opacity then clamp 0.0 1.0 opacity else initialOpacity.value }
            }
        }

    let toJson (x : GeologicalMap) =
        json {
            do! Json.write "version"     0
            do! Json.write "key"         (x.key.ToString())
            do! Json.write "name"        x.name
            do! Json.write "sourceGroup" (x.sourceGroup.ToString())
            do! Json.write "units"       (x.units |> IndexList.toList)
            do! Json.write "visible"     x.visible
            do! Json.writeFloat "opacity" x.opacity.value
        }

    let fromJson =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! read0
            | _ -> return! v |> sprintf "don't know version %d of GeologicalMap" |> Json.error
        }

type GeologicalMap with
    static member FromJson (_ : GeologicalMap) = GeologicalMap.fromJson
    static member ToJson (x : GeologicalMap) = GeologicalMap.toJson x

module GeologicalMapsModel =

    let initial = { maps = IndexList.empty; active = None; activeUnit = None; problemCursor = -1; enabled = false }

    let private read0 =
        json {
            let! maps   = JsonTolerant.readSome "maps"
            let! active = Json.tryRead "active"
            return {
                maps       = (maps : list<GeologicalMap>) |> IndexList.ofList
                active     =
                    active |> Option.bind (fun (s : string) ->
                        match Guid.TryParse s with
                        | true, g -> Some g
                        | _ -> None)
                activeUnit = None
                problemCursor = -1
                enabled    = false
            }
        }

    let toJson (x : GeologicalMapsModel) =
        json {
            do! Json.write "version" 0
            do! Json.write "maps"    (x.maps |> IndexList.toList)
            match x.active with
            | Some a -> do! Json.write "active" (a.ToString())
            | None -> ()
        }

    let fromJson =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! read0
            // a newer release's maps: none here, the annotations themselves still load
            | _ -> return initial
        }

    /// The map the tools work on; none while the feature is off.
    let tryActive (m : GeologicalMapsModel) =
        if not m.enabled then None else
        m.active |> Option.bind (fun k -> m.maps |> IndexList.toArray |> Array.tryFind (fun x -> x.key = k))

type GeologicalMapsModel with
    static member FromJson (_ : GeologicalMapsModel) = GeologicalMapsModel.fromJson
    static member ToJson (x : GeologicalMapsModel) = GeologicalMapsModel.toJson x
