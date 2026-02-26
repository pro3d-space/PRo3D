// The KdTree types live in the legacy namespace Aardvark.VRVis.Opc for compatibility reasons (hurdleless legacy code and deserialization)
namespace Aardvark.VRVis.Opc

open Aardvark.Geometry
open Aardvark.Base

module KdTrees =

    type LazyKdTree =
        { kdTree: option<ConcreteKdIntersectionTree>
          affine: Trafo3d
          boundingBox: Box3d
          kdtreePath: string
          objectSetPath: string
          coordinatesPath: string
          texturePath: string }

    type InCoreKdTree =
        { kdTree: ConcreteKdIntersectionTree
          boundingBox: Box3d }

    type Level0KdTree =
        | LazyKdTree of LazyKdTree
        | InCoreKdTree of InCoreKdTree


namespace OpcViewer.Base


open System
open System.IO
open Aardvark.Geometry
open Aardvark.Base
open Aardvark.Base.Coder
open Aardvark.Data.Opc
open MBrace.FsPickler
open MBrace.FsPickler.Combinators
open FSharp.Data.Adaptive
open System.Collections.Generic

open Aardvark.VRVis.Opc.KdTrees

module KdTrees =

    type LazyKdTree = Aardvark.VRVis.Opc.KdTrees.LazyKdTree
    type InCoreKdTree = Aardvark.VRVis.Opc.KdTrees.InCoreKdTree
    type Level0KdTree = Aardvark.VRVis.Opc.KdTrees.Level0KdTree


    let relativePath (path: string) (remaining: int) =
        path.Split([| "/"; "\\" |], StringSplitOptions.None)
        |> List.ofArray
        |> List.rev
        |> List.take remaining
        |> List.rev
        |> Path.combine

    let relativePath' (path: string) = relativePath path 3

    let Patches_DirNames = OpcPaths.Patches_DirNames |> List.toArray

    // tries to repair caches with broken capitalization in patches vs Patches
    // Background: We have dozens cache files out there with inconsistent capitalization for "patches" vs "Patches".
    // Fixing it is expensive. On readonly filesystem we can't even fix them.
    // this we need to workaround rather brutal. Here we try variants 
    // of valid patch file paths to resolve a file from within a patch directory.
    // e.g. "Dinosaur_Quarry_2/OPC_000_000/patches/00-Patch-00007~0001/someFile.aara" => 
    // "Dinosaur_Quarry_2/OPC_000_000/Patches/00-Patch-00007~0001/someFile.aara"
    let tryRepairCaseInsitivityInCaches (pathToFileInPatchDir : string) =
        let components = pathToFileInPatchDir.Split([| "/"; "\\" |], StringSplitOptions.None) 
        let prefix = components[0..components.Length - 4]
        let suffix = components[components.Length - 2 ..]
        let patches = components[components.Length - 3]
        Patches_DirNames |> Array.tryPick (fun p -> 
            let path = Path.Combine(Array.concat [prefix; [| p |]; suffix])
            //Report.Line(5, $"[KdTrees] trying to fix KdPath: {path}")
            if Prinziple.fileExists path then 
                Some path
            else 
                None
        )

    // checks whether the file exists, if not, it tries to repair it.
    let tryFixPatchFileIfNeeded (original : string) = 
        if Prinziple.fileExists original then
            Some original
        else
            //Report.Line(4, $"KdPath does not exist, trying to fix it.. ({original})")
            match tryRepairCaseInsitivityInCaches original with
            | Some f -> 
                //Report.Line(4, $"fixed kdTree path, {original} -> {f}")
                Some f
            | None -> 
                //Report.Line(2, $"could not fix path: {original}")
                None


    let tryExpandKdTreePath (basePath : string) (lkt : LazyKdTree) =
        let kdTreeSub = lkt.kdtreePath |> relativePath'
        let triangleSub = lkt.objectSetPath |> relativePath'

        let kdPath = Path.Combine(basePath, kdTreeSub)
        let objectSetPath = Path.Combine(basePath, triangleSub)

        match tryFixPatchFileIfNeeded kdPath, tryFixPatchFileIfNeeded objectSetPath with
        | Some kdPath, Some objsetSetPath -> 
            Some { lkt with kdtreePath = kdPath; objectSetPath = objsetSetPath } 
        | _ -> None

    let expandKdTreePaths basePath kd =
        kd 
        |> HashMap.map (fun _ k -> 
            match k with
            | Level0KdTree.LazyKdTree lkt ->
                match tryExpandKdTreePath basePath lkt with
                | Some kd -> 
                    Level0KdTree.LazyKdTree kd
                | None -> 
                    Log.warn "could not fix lazy kdtree path"
                    k
            | Level0KdTree.InCoreKdTree ik -> 
                InCoreKdTree ik
        )

    // tries to fix kdTree path in lazyKdtree. Throws if not fixable.
    let validateLazyKdtreePaths (paths : OpcPaths) (l : LazyKdTree) =
        match (*tryFixPatchFileIfNeeded l.kdtreePath*) None with
        | Some fixedPath -> { l with kdtreePath = fixedPath }
        | _ -> 
            // try fixing relative paths.
            match tryExpandKdTreePath paths.Opc_DirAbsPath l with
            | Some o -> 
                Report.Line(12, $"[KdTrees] repaired KdTree path {l.kdtreePath} => {o.kdtreePath}.")
                o
            | None -> 
                failwithf "[KdTrees] could not fix KdTree path: %s" l.kdtreePath


    let makeInCoreKd a =
        { kdTree = new ConcreteKdIntersectionTree()
          boundingBox = a }

    let makeLazyTree a b c d e f =
        { kdTree = None
          affine = a
          boundingBox = b
          kdtreePath = c
          objectSetPath = d
          coordinatesPath = e
          texturePath = f }

    // PICKLER
    let incorePickler: Pickler<InCoreKdTree> =
        Pickler.product makeInCoreKd
        ^. Pickler.field (fun s -> s.boundingBox) Pickler.auto<Box3d>

    let lazyPickler: Pickler<LazyKdTree> =
        Pickler.product makeLazyTree
        ^+ Pickler.field (fun (s: LazyKdTree) -> s.affine) Pickler.auto<Trafo3d>
        ^+ Pickler.field (fun (s: LazyKdTree) -> s.boundingBox) Pickler.auto<Box3d>
        ^+ Pickler.field (fun s -> s.kdtreePath) Pickler.string
        ^+ Pickler.field (fun s -> s.objectSetPath) Pickler.string
        ^+ Pickler.field (fun s -> s.coordinatesPath) Pickler.string
         ^. Pickler.field (fun s -> s.texturePath) Pickler.string

    let level0KdTreePickler: Pickler<Level0KdTree> =
        Pickler.sum (fun x k1 k2 ->
            match x with
            | InCoreKdTree k -> k1 k
            | LazyKdTree k -> k2 k)
        ^+ Pickler.case InCoreKdTree incorePickler
           ^. Pickler.case LazyKdTree lazyPickler

    // SAVE LOAD
    let save path (b: BinarySerializer) (d: 'a) =
        let arr = b.Pickle d
        Prinziple.writeAllBytes path arr
        d

    let loadAs<'a> path (b: BinarySerializer) : 'a =
        let arr = Prinziple.readAllBytes path
        b.UnPickle arr

    let loadKdtree path =
        //Log.startTimed "loading tree"
        use b = new BinaryReadingCoder(Prinziple.openRead path)
        let mutable kdTree = Unchecked.defaultof<KdIntersectionTree>
        b.CodeT(&kdTree)
        //Log.stop()
        ConcreteKdIntersectionTree(kdTree, Trafo3d.Identity)

    let saveKdTree (kdTree : KdIntersectionTree) path =
        Log.startTimed "saving kd tree"
        use b = new BinaryWritingCoder(Prinziple.openWrite path)
        b.CodeT(ref kdTree)
        Log.stop()

    type KdTreeParameters = { 
        flags : KdIntersectionTree.BuildFlags 
        relativeMinCellSize : float
        splitPlaneEpsilon : float
        setObjectSetToNull : bool // can be recomputed on load
    }

    module KdTreeParameters = 
        let legacyDefault = 
            {
                // retrieved from 2016 TextureConverter tool
                flags = KdIntersectionTree.BuildFlags.Hierarchical ||| KdIntersectionTree.BuildFlags.MediumIntersection
                relativeMinCellSize = 1E-06
                splitPlaneEpsilon = 1E-07
                setObjectSetToNull = true
            }

    let loadKdTrees'
        (h: PatchHierarchy)
        (trafo: Trafo3d)
        (load: bool)
        (mode: ViewerModality)
        (b: BinarySerializer)
        (forceRebuild : bool)
        (ignoreMasterKdTree : bool)
        (loadTriangles : Trafo3d -> string -> TriangleSet)
        (surpressFileConstruction : bool)
        (validateKdTrees : bool)
        (kdTreeParameters : KdTreeParameters)
        : HashMap<Box3d, Level0KdTree> =

        let masterKdPath =
            mode
            |> ViewerModality.matchy (h.kdTreeAggZero_FileAbsPath) (h.kdTreeAggZero2d_FileAbsPath)

        if surpressFileConstruction && forceRebuild then
            Log.warn "[KdTrees] forced recreation, but surpressFileConstruction is true. The build will not have any effect."

        let cacheFile = System.IO.Path.ChangeExtension(masterKdPath, ".cache")

        let loadAndCreateCache () =
            let patchInfos =
                h.tree
                |> QTree.getLeaves
                |> Seq.toArray
                |> Array.map (fun x -> x.info)

            let kd0Paths =
                patchInfos
                |> Array.map (fun x -> x, h.kdTree_FileAbsPath x.Name 0 mode)

            let missingKd0Paths = kd0Paths |> Array.filter (not << Prinziple.fileExists << snd)
            if missingKd0Paths.Length > 0 then
                Log.line "[KdTrees] missing kd0 paths: %d/%d" missingKd0Paths.Length kd0Paths.Length

            match tryFixPatchFileIfNeeded masterKdPath with
            | Some masterKdPath when not ignoreMasterKdTree && not forceRebuild ->
                Log.warn "Found master kdtree - loading incore. THIS NEEDS A LOT OF MEMORY. CONSIDER CREATING PER-PATCH KD TREES. see: https://github.com/pro3d-space/PRo3D/blob/9821c8882b024c7ed85c23ee76110c70e249e480/docs/KdTrees.md#create-kdtrees-for-an-opc-hierarchy"
                let tree = loadKdtree masterKdPath

                let kd =
                    { kdTree = tree
                      boundingBox = tree.KdIntersectionTree.BoundingBox3d.Transformed(trafo) }

                HashMap.single kd.boundingBox (InCoreKdTree kd) 
            | _ -> 
                Log.line "Found master kdtree and patch trees"
                Log.startTimed "building lazy kdtree cache"

                let num = kd0Paths |> List.ofSeq |> List.length


                let kdTrees =
                    kd0Paths
                    |> Array.indexed
                    |> Array.map (fun (i, (info,kdPath)) ->
                        
                        let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
                        let pos =
                            match mode with
                            | ViewerModality.XYZ -> info.Positions
                            | ViewerModality.SvBR -> info.Positions2d.Value

                        let objectSetPath = dir +/ pos
                        let trafo = mode |> ViewerModality.matchy info.Local2Global info.Local2Global2d

                        let createConcreteTree () : ConcreteKdIntersectionTree = 
                            let triangleSet = loadTriangles trafo objectSetPath

                            Log.startTimed $"Building KdTree for {info.Name}"
                            let kdTree = KdIntersectionTree(triangleSet, kdTreeParameters.flags, kdTreeParameters.relativeMinCellSize, kdTreeParameters.splitPlaneEpsilon)
                            Log.stop()

                            // can (and will) be recomputed from vertex data on load
                            if kdTreeParameters.setObjectSetToNull then
                                kdTree.ObjectSet <- null

                            if not surpressFileConstruction then
                                Log.startTimed "saving KdTree to: %s" info.Name
                                saveKdTree kdTree kdPath
                                Log.stop()
                            else
                                Log.warn "[KdTrees] live KdTree construction is supressed, please create KdTrees manually."

                            // TODO: Use Prinziple
                            if File.Exists kdPath then
                                let fi = FileInfo(kdPath)
                                Log.line $"{info.Name} has size: {Mem(fi.Length)}."

                            ConcreteKdIntersectionTree(kdTree, Trafo3d.Identity)


                        let t =
                            if Prinziple.fileExists kdPath then 
                                if validateKdTrees then
                                    try 
                                        createConcreteTree() |> ignore
                                        Some kdPath
                                    with e -> 
                                        Log.warn "[KdTrees] could not load kdtree: %A" e
                                        if surpressFileConstruction then None
                                        else 
                                            createConcreteTree() |> ignore
                                            if Prinziple.fileExists kdPath then Some kdPath else None
                                elif forceRebuild then
                                    createConcreteTree() |> ignore
                                    Some kdPath
                                else
                                    Some kdPath
                            else
                                if not surpressFileConstruction || forceRebuild then
                                    createConcreteTree() |> ignore
                                    Some kdPath
                                else
                                    // no existing, did not want to rebuild
                                    Log.warn "[KdTrees] Kdtree not available, please build it manually using opc-tool or pro3d."
                                    None
                        
                        match t with
                        | Some t -> 
                            let lazyTree: LazyKdTree =
                                { kdTree = None
                                  objectSetPath = objectSetPath
                                  coordinatesPath = dir +/ (List.head info.Coordinates)
                                  texturePath = Patch.extractTexturePath (OpcPaths h.opcPaths.Opc_DirAbsPath) info 0
                                  kdtreePath = kdPath
                                  affine =
                                    mode
                                    |> ViewerModality.matchy info.Local2Global info.Local2Global2d
                                  boundingBox = info.GlobalBoundingBox //t.KdIntersectionTree.BoundingBox3d 
                                }

                            Report.Progress(float i / float num)

                            (lazyTree.boundingBox, (LazyKdTree lazyTree)) |> Some
                        | _ -> 
                            None
                    )

                
                Log.stop ()

                if kdTrees |> Array.exists Option.isNone then 
                    HashMap.empty
                else
                    let trees = kdTrees |> Array.map Option.get |> Array.toList // safe because check above

                    try
                        trees |> save cacheFile b |> ignore
                    with e -> 
                        Log.warn "[KdTrees] could not save LazyKdTree to %s" cacheFile
                        Log.warn "the exception is: %A" e.Message
                        Log.warn "Maybe this is a readonly file system? We often see this with NTFS disks and macs. I will continue without the kdtree cache, but"
                        Log.warn "be aware that reloading the surface requires re-creation of the LazyKdTree"

                    if load then
                        trees |> HashMap.ofList
                    else
                        HashMap.empty





        if Prinziple.fileExists cacheFile then
            Log.line "Found lazy KdTree cache"

            if load then
                if forceRebuild then
                    loadAndCreateCache()
                else
                    try
                        let trees = loadAs<list<Box3d * Level0KdTree>> cacheFile b
                        Report.BeginTimed(5, "tree validation")
                        let validatedTrees = 
                            trees |> List.map (fun (b, kdTree) -> 
                                match kdTree with
                                | Level0KdTree.InCoreKdTree k -> b, kdTree
                                | Level0KdTree.LazyKdTree l -> 
                                    let fixedTree = validateLazyKdtreePaths h.opcPaths l
                                    b, Level0KdTree.LazyKdTree fixedTree
                            )
                        Report.EndTimed(5) |> ignore
                        validatedTrees |> HashMap.ofList
                    with
                    | e ->
                        Log.startTimed "could not load lazy KdTree cache. (%A) rebuilding..." e
                        let r = loadAndCreateCache ()
                        Log.stop()
                        r
            else
                HashMap.empty
        else
            loadAndCreateCache ()

    let loadKdTrees
        (h: PatchHierarchy) (trafo: Trafo3d) (mode: ViewerModality)
        (b: BinarySerializer) (forceRebuild : bool) (ignoreMasterKdTree : bool)
        (loadTriangles : Trafo3d -> string -> TriangleSet) (surpressFileConstruction : bool) : HashMap<Box3d, Level0KdTree> =

        let flags = KdTreeParameters.legacyDefault //{ KdTreeParameters.legacyDefault with flags = KdIntersectionTree.BuildFlags.Picking ||| KdIntersectionTree.BuildFlags.FastBuild }
        loadKdTrees' h trafo true mode b forceRebuild ignoreMasterKdTree loadTriangles surpressFileConstruction false flags