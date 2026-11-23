/// Synthetic annotations for the map projection view (#772, phase 2). Generated rather than taken
/// from real catalogs, so every case the map has to get right is present by construction and
/// nothing non-public ends up in a test: a polyline across the +-180 degree meridian, one near
/// the north pole, a filled polygon, two points, and an ellipse built by the SBMT row parser.
///
/// Every annotation has its own colour, none of which the graticule uses, so a rendered pixel
/// says which annotation it belongs to.
///
/// `Tests.dll --write-map-annotations <file>` writes them as a PRo3D annotation file; the
/// committed `tests-ui/fixtures/map-projection-annotations.pro3d.ann` was generated that way
/// and is what the standalone app and the Playwright spec load.
module MapProjectionAnnotationFixture

open System
open System.Globalization

open Expecto
open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.MapProjection

/// Above the Dimorphos test OPC's surface (radius up to ~90 m), so nothing is hidden under it
/// if someone turns the depth test on.
let radius = 95.0

let private deg (d : float) = d * Math.PI / 180.0

/// Body-fixed position at (longitude, latitude) in degrees.
let at (lonDeg : float) (latDeg : float) =
    let lon, lat = deg lonDeg, deg latDeg
    V3d(cos lat * cos lon, cos lat * sin lon, sin lat) * radius

type Case =
    {
        name       : string
        color      : C4b
        annotation : Annotation
    }

let private annotation (geometry : Geometry) (color : C4b) (thickness : float) (points : list<V3d>) =
    let a =
        Annotation.make Projection.Linear None geometry None { c = color }
            { Annotation.Initial.thickness with value = thickness } ""
    let pts = IndexList.ofList points
    // DrawingApp pivots an annotation on its first point
    let pivot = match points with p :: _ -> Trafo3d.Translation p | [] -> Trafo3d.Identity
    { a with points = pts; modelTrafo = pivot }

let seamLine =
    { name = "polyline across the 180 degree meridian"; color = C4b(255uy, 0uy, 255uy, 255uy)
      annotation =
        annotation Geometry.Polyline (C4b(255uy, 0uy, 255uy, 255uy)) 4.0
            [ at 160.0 10.0; at 170.0 10.0; at 180.0 10.0; at -170.0 10.0; at -160.0 10.0 ] }

let polarLine =
    { name = "polyline at 70 degrees north"; color = C4b(0uy, 255uy, 255uy, 255uy)
      annotation =
        annotation Geometry.Polyline (C4b(0uy, 255uy, 255uy, 255uy)) 4.0
            [ at -60.0 70.0; at -30.0 70.0; at 0.0 70.0; at 30.0 70.0; at 60.0 70.0 ] }

let polygon =
    let corners = [ at 40.0 -10.0; at 60.0 -10.0; at 60.0 -30.0; at 40.0 -30.0 ]
    let a =
        annotation Geometry.Polygon (C4b(0uy, 255uy, 0uy, 255uy)) 4.0 (corners @ [ List.head corners ])
    { name = "filled polygon"; color = C4b(0uy, 255uy, 0uy, 255uy)
      annotation = { a with showFill = true; fillAlpha = { a.fillAlpha with value = 0.5 } } }

let orangePoint =
    { name = "point at (-90, 0)"; color = C4b(255uy, 128uy, 0uy, 255uy)
      annotation = annotation Geometry.Point (C4b(255uy, 128uy, 0uy, 255uy)) 8.0 [ at -90.0 0.0 ] }

let bluePoint =
    { name = "point at (0, -45)"; color = C4b(0uy, 0uy, 255uy, 255uy)
      annotation = annotation Geometry.Point (C4b(0uy, 0uy, 255uy, 255uy)) 8.0 [ at 0.0 -45.0 ] }

/// Through the SBMT parser, as an imported boulder would come in (60 boundary samples).
let ellipse =
    let inv (x : float) = x.ToString("R", CultureInfo.InvariantCulture)
    let centreKm = at -120.0 30.0 / 1000.0
    let row =
        String.Join("\t",
            [| "1"; "default"; inv centreKm.X; inv centreKm.Y; inv centreKm.Z
               "0.0"; "0.0"; "0.0"; "NA"; "NA"; "NA"; "NA"
               inv 0.04; inv 0.5; inv 30.0; "255,128,255"; "NA"; "\"synthetic\"" |])
    match SbmtImporter.parseEllipseLine Trafo3d.Identity "DIMORPHOS_SHM" row with
    | Some a -> { name = "ellipse (SBMT row)"; color = C4b(255uy, 128uy, 255uy, 255uy); annotation = a }
    | None -> failwith "the synthetic SBMT ellipse row must parse"

let all = [ seamLine; polarLine; polygon; orangePoint; bluePoint; ellipse ]

/// The fixture as a PRo3D annotation file.
let serialize () =
    let leaves = all |> List.map (fun c -> Leaf.Annotations c.annotation) |> IndexList.ofList
    let drawing =
        { DrawingModel.initialdrawing with
            annotations = GroupsApp.addLeaves List.empty leaves DrawingModel.initialdrawing.annotations }
    PRo3D.Core.Drawing.IO.getSerialized drawing

let write (path : string) =
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath path)) |> ignore
    System.IO.File.WriteAllText(path, serialize ())

let tests () =
    testList "map projection annotation fixture (#772)" [

        test "the fixture round-trips through a PRo3D annotation file" {
            let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "map-annotations-%s.pro3d.ann" (Guid.NewGuid().ToString("N")))
            try
                write path
                let loaded = MapAnnotations.load "DIMORPHOS_SHM" path
                Expect.equal loaded.Length all.Length "every annotation comes back"
                for c in all do
                    match loaded |> List.tryFind (fun a -> a.key = c.annotation.key) with
                    | None -> failtestf "%s is missing" c.name
                    | Some a ->
                        Expect.equal a.color.c c.color (sprintf "%s keeps its colour" c.name)
                        Expect.equal (IndexList.count a.points) (IndexList.count c.annotation.points) (sprintf "%s keeps its points" c.name)
            finally
                try System.IO.File.Delete path with _ -> ()
        }

        test "the cases lie where they are meant to" {
            let lonLat (p : V3d) =
                let l = Projection.lonLatR p
                V2d(l.X * 180.0 / Math.PI, l.Y * 180.0 / Math.PI)
            let seam = seamLine.annotation.points |> IndexList.toArray |> Array.map lonLat
            Expect.isTrue (seam |> Array.exists (fun p -> p.X > 150.0) && seam |> Array.exists (fun p -> p.X < -150.0))
                "the seam line has vertices on both sides of 180 degrees"
            let ellipsePts = ellipse.annotation.points |> IndexList.toArray
            Expect.isGreaterThan ellipsePts.Length 30 "the SBMT ellipse is densely sampled"
            let centre = lonLat (ellipsePts |> Array.fold (+) V3d.Zero |> fun s -> s / float ellipsePts.Length)
            Expect.isLessThan (abs (centre.X + 120.0) + abs (centre.Y - 30.0)) 1.0 "the ellipse sits at (-120, 30)"
        }
    ]
