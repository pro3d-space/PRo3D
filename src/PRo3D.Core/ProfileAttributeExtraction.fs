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

    // Cache for triangle-to-grid index mappings (keyed by objectSetPath)
    let private triGridMappingCache = Dictionary<string, int[][]>()

    let buildTriangleToGridMapping (affine: Trafo3d) (objectSetPath: string) =
        match triGridMappingCache.TryGetValue(objectSetPath) with
        | true, mapping -> mapping
        | _ ->
            let positions = objectSetPath |> Aara.fromFile<V3f>
            let size = positions.Size.XY.ToV2i()
            let indices = TriangleSet.computeGridIndices size positions.Data
            let mapping = indices |> Array.chunkBySize 3
            triGridMappingCache.[objectSetPath] <- mapping
            Log.line "[Extraction] built triangle mapping for %s (%d triangles)" (Path.GetFileName(Path.GetDirectoryName(objectSetPath))) mapping.Length
            mapping

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

    let extractAttributesAtUV (uv: V2f) (patchInfo: PatchFileInfo) (opcPaths: OpcPaths) =
        let numTextures = patchInfo.Textures.Length / 2 // first half = textures, second half = weights
        let results = Dictionary<string, float[]>()
        for i in 1 .. numTextures - 1 do
            let texturePath = Patch.tryExtractTexturePath opcPaths patchInfo i
            match texturePath with
            | None -> ()
            | Some (texturePath, texName) ->
                let isExr = Path.GetExtension(texturePath).ToLower() = ".exr"
                let extension =
                    match Path.GetExtension(texturePath).ToLower() with
                    | ".dds" -> Some TextureLoading.DDS
                    | ".tiff" | ".tif" -> Some TextureLoading.TIFF
                    | ".exr" -> Some TextureLoading.TextureFormat.OpenEXR
                    | _ -> None

                if isExr then
                    use stream = Prinziple.openRead texturePath
                    let mipMap = TextureLoading.loadImageFromStream stream (ChannelReference.ChannelWithIndex 0) extension
                    match mipMap.ImageArray |> Seq.tryHead with
                    | Some img ->
                        match sampleImageAtUV uv img with
                        | Some values -> results.[texName] <- values
                        | None -> ()
                    | None ->
                        Log.warn "[Extraction] no image in EXR mipmap for texture %d" i
                else
                    use stream = Prinziple.openRead texturePath
                    let mipMap = TextureLoading.loadImageFromStream stream ChannelReference.NoChannelSelection extension
                    match mipMap.ImageArray |> Seq.tryHead with
                    | Some img ->
                        match sampleImageAtUV uv img with
                        | Some values -> results.[texName] <- values
                        | None -> ()
                    | None ->
                        Log.warn "[Extraction] no image in mipmap for texture %d" i

        results

    // Cache for PatchFileInfo lookups, keyed by surface guid
    let private patchInfoLookupCache = Dictionary<Guid, Dictionary<string, PatchFileInfo * OpcPaths>>()

    /// Build (or retrieve from cache) the patchInfoLookup for a given SgSurface.
    let buildPatchInfoLookup (sgSurface: SgSurface) =
        match patchInfoLookupCache.TryGetValue(sgSurface.surface) with
        | true, lookup -> lookup
        | _ ->
            let dict = Dictionary<string, PatchFileInfo * OpcPaths>()
            match sgSurface.dataSource with
            | DataSource.OpcHierarchy hierarchies ->
                for h in hierarchies do
                    let leaves = QTree.getLeaves h.tree |> Seq.toArray
                    for leaf in leaves do
                        let info = leaf.info
                        let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
                        let objectSetPath = dir +/ info.Positions
                        dict.[objectSetPath] <- (info, OpcPaths h.opcPaths.Opc_DirAbsPath)
            | DataSource.Mesh ->
                Log.warn "[Extraction] mesh data source not supported for attribute extraction"
            patchInfoLookupCache.[sgSurface.surface] <- dict
            Log.line "[Extraction] built patch info lookup for surface %A with %d entries" sgSurface.surface dict.Count
            dict

    /// Extract attributes at a KdTree hit point.
    /// Returns the attributes dictionary, or None if extraction fails.
    let extractAttributesFromHit (hitInfo: KdTreeHitInfo) (ray: FastRay3d) =
        let sgSurf = hitInfo.sgSurface

        // find the Level0KdTree for the hit bounding box
        match sgSurf.picking with
        | Picking.KdTree kdMap ->
            match kdMap |> HashMap.tryFind hitInfo.hitBBox with
            | Some (Level0KdTree.LazyKdTree kd) ->
                try
                    let gridMapping = buildTriangleToGridMapping kd.affine kd.objectSetPath
                    let triIdx = hitInfo.hit.SetObject.Index
                    let triangleSet = hitInfo.hit.SetObject.Set :?> TriangleSet
                    let triangle = DebugKdTreesX.getTriangle triangleSet triIdx
                    let hitPoint = ray.Ray.GetPointOnRay(hitInfo.hit.RayHit.T)
                    let uv = getUVAtHit kd.coordinatesPath gridMapping triIdx triangle hitPoint

                    let patchInfoLookup = buildPatchInfoLookup sgSurf
                    match patchInfoLookup.TryGetValue(kd.objectSetPath) with
                    | true, (patchInfo, opcPaths) ->
                        let attributes = extractAttributesAtUV uv patchInfo opcPaths
                        Some (hitPoint, attributes)
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
        let mutable samples = []
        let mutable accDistance = 0.0

        for i in 0 .. points.Length - 1 do
            let p = points.[i]
            if i > 0 then
                accDistance <- accDistance + Vec.distance points.[i-1] p

            let rayOrigin = p + up * 10.0
            let ray = FastRay3d(Ray3d(rayOrigin, -up))

            match SurfaceIntersection.doKdTreeIntersection surfacesModel refSys observedSystem observerSystem ray surfaceFilter cache false with
            | Some hitInfo, c ->
                cache <- c
                match extractAttributesFromHit hitInfo ray with
                | Some (hitPoint, attributes) ->
                    for kvp in attributes do
                        allAttributeNames <- allAttributeNames |> Set.add kvp.Key
                    samples <- samples @ [{ position = hitPoint; distance = accDistance; attributes = attributes }]
                | None ->
                    Log.warn $"[MultiAttrProfile] point {i}: attribute extraction failed"
            | None, c ->
                cache <- c
                Log.warn $"[MultiAttrProfile] point {i}: no surface hit"

        samples, allAttributeNames, cache

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
