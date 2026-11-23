/// Builds the geological map showcase scene (#843): Dimorphos with a hand-designed set of
/// contacts and label points, and a map over them.
///
/// Not a test - a scene to open. It goes through the same ViewerActions the UI raises
/// (import, save), and the annotations are draped on the terrain by the viewer's own kd-tree
/// ray casts, so the scene is what a mapper could have drawn by hand.
///
/// Run it with (PRO3D_TEST_DATA pointing at a PRo3D.Resources.TestData checkout):
///     dotnet run --project src/Tests -- --make-geomap-showcase [outDir]
///
/// The map, as designed (latitude/longitude in the Dimorphos body frame):
///   - a wavy contact once around the body near the equator: north / south
///   - a crater rim (closed loop) in the north that reaches across the wavy contact
///   - a meridian through the south pole, overshooting into the north at both ends:
///     splits the south into two units, the overshoots are trimmed
///   - a small closed loop in the south: a boulder field inside a unit
///   - a short contact that reaches nothing: an open end
/// giving six units; the part of the crater south of the wavy contact is left without a label
/// to show how an unlabelled unit looks.
module PRo3D.Tests.GeologicalMapShowcase

open System
open System.IO

open Aardvark.Base
open Aardvark.Geometry
open Aardvark.UI
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Base.Geometry
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer

open PRo3D.Tests

let private dir (latDeg : float) (lonDeg : float) =
    let lat = latDeg * Constant.RadiansPerDegree
    let lon = lonDeg * Constant.RadiansPerDegree
    V3d(cos lat * cos lon, cos lat * sin lon, sin lat)

let private basis (n : V3d) =
    let n = n.Normalized
    let u = (if abs n.Z < 0.9 then Vec.Cross(n, V3d.OOI) else Vec.Cross(n, V3d.IOO)).Normalized
    n, u, Vec.Cross(n, u)

/// closed circle of angular radius rhoDeg around direction c, `count` samples
let private loop (c : V3d) (rhoDeg : float) (count : int) =
    let n, u, v = basis c
    let rho = rhoDeg * Constant.RadiansPerDegree
    Array.init (count + 1) (fun i ->
        let a = Constant.PiTimesTwo * float (i % count) / float count
        n * cos rho + (u * cos a + v * sin a) * sin rho)

/// great-circle arc from a to b, `count` pieces
let private arc (a : V3d) (b : V3d) (count : int) =
    let a = a.Normalized
    let b = b.Normalized
    let w = acos (clamp -1.0 1.0 (Vec.Dot(a, b)))
    Array.init (count + 1) (fun i ->
        let t = float i / float count
        (a * sin ((1.0 - t) * w) + b * sin (t * w)) / sin w)

/// the radial ray from outside the body, inward: on a star-shaped body its first hit is
/// the surface point in that direction
let private drape (m : Model) (cache : HashMap<string, ConcreteKdIntersectionTree>) (d : V3d) =
    let ray = FastRay3d(Ray3d(d * 500.0, -d))
    let filter = fun (_ : Guid) (l : Leaf) (_ : SgSurface) -> l.visible && l.active
    match SurfaceIntersection.doKdTreeIntersection
              m.scene.surfacesModel m.scene.referenceSystem (fun _ -> None) None ray filter cache false with
    | Some hit, cache -> Some (ray.Ray.GetPointOnRay hit.hit.RayHit.T), cache
    | None, cache -> None, cache

let private drapeAll (m : Model) (cache : HashMap<string, ConcreteKdIntersectionTree>) (ds : V3d[]) =
    let mutable cache = cache
    let pts =
        ds |> Array.choose (fun d ->
            let p, c = drape m cache d
            cache <- c
            p)
    pts, cache

let private contactColor = { c = C4b(30, 30, 30) }

let private polyline (points : V3d[]) (name : string) =
    let a = Annotation.make Projection.Linear None Geometry.Polyline None contactColor { Annotation.Initial.thickness with value = 3.0 } "Dimorphos"
    { a with
        points     = IndexList.ofArray points
        modelTrafo = points |> Array.tryHead |> Option.map Trafo3d.Translation |> Option.defaultValue Trafo3d.Identity
        text       = name
        showText   = false }

