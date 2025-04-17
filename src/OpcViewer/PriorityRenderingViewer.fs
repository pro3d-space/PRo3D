namespace PriorityRendering

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Data.Opc
open Aardvark.Application.Slim
open Aardvark.GeoSpatial.Opc

open FSharp.Data.Adaptive 
open MBrace.FsPickler

open Aardvark.GeoSpatial.Opc.Load

[<AutoOpen>]
module Shader =

    open Aardvark.Base
    open Aardvark.Rendering
    open Aardvark.Rendering.Effects
    
    open FShade

    let LoDColor  (v : Vertex) =
        fragment {
            if uniform?LodVisEnabled then
                let c : V4d = uniform?LoDColor
                let gamma = 1.0
                let grayscale = 0.2126 * v.c.X ** gamma + 0.7152 * v.c.Y ** gamma  + 0.0722 * v.c.Z ** gamma 
                return grayscale * c 
            else return v.c
        }

    let stableTrafo (v : Vertex) =
        vertex {
            let vp = uniform.ModelViewTrafo * v.pos
            let wp = uniform.ModelTrafo * v.pos
            return {
                pos = uniform.ProjTrafo * vp
                wp = wp
                n = uniform.NormalMatrix * v.n
                b = uniform.NormalMatrix * v.b
                t = uniform.NormalMatrix * v.t
                c = v.c
                tc = v.tc
            }
        }

    type UniformScope with
        member x.MousePosNdc : V2d = uniform?MousePosNdc
        member x.RadiusNdc : float = uniform?RadiusNdc
        member x.SurfaceLensDepth : float = uniform?SurfaceLensDepth
        member x.SurfaceAllowLens : bool = uniform?SurfaceAllowLens
        member x.LensDepth : float = uniform?LensDepth
        member x.LensEnabled : bool = uniform?LensEnabled


    let lensMask =
        sampler2d {
            texture uniform?LensMask
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let discardNdcDistance (v : Vertex) =
        fragment {
            let d = Vec.distance v.pos.XY uniform.MousePosNdc
            if d > uniform.RadiusNdc then
                discard ()

            return v.c
        }
 
    type Vertex = 
        {
            [<Position>] pos : V4d
            [<Semantic("ClipPos")>] clipPos : V4d
            [<Color>] c : V4d
        }

    let keepNdc (v : Vertex) =
        vertex {
            return { v with clipPos = uniform.ModelViewProjTrafo * v.pos }
        }

    let magicLens (v : Vertex) = 
        fragment {
            if uniform.SurfaceAllowLens && uniform.LensEnabled && uniform.SurfaceLensDepth < uniform.LensDepth then
                let ndc = v.clipPos.XY / v.clipPos.W
                let mask = lensMask.Sample(ndc * 0.5 + V2d(0.5,0.5))
                let clipped = mask.X > 0.99 && mask.Y > 0.99 && mask.Z > 0.99 
                return V4d(v.c.XYZ, v.c.W * 0.4)
            else
                return v.c
        }


module Trafo = 
    let (^) l r = sprintf "%s%s" l r

    let readLine (filePath:string) =
            use sr = new System.IO.StreamReader (filePath)
            sr.ReadLine ()       

    let fromPath (trafoPath:string) =
         match System.IO.File.Exists trafoPath with
            | true ->
                let t = readLine trafoPath
                Trafo3d.Parse(t)
            | false -> 
                Trafo3d.Identity


module PriorityRenderingViewer = 
    

    type Surface = { priority : int; lensDepth : Option<float>; path : string; patches : Option<array<string>> }

    let run () =

        Aardvark.Init()

        use app = new OpenGlApplication()
        let win = app.CreateGameWindow()
        let runtime = win.Runtime

        let runner = runtime.CreateLoadRunner 1 

        let serializer = FsPickler.CreateBinarySerializer()

        let vicoriaCraterBasePath = @"C:\pro3ddata\VictoriaCrater"

        let speed = 5.0
        let lodDecider = DefaultMetrics.mars

        let bb = Box3d.Parse("[[3376372.058677169, -325173.566694686, -121309.194857123], [3376385.170513898, -325152.282144333, -121288.943956908]]")
        let surfaces = 
            [ { priority = 0; path = "HiRISE_VictoriaCrater"; lensDepth = Some 0.0; patches = None }
              { priority = 1; path = "HiRISE_VictoriaCrater_SuperResolution"; lensDepth = Some 1.0; patches = None }
              { priority = 2; path = "MER-B_CapeDesire_wbs"; lensDepth = None; patches = None }
            ]
            |> Seq.map (fun p -> 
                let fullPath = Path.combine [vicoriaCraterBasePath; p.path]
                let patches = Directory.GetDirectories fullPath
                { p with patches = Some patches }
            )

        let surfaceSgs = 
            surfaces |> Seq.map (fun s -> 
                s.patches
                |> Option.defaultValue [||]
                |> Array.map (fun basePath ->
                    let h = PatchHierarchy.load serializer.Pickle serializer.UnPickle (OpcPaths.OpcPaths basePath)
                    let t = PatchLod.toRoseTree h.tree
                    Sg.patchLod win.FramebufferSignature runner basePath lodDecider false false ViewerModality.XYZ PatchLod.CoordinatesMapping.Local true t
                )
                |> Sg.ofSeq
                |> Sg.uniform' "SurfaceLensDepth" (s.lensDepth |> Option.defaultValue 0.0)
                |> Sg.uniform' "SurfaceAllowLens" (s.lensDepth |> Option.isSome)
            )

        let speed = AVal.init speed

        let initialView = CameraView.lookAt bb.Max bb.Center bb.Center.Normalized
        let view = initialView |> DefaultCameraController.controlWithSpeed speed win.Mouse win.Keyboard win.Time
        let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 0.1 1000.0 (float s.X / float s.Y))


        let fillMode = cval FillMode.Fill

        win.Keyboard.KeyDown(Keys.PageUp).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value * 1.5)
        )

        win.Keyboard.KeyDown(Keys.PageDown).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value / 1.5)
        )


        win.Keyboard.KeyDown(Keys.F).Values.Add(fun _ -> 
            transact (fun _ -> 
                fillMode.Value <-
                    match fillMode.Value with
                    | FillMode.Fill -> FillMode.Line
                    | _-> FillMode.Fill
            )
        )

        let lensDepth = cval 2.0
        let lensEnabled = cval true

        let size = V2i(1024,1024)
        let colors = runtime.CreateTexture2DArray(size, TextureFormat.Rgba8, 1, 1, 10)
        let depths = runtime.CreateTexture2DArray(size, TextureFormat.Depth32fStencil8, 1, 1, 10)

        let renderTasks =
            surfaceSgs 
            |> Seq.toList
            |> List.map (fun sg -> 
                sg
                |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
                |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo)
                |> Sg.texture "LensMask" lensMask
                |> Sg.uniform "LensDepth" lensDepth
                |> Sg.uniform "LensEnabled" lensEnabled
                |> Sg.shader {
                        do! Shader.keepNdc
                        do! Shader.stableTrafo 
                        do! DefaultSurfaces.constantColor C4f.White 
                        do! DefaultSurfaces.diffuseTexture 
                        do! Shader.magicLens
                   }
                |> Sg.blendMode' BlendMod
            )
            
        let a = 
            runtime.CreateFramebuffer(
                win.FramebufferSignature, 
                Map.ofList [
                    DefaultSemantic.Colors, colors[TextureAspect.Color, 0, 0] :> IFramebufferOutput
                ]
            )

        let lensMask = 
            let signarure = runtime.CreateFramebufferSignature (Map.ofList [DefaultSemantic.Colors, TextureFormat.Rgba8])
            Sg.fullScreenQuad
            |> Sg.uniform "MousePosNdc" (win.Mouse.Position |> AVal.map (fun p -> V2d(p.NormalizedPosition.X, 1.0 - p.NormalizedPosition.Y) * 2.0 - V2d(1.0,1.0)))
            |> Sg.uniform' "RadiusNdc" 0.6
            |> Sg.shader {
                do! DefaultSurfaces.constantColor C4f.White
                do! Shader.discardNdcDistance 
            }
            |> Sg.compile win.Runtime signarure
            |> RenderTask.renderToColor win.Sizes 



        let sg = 
            Sg.ofSeq surfaceSgs
            |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
            |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo)
            |> Sg.texture "LensMask" lensMask
            |> Sg.uniform "LensDepth" lensDepth
            |> Sg.uniform "LensEnabled" lensEnabled
            |> Sg.shader {
                    do! Shader.keepNdc
                    do! Shader.stableTrafo 
                    do! DefaultSurfaces.constantColor C4f.White 
                    do! DefaultSurfaces.diffuseTexture 
                    do! Shader.magicLens
               }
            |> Sg.blendMode' BlendMode.Blend
            |> Sg.fillMode fillMode

        win.RenderTask <- runtime.CompileRender(win.FramebufferSignature, sg)
        win.Run()
        0