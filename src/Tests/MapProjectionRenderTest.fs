/// Rung 3 of the map projection testing ladder (#772): the Dimorphos OPC rendered headless
/// into the map, through the same `MapSg.surfaces` the panel uses.
///
/// The source of truth is `Projection` (double, CPU), never the shader itself: a debug
/// effect writes each fragment's interpolated (longitude, latitude, radius), and every
/// covered pixel must show the longitude/latitude that inverting its own position through
/// `Projection` gives. A broken projection, a y-flip, a torn seam or a missing pole cap all
/// fail that check -- "something was drawn" would pass all of them.
///
/// Self-skips without a GL context or without PRO3D_TEST_DATA (HERA/Dimorphos_opc/Dimorphos).
module MapProjectionRenderTest

open System
open System.IO

open Expecto

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive
open Aardvark.GeoSpatial.Opc.Load
open Aardvark.Data.Opc
open Aardvark.GeoSpatial.Opc

open PRo3D.Core
open PRo3D.MapProjection
open PRo3D.Tests
open PRo3D.Tool.SunAnglesVerb

let private dimorphosOpc () =
    TestUtils.Roots.testData None
    |> Option.map (fun root -> Path.Combine(root, "HERA", "Dimorphos_opc", "Dimorphos"))
    |> Option.filter Directory.Exists

let private outputDir () =
    let d = Path.Combine(AppContext.BaseDirectory, "map-projection-output")
    Directory.CreateDirectory d |> ignore
    d

/// Whether row 0 of a `FloatTarget` readback is the top of the image, measured rather than
/// assumed: a quad covering the upper half of NDC is drawn into the same kind of target.
let private rowZeroIsTop (runtime : IRuntime) =
    let target = FloatTarget.create runtime (V2i(8, 8))
    try
        let upperHalf =
            IndexedGeometry(
                IndexedGeometryMode.TriangleList,
                [| 0; 1; 2; 0; 2; 3 |],
                SymDict.ofList [ DefaultSemantic.Positions, [| V3f(-1, 0, 0); V3f(1, 0, 0); V3f(1, 1, 0); V3f(-1, 1, 0) |] :> Array ],
                SymDict.empty)
        let sg =
            Sg.ofIndexedGeometry upperHalf
            |> Sg.shader {
                do! DefaultSurfaces.trafo
                do! DefaultSurfaces.constantColor C4f.White
            }
        let img = FloatTarget.render target 1 sg
        (img.GetChannel Col.Channel.Alpha).[4, 0] > 0.5f
    finally
        FloatTarget.dispose target

type private Rendered =
    {
        image    : PixImage<float32>
        viewProj : Trafo3d
        size     : V2i
        maxR     : float
    }

/// Which LoD decider a render uses: always finest (phase 1), or the map-space heuristic (1.5).
type private Lod = Finest | MapLod

let private renderAt (runtime : IRuntime) (opc : string) (kind : MapProjectionKind) (effects : FShade.Effect[])
                     (size : V2i) (lod : Lod) (center : V2d) (zoom : float) =
    let hierarchies = MapSg.hierarchiesOf opc
    let maxR = MapSg.maxRadius hierarchies
    let viewProj = Projection.viewProj kind Projection.defaultMaxColatitude center zoom size
    let target = FloatTarget.create runtime size
    try
        let runner = runtime.CreateLoadRunner 1
        let decider =
            match lod with
            | Finest -> MapSg.finestLod
            | MapLod ->
                MapSg.mapLod (AVal.constant kind) (AVal.constant viewProj) (AVal.constant size)
                             (AVal.constant Projection.defaultMaxColatitude) MapSg.defaultTargetPixels
        let cfg = { OpcSg.defaultConfig target.signature runner decider "DIMORPHOS" with asyncLoading = false }
        let view : MapSg.MapView =
            {
                kind          = AVal.constant kind
                viewProj      = AVal.constant viewProj
                maxColatitude = AVal.constant Projection.defaultMaxColatitude
                unitsPerPixel = AVal.constant 1.0
                radiusRange   = AVal.constant (Projection.radiusRange maxR)
            }
        let surface : MapSg.MapSurface =
            { hierarchies = hierarchies; placement = AVal.constant Trafo3d.Identity; visible = AVal.constant true }
        let sg =
            MapSg.surfaces cfg effects view (ASet.single surface)
            // the LoD decider reads the scope camera; the map effects ignore it
            |> Sg.viewTrafo (AVal.constant Trafo3d.Identity)
            |> Sg.projTrafo (AVal.constant Trafo3d.Identity)
        // warm-up frames: the LoD tree refines only after a frame has been rendered
        { image = FloatTarget.render target 4 sg; viewProj = viewProj; size = size; maxR = maxR }
    finally
        FloatTarget.dispose target

