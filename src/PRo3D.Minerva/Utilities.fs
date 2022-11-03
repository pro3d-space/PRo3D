namespace PRo3D.Minerva

open System
open System.IO
open System.Diagnostics

open Aardvark.Base
open Aardvark.electron.shell
open Aardvark.Rendering
open Aardvark.Rendering.Text 
open FSharp.Data.Adaptive
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.UI

open FShade

open OpcViewer.Base
open PRo3D.Base

module Files = 

    let loadTifAndConvert (client : System.Net.WebClient) (featureId: string)  =

        let filename = featureId.ToLower() + ".tif"
        let imagePath = @".\MinervaData\" + filename

        match (File.Exists imagePath) with
        | true -> ()
        | false -> 
            let targetPath = @".\MinervaData\" + featureId.ToLower() + ".png"

            let path = "https://minerva.eox.at/store/datafile/" + featureId + "/" + filename

            try
                client.DownloadFile(path, imagePath) |> ignore   
                System.Threading.Thread.Sleep(10)
                System.Threading.Thread.Sleep(10)
                PixImage.Create(imagePath).ToPixImage<byte>().SaveAsImage(targetPath)
                System.Threading.Thread.Sleep(10)
            with e -> ()//Log.line "[Minerva] error: %A" e
    
    //let loadTif2AndConvert (access: string) (featureIds: list<string>) =

    let impactViewer = @"C:\Program Files\JOANNEUM RESEARCH\ImpactViewer\bin\ImpactViewer.exe"

    let openImage imagePath = 
        let argument = sprintf "\"%s\"" imagePath
        Process.Start(impactViewer, argument) |> ignore

    let openExplorer filePath =
        let argument = sprintf "/select, \"%s\"" filePath
        shell.openPath(argument) |> ignore

    let downloadAndOpenTif (featureId:string) (model:MinervaModel) =
        // https://minerva.eox.at/store/datafile/FRB_495137799RADLF0492626FHAZ00323M1/frb_495137799radlf0492626fhaz00323m1.tif
        let filename = featureId.ToLower() + ".tif" //"frb_495137799radlf0492626fhaz00323m1.tif"
        let imagePath = @".\MinervaData\" + filename
        let path = "https://minerva.eox.at/store/datafile/" + featureId + "/" + filename
        match (File.Exists imagePath) with
        | true -> 
           imagePath |> openImage
           model
        | false -> 
            let mutable client = new System.Net.WebClient()
            client.UseDefaultCredentials <- true
            let credentials = 
                System.Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("minerva:tai8Ies7"))
            client.Headers.[System.Net.HttpRequestHeader.Authorization] <- "Basic " + credentials
            //try takeScreenshot baseAddress sh.col sh.row sh.id sh.folder with e -> printfn "error: %A" e

            try (client.DownloadFile(path, imagePath) |> ignore) with
                e -> Log.error "[Minerva] error: %A" e

            match (File.Exists imagePath) with
            | true -> 
                imagePath |> openImage
            | false when Config.ShowMinervaErrors -> 
                Log.error "[Minerva] sth. went wrong with tif file"
            | _ -> ()
                
            model
             
module DataStructures =
    type HarriSchirchWrongBlockingCollection<'a>() =
        let sema = new System.Threading.SemaphoreSlim(0)
        let l = obj()
        let queue = System.Collections.Generic.Queue<'a>()
        let mutable finished = false
    
        member x.TakeAsync() =
            async {
                do! sema.WaitAsync() |> Async.AwaitTask
                if finished then return None
                else
                    return 
                        lock l (fun _ -> 
                            queue.Dequeue() |> Some
                        )
            }
    
        member x.Enqueue(v) =
            lock l (fun _ -> 
                queue.Enqueue(v)
            )
            sema.Release() |> ignore
    
        member x.CompleteAdding() =
            finished <- true
            sema.Release()
    
        member x.IsCompleted = finished

