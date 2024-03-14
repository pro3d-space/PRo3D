module Solarsytsem

#nowarn "9"

open System
open System.Threading
open FSharp.NativeInterop
open System.IO
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open FSharp.Data.Adaptive
open JR
open Aardvark.Rendering.Text

open MBrace.FsPickler

open Aardvark.GeoSpatial.Opc
open Aardvark.SceneGraph.Opc
open Aardvark.Opc

open Aardvark.SceneGraph.Semantics
open Aardvark.SceneGraph.Semantics.TrafoSemantics

open PRo3D.Extensions

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

module Spice = 

    let getRelState (referenceFrame : string) (target : string) (observer : string) (obsTime : string)   = 
        let p : double[] = Array.zeroCreate 3
        let m : double[] = Array.zeroCreate 9
        let pdPosVec = fixed &p[0]
        let pdRotMat = fixed &m[0]
        let r = CooTransformation.GetRelState(target, "SUN", observer, obsTime, referenceFrame, NativePtr.toNativeInt pdPosVec, NativePtr.toNativeInt pdRotMat)
        if r <> 0 then 
            Log.warn "[spice] GetRelState failed."
            None
        else 
            Some { pos = V3d(p[0],p[1],p[2]); vel = V3d.Zero; rot = M33d(m).Transposed}


module Shaders =

    open FShade
    open Aardvark.Rendering.Effects

    type Vertex = 
        {
            [<Position>] p: V3d
            [<PointSize>] s : float
            [<Color>] c: V4d
            [<PointCoord>] tc: V2d
            [<Semantic("Size")>] ndcSize : float
        }


    let splatPoint (v : Vertex) = 
        vertex {
            return { v with s = v.ndcSize }
        }

    let round (v : Vertex) =
        fragment {
            let c = v.tc * 2.0 - V2d.II
            let p = V3d(c, 1.0 - Vec.length c.XY)
            let f = Vec.dot c c - 1.0
            if f > 0.0 then discard()
            return { v with c = v.c * Vec.dot V3d.III p }
        }

    type UniformScope with
        member x.SunPosViewSpace : V3d = uniform?SunPosViewSpace
        member x.NormalToViewSpace : M33d = uniform?NormalToViewSpace
        member x.CenterLocalSpace : V3d = uniform?CenterLocalSpace


    let stableTrafo (v : Effects.Vertex) =
        vertex {
            let vp = uniform.ModelViewTrafo * v.pos
            return {
                v with 
                    pos = uniform.ProjTrafo * vp
            }
        }

    type LightingVertex = 
        {
            [<Position>] pos : V4d
            [<Color>] c: V4d
            [<Normal>] vn : V3d
            [<Semantic("ViewPos")>] vp : V3d
            [<Depth>] d : float
        }

    let normalViewSpace (v : LightingVertex) =
        vertex {
            // getting numerically stable "planet" normal is not easy...
            let surfaceViewSpace = uniform.ModelViewTrafo * v.pos
            let centerViewSpace = uniform.ModelViewTrafo * V4d(uniform.CenterLocalSpace, 1.0)
            let n = (surfaceViewSpace.XYZ - centerViewSpace.XYZ).Normalized
            return { v with vn = n; vp = surfaceViewSpace.XYZ }
        }

    type UniformScope with
        member x.Near : float = uniform?Near
        member x.Far : float = uniform?Far

    [<ReflectedDefinition;Inline>]
    let linearizeDepth (d : float) =
        uniform.Near * uniform.Far / (uniform.Far + d * (uniform.Near - uniform.Far))

    let sunLight (v : LightingVertex) = 
        fragment {
            let lightDirViewSpace = (uniform.SunPosViewSpace - v.vp).Normalized
            let l = max 0.1 (Vec.dot lightDirViewSpace v.vn)
            let cMars = v.c * V4d(1.0, 0.3, 0.3, 1.0)
            // hacky depth, use proper inverse depth for astronomical depth....
            return { v with c = cMars * l; d = -((v.vp.Z + uniform.Near) / (uniform.Far - uniform.Near)) }
        }

