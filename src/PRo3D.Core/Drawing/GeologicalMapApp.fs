namespace PRo3D.Core.Drawing

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

open Aardvark.Base
open Aardvark.Rendering
// Aardvark.SceneGraph before Aardvark.UI: Sg.* then means aardvark.media's message-carrying
// ISg<'msg> functions, falling back to Aardvark.SceneGraph's only for what those lack
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
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

    /// A label point: its unit, with the code written next to it.
    let withUnit (code : string) (a : Annotation) =
        { a with geoUnit = Some code; text = code; showText = true }

    /// A new label point at a terrain position.
    let labelPoint (code : string) (position : V3d) =
        let a = Annotation.make Projection.Linear None Geometry.Point None { c = C4b.White } Annotation.Initial.thickness ""
        { a with points = IndexList.single position; modelTrafo = Trafo3d.Translation position } |> withUnit code

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
        | Action.AssignToSelected
        | Action.AssignAt _
        | Action.FlyTo _ -> m
        // the viewer flies and sets the cursor (it needs the camera)
        | Action.NextProblem -> m
        | Action.PickUnit (key, code) ->
            match m.maps |> IndexList.toArray |> Array.tryFind (fun x -> x.key = key) with
            | Some map ->
                match map.units |> IndexList.toArray |> Array.tryFind (fun u -> u.code = code) with
                | Some u -> { m with activeUnit = Some u.id }
                | None -> m
            | None -> m

    // -----------------------------------------------------------------------------------------
    // the group's annotations -> topology
    // -----------------------------------------------------------------------------------------

    /// Annotations that count as contacts. Any line in the map's group does.
    let isContact (g : Geometry) =
        match g with
        | Geometry.Polyline | Geometry.Polygon | Geometry.Line -> true
        | _ -> false

    /// What a map is computed from: the contacts and label points of its group.
    type Sources =
        {
            contacts  : ContactTopology.Contact[]
            labels    : ContactTopology.Label[]
            /// draped polylines of two contacts never meet exactly: a fraction of the spacing
            tolerance : float
        }

    /// The faces of the contacts, before labels.
    type Built =
        {
            network  : ContactTopology.Network
            /// every contact sample: the terrain the shell must enclose
            samples  : V3d[]
        }

    type Derived =
        {
            built        : Built
            /// the faces with their units. With no closed face yet but labels, a single face
            /// without boundary - the whole body
            topology     : ContactTopology.Result
            contacts     : int
            labels       : int
            /// label positions, for the shell when there are no contacts
            labelSamples : V3d[]
            labelPoints  : ContactTopology.Label[]
        }

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

    /// The sampled lines of the active map's contacts, while a contact is being drawn into that
    /// map (its group is the active group and the drawing geometry is a line); otherwise none.
    /// What a new contact point snaps to (ContactSnap).
    let snapTargets (groups : GroupsModel) (maps : GeologicalMapsModel) (drawingGeometry : Geometry) : V3d[][] =
        match GeologicalMapsModel.tryActive maps with
        | Some map when groups.activeGroup.id = map.sourceGroup && isContact drawingGeometry ->
            leafIdsOf groups.rootGroup map.sourceGroup |> Array.choose (fun id ->
                match HashMap.tryFind id groups.flat with
                | Some (Leaf.Annotations a) when isContact a.geometry ->
                    let pts = Annotation.retrievePoints a |> Array.ofList
                    let pts =
                        if a.geometry = Geometry.Polygon && pts.Length > 2 then Array.append pts [| pts.[0] |]
                        else pts
                    if pts.Length >= 2 then Some pts else None
                | _ -> None)
        | _ -> [||]

    /// How close to a contact (screen pixels) a new contact point snaps onto it.
    [<Literal>]
    let SnapPixels = 10.0

    /// A point picked while drawing: onto the nearest contact of the active map within
    /// SnapPixels (seen from `eye`, a view of `hfov` radians over `width` pixels), when a
    /// contact is being drawn into that map; otherwise unchanged.
    let snap (drawing : DrawingModel) (eye : V3d) (hfov : float) (width : int) (p : V3d) : V3d =
        match snapTargets drawing.annotations drawing.geologicalMaps drawing.geometry with
        | [||] -> p
        | targets ->
            let radius = ContactSnap.radiusOf SnapPixels hfov width
            // the snap point must lie near the picked terrain point too, not just look near it
            let reach = 4.0 * radius * Vec.Distance(p, eye)
            match ContactSnap.tryPoint eye radius reach targets p with
            | Some q ->
                Log.line "[GeologicalMap] snapped onto a contact (%.3f m away)" (Vec.Distance(p, q))
                q
            | None -> p

    /// The body centre: annotation coordinates are body-fixed, centred on the body.
    let center = V3d.Zero

    let private sources (groups : AdaptiveGroupsModel) (map : AdaptiveGeologicalMap) : aval<Sources> =
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
            let tolerance =
                let mutable acc = 0.0
                let mutable n = 0
                for c in contacts do
                    for i in 0 .. c.points.Length - 2 do
                        acc <- acc + Vec.Distance(c.points.[i], c.points.[i + 1])
                        n <- n + 1
                if n = 0 then 0.01 else max 1e-4 (0.3 * acc / float n)
            { contacts = contacts.ToArray(); labels = labels.ToArray(); tolerance = tolerance })

    /// The network, recomputed only when the contacts change. Every annotation change in the
    /// scene re-evaluates this node (it reads the whole annotation map); unchanged contacts
    /// return the previous network, the same object, which is what lets everything after it
    /// (volumes, vertex buffers) skip work when only labels change.
    let private build (sources : aval<Sources>) : aval<Built> =
        let mutable last : Option<ContactTopology.Contact[] * float * Built> = None
        AVal.custom (fun t ->
            let s = sources.GetValue t
            match last with
            | Some (c, tol, b) when tol = s.tolerance && c = s.contacts -> b
            | _ ->
                let sw = Diagnostics.Stopwatch.StartNew()
                let network = ContactTopology.build center s.tolerance s.contacts
                Log.line "[GeologicalMap] %d contacts: %d faces, %d open ends in %.1f ms"
                    s.contacts.Length network.faces.Length network.openEnds.Length sw.Elapsed.TotalMilliseconds
                let b = { network = network; samples = s.contacts |> Array.collect (fun c -> c.points) }
                last <- Some (s.contacts, s.tolerance, b)
                b)

    let private derive (groups : AdaptiveGroupsModel) (map : AdaptiveGeologicalMap) : aval<Derived> =
        let sources = sources groups map
        let built = build sources
        (sources, built) ||> AVal.map2 (fun s b ->
            let topology : ContactTopology.Result =
                if b.network.faces.Length = 0 && s.labels.Length > 0 then
                    // no closed face yet: the labels colour the whole body
                    let units = s.labels |> Array.map (fun l -> l.unit) |> Array.distinct |> List.ofArray
                    let whole : ContactTopology.Face =
                        { id = 0; cycles = [||]; sources = [||]
                          labels = s.labels |> Array.map (fun l -> l.id) |> List.ofArray
                          unit =
                            match units with
                            | [u] -> ContactTopology.FaceUnit.Unit u
                            | us  -> ContactTopology.FaceUnit.Conflict us }
                    { faces = [| whole |]; openEnds = b.network.openEnds; eulerOk = b.network.eulerOk
                      labelFace = s.labels |> Array.map (fun l -> l.id, 0) |> Map.ofArray }
                else ContactTopology.label b.network s.labels
            { built = b; topology = topology; contacts = s.contacts.Length; labels = s.labels.Length
              labelSamples = s.labels |> Array.map (fun l -> l.position)
              labelPoints  = s.labels })

    /// The same sources from the plain model: for actions computed in update (NextProblem).
    let sourcesOf (groups : GroupsModel) (map : GeologicalMap) : Sources =
        let contacts = List<ContactTopology.Contact>()
        let labels = List<ContactTopology.Label>()
        for id in leafIdsOf groups.rootGroup map.sourceGroup do
            match HashMap.tryFind id groups.flat with
            | Some (Leaf.Annotations a) ->
                if isContact a.geometry then
                    let pts = Annotation.retrievePoints a |> Array.ofList
                    let pts =
                        if a.geometry = Geometry.Polygon && pts.Length > 2 && pts.[0] <> pts.[pts.Length - 1]
                        then Array.append pts [| pts.[0] |]
                        else pts
                    if pts.Length >= 2 then contacts.Add { id = a.key; points = pts }
                elif a.geometry = Geometry.Point then
                    match a.geoUnit, IndexList.tryFirst a.points with
                    | Some u, Some p -> labels.Add { id = a.key; position = p; unit = u }
                    | _ -> ()
            | _ -> ()
        let tolerance =
            let mutable acc = 0.0
            let mutable n = 0
            for c in contacts do
                for i in 0 .. c.points.Length - 2 do
                    acc <- acc + Vec.Distance(c.points.[i], c.points.[i + 1])
                    n <- n + 1
            if n = 0 then 0.01 else max 1e-4 (0.3 * acc / float n)
        { contacts = contacts.ToArray(); labels = labels.ToArray(); tolerance = tolerance }

    [<RequireQualifiedAccess>]
    type ProblemKind =
        | Unlabelled
        | Conflict
        | OpenEnd

    /// Something in a map that needs attention, and where it is.
    type Problem = { kind : ProblemKind; position : V3d; text : string }

    /// A point inside (or at least on) a face, to fly to: the mean direction of its first
    /// boundary at their mean radius.
    let private facePoint (f : ContactTopology.Face) =
        match f.cycles |> Array.tryHead with
        | Some c when c.Length > 0 ->
            let mutable d = V3d.Zero
            let mutable r = 0.0
            for p in c do
                d <- d + (p - center).Normalized
                r <- r + (p - center).Length
            if d.Length > 1e-9 then Some (center + d.Normalized * (r / float c.Length))
            else Some c.[0]
        | _ -> None

    /// The problems of a labelled topology, in a fixed order: unlabelled units, conflicts,
    /// open contact ends.
    let problems (topology : ContactTopology.Result) (labels : ContactTopology.Label[]) : Problem[] =
        let labelPos = labels |> Array.map (fun l -> l.id, l.position) |> Map.ofArray
        [|
            for f in topology.faces do
                match f.unit with
                | ContactTopology.FaceUnit.Unlabelled ->
                    match facePoint f with
                    | Some p -> yield { kind = ProblemKind.Unlabelled; position = p; text = sprintf "unlabelled unit (#%d)" f.id }
                    | None -> ()
                | _ -> ()
            for f in topology.faces do
                match f.unit with
                | ContactTopology.FaceUnit.Conflict us ->
                    let p = f.labels |> List.tryPick (fun l -> Map.tryFind l labelPos) |> Option.orElse (facePoint f)
                    match p with
                    | Some p -> yield { kind = ProblemKind.Conflict; position = p; text = sprintf "conflicting labels %s (#%d)" (String.concat ", " us) f.id }
                    | None -> ()
                | _ -> ()
            for i in 0 .. topology.openEnds.Length - 1 do
                yield { kind = ProblemKind.OpenEnd; position = topology.openEnds.[i]; text = sprintf "open contact end %d" (i + 1) }
        |]

    /// The problems of a map, computed from the plain model (NextProblem; ~the cost of one
    /// topology build).
    let problemsOf (groups : GroupsModel) (map : GeologicalMap) : Problem[] =
        let s = sourcesOf groups map
        let t = ContactTopology.compute center s.tolerance s.contacts s.labels
        problems t s.labels

    // shared between the 3D views and the panel, so the topology is computed once per change;
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

    /// The volumes of all faces in one buffer, with each vertex's face id.
    type private MapGeometry =
        {
            positions : V3f[]
            faceIds   : float32[]
            model     : Trafo3d
            /// where events are picked: the shell's bounds
            bounds    : Box3d
        }

    let private emptyGeometry = { positions = [||]; faceIds = [||]; model = Trafo3d.Identity; bounds = Box3d.Invalid }

    /// The units' volumes. Rebuilt only when the network, the reach or - for a map without
    /// contacts - the labels change; a label change alone returns the previous object.
    let private geometry (planet : aval<Planet>) (reach : aval<FillReach>) (derived : aval<Derived>) : aval<MapGeometry> =
        let mutable last : Option<obj * Planet * FillReach * V3d[] * int * MapGeometry> = None
        AVal.custom (fun t ->
            let d = derived.GetValue t
            let planet = planet.GetValue t
            let reach = reach.GetValue t
            // the shell encloses the contacts; with none, the label points
            let shellPoints = if d.built.samples.Length > 0 then [||] else d.labelSamples
            let faceCount = d.topology.faces.Length
            match last with
            | Some (b, p, r, sp, n, g) when obj.ReferenceEquals(b, d.built) && p = planet && r = reach && sp = shellPoints && n = faceCount -> g
            | _ ->
                let samples = if d.built.samples.Length > 0 then d.built.samples else d.labelSamples
                let size = if samples.Length = 0 then 0.0 else Box3d(samples).Size.Length
                let reachM = FillReach.resolve planet reach size
                let g =
                    match MapShell.ofPoints center reachM samples with
                    | None -> emptyGeometry
                    | Some shell ->
                        let volumes =
                            d.topology.faces |> Array.choose (fun f ->
                                if f.cycles.Length = 0 then Some (f.id, (MapShell.wholeBody shell).volume)
                                else MapShell.tryFace shell f.cycles |> Option.map (fun v -> f.id, v.volume))
                        let count = volumes |> Array.sumBy (fun (_, v) -> v.Length)
                        let positions = Array.zeroCreate count
                        let ids = Array.zeroCreate count
                        // world points shifted by a pivot purely to keep the float32 buffer precise
                        let origin = volumes |> Array.tryPick (fun (_, v) -> Array.tryHead v) |> Option.defaultValue V3d.Zero
                        let mutable i = 0
                        for (id, v) in volumes do
                            for p in v do
                                positions.[i] <- V3f(p - origin)
                                ids.[i] <- float32 id
                                i <- i + 1
                        { positions = positions; faceIds = ids; model = Trafo3d.Translation origin
                          bounds = Box3d(shell.center - V3d(shell.outer), shell.center + V3d(shell.outer)) }
                last <- Some (d.built :> obj, planet, reach, shellPoints, faceCount, g)
                g)

    /// Width of the face table texture.
    [<Literal>]
    let private TableWidth = 1024

    let private unlabelledColor = C4f(0.55f, 0.55f, 0.55f, 1.0f)
    let private conflictColor   = C4f(1.0f, 0.0f, 1.0f, 1.0f)

    /// Per face: colour, and opacity in alpha - negative for a unit drawn hatched (no label).
    /// The only thing that changes when a unit is assigned.
    let private faceTable (map : AdaptiveGeologicalMap) (derived : aval<Derived>) : aval<ITexture> =
        AVal.custom (fun t ->
            let d = derived.GetValue t
            let opacity = map.opacity.value.GetValue t |> float32
            let palette = Dictionary<string, C4f>()
            for u in map.units.Content.GetValue t do
                palette.[u.code.GetValue t] <- (u.color.c.GetValue t).ToC4f()
            let n = max 1 d.topology.faces.Length
            let height = (n + TableWidth - 1) / TableWidth
            let img = PixImage<float32>(Col.Format.RGBA, V2i(TableWidth, height))
            let mutable m = img.GetMatrix<C4f>()
            for f in d.topology.faces do
                let c =
                    match f.unit with
                    | ContactTopology.FaceUnit.Unit code ->
                        match palette.TryGetValue code with
                        | true, c -> C4f(c.R, c.G, c.B, opacity)
                        // a code without a legend entry: grey, but not hatched - it has a label
                        | _ -> C4f(unlabelledColor.R, unlabelledColor.G, unlabelledColor.B, opacity)
                    | ContactTopology.FaceUnit.Unlabelled -> C4f(unlabelledColor.R, unlabelledColor.G, unlabelledColor.B, -opacity)
                    | ContactTopology.FaceUnit.Conflict _ -> C4f(conflictColor.R, conflictColor.G, conflictColor.B, opacity)
                m.[int64 (f.id % TableWidth), int64 (f.id / TableWidth)] <- c
            PixTexture2d(PixImageMipMap [| img :> PixImage |], false) :> ITexture)

    module private Shader =
        open FShade

        let private terrainDepth =
            sampler2dMS {
                texture uniform?TerrainDepth
                filter Filter.MinMagPoint
            }

        let private ids =
            sampler2d {
                texture uniform?MapIds
                filter Filter.MinMagPoint
            }

        let private table =
            sampler2d {
                texture uniform?FaceTable
                filter Filter.MinMagPoint
            }

        type Vertex =
            {
                [<Position>]           pos   : V4f
                [<Semantic("FaceId")>] fid   : float32
                [<FragCoord>]          fc    : V4f
                [<FrontFacing>]        front : bool
            }

        let vertex (v : Vertex) =
            vertex {
                let mv : M44f = uniform?MV
                return { v with pos = uniform.ProjTrafo * (mv * v.pos) }
            }

        /// As DeferredFills.accumulate, but summing face id, terrain depth and coverage: a
        /// closed volume's faces behind the terrain cancel unless the terrain lies inside it,
        /// so each sum is +-(value of the one face containing the terrain), or 0.
        let accumulate (v : Vertex) =
            fragment {
                let terrain = terrainDepth.Read(V2i v.fc.XY, 0).X
                if terrain >= 1.0f then discard()
                let z = clamp 0.0f 1.0f v.fc.Z
                if z <= terrain then discard()
                let w = if v.front then -1.0f else 1.0f
                return V4f(w * (v.fid + 1.0f), w * terrain, w, 0.0f)
            }

        type CompositeVertex =
            {
                [<Position>]  pos : V4f
                [<FragCoord>] fc  : V4f
                [<Color>]     c   : V4f
            }

        let composite (v : CompositeVertex) =
            fragment {
                let vp : V2i = uniform?ViewportSize
                let s = ids.Read(V2i (v.fc.XY * V2f ids.Size / V2f vp), 0)
                let cover = abs s.Z
                if cover < 0.5f then discard()
                let id = int (abs s.X / cover + 0.5f) - 1
                let tw : int = uniform?TableWidth
                let c = table.Read(V2i(id % tw, id / tw), 0)
                let hovered : int = uniform?HoveredFace
                let a = abs c.W
                // unlabelled: screen-space stripes
                let stripe = c.W < 0.0f && ((int (v.fc.X + v.fc.Y) / 6) % 2 = 0)
                let rgb = if stripe then c.XYZ * 0.35f else c.XYZ
                let lit = id = hovered
                let rgb = if lit then rgb * 0.6f + V3f(0.4f, 0.4f, 0.4f) else rgb
                let a = if lit then min 1.0f (a + 0.25f) else a
                return V4f(rgb, a)
            }

    module private MarkerShader =
        open FShade

        let private terrainDepth =
            sampler2dMS {
                texture uniform?TerrainDepth
                filter Filter.MinMagPoint
            }

        type Vertex =
            {
                [<Position>]   pos  : V4f
                [<PointSize>]  size : float32
                [<PointCoord>] pc   : V2f
            }

        /// A round point, hidden where the terrain lies in front of it - read from the
        /// surfaces' depth, so a marker on the far side of the body does not shine through.
        let vertex (v : Vertex) =
            vertex {
                let mv : M44f = uniform?MV
                let p = uniform.ProjTrafo * (mv * v.pos)
                let ndc = p.XYZ / p.W
                let size = V2f terrainDepth.Size
                let px = V2i (clamp (V2f.Zero) (size - V2f.II) ((ndc.XY * 0.5f + V2f(0.5f, 0.5f)) * size))
                let terrain = terrainDepth.Read(px, 0).X
                let z = ndc.Z * 0.5f + 0.5f
                let hidden = z > terrain + 0.0002f
                return { v with pos = (if hidden then V4f(2.0f, 2.0f, 2.0f, 1.0f) else p); size = 12.0f }
            }

        let fragment (v : Vertex) =
            fragment {
                let d = v.pc - V2f(0.5f, 0.5f)
                if d.Length > 0.5f then discard()
                let edge = d.Length > 0.35f
                return (if edge then V4f(1.0f, 1.0f, 1.0f, 1.0f) else V4f(0.9f, 0.1f, 0.1f, 1.0f))
            }

    /// What the Assign unit tool needs from the viewer: whether it is the active tool, and
    /// whether the left button belongs to it right now (ViewerUtils.toolArmed).
    type AssignGate = { active : aval<bool>; armed : aval<bool> }

    /// One map: its units' volumes into a face-id target against the surface depth, then laid
    /// over the terrain from the face table. Events (the Assign unit tool) read the face id and
    /// the terrain depth back from the same target - no KdTree, no ray cast.
    let private mapSg
        (runtime : IRuntime) (surfaces : OffscreenSurfaces.SurfaceDepth)
        (planet : aval<Planet>) (reach : aval<FillReach>)
        (groups : AdaptiveGroupsModel) (maps : AdaptiveGeologicalMapsModel) (gate : AssignGate)
        (view : aval<M44d>) (map : AdaptiveGeologicalMap) : ISg<Action> =

        let derived = derived groups map
        let geometry = geometry planet reach derived
        let mv = (geometry, view) ||> AVal.map2 (fun g v -> v * g.model.Forward)

        let accumulation =
            let signature () = runtime.CreateFramebufferSignature [ DefaultSemantic.Colors, TextureFormat.Rgba32f ]
            let sg : ISg<unit> =
                Sg.draw IndexedGeometryMode.TriangleList
                |> Sg.vertexAttribute DefaultSemantic.Positions (geometry |> AVal.map (fun g -> g.positions))
                |> Sg.vertexAttribute (Sym.ofString "FaceId") (geometry |> AVal.map (fun g -> g.faceIds))
                |> Sg.uniform "MV" mv
                |> Sg.shader {
                    do! Shader.vertex
                    do! Shader.accumulate
                }
                |> Sg.texture "TerrainDepth" surfaces.depth
                |> Sg.cullMode (AVal.constant CullMode.None)
                |> Sg.depthClamp' true
                |> Sg.depthTest (AVal.constant DepthTest.None)
                |> Sg.blendMode (AVal.constant (BlendMode.simple BlendFactor.One BlendFactor.One))
                |> Sg.viewTrafo surfaces.viewTrafo
                |> Sg.projTrafo surfaces.projTrafo
            OffscreenSurfaces.renderTargets runtime signature (sg :> Aardvark.SceneGraph.ISg)
                (RenderTask.renderToColorWithClear surfaces.size (clear { color C4f.Zero }))
                (fun c -> c.Acquire())
                (fun c -> c.Release())
                (fun c t rt -> c.GetValue(t, rt))

        // open contact ends, marked in red; lifted a little off the terrain so the depth test
        // against it does not swallow them
        let markers =
            derived |> AVal.map (fun d ->
                let ends = d.topology.openEnds
                let origin = ends |> Array.tryHead |> Option.defaultValue V3d.Zero
                let lift (p : V3d) = center + (p - center) * 1.003
                ends |> Array.map (fun p -> V3f(lift p - origin)), Trafo3d.Translation origin)
        let markerSg : ISg<Action> =
            Sg.draw IndexedGeometryMode.PointList
            |> Sg.vertexAttribute DefaultSemantic.Positions (markers |> AVal.map fst)
            |> Sg.uniform "MV" ((markers, view) ||> AVal.map2 (fun (_, t) v -> v * t.Forward))
            |> Sg.texture "TerrainDepth" surfaces.depth
            |> Sg.shader {
                do! MarkerShader.vertex
                do! MarkerShader.fragment
            }
            |> Sg.depthTest (AVal.constant DepthTest.None)
            |> Sg.onOff ((map.visible, markers) ||> AVal.map2 (fun v (m, _) -> v && m.Length > 0))

        let hovered = cval -1
        let isActive = (maps.active, gate.active) ||> AVal.map2 (fun a on -> on && a = Some map.key)

        /// The face id and terrain point under the cursor, from the face-id target.
        let read (evt : SceneEvent) : Option<int * V3d> =
            try
                let tex = accumulation.GetValue(AdaptiveToken.Top, RenderToken.Empty)
                let size = tex.Size.XY
                let vp = V2i(max 1 evt.evtViewport.X, max 1 evt.evtViewport.Y)
                let px =
                    V2i(clamp 0 (size.X - 1) (evt.evtPixel.X * size.X / vp.X),
                        clamp 0 (size.Y - 1) (evt.evtPixel.Y * size.Y / vp.Y))
                let img = runtime.Download(tex, 0, 0, Box2i.FromMinAndSize(px, V2i.II)) |> unbox<PixImage<float32>>
                let s = img.GetMatrix<C4f>().[0, 0]
                let cover = abs s.B
                if cover < 0.5f then None
                else
                    let id = int (abs s.R / cover + 0.5f) - 1
                    let depth = float (abs s.G / cover)
                    let ndc =
                        V3d(2.0 * (float evt.evtPixel.X + 0.5) / float vp.X - 1.0,
                            1.0 - 2.0 * (float evt.evtPixel.Y + 0.5) / float vp.Y,
                            2.0 * depth - 1.0)
                    let viewProj = evt.evtView * evt.evtProj
                    Some (id, viewProj.Backward.TransformPosProj ndc)
            with e ->
                Log.warn "[GeologicalMap] reading the face under the cursor failed: %s" e.Message
                None

        // drag-paint: the face last given the unit during this stroke
        let mutable painting = false
        let mutable painted = -1

        let assignAt (id : int) (p : V3d) =
            let d = derived.GetValue()
            match d.topology.faces |> Array.tryFind (fun f -> f.id = id) with
            | Some f -> Seq.singleton (Action.AssignAt(map.key, f.labels, p))
            | None -> Seq.empty

        let pickUnit (id : int) =
            let d = derived.GetValue()
            match d.topology.faces |> Array.tryFind (fun f -> f.id = id) |> Option.map (fun f -> f.unit) with
            | Some (ContactTopology.FaceUnit.Unit code) -> Seq.singleton (Action.PickUnit(map.key, code))
            | _ -> Seq.empty

        let setHovered (id : int) =
            if hovered.Value <> id then transact (fun () -> hovered.Value <- id)

        let events =
            [
                SceneEventKind.Move, fun (hit : SceneHit) ->
                    match read hit.event with
                    | Some (id, p) ->
                        setHovered id
                        if painting && id <> painted && gate.armed.GetValue() then
                            painted <- id
                            true, Seq.delay (fun () -> assignAt id p)
                        else true, Seq.empty
                    | None ->
                        setHovered -1
                        true, Seq.empty
                SceneEventKind.Down, fun (hit : SceneHit) ->
                    if hit.event.evtButtons <> Aardvark.Application.MouseButtons.Left || not (gate.armed.GetValue()) then
                        true, Seq.empty
                    else
                        match read hit.event with
                        | Some (id, _) when hit.event.evtAlt -> true, Seq.delay (fun () -> pickUnit id)
                        | Some (id, p) ->
                            painting <- true
                            painted <- id
                            true, Seq.delay (fun () -> assignAt id p)
                        | None -> true, Seq.empty
                SceneEventKind.Up, fun (_ : SceneHit) ->
                    painting <- false
                    painted <- -1
                    true, Seq.empty
                SceneEventKind.Leave, fun (_ : SceneHit) ->
                    setHovered -1
                    true, Seq.empty
            ]

        // only the active map, only while its tool is selected - otherwise the shell's box would
        // sit in front of the terrain and take every click from the other tools
        let pickShape =
            (isActive, geometry) ||> AVal.map2 (fun on g ->
                if on && g.bounds.IsValid then PickShape.Box g.bounds else PickShape.Box Box3d.Invalid)

        let shownHover = (isActive, hovered :> aval<_>) ||> AVal.map2 (fun on h -> if on then h else -1)

        Sg.fullScreenQuad
        |> Sg.noEvents
        |> Sg.texture "MapIds" accumulation
        |> Sg.texture "FaceTable" (faceTable map derived)
        |> Sg.uniform "TableWidth" (AVal.constant TableWidth)
        |> Sg.uniform "HoveredFace" shownHover
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! Shader.composite
        }
        |> Sg.viewTrafo' Trafo3d.Identity
        |> Sg.projTrafo' Trafo3d.Identity
        |> Sg.depthTest (AVal.constant DepthTest.None)
        |> Sg.blendMode (AVal.constant BlendMode.Blend)
        |> Sg.writeBuffers' (Set.ofList [WriteBuffer.Color DefaultSemantic.Colors])
        |> Sg.pass PackedRendering.fillPass
        |> Sg.onOff ((map.visible, geometry) ||> AVal.map2 (fun v g -> v && g.positions.Length > 0))
        |> Sg.pickable' pickShape
        |> Sg.withEvents events
        |> fun units -> Sg.ofList [ units; markerSg ]

    /// The maps' units over the terrain, one offscreen pass per visible map. Only in views that
    /// render their surfaces offscreen; the Snapshots path (no surface depth) shows no maps yet.
    let render
        (runtime : IRuntime) (surfaceDepth : Option<OffscreenSurfaces.SurfaceDepth>)
        (planet : aval<Planet>) (reach : aval<FillReach>) (gate : AssignGate)
        (groups : AdaptiveGroupsModel) (maps : AdaptiveGeologicalMapsModel) (view : aval<M44d>) : ISg<Action> =
        match surfaceDepth with
        | None -> Sg.empty
        | Some s ->
            maps.maps
            |> AList.toASet
            |> ASet.map (mapSg runtime s planet reach groups maps gate view)
            |> Sg.set

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
            let d = derived groups m
            let lines = d |> AVal.map summary
            // at most this many listed; N walks through all of them
            let shown = 40
            let list =
                d |> AVal.map (fun d -> problems d.topology d.labelPoints |> Array.truncate shown |> IndexList.ofArray)
            div [] [
                Incremental.div (AttributeMap.ofList [clazz "ui list"]) (
                    lines |> AList.ofAVal |> AList.map (fun l -> div [clazz "item"] [text l]))
                Incremental.div (AttributeMap.ofList [style "max-height: 12em; overflow-y: auto; text-align: left; margin-top: 4px"]) (
                    list |> AList.ofAVal |> AList.map (fun p ->
                        let icon =
                            match p.kind with
                            | ProblemKind.Unlabelled -> "grey square icon"
                            | ProblemKind.Conflict -> "purple exclamation triangle icon"
                            | ProblemKind.OpenEnd -> "red circle icon"
                        div [style "cursor: pointer; white-space: nowrap; padding: 1px 0; color: #ddd"
                             attribute "title" "fly there (N: next problem)"
                             onClick (fun _ -> Action.FlyTo p.position)] [
                            i [clazz icon] []
                            text p.text
                        ]))
            ]

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
