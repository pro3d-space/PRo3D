namespace PRo3D.Core

open System
open System.Collections.Generic
open System.Globalization
open System.IO

open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Base.Annotation

open FSharp.Data.Adaptive

/// Polygons from a plain CSV of body-fixed vertices, one row per vertex. See
/// docs/PolygonCsvImport.md for the format; in short:
///
///     polygon_id,x,y,z[,label,color,group]
///
/// Rows sharing a `polygon_id` form one polygon, vertices in row order. The same reader
/// takes PRo3D's own per-point CSV export (`key`, `pointIndex`, `x`/`y`/`z`) and the
/// output of `pro3d-tool unproject` (`x_m`/`y_m`/`z_m`, `status`), so image coordinates
/// reach the viewer by running unproject first.
///
/// The parser and the surface snap are pure (the ray cast is passed in), so both are
/// tested without a viewer or an OPC.
module PolygonCsvImporter =

    /// One polygon as read from the file, before it becomes an annotation.
    type PolygonRecord = {
        id       : string
        vertices : V3d[]
        label    : string
        color    : Option<C4b>
        group    : string
    }

    type ParseResult = {
        polygons : list<PolygonRecord>
        /// rows or polygons that were left out, and why; for the log and the notice
        warnings : list<string>
    }

    // ---------------------------------------------------------------- tokenising

    let private isBlankOrComment (line : string) =
        let t = line.Trim()
        t.Length = 0 || t.StartsWith "#"

    /// The separator a file uses: tab, semicolon or comma, in that order of preference,
    /// else runs of whitespace (`None`). Decided once, from the first data line.
    let private detectSeparator (line : string) : Option<char> =
        if   line.Contains "\t" then Some '\t'
        elif line.Contains ";"  then Some ';'
        elif line.Contains ","  then Some ','
        else None

    /// Splits one line, honouring double quotes (so a `"Crater, rim"` label or an
    /// `"[255, 0, 0, 255]"` colour survives a comma separator); `""` inside quotes is a quote.
    let splitLine (sep : Option<char>) (line : string) : string[] =
        match sep with
        | None -> line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
        | Some sep ->
            let cells = List<string>()
            let cur = Text.StringBuilder()
            let mutable quoted = false
            let mutable i = 0
            while i < line.Length do
                let c = line.[i]
                if quoted then
                    if c = '"' then
                        if i + 1 < line.Length && line.[i + 1] = '"' then
                            cur.Append '"' |> ignore
                            i <- i + 1
                        else quoted <- false
                    else cur.Append c |> ignore
                elif c = '"' then quoted <- true
                elif c = sep then
                    cells.Add(cur.ToString().Trim())
                    cur.Clear() |> ignore
                else cur.Append c |> ignore
                i <- i + 1
            cells.Add(cur.ToString().Trim())
            cells.ToArray()

    let private tryFloat (s : string) =
        match Double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v when Double.IsFinite v -> Some v
        | _ -> None

    /// `#RRGGBB`, `#RRGGBBAA`, or Aardvark's own `[r, g, b, a]` (what the export's `color`
    /// column holds). Anything else is no colour, and the default applies.
    let tryParseColor (s : string) : Option<C4b> =
        let s = s.Trim()
        let hex (i : int) = Byte.Parse(s.Substring(i, 2), NumberStyles.HexNumber)
        try
            if s.StartsWith "#" && (s.Length = 7 || s.Length = 9) then
                Some (C4b(hex 1, hex 3, hex 5, (if s.Length = 9 then hex 7 else 255uy)))
            elif s.StartsWith "[" then
                let parts =
                    s.Trim('[', ']').Split(',')
                    |> Array.choose (fun p ->
                        match Int32.TryParse(p.Trim()) with
                        | true, v when v >= 0 && v <= 255 -> Some (byte v)
                        | _ -> None)
                match parts with
                | [| r; g; b |]    -> Some (C4b(r, g, b, 255uy))
                | [| r; g; b; a |] -> Some (C4b(r, g, b, a))
                | _ -> None
            else None
        with _ -> None

    // ---------------------------------------------------------------- columns

    type private Columns = {
        id     : int
        x      : int
        y      : int
        z      : int
        order  : Option<int>
        label  : Option<int>
        color  : Option<int>
        group  : Option<int>
        status : Option<int>
        /// unproject output: the image a pixel was picked in; part of the polygon key
        image  : Option<int>
    }

    /// Without a header: id, x, y, z.
    let private positional = {
        id = 0; x = 1; y = 2; z = 3
        order = None; label = None; color = None; group = None; status = None; image = None
    }

    /// Column lookup by header name, case-insensitive; the first candidate present wins.
    /// `x_m` beats `x` because unproject output carries both, and there `x` is the pixel.
    let private fromHeader (header : string[]) : Result<Columns, string> =
        let names = header |> Array.map (fun h -> h.Trim().ToLowerInvariant())
        let find (candidates : list<string>) =
            candidates |> List.tryPick (fun c -> names |> Array.tryFindIndex ((=) c))
        let idCol = find [ "polygon_id"; "polygon"; "id"; "key"; "name" ]
        match idCol, find [ "x_m"; "x" ], find [ "y_m"; "y" ], find [ "z_m"; "z" ] with
        | Some id, Some x, Some y, Some z ->
            Result.Ok {
                id = id; x = x; y = y; z = z
                order  = find [ "pointindex"; "point_index"; "vertex"; "vertex_index" ]
                label  = find [ "label"; "text" ]
                color  = find [ "colorhex"; "color"; "colour" ]
                group  = find [ "group"; "grouppath"; "groupname" ]
                status = find [ "status" ]
                image  = find [ "image" ]
            }
        | None, _, _, _ ->
            Result.Error "no polygon id column (polygon_id, polygon, id, key or name)"
        | _ ->
            Result.Error "no x/y/z columns (x, y, z or x_m, y_m, z_m)"

    // ---------------------------------------------------------------- parsing

    let private cell (cells : string[]) (i : int) = if i < cells.Length then cells.[i] else ""
    let private optCell cells (i : Option<int>) = i |> Option.map (cell cells) |> Option.defaultValue ""

    let private readRows (sep : Option<char>) (cols : Columns) (rows : (int * string)[]) : ParseResult =
        let warnings = List<string>()
        // first-appearance order of polygons; vertices with their sort key and row number
        let order = List<string>()
        let byId = Dictionary<string, List<float * int * V3d>>()
        let meta = Dictionary<string, string * Option<C4b> * string>()

        for (lineNo, line) in rows do
            let cells = splitLine sep line
            let status = optCell cells cols.status
            if status <> "" && not (String.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) then
                warnings.Add(sprintf "line %d: status '%s', skipped" lineNo status)
            else
                // with an image column, polygon p in image a and p in image b are two polygons
                let id =
                    match cell cells cols.id, optCell cells cols.image with
                    | "", _ -> ""
                    | id, "" -> id
                    | id, image -> sprintf "%s @ %s" id image
                match id, tryFloat (cell cells cols.x), tryFloat (cell cells cols.y), tryFloat (cell cells cols.z) with
                | "", _, _, _ -> warnings.Add(sprintf "line %d: no polygon id, skipped" lineNo)
                | id, Some x, Some y, Some z ->
                    let sortKey =
                        cols.order
                        |> Option.bind (fun i -> tryFloat (cell cells i))
                        |> Option.defaultValue (float lineNo)
                    match byId.TryGetValue id with
                    | true, vs -> vs.Add((sortKey, lineNo, V3d(x, y, z)))
                    | _ ->
                        order.Add id
                        let vs = List<_>()
                        vs.Add((sortKey, lineNo, V3d(x, y, z)))
                        byId.[id] <- vs
                        let color = optCell cells cols.color |> tryParseColor
                        meta.[id] <- (optCell cells cols.label, color, optCell cells cols.group)
                | _ -> warnings.Add(sprintf "line %d: x/y/z not numbers, skipped" lineNo)

        let polygons =
            order
            |> Seq.toList
            |> List.choose (fun id ->
                match byId.TryGetValue id, meta.TryGetValue id with
                | (true, vs), (true, (label, color, group)) ->
                    let pts =
                        vs.ToArray()
                        |> Array.sortBy (fun (k, line, _) -> k, line)
                        |> Array.map (fun (_, _, p) -> p)
                    // a ring written closed (last = first) is stored once, then closed below
                    let pts =
                        if pts.Length > 3 && pts.[0] = pts.[pts.Length - 1] then pts.[.. pts.Length - 2]
                        else pts
                    if pts.Length < 3 then
                        warnings.Add(sprintf "polygon '%s': %d vertices, needs at least 3, skipped" id pts.Length)
                        None
                    else
                        Some { id = id; vertices = pts; label = (if label = "" then id else label); color = color; group = group }
                | _ -> None)

        { polygons = polygons; warnings = warnings |> Seq.toList }

    /// Reads the lines of a polygon CSV. Rows that cannot be used are skipped and listed
    /// in `warnings`; a missing id/x/y/z column fails the whole file.
    let parseLines (lines : seq<string>) : Result<ParseResult, string> =
        let data =
            lines
            |> Seq.mapi (fun i l -> i + 1, l)
            |> Seq.filter (snd >> isBlankOrComment >> not)
            |> Seq.toArray
        match Array.tryHead data with
        | None -> Result.Error "the file has no data rows"
        | Some (_, firstLine) ->
            let sep = detectSeparator firstLine
            let first = splitLine sep firstLine
            // a header is a first row whose 2nd..4th cells are not all numbers
            let hasHeader =
                first.Length < 4 || (first.[1..3] |> Array.exists (tryFloat >> Option.isNone))
            if hasHeader then
                fromHeader first |> Result.map (fun cols -> readRows sep cols data.[1..])
            else
                Result.Ok (readRows sep positional data)

    let parseFile (path : string) : Result<ParseResult, string> =
        try parseLines (File.ReadLines path)
        with e -> Result.Error e.Message

    // ---------------------------------------------------------------- snapping

    /// The direction a point is dropped along: the scene's **sky** (local up), as the
    /// drawing tool's sky projection uses it.
    type Sky = {
        /// local up at a point
        up       : V3d -> V3d
        /// how far down a hit may lie; deeper is the far side of the body
        maxDepth : V3d -> float
    }

    module Sky =
        /// `CooTransformation.getUpVector`: the ellipsoid normal on Mars, radial on other
        /// bodies, the fixed +Z / -Z of a local frame (Planet None, ENU, JPL). On a body a
        /// downward hit deeper than the distance to its centre is on the far side.
        let ofPlanet (planet : Planet) : Sky =
            match planet with
            | Planet.None | Planet.ENU | Planet.JPL ->
                { up = (fun p -> CooTransformation.getUpVector p planet); maxDepth = fun _ -> infinity }
            | _ ->
                { up = (fun p -> CooTransformation.getUpVector p planet); maxDepth = fun p -> p.Length }

        /// Up = away from the origin; for a body whose shape is not known to the reference
        /// system, and for tests.
        let radial : Sky =
            { up = (fun p -> if p.LengthSquared > 0.0 then p.Normalized else V3d.ZAxis)
              maxDepth = fun p -> p.Length }

    /// Moves a vertex onto the surface along the sky (local up) through it. Two rays leave
    /// the vertex, one down and one up, and the nearer hit wins: so a vertex slightly above
    /// or below the surface both land, and under an overhang the surface next to the vertex
    /// is taken rather than the topmost one.
    ///
    /// A downward hit deeper than `sky.maxDepth` (on a body: past its centre) is on the far
    /// side (the near side had a hole, or was hidden) and is rejected: a vertex never jumps
    /// through the body.
    ///
    /// `cast origin direction` returns the nearest hit point of a ray, or `None`.
    let snapToSurface (cast : V3d -> V3d -> Option<V3d>) (sky : Sky) (p : V3d) : Option<V3d> =
        let up = sky.up p
        let maxDepth = sky.maxDepth p
        let down =
            cast p -up
            |> Option.filter (fun h -> (h - p).Length <= maxDepth)
        match down, cast p up with
        | Some a, Some b -> Some (if (a - p).LengthSquared <= (b - p).LengthSquared then a else b)
        | Some a, None   -> Some a
        | None, Some b   -> Some b
        | None, None     -> None

    /// Snaps every vertex; a vertex with no hit keeps its position. Returns the polygons and
    /// how many vertices missed.
    let snapAll (cast : V3d -> V3d -> Option<V3d>) (sky : Sky) (polygons : list<PolygonRecord>) =
        let mutable missed = 0
        let snapped =
            polygons
            |> List.map (fun poly ->
                let vs =
                    poly.vertices
                    |> Array.map (fun p ->
                        match snapToSurface cast sky p with
                        | Some hit -> hit
                        | None -> missed <- missed + 1; p)
                { poly with vertices = vs })
        snapped, missed

    // ---------------------------------------------------------------- annotations

    let defaultColor = C4b(255uy, 200uy, 0uy, 255uy)

    /// A closed polygon annotation, stored like a drawn one: first vertex repeated at the
    /// end, linear projection, no segments.
    ///
    /// `modelTrafo` is the translation to the first vertex, as for a drawn annotation: the
    /// line renderer draws relative to it in float32, so planetary-scale coordinates stay
    /// precise. Callers guarantee at least 3 vertices (`readRows` does).
    let toAnnotation (up : V3d) (north : V3d) (planet : Planet) (poly : PolygonRecord) : Annotation =
        let color = { c = poly.color |> Option.defaultValue defaultColor }
        let first = poly.vertices.[0]
        let closed = Array.append poly.vertices [| first |]
        let a =
            { Annotation.make Projection.Linear None Geometry.Polygon None color Annotation.Initial.thickness "" with
                points     = IndexList.ofArray closed
                modelTrafo = Trafo3d.Translation first
                fillColor  = color
                text       = poly.label
                showText   = false }
        { a with results = Some (Calculations.calculateAnnotationResults a up north planet) }

    /// A closed polygon whose edges follow the surface, stored like one drawn with sky
    /// projection: open vertex list, one segment per edge with the in-between points every
    /// `spacing` metres dropped onto the surface by `sample`, and the closing edge stored
    /// first -> last as `DrawingApp.closePolyline` does (#821). In-between points that miss
    /// the surface are left out, as when drawing.
    ///
    /// `spacingFor a b` is the spacing of edge a -> b; `build` makes it coarser on edges that
    /// would otherwise exceed `ImportOptions.maxSamplesPerEdge` points.
    let toDrapedAnnotation
        (up : V3d) (north : V3d) (planet : Planet)
        (spacingFor : V3d -> V3d -> float) (sample : V3d -> Option<V3d>)
        (poly : PolygonRecord) : Annotation =
        let color = { c = poly.color |> Option.defaultValue defaultColor }
        let v = poly.vertices
        let resample a b = PRo3D.Core.Drawing.DrawingApp.resampleSegment (spacingFor a b) sample a b
        let segments =
            [ for i in 0 .. v.Length - 2 do
                yield resample v.[i] v.[i + 1]
              yield resample v.[0] v.[v.Length - 1] ]
        let a =
            { Annotation.make Projection.Sky None Geometry.Polygon None color Annotation.Initial.thickness "" with
                points     = IndexList.ofArray v
                segments   = IndexList.ofList segments
                modelTrafo = Trafo3d.Translation v.[0]
                fillColor  = color
                text       = poly.label
                showText   = false }
        { a with results = Some (Calculations.calculateAnnotationResults a up north planet) }

    /// What the import dialog sets.
    type ImportOptions = {
        /// move every vertex onto the surface (`snapToSurface`)
        snapVertices : bool
        /// add in-between points along every edge, dropped onto the surface
        drapeEdges   : bool
        /// distance between in-between points, metres
        spacing      : float
        /// fit a plane to the vertices (dip & strike) as a finished drawn polygon does;
        /// off by default, it is a regression per polygon
        dipAndStrike : bool
    }

    module ImportOptions =
        let initial = { snapVertices = false; drapeEdges = false; spacing = 1.0; dipAndStrike = false }

        /// Smallest spacing accepted, metres. Every in-between point is a ray cast pair.
        let minSpacing = 0.01

        /// Most in-between points on one edge; a longer edge is sampled coarser.
        let maxSamplesPerEdge = 1000

    type ImportSummary = {
        annotations    : list<string * Annotation>
        /// vertices that missed the surface and stayed where they were
        missedVertices : int
        /// in-between points that missed the surface and were left out
        missedSamples  : int
        /// edges sampled coarser than the spacing asked for (`maxSamplesPerEdge`)
        coarsenedEdges : int
    }

    /// Turns parsed polygons into annotations under `options`; `cast` is the ray cast used
    /// for snapping and for the in-between points (`snapToSurface` along `sky`), in the
    /// coordinates of the file. `frame` is the body frame those coordinates are in when a GIS
    /// observer is set (the annotations' `referenceSystem`, as for drawn ones), else `None`.
    let build (up : V3d) (north : V3d) (planet : Planet)
              (frame : Option<PRo3D.Base.Gis.SpiceReferenceSystem>)
              (cast : V3d -> V3d -> Option<V3d>) (sky : Sky) (options : ImportOptions)
              (polygons : list<PolygonRecord>) : ImportSummary =
        let polygons, missedVertices =
            if options.snapVertices then snapAll cast sky polygons else polygons, 0
        let mutable missedSamples = 0
        let sample p =
            match snapToSurface cast sky p with
            | Some hit -> Some hit
            | None -> missedSamples <- missedSamples + 1; None
        let spacing = max ImportOptions.minSpacing options.spacing
        let mutable coarsenedEdges = 0
        let spacingFor (a : V3d) (b : V3d) =
            let needed = (b - a).Length / float ImportOptions.maxSamplesPerEdge
            if needed > spacing then
                coarsenedEdges <- coarsenedEdges + 1
                needed
            else spacing
        let withDipAndStrike (a : Annotation) =
            if options.dipAndStrike then
                { a with dnsResults = a.points |> DipAndStrike.calculateDipAndStrikeResults up north }
            else a
        let annotations =
            polygons
            |> List.map (fun p ->
                let a =
                    if options.drapeEdges then toDrapedAnnotation up north planet spacingFor sample p
                    else toAnnotation up north planet p
                p.group, withDipAndStrike { a with referenceSystem = frame })
        { annotations = annotations; missedVertices = missedVertices; missedSamples = missedSamples
          coarsenedEdges = coarsenedEdges }

    let private mkNode (name : string) (leaves : list<Guid>) (subNodes : list<Node>) (expanded : bool) : Node =
        {
            version      = Node.current
            key          = Guid.NewGuid()
            name         = name
            leaves       = leaves |> IndexList.ofList
            subNodes     = subNodes |> IndexList.ofList
            visible      = true
            expanded     = expanded
            defaultColor = Node.initialDefaultColor
        }

    /// One group named after the file; polygons with a `group` value go to a sub-group of
    /// that name. Returns the group, the leaves and the leaf -> group-name lookup.
    let toGroup (fileName : string) (annotations : list<string * Annotation>)
        : Node * HashMap<Guid, Leaf> * HashMap<Guid, string> =
        let ungrouped = annotations |> List.filter (fun (g, _) -> g = "") |> List.map snd
        let grouped =
            annotations
            |> List.filter (fun (g, _) -> g <> "")
            |> List.groupBy fst
            |> List.map (fun (g, xs) -> g, xs |> List.map snd)
        let subNodes =
            grouped |> List.map (fun (g, xs) -> mkNode g (xs |> List.map (fun a -> a.key)) [] false)
        let top =
            mkNode (sprintf "%s (%d)" fileName annotations.Length)
                (ungrouped |> List.map (fun a -> a.key)) subNodes false
        let flat =
            annotations |> List.map (fun (_, a) -> a.key, Leaf.Annotations a) |> HashMap.ofList
        let lookup =
            [ for a in ungrouped do yield a.key, top.name
              for (g, xs) in grouped do
                  for a in xs do yield a.key, g ]
            |> HashMap.ofList
        top, flat, lookup
