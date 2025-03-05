namespace PRo3D.SPICE

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.SceneGraph

open Aardvark.Rendering
open Aardvark.Geometry

open PRo3D.Extensions
open PRo3D.Extensions.FSharp

open PRo3D.Base
open PRo3D.Core

open PRo3D.SPICE

module Rendering = 

    type BodyRenderingParameters = {
        name : string
        diameter : float<km>
        color : C4f
        diffuseMap : Option<string>
        normalMap : Option<string>
        specularMap : Option<string>
        referenceFrame : Option<string>
        visible : aval<bool>
    }

    [<AutoOpen>]
    module StableTrafoSceneGraphExtension =
        open Aardvark.Base.Ag
        open Aardvark.SceneGraph.Semantics.TrafoExtensions
        
        type StableViewProjTrafo(child : ISg, shadowViewProjTrafo : aval<Trafo3d>) =
            inherit Sg.AbstractApplicator(child)
            member x.ShadowViewProjTrafo = shadowViewProjTrafo

        [<Rule>]
        type StableTrafoSemantics() =
            member x.StableModelViewProjTexture(app : StableViewProjTrafo, scope : Ag.Scope) =
                let trafo = 
                    (scope.ModelTrafo, app.ShadowViewProjTrafo) ||> AVal.map2 (*)
                app.Child?StableModelViewProjTexture <- trafo

        module Sg = 
            let wrapStableShadowViewProjTrafo (trafo : aval<Trafo3d>) (sg : ISg) = 
                StableViewProjTrafo(sg, trafo) :> ISg


    let getRelState (referenceFrame : aval<string>) (supportBody : aval<string>) (body : string) (observer : aval<string>) (time : aval<DateTime>) =
        AVal.custom (fun t -> 
            let observer = observer.GetValue(t)
            let time = time.GetValue(t)
            let referenceFrame = referenceFrame.GetValue(t)
            let supportBody = supportBody.GetValue(t)
            CooTransformation.getRelState body supportBody observer time referenceFrame
        )

    let getPosition (referenceFrame : aval<string>) (supportBody : aval<string>) (body : string) (observer : aval<string>) (time : aval<DateTime>) = 
        let getPos (o : CooTransformation.RelState) = o.pos
        getRelState referenceFrame supportBody body observer time |> AVal.map (Option.map getPos)

    let fullTrafo  (referenceFrame : aval<string>) (supportBody : aval<string>) (body : string) (bodyFrame : Option<string>) (observer : aval<string>) (time : aval<DateTime>) =
        let rotation = 
            match bodyFrame with
            | None -> AVal.constant None
            | Some frame -> 
                (referenceFrame, time) ||> AVal.map2 (fun referenceFrame time -> CooTransformation.getRotationTrafo frame referenceFrame time)
        let pos = getRelState referenceFrame supportBody body observer time
        (rotation, pos) ||> AVal.map2 (fun rot relState -> 
            match rot, relState with
            | Some rot, Some relState -> 
                Some (rot * Trafo3d.Translation relState.pos)
                //Some (Trafo3d.Translation relState.pos)
            | None, Some relState -> relState.pos |> Trafo3d.Translation |> Some
            | _ -> 
                None
        )

    let bodiesVisualization (referenceFrame : aval<string>) (supportBody : aval<string>) 
                            (bodies : aset<string>) (bodyRenderingParameters : string -> BodyRenderingParameters) (observer : aval<string>) 
                            (time : aval<DateTime>) (wrapModel : string -> ISg -> ISg) =

        let sphericalUnitBody (scale : float) = 
            PolyMeshPrimitives.Sphere(30, 1.0, C4b.White, DefaultSemantic.DiffuseColorCoordinates, DefaultSemantic.DiffuseColorUTangents, DefaultSemantic.DiffuseColorVTangents)
                                .Transformed(Trafo3d.Scale scale)
                                .GetIndexedGeometry()

            |> Sg.ofIndexedGeometry


        let fallbackTexture = 
            let whitePix =
                let pi = PixImage<byte>(Col.Format.RGBA, V2i.II)
                pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
                pi
            PixTexture2d(PixImageMipMap(whitePix)) :> ITexture


        let bodySgs = 
            bodies 
            |> ASet.map (fun bodyName -> 
                let renderingParameters = bodyRenderingParameters bodyName
                let bodyPos = getPosition referenceFrame supportBody bodyName observer time
                let transformation = 
                    fullTrafo referenceFrame supportBody bodyName renderingParameters.referenceFrame observer time
                    |> AVal.map (fun trafo -> 
                        match trafo with
                        | Some trafo -> trafo, true
                        | _ -> 
                            Log.warn "could not get trafo for body %s" bodyName
                            Trafo3d.Identity, false
                    )
                let sunDirection = 
                    let sunPos = getPosition referenceFrame (AVal.constant "EARTH") "Sun" observer time
                    (sunPos, bodyPos)
                    ||> AVal.map2 (fun sunPos bodyPos -> 
                        match sunPos, bodyPos with
                        | Some sunPos, Some bodyPos -> sunPos - bodyPos |> Vec.normalize
                        | _ -> V3d.Zero
                    )
                let isObserver = observer |> AVal.map (fun o -> o = bodyName)
                let createTexture (filePath : string) =
                    PixTexture2d(filePath |> PixImageMipMap.Load) :> ITexture
                let radius = renderingParameters.diameter |> kmToMeters |> float 
                let model =  sphericalUnitBody (radius * 0.5) // inefficient, but for a reason, see below for sg.scale
                model
                |> wrapModel bodyName
                |> Sg.onOff renderingParameters.visible
                //|> Sg.scale radius (don't use model trafo for scaling here to make transformations less complex, trust me)
                |> Sg.trafo (AVal.map fst transformation)
                |> Sg.onOff (AVal.map snd transformation)
                |> Sg.applyPlanet bodyName
                |> Sg.uniform "SunDirectionWorld" sunDirection
                |> Sg.uniform' "Color" renderingParameters.color
                |> Sg.texture' DefaultSemantic.DiffuseColorTexture (renderingParameters.diffuseMap |> Option.map createTexture |> Option.defaultValue fallbackTexture)
                |> Sg.uniform' "HasDiffuseColorTexture" (renderingParameters.diffuseMap |> Option.isSome)
                |> Sg.texture' DefaultSemantic.NormalMapTexture (renderingParameters.normalMap |> Option.map createTexture |> Option.defaultValue fallbackTexture)
                |> Sg.uniform' "HasNormalMap" (renderingParameters.diffuseMap |> Option.isSome)
                |> Sg.texture' DefaultSemantic.SpecularColorTexture (renderingParameters.specularMap |> Option.map createTexture |> Option.defaultValue fallbackTexture)
                |> Sg.uniform' "HasSpecularColorTexture" (renderingParameters.diffuseMap |> Option.isSome)
            )
            |> Sg.set


        bodySgs
        |> Sg.cullMode' CullMode.Back


    let renderShadowMap (runtime : IRuntime) (referenceFrame : aval<string>) (supportBody : aval<string>) (scene : ISg) 
                        (observer : aval<string>) (time : aval<DateTime>) = 
        
        let sunToObserverCameraView = 
            //let shadowCaster = "MOON"
            let shadowCaster = "SUN"
            getRelState referenceFrame (AVal.constant "VENUS") shadowCaster observer time |> AVal.map (function 
                | None -> None
                | Some relState -> 
                    let pos = relState.pos
                    //let pos = V3d(-3364116.4273718265, -15969149.3536416, 8157900.047172555)
                    CameraView.lookAt pos V3d.Zero V3d.OOI |> Some
            )
            

        let shadowMapValid = sunToObserverCameraView |> AVal.map (function None -> printfn "WJS"; false | Some _ -> true)
        let lightCameraView = sunToObserverCameraView |> AVal.map (Option.defaultValue (CameraView.lookAt V3d.OOO V3d.OOO V3d.OOI))
        let lightView = lightCameraView |> AVal.map CameraView.viewTrafo
        let lightFrustum = 
            lightCameraView |> AVal.map (fun v -> 
                let loc = CameraView.location v
                let fovEarth = 0.05
                let nearEarth = (loc.Length * 0.5)

                let fovMars = 0.004
                let nearMars = (loc.Length * 0.99)
                let farMars = (loc.Length * 1.01)
                Frustum.perspective fovEarth nearMars farMars 1.0
            )
        let lightProj = lightFrustum |> AVal.map  Frustum.projTrafo
        let shadowCamera = (lightCameraView, lightFrustum) ||> AVal.map2 Camera.create
        let shadowMapSize = V2i(8192, 8192) |> AVal.constant

        let signature = 
           runtime.CreateFramebufferSignature [
               DefaultSemantic.Colors, TextureFormat.Rgba8
               DefaultSemantic.DepthStencil, TextureFormat.DepthComponent32f
           ]

        let clearValues =
            clear { 
                depth 1.0
                color C4f.Black 
            }

        let color, shadowMap = 
            scene
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
                do! DefaultSurfaces.sgColor
            }
            |> Sg.onOff shadowMapValid
            |> Sg.viewTrafo lightView
            |> Sg.projTrafo lightProj
            |> Sg.compile runtime signature
            |> RenderTask.renderToColorAndDepthWithClear shadowMapSize clearValues

        shadowMap, color, shadowMapValid, shadowCamera


    let getTimeSteps (samples : int) (trajectoryDurationInPast : TimeSpan) (currentTime : DateTime) =
        let trajectoryDuration = trajectoryDurationInPast
        let startTime = currentTime - trajectoryDuration
        Array.init samples (fun i -> 
            let time = currentTime - ((currentTime - startTime) / float samples) * float i
            let alpha = float i / float samples
            struct (time, alpha)
        ) 

    let getTimeStepsPlain (samples : int) (trajectoryDurationInPast : TimeSpan) (currentTime : DateTime) =
        let sampleLength = trajectoryDurationInPast / float samples
        List.init samples (fun i -> 
            let time = currentTime - sampleLength * float i
            let alpha = float i / float samples
            struct (time, alpha)
        ) 


    let trajectoryVisualization (referenceFrame : aval<string>) (observer : aval<string>) (time : aval<DateTime>) 
                                (getTrajectorySamples : string -> aval<TimeSpan * int>) 
                                (showTrajectory : string -> aval<bool>)
                                (trajectoryColor : string -> aval<C4b>) (bodies : aset<string>) =
 

        let trajectories =
            bodies
            |> ASet.mapA (fun name -> 
                adaptive {
                    let! referenceFrame = referenceFrame
                    let! show = showTrajectory name
                    if show then
                        let! trajectoryLength, trajectorySamples = getTrajectorySamples(name)

                        // this one only works properly when getTimeStemps provides temporal coherence (which is currently not the case)
                        let cache = LruCache(int64 (trajectorySamples))
                       
                        let! observer = observer

                        let mutable hits = 0
                        let getPosition (time : DateTime) = 
                            let mutable wasHit = true
                            let r = 
                                cache.GetOrAdd(time, 1, fun _ -> 
                                    wasHit <- false
                                    CooTransformation.getRelState name defaultSupportBodyWhenIrrelevant observer time referenceFrame
                                )
                            if wasHit then 
                                hits <- hits + 1
                            r

                        let mutable oldTime = None
                        let mutable oldTimes = [||]
                        let! time = time
                        let times = 
                            let opt = true
                            // very ugly hack to allow smooth anomation and vary dynamic trajectory setting at the same time (it could be refactored as an alist acutally)
                            let lessThanOneSampleAway (ts : TimeSpan) = ts < (trajectoryLength / float trajectorySamples)
                            let lessThanTwoAway (ts : TimeSpan) = ts < (trajectoryLength / float trajectorySamples) * 2.0
                            match oldTime with
                            | Some lastTime when lessThanOneSampleAway (time - lastTime) && oldTimes.Length >= trajectorySamples  && opt -> 
                                oldTimes[0] <- struct (time, 1.0)
                                oldTimes
                            | Some lastTime when lessThanTwoAway (time - lastTime) && oldTimes.Length >= trajectorySamples && opt  ->
                                oldTime <- Some time
                                Array.append [|struct (time, 1.0)|] oldTimes[0 .. oldTimes.Length - 2]
                            | _ -> 
                                oldTime <- Some time
                                getTimeStepsPlain trajectorySamples trajectoryLength time |> List.toArray
                                

                        hits <- 0
                        let r = 
                            times 
                            |> Seq.toArray
                            |> Array.choose (fun (struct (t, alpha)) -> 
                                getPosition t
                            )
                            |> Array.pairwise
                            |> Array.map (fun (p0, p1) -> 
                                Line3d(p0.pos, p1.pos)
                            )
                            |> Seq.toArray

                        oldTimes <- times |> Seq.toArray
                        //printfn "hits %s: %d/%d" b.name hits  times.Length

                        return r, trajectoryColor name
                    else 
                        return [||], trajectoryColor name
                }
            ) 

        let offset = 
            trajectories.Content
            |> AVal.map (fun trajectories ->
                match Seq.tryHead trajectories with
                | Some (t,_) -> 
                    match Array.tryHead t with
                    | Some l -> Trafo3d.Translation l.P0
                    | _ -> Trafo3d.Identity
                | _ -> Trafo3d.Identity
             )

        let trajectoryVisualizations = 
            trajectories
            |> ASet.map (fun (trajectory, c) ->
                let lines = 
                    offset |> AVal.map (fun offset -> 
                        trajectory |> Array.map (fun l -> l.Transformed(offset.Backward))
                    )
                Sg.lines c lines
                |> Sg.trafo offset
            )
            |> Sg.set
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
            }

        trajectoryVisualizations