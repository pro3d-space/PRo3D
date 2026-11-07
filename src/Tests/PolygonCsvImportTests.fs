module PolygonCsvImportTests

open Expecto
open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

let private parse (text : string) =
    match PolygonCsvImporter.parseLines (text.Replace("\r", "").Split('\n')) with
    | Result.Ok r -> r
    | Result.Error e -> failtestf "parse failed: %s" e

let private ids (r : PolygonCsvImporter.ParseResult) = r.polygons |> List.map (fun p -> p.id)

/// The first built annotation; fails the test when there is none.
let private first (built : PolygonCsvImporter.ImportSummary) =
    match built.annotations with
    | (_, a) :: _ -> a
    | [] -> failtest "no annotation built"

/// Ray cast against a sphere of radius `r` around the origin: the nearest hit at t >= 0.
let private sphereCast (r : float) (o : V3d) (d : V3d) : Option<V3d> =
    let d = d.Normalized
    let b = Vec.dot o d
    let c = Vec.dot o o - r * r
    let disc = b * b - c
    if disc < 0.0 then None
    else
        let s = sqrt disc
        [ -b - s; -b + s ] |> List.filter (fun t -> t >= 0.0) |> List.sort |> List.tryHead
        |> Option.map (fun t -> o + d * t)

let private parsing =
    testList "parsing" [
        test "header, comma, rows grouped by id in first-appearance order" {
            let r = parse "polygon_id,x,y,z\na,0,0,0\nb,5,5,5\na,1,0,0\nb,6,5,5\na,1,1,0\nb,6,6,5\n"
            Expect.equal (ids r) [ "a"; "b" ] "order"
            Expect.equal r.polygons.Head.vertices [| V3d(0,0,0); V3d(1,0,0); V3d(1,1,0) |] "vertices in row order"
            Expect.isEmpty r.warnings "no warnings"
        }

        test "headerless rows are id,x,y,z; semicolon, tab and whitespace separators" {
            for sep in [ ","; ";"; "\t"; "  " ] do
                let row (i, x) = String.concat sep [ i; x; "2"; "3" ]
                let text = [ "p", "1"; "p", "4"; "p", "7" ] |> List.map row |> String.concat "\n"
                let r = parse text
                Expect.equal (ids r) [ "p" ] (sprintf "sep %A" sep)
                Expect.equal r.polygons.Head.vertices.[1] (V3d(4, 2, 3)) (sprintf "sep %A" sep)
        }

        test "a closing vertex equal to the first is dropped (stored once)" {
            let r = parse "id,x,y,z\na,0,0,0\na,1,0,0\na,1,1,0\na,0,0,0\n"
            Expect.equal r.polygons.Head.vertices.Length 3 "ring opened"
        }

        test "fewer than 3 vertices and bad numbers are skipped with a warning" {
            let r = parse "id,x,y,z\na,0,0,0\na,1,0,0\nb,0,0,0\nb,1,0,0\nb,nope,0,0\nb,1,1,0\n"
            Expect.equal (ids r) [ "b" ] "only b survives"
            Expect.equal r.warnings.Length 2 "bad row + short polygon"
        }

        test "label, colour (hex and Aardvark format, quoted) and group columns" {
            let r =
                parse "polygon_id,x,y,z,label,color,group\n\
                       a,0,0,0,\"Crater, rim\",#ff0000,craters\n\
                       a,1,0,0,,,\n\
                       a,1,1,0,,,\n\
                       b,0,0,0,,\"[0, 0, 255, 128]\",\n\
                       b,1,0,0,,,\n\
                       b,1,1,0,,,\n"
            let a, b = r.polygons.[0], r.polygons.[1]
            Expect.equal a.label "Crater, rim" "quoted label"
            Expect.equal a.color (Some (C4b(255uy, 0uy, 0uy, 255uy))) "hex"
            Expect.equal a.group "craters" "group"
            Expect.equal b.color (Some (C4b(0uy, 0uy, 255uy, 128uy))) "aardvark format"
            Expect.equal b.label "b" "label defaults to id"
        }

        test "PRo3D per-point export: key + pointIndex orders vertices" {
            let r = parse "key,pointIndex,x,y,z\nk,2,2,0,0\nk,0,0,0,0\nk,1,1,0,0\n"
            Expect.equal r.polygons.Head.vertices [| V3d(0,0,0); V3d(1,0,0); V3d(2,0,0) |] "by pointIndex"
        }

        test "unproject output: x_m beats the pixel x, non-ok rows are skipped" {
            let r =
                parse "image,x,y,polygon_id,status,x_m,y_m,z_m\n\
                       img.tif,320,256,p,ok,10,0,0\n\
                       img.tif,5,5,p,no-hit,,,\n\
                       img.tif,321,256,p,ok,0,10,0\n\
                       img.tif,322,256,p,ok,0,0,10\n"
            Expect.equal r.polygons.Head.vertices [| V3d(10,0,0); V3d(0,10,0); V3d(0,0,10) |] "metres, not pixels"
            Expect.equal r.warnings.Length 1 "the no-hit row"
        }

        test "unproject output: the same polygon_id in two images is two polygons" {
            let r =
                parse "image,x,y,polygon_id,status,x_m,y_m,z_m\n\
                       a.tif,1,1,p,ok,0,0,0\n\
                       b.tif,1,1,p,ok,5,5,5\n\
                       a.tif,2,1,p,ok,1,0,0\n\
                       b.tif,2,1,p,ok,6,5,5\n\
                       a.tif,2,2,p,ok,1,1,0\n\
                       b.tif,2,2,p,ok,6,6,5\n"
            Expect.equal (ids r) [ "p @ a.tif"; "p @ b.tif" ] "keyed by image and id"
            Expect.equal r.polygons.[1].vertices.[0] (V3d(5, 5, 5)) "b's own vertices"
        }

        test "missing columns fail the file" {
            let r = PolygonCsvImporter.parseLines [ "name,a,b,c"; "x,1,2,3" ]
            Expect.isError r "no x/y/z"
            Expect.isError (PolygonCsvImporter.parseLines [ "# only a comment" ]) "empty"
        }
    ]

