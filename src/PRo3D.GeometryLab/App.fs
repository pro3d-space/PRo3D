module PRo3D.GeometryLab.App

open System
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base.Geometry
open PRo3D.Base.Geometry.RegionOps
open PRo3D.GeometryLab

// Aardvark's onMouseDown reports absolute coordinates; the Simple2DDrawing example works around
// that with handlers that resolve coordinates relative to the svg element. Same approach here.
// The coordinates come back as two plain JSON numbers parsed by hand - FsPickler's V2d pickler
// rejects integral JSON numbers ("not a float"), and a pixel coordinate is often integral.
module Events =
    let private relative (kind : string) (cb : V2d -> 'msg) =
        onEvent kind
            [ "(function(){var r=event.currentTarget.getBoundingClientRect(); return event.clientX - r.left;})()"
              "(function(){var r=event.currentTarget.getBoundingClientRect(); return event.clientY - r.top;})()" ]
            (fun args ->
                match args with
                | x :: y :: _ ->
                    let parse (s : string) =
                        match Double.TryParse(s.Trim(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                        | true, v -> Some v
                        | _ -> None
                    match parse x, parse y with
                    | Some x, Some y -> cb (V2d(x, y))
                    | _ -> cb V2d.Zero
                | _ -> cb V2d.Zero)

    let onMouseDownRel cb = relative "onmousedown" cb
    let onMouseUpRel   cb = relative "onmouseup" cb
    let onMouseMoveRel cb = relative "onmousemove" cb

let initial =
    {
        shapes  = IndexList.empty
        nextId  = 0
        tool    = Draw
        drawing = IndexList.empty
        cursor  = None
        cutFrom = None
        status  = "draw a polygon: click to add points, Close to finish"
        past    = None
        future  = None
    }

let private remember (m : Model) = { m with past = Some m; future = None }

let private selected (m : Model) =
    m.shapes |> IndexList.toList |> List.filter (fun s -> s.selected)

let private addShape (region : Region) (m : Model) =
    { m with
        shapes = m.shapes |> IndexList.add { id = m.nextId; region = region; selected = false }
        nextId = m.nextId + 1 }

/// Sequencing only. Every geometric decision belongs to RegionOps, so it stays checkable without
/// a GUI - see plans/testingStrategy.md.
let update (m : Model) (msg : Message) =
    match msg with
    | SetTool t ->
        { m with tool = t; drawing = IndexList.empty; cutFrom = None }

    | MouseDown p ->
        match m.tool with
        | Draw   -> { m with drawing = m.drawing |> IndexList.add p }
        | Cut    -> { m with cutFrom = Some p }
        | Select -> m

    | ClosePolygon ->
        let ring = m.drawing |> IndexList.toArray
        match ofRing2d ring with
        | None ->
            { m with drawing = IndexList.empty; status = "that ring encloses nothing" }
        | Some region ->
            let m = remember m
            { addShape region m with
                drawing = IndexList.empty
                status  = sprintf "added shape, area %.1f" (area region) }

    | MoveCursor p ->
        { m with cursor = Some p }

    | MouseUp p ->
        match m.tool, m.cutFrom with
        | Cut, Some from ->
            let m = remember m
            let mutable cutCount = 0
            let mutable nextId = m.nextId
            let rebuilt =
                m.shapes
                |> IndexList.toList
                |> List.collect (fun s ->
                    match cut from p s.region with
                    | [ single ] -> [ { s with region = single } ]     // untouched
                    | pieces ->
                        cutCount <- cutCount + 1
                        pieces |> List.map (fun piece ->
                            let id = nextId
                            nextId <- nextId + 1
                            { id = id; region = piece; selected = false }))
            { m with
                shapes  = IndexList.ofList rebuilt
                nextId  = nextId
                cutFrom = None
                status  =
                    if cutCount = 0 then "the stroke did not cut through anything"
                    else sprintf "cut %d shape(s)" cutCount }
        | _ -> m

    | ToggleSelect id ->
        { m with
            shapes =
                m.shapes |> IndexList.map (fun s ->
                    if s.id = id then { s with selected = not s.selected } else s) }

    | MergeSelected ->
        match selected m with
        | [ a; b ] ->
            let m = remember m
            let merged = merge a.region b.region
            let rest = m.shapes |> IndexList.filter (fun s -> not s.selected)
            let holeCount = holes merged |> List.length
            let componentCount = outerRings merged |> List.length
            { addShape merged { m with shapes = rest } with
                status =
                    sprintf "merged: %d component(s), %d hole(s)%s"
                        componentCount holeCount
                        (if holeCount > 0 then " - not storable as single-ring annotations" else "") }
        | other ->
            { m with status = sprintf "select exactly two shapes to merge (%d selected)" other.Length }

    | DeleteSelected ->
        let m = remember m
        { m with shapes = m.shapes |> IndexList.filter (fun s -> not s.selected) }

    | ExportFixture ->
        let regions = m.shapes |> IndexList.toList |> List.map (fun s -> s.region)
        if regions.IsEmpty then { m with status = "nothing to export" }
        else
            let dir = Path.Combine(__SOURCE_DIRECTORY__, "..", "Tests", "data", "regions")
            let name = sprintf "lab-%s" (Guid.NewGuid().ToString("N").Substring(0, 8))
            let path = RegionFixture.save dir name regions "exported from the geometry lab"
            { m with status = sprintf "wrote %s" (Path.GetFileName path) }

    | Undo ->
        match m.past with
        | Some p -> { p with future = Some m }
        | None   -> { m with status = "nothing to undo" }

    | Redo ->
        match m.future with
        | Some f -> f
        | None   -> { m with status = "nothing to redo" }

// ---------------------------------------------------------------------------------------------

let private pathOf (pts : V2d[]) =
    pts
    |> Array.mapi (fun i p -> sprintf "%s%.2f %.2f" (if i = 0 then "M" else "L") p.X p.Y)
    |> String.concat " "
    |> fun s -> s + " Z"

let view (m : AdaptiveModel) =

    let toolButton (t : Tool) (label : string) =
        button [ clazz "ui button"; onClick (fun _ -> SetTool t) ] [ text label ]

    let shapes =
        m.shapes
        |> AList.map (fun s ->
            // holes are drawn in the background colour, so a merge that produces one is obvious
            // rather than silently filled
            let outer = outerRings s.region |> List.map (fun r -> r |> Array.map (fun p -> V2d(p.X, p.Y)))
            let hs    = holes s.region      |> List.map (fun r -> r |> Array.map (fun p -> V2d(p.X, p.Y)))
            let fill  = if s.selected then "#4c9aff" else "#b0bec5"
            Svg.g [] [
                for ring in outer do
                    yield Svg.path [
                        attribute "d" (pathOf ring)
                        attribute "fill" fill
                        attribute "stroke" "#263238"
                        attribute "stroke-width" "1.5"
                        onClick (fun _ -> ToggleSelect s.id)
                    ]
                for hole in hs do
                    yield Svg.path [
                        attribute "d" (pathOf hole)
                        attribute "fill" "#ffffff"
                        attribute "stroke" "#d32f2f"
                        attribute "stroke-dasharray" "4 2"
                        attribute "stroke-width" "1.5"
                    ]
            ])

    let inProgress =
        alist {
            let! pts = m.drawing |> AList.toAVal
            let pts = pts |> IndexList.toArray
            // vertices drawn individually, so the very first click is already visible
            for p in pts do
                yield Svg.circle [
                    attribute "cx" (sprintf "%.2f" p.X)
                    attribute "cy" (sprintf "%.2f" p.Y)
                    attribute "r" "3"
                    attribute "fill" "#ff9800"
                ]
            if pts.Length > 1 then
                yield Svg.path [
                    attribute "d" (pathOf pts)
                    attribute "fill" "none"
                    attribute "stroke" "#ff9800"
                    attribute "stroke-width" "1.5"
                ]

            let! from = m.cutFrom
            let! cur  = m.cursor
            match from, cur with
            | Some a, Some b ->
                yield Svg.line [
                    attribute "x1" (sprintf "%.2f" a.X); attribute "y1" (sprintf "%.2f" a.Y)
                    attribute "x2" (sprintf "%.2f" b.X); attribute "y2" (sprintf "%.2f" b.Y)
                    attribute "stroke" "#d32f2f"; attribute "stroke-width" "2"
                ]
            | _ -> ()
        }

    require Html.semui (
        body [] [
            div [ clazz "ui menu" ] [
                toolButton Draw "Draw"
                toolButton Cut "Cut"
                toolButton Select "Select"
                button [ clazz "ui button"; onClick (fun _ -> ClosePolygon) ] [ text "Close" ]
                button [ clazz "ui button"; onClick (fun _ -> MergeSelected) ] [ text "Merge" ]
                button [ clazz "ui button"; onClick (fun _ -> DeleteSelected) ] [ text "Delete" ]
                button [ clazz "ui button"; onClick (fun _ -> Undo) ] [ text "Undo" ]
                button [ clazz "ui button"; onClick (fun _ -> Redo) ] [ text "Redo" ]
                button [ clazz "ui button"; onClick (fun _ -> ExportFixture) ] [ text "Export fixture" ]
            ]
            div [ clazz "ui segment" ] [ Incremental.text m.status ]
            Incremental.Svg.svg
                (AttributeMap.ofList [
                    attribute "width" "1000"
                    attribute "height" "700"
                    style "border:1px solid #cfd8dc; background:#ffffff"
                    Events.onMouseDownRel (fun p -> MouseDown p)
                    Events.onMouseUpRel   (fun p -> MouseUp p)
                    Events.onMouseMoveRel (fun p -> MoveCursor p)
                 ])
                (AList.append (shapes |> AList.map (fun s -> s)) inProgress)
        ]
    )

let app : App<Model, AdaptiveModel, Message> =
    {
        initial   = initial
        update    = update
        view      = view
        threads   = fun _ -> ThreadPool.empty
        unpersist = Unpersist.instance
    }
