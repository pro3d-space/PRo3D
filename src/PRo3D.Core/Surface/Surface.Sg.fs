namespace PRo3D.Core.Surface

open System
open System.IO
open Aardvark.Base
open Aardvark.Base.Ag
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Data.Opc
open Aardvark.SceneGraph.Semantics
open Aardvark.UI

open Aardvark.UI.Primitives
open Aardvark.UI

open Aardvark.UI.Operators
open Aardvark.UI.Trafos  

open Aardvark.GeoSpatial.Opc
open Aardvark.Data.Opc
//open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open Aardvark.GeoSpatial.Opc.PatchLod
open Aardvark.GeoSpatial.Opc.Load
open OpcViewer.Base
open Aardvark.Rendering.Text
open Aardvark.Geometry

module FootprintSg = 
    open Aardvark.SceneGraph.Sg
    
    type FootprintApplicator(vp : aval<M44d>, child : ISg)  =
        inherit AbstractApplicator(child)
        member x.ViewProj = vp

    [<Rule>]
    type FootprintSem() =
        member x.FootprintVP(n : FootprintApplicator, scope : Ag.Scope) =
            n.Child?FootprintVP <- n.ViewProj

//module DepthSg = 
//    open Aardvark.SceneGraph.Sg
    
//    type DepthApplicator(vp : aval<M44d>, child : ISg)  =
//        inherit AbstractApplicator(child)
//        member x.ViewProj = vp

//    [<Rule>]
//    type DepthSem() =
//        member x.DepthVP(n : DepthApplicator, scope : Ag.Scope) =
//            n.Child?DepthVP <- n.ViewProj

