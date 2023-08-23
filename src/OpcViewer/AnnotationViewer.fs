namespace Aardvark.Opc

open System.IO
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.SceneGraph.Opc
open Aardvark.Application.Slim
open Aardvark.GeoSpatial.Opc

open Aardvark.UI
open Aardvark.SceneGraph
open FSharp.Data.Adaptive 
open MBrace.FsPickler
open PRo3D.Base.Annotation
open PRo3D.Core

open PRo3D.Core.Drawing
open System.Collections.Generic
open Aardvark.Rendering
open System

open Adaptify.FSharp.Core

open PRo3D.Core.PackedRendering
open PRo3D.Base
open Aardvark.Geometry






module QTree =

    open Aardvark.SceneGraph.Opc

    let rec foldCulled (consider : Box3d -> bool) (f : Patch -> 's -> 's) (seed : 's) (tree : QTree<Patch>) =
        match tree with
        | Node(p, children) -> 
            if consider p.info.GlobalBoundingBox then
                Seq.fold (foldCulled consider f) seed children
            else
                seed
        | Leaf(p) -> 
            if consider p.info.GlobalBoundingBox then
                f p seed
            else
                seed

        
type QueryAttribute = { channels : int; array : System.Array }
type QueryResult = 
    {
        attributes        : Map<string, QueryAttribute>
        globalPositions   : IReadOnlyList<V3d>
        localPositions    : IReadOnlyList<V3f>
        patchFileInfoPath : string
        indices           : IReadOnlyList<int>
    }

module AnnotationViewer = 

    
    let createAnnotationSg (win : IRenderWindow) (view : aval<CameraView>) (frustum : aval<Frustum>) (showOld : aval<bool>) (pick : Annotation -> unit) (annotations : Annotations) =
        let model = AdaptiveGroupsModel.Create annotations.annotations
        let runtime = win.Runtime

        let annoSet = 
            model.flat 
            |> AMap.choose (fun _ y -> y |> PRo3D.Core.Drawing.DrawingApp.tryToAnnotation) // TODO v5: here we collapsed some avals - check correctness
            |> AMap.toASet

        let config : Sg.innerViewConfig =
            {
                nearPlane        = AVal.constant 0.1
                hfov             = AVal.constant 60.0         
                arrowThickness   = AVal.constant 2.0
                arrowLength      = AVal.constant 2.0
                dnsPlaneSize     = AVal.constant 1.0
                offset           = AVal.constant 2.4
                pickingTolerance = AVal.constant 0.01
            }


        //let selectedAnnotation = cval -1
        let hoveredAnnotation = cval -1
        let picked = cval None


        let mv = view |> AVal.map (fun c -> (CameraView.viewTrafo c).Forward)
        let points = points (model.selectedLeaves |> ASet.map (fun x -> x.id)) annoSet config.offset mv
        let lines, pickIds, bb = PackedRendering.linesNoIndirect config.offset hoveredAnnotation (picked |> AVal.map Option.toList |> ASet.ofAVal) annoSet mv

        let pickColors = 
            //let size = AVal.constant (V2i(128,128))
            let size = win.Sizes
            let signature =
                runtime.CreateFramebufferSignature [
                    DefaultSemantic.Colors, TextureFormat.Rgba32f
                    DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
                ]

            let pickColors = 
                lines
                |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
                |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo) //(size |> AVal.map (fun s -> Frustum.perspective 20.0 0.01 10000.0 (s.X / s.Y)))
                |> Sg.shader { 
                      do! LineShader.noIndirectLineVertexPicking
                      do! LineShader.thickLine
                      do! PRo3D.Base.Shader.DepthOffset.depthOffsetFS 
                      do! Picking.pickId
                }
                |> Sg.uniform "PickingTolerance" config.pickingTolerance
                |> Sg.compile runtime signature

            let cleared = RenderTask.ofList [
                runtime.CompileClear(signature, C4f(0.0f,0.0f,0.0f,-1.0f))
                pickColors
            ] 

            cleared |> RenderTask.renderToColor size

        if true then
            win.Mouse.Move.Values.Add(fun (o,p) -> 
                
                let r = pickColors.GetValue(AdaptiveToken.Top,RenderToken.Empty)
                let offset = V3i(p.Position.X,p.Position.Y,0)
                if p.Position.X < r.Size.X - 1 && p.Position.Y < r.Size.Y - 1 && p.Position.X >= 0  then
                    let r = runtime.Download(r,0,0,Box2i.FromMinAndSize(p.Position, V2i(1,1))) |> unbox<PixImage<float32>>
                    let m = r.GetMatrix<C4f>()
                    //let center = m.Size.XY / 2L
                    //let ids = pickIds.GetValue()
                    //let mutable bestDist = Double.MaxValue
                    //let mutable bestId = -1
                    //m.ForeachCoord(fun (c : V2l) ->
                    //    let d = Vec.lengthSquared (c - center)
                    //    if d < bestDist then
                    //        let p = m.[c]
                    //        let id : int = BitConverter.SingleToInt32Bits(p.A)
                    //        if id > 0 && id < ids.Length then
                    //            bestDist <- d
                    //            bestId <- id
                    //)
                    //if bestId > 0 then
                    //    Log.line "hit %A" (id, ids.[bestId]
                    //    transact (fun _ -> selectedAnnotation.Value <- id)
                    let p = m.[0,0]
                    let id : int = int (floor p.A) //BitConverter.SingleToInt32Bits(p.A)
                    let ids = pickIds.GetValue()
                    printfn "id: %A" id
                    if id >= 0 && id < ids.Length then
                        Log.line "hit %A" (id, ids.[id])
                        transact (fun _ -> hoveredAnnotation.Value <- id)
                    else 
                        transact (fun _ -> hoveredAnnotation.Value <- -1)
                    //r.SaveAsImage("guh")
                    ()
            )

            win.Mouse.Click.Values.Add(fun b -> 
                if b = MouseButtons.Right then
                    let hovered = hoveredAnnotation.GetValue()
                    let ids = pickIds.GetValue()
                    transact (fun _ -> 
                        if hovered >= 0 && hovered < ids.Length then
                            picked.Value <- Some ids.[hovered]
                            match HashMap.tryFind ids[hovered] annotations.annotations.flat with
                            | None -> ()
                            | Some anno -> 
                                match anno with
                                | Leaf.Annotations a -> 
                                    try pick a with e -> Log.warn "pick failed. %A" e
                                | _ -> ()
                        else 
                            picked.Value <- None
                    )

            )

        let overlay = 
            Sg.fullScreenQuad
            |> Sg.scale 0.1
            |> Sg.translate -0.8 0.8 0.0
            |> Sg.diffuseTexture pickColors
            |> Sg.shader {
                do! DefaultSurfaces.trafo
                do! LensShader.lens
            }
            |> Sg.uniform "MousePosition" ((win.Mouse.Position, win.Sizes) ||> AVal.map2 (fun (p : PixelPosition) s -> V2d(p.NormalizedPosition.X,1.0-p.NormalizedPosition.Y)))
            |> Sg.viewTrafo' Trafo3d.Identity
            |> Sg.projTrafo' Trafo3d.Identity


        let lineSg  =
            lines 
            |> Sg.shader { 
                  do! LineShader.noIndirectLineVertex
                  do! LineShader.thickLine
                  do! PRo3D.Base.Shader.DepthOffset.depthOffsetFS 
            }

        let onOff b (s : ISg) = 
            adaptive { 
                let! b = b
                if b then return s else return Sg.empty
            }

        let newSg =
            Sg.ofList [points; lineSg]
            |> onOff (AVal.map not showOld)

        let sg = 
            annoSet 
            |> ASet.map(fun (_,a) -> 
                let c = UI.mkColor model a
                let picked = UI.isSingleSelect model a
                let showPoints = 
                  a.geometry 
                    |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)
    
                let sg = Sg.finishedAnnotationOld a c config view win.Sizes showPoints picked (AVal.constant false)
                sg :> ISg
            ) 
            |> Sg.set 
            |> onOff showOld


    
        let fc = AdaptiveFalseColorsModel.Create PRo3D.Base.FalseColorsModel.initDnSLegend
        let dnsOld =  
            annoSet 
            |> ASet.map (fun (_,a) -> 
                Sg.finishedAnnotationDiscs a config fc view :> ISg
            )
            |> Sg.set
            |> onOff showOld


        let newDns = 
            fastDns config fc annoSet view |> onOff (AVal.map not showOld)

        Log.startTimed "[Drawing] creating finished annotation geometry"
        let selected =              
            annoSet 
            |> ASet.map(fun (g,a) -> 
                let c = UI.mkColor model a
                let picked = picked |> AVal.map (function | Some v when g = v -> true | _ -> false)
                let showPoints = 
                  a.geometry 
                    |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)
                
                let vm = view |> AVal.map (fun v -> (CameraView.viewTrafo v).Forward)
                let points = PRo3D.Core.Drawing.Sg.getPolylinePoints a   
                let width = a.thickness.value |> AVal.map (fun x -> x + 3.0) // 3.0
                //let spheres = PRo3D.Base.Sg.drawSpheresFast vm win.Sizes points width (AVal.constant C4b.Blue)

                let sg = Sg.finishedAnnotation a c config view win.Sizes showPoints picked (AVal.constant false) :> ISg
                sg
                //spheres :> ISg
            )
            |> Sg.set               
        Log.stop()

        Sg.ofSeq [Sg.dynamic sg; Sg.dynamic newSg; Sg.dynamic dnsOld; Sg.dynamic newDns; overlay; selected]

    let run (scene : OpcScene) (annotations : Annotations) =

        Aardvark.Init()

        use app = new OpenGlApplication()
        let win = app.CreateGameWindow(1)
        win.RenderAsFastAsPossible <- true
        win.VSync <- false
        let runtime = win.Runtime

        let runner = 
            match (win.Runtime :> obj) |> unbox<IRuntime> with
            | :? Aardvark.Rendering.GL.Runtime as r -> Some (r.CreateLoadRunner 1)
            | _ -> None

        let serializer = FsPickler.CreateBinarySerializer()

        let showSurface = true

        let sg, hierarchies = 
            if showSurface && runner.IsSome then
                let runner = runner.Value
                let hierarchies = 
                    scene.patchHierarchies 
                    |> Seq.toList 
                    |> List.map (fun basePath -> 
                        PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath), basePath
                    )
                let sg =
                    hierarchies |> List.map (fun (h, basePath) -> 
                        let t = PatchLod.toRoseTree h.tree
                        Sg.patchLod win.FramebufferSignature runner basePath scene.lodDecider false false ViewerModality.XYZ PatchLod.CoordinatesMapping.Local true t
                    ) |> Sg.ofList 
                sg, hierarchies
            else 
                Sg.empty, []

        let speed = AVal.init scene.speed

        let bb = scene.boundingBox
        let initialView = CameraView.lookAt bb.Max bb.Center bb.Center.Normalized
        let view = initialView |> DefaultCameraController.controlWithSpeed speed win.Mouse win.Keyboard win.Time
        let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 scene.near scene.far (float s.X / float s.Y))

        let lodVisEnabled = cval false
        let fillMode = cval FillMode.Fill
        let showOld = cval false

        win.Keyboard.KeyDown(Keys.PageUp).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value * 1.5)
        )

        win.Keyboard.KeyDown(Keys.PageDown).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value / 1.5)
        )

        win.Keyboard.KeyDown(Keys.L).Values.Add(fun _ -> 
            transact (fun _ -> lodVisEnabled.Value <- not lodVisEnabled.Value)
        )
        
        win.Keyboard.KeyDown(Keys.O).Values.Add(fun _ -> 
            transact (fun _ -> showOld.Value <- not showOld.Value)
        )

        win.Keyboard.KeyDown(Keys.F).Values.Add(fun _ -> 
            transact (fun _ -> 
                fillMode.Value <-
                    match fillMode.Value with
                    | FillMode.Fill -> FillMode.Line
                    | _-> FillMode.Fill
            )
        )

        let resultPoints = cval [||]

        let pick (hit : List<QueryResult> -> unit) (a : Annotation) =
            let corners = a.points |> IndexList.toSeq
            let segmentPoints = a.segments |> Seq.collect (fun s -> s.points)
            let points = Seq.concat [corners; segmentPoints]
            let lineRegression = 
                let regression = new LinearRegression3d(points)
                regression.TryGetRegressionInfo()
            let plane = 
                match lineRegression with
                | None -> 
                    Log.warn "line regression failed"
                    CSharpUtils.PlaneFitting.Fit(points |> Seq.toArray)
                | Some p -> p.Plane
            let projectedPolygon = 
                points 
                |> Seq.map (fun p -> plane.ProjectToPlaneSpace p)
                |> Polygon2d

            let intersectsQuery (globalBoundingBox : Box3d) =
                let p2w = plane.GetPlaneToWorld()
                let pointsInWorld = projectedPolygon.Points |> Seq.map (fun p -> p2w.TransformPos(V3d(p,0.0)))
                pointsInWorld |> Seq.exists (fun p -> globalBoundingBox.Contains p)
                

            let globalCoordWithinQuery (p : V3d) =
                let projected = plane.ProjectToPlaneSpace(p) 
                let h = plane.Height(p)
                projectedPolygon.Contains(projected) && h >= -0.2 && h < 0.2

            let handlePatch (paths : OpcPaths) (requestedAttributes : list<string>) (p : Patch) (o : List<QueryResult>) =
                let ig, _ = Patch.load paths ViewerModality.XYZ p.info
                let pfi = paths.Patches_DirAbsPath +/ p.info.Name
                let attributes = 
                    let available = Set.ofList p.info.Attributes
                    let attributes = 
                        requestedAttributes |> List.choose (fun l -> 
                            if Set.contains l available then
                                let path = paths.Patches_DirAbsPath +/ p.info.Name +/ l
                                if File.Exists path then
                                    Some (l, path)
                                else
                                    None
                            else 
                                Log.warn $"[Queries] requested attribute {l} but patch {p.info.Name} does not provide it." 
                                Log.line "[Queries] available attributes: %s" (available |> Set.toSeq |> String.concat ",")
                                None
                        )

                    attributes
                    |> List.map (fun (attributeName, filePath) -> 
                        let arr = filePath |> fromFile<float> // change this to allow different attribute types
                        attributeName, arr
                    )
                    |> Map.ofList

                //let positions = paths.Patches_DirAbsPath +/ p.info.Name +/ p.info.Positions |> fromFile<V3f>
                let positions = 
                    match ig.IndexedAttributes[DefaultSemantic.Positions] with
                    | (:? array<V3f> as v) when not (isNull v) -> v
                    | _ -> failwith "[Queries] Patch has no V3f[] positions"


                let idxArray = 
                    if ig.IsIndexed then
                        match ig.IndexArray with
                        | :? array<int> as idx -> idx
                        | _ -> failwith "[Queries] Patch index geometry has no int[] index"
                    else
                        failwith "[Queries] Patch index geometry is not indexed."


                let attributesInputOutput =
                    attributes 
                    |> Map.map (fun name inputArray -> 
                        inputArray, List<float>()
                    )
                let globalOutputPositions = List<V3d>()
                let localOutputPositions = List<V3f>()
                let indices = List<int>()
                

                for startIndex in 0 .. 3 ..  idxArray.Length - 3 do
                    let tri = [| idxArray[startIndex]; idxArray[startIndex + 1]; idxArray[startIndex + 2] |] 
                    let localVertices = tri |> Array.map (fun idx -> positions[idx])
                    if localVertices |> Array.exists (fun v -> v.IsNaN) then
                        ()
                    else
                        let globalVertices = 
                            localVertices |> Array.map (fun local -> 
                                let v = V3d local
                                p.info.Local2Global.TransformPos v
                            )
                        let validInside (v : V3d) = globalCoordWithinQuery v
                        let triWithinQuery = globalVertices |> Array.forall validInside
                        if triWithinQuery then
                            indices.Add(localOutputPositions.Count)
                            indices.Add(localOutputPositions.Count + 1)
                            indices.Add(localOutputPositions.Count + 2)
                            attributesInputOutput |> Map.iter (fun name (inputArray, output) -> 
                                tri |> Array.iter (fun idx -> 
                                    output.Add(inputArray[idx])
                                )
                            )
                            globalOutputPositions.AddRange(globalVertices)
                            localOutputPositions.AddRange(localVertices)

                o.Add {
                    attributes = attributesInputOutput |> Map.map (fun p (i,o) -> { channels = 1; array = o.ToArray() :> System.Array})
                    globalPositions = globalOutputPositions :> IReadOnlyList<V3d>
                    patchFileInfoPath = pfi
                    localPositions = localOutputPositions :> IReadOnlyList<V3f>
                    indices = indices :> IReadOnlyList<int>
                }
                o

            let requestedAttributes = ["AccuracyMap.aara"]

            let hits = 
                let points = List<QueryResult>()
                hierarchies |> List.fold (fun points (h, basePath) -> 
                    let paths = OpcPaths basePath
                    let result = QTree.foldCulled intersectsQuery (handlePatch paths requestedAttributes) points h.tree
                    printfn "%A" result
                    points
                ) points
             
            printfn "hits: %A" hits
            hit hits


        let hit (hits : List<QueryResult>) =
            let allVertices = hits |> Seq.collect (fun h -> h.globalPositions) |> Seq.toArray
            let objGeometries = 
                hits 
                |> Seq.map (fun h -> 
                    let colors =
                        match h.attributes |> Map.toSeq |> Seq.tryHead with
                        | None -> None
                        | Some (name, att) -> 
                            match att.array with
                            | :? array<float> as v when v.Length > 0 -> 
                                let min, max = v.Min(), v.Max()
                                if max - min > 0 then
                                    let colors = v |> Array.map (fun v -> if v.IsNaN() then C3b.Black else (v - min) / (max - min) |> TransferFunction.transferPlasma)
                                    Some colors
                                else 
                                    None
                            | _ -> 
                                None

                    let geometry = 
                        {
                            colors = colors
                            indices = h.indices |> Seq.toArray
                            vertices = h.localPositions |> Seq.map V3d  |> Seq.toArray
                        }
                    geometry
                )
            RudimentaryObjExport.writeToString objGeometries |> File.writeAllText "./annotation.obj"
            resultPoints.Value <- allVertices |> Seq.toArray


        let points =
            Sg.instancedGeometry ((view, resultPoints) ||> AVal.map2 (fun view pos -> pos |> Array.map (fun p -> Trafo3d.Translation(V3d p) * view.ViewTrafo))) (IndexedGeometryPrimitives.solidPhiThetaSphere (Sphere3d(V3d.Zero, 0.01)) 8 C4b.White)
            |> Sg.viewTrafo (AVal.constant Trafo3d.Identity)
            |> Sg.shader {
                do! DefaultSurfaces.instanceTrafo
                do! DefaultSurfaces.trafo
                do! DefaultSurfaces.vertexColor
            }

        let sg = 
            sg
            |> Sg.andAlso (createAnnotationSg win view frustum showOld (pick hit) annotations)
            |> Sg.andAlso points
            |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
            |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo)
            //|> Sg.transform preTransform
            |> Sg.effect [
                    //DefaultSurfaces.trafo |> toEffect
                    Shader.stableTrafo |> toEffect
                    DefaultSurfaces.constantColor C4f.White |> toEffect
                    DefaultSurfaces.diffuseTexture |> toEffect
                    Shader.LoDColor |> toEffect
                ]
            |> Sg.uniform "LodVisEnabled" lodVisEnabled
            |> Sg.fillMode fillMode


        Log.startTimed "rendering first frame"
        let fbo = runtime.CreateFramebuffer(win.FramebufferSignature, AVal.constant (V2i(1024,1024))).GetValue()
        let t = runtime.CompileRender(win.FramebufferSignature, sg)
        t.Run(RenderToken.Empty, fbo)
        Log.stop()

        win.RenderTask <- t
        win.Run()
        0