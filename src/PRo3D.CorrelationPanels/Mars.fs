namespace Mars

//Author: Martin Riegelnegg

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.SceneGraph.Opc
open MBrace.FsPickler


module Shader =
    
    open FShade
    
    let mkNormals (tri : Triangle<Effects.Vertex>) =
        triangle {
            let a = tri.P1.wp.XYZ - tri.P0.wp.XYZ
            let b = tri.P2.wp.XYZ - tri.P0.wp.XYZ
            let n = Vec.Cross(a,b)
            yield {tri.P0 with n = n}
            yield {tri.P1 with n = n}
            yield {tri.P2 with n = n}
}

module Terrain = 

////////////////////////////////////////////////////////////////////////////////////////
  module Test =
    let upDummy = V3d.OIO
    let initialCameraDummy : CameraControllerState = 
      {CameraController.initial with
          view = CameraView.lookAt (10.0 * upDummy + V3d.OOI * -20.0) //TODO real mars
                                    (10.0 * upDummy) 
                                    upDummy}

    let dummyMars events =
        Sg.sphere 5 (AVal.constant (new C4b(254,178,76))) (AVal.constant 10.0) 
            |> Sg.shader {
                do! DefaultSurfaces.trafo
                do! DefaultSurfaces.vertexColor
                do! DefaultSurfaces.simpleLighting
            }
            |> Sg.requirePicking
            |> Sg.noEvents
            |> Sg.withEvents events
           // |> Sg.translate 0.0 10.0 0.0
            |> Sg.translate 0.0 10.0 0.0