type AdaptiveLine() =
    let data = ResizeArray()
    let arr = cval [||]
    let mutable last = None

    member x.AddPos(p : V3d) =
        last <- Some p
        data.Add(p)
        arr.Value <- data.ToArray()
        
    member x.Positions = arr :> aval<_>

    member x.Last = last
   



type BodyState = 
    {
        name : string
        pos : cval<V3d>
        ndcSize : cval<V2d>
        history : AdaptiveLine
        radius : float
    }

let run (scenes : list<OpcScene>) = 
    Aardvark.Init()

    use app = new OpenGlApplication()
    use win = app.CreateGameWindow(8)

    // unpack CooTransformation runtime library.
    //Aardvark.UnpackNativeDependencies(typeof<CooTransformation>.Assembly)

    use _ = 
        let r = CooTransformation.Init(true, Path.Combine(".", "logs", "CooTrafo.Log"), 2, 2)
        if r <> 0 then failwith "could not initialize CooTransformation lib."
        { new IDisposable with member x.Dispose() = CooTransformation.DeInit() }



    let spiceFileName = @"F:\pro3d\hera-kernels\kernels\mk\hera_crema_2_0_LPO_ECP_PDP.tm"
    System.Environment.CurrentDirectory <- Path.GetDirectoryName(spiceFileName)
    let r = CooTransformation.AddSpiceKernel(spiceFileName)
    if r <> 0 then failwith "could not add spice kernel"

    let hierarchies = 
        let runner = win.Runtime.CreateLoadRunner 1
        let serializer = FsPickler.CreateBinarySerializer()

        scenes |> List.collect (fun scene -> 
            scene.patchHierarchies 
            |> Seq.toList 
            |> List.map (fun basePath -> 
                let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                let t = PatchLod.toRoseTree h.tree

                let context (n : Aardvark.GeoSpatial.Opc.PatchLod.PatchNode) (s : Ag.Scope) =
                    let v = s.ViewTrafo
                    (v, s)  :> obj

                let map = 
                    Map.ofList [
                        "NormalToViewSpace", (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                            let viewTrafo,_ = scope |> unbox<aval<Trafo3d> * Ag.Scope>
                            let r = AVal.map2 (fun view (model : Trafo3d) -> (model * view).Backward.Transposed.UpperLeftM33()) viewTrafo patch.trafo 
                            r :> IAdaptiveValue
                        )
                        "CenterLocalSpace",  (fun scope (patch : Aardvark.GeoSpatial.Opc.PatchLod.RenderPatch) -> 
                            let globalToLocal = patch.info.Local2Global.Backward
                            let p = globalToLocal.TransformPos(V3d.OOO)
                            p |> AVal.constant :> IAdaptiveValue
                        )

                    ]

                let n =
                    Aardvark.GeoSpatial.Opc.PatchLod.PatchNode(
                              win.FramebufferSignature, runner, basePath, scene.lodDecider, true, true, ViewerModality.XYZ, 
                              PatchLod.CoordinatesMapping.Local, true, context, map,
                              t,
                              None, None, Aardvark.Base.PixImagePfim.Loader
                    )

                //Sg.patchLod win.FramebufferSignature runner basePath scene.lodDecider true true ViewerModality.XYZ PatchLod.CoordinatesMapping.Local true t
                n
            ) 
        )


    let bodySources = 
        [| "sun", C4f.White, 1392700.0
           "mercury", C4f.Gray, 12742.0
           "venus", C4f.AliceBlue, 12742.0
           "earth", C4f.Blue, 12742.0
           "moon", C4f.Beige, 34748.0
           //"mars", C4f.Red, 6779.0
           "phobos", C4f.Red, 22.533
           "deimos", C4f.Red, 12.4
           "HERA_AFC-1", C4f.Magenta, 0.001
        |]

    let time = cval (DateTime.Parse("2025-03-10 19:08:12.60"))
    let time = cval (DateTime.Parse("2025-02-11 19:08:12.60"))
    let time = cval (DateTime.Parse("2024-11-01 19:08:12.60"))
    let time = cval (DateTime.Parse("2025-03-10 19:08:12.60"))

    let observer = "MARS" //"HERA_AFC-1" // "SUN"
    let referenceFrame = "IAU_MARS" // "ECLIPJ2000"
    let observer = "HERA_AFC-1"
    let referenceFrame = "ECLIPJ2000"

    let targetState = 
        match Spice.getRelState referenceFrame "MARS" "HERA" (Time.toUtcFormat time.Value) with
        | Some s -> s
        | None -> failwith "could not get initial position"
    let initialView = CameraView.lookAt V3d.Zero targetState.pos V3d.OOI |> cval
    let speed = 7900.0 * 1000.0
    let view = initialView |> AVal.bind (DefaultCameraController.controlExt speed win.Mouse win.Keyboard win.Time)
    let distanceSunPluto = 5906380000.0 * 1000.0
    let farPlaneMars = 30101626.50 * 1000.0
    let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 1000.0 distanceSunPluto (float s.X / float s.Y))
    let frustumMars = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 1000.0 farPlaneMars (float s.X / float s.Y))
    let aspect = win.Sizes |> AVal.map (fun s -> float s.X / float s.Y)

    let projTrafo = frustum |> AVal.map Frustum.projTrafo
    let viewProj = 
        (view, projTrafo) ||> AVal.map2 (fun view projTrafo -> 
            CameraView.viewTrafo view * projTrafo
        )

    let inNdcBox =
        let box = Box3d.FromPoints(V3d(-1,-1,-1),V3d(1,1,1))
        fun (p : V3d) -> box.Contains p

    let getProjPos (t : AdaptiveToken) (clip : bool) (pos : V3d) =
        let vp = viewProj.GetValue(t)
        let ndc = vp.Forward.TransformPosProj(pos)
        let box = Box3d.FromPoints(V3d(-1,-1,-1),V3d(1,1,1))
        if not clip || inNdcBox ndc then 
            V3d ndc |> Some
        else
            None

    let getCurrentProjPos (clip : bool) (pos : V3d) = 
        getProjPos AdaptiveToken.Top clip pos

    let bodies = 
        bodySources |> Array.map (fun (name,_, diameterKm) -> 
            { name = name; pos = cval V3d.Zero; history = AdaptiveLine(); ndcSize = cval V2d.OO; radius = diameterKm * 1000.0 * 0.5 }
        )


    let animationStep () = 
        bodies |> Array.iter (fun b -> 
            let time = time.GetValue()
            match Spice.getRelState referenceFrame b.name observer (Time.toUtcFormat time) with
            | Some rel -> 
                b.pos.Value <- rel.pos

                match getCurrentProjPos false rel.pos with
                | None -> ()
                | Some p -> 
                    match b.history.Last with
                    | Some l -> 
                        if Vec.distance p l > 0.005 then 
                            b.history.AddPos rel.pos
                    | None -> 
                        b.history.AddPos rel.pos
            | None -> ()
        )

    let colors = bodySources |> Array.map (fun (_,c,_) -> c) 
    let vertices = 
        AVal.custom (fun t -> 
            let w = win.Time.GetValue(t)
            let vp = viewProj.GetValue(t)
            bodies |> Array.map (fun b -> vp.Forward.TransformPosProj b.pos.Value)
        )
    let sizes =
        AVal.custom (fun t -> 
            let p = projTrafo.GetValue(t)
            let location = view.GetValue(t)
            let s = win.Sizes.GetValue(t) |> V3d
            let computeSize (radius : float) (pos : V3d) =
                let d = V3d(radius, 0.0, Vec.length (location.Location - pos))
                let ndc = p.Forward.TransformPosProj(d)
                abs ndc.X * s
            bodies |> Array.map (fun b -> computeSize b.radius b.pos.Value)
        )

    let scale = aspect |> AVal.map (fun aspect -> Trafo3d.Scale(V3d(1.0, aspect, 1.0)))

    let texts = 
        let contents = 
            bodies |> Array.map (fun  b -> 
                let p = 
                    (b.pos, viewProj, scale) |||> AVal.map3 (fun p vp scale -> 
                        let ndc = vp.Forward.TransformPosProj b.pos.Value
                        let scale = if inNdcBox ndc then scale else Trafo3d.Scale(0.0)
                        Trafo3d.Scale(0.02) * scale * Trafo3d.Translation(ndc.XYO)
                    )
                p, AVal.constant b.name
            )
        Sg.texts Font.Symbola C4b.White (ASet.ofArray contents)


    let planets = 
        Sg.draw IndexedGeometryMode.PointList
        |> Sg.vertexAttribute' DefaultSemantic.Colors colors
        |> Sg.vertexAttribute  DefaultSemantic.Positions vertices
        |> Sg.vertexAttribute "Size" sizes
        |> Sg.shader {
            do! Shaders.splatPoint
            do! Shaders.round
        }

    let info = 
        let content = time |> AVal.map (fun t -> sprintf "%s" (Time.toUtcFormat t))
        Sg.text Font.Symbola C4b.Gray content 
        |> Sg.trafo (scale |> AVal.map (fun s -> Trafo3d.Scale(0.1) * s * Trafo3d.Translation(-0.95, -0.95, 0.0)))
        

    let lineSg = 
        let lines =
            bodies |> Array.map (fun b -> 
                let transformedVertices = 
                    (b.history.Positions, viewProj) ||> AVal.map2 (fun vertices vp -> 
                        vertices |> Array.map (fun v -> vp.Forward.TransformPosProjFull v |> V4f)
                    )
                Sg.draw IndexedGeometryMode.LineStrip
                |> Sg.vertexAttribute DefaultSemantic.Positions transformedVertices
            )
        Sg.ofArray lines
        |> Sg.shader { 
            do! DefaultSurfaces.constantColor C4f.White
        }

    let marsTrafo = cval Trafo3d.Identity
    let marsToGlobalReferenceFrame = 
        let marr : double[] = Array.zeroCreate 9
        let pdMat = fixed &marr[0]
        time |> AVal.map (fun time -> 
            let r = CooTransformation.GetPositionTransformationMatrix("IAU_MARS", referenceFrame, Time.toUtcFormat time, pdMat)
            if r <> 0 then 
                Log.warn "GetPositionTransformationMatrix"
                Trafo3d.Identity
            else
                let m = M44d(M33d(marr))
                Trafo3d(m, m.Inverse)
        )

    let sunDir = 
        (time, view) ||> AVal.map2 (fun t v -> 
            let targetState = Spice.getRelState referenceFrame "SUN" "MARS" (Time.toUtcFormat time.Value)
            match targetState with
            | None -> V3d.Zero
            | Some targetState -> 
                v.ViewTrafo.TransformPos(targetState.pos)
        )

    let marsSg =
        Sg.ofList hierarchies
        |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
        |> Sg.projTrafo (frustumMars |> AVal.map Frustum.projTrafo)
        |> Sg.shader {
            do! Shaders.normalViewSpace
            do! Shaders.stableTrafo
            do! DefaultSurfaces.constantColor C4f.White 
            do! DefaultSurfaces.diffuseTexture 
            do! Shader.LoDColor 
            do! Shaders.sunLight
        }
        |> Sg.uniform "LodVisEnabled" (cval false)
        |> Sg.trafo marsToGlobalReferenceFrame
        |> Sg.trafo marsTrafo
        |> Sg.uniform "SunPosViewSpace" sunDir
        |> Sg.uniform "Near" (frustumMars |> AVal.map Frustum.near)
        |> Sg.uniform "Far" (frustumMars |> AVal.map Frustum.far)

    let sg =
        Sg.ofList [ planets; texts; info; lineSg; marsSg ] 


    let s = 
        win.AfterRender.Add(fun _ -> 
            transact (fun _ -> 

                let targetState = Spice.getRelState referenceFrame "MARS" observer (Time.toUtcFormat time.Value)
                match targetState with
                | None -> ()
                | Some targetState -> 
                    let rot = targetState.rot
                    let t = Trafo3d.FromBasis(rot.C0, rot.C1, -rot.C2, V3d.Zero)
                    initialView.Value <- CameraView.ofTrafo t.Inverse
                    marsTrafo.Value <- Trafo3d.Translation(targetState.pos)

                time.Value <- time.Value + TimeSpan.FromDays(0.001)
                animationStep()

            )
        )
    
    let task =
        app.Runtime.CompileRender(win.FramebufferSignature, sg)

    win.RenderTask <- task
    win.Run()
    0