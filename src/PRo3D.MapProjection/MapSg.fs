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

            let bb = patch.info.GlobalBoundingBox
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

    /// Largest distance of any vertex from the body centre, bounded by the root bounding
    /// boxes of the hierarchies -- the depth range of the map.
    let maxRadius (hierarchies : seq<string>) =
        let serializer = FsPickler.CreateBinarySerializer()
        hierarchies
        |> Seq.fold (fun acc basePath ->
            let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
            let root =
                match h.tree with
                | QTree.Node (p, _) -> p
                | QTree.Leaf p -> p
            root.info.GlobalBoundingBox.ComputeCorners()
            |> Array.fold (fun m c -> max m c.Length) acc
        ) 0.0

    /// Index into `effects` for a projection kind.
    let effectIndex (kind : MapProjectionKind) =
        match kind with
        | MapProjectionKind.Equirectangular -> 0
        | _ -> 1

    /// The map uniforms, bound once above everything the map draws.
    let private withMapUniforms (view : MapView) (sg : ISg) =
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

    let graticule (view : MapView) : ISg =
        let lines =
            (view.kind, view.maxColatitude) ||> AVal.map2 graticuleLines
        Sg.draw IndexedGeometryMode.LineList
        |> Sg.vertexAttribute DefaultSemantic.Positions (lines |> AVal.map (Array.collect (fun (a, b, _) -> [| a; b |])))
        |> Sg.vertexAttribute DefaultSemantic.Colors    (lines |> AVal.map (Array.collect (fun (_, _, c) -> [| c; c |])))
        |> Sg.effect [ Shaders.graticuleEffect ]
        |> Sg.depthTest (AVal.constant DepthTest.None)
        |> withMapUniforms view

    /// The whole map: textured surfaces with the graticule on top.
    let map (cfg : OpcSg.Config) (view : MapView) (mapSurfaces : aset<MapSurface>) : ISg =
        let grid =
            graticule view
            // after the surfaces, so the grid lies on top of the map
            |> Sg.pass (RenderPass.after "map-graticule" RenderPassOrder.Arbitrary RenderPass.main)
        Sg.ofList [ surfaces cfg surfaceEffects view mapSurfaces; grid ]
