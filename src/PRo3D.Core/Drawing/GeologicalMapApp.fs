namespace PRo3D.Core.Drawing

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Base.Geometry
open PRo3D.Core

/// Geological maps (#843, docs/GeologicalMap.md): units derived from the contacts (polylines)
/// and label points (points with a geoUnit) of one annotation group, drawn as fills against
/// the surface depth (#842).
///
/// Nothing derived is stored. The topology is recomputed from the group whenever one of its
/// annotations changes (ContactTopology), the volumes whenever the topology or the reach does
/// (MapShell), and only the colours when the legend or the opacity does.
module GeologicalMapApp =

    type Action = GeologicalMapAction

    let private updateMap (key : Guid) (f : GeologicalMap -> GeologicalMap) (m : GeologicalMapsModel) =
        { m with maps = m.maps |> IndexList.map (fun x -> if x.key = key then f x else x) }

    let private updateUnit (unitId : Guid) (f : GeoUnit -> GeoUnit) (map : GeologicalMap) =
        { map with units = map.units |> IndexList.map (fun u -> if u.id = unitId then f u else u) }

    /// A code not used by the map yet: U1, U2, ...
    let private freshCode (map : GeologicalMap) =
        let used = map.units |> IndexList.toArray |> Array.map (fun u -> u.code) |> HashSet.ofArray
        let rec go i = let c = sprintf "U%d" i in if used.Contains c then go (i + 1) else c
        go (map.units.Count + 1)

    let tryFindUnit (m : GeologicalMapsModel) (unitId : Guid) =
        m.maps |> IndexList.toArray |> Array.tryPick (fun map ->
            map.units |> IndexList.toArray |> Array.tryFind (fun u -> u.id = unitId) |> Option.map (fun u -> map, u))

    /// The active unit, when the active map is drawn into: its group is `activeGroup`.
    let activeUnitFor (activeGroup : Guid) (m : GeologicalMapsModel) : Option<GeoUnit> =
        match GeologicalMapsModel.tryActive m, m.activeUnit with
        | Some map, Some unitId when map.sourceGroup = activeGroup ->
            map.units |> IndexList.toArray |> Array.tryFind (fun u -> u.id = unitId)
        | _ -> None

    /// Path (as GroupsApp addresses groups) and node of the group with this key.
    let tryGroupPath (root : Node) (key : Guid) : Option<list<Index> * Node> =
        let rec go (path : list<Index>) (n : Node) =
            // GroupsApp keeps paths deepest index first (getNode reverses them)
            if n.key = key then Some (path, n)
            else n.subNodes |> IndexList.toSeqIndexed |> Seq.tryPick (fun (i, s) -> go (i :: path) s)
        go [] root

    let update (m : GeologicalMapsModel) (action : Action) : GeologicalMapsModel =
        match action with
        | Action.NewMap (group, name) ->
            let map = GeologicalMap.make name group
            { m with maps = IndexList.add map m.maps; active = Some map.key; activeUnit = None }
        | Action.RemoveMap key ->
            { m with
                maps   = m.maps |> IndexList.filter (fun x -> x.key <> key)
                active = if m.active = Some key then None else m.active }
        | Action.SetActive key ->
            if m.active = Some key then m else { m with active = Some key; activeUnit = None }
        | Action.SetVisible (key, v) -> m |> updateMap key (fun x -> { x with visible = v })
        | Action.SetName (key, n)    -> m |> updateMap key (fun x -> { x with name = n })
        | Action.SetOpacity (key, a) -> m |> updateMap key (fun x -> { x with opacity = Numeric.update x.opacity a })
        | Action.AddUnit key ->
            let mutable added = None
            let m =
                m |> updateMap key (fun map ->
                    let color = GeoUnit.palette.[map.units.Count % GeoUnit.palette.Length]
                    let u = GeoUnit.make (freshCode map) color
                    added <- Some u.id
                    { map with units = IndexList.add u map.units })
            { m with activeUnit = added |> Option.orElse m.activeUnit }
        | Action.RemoveUnit (key, unitId) ->
            let m = m |> updateMap key (fun map -> { map with units = map.units |> IndexList.filter (fun u -> u.id <> unitId) })
            { m with activeUnit = if m.activeUnit = Some unitId then None else m.activeUnit }
        | Action.SetUnitName (key, unitId, n) -> m |> updateMap key (updateUnit unitId (fun u -> { u with name = n }))
        | Action.SetUnitColor (key, unitId, a) -> m |> updateMap key (updateUnit unitId (fun u -> { u with color = ColorPicker.update u.color a }))
        | Action.SetActiveUnit unitId ->
            { m with activeUnit = if m.activeUnit = Some unitId then None else Some unitId }
        | Action.SetUnitCode (key, unitId, c) ->
            let c = c.Trim()
            if String.IsNullOrEmpty c then m
            else m |> updateMap key (updateUnit unitId (fun u -> { u with code = c }))
        | Action.AssignToSelected -> m

    // -----------------------------------------------------------------------------------------
    // the group's annotations -> topology
    // -----------------------------------------------------------------------------------------

    /// Annotations that count as contacts. Any line in the map's group does.
    let isContact (g : Geometry) =
        match g with
        | Geometry.Polyline | Geometry.Polygon | Geometry.Line -> true
        | _ -> false

    type Derived =
        {
            topology   : ContactTopology.Result
            /// every contact sample and label position: the terrain the shell must enclose
            samples    : V3d[]
            /// the label points' codes, distinct
            labelUnits : string list
            contacts : int
            labels   : int
        }

    let private emptyDerived =
        { topology = { faces = [||]; openEnds = [||]; eulerOk = true; labelFace = Map.empty }
          samples = [||]; labelUnits = []; contacts = 0; labels = 0 }

    let private findGroup (root : AdaptiveNode) (key : Guid) (t : AdaptiveToken) : Option<AdaptiveNode> =
        let rec find (n : AdaptiveNode) =
            if n.key.GetValue t = key then Some n
            else n.subNodes.Content.GetValue t |> IndexList.toArray |> Array.tryPick find
        find root

    /// Leaf ids of the group with this key and all its subgroups; empty when there is none.
    let private leavesOf (root : AdaptiveNode) (key : Guid) (t : AdaptiveToken) : Guid[] =
        match findGroup root key t with
        | None -> [||]
        | Some n ->
            let res = List<Guid>()
            let rec collect (n : AdaptiveNode) =
                for l in n.leaves.Content.GetValue t do res.Add l
                for s in n.subNodes.Content.GetValue t do collect s
            collect n
            res.ToArray()

    /// Leaf ids of the group with this key and all its subgroups (non-adaptive twin of leavesOf).
    let leafIdsOf (root : Node) (key : Guid) : Guid[] =
        let rec find (n : Node) =
            if n.key = key then Some n
            else n.subNodes |> IndexList.toArray |> Array.tryPick find
        match find root with
        | None -> [||]
        | Some n ->
            let res = List<Guid>()
            let rec collect (n : Node) =
                for l in n.leaves do res.Add l
                for s in n.subNodes do collect s
            collect n
            res.ToArray()

    /// The body centre: annotation coordinates are body-fixed, centred on the body.
    let center = V3d.Zero

    /// Contacts and labels of the group, then the topology. A pure function of the group's
    /// annotations; reruns when any of them changes.
    let private derive (groups : AdaptiveGroupsModel) (map : AdaptiveGeologicalMap) : aval<Derived> =
        AVal.custom (fun t ->
            let key = map.sourceGroup.GetValue t
            let ids = leavesOf groups.rootGroup key t
            let flat = groups.flat.Content.GetValue t
            let contacts = List<ContactTopology.Contact>()
            let labels = List<ContactTopology.Label>()
            for id in ids do
                match HashMap.tryFind id flat with
                | Some (AdaptiveAnnotations a) ->
                    let g = a.geometry.GetValue t
                    if isContact g then
                        let pts = Sg.getPolylinePointsAt a t
                        // a closed polygon contact: the topology wants the closing point
                        let pts =
                            if g = Geometry.Polygon && pts.Length > 2 && pts.[0] <> pts.[pts.Length - 1]
                            then Array.append pts [| pts.[0] |]
                            else pts
                        if pts.Length >= 2 then contacts.Add { id = a.key; points = pts }
                    elif g = Geometry.Point then
                        match a.geoUnit.GetValue t with
                        | Some u ->
                            match a.points.Content.GetValue t |> IndexList.tryFirst with
                            | Some p -> labels.Add { id = a.key; position = p; unit = u }
                            | None -> ()
                        | None -> ()
                | _ -> ()

            if contacts.Count = 0 && labels.Count = 0 then emptyDerived
            else
            let samples =
                Array.append
                    (contacts |> Seq.collect (fun c -> c.points) |> Seq.toArray)
                    (labels |> Seq.map (fun l -> l.position) |> Seq.toArray)
            // draped polylines of two contacts never meet exactly: a fraction of the sample spacing
            let tolerance =
                let mutable acc = 0.0
                let mutable n = 0
                for c in contacts do
                    for i in 0 .. c.points.Length - 2 do
                        acc <- acc + Vec.Distance(c.points.[i], c.points.[i + 1])
                        n <- n + 1
                if n = 0 then 0.01 else max 1e-4 (0.3 * acc / float n)
            let sw = Diagnostics.Stopwatch.StartNew()
            let topology = ContactTopology.compute center tolerance (contacts.ToArray()) (labels.ToArray())
            Log.line "[GeologicalMap] %d contacts, %d labels: %d units, %d open ends in %.1f ms"
                contacts.Count labels.Count topology.faces.Length topology.openEnds.Length sw.Elapsed.TotalMilliseconds
            { topology   = topology
              samples    = samples
              labelUnits = labels |> Seq.map (fun l -> l.unit) |> Seq.distinct |> Seq.toList
              contacts   = contacts.Count
              labels     = labels.Count })

    // shared between the 3D view and the panel, so the topology is computed once per change;
    // the table holds each entry as long as its map's adaptive object lives
    let private derivedCache = ConditionalWeakTable<AdaptiveGeologicalMap, aval<Derived>>()

    let derived (groups : AdaptiveGroupsModel) (map : AdaptiveGeologicalMap) : aval<Derived> =
        lock derivedCache (fun () ->
            match derivedCache.TryGetValue map with
            | true, d -> d
            | _ ->
                let d = derive groups map
                derivedCache.Add(map, d)
                d)

    // -----------------------------------------------------------------------------------------
    // rendering
    // -----------------------------------------------------------------------------------------

    /// One face's volume and what colours it.
    type private FaceMesh = { unit : ContactTopology.FaceUnit; volume : V3d[] }

    /// The volumes of a map's units. Depends on the topology and the reach only.
    let private meshes (planet : aval<Planet>) (reach : aval<FillReach>) (derived : aval<Derived>) : aval<FaceMesh[]> =
        AVal.custom (fun t ->
            let d = derived.GetValue t
            let planet = planet.GetValue t
            let reach = reach.GetValue t
            let size = if d.samples.Length = 0 then 0.0 else Box3d(d.samples).Size.Length
            let reach = FillReach.resolve planet reach size
            match MapShell.ofPoints center reach d.samples with
            | None -> [||]
            | Some shell ->
                if d.topology.faces.Length = 0 then
                    // no closed unit yet: the labels colour the whole body
                    let unit =
                        match d.labelUnits with
                        | []  -> None
                        | [u] -> Some (ContactTopology.FaceUnit.Unit u)
                        | us  -> Some (ContactTopology.FaceUnit.Conflict us)
                    match unit with
                    | None -> [||]
                    | Some u -> [| { unit = u; volume = (MapShell.wholeBody shell).volume } |]
                else
                    d.topology.faces |> Array.choose (fun f ->
                        MapShell.tryFace shell f.cycles
                        |> Option.map (fun v -> { unit = f.unit; volume = v.volume })))

    let private unlabelled = C4f(0.55f, 0.55f, 0.55f, 1.0f)
    let private conflict   = C4f(1.0f, 0.0f, 1.0f, 1.0f)

    /// All visible maps' units as one fill geometry (PackedRendering.DeferredFills).
    let private fillGeometry
        (planet : aval<Planet>) (reach : aval<FillReach>)
        (groups : AdaptiveGroupsModel) (maps : AdaptiveGeologicalMapsModel) (view : aval<M44d>)
        : PackedRendering.FillGeometry =

        let perMap =
            maps.maps |> AList.map (fun m -> m, meshes planet reach (derived groups m))

        let data =
            AVal.custom (fun t ->
                let positions = List<V3f>()
                let colors = List<C4f>()
                let mutable pivot = None
                for (m, meshes) in perMap.Content.GetValue t do
                    if m.visible.GetValue t then
                        let opacity = m.opacity.value.GetValue t |> float32
                        let palette = Dictionary<string, C4f>()
                        for u in m.units.Content.GetValue t do
                            palette.[u.code.GetValue t] <- (u.color.c.GetValue t).ToC4f()
                        for f in meshes.GetValue t do
                            let rgb, a =
                                match f.unit with
                                | ContactTopology.FaceUnit.Unit code ->
                                    match palette.TryGetValue code with
                                    // a code without a legend entry: shown, but greyed
                                    | true, c -> c, opacity
                                    | _ -> unlabelled, opacity
                                | ContactTopology.FaceUnit.Unlabelled -> unlabelled, opacity * 0.5f
                                | ContactTopology.FaceUnit.Conflict _ -> conflict, opacity
                            let color = C4f(rgb.R, rgb.G, rgb.B, a)
                            if f.volume.Length > 0 then
                                let origin =
                                    match pivot with
                                    | Some p -> p
                                    | None -> pivot <- Some f.volume.[0]; f.volume.[0]
                                for p in f.volume do
                                    positions.Add(V3f(p - origin))
                                    colors.Add color
                {| positions = positions.ToArray()
                   colors    = colors.ToArray()
                   model     = pivot |> Option.map Trafo3d.Translation |> Option.defaultValue Trafo3d.Identity |})

        let mv = (data, view) ||> AVal.map2 (fun d v -> v * d.model.Forward)
        let positions = data |> AVal.map (fun d -> d.positions)
        let colors = data |> AVal.map (fun d -> d.colors)
        {
            caps        = Sg.empty
            withVolumes = fun sg ->
                sg
                |> Sg.vertexAttribute DefaultSemantic.Positions positions
                |> Sg.vertexAttribute DefaultSemantic.Colors colors
                |> Sg.uniform "MV" mv
            ranges      =
                data |> AVal.map (fun d ->
                    if d.positions.Length = 0 then [||]
                    else [| DrawCallInfo(FaceVertexCount = d.positions.Length, InstanceCount = 1) |])
        }

    /// The maps' units over the terrain. Only in views that render their surfaces offscreen;
    /// the Snapshots path (no surface depth) shows no maps yet.
    let render
        (runtime : IRuntime) (surfaceDepth : Option<OffscreenSurfaces.SurfaceDepth>)
        (planet : aval<Planet>) (reach : aval<FillReach>)
        (groups : AdaptiveGroupsModel) (maps : AdaptiveGeologicalMapsModel) (view : aval<M44d>) : ISg<'msg> =
        match surfaceDepth with
        | None -> Sg.empty
        | Some s ->
            fillGeometry planet reach groups maps view
            |> PackedRendering.DeferredFills.renderOn runtime s
        |> Sg.noEvents

    // -----------------------------------------------------------------------------------------
    // panel
    // -----------------------------------------------------------------------------------------

    module UI =

        let private summary (d : Derived) =
            let t = d.topology
            let count p = t.faces |> Array.filter (fun f -> p f.unit) |> Array.length
            let unlabelled = count (function ContactTopology.FaceUnit.Unlabelled -> true | _ -> false)
            let conflicts = count (function ContactTopology.FaceUnit.Conflict _ -> true | _ -> false)
            [
                sprintf "%d contacts, %d label points" d.contacts d.labels
                sprintf "%d units" t.faces.Length
                if unlabelled > 0 then sprintf "%d without a label" unlabelled
                if conflicts > 0 then sprintf "%d with conflicting labels" conflicts
                if t.openEnds.Length > 0 then sprintf "%d open contact ends" t.openEnds.Length
                if not t.eulerOk then "topology check failed"
            ]

        let private problemsView (groups : AdaptiveGroupsModel) (m : AdaptiveGeologicalMap) =
            let lines = derived groups m |> AVal.map summary
            Incremental.div (AttributeMap.ofList [clazz "ui list"]) (
                lines |> AList.ofAVal |> AList.map (fun l -> div [clazz "item"] [text l]))

        let private unitView (mapKey : Guid) (active : aval<Option<Guid>>) (u : AdaptiveGeoUnit) =
            let isActive = active |> AVal.map (fun a -> a = Some u.id)
            tr [] [
                td [] [
                    ColorPicker.view u.color |> UI.map (fun a -> Action.SetUnitColor(mapKey, u.id, a))
                ]
                td [] [Html.SemUi.textBox u.code (fun c -> Action.SetUnitCode(mapKey, u.id, c))]
                td [] [Html.SemUi.textBox u.name (fun n -> Action.SetUnitName(mapKey, u.id, n))]
                td [] [
                    Incremental.i
                        (AttributeMap.ofAMap <| amap {
                            yield onClick (fun _ -> Action.SetActiveUnit u.id)
                            let! a = isActive
                            yield clazz (if a then "green check circle icon" else "circle outline icon")
                        })
                        AList.empty
                    |> UI.wrapToolTip DataPosition.Bottom "Active unit: Assign gives it to the selected label points"
                ]
                td [] [
                    i [clazz "trash alternate icon"; onClick (fun _ -> Action.RemoveUnit(mapKey, u.id))] []
                    |> UI.wrapToolTip DataPosition.Bottom "Remove from the legend (label points keep their code)"
                ]
            ]

        let private mapView (groups : AdaptiveGroupsModel) (model : AdaptiveGeologicalMapsModel) (m : AdaptiveGeologicalMap) =
            let isActive = model.active |> AVal.map (fun a -> a = Some m.key)
            let groupName =
                AVal.custom (fun t ->
                    match findGroup groups.rootGroup (m.sourceGroup.GetValue t) t with
                    | Some n -> n.name.GetValue t
                    | None -> "(group missing)")
            div [clazz "ui segment"; style "padding: 6px; margin: 4px 0"] [
                Html.table [
                    Html.row "Map:" [
                        Incremental.i
                            (AttributeMap.ofAMap <| amap {
                                yield onClick (fun _ -> Action.SetActive m.key)
                                let! a = isActive
                                yield clazz (if a then "green dot circle icon" else "circle outline icon")
                            })
                            AList.empty
                        |> UI.wrapToolTip DataPosition.Bottom "Active map"
                        Html.SemUi.textBox m.name (fun n -> Action.SetName(m.key, n))
                    ]
                    Html.row "Group:" [Incremental.text groupName]
                    Html.row "Visible:" [GuiEx.iconCheckBoxSet m.visible (fun v -> Action.SetVisible(m.key, v))]
                    Html.row "Opacity:" [Numeric.view' [Slider; InputBox] m.opacity |> UI.map (fun a -> Action.SetOpacity(m.key, a))]
                    Html.row "Status:" [problemsView groups m]
                ]
                h5 [clazz "ui header"; style "margin: 6px 0"] [text "Legend"]
                table [clazz "ui compact unstackable inverted table"] [
                    Incremental.tbody AttributeMap.empty (m.units |> AList.map (unitView m.key model.activeUnit))
                ]
                div [] [
                    button [clazz "ui tiny button"; onClick (fun _ -> Action.AddUnit m.key)] [text "New unit"]
                    button [clazz "ui tiny button"; onClick (fun _ -> Action.AssignToSelected)] [text "Assign to selected points"]
                    |> UI.wrapToolTip DataPosition.Bottom "Gives the active unit to the selected point annotations"
                    button [clazz "ui tiny red basic button"; onClick (fun _ -> Action.RemoveMap m.key)] [text "Remove map"]
                    |> UI.wrapToolTip DataPosition.Bottom "Removes the map only; its contacts and label points stay"
                ]
            ]

        /// The Geological maps panel. `activeGroup` is the annotation group a new map is made of.
        let view (groups : AdaptiveGroupsModel) (model : AdaptiveGeologicalMapsModel) =
            let activeGroup = groups.activeGroup
            require GuiEx.semui (
                div [style "padding: 4px"] [
                    Incremental.div AttributeMap.empty (
                        alist {
                            let! g = activeGroup
                            yield
                                button [clazz "ui small button"; onClick (fun _ -> Action.NewMap(g.id, g.name))] [
                                    text (sprintf "New map from group '%s'" g.name)
                                ]
                                |> UI.wrapToolTip DataPosition.Bottom
                                    "Contacts: the polylines and polygons of the active annotation group. Label points: its points with a Geo Unit."
                        })
                    Incremental.div AttributeMap.empty (model.maps |> AList.map (mapView groups model))
                ])
