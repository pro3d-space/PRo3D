namespace PRo3D.Minerva.Communication

//type Communicator =
//  {
  
//  }

module Communicator =
  
  open System.Threading
  open Server
  open Client    
  open PRo3D.Minerva.Communication.JsonNetworkCommand
  
  let genRandomNumbers count =
      let rnd = System.Random()
      List.init count (fun _ -> ((rnd.Next() % 10000) |> float) / 10000.0 )
  
  let rndCount = 10000
  let rndList = genRandomNumbers rndCount
  
  let mutable next = 0
  let mutable errorCntr = 0
  let errorNumber = 25
  
  let generateRandomNumber() : float =
      let num = rndList.[next]
      if next >= (rndCount-1) then next <- 0
      else next <- next + 1
      if errorCntr >= errorNumber then
          errorCntr <- 0
          num + 1.0 // invalid number --> want to check how visplore handels these
      else
          errorCntr <- errorCntr + 1
          num
  
  type Communicator() =
    
    let address = "127.0.0.1"
    let listeningPort = 50124
    let sendingPort = 50123
  
    let mutable client : Client = Unchecked.defaultof<_>
    let mutable server : Server = Unchecked.defaultof<_>
  
    let mutable cnt = 0
    let mutable evalCommand : JsonCommand -> string = fun _ -> "no eval set"
         
    do
        // construct new client --> to send commands to Visplore
        client <- new Client(address, sendingPort)
  
        // construct new server --> to receive commands from Visplore
        let responder (msg : string) : string = JsonNetworkCommand.toJsonNetworkResponse msg
        let convertEval (msg : string) : string =
            let tmp = msg |> JsonNetworkCommand.fromJson
            let a = tmp|> evalCommand
            a
        server <- new Server(convertEval, responder)
    
    member this.GetClient() = client
  
    member this.Start(cmd) =
        let address = System.Net.IPAddress.Parse(address)
        evalCommand <- cmd
        server.Start(ipAddress = address, port = listeningPort)
    
    member this.Close() =
        server.Close()

  