let private render runtime opc kind effects size = renderAt runtime opc kind effects size Finest V2d.Zero 1.0

/// Map-space position of pixel (x, y) of a readback.
let private mapPos (r : Rendered) (top : bool) (x : int) (y : int) =
    let row = if top then y else r.size.Y - 1 - y
    Projection.pixelToMap r.viewProj r.size (V2d(float x + 0.5, float row + 0.5))

let private savePng (path : string) (img : PixImage<float32>) (toColor : float32 -> float32 -> float32 -> float32 -> C3b) (top : bool) =
    let size = img.Size
    let out = PixImage<byte>(Col.Format.RGB, size)
    let mutable m = out.GetMatrix<C3b>()
    let r = img.GetChannel Col.Channel.Red
    let g = img.GetChannel Col.Channel.Green
    let b = img.GetChannel Col.Channel.Blue
    let a = img.GetChannel Col.Channel.Alpha
    for y in 0 .. size.Y - 1 do
        let row = if top then y else size.Y - 1 - y
        for x in 0 .. size.X - 1 do
            m.[int64 x, int64 row] <- toColor r.[x, y] g.[x, y] b.[x, y] a.[x, y]
    out.Save path

let private positionColor (x : float32) (y : float32) (_ : float32) (a : float32) =
    if a <= 0.5f then C3b.Black
    else
        let ll = Projection.lonLatR (V3d(float x, float y, 0.0))
        C3b(byte (255.0 * (ll.X / Projection.twoPi + 0.5)), 128uy, 128uy)

/// Angle between two directions on the unit sphere.
let private angleBetween (lon0 : float) (lat0 : float) (lon1 : float) (lat1 : float) =
    let dir (lon : float) (lat : float) = V3d(cos lat * cos lon, cos lat * sin lon, sin lat)
    let d = Vec.dot (dir lon0 lat0) (dir lon1 lat1)
    acos (clamp -1.0 1.0 d)

type private Agreement =
    {
        /// fraction of the considered pixels that are covered
        coverage     : float
        /// worst angle between a pixel's own lon/lat and its surface point, in map pixels
        worstPixels  : float
        badRadius    : int
    }

/// Compares, for every covered pixel, the lon/lat its position means (`Projection.inverse`)
/// with the lon/lat of the surface point drawn there (the interpolated body position).
/// `consider` selects the pixels (map position -> lon/lat -> bool); `pixelAngle` converts
/// the error to map pixels. `flip` mirrors the rows: a check that also passes flipped
/// measures nothing.
let private agreement (r : Rendered) (kind : MapProjectionKind) (top : bool) (flip : bool)
                      (consider : V2d -> V2d -> bool) (pixelAngle : float) =
    let px = r.image.GetChannel Col.Channel.Red
    let py = r.image.GetChannel Col.Channel.Green
    let pz = r.image.GetChannel Col.Channel.Blue
    let cov = r.image.GetChannel Col.Channel.Alpha
    let mutable considered = 0
    let mutable covered = 0
    let mutable worst = 0.0
    let mutable badRadius = 0
    for y in 0 .. r.size.Y - 1 do
        for x in 0 .. r.size.X - 1 do
            let m = mapPos r (top <> flip) x y
            let expected = Projection.inverse kind m
            if consider m expected then
                considered <- considered + 1
                if cov.[x, y] > 0.5f then
                    covered <- covered + 1
                    let llr = Projection.lonLatR (V3d(float px.[x, y], float py.[x, y], float pz.[x, y]))
                    worst <- max worst (angleBetween llr.X llr.Y expected.X expected.Y)
                    if not (llr.Z > 0.0 && llr.Z <= r.maxR * 1.001) then badRadius <- badRadius + 1
    { coverage = float covered / float (max 1 considered); worstPixels = worst / pixelAngle; badRadius = badRadius }

