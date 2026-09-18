namespace PRo3D.MapProjection

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

open MBrace.FsPickler
open Aardvark.Data.Opc
open Aardvark.GeoSpatial.Opc

open PRo3D.Core

/// Scene graphs of the map projection view. The panel and the headless tests build the map
/// through these functions, so what the tests render is what the panel shows.
module MapSg =

    /// One OPC surface as the map sees it.
    type MapSurface =
        {
            /// patch hierarchy directories (each containing `Patches`)
            hierarchies : string[]
            /// body-fixed placement; identity for a surface of the scene body
            placement   : aval<Trafo3d>
            visible     : aval<bool>
        }

    /// Everything about the map that is not a surface.
    type MapView =
        {
            kind          : aval<MapProjectionKind>
            /// map space -> NDC (`Projection.viewProj`)
            viewProj      : aval<Trafo3d>
            /// polar cutoff (`Projection.defaultMaxColatitude`)
            maxColatitude : aval<float>
            /// map units one pixel covers: the size of whatever is drawn at a constant screen size
            unitsPerPixel : aval<float>
            /// radius interval normalised into depth (`Projection.radiusRange`)
            radiusRange   : aval<V2d>
        }

    /// Always refine. Level 0 of a small body fits into memory (Dimorphos test data: 6 leaf
    /// patches), so phase 1 draws everything at full detail; a map-space decider is phase 1.5.
    let finestLod : PatchLod.LodDecider = fun _ _ _ _ _ _ -> true

    /// Map-space level of detail (phase 1.5): refine a patch only while it is on the map
    /// window and its triangles (`RenderPatch.triangleSize`, the level's average size in
    /// metres) still cover more than `targetPixels` map pixels.
    ///
    /// A deliberately coarse heuristic, conservative where it cannot be exact: the patch is a
    /// bounding sphere (centre direction +- angular radius); longitude stretch toward the poles
    /// (equirectangular, capped) and the stereographic scale sec^2(colatitude/2) (polar) are
    /// taken at the patch's far edge; a patch around a pole or wider than half the map counts as
    /// visible, and one wrapping around the body centre is judged by triangle size alone. Not
    /// refining only means the coarser parent is drawn: nothing disappears.
    let mapLod (kind : aval<MapProjectionKind>) (viewProj : aval<Trafo3d>) (viewport : aval<V2i>)
               (maxColatitude : aval<float>) (targetPixels : float) : PatchLod.LodDecider =
        fun self _ _ patch _ _ ->
            let kind = kind.GetValue self
            let vp = (viewProj.GetValue self).Forward
            let size = viewport.GetValue self
            let colatMax = maxColatitude.GetValue self

            // placed as the shader places it: patch trafo = surface placement * Local2Global
            let bb = patch.info.LocalBoundingBox.Transformed(patch.trafo.GetValue self)
            let centre = bb.Center
            let dist = centre.Length
            let halfDiagonal = 0.5 * bb.Size.Length
            let pixelsPerUnit = 0.5 * float size.X * vp.M00
            if dist <= 1.01 * halfDiagonal then
                // the patch wraps around the body centre (on a small body most coarse patches do):
                // no direction to cull by, so only the triangle size decides, at its outer radius
                let outer = bb.ComputeCorners() |> Array.fold (fun m c -> max m c.Length) 1e-6
                (patch.triangleSize / outer) * pixelsPerUnit > targetPixels
            else
                let a = asin (halfDiagonal / dist)
                let ll = Projection.lonLatR centre
                // the map-space box [c - e, c + e] on screen?
                let onScreen (c : V2d) (e : V2d) =
                    let lo = vp.TransformPos(V3d(c - e, 0.0))
                    let hi = vp.TransformPos(V3d(c + e, 0.0))
                    min lo.X hi.X <= 1.0 && max lo.X hi.X >= -1.0 && min lo.Y hi.Y <= 1.0 && max lo.Y hi.Y >= -1.0
                let stretch, visible =
                    match kind with
                    | MapProjectionKind.Equirectangular ->
                        let latFar = abs ll.Y + a
                        if latFar >= Projection.halfPi then 20.0, true
                        else
                            let s = min 20.0 (1.0 / cos latFar)
                            let e = V2d(a * s, a)
                            if e.X >= Projection.pi then s, true
                            else
                                let c = V2d(ll.X, ll.Y)
                                s, (onScreen c e || onScreen (c + V2d(Projection.twoPi, 0.0)) e || onScreen (c - V2d(Projection.twoPi, 0.0)) e)
                    | _ ->
                        let sign = Projection.polarSign kind
                        let colat = Projection.colatitude sign ll.Y
                        if colat - a > colatMax then 1.0, false
                        else
                            let far = min (colat + a) colatMax
                            let s = 1.0 / (cos (0.5 * far) ** 2.0)
                            s, onScreen (Projection.polar sign ll.X ll.Y) (V2d(a * s, a * s))
                if not visible then false
                else
                    let trianglePixels = (patch.triangleSize / dist) * stretch * pixelsPerUnit
                    trianglePixels > targetPixels

    /// Default for `mapLod`: refine while triangles cover more than this many map pixels.
    let defaultTargetPixels = 2.0

    /// The hierarchies of an OPC directory: its subdirectories that contain `Patches`.
    let hierarchiesOf (opcDirectory : string) =
        Directory.GetDirectories opcDirectory
        |> Array.filter (fun d -> Directory.Exists(Path.Combine(d, "Patches")))

    /// Root bounding box of one hierarchy, loaded once per path and kept.
    ///
    /// The depth range, *Zoom to data* and the footprints all want it, and a Jezero scene has
    /// over a hundred surfaces: without the cache, opening the panel would read every hierarchy
    /// from disk once per consumer, on the thread that evaluates them.
    let private rootBoxCache = System.Collections.Concurrent.ConcurrentDictionary<string, Box3d>()

    let rootBox (basePath : string) =
        rootBoxCache.GetOrAdd(basePath, fun path ->
            let serializer = FsPickler.CreateBinarySerializer()
            let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths path)
            match h.tree with
            | QTree.Node (p, _) -> p.info.GlobalBoundingBox
            | QTree.Leaf p -> p.info.GlobalBoundingBox)

    /// Largest distance of any vertex from the body centre, bounded by the root bounding
    /// boxes of the hierarchies -- the depth range of the map.
    let maxRadius (hierarchies : seq<string>) =
        hierarchies
        |> Seq.fold (fun acc basePath ->
            (rootBox basePath).ComputeCorners()
            |> Array.fold (fun m c -> max m c.Length) acc
        ) 0.0

    /// Root bounding boxes of the hierarchies, in their global (unplaced) frame.
    let rootBoxes (hierarchies : seq<string>) =
        hierarchies |> Seq.map rootBox |> Seq.toArray

    /// The depth range of the map over placed surfaces: the largest distance from the body centre
    /// of any placed root bounding box corner.
    let placedMaxRadius (surfaces : aset<MapSurface>) : aval<float> =
        surfaces
        |> ASet.mapA (fun s ->
            // loaded once per surface, outside the evaluation
            let boxes = rootBoxes s.hierarchies
            s.placement |> AVal.map (fun t ->
                boxes |> Array.fold (fun m b -> b.Transformed(t).ComputeCorners() |> Array.fold (fun m c -> max m c.Length) m) 0.0))
        |> ASet.toAVal
        |> AVal.map (fun radii -> radii |> Seq.fold max 0.0)

    /// Index into `effects` for a projection kind.
    let effectIndex (kind : MapProjectionKind) =
        match kind with
        | MapProjectionKind.Equirectangular -> 0
        | _ -> 1

    /// The map uniforms, bound once above everything the map draws.
    let withMapUniforms (view : MapView) (sg : ISg) =
        sg
        |> Sg.uniform "MapViewProj" (view.viewProj |> AVal.map (fun t -> t.Forward))
        |> Sg.uniform "MapRadiusRange" (view.radiusRange |> AVal.map V2f)
        |> Sg.uniform "MapPolarSign" (view.kind |> AVal.map (Projection.polarSign >> float32))
        |> Sg.uniform "MapMaxColatitude" (view.maxColatitude |> AVal.map float32)

    /// OPC surfaces projected into the map.
    ///
    /// `effects` is indexed by `effectIndex` (equirectangular, polar); a local effect pool,
    /// so switching the projection swaps the program without reloading patches.
    /// `cfg.signature` must be the signature of the target the result is compiled for.
    let surfaces (cfg : OpcSg.Config) (effects : FShade.Effect[]) (view : MapView) (surfaces : aset<MapSurface>) : ISg =
        surfaces
        |> ASet.map (fun s ->
            Log.line "[map] drawing %d hierarchies: %s" s.hierarchies.Length (String.concat ", " s.hierarchies)
            OpcSg.build cfg (AVal.constant None) PRo3D.InstrumentVisualization.VisualizationProperties.empty s.hierarchies
            |> Sg.ofList
            |> Sg.trafo s.placement
            |> Sg.onOff s.visible
        )
        |> Sg.set
        |> Sg.effectPool effects (view.kind |> AVal.map effectIndex)
        // projections may flip winding, and the map is seen from outside the body anyway
        |> Sg.cullMode (AVal.constant CullMode.None)
        |> OpcSg.withOpcScaffolding
        |> withMapUniforms view

    let surfaceEffects = [| Shaders.surfaceEffect MapProjectionKind.Equirectangular; Shaders.surfaceEffect MapProjectionKind.PolarNorth |]

    let bodyPositionEffects = [| Shaders.bodyPositionEffect MapProjectionKind.Equirectangular; Shaders.bodyPositionEffect MapProjectionKind.PolarNorth |]

    /// Map-space line segments drawn through the map view-projection: the graticule, the data
    /// footprints and the camera marker are all this.
    let lineSg (view : MapView) (lines : aval<(V3f * V3f * C4b)[]>) : ISg =
        Sg.draw IndexedGeometryMode.LineList
        |> Sg.vertexAttribute DefaultSemantic.Positions (lines |> AVal.map (Array.collect (fun (a, b, _) -> [| a; b |])))
        |> Sg.vertexAttribute DefaultSemantic.Colors    (lines |> AVal.map (Array.collect (fun (_, _, c) -> [| c; c |])))
        |> Sg.effect [ Shaders.graticuleEffect ]
        |> Sg.depthTest (AVal.constant DepthTest.None)
        |> withMapUniforms view

    /// Map-space line segments of the graticule: every 15 degrees, equator yellow, prime
    /// meridian red (the colours of the LatLon shader).
    let graticuleLines (kind : MapProjectionKind) (maxColatitude : float) : (V3f * V3f * C4b)[] =
        let grid      = C4b(200, 200, 200, 255)
        let equator   = C4b.Yellow
        let meridian0 = C4b.Red
        let step = Projection.pi / 12.0
        let seg (a : V2d) (b : V2d) (c : C4b) = V3f(float32 a.X, float32 a.Y, 0.0f), V3f(float32 b.X, float32 b.Y, 0.0f), c
        match kind with
        | MapProjectionKind.Equirectangular ->
            [|
                for k in -12 .. 12 do
                    let x = float k * step
                    yield seg (V2d(x, -Projection.halfPi)) (V2d(x, Projection.halfPi)) (if k = 0 then meridian0 else grid)
                for j in -6 .. 6 do
                    let y = float j * step
                    yield seg (V2d(-Projection.pi, y)) (V2d(Projection.pi, y)) (if j = 0 then equator else grid)
            |]
        | _ ->
            let sign = Projection.polarSign kind
            let latOf colat = sign * (Projection.halfPi - colat)
            let segments = 180
            [|
                // parallels: circles at 15 degree steps of colatitude, down to the cutoff
                let rings = int (floor (maxColatitude / step + 1e-9))
                for ring in 1 .. rings do
                    let colat = float ring * step
                    let color = if abs (colat - Projection.halfPi) < 1e-9 then equator else grid
                    for i in 0 .. segments - 1 do
                        let l0 = Projection.twoPi * float i / float segments
                        let l1 = Projection.twoPi * float (i + 1) / float segments
                        yield seg (Projection.polar sign l0 (latOf colat)) (Projection.polar sign l1 (latOf colat)) color
                // meridians: from the pole out to the cutoff
                for k in 0 .. 23 do
                    let lon = float k * step
                    yield seg (V2d.Zero) (Projection.polar sign lon (latOf maxColatitude)) (if k = 0 then meridian0 else grid)
            |]

    /// Corners of the placed root bounding boxes, in the body-fixed frame the map projects.
    let placedCorners (surfaces : aset<MapSurface>) : aval<V3d[]> =
        surfaces
        |> ASet.mapA (fun s ->
            // loaded once per surface, outside the evaluation
            let boxes = rootBoxes s.hierarchies
            (s.placement, s.visible) ||> AVal.map2 (fun t visible ->
                if visible then boxes |> Array.collect (fun b -> b.Transformed(t).ComputeCorners()) else [||]))
        |> ASet.toAVal
        |> AVal.map (fun cs -> cs |> Seq.toArray |> Array.concat)

    /// Map-space box around everything the map draws, for *Zoom to data*.
    let dataExtent (kind : aval<MapProjectionKind>) (surfaces : aset<MapSurface>) : aval<Option<Box2d>> =
        (kind, placedCorners surfaces) ||> AVal.map2 Projection.mapBoxOf

    /// Line segments of a rectangle, sampled along the edges so that it follows the curvature
    /// of the polar map.
    let private rectangle (color : C4b) (box : Box2d) =
        let steps = 8
        let corner i =
            match i with
            | 0 -> box.Min
            | 1 -> V2d(box.Max.X, box.Min.Y)
            | 2 -> box.Max
            | _ -> V2d(box.Min.X, box.Max.Y)
        [|
            for edge in 0 .. 3 do
                let a = corner edge
                let b = corner ((edge + 1) % 4)
                for i in 0 .. steps - 1 do
                    let p0 = a + (b - a) * (float i / float steps)
                    let p1 = a + (b - a) * (float (i + 1) / float steps)
                    yield V3f(float32 p0.X, float32 p0.Y, 0.0f), V3f(float32 p1.X, float32 p1.Y, 0.0f), color
        |]

    let footprintColor = C4b(120, 200, 255, 255)
    let cameraColor    = C4b(255, 150, 40, 255)

    /// Smallest data footprint on screen, in pixels. A Jezero OPC is about 0.05 degrees across,
    /// which is a fifth of a pixel on a whole-Mars map: without a floor there is no way to see
    /// that there is data at all, let alone where to zoom.
    let footprintMinPixels = 9.0

    /// Above this size on screen the data speaks for itself and the rectangle is only clutter --
    /// on a small body, where the surfaces are the whole map, no footprint is ever drawn.
    let footprintMaxPixels = 400.0

    /// The rectangle to draw for a surface whose data covers `box` in map space, or None when the
    /// data is large enough on screen to speak for itself. Below `footprintMinPixels` the box is
    /// grown around its centre, which is what makes sub-pixel data on a planet visible at all.
    let footprintBox (unitsPerPixel : float) (box : Box2d) : Option<Box2d> =
        let onScreen = box.Size / unitsPerPixel
        if max onScreen.X onScreen.Y > footprintMaxPixels then None
        else
            let half = 0.5 * footprintMinPixels * unitsPerPixel
            let c = box.Center
            Some (Box2d(V2d(min box.Min.X (c.X - half), min box.Min.Y (c.Y - half)),
                        V2d(max box.Max.X (c.X + half), max box.Max.Y (c.Y + half))))

    /// Where the surfaces are: one rectangle per surface, never smaller than
    /// `footprintMinPixels`, drawn over the map.
    let footprints (view : MapView) (surfaces : aset<MapSurface>) : ISg =
        let lines =
            surfaces
            |> ASet.mapA (fun s ->
                // loaded once per surface, outside the evaluation
                let boxes = rootBoxes s.hierarchies
                adaptive {
                    let! visible = s.visible
                    if not visible then return [||]
                    else
                        let! placement = s.placement
                        let! kind = view.kind
                        let! unitsPerPixel = view.unitsPerPixel
                        let corners = boxes |> Array.collect (fun b -> b.Transformed(placement).ComputeCorners())
                        match Projection.mapBoxOf kind corners |> Option.bind (footprintBox unitsPerPixel) with
                        | None -> return [||]
                        | Some box -> return rectangle footprintColor box
                })
            |> ASet.toAVal
            |> AVal.map (fun s -> s |> Seq.toArray |> Array.concat)
        lineSg view lines

    /// The 3D view's camera on the map: a crosshair with a gap and a small box around the
    /// position, at a constant size on screen. On a planet the data is a speck, so this is
    /// what tells you where you are.
    let cameraMarker (view : MapView) (camera : aval<Option<V3d>>) : ISg =
        let lines =
            adaptive {
                let! camera = camera
                match camera with
                | None -> return [||]
                | Some position ->
                    let! kind = view.kind
                    let! unitsPerPixel = view.unitsPerPixel
                    match Projection.mapBoxOf kind [ position ] with
                    | None -> return [||]
                    | Some box ->
                        let c = box.Center
                        let u = unitsPerPixel
                        let seg (a : V2d) (b : V2d) =
                            V3f(float32 a.X, float32 a.Y, 0.0f), V3f(float32 b.X, float32 b.Y, 0.0f), cameraColor
                        let inner, outer, half = 5.0 * u, 12.0 * u, 3.0 * u
                        return
                            Array.append
                                [|
                                    seg (c + V2d(inner, 0.0)) (c + V2d(outer, 0.0))
                                    seg (c - V2d(inner, 0.0)) (c - V2d(outer, 0.0))
                                    seg (c + V2d(0.0, inner)) (c + V2d(0.0, outer))
                                    seg (c - V2d(0.0, inner)) (c - V2d(0.0, outer))
                                |]
                                (rectangle cameraColor (Box2d(c - V2d(half, half), c + V2d(half, half))))
            }
        lineSg view lines

    let graticule (view : MapView) : ISg =
        (view.kind, view.maxColatitude) ||> AVal.map2 graticuleLines |> lineSg view

    /// After the surfaces, so the grid lies on top of the map.
    let graticulePass = RenderPass.after "map-graticule" RenderPassOrder.Arbitrary RenderPass.main

    /// The whole map without annotations: textured surfaces with the graticule on top.
    let map (cfg : OpcSg.Config) (view : MapView) (camera : aval<Option<V3d>>) (mapSurfaces : aset<MapSurface>) : ISg =
        Sg.ofList [
            surfaces cfg surfaceEffects view mapSurfaces
            footprints view mapSurfaces |> Sg.pass graticulePass
            graticule view |> Sg.pass graticulePass
            cameraMarker view camera |> Sg.pass graticulePass
        ]
