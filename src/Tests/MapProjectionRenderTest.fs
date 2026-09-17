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

let private render (runtime : IRuntime) (opc : string) (kind : MapProjectionKind) (effects : FShade.Effect[]) (size : V2i) =
    let hierarchies = MapSg.hierarchiesOf opc
    let maxR = MapSg.maxRadius hierarchies
    let viewProj = Projection.viewProj kind Projection.defaultMaxColatitude V2d.Zero 1.0 size
    let target = FloatTarget.create runtime size
    try
        let runner = runtime.CreateLoadRunner 1
        let cfg = { OpcSg.defaultConfig target.signature runner MapSg.finestLod "DIMORPHOS" with asyncLoading = false }
        let view : MapSg.MapView =
            {
                kind          = AVal.constant kind
                viewProj      = AVal.constant viewProj
                maxColatitude = AVal.constant Projection.defaultMaxColatitude
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

let tests () =
    testSequenced <| testList "map projection render (#772)" [

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