////////////////////////////////////////////////////////////////////////////////////////

  module CapeDesire =
    let pickler = FsPickler.CreateBinarySerializer()
    let pickle = pickler.Pickle<QTree<Patch>>
    let unpickle = pickler.UnPickle<QTree<Patch>>

    let patchHierarchies =
          System.IO.Directory.GetDirectories(@"..\..\data\mars") //TODO make it scream
          |> Seq.collect System.IO.Directory.GetDirectories
    
    let pHs =
        [ 
            for h in patchHierarchies do
                //let p = Path.combine [h; @"Patches\patchhierarchy.xml" ]
                yield PatchHierarchy.load pickle unpickle (OpcPaths h)
        ]
    
    type OPCScene =
        {
            useCompressedTextures : bool
            preTransform          : Trafo3d
            patchHierarchies      : seq<string>
            boundingBox           : Box3d
            near                  : float
            far                   : float
            speed                 : float
        }

    let capeDesireBoundingBox = Box3d.Parse("[[3376372.058677169, -325173.566694686, -121309.194857123], [3376385.170513898, -325152.282144333, -121288.943956908]]") 
    let localToGlobal = M44d.Parse("[[1.000000000, 0.000000000, 0.000000000, 3376382.720196387], [0.000000000, 1.000000000, 0.000000000, -325150.729992706], [0.000000000, 0.000000000, 1.000000000, -121300.468943038], [0.000000000, 0.000000000, 0.000000000, 1.000000000]]")
    let mars() =
        { 
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies = patchHierarchies
            boundingBox      = capeDesireBoundingBox
            near             = 0.1
            far              = 100000.0
            speed            = 5.0
            //lodDecider       = DefaultMetrics.mars
        }
    
    let scene = mars()


    let upReal = V3d(0.96,0.05,0.28) //scene.boundingBox.Center.Normalized  
    //let transl = Trafo3d.Translation(-3376000.0, 325000.0, 121000.0) //WORKS I
    let transl = Trafo3d.Translation(-3376000.0, 325500.0, 121500.0)

    let initialCamera = 
      let r = Trafo3d.RotateInto(V3d.OOI, upReal)
      let cameraPosition = V3d(383.0, 339.0, 192.0) //WORKS I
      let center = V3d(381.50, 344.0, 200.0) //WORKS I
      let camView = CameraView.lookAt cameraPosition center upReal
      {CameraController.initial with view = camView}

    let preTransform =
        ///let bb = scene.boundingBox
        /// Trafo3d.Translation(-bb.Center) * scene.preTransform
        transl * scene.preTransform
        //scene.preTransform
    
    

    let mkISg() =
        Sg2.createFlatISg pickle unpickle (patchHierarchies |> Seq.map OpcPaths |> Seq.toList)
        |> Sg.noEvents
        |> Sg.transform preTransform
    
    
    let defaultEffects =
        [
            DefaultSurfaces.trafo                   |> toEffect
            DefaultSurfaces.constantColor C4f.White |> toEffect
            DefaultSurfaces.diffuseTexture          |> toEffect
        ]

   
    let simpleLightingEffects =
        let col = C4f(V4d(0.8, 0.5, 0.5, 1.0))
        [
            DefaultSurfaces.trafo             |> toEffect
            Shader.mkNormals                  |> toEffect
            DefaultSurfaces.constantColor col |> toEffect
            DefaultSurfaces.simpleLighting    |> toEffect
        ]
    
    let mutable min     = V3d.III * 50000000.0
    let mutable max     = -V3d.III * 50000000.0

    //let mutable totalBB = Box3d.Unit.Translated(scene.boundingBox.Center)
    let mutable totalBB = Box3d.Unit.Translated(scene.boundingBox.Center)
   
    let patchBB() = totalBB.Translated(-scene.boundingBox.Center)
    
    let buildKDTree (g : IndexedGeometry) (local2global : Trafo3d) =
        let pos = g.IndexedAttributes.[DefaultSemantic.Positions] |> unbox<V3f[]>
        let index = g.IndexArray |> unbox<int[]>
    
        let triangles =
            [| 0 .. 3 .. index.Length - 2 |] 
                |> Array.choose (fun bi -> 
                    let p0 = pos.[index.[bi]]
                    let p1 = pos.[index.[bi + 1]]
                    let p2 = pos.[index.[bi + 2]]
                    if isNan p0 || isNan p1 || isNan p2 then
                        None
                    else
                        let a = V3d(float p0.X, float p0.Y, float p0.Z) |> local2global.Forward.TransformPos
                        let b = V3d(float p1.X, float p1.Y, float p1.Z) |> local2global.Forward.TransformPos
                        let c = V3d(float p2.X, float p2.Y, float p2.Z) |> local2global.Forward.TransformPos
                        
                        if a.X < min.X then min.X <- a.X
                        if a.Y < min.Y then min.Y <- a.Y
                        if a.Z < min.Z then min.Z <- a.Z
                        if a.X > max.X then max.X <- a.X
                        if a.Y > max.Y then max.Y <- a.Y
                        if a.Z > max.Z then max.Z <- a.Z
    
                        if b.X < min.X then min.X <- b.X
                        if b.Y < min.Y then min.Y <- b.Y
                        if b.Z < min.Z then min.Z <- b.Z
                        if b.X > max.X then max.X <- b.X
                        if b.Y > max.Y then max.Y <- b.Y
                        if b.Z > max.Z then max.Z <- b.Z
    
                        if c.X < min.X then min.X <- c.X
                        if c.Y < min.Y then min.Y <- c.Y
                        if c.Z < min.Z then min.Z <- c.Z
                        if c.X > max.X then max.X <- c.X
                        if c.Y > max.Y then max.Y <- c.Y
                        if c.Z > max.Z then max.Z <- c.Z
                        
                        totalBB <- Box3d(min, max)
                        Triangle3d(V3d p0, V3d p1, V3d p2) |> Some
                )
        
        let tree = Geometry.KdTree.build Geometry.Spatial.triangle (Geometry.KdBuildInfo(100, 5)) triangles
        tree
    
    let leaves =
        pHs
        |> List.collect(fun x ->
            x.tree |> QTree.getLeaves |> Seq.toList |> List.map(fun y -> (x.opcPaths.Opc_DirAbsPath, y)))
    
    let kdTrees =
        leaves
        |> List.map(fun (dir,patch) -> (Patch.load (OpcPaths dir) ViewerModality.XYZ patch.info, dir, patch.info))
        |> List.map(fun ((a,_),c,d) -> (a,c,d))
        |> List.map ( fun (g,dir,info) ->
            buildKDTree g info.Local2Global
        )
    
    let pickSg events =
        leaves
        |> List.map(fun (dir,patch) -> (Patch.load (OpcPaths dir) ViewerModality.XYZ patch.info, dir, patch.info))
        |> List.map(fun ((a,_),c,d) -> (a,c,d))
        |> List.map2 ( fun t (g,dir,info) ->
            let pckShp = t |> PickShape.Triangles
            Sg.ofIndexedGeometry g
            |> Sg.pickable pckShp
            |> Sg.trafo (AVal.constant info.Local2Global)
        ) kdTrees
        |> Sg.ofList
        |> Sg.requirePicking
        |> Sg.noEvents
        |> Sg.withEvents events
        |> Sg.transform preTransform
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! DefaultSurfaces.constantColor C4f.DarkRed
        }
      |> Sg.depthTest (AVal.constant DepthTestMode.Never)


    let getRealMars events =
      mkISg ()
        |> Sg.effect defaultEffects
        |> Sg.andAlso (pickSg events)