// ---- annotations (phase 2) -----------------------------------------------------------------

/// The synthetic annotations (MapProjectionAnnotationFixture) alone, through MapAnnotations.sg.
let private renderAnnotations (runtime : IRuntime) (kind : MapProjectionKind) (size : V2i) (center : V2d) (zoom : float) =
    let viewProj = Projection.viewProj kind Projection.defaultMaxColatitude center zoom size
    let target = FloatTarget.create runtime size
    try
        let view : MapSg.MapView =
            {
                kind          = AVal.constant kind
                viewProj      = AVal.constant viewProj
                maxColatitude = AVal.constant Projection.defaultMaxColatitude
                unitsPerPixel = AVal.constant 1.0
                radiusRange   = AVal.constant (Projection.radiusRange 100.0)
            }
        let inputs =
            { MapAnnotations.none with
                annotations = MapAnnotations.ofList (MapProjectionAnnotationFixture.all |> List.map (fun c -> c.annotation)) }
        let sg =
            MapAnnotations.sg inputs view
            // thickLine widens lines in pixels; a render control provides this uniform itself
            |> Sg.uniform "ViewportSize" (AVal.constant size)
            |> Sg.viewTrafo (AVal.constant Trafo3d.Identity)
            |> Sg.projTrafo (AVal.constant Trafo3d.Identity)
        { image = FloatTarget.render target 2 sg; viewProj = viewProj; size = size; maxR = 100.0 }
    finally
        FloatTarget.dispose target

/// Pixel (column, readback row) of a map-space point, if it is inside the image.
let private pixelOf (r : Rendered) (top : bool) (m : V2d) =
    let ndc = r.viewProj.Forward.TransformPos(V3d(m, 0.0))
    let x = int (floor ((ndc.X + 1.0) * 0.5 * float r.size.X))
    let rowTop = int (floor ((1.0 - ndc.Y) * 0.5 * float r.size.Y))
    if x < 0 || x >= r.size.X || rowTop < 0 || rowTop >= r.size.Y then None
    else Some (x, (if top then rowTop else r.size.Y - 1 - rowTop))

/// Where the CPU projection puts each annotation's check points, in map space: along every
/// segment of a line (unwrapped from its first end, the way the geometry stage draws it), at a
/// point, or inside a fill. Each with the colour the pixel must have.
let private expectations (kind : MapProjectionKind) =
    let lonLat (p : V3d) = let l = Projection.lonLatR p in V2d(l.X, l.Y)
    [
        for c in MapProjectionAnnotationFixture.all do
            let color = c.color.ToC4f()
            let expected = V3d(float color.R, float color.G, float color.B)
            let pts = c.annotation.points |> IndexList.toArray |> Array.map lonLat
            match c.annotation.geometry with
            | PRo3D.Base.Annotation.Geometry.Point ->
                for p in pts do
                    yield c.name, Projection.forward kind p.X p.Y, expected, false
            | _ ->
                // the ellipse and the lines: samples along every segment, away from its ends
                for i in 0 .. pts.Length - 2 do
                    let a, b = pts.[i], pts.[i + 1]
                    for t in [ 0.3; 0.5; 0.7 ] do
                        match kind with
                        | MapProjectionKind.Equirectangular ->
                            let bx = a.X + Projection.wrapPi (b.X - a.X)
                            yield c.name, V2d(a.X + t * (bx - a.X), a.Y + t * (b.Y - a.Y)), expected, false
                        | _ ->
                            // the polar maps drop a segment lying entirely beyond their cutoff
                            let sign = Projection.polarSign kind
                            let beyond (ll : V2d) = Projection.colatitude sign ll.Y > Projection.defaultMaxColatitude
                            if not (beyond a && beyond b) then
                                let pa = Projection.forward kind a.X a.Y
                                let pb = Projection.forward kind b.X b.Y
                                yield c.name, pa + t * (pb - pa), expected, false
                let fillVisible (ll : V2d) =
                    kind = MapProjectionKind.Equirectangular
                    || Projection.colatitude (Projection.polarSign kind) ll.Y <= Projection.defaultMaxColatitude
                if c.annotation.showFill && pts.Length > 1 && fillVisible pts.[0] then
                    // well inside the polygon: 50 % fill over the transparent clear colour
                    let centre = pts |> Array.take (pts.Length - 1) |> Array.fold (+) V2d.Zero |> fun s -> s / float (pts.Length - 1)
                    yield c.name + " (fill)", Projection.forward kind centre.X centre.Y, expected * float c.annotation.fillAlpha.value, true
    ]

