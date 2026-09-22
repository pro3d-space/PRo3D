namespace PRo3D.Core.Surface

open System
open System.IO
open System.Collections.Generic

open Aardvark.Base
open Aardvark.Geometry
open Aardvark.Data.Opc
open Aardvark.Rendering
open Aardvark.SceneGraph.Opc
open Aardvark.GeoSpatial.Opc

open FSharp.Data.Adaptive

open OpcViewer.Base
open OpcViewer.Base.KdTrees
open Aardvark.VRVis.Opc.KdTrees

open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3DCompability

open System.Globalization

/// Compact triangle-to-position-grid mapping of one patch: two triangles per quad, so each
/// quad's top-left grid index is enough to recover any triangle's three grid indices.
/// Built by `TriangleSet.computeTriangleQuadStarts`, which documents how the numbering
/// follows the KdTree's object set.
type PatchTriangleGrid =
    {
        /// size of the patch's position grid (index = y * gridSize.X + x)
        gridSize  : V2i
        /// top-left grid index of each quad in triangle order, -1 where the triangles are
        /// degenerate fillers with no grid position
        quadStart : int[]
    }

    member x.TriangleCount = x.quadStart.Length * 2

    /// The three position grid indices of triangle `triangleIndex`, matching the winding
    /// `TriangleSet.computeGridIndices` produces (a,b,c then c,b,d). None for a filler
    /// triangle or an index outside the patch.
    member x.TryGetTriangleIndices (triangleIndex : int) =
        let quad = triangleIndex / 2
        if triangleIndex < 0 || quad >= x.quadStart.Length then None
        else
            let a = x.quadStart.[quad]
            if a < 0 then None
            else
                let b = a + x.gridSize.X
                if triangleIndex % 2 = 0 then Some [| a; b; a + 1 |]
                else Some [| a + 1; b; b + 1 |]

/// What to do for layers the per-vertex `*.aara` data does not cover.
[<RequireQualifiedAccess>]
type TextureFallback =
    /// Never decode textures. Required for interactive per-frame use such as the 3D
    /// cursor, where one image decode per layer per mouse move is not affordable.
    | Disabled
    /// Decode and point sample the patch's attribute textures. `scalarLayers` are the
    /// surface's `*.opcx` attribute layers as stored on `Surface.scalarLayers`; they carry
    /// the range the texture values are normalised into, which is needed to recover
    /// physical values.
    | Enabled of scalarLayers : HashMap<int, ScalarLayer>

/// Attributes sampled at a single picked surface point.
type AttributeHit = {
    position   : V3d
    /// name of the patch the hit landed in, e.g. "0_0_2"
    patchName  : string
    attributes : list<SampledAttribute>
}

/// Result of attribute extraction at a single sample point.
type ProfileSample = {
    position   : V3d
    distance   : float
    attributes : Dictionary<string, float[]>
}

