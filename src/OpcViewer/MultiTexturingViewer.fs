namespace Aardvark.Opc

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Data.Opc
open Aardvark.Application.Slim
open Aardvark.GeoSpatial.Opc

open FSharp.Data.Adaptive 
open MBrace.FsPickler

open Aardvark.GeoSpatial.Opc.Load

open System
open Aardvark.Geometry
open OpcViewer.Base
open PRo3D.Core.Surface

module private MultiTexturingShader =

    open Aardvark.Base
    open Aardvark.Rendering
    open Aardvark.Rendering.Effects

    open FShade

    let LoDColor  (v : Vertex) =
        fragment {
            if uniform?LodVisEnabled then
                let c : V4f = uniform?LoDColor
                let gamma = 1.0f
                let grayscale = 0.2126f * v.c.X ** gamma + 0.7152f * v.c.Y ** gamma  + 0.0722f * v.c.Z ** gamma
                return grayscale * c
            else return v.c
        }

    let stableTrafo (v : Vertex) =
        vertex {
            let vp = uniform.ModelViewTrafo * v.pos
            let wp = uniform.ModelTrafo * v.pos
            return {
                pos = uniform.ProjTrafo * vp
                wp = wp
                n = uniform.NormalMatrix * v.n
                b = uniform.NormalMatrix * v.b
                t = uniform.NormalMatrix * v.t
                c = v.c
                tc = v.tc
            }
        }

    let private secondaryTextureSampler =
        sampler2d {
            texture uniform?SecondaryTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let private transferFunctionSampler =
        sampler2d {
            texture uniform?TransferFunction
            filter Filter.MinMagPoint
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
        }

    type WeightedVertex =
        {
            [<Color>]
            c: V4f
            [<TexCoord>]
            tc: V2f
            [<Semantic("SecondaryWeight")>]
            weight : float32
        }

    type UniformScope with
        member x.UseSecondary : bool = uniform?UseSecondary
        member x.SecondaryOpacity : float32 = uniform?SecondaryOpacity
        member x.SecondaryMinMax : V2f = uniform?SecondaryMinMax

    let secondaryTexture (v : WeightedVertex) =
        fragment {
            let useSecondary = uniform.UseSecondary
            let opacity : float32 = uniform.SecondaryOpacity
            if useSecondary then
                let range = uniform.SecondaryMinMax.Y - uniform.SecondaryMinMax.X
                let e = (secondaryTextureSampler.Sample(v.tc) - uniform.SecondaryMinMax.X) / range
                let c = transferFunctionSampler.Sample(V2f(e.X, 0.0f))
                return V4f(Fun.Lerp(min v.weight opacity, v.c.XYZ, c.XYZ), 1.0f)
            else
                return v.c
        }


module MultiTexturingViewer =


    let run (scene : OpcScene) =

        Aardvark.Init()

        use app = new OpenGlApplication()
        let win = app.CreateGameWindow(samples = 1)
        let runtime = win.Runtime

        let runner = runtime.CreateLoadRunner 1

        let serializer = FsPickler.CreateBinarySerializer()


        // texture controls
        let primaryTexture = cval 1
        let secondaryTexture = cval { texture = TextureReference.LegacyId 0; channel = ChannelReference.ChannelWithIndex 0 }
        let useSecondary = cval false
        let secondaryOpacity = cval 1.0

        let attributeParameters =
            primaryTexture |> AVal.map (fun primaryTextureId ->
                let id = { texture = TextureReference.LegacyId primaryTextureId; channel = ChannelReference.NoChannelSelection }
                { selectedTexture = Some id; selectedScalar = None }
            )

        let patchHierarchies, hierarchies =
            scene.patchHierarchies |> Seq.toList |> List.map (fun basePath ->
                let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                let t = PatchLod.toRoseTree h.tree
                let paths = OpcPaths basePath
                let loader = Aardvark.Data.PixImagePfim.Loader
                let sg =
                    Sg.patchLodMultiTexturingWithLoader win.FramebufferSignature runner basePath
                        scene.lodDecider SecondaryTexture.getSecondary false false
                        ViewerModality.XYZ PatchLod.CoordinatesMapping.Local true t
                        (Some (SecondaryTexture.textures paths)) (Some (SecondaryTexture.vertexAttributes paths)) loader
                h, sg
            ) |> List.unzip

        // load KdTrees for ray intersection picking
        let kdTreeMap =
            patchHierarchies |> List.fold (fun acc h ->
                let trees =
                    KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ
                        serializer false true DebugKdTreesX.loadTriangles' false
                HashMap.union acc trees
            ) HashMap.empty

        Log.line "[KdTree] loaded %d kd-tree entries" (kdTreeMap |> HashMap.count)

        let kdCache = ref (HashMap.empty<string, ConcreteKdIntersectionTree>)

        let speed = AVal.init scene.speed

        let bb = scene.boundingBox
        let initialView = CameraView.lookAt (bb.Min * 2.0) bb.Center bb.Center.Normalized
        let view = initialView |> DefaultCameraController.controlWithSpeed speed win.Mouse win.Keyboard win.Time
        let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 scene.near scene.far (float s.X / float s.Y))

        let viewTrafo = view |> AVal.map CameraView.viewTrafo
        let projTrafo = frustum |> AVal.map Frustum.projTrafo

        // construct a world-space ray from the current mouse position
        let getPickRay () =
            let pos = win.Mouse.Position.GetValue()
            let vt = viewTrafo.GetValue()
            let pt = projTrafo.GetValue()
            let np = V2d(pos.NormalizedPosition.X, 1.0 - pos.NormalizedPosition.Y) * 2.0 - V2d.II
            let nearNdc = V3d(np, -1.0)
            let farNdc  = V3d(np,  1.0)
            let nearWorld = (vt * pt).Inverse.TransformPosProj(nearNdc)
            let farWorld  = (vt * pt).Inverse.TransformPosProj(farNdc)
            let dir = (farWorld - nearWorld) |> Vec.normalize
            FastRay3d(Ray3d(nearWorld, dir))

        // intersect ray against all loaded KdTrees, return closest hit
        let intersectAllKdTrees (ray : FastRay3d) =
            let mutable bestHit : ObjectRayHit option = None
            let mutable bestT = Double.MaxValue
            for (bb, lvl0Tree) in kdTreeMap |> HashMap.toList do
                let mutable tmin = 0.0
                let mutable tmax = Double.MaxValue
                if ray.Intersects(bb, &tmin, &tmax) then
                    let kdTree, newCache = DebugKdTreesX.loadObjectSet kdCache.Value lvl0Tree
                    kdCache.Value <- newCache
                    if not (isNull kdTree.KdIntersectionTree.ObjectSet) then
                        let kdi = kdTree.KdIntersectionTree
                        let mutable hit = ObjectRayHit.MaxRange
                        let hitFilter = Func<IIntersectableObjectSet,int,int,RayHit3d,bool>(fun _ _ _ _ -> false)
                        try
                            if kdi.Intersect(ray, null, hitFilter, 0.0, Double.MaxValue, &hit) then
                                if hit.RayHit.T < bestT then
                                    bestT <- hit.RayHit.T
                                    bestHit <- Some hit
                        with e ->
                            Log.error "[KdTree] intersection error: %A" e
            bestHit

        // extract face normal from a KdTree hit, oriented toward the ray origin
        let getNormalFromHit (ray : FastRay3d) (hit : ObjectRayHit) =
            try
                let triangleSet = hit.SetObject.Set :?> TriangleSet
                let triangle = DebugKdTreesX.getTriangle triangleSet hit.SetObject.Index
                let normal = triangle.Normal
                if Vec.dot normal ray.Ray.Direction > 0.0 then -normal else normal
            with e ->
                Log.warn "[KdTree] could not extract normal from hit: %A" e
                V3d.OOI

        // sample interior points along a segment by shooting rays into the surface
        let sampleSegment (p0 : V3d) (n0 : V3d) (p1 : V3d) (n1 : V3d) =
            let avgNormal = (n0 + n1) |> Vec.normalize
            let segmentLength = Vec.distance p0 p1
            let offset = max 1.0 (segmentLength * 0.5)
            let numSteps = 5
            [| for i in 1 .. numSteps - 1 do
                let t = float i / float numSteps
                let linePoint = p0 * (1.0 - t) + p1 * t
                let rayOrigin = linePoint + avgNormal * offset
                let rayDir = -avgNormal
                let ray = FastRay3d(Ray3d(rayOrigin, rayDir))
                match intersectAllKdTrees ray with
                | Some hit ->
                    yield ray.Ray.GetPointOnRay(hit.RayHit.T)
                | None ->
                    Log.warn "[Sampling] no KdTree hit for sample at t=%.2f" t
            |]

        let lodVisEnabled = cval true
        let fillMode = cval FillMode.Fill

        // collected picked points (world space)
        let pickedPoints = cval [||] : cval<V3d[]>

        // KdTree pick info: (position, normal) for each pick, used for sampling
        let pickInfos = ref ([||] : (V3d * V3d)[])

        // accumulated sampled points along line segments
        let sampledPoints = cval [||] : cval<V3d[]>

        win.Keyboard.KeyDown(Keys.PageUp).Values.Add(fun _ ->
            transact (fun _ -> speed.Value <- speed.Value * 1.5)
        )

        win.Keyboard.KeyDown(Keys.PageDown).Values.Add(fun _ ->
            transact (fun _ -> speed.Value <- speed.Value / 1.5)
        )

        win.Keyboard.KeyDown(Keys.L).Values.Add(fun _ ->
            transact (fun _ -> lodVisEnabled.Value <- not lodVisEnabled.Value)
        )

        win.Keyboard.KeyDown(Keys.F).Values.Add(fun _ ->
            transact (fun _ ->
                fillMode.Value <-
                    match fillMode.Value with
                    | FillMode.Fill -> FillMode.Line
                    | _-> FillMode.Fill
            )
        )

        // Z: cycle primary texture
        win.Keyboard.KeyDown(Keys.Z).Values.Add(fun _ ->
            transact (fun _ -> primaryTexture.Value <- primaryTexture.Value + 1)
            Log.line "primary texture: %d" primaryTexture.Value
        )

        // T: cycle secondary texture
        win.Keyboard.KeyDown(Keys.T).Values.Add(fun _ ->
            transact (fun _ ->
                secondaryTexture.Value <-
                    let t =
                        match secondaryTexture.Value.texture with
                        | TextureReference.LegacyId id -> TextureReference.LegacyId (id + 1)
                        | _ -> secondaryTexture.Value.texture
                    { secondaryTexture.Value with texture = t }
            )
            Log.line "secondary texture: %A" secondaryTexture.Value
        )

        // C: cycle secondary texture channel
        win.Keyboard.KeyDown(Keys.C).Values.Add(fun _ ->
            transact (fun _ ->
                secondaryTexture.Value <-
                    let c =
                        match secondaryTexture.Value.channel with
                        | ChannelReference.ChannelWithIndex id -> ChannelReference.ChannelWithIndex (id + 1)
                        | _ -> secondaryTexture.Value.channel
                    { secondaryTexture.Value with channel = c }
            )
            Log.line "secondary texture: %A" secondaryTexture.Value
        )

        // M: toggle secondary texture overlay
        win.Keyboard.KeyDown(Keys.M).Values.Add(fun _ ->
            transact (fun _ -> useSecondary.Value <- not useSecondary.Value)
            Log.line "use secondary: %A" useSecondary.Value
        )

        // Up/Down: adjust secondary texture opacity
        win.Keyboard.KeyDown(Keys.Up).Values.Add(fun _ ->
            transact (fun _ -> secondaryOpacity.Value <- clamp 0.0 1.0 (secondaryOpacity.Value + 0.1))
            Log.line "secondary opacity: %A" secondaryOpacity.Value
        )

        win.Keyboard.KeyDown(Keys.Down).Values.Add(fun _ ->
            transact (fun _ -> secondaryOpacity.Value <- clamp 0.0 1.0 (secondaryOpacity.Value - 0.1))
            Log.line "secondary opacity: %A" secondaryOpacity.Value
        )

        // clear picked points and sampled points
        win.Keyboard.KeyDown(Keys.Escape).Values.Add(fun _ ->
            transact (fun _ ->
                pickedPoints.Value <- [||]
                sampledPoints.Value <- [||]
            )
            pickInfos.Value <- [||]
        )

        // line visualization from picked points
        let lineSg =
            (pickedPoints :> aval<_>)
            |> AVal.map (fun (ptsWS : V3d[]) ->
                if ptsWS.Length < 2 then
                    Sg.empty
                else
                    let localShift = ptsWS.[0]
                    let modelTrafo = Trafo3d.Translation(localShift)
                    let relativePts = ptsWS |> Array.map modelTrafo.Backward.TransformPos

                    let ig =
                        IndexedGeometry(
                            Mode = IndexedGeometryMode.LineStrip,
                            IndexedAttributes =
                                SymDict.ofList [
                                    DefaultSemantic.Positions, (relativePts |> Array.map V3f :> System.Array)
                                ]
                        )

                    Sg.ofIndexedGeometry ig
                    |> Sg.uniform "LineWidth" (AVal.constant 3.0)
                    |> Sg.shader {
                        do! DefaultSurfaces.stableTrafo
                        do! DefaultSurfaces.thickLine
                        do! DefaultSurfaces.constantColor C4f.Yellow
                    }
                    |> Sg.trafo' modelTrafo
            )
            |> Sg.dynamic

        // point markers at each picked location
        let pointsSg =
            (pickedPoints :> aval<_>)
            |> AVal.map (fun (ptsWS : V3d[]) ->
                ptsWS
                |> Array.map (fun p ->
                    Sg.sphere' 4 C4b.Red 0.3
                    |> Sg.translation (AVal.constant p)
                    |> Sg.shader {
                        do! DefaultSurfaces.trafo
                        do! DefaultSurfaces.constantColor C4f.Red
                    }
                )
                |> Array.toList
                |> Sg.ofList
            )
            |> Sg.dynamic

        // sampled point markers along line segments
        let sampledPointsSg =
            (sampledPoints :> aval<_>)
            |> AVal.map (fun (pts : V3d[]) ->
                pts
                |> Array.map (fun p ->
                    Sg.sphere' 4 C4b.Green 0.2
                    |> Sg.translation (AVal.constant p)
                    |> Sg.shader {
                        do! DefaultSurfaces.trafo
                        do! DefaultSurfaces.constantColor C4f.Green
                    }
                )
                |> Array.toList
                |> Sg.ofList
            )
            |> Sg.dynamic

        let tf =
            let stream = typeof<PatchLod.PatchNode>.Assembly.GetManifestResourceStream("Aardvark.GeoSpatial.Opc.resources.colormap.png")
            PixTexture2d(PixImageMipMap(PixImage.Load(stream)), false)

        let surfaceSg =
            Sg.ofList hierarchies
            |> Sg.AttributeParameters attributeParameters
            |> Sg.effect [
                        MultiTexturingShader.stableTrafo |> toEffect
                        DefaultSurfaces.constantColor C4f.White |> toEffect
                        DefaultSurfaces.diffuseTexture |> toEffect
                        MultiTexturingShader.LoDColor |> toEffect
                        MultiTexturingShader.secondaryTexture |> toEffect
                    ]
            |> Sg.uniform  "UseSecondary" useSecondary
            |> Sg.uniform  "SecondaryOpacity" secondaryOpacity
            |> Sg.uniform' "SecondaryMinMax" (V2d(0.0, 0.1))
            |> Sg.texture' "TransferFunction" tf
            |> Sg.uniform  "LodVisEnabled" lodVisEnabled
            |> Sg.fillMode fillMode
            |> SecondaryTexture.Sg.applySecondaryTextureId (secondaryTexture |> AVal.map Some)

        let sceneSg =
            Sg.ofList [ surfaceSg; lineSg; pointsSg; sampledPointsSg ]
            |> Sg.viewTrafo viewTrafo
            |> Sg.projTrafo projTrafo

        // render to offscreen color + depth textures (enables depth readback for picking)
        let color, sceneDepth =
            let clearColor =
                clear {
                    color C4f.Black
                    depth 1.0
                }
            sceneSg
            |> Sg.compile runtime win.FramebufferSignature
            |> RenderTask.renderToColorAndDepthWithClear win.Sizes clearColor

        // display the color texture on a fullscreen quad
        let fullScreenPass =
            Sg.fullScreenQuad
            |> Sg.diffuseTexture color
            |> Sg.shader {
                do! DefaultSurfaces.diffuseTexture
            }

        // depth readback: unproject screen position to world space
        let getPos () =
            let depthTexture = sceneDepth.GetValue()
            let pos = win.Mouse.Position.GetValue()
            let vt = viewTrafo.GetValue()
            let pt = projTrafo.GetValue()
            if Vec.allSmaller pos.Position depthTexture.Size.XY && Vec.allGreater pos.Position V2i.Zero then
                let p = V2i(pos.Position.X, pos.Position.Y)
                let np = V2d(pos.NormalizedPosition.X, 1.0 - pos.NormalizedPosition.Y) * 2.0 - V2d.II
                let depth = depthTexture.DownloadDepth(region = Box2i.FromMinAndSize(p, V2i(1,1)))[0,0]
                let ndc = V3d(np, float depth * 2.0 - 1.0)
                let wp = (vt * pt).Inverse.TransformPosProj(ndc)
                Some wp
            else
                Log.warn "out of bounds"
                None

        // double-click to pick a point on the surface (depth buffer + KdTree with sampling)
        win.Mouse.DoubleClick.Values.Add(fun e ->
            if e = MouseButtons.Left then
                let depthHit = getPos ()
                let ray = getPickRay ()
                let kdHit = intersectAllKdTrees ray

                // add depth-buffer position to visual markers
                match depthHit with
                | Some p ->
                    transact (fun _ ->
                        pickedPoints.Value <- Array.append pickedPoints.Value [| p |]
                    )
                    Log.line "Depth pick: %A (total: %d)" p pickedPoints.Value.Length
                | None ->
                    Log.warn "Depth pick: no hit"

                // process KdTree hit for normal computation and segment sampling
                match kdHit with
                | Some hit ->
                    let kdPos = ray.Ray.GetPointOnRay(hit.RayHit.T)
                    let triIdx = hit.SetObject.Index
                    let normal = getNormalFromHit ray hit
                    Log.line "KdTree pick: %A (triangle: %d, normal: %A)" kdPos triIdx normal

                    // sample the new segment if there is a previous pick
                    let prevInfos = pickInfos.Value
                    if prevInfos.Length > 0 then
                        let (prevPos, prevNormal) = prevInfos.[prevInfos.Length - 1]
                        let newSamples = sampleSegment prevPos prevNormal kdPos normal
                        transact (fun _ ->
                            sampledPoints.Value <- Array.append sampledPoints.Value newSamples
                        )
                        Log.line "Sampled %d points along segment" newSamples.Length

                    pickInfos.Value <- Array.append prevInfos [| (kdPos, normal) |]

                    // log distance between depth and KdTree picks
                    match depthHit with
                    | Some dp -> Log.line "Distance between picks: %.6f" (Vec.distance dp kdPos)
                    | None -> ()
                | None ->
                    Log.warn "KdTree pick: no hit"
        )

        win.RenderTask <- Sg.compile runtime win.FramebufferSignature fullScreenPass
        win.Run()
        0