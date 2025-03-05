open PRo3D.Core

open System.IO
open Aardvark.Base
open Aardvark.GeoSpatial.Opc


[<EntryPoint>]
let main args = 
    
    let mola =
        { 
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies = 
                    Seq.delay (fun _ -> 
                        System.IO.Directory.GetDirectories(@"C:\pro3ddata\MOLA") 
                    )
            boundingBox      = Box3d.Parse("[[1042657.138109462, 3023778.035968372, -472791.711967824], [1492041.915577915, 3230435.734121298, -231.611523378]]") 
            near             = 0.1
            far              = 10000.0
            speed            = 5.0
            lodDecider       =  DefaultMetrics.mars2 
        }

    let jezereo =
        { 
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies = 
                    Seq.delay (fun _ -> 
                        System.IO.Directory.GetDirectories(@"C:\pro3ddata\JezeroRGB\Jezero1") 
                        |> Seq.collect System.IO.Directory.GetDirectories
                    )
            boundingBox      = Box3d.Parse("[[701677.203042967, 3141128.733093360, 1075935.257765322], [701942.935458576, 3141252.724183598, 1076182.681085336]]") 
            near             = 0.1
            far              = 10000.0
            speed            = 5.0
            lodDecider       =  DefaultMetrics.mars2 
        }

    let gale = 
        { 
            useCompressedTextures = true
            preTransform     = Trafo3d.Identity
            patchHierarchies = 
                    Seq.delay (fun _ -> 
                        System.IO.Directory.GetDirectories(@"C:\pro3ddata\MSL") 
                    )
            boundingBox      = Box3d.Parse("[[-2501010.345666911, 2277325.986722603, -298757.311126349], [-2499653.391829742, 2278566.143892688, -296038.813352552]]") 
            near             = 0.1
            far              = 10000.0
            speed            = 5.0
            lodDecider       =  DefaultMetrics.mars2 
        }


    let jezero = 
        Directory.GetDirectories(@"C:\pro3ddata\JezeroRGB\Jezero1")
        |> Seq.collect Directory.GetDirectories
      
    let gale = 
         Directory.GetDirectories(@"C:\pro3ddata\MSL\")
         |> Seq.collect Directory.GetDirectories

    let additional = Seq.append jezero gale

    TestViewer.run mola additional