type private AnnotationCheck = { checkedPoints : int; wrong : list<string>; offScreen : int }

let private checkAnnotations (r : Rendered) (kind : MapProjectionKind) (top : bool) =
    let red = r.image.GetChannel Col.Channel.Red
    let green = r.image.GetChannel Col.Channel.Green
    let blue = r.image.GetChannel Col.Channel.Blue
    let alpha = r.image.GetChannel Col.Channel.Alpha
    let mutable checkedPoints = 0
    let mutable offScreen = 0
    let wrong = Collections.Generic.List<string>()
    for name, m, expected, isFill in expectations kind do
        // the map repeats every 2 pi: the copy on screen, if any
        let onScreen =
            match kind with
            | MapProjectionKind.Equirectangular ->
                [ 0.0; Projection.twoPi; -Projection.twoPi ] |> List.tryPick (fun dx -> pixelOf r top (m + V2d(dx, 0.0)))
            | _ -> pixelOf r top m
        match onScreen with
        | None -> offScreen <- offScreen + 1
        | Some (x, y) ->
            checkedPoints <- checkedPoints + 1
            let got = V3d(float red.[x, y], float green.[x, y], float blue.[x, y])
            let tolerance = if isFill then 0.15 else 0.05
            if alpha.[x, y] <= 0.0f || (got - expected).NormMax > tolerance then
                wrong.Add(sprintf "%s at pixel (%d, %d): %A, expected %A" name x y got expected)
    { checkedPoints = checkedPoints; wrong = List.ofSeq wrong; offScreen = offScreen }