module ProfileAttributeExtraction =

    let computeBarycentric (tri: Triangle3d) (p: V3d) =
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

    // Cached triangle-to-grid mappings, keyed by objectSetPath. Building one reads the
    // patch's whole position grid, so it is worth keeping - but each entry costs ~4 MB for
    // a 1032^2 patch, and the 3D cursor pulls in a new patch whenever it crosses a patch
    // boundary. Bounded so a long session cannot grow without limit.
    let private maxCachedTriangleGrids = 32
    let private triGridMappingCache = Dictionary<string, PatchTriangleGrid>()

    /// Maps triangles of a patch's KdTree TriangleSet back onto the position grid it was
    /// built from. `TriangleSet.computeTriangleQuadStarts` documents how the numbering has to
    /// follow the object set the KdTree was built from.
    let buildTriangleToGridMapping (affine: Trafo3d) (objectSetPath: string) =
        lock triGridMappingCache (fun () ->
            match triGridMappingCache.TryGetValue(objectSetPath) with
            | true, mapping -> mapping
            | _ ->
                let positions = objectSetPath |> Aara.fromFile<V3f>
                let size = positions.Size.XY.ToV2i()
                let mapping =
                    {
                        gridSize  = size
                        quadStart = TriangleSet.computeTriangleQuadStarts size positions.Data
                    }

                if triGridMappingCache.Count >= maxCachedTriangleGrids then
                    Log.line "[Extraction] triangle mapping cache full (%d patches) - dropping" triGridMappingCache.Count
                    triGridMappingCache.Clear()

                triGridMappingCache.[objectSetPath] <- mapping
                Log.line "[Extraction] built triangle mapping for %s (%d triangles, %dx%d grid)"
                    (Path.GetFileName(Path.GetDirectoryName(objectSetPath))) mapping.TriangleCount size.X size.Y
                mapping
        )


    /// Interpolated texture coordinate at a hit. Only the three corner texels are read
    /// from the coordinates *.aara instead of loading the whole grid.
    let getUVAtHit (coordinatesPath: string) (positionsGridSize: V2i) (gridIndices: int[]) (weights: V3d) =
        match VertexAttributes.tryGetLayer coordinatesPath with
        | None -> None
        | Some layer ->
            match VertexAttributes.sampleOne layer positionsGridSize gridIndices weights with
            | Some sample when sample.values.Length >= 2 ->
                // V-flip to match Patch.load convention. Interpolation and the flip
                // commute because barycentric weights sum to 1.
                Some (V2f(float32 sample.values.[0], 1.0f - float32 sample.values.[1]))
            | _ -> None

    let private readPixelValue (img: PixImage) (px: int) (py: int) =
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
            Log.warn "[Extraction] unsupported pixel format: %A" (img.GetType())
            None

    let private sampleImageAtUV (uv: V2f) (img: PixImage) =
        let w = img.Size.X
        let h = img.Size.Y
        let px = clamp 0 (w - 1) (int (uv.X * float32 w))
        let py = clamp 0 (h - 1) (int (uv.Y * float32 h))
        readPixelValue img px py

    /// Indices into `patchInfo.Textures` that denote actual attribute textures.
    /// `PatchFileInfo.Textures` interleaves DiffuseColorNTexture with DiffuseColorNWeights
    /// entries; the weights are *.aara files and are dropped here. The first remaining
    /// entry is the patch's base colour texture and carries no attribute meaning.
    let private attributeTextureIndices (patchInfo: PatchFileInfo) =
        let textures =
            patchInfo.Textures
            |> List.indexed
            |> List.filter (fun (_, t) -> not (t.fileName.EndsWith(".aara", StringComparison.OrdinalIgnoreCase)))
            |> List.map fst
        match textures with
        | _ :: rest -> rest
        | []        -> []

    /// Sentinel the exports write into attribute textures where a layer has no value.
    /// Normalised samples live in [0, 1], so anything this negative is nodata.
    let private textureNoData = -9999.0

    /// Attribute textures store each layer normalised into the layer's
    /// `ChannelsDefinedRange` from the `*.opcx` - EXR layers hold [0,1] floats, and 8/16
    /// bit images are normalised by `readPixelValue`. The per-vertex `*.aara` layers hold
    /// physical values instead, so texture samples are mapped back onto the layer's range
    /// to make the two sources comparable. Without a known range the raw sample is
    /// returned unchanged, which is what this code did before the ranges were consulted.
    let private toPhysical (range : Option<Range1d>) (values : float[]) =
        match range with
        | None   -> values
        | Some r -> values |> Array.map (fun v -> r.Min + v * r.Size)

    /// Looks up the range a texture layer's values are normalised into, by layer label.
    /// `Surface.scalarLayers` is keyed by layer index, so it is re-keyed here.
    let rangeLookup (scalarLayers : HashMap<int, ScalarLayer>) =
        let byLabel =
            scalarLayers
            |> HashMap.toValueList
            |> List.map (fun l -> l.label, l.definedRange)
            |> Map.ofList
        fun (label : string) -> byLabel |> Map.tryFind label

    let private sampleTexture (opcPaths: OpcPaths) (patchInfo: PatchFileInfo) (rangeOf: string -> Option<Range1d>) (uv: V2f) (index: int) =
        match Patch.tryExtractTexturePath opcPaths patchInfo index with
        | None -> None
        | Some (texturePath, texName) ->
            let ext = Path.GetExtension(texturePath).ToLowerInvariant()
            let format =
                match ext with
                | ".dds"          -> Some TextureLoading.DDS
                | ".tiff" | ".tif" -> Some TextureLoading.TIFF
                | ".exr"          -> Some TextureLoading.TextureFormat.OpenEXR
                | _               -> None

            // EXR layers are single channel scalar maps written into channel 0
            let channel =
                if ext = ".exr" then ChannelReference.ChannelWithIndex 0
                else ChannelReference.NoChannelSelection

            use stream = Prinziple.openRead texturePath
            let mipMap = TextureLoading.loadImageFromStream stream channel format
            match mipMap.ImageArray |> Seq.tryHead with
            | None ->
                Log.warn "[Extraction] no image in mipmap for texture %d (%s)" index texName
                None
            | Some img ->
                sampleImageAtUV uv img
                |> Option.bind (fun values ->
                    if values |> Array.exists (fun v -> v <= textureNoData) then None
                    else
                        Some { name = texName; values = toPhysical (rangeOf texName) values; source = AttributeSource.TextureSampling }
                )

    /// Samples the patch's attribute textures at `uv`, skipping layers already covered by
    /// per-vertex data. Decoding an image per layer is what makes this the slow path.
    let extractAttributesAtUV (uv: V2f) (patchInfo: PatchFileInfo) (opcPaths: OpcPaths) (rangeOf: string -> Option<Range1d>) (alreadyCovered: string -> bool) =
        attributeTextureIndices patchInfo
        |> List.choose (fun index ->
            match Patch.tryExtractTexturePath opcPaths patchInfo index with
            | Some (_, texName) when alreadyCovered texName -> None
            | Some _ -> sampleTexture opcPaths patchInfo rangeOf uv index
            | None   -> None
        )

    /// Paths persisted in kd-trees and paths derived from `OpcPaths` can use different
    /// directory separators, so they are normalised before being matched against each other.
    let private normalizeSeparators (path : string) =
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)

    // Cache for PatchFileInfo lookups, keyed by surface guid
    let private patchInfoLookupCache = Dictionary<Guid, Dictionary<string, PatchFileInfo * OpcPaths>>()

    /// Build (or retrieve from cache) the patchInfoLookup for a given SgSurface.
    ///
    /// Keys are compared case-insensitively on purpose: `OpcPaths.Patches_DirAbsPath` probes
    /// `"patches"` before `"Patches"` and `Directory.Exists` is case-insensitive on Windows,
    /// so the keys built here can spell the patches directory differently than the
    /// `objectSetPath` persisted in a kd-tree does. A case-sensitive lookup silently misses
    /// every patch of such a surface.
    let buildPatchInfoLookup (sgSurface : SgSurface) =
        // reached from the background picking thread and from profile export on the update
        // thread, so the shared Dictionary needs the same lock as the mapping cache
        lock patchInfoLookupCache (fun () ->
        match patchInfoLookupCache.TryGetValue(sgSurface.surface) with
        | true, lookup -> lookup
        | _ ->
            let dict = Dictionary<string, PatchFileInfo * OpcPaths>(StringComparer.OrdinalIgnoreCase)
            match sgSurface.dataSource with
            | DataSource.OpcHierarchy hierarchies ->
                for h in hierarchies do
                    let leaves = QTree.getLeaves h.tree |> Seq.toArray
                    for leaf in leaves do
                        let info = leaf.info
                        let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
                        let objectSetPath = dir +/ info.Positions
                        dict.[normalizeSeparators objectSetPath] <- (info, OpcPaths h.opcPaths.Opc_DirAbsPath)
            | DataSource.Mesh ->
                Log.warn "[Extraction] mesh data source not supported for attribute extraction"
            patchInfoLookupCache.[sgSurface.surface] <- dict
            Log.line "[Extraction] built patch info lookup for surface %A with %d entries" sgSurface.surface dict.Count
            dict
        )

    /// Drops all cached per-patch data. Called when surfaces are removed.
    let clearCaches () =
        lock triGridMappingCache (fun () -> triGridMappingCache.Clear())
        lock patchInfoLookupCache (fun () -> patchInfoLookupCache.Clear())
        VertexAttributes.clearCache ()

    /// Extract attributes at a KdTree hit point.
    ///
    /// Per-vertex `*.aara` layers are read first - three small random-access reads per
    /// layer. Layers the vertex data does not cover fall back to `textureFallback`.
    let extractAttributesFromHit (textureFallback : TextureFallback) (hitInfo : KdTreeHitInfo) (ray : FastRay3d) =
        let sgSurf = hitInfo.sgSurface

        // find the Level0KdTree for the hit bounding box
        match sgSurf.picking with
        | Picking.KdTree kdMap ->
            match kdMap |> HashMap.tryFind hitInfo.hitBBox with
            | Some (Level0KdTree.LazyKdTree kd) ->
                try
                    let mapping = buildTriangleToGridMapping kd.affine kd.objectSetPath
                    let positionsGridSize = mapping.gridSize
                    let triIdx = hitInfo.hit.SetObject.Index
                    let triangleSet = hitInfo.hit.SetObject.Set :?> TriangleSet
                    let triangle = DebugKdTreesX.getTriangle triangleSet triIdx
                    let hitPoint = ray.Ray.GetPointOnRay(hitInfo.hit.RayHit.T)
                    let weights = computeBarycentric triangle hitPoint

                    let patchDir = Path.GetDirectoryName kd.objectSetPath
                    let patchInfoLookup = buildPatchInfoLookup sgSurf
                    match mapping.TryGetTriangleIndices triIdx,
                          patchInfoLookup.TryGetValue(normalizeSeparators kd.objectSetPath) with
                    | None, _ ->
                        Log.warn "[Extraction] triangle %d outside the grid mapping of %s (%d triangles)"
                            triIdx patchDir mapping.TriangleCount
                        None
                    | Some gridIndices, (true, (patchInfo, opcPaths)) ->
                        let fromVertices =
                            let layers = VertexAttributes.getLayers patchDir patchInfo
                            VertexAttributes.sample layers positionsGridSize gridIndices weights

                        // texture layer names come from the Images/<Layer> folder, per-vertex
                        // ones from the *.aara base name - compared case-insensitively so a
                        // spelling difference cannot let the less accurate texture value
                        // through and override the per-vertex one
                        let covered =
                            // System's HashSet - FSharp.Data.Adaptive.HashSet has no comparer
                            System.Collections.Generic.HashSet<string>(
                                fromVertices |> List.map (fun a -> a.name), StringComparer.OrdinalIgnoreCase)

                        let fromTextures =
                            // Only pay for image decoding when the per-vertex layers are
                            // missing or do not cover a layer.
                            match textureFallback with
                            | TextureFallback.Disabled -> []
                            | TextureFallback.Enabled _ when attributeTextureIndices patchInfo |> List.isEmpty -> []
                            | TextureFallback.Enabled scalarLayers ->
                                match getUVAtHit kd.coordinatesPath positionsGridSize gridIndices weights with
                                | Some uv -> extractAttributesAtUV uv patchInfo opcPaths (rangeLookup scalarLayers) covered.Contains
                                | None ->
                                    Log.warn "[Extraction] no texture coordinates for %s" patchDir
                                    []

                        Some {
                            position   = hitPoint
                            patchName  = patchInfo.Name
                            attributes = fromVertices @ fromTextures
                        }
                    | _ ->
                        Log.warn "[Extraction] no patch info found for %s" kd.objectSetPath
                        None
                with e ->
                    Log.warn "[Extraction] error: %A" e
                    None
            | Some (Level0KdTree.InCoreKdTree _) ->
                Log.warn "[Extraction] InCoreKdTree not supported for attribute extraction"
                None
            | None ->
                Log.warn "[Extraction] no Level0KdTree found for hitBBox %A" hitInfo.hitBBox
                None
        | _ ->
            Log.warn "[Extraction] surface picking is not KdTree-based"
            None

    /// Attributes under a single position, by casting a ray straight down onto the
    /// visible surfaces. Used by the annotation export, which samples exactly the
    /// points it is exporting rather than a profile of its own.
    ///
    /// `withTextureFallback` decides whether layers the per-vertex data misses are
    /// chased into the attribute textures. That costs an image decode per layer per
    /// patch, so it is worth it when the caller wants every layer it can get, and
    /// pure waste when it only came for the per-vertex LonLatRad grid.
    ///
    /// The question this answers - "what is the surface at this world position?" - is a
    /// point location, not a ray: the direction is not a parameter of it and the caller
    /// therefore does not supply one. A ray is only how the KdTree can be asked at all
    /// (`extractAttributesFromHit` uses it for nothing but `GetPointOnRay`), so the
    /// direction is derived here, per point, as the body-local up *at that point*.
    ///
    /// It deliberately does not use `refSys.up`: that is the up at the reference system's
    /// `origin`, not at `position`, and on a body the two diverge with distance. (It used
    /// to be worse - navigation recomputed it from the camera location on every event, so
    /// the landing point depended on where the viewer happened to be standing.) The
    /// ellipsoid normal at `position` is both what "straight down" was always meant to be
    /// and the well-conditioned choice, being roughly perpendicular to the local terrain
    /// rather than grazing it.
    let sampleAt
        (surfacesModel       : SurfaceModel)
        (refSys              : ReferenceSystem)
        (observedSystem      : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem      : Option<ObserverSystem>)
        (withTextureFallback : bool)
        (cache               : HashMap<string, ConcreteKdIntersectionTree>)
        (position            : V3d)
        : Option<AttributeHit> * HashMap<string, ConcreteKdIntersectionTree> =

        let surfaceFilter = fun (_id : Guid) (l : Leaf) (_s : SgSurface) -> l.visible && l.active
        let up = ReferenceSystemApp.upVector position refSys.planet
        let ray = FastRay3d(Ray3d(position + up * 10.0, -up))

        match SurfaceIntersection.doKdTreeIntersection surfacesModel refSys observedSystem observerSystem ray surfaceFilter cache false with
        | Some hitInfo, cache ->
            // the *.opcx attribute layer ranges of the surface that was hit - needed
            // to turn normalised texture samples back into physical values
            let fallback =
                if not withTextureFallback then TextureFallback.Disabled
                else
                    match SurfaceModel.getSurface surfacesModel hitInfo.sgSurface.surface with
                    | Some leaf -> TextureFallback.Enabled (Leaf.toSurface leaf).scalarLayers
                    | None      -> TextureFallback.Enabled HashMap.empty
            extractAttributesFromHit fallback hitInfo ray, cache
        | None, cache -> None, cache

    /// The per-vertex layer carrying (longitude, latitude, radius-in-metres) at
    /// every vertex of a patch. Newer OPC exports ship it; older ones do not, which
    /// is what makes it something an export has to check for rather than assume.
    [<Literal>]
    let LonLatRadLayer = "LonLatRad"

    /// The geographic coordinates of a hit, straight out of the patch's own
    /// LonLatRad grid rather than re-derived from the position.
    ///
    /// `None` when the surface ships no such layer, or the hit landed on part of
    /// the position grid the attribute grid does not cover (its skirt).
    let tryLonLatRadius (hit : AttributeHit) =
        hit.attributes
        |> List.tryFind (fun a -> String.Equals(a.name, LonLatRadLayer, StringComparison.OrdinalIgnoreCase))
        |> Option.bind (fun a ->
            if a.values.Length >= 3 then Some (V3d(a.values.[0], a.values.[1], a.values.[2]))
            else
                Log.warn "[Extraction] %s has %d channels, expected 3" LonLatRadLayer a.values.Length
                None)

    /// Whether any loaded OPC surface ships the LonLatRad layer at all — i.e.
    /// whether an export can take lat/lon/alt from the file for this scene.
    ///
    /// One patch per hierarchy is enough: the attribute layers are a property of
    /// the OPC export as a whole, not of the individual patch. Called once per
    /// export, never per point.
    let hasLonLatRadLayer (surfacesModel : SurfaceModel) =
        let carriesLayer (info : PatchFileInfo) =
            info.Attributes
            |> List.exists (fun file ->
                String.Equals(Path.GetFileNameWithoutExtension file, LonLatRadLayer, StringComparison.OrdinalIgnoreCase))

        surfacesModel.sgSurfaces
        |> HashMap.toValueList
        |> List.exists (fun sgSurface ->
            match sgSurface.dataSource with
            | DataSource.OpcHierarchy hierarchies ->
                hierarchies
                |> Array.exists (fun h ->
                    // tryHead, not toArray: one leaf answers the question, and a
                    // deep hierarchy has a lot of them
                    QTree.getLeaves h.tree
                    |> Seq.tryHead
                    |> Option.map (fun leaf -> carriesLayer leaf.info)
                    |> Option.defaultValue false)
            | DataSource.Mesh -> false)

    /// Extract profile samples along annotation points by re-picking into surfaces.
    /// Returns a list of ProfileSamples and the set of all attribute names encountered.
    let extractProfile
        (points        : list<V3d>)
        (up            : V3d)
        (surfacesModel : SurfaceModel)
        (refSys        : ReferenceSystem)
        (observedSystem : SurfaceId -> Option<SpiceReferenceSystem>)
        (observerSystem : Option<ObserverSystem>)
        (cache         : HashMap<string, ConcreteKdIntersectionTree>)
        : ProfileSample list * Set<string> * HashMap<string, ConcreteKdIntersectionTree> =

        let surfaceFilter = fun (_id : Guid) (l : Leaf) (_s : SgSurface) -> l.visible && l.active
        let mutable cache = cache
        let mutable allAttributeNames = Set.empty<string>
        let samples = List<ProfileSample>(points.Length)
        let mutable accDistance = 0.0
        let mutable vertexSourced = 0
        let mutable textureSourced = 0

        let pointArray = points |> List.toArray

        for i in 0 .. pointArray.Length - 1 do
            let p = pointArray.[i]
            if i > 0 then
                accDistance <- accDistance + Vec.distance pointArray.[i-1] p

            let rayOrigin = p + up * 10.0
            let ray = FastRay3d(Ray3d(rayOrigin, -up))

            match SurfaceIntersection.doKdTreeIntersection surfacesModel refSys observedSystem observerSystem ray surfaceFilter cache false with
            | Some hitInfo, c ->
                cache <- c
                // the *.opcx attribute layer ranges of the surface that was hit - needed to
                // turn normalised texture samples back into physical values
                let scalarLayers =
                    match SurfaceModel.getSurface surfacesModel hitInfo.sgSurface.surface with
                    | Some leaf -> (Leaf.toSurface leaf).scalarLayers
                    | None      -> HashMap.empty
                match extractAttributesFromHit (TextureFallback.Enabled scalarLayers) hitInfo ray with
                | Some hit ->
                    let attributes = Dictionary<string, float[]>()
                    for a in hit.attributes do
                        attributes.[a.name] <- a.values
                        allAttributeNames <- allAttributeNames |> Set.add a.name
                        match a.source with
                        | AttributeSource.VertexData      -> vertexSourced <- vertexSourced + 1
                        | AttributeSource.TextureSampling -> textureSourced <- textureSourced + 1
                    samples.Add { position = hit.position; distance = accDistance; attributes = attributes }
                | None ->
                    Log.warn $"[MultiAttrProfile] point {i}: attribute extraction failed"
            | None, c ->
                cache <- c
                Log.warn $"[MultiAttrProfile] point {i}: no surface hit"

        Log.line "[MultiAttrProfile] %d values from per-vertex layers, %d from texture sampling"
            vertexSourced textureSourced

        samples |> List.ofSeq, allAttributeNames, cache

    let private formatFloat (v : float) =
        v.ToString("G", CultureInfo.InvariantCulture)

    /// Write profile samples to a CSV file.
    let writeCsv (path : string) (samples : ProfileSample list) (attributeNames : Set<string>) =
        let attrNames = attributeNames |> Set.toList
        use writer = new StreamWriter(path)

        let header = "distance,x,y,z" + (attrNames |> List.map (fun n -> $",{n}") |> String.concat "")
        writer.WriteLine(header)

        for sample in samples do
            let p = sample.position
            let attrValues =
                attrNames |> List.map (fun name ->
                    match sample.attributes.TryGetValue(name) with
                    | true, values -> "," + (values |> Array.map formatFloat |> String.concat ";")
                    | false, _ -> ","
                ) |> String.concat ""
            writer.WriteLine($"{formatFloat sample.distance},{formatFloat p.X},{formatFloat p.Y},{formatFloat p.Z}{attrValues}")

        Log.line $"[MultiAttrProfile] wrote {samples.Length} samples with {attrNames.Length} attributes to {path}"
