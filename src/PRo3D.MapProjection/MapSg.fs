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
