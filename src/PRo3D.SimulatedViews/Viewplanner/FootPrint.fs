namespace PRo3D.SimulatedViews

open System
open System.IO

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI
open IPWrappers
open FShade.Intrinsics
open Aardvark.Base.CameraView

open PRo3D.Base
open Chiron

open Adaptify

module FootPrint = 
    
    let getFootprintsPath (scenePath:string) =
        let path = Path.GetDirectoryName scenePath
        Path.combine [path;"FootPrints"]
       
    let toPngName roverName instrumentName (dateTime : DateTime) =
        System.String.Format(
            "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}_{6}_{7}",
            dateTime.Year,
            dateTime.Month, 
            dateTime.Day, 
            dateTime.Hour, 
            dateTime.Minute, 
            dateTime.Second, 
            roverName, 
            instrumentName
        )
    
    let toSvxName roverName instrumentName (dateTime : DateTime) =
        System.String.Format(
            "{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}_{6}_{7}.svx",
            dateTime.Year, 
            dateTime.Month, 
            dateTime.Day, 
            dateTime.Hour, 
            dateTime.Minute, 
            dateTime.Second, 
            roverName, 
            instrumentName
        )
    
    let createFootprintData (vp:ViewPlanModel) (scenePath:string) =
    
        let fpPath = getFootprintsPath scenePath
    
        match vp.selectedViewPlan with
        | Some v -> 
            let now = DateTime.Now
            let roverName = v.rover.id
            let instrumentName = 
                match v.selectedInstrument with
                | Some i -> i.id
                | None -> ""
                       
            // screenshot
            let pngName = toPngName roverName instrumentName now
                
            let svxName = toSvxName roverName instrumentName now
    
            let width, height =
                match v.selectedInstrument with
                | Some i -> 
                    let horRes = i.intrinsics.horizontalResolution/uint32(2)
                    let vertRes = i.intrinsics.verticalResolution/uint32(2)
                    int(horRes), int(vertRes)
                | None -> 512, 512
    
            // save png file
            try Utilities.takeScreenshotFromAllViews "http://localhost:54322" width height pngName fpPath ".png" with 
                e -> printfn "error: %A" e
                  
            //instrumentInfo
            let angles = {
                panAxis = 
                    v.rover.axes.TryFind "Pan Axis" 
                    |> Option.map(fun x -> x.angle.value ) 
                    |> Option.defaultValue 1.0      
                tiltAxis = 
                    v.rover.axes.TryFind "Tilt Axis" 
                    |> Option.map(fun x -> x.angle.value ) 
                    |> Option.defaultValue 1.0
            }
    
            let focal =
                v.selectedInstrument
                |> Option.map(fun x -> x.focal.value )
                |> Option.defaultValue 1.0                
    
            let referenceFrameInfo = {
                name = "Ground"
                parentFrameName = ""
            }
    
            let instrumentInfo = {
                camIdentifier       = instrumentName
                angles              = angles
                focalLength         = focal
                referenceFrameInfo  = referenceFrameInfo
            }
            
            //simulatedViewData
            let fileInfo = {
                fileType = "PNGImage"
                path     = fpPath
                name     = pngName
            }
    
            let calibration = {
                instrumentPlatformXmlFileName    = v.rover.id + ".xml"
                instrumentPlatformXmlFileVersion = 1.0
            }
    
            let roverInfo = {
                position       = v.position
                lookAtPosition = v.lookAt
                placementTrafo = v.roverTrafo
            }
    
            let acquisition = {
                roverInfo       = roverInfo
                instrumentInfo  = instrumentInfo
            }
    
            let simulatedViewData = {
                fileInfo    = fileInfo
                calibration = calibration
                acquisition = acquisition
            }
    
            //Serialization.save (Path.Combine(fpPath, svxName)) simulatedViewData |> ignore
            let json = 
                simulatedViewData 
                |> Json.serialize 
                |> Json.formatWith JsonFormattingOptions.Pretty 
                |> Serialization.writeToFile (Path.Combine(fpPath, svxName))
            //Serialization.writeToFile (Path.Combine(fpPath, svxName)) json 
            vp
        | None -> vp 
    
    let updateFootprint (instrument:Instrument) (roverpos:V3d) (model:ViewPlanModel) =
        
        let id = 
            match model.selectedViewPlan with
            | Some vp -> Some vp.id
            | None -> None
        
        let res = V2i((int)instrument.intrinsics.horizontalResolution, (int)instrument.intrinsics.verticalResolution)
        //let image = PixImage<byte>(Col.Format.RGB,res).ToPixImage(Col.Format.RGB)
       
        let pi = PixImage<byte>(Col.Format.RGBA, res)
        pi.GetMatrix<C4b>().SetByCoord(fun (c : V2l) -> C4b.White) |> ignore
        let tex = PixTexture2d(PixImageMipMap [| (pi.ToPixImage(Col.Format.RGBA)) |], true) :> ITexture
        
        let projectionTrafo = model.instrumentFrustum |> Frustum.projTrafo
        
        let location = model.instrumentCam.Location - roverpos //transformenExt.position
        let testview = model.instrumentCam.WithLocation location
    
        let fp = 
            {
                vpId             = id
                isVisible        = true
                projectionMatrix = projectionTrafo.Forward
                instViewMatrix   = testview.ViewTrafo.Forward //model.instrumentCam.view.ViewTrafo.Forward
                projTex          = tex
                globalToLocalPos = roverpos //transformenExt.position
            }
        fp //{ model with footPrint = fp }