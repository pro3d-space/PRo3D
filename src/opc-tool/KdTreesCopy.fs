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

module KdTrees = 

  type LazyKdTree = {
      kdTree          : option<ConcreteKdIntersectionTree>
      affine          : Trafo3d
      boundingBox     : Box3d        
      kdtreePath      : string
      objectSetPath   : string
      coordinatesPath : string
      texturePath     : string
    }
  
  type InCoreKdTree = {
      kdTree      : ConcreteKdIntersectionTree
      boundingBox : Box3d
    }
  
  type Level0KdTree = 
      | LazyKdTree   of LazyKdTree
      | InCoreKdTree of InCoreKdTree

  let relativePath (path : string) (remaining : int) =
    path.Split(Path.DirectorySeparatorChar) 
    |> List.ofArray 
    |> List.rev 
    |> List.take remaining 
    |> List.rev 
    |> Path.combine

  let relativePath' (path : string) =
    relativePath path 3

  let expandKdTreePaths basePath kd = 
    kd 
      |> HashMap.map(fun _ k ->
        match k with 
          | Level0KdTree.LazyKdTree lkt -> 
            let kdTreeSub   = lkt.kdtreePath    |> relativePath'
            let triangleSub = lkt.objectSetPath |> relativePath'
      
            LazyKdTree { 
              lkt with 
                  kdtreePath    = Path.Combine(basePath, kdTreeSub)
                  objectSetPath = Path.Combine(basePath, triangleSub)
            }
          | Level0KdTree.InCoreKdTree ik -> InCoreKdTree ik
        )

  let makeInCoreKd a = 
    {
      kdTree = new ConcreteKdIntersectionTree()
      boundingBox = a
    }

  let makeLazyTree a b c d e f =
    {
      kdTree          = None
      affine          = a
      boundingBox     = b
      kdtreePath      = c
      objectSetPath   = d    
      coordinatesPath = e
      texturePath     = f
    }

  // PICKLER
  let incorePickler : Pickler<InCoreKdTree> =
    Pickler.product makeInCoreKd
    ^. Pickler.field (fun s -> s.boundingBox)     Pickler.auto<Box3d>

  let lazyPickler : Pickler<LazyKdTree> =
      Pickler.product makeLazyTree
      ^+ Pickler.field (fun (s:LazyKdTree) -> s.affine)       Pickler.auto<Trafo3d>
      ^+ Pickler.field (fun (s:LazyKdTree) -> s.boundingBox)  Pickler.auto<Box3d>
      ^+ Pickler.field (fun s -> s.kdtreePath)                Pickler.string
      ^+ Pickler.field (fun s -> s.objectSetPath)             Pickler.string
      ^+ Pickler.field (fun s -> s.coordinatesPath)           Pickler.string
      ^. Pickler.field (fun s -> s.texturePath)               Pickler.string

  let level0KdTreePickler : Pickler<Level0KdTree> =
      Pickler.sum (fun x k1 k2->
          match x with
              | InCoreKdTree k -> k1 k
              | LazyKdTree k -> k2 k)
      ^+ Pickler.case InCoreKdTree incorePickler
      ^. Pickler.case LazyKdTree lazyPickler

  // SAVE LOAD
  let save path (b : BinarySerializer) (d : 'a) =    
    let arr =  b.Pickle d
    System.IO.File.WriteAllBytes(path, arr);
    d
  
  let loadAs<'a> path (b : BinarySerializer) : 'a =
    let arr = System.IO.File.ReadAllBytes(path)
    b.UnPickle arr
  
  let loadKdtree path =
    //Log.startTimed "loading tree"
    use b = new BinaryReadingCoder(System.IO.File.OpenRead(path))
    let mutable kdTree = Unchecked.defaultof<KdIntersectionTree>
    b.CodeT(&kdTree)
    //Log.stop()        
    ConcreteKdIntersectionTree(kdTree, Trafo3d.Identity)

  let loadKdTrees' (h : PatchHierarchy) (trafo:Trafo3d) (load : bool) (mode:ViewerModality) (b : BinarySerializer) : HashMap<Box3d,Level0KdTree> =
    //ObjectBuilder

    let masterKdPath = 
      mode |> ViewerModality.matchy
        (h.kdTreeAggZero_FileAbsPath) 
        (h.kdTreeAggZero2d_FileAbsPath)

    let cacheFile = System.IO.Path.ChangeExtension(masterKdPath, ".cache")

    let loadAndCreateCache() =
      let patchInfos =
        h.tree |> QTree.getLeaves |> Seq.toList |> List.map(fun x -> x.info)

      let kd0Paths = 
        patchInfos 
          |> List.map(fun x -> h.kdTree_FileAbsPath x.Name 0 mode)

      let kd0PathsExist = 
          kd0Paths |> List.forall(System.IO.File.Exists)
    
      match (System.IO.File.Exists(masterKdPath), kd0PathsExist) with
        | (true, false) -> 
          Log.line "Found master kdtree - loading incore"
          let tree = loadKdtree masterKdPath                     
          let kd = {
              kdTree = tree;
              boundingBox = tree.KdIntersectionTree.BoundingBox3d.Transformed(trafo)
          }
          HashMap.add kd.boundingBox (InCoreKdTree kd) HashMap.empty
        | (true, true) ->   
          Log.line "Found master kdtree and patch trees"
          Log.startTimed "building lazy kdtree cache"
                    
          let num = kd0Paths |> List.ofSeq |> List.length        
            
          let bla = 
            kd0Paths
              |> List.zip patchInfos
              |> List.mapi(
                fun i (info, kdPath) ->                                
                  let t = loadKdtree kdPath
                  let pos =
                    match mode with
                    | XYZ -> info.Positions
                    | SvBR -> info.Positions2d.Value
                    
                  let dir = h.opcPaths.Patches_DirAbsPath +/ info.Name
                        
                  let lazyTree : LazyKdTree = {
                      kdTree          = None
                      objectSetPath   = dir +/ pos
                      coordinatesPath = dir +/ (List.head info.Coordinates)
                      texturePath     = Patch.extractTexturePath (OpcPaths h.opcPaths.Opc_DirAbsPath) info 0 
                      kdtreePath      = kdPath
                      affine          = 
                        mode 
                          |> ViewerModality.matchy info.Local2Global info.Local2Global2d
                      boundingBox   = t.KdIntersectionTree.BoundingBox3d.Transformed(trafo)
                  }
                  Report.Progress(float i / float num)
            
                  (lazyTree.boundingBox, (LazyKdTree lazyTree))
              )                            
             
          Log.stop()
             
          bla |> save cacheFile b |> ignore
               
          if load then
            bla |> HashMap.ofList
          else
            HashMap.empty
        | _ ->
          Log.warn "Could not find level 0 kdtrees"
          HashMap.empty

    if System.IO.File.Exists(cacheFile) then
      Log.line "Found lazy kdtree cache"
      if load then
        try 
          let trees = loadAs<list<Box3d*Level0KdTree>> cacheFile b
    //      let trees = trees |> List.filter(fun (_,(LazyKdTree k)) -> k.kdtreePath = blar)
          trees |> HashMap.ofList
        with e ->
            Log.warn "could not load lazy kdtree cache. (%A) rebuilding..." e
            loadAndCreateCache()
      else
        HashMap.empty
    else
      loadAndCreateCache()
    
  let loadKdTrees (h : PatchHierarchy) (trafo:Trafo3d) (mode:ViewerModality) (b : BinarySerializer) : HashMap<Box3d,Level0KdTree> =
    loadKdTrees' (h) (trafo) (true) mode b