let private snapping =
    testList "snapping" [
        test "vertices above and below a sphere land on it along the radial" {
            let cast = sphereCast 100.0
            for p in [ V3d(150, 0, 0); V3d(30, 40, 0); V3d(-1, -1, -1) * 200.0 ] do
                match PolygonCsvImporter.snapToSurface cast PolygonCsvImporter.Sky.radial p with
                | Some hit ->
                    Expect.floatClose Accuracy.high hit.Length 100.0 "on the sphere"
                    Expect.floatClose Accuracy.high (Vec.dot hit.Normalized p.Normalized) 1.0 "same direction"
                | None -> failtest "no hit"
        }

        test "the nearer of the inward and outward hit wins" {
            // a shell between r = 90 and r = 100: a vertex at 92 is nearer the inner wall
            let cast (o : V3d) (d : V3d) =
                [ sphereCast 90.0 o d; sphereCast 100.0 o d ]
                |> List.choose id
                |> List.sortBy (fun h -> (h - o).Length)
                |> List.tryHead
            match PolygonCsvImporter.snapToSurface cast PolygonCsvImporter.Sky.radial (V3d(92, 0, 0)) with
            | Some hit -> Expect.floatClose Accuracy.high hit.X 90.0 "inner wall"
            | None -> failtest "no hit"
        }

        test "an inward hit beyond the barycentre (far side, near side missing) is rejected" {
            // a hole under the vertex: the inward ray only finds the far side of the body
            let cast (o : V3d) (d : V3d) =
                if Vec.dot o d < 0.0 then Some (o + d.Normalized * (o.Length + 100.0)) else None
            Expect.isNone (PolygonCsvImporter.snapToSurface cast PolygonCsvImporter.Sky.radial (V3d(150, 0, 0))) "no jump through the body"
        }

        test "local frame (Planet None): straight down along +Z, not towards the origin" {
            // ground plane z = 0, far from the origin
            let plane (o : V3d) (d : V3d) =
                let d = d.Normalized
                if abs d.Z < 1e-12 then None
                else
                    let t = -o.Z / d.Z
                    if t >= 0.0 then Some (o + d * t) else None
            let p = V3d(1000, 2000, 5)
            match PolygonCsvImporter.snapToSurface plane (PolygonCsvImporter.Sky.ofPlanet Planet.None) p with
            | Some hit -> Expect.equal hit (V3d(1000, 2000, 0)) "dropped vertically"
            | None -> failtest "no hit"
            // below the ground and below the origin's height: still lands (no far-side cut-off)
            match PolygonCsvImporter.snapToSurface plane (PolygonCsvImporter.Sky.ofPlanet Planet.None) (V3d(10, 0, -3)) with
            | Some hit -> Expect.equal hit (V3d(10, 0, 0)) "lifted up"
            | None -> failtest "no hit"
        }

        test "misses keep their position and are counted; the origin is never snapped" {
            let poly : PolygonCsvImporter.PolygonRecord =
                { id = "a"; vertices = [| V3d(150, 0, 0); V3d.Zero; V3d(0, 150, 0) |]; label = "a"; color = None; group = "" }
            let snapped, missed = PolygonCsvImporter.snapAll (fun _ _ -> None) PolygonCsvImporter.Sky.radial [ poly ]
            Expect.equal missed 3 "all missed"
            Expect.equal snapped.Head.vertices poly.vertices "unchanged"
        }
    ]

