namespace PRo3D

open System
open System.Diagnostics
open FSharp.Data.Adaptive    
open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Core
open PRo3D.SimulatedViews
open RemoteControlModel
open MBrace

type ClientStatistics =
    {
        session         : System.Guid
        name            : string
        frameCount      : int
        invalidateTime  : float
        renderTime      : float
        compressTime    : float
        frameTime       : float
    }

module RemoteControlApp =
    
    let jsonSerializer = FsPickler.Json.JsonSerializer(indent=true)

    

    let fromDate (dt : DateTime) =
        dt.ToString("yyyymmdd_hhmmss")

    let takeScreenshot baseAddress (width:int) (height:int) name folder =
        let wc = new System.Net.WebClient()
        let path = "screenshots"
        if System.IO.Directory.Exists path |> not then
            System.IO.Directory.CreateDirectory path |> ignore
        let clientStatistic = 
            let path = sprintf "%s/rendering/stats.json" baseAddress
            Log.line "[RemoteControl] querying rendering stats at: %s" path
            let result = wc.DownloadString(path) 
            let clientBla : list<ClientStatistics> =
                Pickler.unpickleOfJson  result
            match clientBla with
                | [] -> failwith "no client bla"
                | x::[] -> x
                | _ -> failwith "doent know"
        let screenshot =            
            sprintf "%s/rendering/screenshot/%s?w=%d&h=%d&samples=8" baseAddress clientStatistic.name width height
        Log.line "[RemoteControl] Running screenshot on: %s" screenshot    

        match System.IO.Directory.Exists folder with
          | true -> ()
          | false -> System.IO.Directory.CreateDirectory folder |> ignore
               
        let filename = System.IO.Path.ChangeExtension (name,".jpg")
        wc.DownloadFile(screenshot,Path.combine [folder; filename])

    let mkInstrumetnWps m = 
        let pShots = 
            m.shots |> IndexList.choose(fun x -> PlatformShot.froAdaptiveRoverModel m.Rover x)
        { m with platformShots = pShots }

    let loadData m = 
        match Serialization.fileExists "./waypoints.wps" with
          | Some path -> 
                let wp = Serialization.loadAs<IndexList<PRo3D.Viewer.WayPoint>> path

                let shots = 
                    wp 
                      |> IndexList.map Shot.fromWp 
                      |> IndexList.toList 
                      |> Serialization.saveJson "./shots.json" 
                      |> IndexList.ofList

                let m = { m with shots = shots }  |> mkInstrumetnWps

                m.platformShots |> IndexList.toList |> Serialization.saveJson "./platformshots.json" |> ignore
                m
          | None -> m

    let loadLast m = 
        match Serialization.fileExists "./remote.mdl" with
          | Some path -> 
            Serialization.loadAs<RemoteModel> path
          | None ->
            m   

    let loadRoverData (m : RemoteModel) =      
      let names = RoverProvider.platformNames()    
      printfn "%A" names

      let roverData =
          names 
            |> Array.map RoverProvider.initRover 
            |> List.ofArray 

      let rovs = roverData|> List.map(fun (r,_) -> r.id, r) |> HashMap.ofList
      let plats = roverData|> List.map(fun (r,p) -> r.id, p) |> HashMap.ofList

      printfn "found rover(s): %d" (rovs |> HashMap.count)
      
      { m with Rover = { m.Rover with rovers = rovs; platforms = plats } }
           
    let update (baseAddress : string) (send : RemoteAction -> unit) (m : RemoteModel) (a : RemoteControlModel.Action) =
        match a with
            | CaptureShot sh ->                
                let view = sh |> Shot.getViewSpec
                view |> RemoteAction.SetView |> send

                try Utilities.takeScreenshot baseAddress sh.col sh.row sh.id sh.folder ".png"  with e -> printfn "error: %A" e
                m
            | CapturePlatform psh ->
                let view = PlatformShot.getViewSpec m.Rover psh
                view |> RemoteAction.SetView |> send

                try Utilities.takeScreenshot baseAddress view.resolution.X view.resolution.Y psh.id psh.folder ".png"  with e -> printfn "error: %A" e
                m                
            | UpdateCameraTest sh ->                                
                send <| RemoteAction.SetCameraView (sh |> Shot.getCamera)
                m
            | UpdatePlatformTest sh ->
                let platform = RemoteControlModel.PlatformShot.froAdaptiveRoverModel m.Rover sh
                match platform with 
                   | Some p ->
                        
                        let view = PlatformShot.getViewSpec m.Rover p
                        view |> RemoteAction.SetView |> send

                        try Utilities.takeScreenshot baseAddress view.resolution.X view.resolution.Y sh.id sh.folder ".png"  with e -> printfn "error: %A" e
                        m
                    | None -> m
            | SelectShot sh -> 
                //let cv = sh |> Shot.getCamera
                //let cv = CameraView.ofTrafo cv.ViewTrafo
                //send <| RemoteAction.SetCameraView (cv)
                { m with selectedShot = Some sh }
            | Play ->                
                for sh in m.shots do
                    send <| RemoteAction.SetCameraView (sh |> Shot.getCamera)                  
                    try Utilities.takeScreenshot baseAddress sh.col sh.row sh.id sh.folder ".png" with e -> printfn "error: %A" e 
                m
            | Load ->
                loadData m
            | SaveModel -> 
                m |> Serialization.save "./remote.mdl"
            | RemoveModel -> 
                System.IO.File.Delete "./remote.mdl"
                m
            | OpenFolder path ->
                Process.Start("explorer.exe", path) |> ignore
                m
            | RoverMessage a ->                
                let m = { m with Rover = RoverApp.update m.Rover a }                                
                match m.selectedShot with
                    | Some sh ->                
                        let psh = PlatformShot.froAdaptiveRoverModel m.Rover sh
                        match psh with
                          | Some p ->                                                                                                         
                            PlatformShot.getViewSpec m.Rover p 
                              |> RemoteAction.SetView 
                              |> send                            
                          | None -> ()                                                                       
                    | _ -> ()
                m

    let item (sh:Shot) (selected : aval<Option<Shot>>) =
        let attr = 
            let background = "background-color:#636363"
            amap {
                let! sel = selected
                let bla =
                  match sel with 
                    | Some x -> x.id = sh.id
                    | None -> false

                if bla then yield style background
            
                yield clazz "item"                
                yield onClick (fun _ -> SelectShot sh)
            } |> AttributeMap.ofAMap

        Incremental.div attr 
            (
                alist {
                    yield div [clazz "content"] [                                        
                        div [clazz "header"] [
                            text sh.id
                            i [clazz "ui inverted icon map pin"; onClick (fun _ -> UpdateCameraTest sh)] [] 
                            |> UI.wrapToolTip DataPosition.Bottom "goto waypoint"

                            i [clazz "ui inverted icon camera retro"; onClick (fun _ -> UpdateCameraTest sh)] [] 
                            |> UI.wrapToolTip DataPosition.Bottom "take screenshot"
                            // i [clazz "ui inverted icon folder"; onClick (fun _ -> OpenFolder sh.folder)] []
                            i [clazz "ui inverted icon find"; onClick (fun _ -> UpdatePlatformTest sh)] [] 
                            |> UI.wrapToolTip DataPosition.Bottom "take platformshot"
                        ]
                    ]
                }
            )

    //#bdbdbd
    let viewShots (m:AdaptiveRemoteModel) = 
        Incremental.div 
            (AttributeMap.ofList [clazz "ui divided list inverted segment"; style "overflow-y : auto; width: 300px; overflow : visible"]) 
            (                
                alist {                                                          
                    for sh in m.shots do 
                        yield item sh m.selectedShot
                }
            )
    
    let view (m : AdaptiveRemoteModel) : DomNode<RemoteControlModel.Action> =          
        require GuiEx.semui (
            div [clazz "ui two column grid"] [
                div [clazz "ui segment"] [
                    h1 [clazz "ui header"] [text "Screenshot Service"]
                    button [clazz "ui labeled icon button"; onClick (fun _ -> SaveModel)] [
                        i [clazz "save icon"] []
                        text "Save Model"
                    ]
                    br []
                    button [clazz "ui labeled icon button"; onClick (fun _ -> RemoveModel)] [
                        i [clazz "remove icon"] []
                        text "Delete Model"
                    ]
                ]
                div [clazz "column"] [
                    div [clazz "ui segment"] [
                        h2 [clazz "ui header"] [text "Waypoints"]
                        button [clazz "ui labeled icon button"; onClick (fun _ -> Play)] [
                            i [clazz "play icon"] []
                            text "play"
                        ]
                        button [clazz "ui labeled icon button"; onClick (fun _ -> Load)] [
                            i [clazz "Eject icon"] []
                            text "load"
                        ]
                        viewShots m
                    ]
                    //div[clazz "ui segment"] [
                    //    h2 [clazz "ui header"][text "Rovers"]
                    //    RoverApp.view m.Rover |> UI.map RoverMessage
                    //]
                ]
                div [clazz "column"] []                
            ]    
        )

    let threads (m : RemoteModel) : ThreadPool<RemoteControlModel.Action> =
        ThreadPool.empty
    
    let app baseAddress send =
        {
            unpersist = Unpersist.instance
            threads = threads
            view = view
            update = update baseAddress send
            initial = { selectedShot = None; shots = IndexList.empty; Rover = RoverApp.initial; platformShots = IndexList.empty } 
              |> loadData |> loadLast |> loadRoverData
        }
