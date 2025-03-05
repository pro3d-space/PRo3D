#nowarn "9"
namespace PRo3D.Core

open System
open System.Threading
open FSharp.NativeInterop
open System.IO

open MBrace.FsPickler

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open FSharp.Data.Adaptive
open Aardvark.Rendering.Text

open Aardvark.Data
open Aardvark.Data.Opc
open System.Globalization
open Aardvark.GeoSpatial.Opc
open Aardvark.GeoSpatial.Opc.Load
open Aardvark.SceneGraph.Semantics


open Aardvark.FontProvider

open PRo3D.Extensions
open PRo3D.Extensions.FSharp
open PRo3D.SPICE

open PRo3D.Core.ProjectedImages

[<Struct>]
type RelState = 
    {
        pos : V3d
        vel : V3d
        rot : M33d
    }

module Time =

    let toUtcFormat (d : DateTime) = 
        d.ToUniversalTime()
         .ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'");

type Font = GoogleFontProvider<"Roboto Mono">
          
type CameraMode =
    | FreeFly
    | Orbit

module TestViewer = 
    let run (scene : OpcScene) (additionalHierarchies : seq<string>)  = 
        Aardvark.Init()

        use app = new OpenGlApplication()
        use win = app.CreateGameWindow(8)

        use _ = 
            let logPath = Path.Combine(".", "logs", "CooTrafo.Log")
            Log.line "log path for coo trafo: %s" logPath
            let r = CooTransformation.Init(true, logPath, 0, 0)
            if r <> 0 then failwith "could not initialize CooTransformation lib."
            { new IDisposable with member x.Dispose() = CooTransformation.DeInit() }


        let spiceFileName = @"C:\Users\haral\Desktop\pro3d\spice\kernels\mk\hera_plan.tm"
        System.Environment.CurrentDirectory <- Path.GetDirectoryName(spiceFileName)
        let r = CooTransformation.AddSpiceKernel(spiceFileName)
        if r <> 0 then failwith "could not add spice kernel"


        let observer = cval "MARS" //"HERA_AFC-1" 
        let supportBody = cval "SUN"
        let referenceFrame = cval "IAU_MARS" 
        let referenceFrame = cval "ECLIPJ2000"
        let time = 
            let startTime = "2025-03-12 11:50:00.000Z"
            cval (DateTime.Parse startTime)


        let hera = 
            match CooTransformation.getRelState "HERA" "SUN" observer.Value time.Value referenceFrame.Value  with
            | Some s -> s.pos
            | None -> failwith "could not get initial position"


        let marsToRefFrame (body : string) (observer : string) (referenceFrame : string) (sourceFrame : string) = 
            match CooTransformation.getRelState body "SUN" observer time.Value referenceFrame, CooTransformation.getRotationTrafo sourceFrame referenceFrame time.Value with
            | Some rel, Some rot -> 
                rot * Trafo3d.Translation(rel.pos) |> Some
            | _ -> 
                None

        let marsToReferenceFrame = marsToRefFrame "MARS" observer.Value referenceFrame.Value "IAU_MARS" |> Option.get

        //let initialView = CameraView.lookAt targetState.pos V3d.Zero V3d.OOI |> cval
        let bb =  Box3d.Parse("[[701677.203042967, 3141128.733093360, 1075935.257765322], [701942.935458576, 3141252.724183598, 1076182.681085336]]").Transformed(marsToReferenceFrame)
        let initialView = CameraView.lookAt bb.Max bb.Center bb.Center.Normalized |> cval
        let initialView = CameraView.lookAt -hera bb.Center V3d.OOI |> cval
        let speed = 7900.0 * 100.0 |> cval
        let cameraMode = cval CameraMode.Orbit

        let view = 
            adaptive {
                let! mode = cameraMode
                let! initialView = initialView
                match mode with
                | CameraMode.FreeFly -> 
                    let! currentSpeed = speed
                    return! 
                        DefaultCameraController.controlExt (float currentSpeed) win.Mouse win.Keyboard win.Time initialView
                | CameraMode.Orbit -> 
                    return!
                        AVal.integrate 
                            initialView win.Time [
                                DefaultCameraController.controlZoomWithSpeed speed win.Mouse
                                DefaultCameraController.controllScrollWithSpeed speed win.Mouse win.Time
                                DefaultCameraController.controlOrbitAround win.Mouse (AVal.constant <| V3d.Zero)
                            ]
            }


        let distanceSunPluto = 5906380000.0 * 1000.0
        let farPlaneMars = 30101626.50 * 1000.0
        let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 100.0 farPlaneMars (float s.X / float s.Y))
        //let frustumMars = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 1000.0 farPlaneMars (float s.X / float s.Y))
        let aspect = win.Sizes |> AVal.map (fun s -> float s.X / float s.Y)


        let opcSurfaces = ["mars"] //["mars"]


        let getRenderingParameters (b : string) : Rendering.BodyRenderingParameters = 
            let b = CelestialBodies.getBodySource b |> Option.get
            {
                name = b.name
                diameter = b.diameter
                color = b.color
                diffuseMap = b.diffuseMap
                normalMap = b.normalMap
                specularMap = b.specularMap
                referenceFrame = b.referenceFrame
                visible = List.contains b.name opcSurfaces |> not |> AVal.constant
            }

        let instruments =
            let frustum = Frustum.perspective 5.5306897076421 10000000.0 distanceSunPluto 1.0
            Map.ofList [
                "HERA_AFC-1", frustum
                "HERA_AFC-2", frustum
            ]

        let getLookAt (viewerBody : string) (observer : string) (referenceFrame : string) (supportBody : string) (time : DateTime) =
            let afc1Pos = CooTransformation.getRelState viewerBody supportBody observer time referenceFrame
            match afc1Pos with    
            | Some targetState -> 
                let rot = targetState.rot
                let t = Trafo3d.FromBasis(rot.C0, rot.C1, rot.C2, targetState.pos)
                //CameraView.lookAt targetState.pos V3d.Zero V3d.OOI |> Some
                CameraView.ofTrafo t.Inverse |> Some 
            | _ -> 
                None


        let startTime = DateTime.Parse("2025-03-12 11:30:20.482190Z", CultureInfo.InvariantCulture)
        let endTime = DateTime.Parse("2025-03-12 15:20:20.482190Z", CultureInfo.InvariantCulture)
        let shots = (endTime - startTime) / TimeSpan.FromMinutes(2) |> ceil |> int
        let observationTimes = ProjectedImages.splitTimes startTime endTime shots

        let allObservationsFor (viewerBody : string)  (bodyReferenceFrame : string) (targetRefSystem : string) = 
            let shots = (endTime - startTime) / TimeSpan.FromMinutes(2) |> ceil |> int
            let interval = (endTime - startTime) / float shots
            let snapshots = [ 0 .. shots ] |> List.map (fun i -> startTime + interval * float i) |> List.toArray
            time |> AVal.map (fun _ -> 
                snapshots 
                |> Array.choose (fun time -> 
                    match getLookAt viewerBody observer.Value targetRefSystem "SUN" time,  CooTransformation.getRotationTrafo bodyReferenceFrame targetRefSystem time  with
                    | Some camInMarsSpace, Some t -> 
                        let frustum = instruments["HERA_AFC-1"]
                        let forward = (t * CameraView.viewTrafo camInMarsSpace * Frustum.projTrafo frustum)
                        (time, forward) |> Some

                    | _ -> 
                        None
                )
            )

        let marsObservations = allObservationsFor "HERA" "IAU_MARS" referenceFrame.Value
        let marsObservations = 
            let p = {
                worldReferenceSystem = referenceFrame.Value
                observer = observer.Value
                sourceReferenceFrame = "IAU_MARS"
                target = CameraFocus.FocusBody "MARS"
                cameraSource = CameraSource.InBody "HERA"
                instrument = Intrinsics.Plain instruments["HERA_AFC-1"]
                supportBody = "SUN"
            }
            let observations = observationTimes |> ProjectedImages.computeProjections p
            Array.zip observationTimes observations

        let firstProjection = marsObservations |> (snd << Array.head) |> AVal.constant
        let prio = RenderPass.after "priority" RenderPassOrder.Arbitrary RenderPass.main 

        let hierarchies = 
            let runner = win.Runtime.CreateLoadRunner 1
            let serializer = FsPickler.CreateBinarySerializer()

            let createSg (sunLightEnabled : aval<bool>) (body : string) (bodyFrame : string) (hierarchies : seq<string>) =

                let bodyPos = Rendering.getPosition referenceFrame supportBody body observer time
                let sunLightDirection = 
                    let sunPos = Rendering.getPosition referenceFrame (AVal.constant "EARTH") "Sun" observer time
                    (sunPos, bodyPos)
                    ||> AVal.map2 (fun sunPos bodyPos -> 
                        match sunPos, bodyPos with
                        | Some sunPos, Some bodyPos -> sunPos - bodyPos |> Vec.normalize
                        | _ -> V3d.Zero
                    )

                let allObservations = allObservationsFor "HERA" bodyFrame referenceFrame.Value

                hierarchies
                |> Seq.toList 
                |> List.map (fun basePath -> 
                    let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                    let t = PatchLod.toRoseTree h.tree

                    let context (n : Aardvark.GeoSpatial.Opc.PatchLod.PatchNode) (s : Ag.Scope) =
                        let v = s.ViewTrafo
                        let m = s.ModelTrafo
                        (m, v)  :> obj

                    let map = 
                        Map.ofList [
                            "ProjectedImagesLocalTrafos", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                                let (modelTrafo,_) = scope |> unbox<aval<Trafo3d> * aval<Trafo3d>>
                                (allObservations, modelTrafo)
                                ||> AVal.map2 (fun arr modelTrafo -> 
                                    arr
                                    |> Array.map (fun (_, vp : Trafo3d) -> 
                                        // first to body space, then through projection
                                        vp.Forward * patch.info.Local2Global.Forward  |> M44f.op_Explicit
                                    )
                                ) :> IAdaptiveValue
                            )
                            "ProjectedImageModelViewProjValid", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                                true |> AVal.constant :> IAdaptiveValue
                             )
                            "ProjectedImageModelViewProj", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                                let (m,_) = scope |> unbox<aval<Trafo3d> * aval<Trafo3d>>
                                (firstProjection, m) ||> AVal.map2 (fun vp m -> 
                                     vp.Forward * patch.info.Local2Global.Forward
                                ) :> IAdaptiveValue
                            )
                            "SunDirectionWorld", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                                sunLightDirection :> IAdaptiveValue
                            )
                            "SunLightEnabled", fun _ _ -> sunLightEnabled :> IAdaptiveValue
                            "ApproximateBodyNormalLocalSpace", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) ->
                                patch.info.Local2Global.Backward.TransformDir(patch.info.GlobalBoundingBox.Center.Normalized).Normalized |> AVal.constant :> IAdaptiveValue
                            )
                        ]

                    let n =
                        Aardvark.GeoSpatial.Opc.PatchLod.PatchNode(
                                  win.FramebufferSignature, runner, basePath, scene.lodDecider, true, true, ViewerModality.XYZ, 
                                  PatchLod.CoordinatesMapping.Local, true, context, map,
                                  t,
                                  None, None, PixImagePfim.Loader
                        )

                    n
                ) 
 
            let mola = 
                createSg (AVal.constant true) "MARS" "IAU_MARS" scene.patchHierarchies 
                |> Sg.ofSeq 

            let hirise = 
                createSg (AVal.constant false) "MARS" "IAU_MARS" additionalHierarchies 
                |> Sg.ofSeq 
                |> Sg.depthTest' DepthTest.None 
                |> Sg.pass prio

            Sg.ofList [
                mola; 
                hirise
            ]
            
        let timeClampedCount =
            time |> AVal.map (fun currentTime  -> 
                match marsObservations |> Array.tryFindIndex (fun (observationTime,_) -> observationTime > currentTime) with
                | Some i -> 
                    printfn "i: %A" i
                    i
                | _ -> 
                    marsObservations.Length
            )

        let count = cval 0
        let projectionCount = timeClampedCount 


        let bodies = CelestialBodies.bodySources |> Array.map (fun b -> b.name, b) |> AMap.ofArray
        let wrapModel (planet : string) (sg : ISg) =
            let p = {
                worldReferenceSystem = referenceFrame.Value
                observer = observer.Value
                sourceReferenceFrame = "IAU_MARS"
                target = CameraFocus.FocusBody "MARS"
                cameraSource = CameraSource.InBody "HERA"
                instrument = Intrinsics.Plain instruments["HERA_AFC-1"]
                supportBody = "SUN"
            }
            let observations = observationTimes |> ProjectedImages.computeProjections p
            let getProjectionTrafo _ =
                observations[0] |> Some |> AVal.constant
            sg
            // projected frusta
            |> Sg.uniform "ProjectedImagesLocalTrafosCount" projectionCount 
            |> Sg.uniform' "ProjectedImagesLocalTrafos" (observations |> Array.map (Trafo.forward >> M44f.op_Explicit))
            // projected texture
            |> Sg.fileTexture "ProjectedTexture" @"C:\Users\haral\Pictures\OIP.jpg" true
            |> Sg.applyProjectedImage getProjectionTrafo 
            |> Sg.uniform' "ProjectedImageModelViewProjValid" true
            //|> Rendering.StableTrafoSceneGraphExtension.Sg.wrapStableShadowViewProjTrafo (shadowMapCamera |> AVal.map Camera.viewProjTrafo) 


        let planets = 
            Rendering.bodiesVisualization referenceFrame supportBody (bodies |> AMap.toASetValues |> ASet.map (fun b -> b.name)) getRenderingParameters observer time wrapModel
            |> Sg.shader {
                do! Shaders.planetLocalLightingViewSpace
                do! ImageProjection.Shaders.stableImageProjectionTrafo
                do! Shaders.transformShadowVertices

                // regenerate normals using gs. the face normals from the mesh do not work (did not investigate it further).
                do! ImageProjection.Shaders.generateNormal
                //do! ImageProjection.Shaders.useVertexNormals
                do! ImageProjection.Shaders.flipNormals

                do! Shaders.genAndFlipTextureCoord // for some reason v needs to be 1- flipped. 
                do! DefaultSurfaces.sgColor

                do! Shaders.stableTrafo
                do! DefaultSurfaces.diffuseTexture

                //do! Shaders.shadow
                //do! Rendering.Shaders.shadowPCF
                do! Shaders.solarLightingWithSpecular
                do! ImageProjection.Shaders.stableImageProjection
                do! ImageProjection.Shaders.localImageProjections
            }

        let trajectories = 
            let getTrajectoryProperties (bodyName : string) =
                observer |> AVal.map (fun observer -> 
                    let body = CelestialBodies.getOrbitLength bodyName
                    let observer = CelestialBodies.getOrbitLength observer
                    TimeSpan.FromDays(1), 200
                )

            let color body = 
                bodies |> AMap.tryFind body |> AVal.map (function
                    | None ->  C4b.White
                    | Some c -> C4b c.color
                )

            Rendering.trajectoryVisualization referenceFrame observer time getTrajectoryProperties (constF (AVal.constant true)) color (bodies |> AMap.toASetValues |> ASet.map (fun b -> b.name))
       


        let marsTrafo = cval Trafo3d.Identity
        let transformation = 
            Rendering.fullTrafo referenceFrame supportBody "MARS" (Some "IAU_MARS") observer time
            |> AVal.map (fun trafo -> 
                match trafo with
                | Some trafo -> trafo, true
                | _ -> 
                    Log.warn "could not get trafo for body %s" "MARS"
                    Trafo3d.Identity, false
            )

        let mutable percentage = 0.0

        win.Keyboard.DownWithRepeats.Values.Add(fun k ->
            percentage <-
                match k with
                | Keys.Down -> clamp 0.0 1.0 (percentage - 0.05)
                | Keys.Up -> clamp 0.0 1.0 (percentage + 0.05)
                | _ -> percentage

            let c = marsObservations.Length
            let v = float c * percentage |> int
            transact (fun _ -> 
                count.Value <- clamp 0 c v
                printfn "%A" count.Value
            )
                
        )



        let marsSg =
            hierarchies
            |> Sg.shader {
                do! Shaders.planetLocalLightingViewSpace
                do! ImageProjection.Shaders.stableImageProjectionTrafo
                do! ImageProjection.Shaders.generateNormal
                do! Shaders.stableTrafo
                do! DefaultSurfaces.constantColor C4f.White 
                do! DefaultSurfaces.diffuseTexture 
                do! Shaders.solarLighting
                do! ImageProjection.Shaders.localImageProjections
                do! ImageProjection.Shaders.stableImageProjection
                //do! Shader.LoDColor 
            }
            |> Sg.uniform "LodVisEnabled" (cval false)
            |> Sg.uniform "ProjectedImagesLocalTrafosCount" projectionCount //count
            |> Sg.fileTexture "ProjectedTexture" @"C:\Users\haral\Pictures\OIP.jpg" true
            |> Sg.trafo (transformation |> AVal.map fst)
            |> Sg.onOff (transformation |> AVal.map snd)


        let viewProj = (view, frustum) ||> AVal.map2 (fun view frustum -> (view |> CameraView.viewTrafo) * (frustum |> Frustum.projTrafo))
        let font = Font.Font
        let aspectScaling = aspect |> AVal.map (fun aspect -> Trafo3d.Scale(V3d(1.0, aspect, 1.0)))
        let inNdcBox =
            let box = Box3d.FromPoints(V3d(-1,-1,-1),V3d(1,1,1))
            fun (p : V3d) -> box.Contains p

        let markers = 
            MarsTaggedLocations.taggedLocations 
            |> List.choose (fun (name, (lat,lon)) -> 
                match CooTransformation.latLon2Xyz "MARS" (lat, lon, 0.0), CooTransformation.latLon2Xyz "MARS" (lat, lon, 500000.0) with
                | Some p0, Some p1 -> 
                    let text =
                        let contents = 
                            Array.ofList [
                                let p = 
                                    AVal.custom (fun t -> 
                                        let referenceFrame = referenceFrame.GetValue(t)
                                        let time = time.GetValue(t)
                                        let marsToGlobal = CooTransformation.getRotationTrafo "IAU_MARS" referenceFrame time |> Option.get
                                        let bodyPos = marsToGlobal.TransformPos(p1) |> Some
                                        let vp = viewProj.GetValue t
                                        let scale = aspectScaling.GetValue t
                                        match bodyPos with
                                        | None -> Trafo3d.Scale(0.0)
                                        | Some p ->
                                            let ndc = vp.Forward.TransformPosProj (p) 
                                            let scale = if inNdcBox ndc then scale else Trafo3d.Scale(0.0)
                                            Trafo3d.Scale(0.03) * scale * Trafo3d.Translation(ndc.XYZ)
                                    )
                                p, AVal.constant name
                            ]
                        Sg.texts font C4b.White (ASet.ofArray contents)

                    let global2Local = Trafo3d.Translation(p0)
                    let line = 
                        [|
                            Line3d(p0, p1).Transformed(global2Local.Backward)
                        |]
                    let line = 
                        Sg.lines' C4b.White line
                        |> Sg.trafo' global2Local
                        |> Sg.trafo (transformation |> AVal.map fst)
                        |> Sg.pass prio
                        //|> Sg.depthTest' DepthTest.None
            
                    let sg = 
                        line
                        |> Sg.shader {
                            do! DefaultSurfaces.stableTrafo
                            do! DefaultSurfaces.thickLine
                        }
                        |> Sg.uniform' "LineWidth" 1.5
                        |> Sg.uniform' "PointSize" 8.0
                    
                    Some (sg, text)

                | _ -> None
            )

        

        let sg =
            Sg.ofList [ planets; marsSg; trajectories; (List.map fst markers |> Sg.ofList) ] 
            |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
            |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo)


        let bodyLabels = 
            bodies.Content
            |> AVal.map (fun bodies -> 
                let contents = 
                    bodies |> HashMap.toValueArray |> Array.map (fun bodyDesc -> 
                        let bodyPos = Rendering.getPosition referenceFrame supportBody bodyDesc.name observer time
                        let p = 
                            AVal.custom (fun t -> 
                                let p = bodyPos.GetValue t
                                let vp = viewProj.GetValue t
                                let scale = aspectScaling.GetValue t
                                let observer = observer.GetValue()
                                match p with
                                | None -> Trafo3d.Scale(0.0)
                                | Some p ->
                                    let ndc = vp.Forward.TransformPosProj (p) 
                                    let scale = if inNdcBox ndc && bodyDesc.name <> observer then scale else Trafo3d.Scale(0.0)
                                    Trafo3d.Scale(0.05) * scale * Trafo3d.Translation(ndc.XYZ)
                            )
                        p, AVal.constant bodyDesc.name
                    )
                Sg.texts font C4b.White (ASet.ofArray contents)
             )
             |> Sg.dynamic

        let info = 
            let content = time |> AVal.map (fun t -> sprintf "%s" (CooTransformation.Time.toUtcFormat t))
            Sg.text font C4b.Gray content 
            |> Sg.trafo (aspectScaling |> AVal.map (fun s -> Trafo3d.Scale(0.1) * s * Trafo3d.Translation(-0.95,-0.95,0.0)))
     
        let help = 
            let content =
                observer |> AVal.map (fun o -> 
                    String.concat Environment.NewLine [
                        "<c>    : switch camera mode"
                        "<t>    : reset time"
                        $"<n>    : switch observer ({o})."
                    ]
                )
            Sg.text font C4b.Gray content
            |> Sg.trafo (aspectScaling |> AVal.map (fun s -> Trafo3d.Scale(0.02) * s * Trafo3d.Translation(-0.95, 0.90,0.0)))


        let sg = Sg.ofList [sg; bodyLabels; info; help; (markers |> List.map snd |> Sg.ofList)]

        let mutable paused = true
        let s = 
            let sw = Diagnostics.Stopwatch.StartNew()
            let mutable lastFrame = None
            win.AfterRender.Add(fun _ -> 
                transact (fun _ -> 

                    let dt = 
                        match lastFrame with
                        | None -> TimeSpan.Zero
                        | Some l -> sw.Elapsed - l
                    if not paused then
                        time.Value <- time.Value + dt * 160.0
                    //let frustum = instruments.["HERA_AFC-1"]
                    //let view = (getLookAt "HERA"  referenceFrame.Value time.Value).Value
                    //customObservationCamera.Value <- Some (Camera.create view frustum)
                    //animationStep()
                    lastFrame <- Some sw.Elapsed
                )
            )


        win.Keyboard.KeyDown(Keys.C).Values.Add(fun _ -> 
            transact (fun _ -> 
                cameraMode.Value <-
                    match cameraMode.Value with
                    | CameraMode.FreeFly -> CameraMode.Orbit
                    | CameraMode.Orbit -> CameraMode.FreeFly
            )
        )
        win.Keyboard.KeyDown(Keys.Space).Values.Add(fun _ -> 
            paused <- not paused
        )

        let task =
            app.Runtime.CompileRender(win.FramebufferSignature, sg)

        win.RenderTask <- task
        win.Run()
        0