let private labelPoint (p : V3d) (unitCode : string) =
    let a = Annotation.make Projection.Linear None Geometry.Point None { c = C4b.White } { Annotation.Initial.thickness with value = 4.0 } "Dimorphos"
    { a with
        points     = IndexList.single p
        modelTrafo = Trafo3d.Translation p
        geoUnit    = Some unitCode }

/// The legend: code, name, colour.
let legend =
    [ "Np", "Northern plains",        C4b(141, 211, 199)
      "Cr", "Crater floor",           C4b(253, 180,  98)
      "Sw", "Southern rough, west",   C4b(190, 186, 218)
      "Se", "Southern smooth, east",  C4b(128, 177, 211)
      "Bf", "Boulder field",          C4b(251, 128, 114) ]

/// Writes the scene and returns its path.
let build (outDir : string) =
    Startup.init ()
    do Aardvark.Base.Aardvark.UnpackNativeDependencies(
        typeof<PRo3D.Extensions.FSharp.CooTransformation.RelState>.Assembly)
    let appData = Path.combine [Environment.GetFolderPath Environment.SpecialFolder.ApplicationData; "Pro3D"]
    CooTransformation.initCooTrafo None appData

    let opcDir =
        match TestUtils.Roots.dimorphosOpc None with
        | Some d -> d
        | None -> failwith "no Dimorphos OPC: set PRO3D_TEST_DATA to a PRo3D.Resources.TestData checkout"

    match Render.context.Value with
    | None -> failwith "no GL runtime"
    | Some _ -> ()

    let freshModel, update = Render.makeViewer ()
    if not (Directory.Exists outDir) then Directory.CreateDirectory outDir |> ignore
    let scenePath = Path.Combine(outDir, "dimorphos-geological-map.pro3d")

    let imported = update (freshModel ()) (ViewerAction.ImportSurface [ opcDir ])

    // ---- contacts, as directions ------------------------------------------------------
    let wavy =
        Array.init 721 (fun i ->
            let lon = float (i % 720) * 0.5
            dir (10.0 + 8.0 * sin (2.0 * lon * Constant.RadiansPerDegree)) lon)
    let crater = loop (dir 40.0 30.0) 28.0 400
    // from lat 15 on longitude 120 over the south pole to lat 15 on longitude 300
    let meridian =
        let e = dir 0.0 120.0
        // 15 -> -90 (the pole) -> -195, i.e. back up to latitude 15 on the far side
        Array.init 421 (fun i ->
            let phi = (15.0 - float i * 0.5) * Constant.RadiansPerDegree
            (e * cos phi + V3d.OOI * sin phi).Normalized)
    let boulders = loop (dir -50.0 210.0) 12.0 180
    let dangling = arc (dir 60.0 180.0) (dir 45.0 220.0) 60

    let mutable cache = HashMap.empty
    let drapeNamed (name : string) (ds : V3d[]) =
        let pts, c = drapeAll imported cache ds
        cache <- c
        if pts.Length < ds.Length then
            Log.warn "[geomap showcase] %s: %d of %d samples missed the surface" name (ds.Length - pts.Length) ds.Length
        name, pts

    let contacts =
        [ drapeNamed "equatorial contact" wavy
          drapeNamed "crater rim" crater
          drapeNamed "southern meridian" meridian
          drapeNamed "boulder field rim" boulders
          drapeNamed "unfinished contact" dangling ]

    // ---- label points ------------------------------------------------------------------
    let labels =
        [ "Np", dir 60.0 100.0
          "Cr", dir 40.0 30.0
          "Sw", dir -30.0 160.0
          "Se", dir -40.0 30.0
          "Bf", dir -50.0 210.0 ]
        |> List.choose (fun (code, d) ->
            let p, c = drape imported cache d
            cache <- c
            p |> Option.map (fun p -> code, p))

    let contactAnnotations = contacts |> List.map (fun (name, pts) -> polyline pts name)
    let labelAnnotations = labels |> List.map (fun (code, p) -> labelPoint p code)

    // ---- what the map will show: checked here, so a broken showcase fails loudly --------
    let topology =
        let contact (a : Annotation) : ContactTopology.Contact =
            { ContactTopology.Contact.id = a.key; ContactTopology.Contact.points = IndexList.toArray a.points }
        let cs = contactAnnotations |> List.map contact |> Array.ofList
        let ls =
            labelAnnotations |> List.choose (fun a ->
                match a.geoUnit, IndexList.tryFirst a.points with
                | Some u, Some p -> Some ({ id = a.key; position = p; unit = u } : ContactTopology.Label)
                | _ -> None)
            |> Array.ofList
        let spacing =
            cs |> Array.collect (fun c -> Array.init (c.points.Length - 1) (fun i -> Vec.Distance(c.points.[i], c.points.[i + 1])))
            |> Array.average
        let sw = System.Diagnostics.Stopwatch.StartNew()
        ContactTopology.compute V3d.Zero (0.3 * spacing) cs ls |> ignore
        let first = sw.Elapsed.TotalMilliseconds
        sw.Restart()
        let r = ContactTopology.compute V3d.Zero (0.3 * spacing) cs ls
        Log.line "[geomap showcase] topology: %.1f ms cold, %.1f ms warm" first sw.Elapsed.TotalMilliseconds
        r
    Log.line "[geomap showcase] %d units, %d open ends, euler %b; units: %A"
        topology.faces.Length topology.openEnds.Length topology.eulerOk
        (topology.faces |> Array.map (fun f -> f.unit))
    if topology.faces.Length <> 6 then failwithf "expected 6 units, got %d" topology.faces.Length
    if not topology.eulerOk then failwith "topology check failed"

    // ---- into the annotation tree: one group holding the map's sources -------------------
    let groupName = "Dimorphos geology"
    let annotations = GroupsApp.addGroupToRoot imported.drawing.annotations groupName
    let groupIndex, group =
        annotations.rootGroup.subNodes |> IndexList.toSeqIndexed |> Seq.tryFind (fun (_, n) -> n.name = groupName)
        |> function Some g -> g | None -> failwith "group not created"
    let leaves =
        (contactAnnotations @ labelAnnotations) |> List.map Leaf.Annotations |> IndexList.ofList
    let annotations = GroupsApp.addLeaves [ groupIndex ] leaves annotations

    let map =
        { GeologicalMap.make "Dimorphos units" group.key with
            units = legend |> List.map (fun (code, name, color) -> { GeoUnit.make code color with name = name }) |> IndexList.ofList }

    let withMap =
        { imported with
            drawing = { imported.drawing with
                          annotations = annotations
                          geologicalMaps = { GeologicalMapsModel.initial with maps = IndexList.single map; active = Some map.key } } }

    let surfaceId =
        match withMap.scene.surfacesModel.surfaces.flat |> HashMap.toList with
        | (id, _) :: _ -> id
        | [] -> failwith "no surface"
    let framed =
        update withMap (ViewerAction.SurfaceActions (SurfaceAppAction.FlyToSurface surfaceId))
        |> Render.runAnimationToCompletion update

    update framed (ViewerAction.SaveAs scenePath) |> ignore

    // reopen: a showcase that does not load is worse than none
    let reopened = update (freshModel ()) (ViewerAction.LoadScene scenePath)
    let maps = reopened.drawing.geologicalMaps.maps
    if maps.Count <> 1 then failwithf "reopened with %d maps" maps.Count
    // group keys are renewed on load; the map must follow its group
    for m in maps do
        let ids = PRo3D.Core.Drawing.GeologicalMapApp.leafIdsOf reopened.drawing.annotations.rootGroup m.sourceGroup
        if ids.Length <> contactAnnotations.Length + labelAnnotations.Length then
            failwithf "reopened map '%s' finds %d annotations in its group" m.name ids.Length
    let labelled =
        reopened.drawing.annotations.flat |> HashMap.toList |> List.filter (fun (_, l) ->
            match l with
            | Leaf.Annotations a -> a.geoUnit.IsSome
            | _ -> false)
    if labelled.Length <> labels.Length then failwithf "reopened with %d label points, wrote %d" labelled.Length labels.Length

    Log.line "[geomap showcase] wrote %s" scenePath
    scenePath