let private annotations =
    testList "annotations" [
        test "closed polygon annotation in a group named after the file, sub-groups per group column" {
            let r =
                parse "polygon_id,x,y,z,group\na,0,0,0,\na,1,0,0,\na,1,1,0,\nb,0,0,0,g\nb,1,0,0,g\nb,1,1,0,g\n"
            let anns =
                r.polygons |> List.map (fun p -> p.group, PolygonCsvImporter.toAnnotation V3d.ZAxis V3d.YAxis Planet.None p)
            let a = snd anns.Head
            Expect.equal a.geometry Geometry.Polygon "polygon"
            Expect.equal a.points.Count 4 "closed: first point repeated"
            Expect.equal (a.points |> IndexList.toList |> List.last) (V3d(0, 0, 0)) "closing point"
            Expect.isSome a.results "measurements computed"

            let node, flat, lookup = PolygonCsvImporter.toGroup "f.csv" anns
            Expect.equal node.leaves.Count 1 "a at top level"
            Expect.equal node.subNodes.Count 1 "one sub-group"
            Expect.equal flat.Count 2 "both leaves"
            Expect.equal (lookup |> HashMap.tryFind (snd anns.[1]).key) (Some "g") "lookup to sub-group"
        }
    ]

let private draping =
    testList "draping" [
        test "sky-projected in-between points: one segment per edge, closing edge first -> last, samples on the surface" {
            let cast = sphereCast 100.0
            // a triangle well above a sphere of radius 100, edges ~70 m long
            let poly : PolygonCsvImporter.PolygonRecord =
                { id = "t"; vertices = [| V3d(150, 0, 0); V3d(0, 150, 0); V3d(0, 0, 150) |]; label = "t"; color = None; group = "" }
            let options = { PolygonCsvImporter.ImportOptions.initial with snapVertices = true; drapeEdges = true; spacing = 5.0 }
            let built = PolygonCsvImporter.build V3d.ZAxis V3d.YAxis Planet.None None cast PolygonCsvImporter.Sky.radial options [ poly ]
            let a = first built
            Expect.equal a.projection Projection.Sky "drawn-with-sky representation"
            Expect.equal a.points.Count 3 "open vertex list, as drawn"
            Expect.equal a.segments.Count 3 "one segment per edge incl. the closing one"
            let segs = a.segments |> IndexList.toList
            Expect.equal (segs |> List.last).startPoint (a.points |> IndexList.toList |> List.head) "closing edge starts at the first vertex"
            Expect.equal (segs |> List.last).endPoint (a.points |> IndexList.toList |> List.last) "and ends at the last"
            for s in segs do
                Expect.isGreaterThan s.points.Count 10 "in-between points every 5 m"
                for p in s.points do
                    Expect.floatClose Accuracy.medium p.Length 100.0 "in-between point on the sphere"
            Expect.equal built.missedSamples 0 "nothing missed"
        }

        test "without draping the polygon stays linear and closed" {
            let poly : PolygonCsvImporter.PolygonRecord =
                { id = "t"; vertices = [| V3d(150, 0, 0); V3d(0, 150, 0); V3d(0, 0, 150) |]; label = "t"; color = None; group = "" }
            let built = PolygonCsvImporter.build V3d.ZAxis V3d.YAxis Planet.None None (sphereCast 100.0) PolygonCsvImporter.Sky.radial PolygonCsvImporter.ImportOptions.initial [ poly ]
            let a = first built
            Expect.equal a.projection Projection.Linear "linear"
            Expect.equal a.points.Count 4 "closed"
            Expect.equal a.segments.Count 0 "no segments"
            Expect.floatClose Accuracy.high a.points.[0].Length 150.0 "not snapped"
        }

        test "long edges are capped at maxSamplesPerEdge points and counted" {
            // a triangle with 10 km edges at 1 m spacing would be 10000 points per edge
            let poly : PolygonCsvImporter.PolygonRecord =
                { id = "t"; vertices = [| V3d(1e6, 0, 0); V3d(1e6, 1e4, 0); V3d(1e6, 0, 1e4) |]; label = "t"; color = None; group = "" }
            let options = { PolygonCsvImporter.ImportOptions.initial with drapeEdges = true; spacing = 1.0 }
            let onTheSpot (o : V3d) (_ : V3d) = Some o
            let built = PolygonCsvImporter.build V3d.ZAxis V3d.YAxis Planet.None None onTheSpot PolygonCsvImporter.Sky.radial options [ poly ]
            let a = first built
            for s in a.segments do
                Expect.isLessThanOrEqual s.points.Count PolygonCsvImporter.ImportOptions.maxSamplesPerEdge "capped"
            Expect.equal built.coarsenedEdges 3 "all three edges coarsened"
        }
    ]

