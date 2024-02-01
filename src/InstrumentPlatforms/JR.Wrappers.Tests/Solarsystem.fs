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
        let mutable t = 0.0
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
        }

    let splatPoint (v : Vertex) = 
        vertex {
            return { v with s = 4.0 }
        }

    let round (v : Vertex) =
        fragment {
            let c = v.tc * 2.0 - V2d.II
            let f = Vec.dot c c - 1.0
            if f > 0.0 then discard()
            return v
        }

type BodyState = 
    {
        name : string
        pos : cval<V3d>
        history : cval<list<V3d>>
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
        //let v = CooTransformation.GetDllVersion()
        //Log.line "[CooTransformation] version: %A" v
        { new IDisposable with member x.Dispose() = CooTransformation.DeInit() }


    let bodySources = 
        [| "sun", C4f.White;
           "mercury", C4f.Gray;
           "venus", C4f.AliceBlue;
           "earth", C4f.Blue;
           "moon", C4f.Beige;
           "mars", C4f.Red;
           "phobos", C4f.Red;
           "deimos", C4f.Red;
           "HERA", C4f.Magenta
           //"jupiter", C4f.White;
           //"saturn", C4f.Brown;
           //"uranus", C4f.LightBlue;
           //"neptune", C4f.Blue
        |]

    let spiceFileName = @"F:\pro3d\hera-kernels\kernels\mk\hera_crema_2_0_LPO_ECP_PDP.tm"
    System.Environment.CurrentDirectory <- Path.GetDirectoryName(spiceFileName)
    let r = CooTransformation.AddSpiceKernel(spiceFileName)
    if r <> 0 then failwith "could not add spice kernel"

    let time = cval (DateTime.Parse("2025-03-10 19:08:12.60"))
    //let time = cval DateTime.UtcNow

    let observer = "MARS" // "SUN"

    let lookAtMoon = Spice.getRelState "J2000" "HERA" "MARS" (Time.toUtcFormat time.Value)
    let initialView = CameraView.lookAt V3d.Zero lookAtMoon.pos V3d.OOI
    let speed = 7900.0
    let view = initialView |> DefaultCameraController.controlExt speed win.Mouse win.Keyboard win.Time
    let distanceSunPluto = 5906380000.0
    let proj = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 10000.0 distanceSunPluto (float s.X / float s.Y))
    let aspect = win.Sizes |> AVal.map (fun s -> float s.X / float s.Y)


    let getProjPos (t : AdaptiveToken) (pos : V3d) =
        let view = view.GetValue(t)
        let frustum = proj.GetValue(t)
        let vp = CameraView.viewTrafo view * Frustum.projTrafo frustum
        let ndc = vp.Forward.TransformPosProj(pos)
        V3d ndc

    let getCurrentProjPos (pos : V3d) = 
        getProjPos AdaptiveToken.Top pos

    let bodies = 
        bodySources |> Array.map (fun (name,_) -> 
            { name = name; pos = cval V3d.Zero; history = cval [] }
        )


    let animationStep () = 
        bodies |> Array.iter (fun b -> 
            let time = time.GetValue()
            let rel = Spice.getRelState "J2000" b.name observer (Time.toUtcFormat time)
            b.pos.Value <- getCurrentProjPos rel.pos
            b.history.Value <-
                match b.history.Value with
                | [] -> [rel.pos]
                | h::_ -> 
                    if Vec.distance h b.pos.Value > 0.04 then
                        rel.pos :: b.history.Value
                    else
                        b.history.Value

        )

    let colors = bodySources |> Array.map snd 
    let vertices = 
        AVal.custom (fun t -> 
            bodies |> Array.map (fun b -> 
                b.pos.GetValue(t) |> V3f
            )
        )

    let scale = aspect |> AVal.map (fun aspect -> Trafo3d.Scale(V3d(1.0, aspect, 1.0)))

    let texts = 
        let contents = 
            bodies |> Array.map (fun  b -> 
                let p = (b.pos,scale) ||> AVal.map2 (fun p scale -> Trafo3d.Scale(0.05) * scale * Trafo3d.Translation(p))
                p, AVal.constant b.name
            )
        Sg.texts Font.Symbola C4b.White (ASet.ofArray contents)

    let planets = 
        Sg.draw IndexedGeometryMode.PointList
        |> Sg.vertexAttribute' DefaultSemantic.Colors colors
        |> Sg.vertexAttribute  DefaultSemantic.Positions vertices
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
            AVal.custom (fun t -> 
                bodies |> Array.collect (fun b -> 
                    let h = b.history.GetValue(t)
                    h |> Seq.map (getProjPos t) |> Seq.pairwise |> Seq.map Line3d |> Seq.toArray
                )
            )
        Sg.lines (AVal.constant C4b.Gray) lines
        |> Sg.shader { 
            do! DefaultSurfaces.vertexColor
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