module Drawing =

    // todo check if PRo3D.Base utilities Sg extension with offset works!
    let drawSingleColorPoints pointsF color pointSize = 
      Sg.draw IndexedGeometryMode.PointList
      |> Sg.vertexAttribute DefaultSemantic.Positions pointsF
      |> Sg.uniform "PointSize" pointSize
      |> Sg.effect [
            toEffect Shader.pointTrafo
            toEffect (Shader.constantColor color)
            toEffect DefaultSurfaces.pointSprite
            toEffect Shader.pointSpriteFragment
      ]

    let stablePoints (sgfeatures : AdaptiveSgFeatures) =
      Sg.stablePoints sgfeatures.trafo sgfeatures.positions 

    let drawSingleColoredFeaturePoints (sgfeatures : AdaptiveSgFeatures) (pointSize:aval<float>) (color:C4f) = 
        let pointsF = stablePoints sgfeatures
        drawSingleColorPoints pointsF (color.ToV4d()) pointSize |> Sg.trafo sgfeatures.trafo

    let drawFeaturePoints (sgfeatures : AdaptiveSgFeatures) (pointSize:aval<float>) = 
        let pointsF = stablePoints sgfeatures
        Sg.drawColoredPoints pointsF sgfeatures.colors pointSize |> Sg.trafo sgfeatures.trafo

    let drawHoveredFeaturePoint hoveredProduct pointSize trafo =
      let hoveredPoint = 
        AVal.map2(fun (x:Option<SelectedProduct>) (t:Trafo3d) ->  
          match x with 
          | None -> [||]
          | Some a ->  [|V3f(t.Backward.TransformPos(a.pos))|]) hoveredProduct trafo 

      drawSingleColorPoints hoveredPoint (C4f.Yellow.ToV4d()) (pointSize |> AVal.map(fun x -> x + 5.0)) |> Sg.trafo trafo

    let pass0 = RenderPass.main
    let pass1 = RenderPass.after "outline" RenderPassOrder.Arbitrary pass0

    let drawSelectedFeaturePoints (sgfeatures : AdaptiveSgFeatures) (pointSize:aval<float>) =

        let outline =
          drawSingleColoredFeaturePoints sgfeatures (pointSize |> AVal.map(fun x -> x + 4.0)) C4f.VRVisGreen 
          |> Sg.pass pass0

        let inside = 
          drawFeaturePoints sgfeatures pointSize
          |> Sg.pass pass1
          |> Sg.depthTest (AVal.constant(DepthTest.Always))

        Sg.ofList [inside; outline]

    let coneISg color radius height trafo =  
        Sg.cone 30 color radius height
        |> Sg.noEvents
        |> Sg.shader {
            do! Shader.StableTrafo.stableTrafo
            do! DefaultSurfaces.vertexColor
            //do! Shader.stableLight
        }
        |> Sg.trafo(trafo)

    let featureMousePick (boundingBox : aval<Box3d>) =
      boundingBox 
        |> AVal.map(fun box ->  
            if box.IsInvalid then
                Sg.empty
            else
                Sg.empty 
                  |> Sg.pickable (PickShape.Box box)
                  |> Sg.withEvents [
                      SceneEventKind.Click, (fun sceneHit -> true, Seq.ofList[PickProducts sceneHit]) 
                      SceneEventKind.Move,  (fun sceneHit -> true, Seq.ofList[HoverProducts sceneHit])
                  ])
        |> Sg.dynamic 

    [<AutoOpen>]
    module MissingInBase = 
        type ProcListBuilder with   
            member x.While(predicate : unit -> bool, body : ProcList<'m,unit>) : ProcList<'m,unit> =
                proclist {
                    let p = predicate()
                    if p then 
                        yield! body
                        yield! x.While(predicate,body)
                    else ()
                }

module List =
    let take' (n : int) (input : list<'a>) : list<'a> =
        if n >= input.Length then input else input |> List.take n