let tests () =
    testSequenced <| testList "map projection render (#772)" [

        test "annotations: every line, point, fill and ellipse lands where the CPU projection puts it" {
            match Render.context.Value with
            | None -> skiptest "no OpenGL runtime in this environment"
            | Some (runtime, _) ->

            Startup.init ()
            let top = rowZeroIsTop runtime
            for name, kind, size, center, zoom, minChecked in
                    [ "equirectangular", MapProjectionKind.Equirectangular, V2i(1024, 512), V2d.Zero, 1.0, 200
                      // only the seam line's middle segments fall into this window
                      "equirectangular zoom 6 across the seam", MapProjectionKind.Equirectangular, V2i(1024, 512), V2d(Projection.pi - 0.05, 0.2), 6.0, 8
                      "polar north", MapProjectionKind.PolarNorth, V2i(768, 768), V2d.Zero, 1.0, 50 ] do
                let r = renderAnnotations runtime kind size center zoom
                savePng (Path.Combine(outputDir (), sprintf "annotations-%s.png" (name.Replace(' ', '-'))))
                    r.image (fun rr g b a -> if a <= 0.0f then C3b.Black else C3b(byte (255.0f * rr), byte (255.0f * g), byte (255.0f * b))) top
                let check = checkAnnotations r kind top
                let flipped = checkAnnotations r kind (not top)
                printfn "annotations, %s: %d points checked (%d off screen), %d wrong; flipped: %d wrong"
                    name check.checkedPoints check.offScreen check.wrong.Length flipped.wrong.Length
                for w in check.wrong |> List.truncate 8 do printfn "  %s" w
                Expect.isGreaterThanOrEqual check.checkedPoints minChecked (sprintf "%s: enough annotation pixels on screen" name)
                Expect.isEmpty check.wrong (sprintf "%s: every annotation pixel has its annotation's colour" name)
                Expect.isGreaterThan flipped.wrong.Length (check.checkedPoints / 2) (sprintf "%s: the check would catch a mirrored map" name)
        }

        test "equirectangular: every pixel shows the surface point at its own longitude and latitude" {
            match Render.context.Value, dimorphosOpc () with
            | None, _ -> skiptest "no OpenGL runtime in this environment"
            | _, None -> skiptest "no Dimorphos OPC: set PRO3D_TEST_DATA to a PRo3D.Resources.TestData checkout (HERA/Dimorphos_opc/Dimorphos)"
            | Some (runtime, _), Some opc ->

            Startup.init ()
            let top = rowZeroIsTop runtime
            let kind = MapProjectionKind.Equirectangular
            let r = render runtime opc kind MapSg.bodyPositionEffects (V2i(1024, 512))
            savePng (Path.Combine(outputDir (), "equirect-position.png")) r.image positionColor top

            let pixelAngle = Projection.pi / float r.size.Y
            // the pole caps approximate the last row of triangles; leave the outermost degree out
            let belowPoleCaps (_ : V2d) (ll : V2d) = abs ll.Y < Projection.halfPi - Projection.pi / 180.0
            let a = agreement r kind top false belowPoleCaps pixelAngle
            let flipped = agreement r kind top true belowPoleCaps pixelAngle
            printfn "equirectangular: coverage %.4f, worst %.2f px (flipped: %.1f px), row 0 is top: %b"
                a.coverage a.worstPixels flipped.worstPixels top

            // the SPC shape model is closed: the whole 360 x 180 degree rectangle is surface
            Expect.isGreaterThan a.coverage 0.999 "the map is covered, seam included"
            Expect.isLessThan a.worstPixels 1.5 "every pixel shows the surface point at its longitude and latitude"
            Expect.isGreaterThan flipped.worstPixels 20.0 "the check would catch an upside-down map"
            Expect.equal a.badRadius 0 "every radius lies within the body's bounding radius"

            // the seam and the poles are drawn: first/last column and top/bottom row covered
            let cov = r.image.GetChannel Col.Channel.Alpha
            let fraction (cells : seq<int * int>) =
                let cells = Seq.toArray cells
                float (cells |> Array.sumBy (fun (x, y) -> if cov.[x, y] > 0.5f then 1 else 0)) / float cells.Length
            for x in [ 0; r.size.X - 1 ] do
                Expect.isGreaterThan (fraction (seq { for y in 0 .. r.size.Y - 1 -> x, y })) 0.999 (sprintf "column %d at the 180 degree seam" x)
            for y in [ 0; r.size.Y - 1 ] do
                Expect.isGreaterThan (fraction (seq { for x in 0 .. r.size.X - 1 -> x, y })) 0.999 (sprintf "row %d at a pole" y)
        }

        test "polar stereographic: the hemisphere disc shows the surface point at each pixel" {
            match Render.context.Value, dimorphosOpc () with
            | None, _ -> skiptest "no OpenGL runtime in this environment"
            | _, None -> skiptest "no Dimorphos OPC: set PRO3D_TEST_DATA to a PRo3D.Resources.TestData checkout (HERA/Dimorphos_opc/Dimorphos)"
            | Some (runtime, _), Some opc ->

            Startup.init ()
            let top = rowZeroIsTop runtime
            for kind in [ MapProjectionKind.PolarNorth; MapProjectionKind.PolarSouth ] do
                let r = render runtime opc kind MapSg.bodyPositionEffects (V2i(768, 768))
                savePng (Path.Combine(outputDir (), sprintf "%A-position.png" kind)) r.image positionColor top

                // d(colatitude) <= d(rho) on the stereographic plane, so a map-space pixel bounds the angle
                let pixelAngle = (Projection.extent kind Projection.defaultMaxColatitude).Size.X / float r.size.X
                let insideDisc (m : V2d) (_ : V2d) = m.Length < 1.97
                let a = agreement r kind top false insideDisc pixelAngle
                let flipped = agreement r kind top true insideDisc pixelAngle
                printfn "%A: disc coverage %.4f, worst %.2f px (flipped: %.1f px)" kind a.coverage a.worstPixels flipped.worstPixels
                Expect.isGreaterThan a.coverage 0.999 (sprintf "%A: the hemisphere disc is covered" kind)
                Expect.isLessThan a.worstPixels 1.5 (sprintf "%A: every pixel shows the surface point at its position" kind)
                Expect.isGreaterThan flipped.worstPixels 20.0 (sprintf "%A: the check would catch a mirrored map" kind)
                Expect.equal a.badRadius 0 (sprintf "%A: radii within the body" kind)
        }

        test "map LoD (phase 1.5) draws the root at zoom 1 and refines where the view zooms in" {
            match dimorphosOpc () with
            | None -> skiptest "no Dimorphos OPC: set PRO3D_TEST_DATA to a PRo3D.Resources.TestData checkout (HERA/Dimorphos_opc/Dimorphos)"
            | Some opc ->
            let serializer = MBrace.FsPickler.FsPickler.CreateBinarySerializer()
            let hierarchy = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths (MapSg.hierarchiesOf opc).[0])
            let size = V2i(1024, 512)
            /// the patches drawn: RoseTree.filter keeps descending while the decider says so
            let drawn (kind : MapProjectionKind) (center : V2d) (zoom : float) =
                let vp = Projection.viewProj kind Projection.defaultMaxColatitude center zoom size
                let lod = MapSg.mapLod (AVal.constant kind) (AVal.constant vp) (AVal.constant size) (AVal.constant Projection.defaultMaxColatitude) MapSg.defaultTargetPixels
                let decide (p : Patch) =
                    let rp : PatchLod.RenderPatch =
                        { info = p.info; level = p.level; triangleSize = p.triangleSize; trafo = AVal.constant p.info.Local2Global
                          modality = ViewerModality.XYZ; coordinates = PatchLod.CoordinatesMapping.Local }
                    lod AdaptiveToken.Top (AVal.constant Trafo3d.Identity) (AVal.constant Trafo3d.Identity) rp (AVal.constant Unchecked.defaultof<_>) (AVal.constant true)
                let rec walk (t : QTree<Patch>) =
                    match t with
                    | QTree.Leaf p -> [ p.info.Name ]
                    | QTree.Node (p, children) ->
                        if decide p then children |> Array.toList |> List.collect walk else [ p.info.Name ]
                walk hierarchy.tree
            let whole = drawn MapProjectionKind.Equirectangular V2d.Zero 1.0
            let zoomed = drawn MapProjectionKind.Equirectangular (V2d(0.3, 0.2)) 16.0
            let polarZoomed = drawn MapProjectionKind.PolarNorth (V2d(0.3, -0.2)) 8.0
            printfn "map LoD patches: zoom 1 %A; zoom 16 %A; polar zoom 8 %A" whole zoomed polarZoomed
            Expect.equal whole [ "2_0_0" ] "the whole map at 1024 px needs only the root"
            Expect.isTrue (zoomed |> List.exists (fun n -> n.StartsWith "0_")) "zoomed in, leaves are drawn"
            // No off-screen culling to expect here: both level-1 patches of this OPC wrap around the
            // body centre, so they have no direction to cull by and refine by triangle size alone.
            Expect.isTrue (polarZoomed |> List.exists (fun n -> n.StartsWith "0_")) "polar zoomed in, leaves are drawn"
        }

        test "map LoD (phase 1.5): whole map and zoomed-in window still show every surface point exactly" {
            match Render.context.Value, dimorphosOpc () with
            | None, _ -> skiptest "no OpenGL runtime in this environment"
            | _, None -> skiptest "no Dimorphos OPC: set PRO3D_TEST_DATA to a PRo3D.Resources.TestData checkout (HERA/Dimorphos_opc/Dimorphos)"
            | Some (runtime, _), Some opc ->

            Startup.init ()
            let top = rowZeroIsTop runtime
            let size = V2i(1024, 512)
            let everyPixel (_ : V2d) (ll : V2d) = abs ll.Y < Projection.halfPi - Projection.pi / 180.0
            for name, kind, center, zoom in
                    [ "whole equirectangular map", MapProjectionKind.Equirectangular, V2d.Zero, 1.0
                      "zoom 16 across the seam", MapProjectionKind.Equirectangular, V2d(Projection.pi - 0.05, 0.3), 16.0
                      "polar north zoom 4", MapProjectionKind.PolarNorth, V2d(0.3, -0.2), 4.0 ] do
                let r = renderAt runtime opc kind MapSg.bodyPositionEffects size MapLod center zoom
                savePng (Path.Combine(outputDir (), sprintf "lod-%s.png" (name.Replace(' ', '-')))) r.image positionColor top
                // a map pixel spans 1/pixelsPerUnit map units; in radians that bounds the angle
                let pixelAngle = 2.0 / (float size.X * r.viewProj.Forward.M00)
                let consider (m : V2d) (ll : V2d) =
                    everyPixel m ll && (kind = MapProjectionKind.Equirectangular || m.Length < 1.97)
                let a = agreement r kind top false consider pixelAngle
                printfn "map LoD, %s: coverage %.4f, worst %.2f px" name a.coverage a.worstPixels
                Expect.isGreaterThan a.coverage 0.999 (sprintf "%s: covered" name)
                // coarser patches are chords of the surface: allow a little more than finest
                Expect.isLessThan a.worstPixels 2.0 (sprintf "%s: surface points where the pixels say" name)
        }

        // What the texture looks like is the data's business: the test OPC's default layer
        // (DRACO_1) is the raw DRACO frame stored as a 2:1 map raster, so the correct map shows
        // that photo, undistorted. Geometry is covered above; this pins "texture reaches the map".
        test "textured equirectangular map renders deterministically with image content" {
            match Render.context.Value, dimorphosOpc () with
            | None, _ -> skiptest "no OpenGL runtime in this environment"
            | _, None -> skiptest "no Dimorphos OPC: set PRO3D_TEST_DATA to a PRo3D.Resources.TestData checkout (HERA/Dimorphos_opc/Dimorphos)"
            | Some (runtime, _), Some opc ->

            Startup.init ()
            let top = rowZeroIsTop runtime
            let size = V2i(1024, 512)
            let a = render runtime opc MapProjectionKind.Equirectangular MapSg.surfaceEffects size
            let b = render runtime opc MapProjectionKind.Equirectangular MapSg.surfaceEffects size
            let gray (r : float32) (g : float32) (bl : float32) (al : float32) =
                if al <= 0.0f then C3b(64uy, 0uy, 64uy)
                else C3b(byte (255.0f * clamp 0.0f 1.0f r), byte (255.0f * clamp 0.0f 1.0f g), byte (255.0f * clamp 0.0f 1.0f bl))
            savePng (Path.Combine(outputDir (), "equirect-texture.png")) a.image gray top

            let ra = a.image.GetChannel Col.Channel.Red
            let rb = b.image.GetChannel Col.Channel.Red
            let distinct = Collections.Generic.HashSet<int>()
            let mutable differing = 0
            for y in 0 .. size.Y - 1 do
                for x in 0 .. size.X - 1 do
                    distinct.Add(int (ra.[x, y] * 255.0f)) |> ignore
                    if ra.[x, y] <> rb.[x, y] then differing <- differing + 1
            printfn "textured map: %d distinct red levels, %d differing pixels between renders" distinct.Count differing
            Expect.isGreaterThan distinct.Count 32 "the surface texture shows up, not a flat colour"
            Expect.equal differing 0 "two renders are identical"
        }
    ]
