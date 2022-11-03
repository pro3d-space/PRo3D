namespace PRo3D.Viewer

open PRo3D.Viewer

module RemoteApi =

    type Api(emit : ViewerAction -> unit) = 

        member x.LoadScene(fullPath : string) = 
            ViewerAction.LoadScene fullPath |> emit

        member x.SaveScene(fullPath : string) = 
            ViewerAction.SaveScene fullPath |> emit

        member x.ImportOpc(folders : array<string>) =
            List.ofArray folders |> ViewerAction.DiscoverAndImportOpcs |> emit


    type LoadScene = 
        {
            // absolute path
            sceneFile : string
        }

    type SaveScene = 
        {
            // absolute path
            sceneFile : string
        }

    type ImportOpc = 
        {
            // absolute path
            folders : array<string>
        }


    module Suave = 

        open Suave
        open Suave.Filters
        open Suave.Operators

        open System
        open System.IO

        open System.Text.Json

        let getUTF8 (str: byte []) = System.Text.Encoding.UTF8.GetString(str)

        let loadScene (api : Api) = 
            path "/loadScene" >=> request (fun r -> 
                let str = r.rawForm |> getUTF8
                let command : LoadScene = str |> JsonSerializer.Deserialize
                if File.Exists command.sceneFile then
                    api.LoadScene command.sceneFile 
                    Successful.OK "done"
                else
                    RequestErrors.BAD_REQUEST "Oops, something went wrong here!"
            )

        let saveScene (api : Api) = 
            path "/saveScene" >=> request (fun r -> 
                let str = r.rawForm |> getUTF8
                let command : SaveScene = str |> JsonSerializer.Deserialize
                api.SaveScene command.sceneFile 
                Successful.OK "done"
            )

        let importOpc (api : Api) = 
            path "/importOpc" >=> request (fun r -> 
                let str = r.rawForm |> getUTF8
                let command : ImportOpc = str |> JsonSerializer.Deserialize
                api.ImportOpc command.folders 
                Successful.OK "done"
            )
        
        let webPart (api : Api) = 
            choose [
                loadScene api
                importOpc api
                saveScene api
            ]