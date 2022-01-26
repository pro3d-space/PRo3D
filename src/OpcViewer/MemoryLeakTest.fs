namespace Aardvark.Opc.Tests

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.SceneGraph.Opc
open Aardvark.Application.Slim
open Aardvark.GeoSpatial.Opc

open FSharp.Data.Adaptive 
open MBrace.FsPickler

open Aardvark.Opc

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


module MemoryLeakTest = 
    
    open System
    open Aardvark.Base
    open Aardvark.Rendering.SceneGraph.HierarchicalLoD

    module VertexGeometry = 

        let private sizes = 
            // maps to common backend sizes
            Dictionary.ofList [
                typeof<V2d>, sizeof<float32> * 2
                typeof<V2f>, sizeof<float32> * 2
                typeof<V3f>, sizeof<float32> * 3
                typeof<V3d>, sizeof<float32> * 3
                typeof<V4f>, sizeof<float32> * 4
                typeof<V4d>, sizeof<float32> * 4
            ]
        

        let estimateSizeInBytes (vg : IndexedGeometry) = 
            let indexSize = if vg.IsIndexed then (vg.IndexArray |> unbox<int[]>).Length * sizeof<int> else 0
            let attributes = 
                vg.IndexedAttributes |> Seq.sumBy (fun attribute ->
                    attribute.Value.Length * sizes.[attribute.Value.GetType().GetElementType()]
                )
            indexSize + attributes

    let estimateMemory (runtime : IRuntime) (paths : OpcPaths) (hierarchy : PatchHierarchy) = 

        let rec flattenLeaves (t : QTree<Patch>) = 
            match t with
            | QTree.Leaf patch -> Seq.singleton patch
            | QTree.Node(path, children) -> 
                children |> Seq.collect flattenLeaves 

        let estimateSize (p : Patch) = 
            let geometry = 
                try 
                    Patch.load paths ViewerModality.XYZ p.info |> Result.Ok
                with e -> 
                    Log.warn "could not load patch %A" p.info
                    Result.Error e

            let texture = 
                let texturePath = 
                    Patch.extractTexturePath paths p.info 0
                try 
                    Loaders.loadTexture true false runtime texturePath |> Result.Ok
                with e -> 
                    Log.warn "could not texture for patch %A" p.info
                    Result.Error e

            match geometry, texture with
            | Result.Ok (geometry, _), Result.Ok (texture,textureSizeInBytes,dispose) -> 
                let geometrySize = VertexGeometry.estimateSizeInBytes geometry
                dispose.Dispose()
                textureSizeInBytes + geometrySize
            | _ -> 
                0
            
        let estimatedMemory = flattenLeaves hierarchy.tree |> Seq.sumBy estimateSize

        estimatedMemory


    let run (scene : OpcScene) =

        Aardvark.Init()

        use app = new OpenGlApplication()
        let win = app.CreateGameWindow()
        let runtime = win.Runtime

        let runner = runtime.CreateLoadRunner 1 

        let serializer = FsPickler.CreateBinarySerializer()


        let hierarchies = 
            scene.patchHierarchies |> Seq.toList |> List.map (fun basePath -> 
                let path = OpcPaths.OpcPaths basePath
                let hierarchy = PatchHierarchy.load serializer.Pickle serializer.UnPickle path
                hierarchy, basePath
            )

        let memory = 
            hierarchies |> List.sumBy (fun (hierarchy, basePath) -> 
                estimateMemory runtime (OpcPaths.OpcPaths basePath) hierarchy
            )

        let hierarchies = 
            hierarchies |> List.map (fun (hierarchy, basePath) -> 
                let t = PatchLod.toRoseTree hierarchy.tree
                Sg.patchLod win.FramebufferSignature runner basePath scene.lodDecider false false ViewerModality.XYZ true t
            )

        let speed = AVal.init scene.speed

        let bb = scene.boundingBox
        let initialView = CameraView.lookAt bb.Max bb.Center bb.Center.Normalized
        let view = initialView |> DefaultCameraController.controlWithSpeed speed win.Mouse win.Keyboard win.Time
        let frustum = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 scene.near scene.far (float s.X / float s.Y))

        let lodVisEnabled = cval true
        let fillMode = cval FillMode.Fill

        win.Keyboard.KeyDown(Keys.PageUp).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value * 1.5)
        )

        win.Keyboard.KeyDown(Keys.PageDown).Values.Add(fun _ -> 
            transact (fun _ -> speed.Value <- speed.Value / 1.5)
        )

        win.Keyboard.KeyDown(Keys.L).Values.Add(fun _ -> 
            transact (fun _ -> lodVisEnabled.Value <- not lodVisEnabled.Value)
        )

        win.Keyboard.KeyDown(Keys.F).Values.Add(fun _ -> 
            transact (fun _ -> 
                fillMode.Value <-
                    match fillMode.Value with
                    | FillMode.Fill -> FillMode.Line
                    | _-> FillMode.Fill
            )
        )

        let sg = 
            Sg.ofList hierarchies
            |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
            |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo)
            //|> Sg.transform preTransform
            |> Sg.effect [
                        Shader.stableTrafo |> toEffect
                        DefaultSurfaces.constantColor C4f.White |> toEffect
                        DefaultSurfaces.diffuseTexture |> toEffect
                        Shader.LoDColor |> toEffect
                    ]
            |> Sg.uniform "LodVisEnabled" lodVisEnabled
            |> Sg.fillMode fillMode

        win.RenderTask <- runtime.CompileRender(win.FramebufferSignature, sg)
        win.Run()
        0