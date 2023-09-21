namespace Aardvark.VRVis.Opc
// copy of https://raw.githubusercontent.com/aardvark-platform/OpcViewer/master/src/OPCViewer.Base/KdTrees.fs
// should be consolidated
open System.IO
open Aardvark.Geometry
open Aardvark.Base
open Aardvark.Base.Coder
open Aardvark.SceneGraph.Opc
open MBrace.FsPickler
open MBrace.FsPickler.Combinators
open FSharp.Data.Adaptive
open Aardvark.Rendering
open System.Collections.Generic

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

    let relativePath (path: string) (remaining: int) =
        path.Split(Path.DirectorySeparatorChar)
        |> List.ofArray
        |> List.rev
        |> List.take remaining
        |> List.rev
        |> Path.combine

    let relativePath' (path: string) = relativePath path 3

    let expandKdTreePaths basePath kd =
        kd
        |> HashMap.map (fun _ k ->
            match k with
            | Level0KdTree.LazyKdTree lkt ->
                let kdTreeSub = lkt.kdtreePath |> relativePath'
                let triangleSub = lkt.objectSetPath |> relativePath'

                LazyKdTree
                    { lkt with
                        kdtreePath = Path.Combine(basePath, kdTreeSub)
                        objectSetPath = Path.Combine(basePath, triangleSub) }
            | Level0KdTree.InCoreKdTree ik -> InCoreKdTree ik)

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
        System.IO.File.WriteAllBytes(path, arr)
        d

    let loadAs<'a> path (b: BinarySerializer) : 'a =
        let arr = System.IO.File.ReadAllBytes(path)
        b.UnPickle arr

    let loadKdtree path =
        //Log.startTimed "loading tree"
        use b = new BinaryReadingCoder(System.IO.File.OpenRead(path))
        let mutable kdTree = Unchecked.defaultof<KdIntersectionTree>
        b.CodeT(&kdTree)
        //Log.stop()
        ConcreteKdIntersectionTree(kdTree, Trafo3d.Identity)

    let saveKdTree (kdTree : KdIntersectionTree) path =
        Log.startTimed "saving kd tree"
        use b = new BinaryWritingCoder(System.IO.File.OpenWrite(path))
        b.CodeT(ref kdTree)
        Log.stop()


    let loadKdTrees'
        (h: PatchHierarchy)
        (trafo: Trafo3d)
        (load: bool)
        (mode: ViewerModality)
        (b: BinarySerializer)
        (forceRebuild : bool)
        : HashMap<Box3d, Level0KdTree> =
        //ObjectBuilder

        let masterKdPath =
            mode
            |> ViewerModality.matchy (h.kdTreeAggZero_FileAbsPath) (h.kdTreeAggZero2d_FileAbsPath)

        let cacheFile = System.IO.Path.ChangeExtension(masterKdPath, ".cache")

        let loadAndCreateCache () =
            let patchInfos =
                h.tree
                |> QTree.getLeaves
                |> Seq.toList
                |> List.map (fun x -> x.info)

            let kd0Paths =
                patchInfos
                |> List.map (fun x -> x, h.kdTree_FileAbsPath x.Name 0 mode)

            let kd0PathsExist = kd0Paths |> List.forall (System.IO.File.Exists << snd)

            if File.Exists(masterKdPath) && not kd0PathsExist then

                Log.line "Found master kdtree - loading incore"
                let tree = loadKdtree masterKdPath

                let kd =
                    { kdTree = tree
                      boundingBox = tree.KdIntersectionTree.BoundingBox3d.Transformed(trafo) }

                HashMap.single kd.boundingBox (InCoreKdTree kd) 
            else
                Log.line "Found master kdtree and patch trees"
                Log.startTimed "building lazy kdtree cache"

                let num = kd0Paths |> List.ofSeq |> List.length

                let kdTrees =
                    kd0Paths
                    |> List.mapi (fun i (info,kdPath) ->
                        
                        let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
                        let pos =
                            match mode with
                            | XYZ -> info.Positions
                            | SvBR -> info.Positions2d.Value

                        let objectSetPath = dir +/ pos
                        let trafo = mode |> ViewerModality.matchy info.Local2Global info.Local2Global2d

                        let createConcreteTree () : ConcreteKdIntersectionTree = 
                            let triangleSet = PRo3D.Core.Surface.DebugKdTreesX.loadTriangles' trafo objectSetPath
                            //let (ig, time) = Patch.load h.opcPaths mode info
                            //let triangles = 
                            //    match ig.IndexArray, ig.IndexedAttributes[DefaultSemantic.Positions] with
                            //    | (:? array<int> as idx), (:? array<V3f> as p) ->
                            //        let triangles = List<Triangle3d>()
                            //        for i in 0 .. 3 .. idx.Length - 1 do
                            //            let t = Triangle3d(V3d p[idx[i]], V3d p[idx[i + 1]], V3d p[idx[i + 2]])
                            //            let nan = t.P0.IsNaN || t.P1.IsNaN || t.P2.IsNaN
                            //            if nan then 
                            //                ()
                            //            else
                            //                triangles.Add(t.Transformed trafo.Forward)

                            //        triangles
                            //    | _ -> 
                            //        failwith "no index or position array"
                            //let triangleSet = Aardvark.Geometry.TriangleSet(triangles)
                            Log.startTimed $"Building KdTree for {dir}"
                            let flags = 
                                 KdIntersectionTree.BuildFlags.Picking ||| KdIntersectionTree.BuildFlags.FastBuild
                                 
                            let kdTree = KdIntersectionTree(triangleSet, flags )
                            Log.stop()
                            Log.startTimed "saving KdTree to: %s" kdPath
                            saveKdTree kdTree kdPath
                            Log.stop()
                            let fi = FileInfo(kdPath)
                            Log.line $"{kdPath} has size: {Mem(fi.Length)}."
                            ConcreteKdIntersectionTree(kdTree, Trafo3d.Identity)

                        let t = 
                            if File.Exists kdPath && not forceRebuild then 
                                try 
                                    loadKdtree kdPath
                                with e -> 
                                    Log.warn "[KdTrees] could not load kdtree: %A" e
                                    createConcreteTree()
                            else
                                createConcreteTree()



                        let lazyTree: LazyKdTree =
                            { kdTree = None
                              objectSetPath = objectSetPath
                              coordinatesPath = dir +/ (List.head info.Coordinates)
                              texturePath = Patch.extractTexturePath (OpcPaths h.opcPaths.Opc_DirAbsPath) info 0
                              kdtreePath = kdPath
                              affine =
                                mode
                                |> ViewerModality.matchy info.Local2Global info.Local2Global2d
                              boundingBox = t.KdIntersectionTree.BoundingBox3d //.Transformed(trafo) 
                            }

                        Report.Progress(float i / float num)

                        (lazyTree.boundingBox, (LazyKdTree lazyTree)))

                Log.stop ()

                kdTrees |> save cacheFile b |> ignore

                if load then
                    kdTrees |> HashMap.ofList
                else
                    HashMap.empty


        if System.IO.File.Exists(cacheFile) && not forceRebuild then
            Log.line "Found lazy KdTree cache"

            if load then
                try
                    let trees = loadAs<list<Box3d * Level0KdTree>> cacheFile b
                    trees |> HashMap.ofList
                with
                | e ->
                    Log.warn "could not load lazy KdTree cache. (%A) rebuilding..." e
                    loadAndCreateCache ()
            else
                HashMap.empty
        else
            loadAndCreateCache ()

    let loadKdTrees
        (h: PatchHierarchy) (trafo: Trafo3d) (mode: ViewerModality)
        (b: BinarySerializer) (forceRebuild : bool) : HashMap<Box3d, Level0KdTree> =
        loadKdTrees' (h) (trafo) (true) mode b forceRebuild