//open Aardvark.Opc

//module Terrain =
    
//    let mars () =
//        let patchHierarchies =
//            System.IO.Directory.GetDirectories(@"..\..\data\mars")
//            |> Seq.collect System.IO.Directory.GetDirectories

//        { 
//            useCompressedTextures = true
//            preTransform     = Trafo3d.Identity
//            patchHierarchies = patchHierarchies
//            boundingBox      = Box3d.Parse("[[3376372.058677169, -325173.566694686, -121309.194857123], [3376385.170513898, -325152.282144333, -121288.943956908]]")
//            near             = 0.1
//            far              = 10000.0
//            speed            = 3.0
//            lodDecider       = DefaultMetrics.mars
//        }
    
//    let scene = mars()

//    let up = scene.boundingBox.Center.Normalized

//    let preTransform =
//        let bb = scene.boundingBox
//        //Trafo3d.Translation(-bb.Center) * scene.preTransform
//        Trafo3d.Translation(-bb.Center + (24.0 * up)) * scene.preTransform
    


//    let mkISg() =
//        Aardvark.Opc.Sg2.createFlatISg scene
//        |> Sg.noEvents
//        |> Sg.transform preTransform
    
//    let defaultEffects =
//        [
//            DefaultSurfaces.trafo |> toEffect
//            DefaultSurfaces.constantColor C4f.White |> toEffect
//            DefaultSurfaces.diffuseTexture |> toEffect
//        ]

//    let buildKDTree (g : IndexedGeometry) =
//        let pos = g.IndexedAttributes.[DefaultSemantic.Positions] |> unbox<V3f[]>
//        let index = g.IndexArray |> unbox<int[]>

//        let triangles =
//            [| 0 .. 3 .. index.Length - 2 |] 
//                |> Array.choose (fun bi -> 
//                    let p0 = pos.[index.[bi]]
//                    let p1 = pos.[index.[bi + 1]]
//                    let p2 = pos.[index.[bi + 2]]
//                    if isNan p0 || isNan p1 || isNan p2 then
//                        None
//                    else
//                        Triangle3d(V3d p0, V3d p1, V3d p2) |> Some
//                )
        
//        let tree = Geometry.KdTree.build Geometry.Spatial.triangle (Geometry.KdBuildInfo(100, 5)) triangles
//        tree
    
//    let patchHierarchies =
//        [ 
//            for h in scene.patchHierarchies do
//                let p = Path.combine [h; @"Patches\patchhierarchy.xml" ]
//                yield PatchHierarchy.load p
//        ]
    
//    let leaves = 
//        patchHierarchies 
//        |> List.collect(fun x ->  
//            x.tree |> QTree.getLeaves |> Seq.toList |> List.map(fun y -> (x.baseDir, y)))
        
//    let kdTrees =
//        leaves
//        |> List.map(fun (dir,patch) -> (Patch.load dir patch.info, dir, patch.info))
//        |> List.map(fun ((a,_),c,d) -> (a,c,d))
//        |> List.map ( fun (g,dir,info) ->
//            buildKDTree g
//        )
    
//    let pickSg events =
//        leaves
//        |> List.map(fun (dir,patch) -> (Patch.load dir patch.info, dir, patch.info))
//        |> List.map(fun ((a,_),c,d) -> (a,c,d))               
//        |> List.map2 ( fun t (g,dir,info) ->
//            let pckShp = t |> PickShape.Triangles
//            Sg.ofIndexedGeometry g
//            |> Sg.pickable pckShp
//            |> Sg.trafo (AVal.constant info.Local2Global)
//        ) kdTrees
//        |> Sg.ofList
//        |> Sg.requirePicking
//        |> Sg.noEvents
//        |> Sg.withEvents events
//        |> Sg.transform preTransform
//        |> Sg.shader {
//            do! DefaultSurfaces.trafo
//            do! DefaultSurfaces.constantColor C4f.DarkRed
//        }
//        |> Sg.depthTest (AVal.constant DepthTestMode.Never)
