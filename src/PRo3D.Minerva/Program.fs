module superduperHarri

open System
open Suave

open Aardvark
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardium
open Aardvark.Application.Slim
open Aardvark.UI
open FSharp.Data
open FSharp.Data.JsonExtensions
open Aardvark.Application
open PRo3D.Minerva
open PRo3D.Minerva.Communication
open System.Threading

[<EntryPoint;STAThread>]
let main argv = 
    Aardvark.Init()
    Aardium.init() 
    
    //test area
    
    //let myMsg = "JsonNetworkCommand 254 {\"JsonCommand\":[{\"Name\":\"MinervaReplaceSelection\"},{\"Param\":{\"Names\":[\"IdList\"],\"NumOfLists\":1,\"NumOfEntries\":3}},{\"Data\":{\"IdList\":[\"FAB_517686998RADLF0542202FHAZ00323M1\",\"FLB_517686998RADLF0542202FHAZ00323M1\",\"FRB_517686998RADLF0542202FHAZ00323M1\"]}}]}"
    //let bla = 
    //  myMsg 
    //    |> PRo3D.Minerva.Communication2.JsonNetworkCommand.removeHeader
    //    |> PRo3D.Minerva.Communication2.JsonNetworkCommand.fromJson
    //Log.line "%A" bla
    
    use app = new OpenGlApplication()
    let runtime = app.Runtime    
    
    let cts = new CancellationTokenSource()
    
    // let messagingMailbox = MailboxProcessor.Start(App.messagingMailbox cts, cts.Token) 
    
    let mainApp = PRo3D.Minerva.MinervaApp.start()
    
    //let s = { MailboxState.empty with update = mainApp.update Guid.Empty }
    //MailboxAction.InitMailboxState s |> messagingMailbox.Post
    
    WebPart.startServerLocalhost 1776 [
          MutableApp.toWebPart' runtime false mainApp
        //  Reflection.assemblyWebPart typeof<EmbeddedResources>.Assembly
          Suave.Files.browseHome        
    ] |> ignore
    
    Aardium.run {
          url "http://localhost:1776/"   //"http://localhost:4321/?page=main"
          width  400
          height 800
          debug true
          title "Minerva 0.0.1"
      }
    
    0



