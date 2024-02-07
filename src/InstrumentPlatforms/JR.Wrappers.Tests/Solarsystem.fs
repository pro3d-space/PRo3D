module Solarsytsem

open System
open System.Threading
open System.IO
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open FSharp.Data.Adaptive
open JR
open Aardvark.Rendering.Text

[<Struct>]
type RelState = 
    {
        pos : V3d
        vel : V3d
    }

module Time =

    let toUtcFormat (d : DateTime) = 
        d.ToUniversalTime()
         .ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'");

module Spice = 

    let getRelState (referenceFrame : string) (target : string) (observer : string) (obsTime : string)   = 
        let mutable px,py,pz = 0.0,0.0,0.0
        let mutable vx,vy,vz = 0.0,0.0,0.0
        let r = CooTransformation.GetRelState(target, observer, obsTime, referenceFrame, &px, &py, &pz, &vx, &vy, &vz)
        if r <> 0 then failwith "[spice] GetRelState failed."
        { pos = V3d(px,py,pz); vel = V3d(vx,vy,vz) }


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


    let private diffuseSampler =
        sampler2d {
            texture uniform.DiffuseColorTexture
            filter Filter.Anisotropic
            maxAnisotropy 16
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let splatPoint (v : Vertex) = 
        vertex {
            return { v with s = v.ndcSize }
        }

    let round (v : Vertex) =
        fragment {
            let c = v.tc * 2.0 - V2d.II
            let p = V3d(c, 1.0 - Vec.length c.XY)
            let p = p.XZY
            let thetha = acos (p.Z) / Math.PI
            let phi = ((float (sign p.Y)) * acos (p.X / Vec.length p.XY)) / (Math.PI * 2.0)
            let t = diffuseSampler.Sample(V2d(phi, thetha))
            let f = Vec.dot c c - 1.0
            if f > 0.0 then discard()
            return { v with c = t * Vec.dot V3d.III p }
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

let run argv = 
    Aardvark.Init()

    use app = new OpenGlApplication()
    use win = app.CreateGameWindow(4)

    // unpack CooTransformation runtime library.
    //Aardvark.UnpackNativeDependencies(typeof<CooTransformation>.Assembly)

    use _ = 
        let r = CooTransformation.Init(true, Path.Combine(".", "logs"))
        if r <> 0 then failwith "could not initialize CooTransformation lib."
        { new IDisposable with member x.Dispose() = CooTransformation.DeInit() }


    let spiceFileName = @"F:\pro3d\hera-kernels\kernels\mk\hera_crema_2_0_LPO_ECP_PDP.tm"
    System.Environment.CurrentDirectory <- Path.GetDirectoryName(spiceFileName)
    let r = CooTransformation.AddSpiceKernel(spiceFileName)
    if r <> 0 then failwith "could not add spice kernel"


    let bodySources = 
        [| "sun", C4f.White, 1392700.0
           "mercury", C4f.Gray, 12742.0
           "venus", C4f.AliceBlue, 12742.0
           "earth", C4f.Blue, 12742.0
           "moon", C4f.Beige, 34748.0
           "mars", C4f.Red, 6779.0
           "phobos", C4f.Red, 22.533
           "deimos", C4f.Red, 12.4
           "HERA", C4f.Magenta, 12742.0
        |]

    let time = cval (DateTime.Parse("2025-03-10 19:08:12.60"))

    let observer = "SUN" // "SUN"

    let lookAtMoon = Spice.getRelState "J2000" "EARTH" "MOON" (Time.toUtcFormat time.Value)
    let initialView = CameraView.lookAt V3d.Zero lookAtMoon.pos V3d.OOI
    let speed = 7900.0
    let view = initialView |> DefaultCameraController.controlExt speed win.Mouse win.Keyboard win.Time
    let distanceSunPluto = 5906380000.0
    let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 1000.0 distanceSunPluto (float s.X / float s.Y))
    let aspect = win.Sizes |> AVal.map (fun s -> float s.X / float s.Y)

    let projTrafo = frustum |> AVal.map Frustum.projTrafo
    let viewProj = 
        (view, projTrafo) ||> AVal.map2 (fun view projTrafo -> 
            CameraView.viewTrafo view * projTrafo
        )

    let getProjPos (t : AdaptiveToken) (clip : bool) (pos : V3d) =
        let vp = viewProj.GetValue(t)
        let ndc = vp.Forward.TransformPosProj(pos)
        let box = Box3d.FromPoints(V3d(-1,-1,-1),V3d(1,1,1))
        if not clip || box.Contains(ndc) then 
            V3d ndc |> Some
        else
            None

    let getCurrentProjPos (clip : bool) (pos : V3d) = 
        getProjPos AdaptiveToken.Top clip pos

    let bodies = 
        bodySources |> Array.map (fun (name,_, diameter) -> 
            { name = name; pos = cval V3d.Zero; history = AdaptiveLine(); ndcSize = cval V2d.OO; radius = diameter * 0.5 }
        )


    let animationStep () = 
        bodies |> Array.iter (fun b -> 
            let time = time.GetValue()
            let rel = Spice.getRelState "J2000" b.name observer (Time.toUtcFormat time)
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
                        Trafo3d.Scale(0.05) * scale * Trafo3d.Translation(ndc.XYO)
                    )
                p, AVal.constant b.name
            )
        Sg.texts Font.Symbola C4b.White (ASet.ofArray contents)




    let planets = 
        Sg.draw IndexedGeometryMode.PointList
        |> Sg.fileTexture DefaultSemantic.DiffuseColorTexture  @"F:\pro3d\moon.jpg" true
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
        |> Sg.trafo (scale |> AVal.map (fun s -> Trafo3d.Scale(0.1) * s * Trafo3d.Translation(-0.95,-0.95,0.0)))
        

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

    let sg =
        Sg.ofList [planets; texts; info; lineSg ] 


    let s = 
        win.AfterRender.Add(fun _ -> 
            transact (fun _ -> 
                time.Value <- time.Value + TimeSpan.FromDays(0.01)
                animationStep()

            )
            
        )

    
    let task =
        app.Runtime.CompileRender(win.FramebufferSignature, sg)


    win.RenderTask <- task
    win.Run()
    0