let private precisionAndResults =
    testList "precision and results" [
        test "modelTrafo is the translation to the first vertex, linear and sky-projected" {
            let poly : PolygonCsvImporter.PolygonRecord =
                { id = "t"; vertices = [| V3d(3.4e6, 1.0, 2.0); V3d(3.4e6, 11.0, 2.0); V3d(3.4e6, 1.0, 12.0) |]; label = "t"; color = None; group = "" }
            for drape in [ false; true ] do
                let options = { PolygonCsvImporter.ImportOptions.initial with drapeEdges = drape }
                let built = PolygonCsvImporter.build V3d.ZAxis V3d.YAxis Planet.None None (fun o _ -> Some o) PolygonCsvImporter.Sky.radial options [ poly ]
                let a = first built
                Expect.equal (a.modelTrafo.Forward.TransformPos V3d.Zero) poly.vertices.[0] (sprintf "drape %b" drape)
        }

        test "dip & strike only when asked for" {
            let poly : PolygonCsvImporter.PolygonRecord =
                { id = "t"; vertices = [| V3d(0, 0, 0); V3d(10, 0, 0); V3d(10, 10, 1); V3d(0, 10, 1) |]; label = "t"; color = None; group = "" }
            let run dns =
                let options = { PolygonCsvImporter.ImportOptions.initial with dipAndStrike = dns }
                let built = PolygonCsvImporter.build V3d.ZAxis V3d.YAxis Planet.None None (fun _ _ -> None) PolygonCsvImporter.Sky.radial options [ poly ]
                (first built).dnsResults
            Expect.isNone (run false) "off by default"
            Expect.isSome (run true) "computed"
        }
    ]

/// The examples docs/PolygonCsvImport.md points at; they must keep importing.
let private shippedExamples =
    let scripts = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "scripts")
    testList "shipped examples" [
        test "scripts/example-polygons.csv: a hexagon and a triangle" {
            match PolygonCsvImporter.parseFile (System.IO.Path.Combine(scripts, "example-polygons.csv")) with
            | Result.Ok r ->
                Expect.equal (ids r) [ "patch_centre"; "triangle_south" ] "ids"
                Expect.equal (r.polygons |> List.map (fun p -> p.vertices.Length)) [ 6; 3 ] "vertex counts"
                Expect.equal r.polygons.Head.label "Hexagon at the frame centre" "label"
                Expect.isEmpty r.warnings "clean"
            | Result.Error e -> failtestf "%s" e
        }

        test "scripts/example-polygon-pixels.csv is unproject input, not importable as is" {
            Expect.isError
                (PolygonCsvImporter.parseFile (System.IO.Path.Combine(scripts, "example-polygon-pixels.csv")))
                "pixels have no z: run pro3d-tool unproject first"
        }
    ]

let tests () =
    testList "polygonCsvImport" [ parsing; snapping; draping; precisionAndResults; annotations; shippedExamples ]
