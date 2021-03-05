namespace PRo3D

open System

open Suave
open Suave.WebPart
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Json

open Aardvark.UI


open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Viewer
open Aardvark.Base

module NotebookEndpoint = 


    let getWebParts (app : MutableApp<Model,ViewerAction>) =

        let setCORSHeaders =
            Suave.Writers.setHeader  "Access-Control-Allow-Origin" "*"
            >=> Suave.Writers.setHeader "Access-Control-Allow-Headers" "content-type"
        
        let allow_cors : WebPart =
            choose [
                OPTIONS >=>
                    fun context ->
                        context |> (
                            setCORSHeaders
                            >=> OK "CORS approved" )
            ]




        [
            allow_cors

            path "/resetCamera" >=> (fun (ctx : HttpContext) -> 
                async {
                    let description = 
                        match ctx.request.queryParam "description" with
                        | Choice1Of2 d -> Some d
                        | Choice2Of2 _ -> None
                    let cam = CameraView.lookAt V3d.III V3d.OOO V3d.OOI
                    ViewerAction.SetCamera cam |> Seq.singleton |> app.update Guid.Empty 
                    return! OK "" ctx
                }
            )

            path "/setCamera" >=> POST >=> (request (fun r ->
                    let getString (rawForm : byte[]) =
                        System.Text.Encoding.UTF8.GetString(rawForm)
                    let cam = r.rawForm |> getString |> PRo3D.Remoting.Camera.fromJson 
                    CameraView.look cam.location cam.forward cam.up |> ViewerAction.SetCamera |> Seq.singleton |> app.update Guid.Empty 
                    OK ""
                )
            )
        ]