module Sg =
    
    type Ag.Scope with
        member x.FootprintVP : aval<M44d> = x?FootprintVP

    let applyFootprint (v : aval<M44d>) (sg : ISg) = 
        FootprintSg.FootprintApplicator(v, sg) :> ISg

    //type Ag.Scope with
    //    member x.DepthVP : aval<M44d> = x?DepthVP

    //let applyDepth (v : aval<M44d>) (sg : ISg) = 
    //    DepthSg.DepthApplicator(v, sg) :> ISg

    type SgHelper = {
        surf   : Surface
        bb     : Box3d
        sg     : ISg        
        kdtree : HashMap<Box3d,KdTrees.Level0KdTree>
        scene  : OpcScene
        patchHierarchies : array<PatchHierarchy>
    }

    let mutable hackRunner : Option<Load.Runner> = None
    let mutable useAsyncLoading = true
    
    let lodDeciderMars 
        (preTrafo    : Trafo3d)
        (self        : AdaptiveToken) 
        (viewTrafo   : aval<Trafo3d>)
        (_projection : aval<Trafo3d>) 
        (p           : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) 
        (lodParams   : aval<LodParameters>)
        (isActive    : aval<bool>)
        =

        let lodParams = lodParams.GetValue self
        let isActive = isActive.GetValue self
       
        let campPos = (viewTrafo.GetValue self).Backward.C3.XYZ
        let bb      = p.info.GlobalBoundingBox.Transformed(lodParams.trafo * preTrafo ) //* preTrafo)
        let closest = bb.GetClosestPointOn(campPos)
        let dist    = (closest - campPos).Length

        //// super agressive to prune out far away stuff, too aggresive !!!
        //if not isActive || (campPos - bb.Center).Length > p.info.GlobalBoundingBox.Size.[p.info.GlobalBoundingBox.Size.MajorDim] * 1.5 
        //    then false
        //else

        let unitPxSize = lodParams.frustum.right / (float lodParams.size.X / 2.0)
        let px = (lodParams.frustum.near * p.triangleSize) / dist // (pow dist 1.2) // (added pow 1.2 here... discuss)

            //    Log.warn "%f to %f - avgSize: %f" px (unitPxSize * lodParams.factor) p.triangleSize
        px > unitPxSize * (exp lodParams.factor)

    let computeScreenSpaceArea (b : Box3d) (viewProj : Trafo3d) =
        let viewSpacePoints =
            b.ComputeCorners()
                |> Array.map (fun v ->
                    viewProj.Forward.TransformPosProj v
                )

        let poly = viewSpacePoints |> Array.map Vec.xy |> Polygon2d

        let clipped = 
            poly.ComputeConvexHullIndexPolygon().ToPolygon2d()
    
        let bounds = Box3d(viewSpacePoints)
        if Box3d(-V3d.III, V3d.III).Intersects(bounds) |> not then 0.0
        else clipped.ComputeArea()

    let marsArea (preTrafo    : Trafo3d)
        (self        : AdaptiveToken) 
        (viewTrafo   : aval<Trafo3d>)
        (projTrafo   : aval<Trafo3d>) 
        (renderPatch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) 
        (lodParams   : aval<LodParameters>)
        (isActive    : aval<bool>) =
        
        let isActive = isActive.GetValue self
        if not isActive then false
        else
            let m = renderPatch.trafo.GetValue self
            let view = viewTrafo.GetValue self
            let proj = projTrafo.GetValue self
            let p = lodParams.GetValue self
            let vp   = view * proj
            let mvp = m * vp
            let area = computeScreenSpaceArea renderPatch.info.LocalBoundingBox mvp
                    
            log area > 1.0 - (log p.factor) * 1.2

    module Helper = 

        let intersectBox' (b : Box3d) (r : FastRay3d) = 
            let mutable tmin = -infinity
            let mutable tmax = infinity
            if r.Intersects(b, &tmin, &tmax) then
                Some (tmin, tmax)
            else
                None

        let intersectBox (b : Box3d) (r : Ray3d) =
            FastRay3d r |> intersectBox' b



    // hs: not sure what the original intention was, but was obviusly wrong (but worked reasonably well for most scenes)
    // this this version removed obvious problems but is still much worse than the reworked on reworkedLoD
    let cleanedOldLegacyLoD 
        (preTrafo    : Trafo3d)
        (self        : AdaptiveToken) 
        (viewTrafo   : aval<Trafo3d>)
        (projTrafo   : aval<Trafo3d>) 
        (renderPatch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) 
        (lodParams   : aval<LodParameters>)
        (isActive    : aval<bool>)
        =

        let isRenderingActive = isActive.GetValue self
        if isRenderingActive then
            let lodParams = lodParams.GetValue self
            let viewTrafo = viewTrafo.GetValue self
            let model = preTrafo * renderPatch.trafo.GetValue self
            let proj = projTrafo.GetValue self
            let viewProj = viewTrafo * proj

            let globalBBModelSpace = renderPatch.info.LocalBoundingBox.Transformed(model)
            let cornersNdc = globalBBModelSpace.ComputeCorners() |> Array.map viewProj.TransformPosProj
            let boundsNdc = Box3d(cornersNdc)
            if Box3d(-V3d.IIO, V3d.III).Intersects(boundsNdc) then
                let campPos = viewTrafo.Backward.C3.XYZ
                let bb      = renderPatch.info.GlobalBoundingBox.Transformed(lodParams.trafo) 
                let closest = bb.GetClosestPointOn(campPos)
                let dist    = (closest - campPos).Length

                let unitPxSize = (lodParams.frustum.right - lodParams.frustum.left) / (float lodParams.size.X * 0.5)
                let px = (0.1 * renderPatch.triangleSize) / (pow dist 1.2) // (pow dist 1.2) // (added pow 1.2 here... discuss)

                // Log.warn "%f to %f - avgSize: %f" px (unitPxSize * lodParams.factor) p.triangleSize
                px > unitPxSize * (exp lodParams.factor)
            else
                false
        else
            false

    let reworkedLoD 
        (preTrafo    : Trafo3d)
        (intersect   : ValueOption<FastRay3d -> ValueOption<V3d>>)
        (self        : AdaptiveToken) 
        (viewTrafo   : aval<Trafo3d>)
        (projTrafo   : aval<Trafo3d>) 
        (renderPatch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) 
        (lodParams   : aval<LodParameters>)
        (isActive    : aval<bool>) =
        
        let renderingActive = isActive.GetValue self
        if not renderingActive then false
        else
            let model = preTrafo * renderPatch.trafo.GetValue self
            let view = viewTrafo.GetValue self
            let proj = projTrafo.GetValue self
            let p = lodParams.GetValue self
            let viewProj = view * proj

            let globalBBModelSpace = renderPatch.info.LocalBoundingBox.Transformed(model)
            let cornersNdc = globalBBModelSpace.ComputeCorners() |> Array.map viewProj.TransformPosProj
            let boundsNdc = Box3d(cornersNdc)
            if Box3d(-V3d.IIO, V3d.III).Intersects(boundsNdc) then
                // if we have a potential hit, also use the hold hacky one, if both (the new one and the old one) agree on going deeper, let's do it
                // the situation is rather complex since many special cases (huge bounding boxes, inhomogenous point densities etc)
                let legacyDecider = lodDeciderMars preTrafo self viewTrafo projTrafo renderPatch lodParams isActive

                let camPos = view.Backward.C3.XYZ
                let ray = Ray3d(view.Backward.C3.XYZ, -view.Backward.C2.XYZ)
                let fastRay = FastRay3d(ray)
                let referencePoint =
                    match Helper.intersectBox' globalBBModelSpace fastRay with
                    | Some (tmin,tmax) -> 
                        match intersect with
                        | ValueSome intersectionFunction ->
                            match intersectionFunction fastRay with
                            | ValueNone -> 
                                Log.warn "no hit"
                                globalBBModelSpace.Center
                            | ValueSome v -> 
                                v
                        | ValueNone -> 
                            if globalBBModelSpace.Contains(camPos) then 
                                let scnd = ray.GetPointOnRay(tmax)
                                scnd
                            else
                                ray.GetPointOnRay(tmax)
                    | _ -> 
                        globalBBModelSpace.Center
                // approach: place virtual sphere with radius = triangle size at bb center
                // go deeper until virtual sphere is smaller than one pixel 
                let localLodFocusPoint = referencePoint// arbitrary. use center for computing screen space triangle size
                let lodCenterViewSpace = view.TransformPos(localLodFocusPoint)
                let pointOnSphere = lodCenterViewSpace + V3d(renderPatch.triangleSize, renderPatch.triangleSize, 0.0)
                let lodCenterNdc = proj.TransformPosProj(lodCenterViewSpace)
                let pointOnSphereNdc = proj.TransformPosProj(pointOnSphere)
                let triangleSizeInNcs = Vec.distance pointOnSphereNdc lodCenterNdc
                // true = go deeper
                // more restrictive condition = less LoDs
                let normalizedQuality = p.factor - (-2.0) / (5.0 - (-2.0))
                // triangle size in [-1,1] space, scale to [0,1], rescale with max viewport dimension to get to pixels
                // roughly. Next, go deepter if triangle is larger than largestTriangleInPixels

                let largestTriangleInPixels = 5.0
                triangleSizeInNcs * 0.5 * float p.size.NormMax > largestTriangleInPixels * normalizedQuality && legacyDecider
            else
                // culled 
                false

    let createPlainSceneGraph 
        (runtime        : IRuntime) 
        (signature      : IFramebufferSignature) 
        (scene          : OpcScene) 
        (createKdTrees  : bool)
        : (ISg * array<PatchHierarchy> * HashMap<Box3d, KdTrees.Level0KdTree>) =

        let runner = 
            match hackRunner with
            | None -> 
                failwith "GL runner was not initialized."
            | Some h -> h

        let patchHierarchies = 
            scene.patchHierarchies
            |> Seq.map Prinziple.register
            |> Seq.map (fun x -> 
                PatchHierarchy.load Serialization.binarySerializer.Pickle Serialization.binarySerializer.UnPickle (OpcPaths x)
            )
            |> Seq.toArray

        let intersect =
            let rayGuidedLoD = false
            if rayGuidedLoD then
                let kdTrees = 
                    patchHierarchies 
                    |> Array.choose (fun h -> 
                        Log.startTimed "loading lowesd kd"
                        let kdTree = 
                            let level, info = 
                                match h.tree with
                                | QTree.Node(p,_) -> p.level, p.info
                                | QTree.Leaf p -> 0, p.info
                            match h.kdTree_FileAbsPath info.Name -1 ViewerModality.XYZ |> KdTrees.tryFixPatchFileIfNeeded with
                            | None -> 
                                Log.warn "no kd tree for level 0"
                                None
                            | Some kdPath ->
                                let kd = KdTrees.loadKdtree kdPath
                                match h.opcPaths.Patches_DirAbsPath +/ info.Name +/ info.Positions |> KdTrees.tryFixPatchFileIfNeeded with
                                | None -> 
                                    None
                                | Some objectSetPath -> 
                                    let t = DebugKdTreesX.loadTriangles' info.Local2Global  objectSetPath
                                    kd.KdIntersectionTree.ObjectSet <- t
                                    Some (h,kd, info.GlobalBoundingBox)
                        Log.stop()
                        kdTree
                    )

                let intersect (r : FastRay3d) = 
                    let hits = 
                        kdTrees 
                        |> Seq.filter (fun (h,kd,bb) ->
                            Helper.intersectBox' bb r |> Option.isSome
                        )
                        |> Seq.choose (fun (h,kd,bb) ->
                            let mutable hit = ObjectRayHit.MaxRange
                            let intersecBox = Helper.intersectBox' kd.KdIntersectionTree.BoundingBox3d r
                            if kd.KdIntersectionTree.Intersect(r, 0.0, Double.MaxValue, &hit) then
                                Some hit
                            else
                                None
                        )
                    if Seq.isEmpty hits then 
                        ValueNone
                    else 
                        let h = hits |> Seq.minBy (fun h -> h.RayHit.T)
                        ValueSome h.RayHit.Point

                ValueSome intersect

            else    
                ValueNone
            
                            
        let kdTreesPerHierarchy =
            [| 
                for h in patchHierarchies do
                    if createKdTrees then   
                        Log.startTimed "[KdTrees] Loading kdtrees: %s" h.opcPaths.Patches_DirAbsPath
                        let m = KdTrees.loadKdTrees h Trafo3d.Identity ViewerModality.XYZ Serialization.binarySerializer false false DebugKdTreesX.loadTriangles' true    
                        Log.stop()
                        if HashMap.isEmpty m then
                            Log.warn "[KdTrees], KdTree map for %s is empty." h.opcPaths.Patches_DirAbsPath
                            yield m
                        else
                            yield m
                    else 
                        yield HashMap.empty
            |]

        let totalKdTrees = kdTreesPerHierarchy.Length
        Log.line "fusing %d kdTrees" totalKdTrees

        let kdTrees = 
            kdTreesPerHierarchy                     
            |> Array.fold HashMap.union HashMap.empty

        let createShadowContext (f : Aardvark.GeoSpatial.Opc.PatchLod.PatchNode) (scope : Scope) =
             match scope.TryGetInherited "LightViewProj" with
             | None -> Option<aval<Trafo3d>>.None :> obj
             | Some v -> Some (v |> unbox<aval<Trafo3d>>) :> obj

        let uniforms = 
             Map.ofList [    
                 //"LightViewProj", fun scope (rp : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                 "LightViewProj", fun scope (rp : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                     let vp : Option<aval<Trafo3d>> = unbox scope
                     match vp with
                     | Some vp -> 
                         AVal.map2 (fun (m : Trafo3d) (vp : Trafo3d) -> (m * vp).Forward)                                     
                                   rp.trafo vp :> IAdaptiveValue
                     | None -> 
                         Log.error "did not provide LightViewProj but shader wanted it."
                         (AVal.constant M44f.Identity) :> IAdaptiveValue
                 "HasLightViewProj", fun scope _ -> 
                     let vp : Option<aval<Trafo3d>> = unbox scope
                     match vp with
                         | Some _ -> AVal.constant true :> IAdaptiveValue
                         | _ -> AVal.constant false :> IAdaptiveValue
             ]
     
        //let lodDeciderMars = lodDeciderMars scene.preTransform
        //let lodDeciderMars = marsArea scene.preTransform
        //let lodDeciderMars = reworkedLoD scene.preTransform intersect
        let lodDeciderMars = cleanedOldLegacyLoD  scene.preTransform 

        let map = 
            Map.ofList [
                "FootprintModelViewProj", fun scope (patch : RenderPatch) -> 
                    let viewTrafo,_ = scope |> unbox<aval<M44d> * obj>
                    let r = AVal.map2 (fun viewTrafo (model : Trafo3d) -> viewTrafo * model.Forward) viewTrafo patch.trafo 
                    r :> IAdaptiveValue
            ]

        // create level of detail hierarchy (Sg)
        let g = 
            patchHierarchies 
            |> Array.map (fun h ->      
                let patchLodWithTextures = 
                    let context (n : PatchNode) (s : Ag.Scope) =
                        let vp = s.FootprintVP
                        let secondaryTexture = SecondaryTexture.getSecondary n s
                        (vp, secondaryTexture)  :> obj

                    let extractTextureScope f (p : OpcPaths) (lodScope : obj) (r : RenderPatch) =
                        let (_, textures) = unbox<aval<M44d> * obj> lodScope
                        f p textures r 

                    let getTextures = extractTextureScope SecondaryTexture.textures
                    let getVertexAttributes = extractTextureScope SecondaryTexture.vertexAttributes

                    PatchNode(
                        signature, 
                        runner, 
                        h.opcPaths.Opc_DirAbsPath, 
                        lodDeciderMars, 
                        scene.useCompressedTextures, 
                        true, 
                        ViewerModality.XYZ, 
                        PatchLod.CoordinatesMapping.Local, 
                        useAsyncLoading, 
                        context, 
                        map,
                        PatchLod.toRoseTree h.tree,
                        Some (getTextures h.opcPaths), 
                        Some (getVertexAttributes h.opcPaths), 
                        Aardvark.Data.PixImagePfim.Loader
                    )
                patchLodWithTextures
            )
            |> SgFSharp.Sg.ofArray  
                                                                      
        g, patchHierarchies, kdTrees
    
    let assertInvalidBB (bb:Box3d) = 
        if(bb.IsInvalid) then 
            failwith (sprintf "invalid bounding box %s" (bb.ToString()))
        else bb
    
    let combineLeafBBs (hierarchies : array<PatchHierarchy>) =
        hierarchies
        |> Array.map(fun d -> d.tree |> QTree.getLeaves)
        |> Array.map (fun p -> p |> Seq.map(fun d -> assertInvalidBB d.info.GlobalBoundingBox)) 
        |> Seq.concat
        |> Box3d
    
    let combineRootBBs (hierarchies : list<PatchHierarchy>) =
        hierarchies
        |> List.map(fun d -> d.tree |> QTree.getRoot)                  
        |> Seq.map (fun p -> assertInvalidBB p.info.GlobalBoundingBox)
        |> Box3d
       
    let createSgSurface 
        (scene : OpcScene) 
        (s     : Surface) 
        (sg    : ISg)     
        (patchHierarchies : array<PatchHierarchy>)
        (bb    : Box3d) 
        (kd    : HashMap<Box3d,KdTrees.Level0KdTree>) = 
    
        let pose = Pose.translate V3d.Zero // bb.Center
        let trafo = { TrafoController.initial with pose = pose; previewTrafo = Pose.toTrafo pose; mode = TrafoMode.Local }
    

        let sgSurface = {
                surface     = s.guid
                sceneGraph  = sg
                globalBB    = bb
                picking     = Picking.KdTree kd
                trafo       = trafo
                isObj       = false
                opcScene    = Some scene
                dataSource  = DataSource.OpcHierarchy patchHierarchies
                //transformation = Init.Transformations
            }
        sgSurface
    
    let transformBox (trafo:Trafo3d) (bb:Box3d) = bb.Transformed(trafo)
              
    let createSgSurfaces 
        runtime 
        signature 
        (surfaces : IndexList<Surface>)
        : HashMap<Guid, SgSurface> =
      
        let surfaces = 
            surfaces
            |> IndexList.toList
            |> List.filter(fun s ->
                let dirExists = Directory.Exists s.importPath
                if dirExists |> not then 
                    Log.error "[Surface.Sg] could not find %s" s.importPath
                dirExists
            )
        
        let sghs =
            surfaces          
            |> List.map (fun surface -> 
                let surface = 
                    { surface with opcPaths = surface.opcNames |> Files.expandNamesToPaths surface.importPath }
                let opcScene =
                    { Configurations.Empty.mars() with
                       patchHierarchies      = surface.opcPaths
                       preTransform          = surface.preTransform
                       useCompressedTextures = false
                       lodDecider            = DefaultMetrics.mars2
                    }
                let (sg, hierarchies, kdtree) = createPlainSceneGraph runtime signature opcScene true

                let bb = 
                    hierarchies 
                    |> combineLeafBBs 
                    |> transformBox surface.preTransform

                let sgSurface = createSgSurface opcScene surface sg hierarchies bb kdtree
                sgSurface.surface, sgSurface
            )
            |> HashMap.ofList       
        
        sghs
    
    

    let viewHomePosition (model:AdaptiveSurfaceModel) : ISg<'a> =
        let point = 
            aset{
                let! guid = model.surfaces.singleSelectLeaf
                let fail = Sg.empty

                match guid with
                | Some i -> 
                    let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                    if exists then
                        let leaf = model.surfaces.flat |> AMap.find i 
                        let! surf = leaf 
                        let x = 
                            match surf with 
                            | AdaptiveSurfaces s -> s 
                            | _ -> surf |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith

                        let! hpos = x.homePosition
                        match hpos with
                        | Some p -> yield Sg.dot (AVal.constant C4b.Yellow) (AVal.constant 3.0) (AVal.constant p.Location)
                        | None -> yield fail
                    else
                        yield fail
                | None -> 
                    yield fail
            }|> Aardvark.UI.``F# Sg``.Sg.set
        Aardvark.UI.``F# Sg``.Sg.ofList [point]

    let viewLeafLabels 
        (near   : aval<float>)
        (fov    : aval<float>) 
        (view   : aval<CameraView>) 
        (model  : AdaptiveSurfaceModel) 
        : ISg<'a> =

        let points = 
            aset{                
                let! guid = model.surfaces.singleSelectLeaf
                match guid with
                | Some surfaceId -> 
                    let! selectedSgSurface = (model.sgSurfaces |> AMap.tryFind surfaceId)
                    match selectedSgSurface with
                    | Some s -> 

                        let leafLabels =
                            match s.dataSource with
                            | DataSource.OpcHierarchy patchHierarchies -> 
                                patchHierarchies
                                |> Array.map (fun h -> h.tree |> QTree.getLeaves)
                                |> Seq.concat
                                |> Seq.map (fun p -> (p.info.Name, p.info.GlobalBoundingBox))
                            | _ -> 
                                []

                        let labels = 
                            leafLabels
                            |> Seq.map (fun (name, box) -> 
                                let pos = box.Center
                                (PRo3D.Base.Sg.text (view) (near) (fov) ~~pos (~~pos |> AVal.map Trafo3d.Translation) ~~0.05 ~~name ~~C4b.White) 
                                |> Sg.andAlso (Sg.dot (AVal.constant C4b.VRVisGreen) (AVal.constant 5.0) (~~pos))
                            )
                        yield! labels                        
                    | None -> yield Sg.empty
                | None -> 
                    yield Sg.empty
            }

        points 
        |> ASet.map Sg.noEvents 
        |> Sg.set
        
        
            
                                  

