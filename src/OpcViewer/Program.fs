// Learn more about F# at http://fsharp.org

open System

open Aardvark.Base
open Aardvark.GeoSpatial.Opc
open Aardvark.Opc

type Kind = Scene | Annotations

[<EntryPoint>]
let main argv =
    
    let kind = Annotations

    let jezero =
        { 
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies = 
                    System.IO.Directory.GetDirectories(@"I:\OPC\2020-08-06-Jezero-OPC") 
                    |> Seq.collect System.IO.Directory.GetDirectories
            boundingBox      = Box3d.Parse("[[709869.947406691, 3140052.258326461, 1075121.095408683], [710116.986329168, 3140296.918390740, 1075374.689812710]]") 
            near             = 0.1
            far              = 10000.0
            speed            = 5.0
            lodDecider       =  DefaultMetrics.mars2 
        }

    match  kind with
    | Scene ->
    
        TestViewer.run jezero

    | Annotations ->

        let scene =
            { 
                useCompressedTextures = true
                preTransform     = Trafo3d.Identity
                patchHierarchies = 
                        System.IO.Directory.GetDirectories(@"I:\OPC\Shaler_OPCs_2019\Shaler_Navcam") 
                        |> Seq.collect System.IO.Directory.GetDirectories
                boundingBox      = Box3d.Parse("[[-2490137.664354247, 2285874.562728135, -271408.476700304], [-2490136.248131170, 2285875.658034266, -271406.605430601]]") 
                near             = 0.1
                far              = 10000.0
                speed            = 5.0
                lodDecider       =  DefaultMetrics.mars2 
            }

        //let scene =
        //    { 
        //        useCompressedTextures = true
        //        preTransform     = Trafo3d.Identity
        //        patchHierarchies = 
        //                System.IO.Directory.GetDirectories(@"F:\pro3d\data\20200220_DinosaurQuarry2") 
        //                |> Seq.collect System.IO.Directory.GetDirectories
        //        boundingBox      = Box3d.Parse("[[-9.996176625, 1.249114172, -1.937521343], [-0.603052397, 27.938626479, -0.132129824]]") 
        //        near             = 0.1
        //        far              = 10000.0
        //        speed            = 5.0
        //        lodDecider       =  DefaultMetrics.mars2 
        //    }

        Aardvark.Rendering.GL.Config.UseNewRenderTask <- true

        let annotations = @"I:\OPC\Shaler_OPCs_2019\ShalerNew.pro3d.ann"
        //let annotations = @"F:\pro3d\data\20200220_DinosaurQuarry2\notrafo.pro3d.ann"

        let annotations = 
            PRo3D.Core.Drawing.DrawingUtilities.IO.loadAnnotations annotations

        FSharp.Data.Adaptive.ShallowEqualityComparer.Set {
            new System.Collections.Generic.IEqualityComparer<Trafo3d> with
                member x.GetHashCode _ = 0
                member x.Equals(_,_) = false
            }

        AnnotationViewer.run scene annotations

