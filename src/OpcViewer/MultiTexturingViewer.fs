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
open System.IO
open System.Collections.Generic
open Aardvark.Geometry
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis.Opc.KdTrees
open OpcViewer.Base
open OpcViewer.Base.KdTrees
open PRo3D.Core.Surface
open PRo3DCompability

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

    let private computeBarycentric (tri: Triangle3d) (p: V3d) =
        let v0 = tri.P1 - tri.P0
        let v1 = tri.P2 - tri.P0
        let v2 = p - tri.P0
        let d00 = Vec.dot v0 v0
        let d01 = Vec.dot v0 v1
        let d11 = Vec.dot v1 v1
        let d20 = Vec.dot v2 v0
        let d21 = Vec.dot v2 v1
        let denom = d00 * d11 - d01 * d01
        if abs denom < 1e-20 then V3d(1.0/3.0, 1.0/3.0, 1.0/3.0)
        else
            let v = (d11 * d20 - d01 * d21) / denom
            let w = (d00 * d21 - d01 * d20) / denom
            let u = 1.0 - v - w
            V3d(u, v, w)

    let private triangleIsNaN (tri: Triangle3d) =
        tri.P0.AnyNaN || tri.P1.AnyNaN || tri.P2.AnyNaN

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

        // Step 2: Build PatchFileInfo lookup keyed by objectSetPath
        let patchInfoLookup =
            let dict = Dictionary<string, PatchFileInfo * OpcPaths>()
            for h in patchHierarchies do
                let leaves = QTree.getLeaves h.tree |> Seq.toArray
                for leaf in leaves do
                    let info = leaf.info
                    let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
                    let objectSetPath = dir +/ info.Positions
                    dict.[objectSetPath] <- (info, OpcPaths h.opcPaths.Opc_DirAbsPath)
            dict
        Log.line "[Extraction] built patch info lookup with %d entries" patchInfoLookup.Count

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

        // Step 3: Cache for triangle-to-grid index mapping
        let triGridMappingCache = Dictionary<string, int[][]>()

        let buildTriangleToGridMapping (affine: Trafo3d) (objectSetPath: string) =
            match triGridMappingCache.TryGetValue(objectSetPath) with
            | true, mapping -> mapping
            | _ ->
                let positions = objectSetPath |> Aara.fromFile<V3f>
                let invalidIndices = DebugKdTreesX.getInvalidIndices3f positions.Data |> List.toArray
                let size = positions.Size.XY.ToV2i()
                let indices = PRo3DCSharp.ComputeIndexArray(size, invalidIndices)
                let transformedPositions =
                    positions.Data |> Array.map (fun x -> x.ToV3d() |> affine.Forward.TransformPos)
                let mapping =
                    indices
                    |> Array.chunkBySize 3
                    |> Array.filter (fun chunk ->
                        if chunk.Length < 3 then false
                        else
                            let tri = Triangle3d(transformedPositions.[chunk.[0]], transformedPositions.[chunk.[1]], transformedPositions.[chunk.[2]])
                            not (triangleIsNaN tri)
                    )
                triGridMappingCache.[objectSetPath] <- mapping
                Log.line "[Extraction] built triangle mapping for %s (%d triangles)" (Path.GetFileName(Path.GetDirectoryName(objectSetPath))) mapping.Length
                mapping

        // Step 4: Load texture coordinates and interpolate UV at hit point
        let getUVAtHit (coordinatesPath: string) (gridMapping: int[][]) (triIdx: int) (triangle: Triangle3d) (hitPoint: V3d) =
            let texCoords = coordinatesPath |> Aara.fromFile<V2f>
            // V-flip to match Patch.load convention
            let texCoordData = texCoords.Data |> Array.map (fun v -> V2f(v.X, 1.0f - v.Y))
            let gridIndices = gridMapping.[triIdx]
            let uv0 = texCoordData.[gridIndices.[0]]
            let uv1 = texCoordData.[gridIndices.[1]]
            let uv2 = texCoordData.[gridIndices.[2]]
            let bary = computeBarycentric triangle hitPoint
            let u = float32 bary.X * uv0.X + float32 bary.Y * uv1.X + float32 bary.Z * uv2.X
            let v = float32 bary.X * uv0.Y + float32 bary.Y * uv1.Y + float32 bary.Z * uv2.Y
            V2f(u, v)

        // Step 5: Load textures and sample at UV
        let extractAttributesAtUV (uv: V2f) (patchInfo: PatchFileInfo) (opcPaths: OpcPaths) =
            let numTextures = patchInfo.Textures.Length / 2 // first half = textures, second half = weights
            let results = Dictionary<string, float[]>()
            for i in 1 .. numTextures - 1 do
                let texturePath = 
                    try 
                        Patch.extractTexturePath opcPaths patchInfo i |> Some
                    with e -> 
                        None
                match texturePath with 
                | None -> ()
                | Some texturePath -> 
                    let texName = Path.GetFileNameWithoutExtension(texturePath)
                    let format = PixImage.GetFormatOfExtension texturePath

                    let readPixelValue (img: PixImage) (px: int) (py: int) =
                        match img with
                        | :? PixImage<byte> as byteImg ->
                            let channels = int byteImg.ChannelCount
                            Some [| for c in 0 .. channels - 1 do float (byteImg.GetChannel(int64 c).[int64 px, int64 py]) / 255.0 |]
                        | :? PixImage<float32> as floatImg ->
                            let channels = int floatImg.ChannelCount
                            Some [| for c in 0 .. channels - 1 do float (floatImg.GetChannel(int64 c).[int64 px, int64 py]) |]
                        | :? PixImage<uint16> as uint16Img ->
                            let channels = int uint16Img.ChannelCount
                            Some [| for c in 0 .. channels - 1 do float (uint16Img.GetChannel(int64 c).[int64 px, int64 py]) / 65535.0 |]
                        | _ ->
                            Log.warn "[Extraction] unsupported pixel format for %s: %A" texName (img.GetType())
                            None

                    if format = PixFileFormat.Exr then
                        // EXR: load channel 0 (OPC texture layers are typically single-channel scalar data)
                        use stream = Prinziple.openRead texturePath
                        let mipMap = TextureLoading.loadImageFromStream format (ChannelReference.ChannelWithIndex 0) stream
                        match mipMap.ImageArray |> Seq.tryHead with
                        | Some img ->
                            let w = img.Size.X
                            let h = img.Size.Y
                            let px = clamp 0 (w - 1) (int (uv.X * float32 w))
                            let py = clamp 0 (h - 1) (int (uv.Y * float32 h))
                            match readPixelValue img px py with
                            | Some values -> results.[texName] <- values
                            | None -> ()
                        | None ->
                            Log.warn "[Extraction] no image in EXR mipmap for texture %d" i
                    else
                        // DDS/TIFF: load all channels at once
                        use stream = Prinziple.openRead texturePath
                        let mipMap = TextureLoading.loadImageFromStream format ChannelReference.NoChannelSelection stream
                        match mipMap.ImageArray |> Seq.tryHead with
                        | Some img ->
                            let w = img.Size.X
                            let h = img.Size.Y
                            let px = clamp 0 (w - 1) (int (uv.X * float32 w))
                            let py = clamp 0 (h - 1) (int (uv.Y * float32 h))
                            match readPixelValue img px py with
                            | Some values -> results.[texName] <- values
                            | None -> ()
                        | None ->
                            Log.warn "[Extraction] no image in mipmap for texture %d" i
              
            results

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

        // intersect ray against all loaded KdTrees, return closest hit + the Level0KdTree that was hit
        let intersectAllKdTrees (ray : FastRay3d) =
            let mutable bestHit : (ObjectRayHit * Level0KdTree) option = None
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
                                    bestHit <- Some (hit, lvl0Tree)
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

        // Step 6: Extract attributes from a KdTree hit
        let extractAttributesFromHit (ray: FastRay3d) (hit: ObjectRayHit) (lvl0Tree: Level0KdTree) =
            match lvl0Tree with
            | Level0KdTree.LazyKdTree kd ->
                try
                    let gridMapping = buildTriangleToGridMapping kd.affine kd.objectSetPath
                    let triIdx = hit.SetObject.Index
                    let triangleSet = hit.SetObject.Set :?> TriangleSet
                    let triangle = DebugKdTreesX.getTriangle triangleSet triIdx
                    let hitPoint = ray.Ray.GetPointOnRay(hit.RayHit.T)
                    let uv = getUVAtHit kd.coordinatesPath gridMapping triIdx triangle hitPoint
                    Log.line "[Extraction] Position: %A, UV: %A" hitPoint uv

                    match patchInfoLookup.TryGetValue(kd.objectSetPath) with
                    | true, (patchInfo, opcPaths) ->
                        let attributes = extractAttributesAtUV uv patchInfo opcPaths
                        for kvp in attributes do
                            Log.line "[Extraction]   %s: %A" kvp.Key kvp.Value
                    | _ ->
                        Log.warn "[Extraction] no patch info found for %s" kd.objectSetPath
                with e ->
                    Log.warn "[Extraction] error: %A" e
            | Level0KdTree.InCoreKdTree _ ->
                Log.warn "[Extraction] InCoreKdTree not supported for attribute extraction"

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
                | Some (hit, lvl0Tree) ->
                    let pt = ray.Ray.GetPointOnRay(hit.RayHit.T)
                    extractAttributesFromHit ray hit lvl0Tree
                    yield pt
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

                // process KdTree hit for normal computation, segment sampling, and attribute extraction
                match kdHit with
                | Some (hit, lvl0Tree) ->
                    let kdPos = ray.Ray.GetPointOnRay(hit.RayHit.T)
                    let triIdx = hit.SetObject.Index
                    let normal = getNormalFromHit ray hit
                    Log.line "KdTree pick: %A (triangle: %d, normal: %A)" kdPos triIdx normal

                    // Step 6: extract attributes at the picked point
                    extractAttributesFromHit ray hit lvl0Tree

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