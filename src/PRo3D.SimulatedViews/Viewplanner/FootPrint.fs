namespace PRo3D.SimulatedViews

//open Aardvark.Base
//open Aardvark.UI.Animation
//open Aardvark.UI.Primitives
//open Aardvark.Application
//open Aardvark.UI
//open Aardvark.VRVis
//open FSharp.Data.Adaptive
//open Aardvark.SceneGraph.SgPrimitives
//open Aardvark.Base.Rendering

//open System
//open System.Diagnostics
//open System.IO

//open MBrace.FsPickler.Json   
//open Chiron

//open PRo3D.Base


//module FootPrint = 
        
//    let getFootprintsPath (scenePath:string) =
//        let path = Path.GetDirectoryName scenePath
//        Path.combine [path;"FootPrints"]
       
//    let createFootprintData (vp:ViewPlanModel) (scenePath:string) =

//        let fpPath = getFootprintsPath scenePath

//        match vp.selectedViewPlan with
//            | Some v -> 
//                let now = DateTime.Now
//                let roverName = v.rover.id
//                let instrumentName = 
//                    match v.selectedInstrument with
//                                | Some i -> i.id
//                                | None -> ""
                
               
//                let pngName = System.String.Format(
//                                        "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}_{6}_{7}",
//                                        now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, roverName, instrumentName)

//                let svxName = System.String.Format(
//                                    "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}_{6}_{7}.svx",
//                                    now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, roverName, instrumentName)

//                let width, height =
//                        match v.selectedInstrument with
//                                | Some i -> let horRes = i.intrinsics.horizontalResolution/uint32(2)
//                                            let vertRes = i.intrinsics.verticalResolution/uint32(2)
//                                            int(horRes), int(vertRes)
//                                | None -> 512, 512
//                // save png file
//                try Utilities.takeScreenshotFromAllViews "http://localhost:54321" width height pngName fpPath ".png" with e -> printfn "error: %A" e

               
//                let fileInfo = {
//                    fileType = "PNGImage"
//                    path = fpPath
//                    name = pngName
//                }
//                let calibration = {
//                    instrumentPlatformXmlFileName       = v.rover.id + ".xml"
//                    instrumentPlatformXmlFileVersion    = 1.0
//                }
//                let roverInfo = {
//                    position = v.position
//                    lookAtPosition = v.lookAt
//                    placementTrafo = v.roverTrafo
//                }
//                let panAx = v.rover.axes.TryFind "Pan Axis" |> Option.map(fun x -> x.angle.value )
//                let panVal = match panAx with | Some av -> av | None -> 1.0

//                let tiltAx = v.rover.axes.TryFind "Tilt Axis" |> Option.map(fun x -> x.angle.value )
//                let tiltVal = match tiltAx with | Some av -> av | None -> 1.0
//                let angles = {
//                    panAxis = panVal
//                    tiltAxis = tiltVal
//                }
//                let focal =
//                        match v.selectedInstrument with
//                                | Some i -> i.focal.value
//                                | None -> 1.0
//                let referenceFrameInfo = {
//                    name = "Ground"
//                    parentFrameName = ""
//                }

//                let instrumentinfo = {
//                    camIdentifier       = instrumentName
//                    angles              = angles
//                    focalLength         = focal
//                    referenceFrameInfo  = referenceFrameInfo
//                }
//                let acquisition = {
//                    roverInfo       = roverInfo
//                    instrumentInfo  = instrumentinfo
//                }
//                let simulatedViewData =
//                    {
//                        fileInfo    = fileInfo
//                        calibration = calibration
//                        acquisition = acquisition
//                    }
//                //Serialization.save (Path.Combine(fpPath, svxName)) simulatedViewData |> ignore
//                let json = simulatedViewData |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty |> Serialization.writeToFile (Path.Combine(fpPath, svxName))
//                //Serialization.writeToFile (Path.Combine(fpPath, svxName)) json 
//                vp
//            | None -> vp 
    
//    let updateFootprint (instrument:Instrument) (roverpos:V3d) (model:ViewPlanModel) =
        
//        let id = match model.selectedViewPlan with
//                    | Some vp -> Some vp.id
//                    | None -> None
        

//        let res = V2i((int)instrument.intrinsics.horizontalResolution, (int)instrument.intrinsics.verticalResolution)
//        //let image = PixImage<byte>(Col.Format.RGB,res).ToPixImage(Col.Format.RGB)
       
//        let pi = PixImage<byte>(Col.Format.RGBA, res)
//        pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
//        let tex = PixTexture2d(PixImageMipMap [| (pi.ToPixImage(Col.Format.RGBA)) |], true) :> ITexture
        
//        let projectionTrafo = model.instrumentFrustum |> Frustum.projTrafo
        

//        let location = model.instrumentCam.Location - roverpos //transformenExt.position
//        let testview = model.instrumentCam.WithLocation location


//        let fp = 
//                {
//                    vpId      = id
//                    isVisible = true
//                    projectionMatrix = projectionTrafo.Forward
//                    instViewMatrix = testview.ViewTrafo.Forward //model.instrumentCam.view.ViewTrafo.Forward
//                    projTex = tex
//                    globalToLocalPos = roverpos //transformenExt.position
//                }
//        fp //{ model with footPrint = fp }
