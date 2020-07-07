module Server

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Text

open Suave
open SuaveConfig
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors

open Suave.Json
open System.Runtime.Serialization
open Suave.Logging


type Socket with
  member socket.AsyncAccept() = Async.FromBeginEnd(socket.BeginAccept, socket.EndAccept)

  member socket.AsyncReceive(buffer:byte[], ?offset, ?count) =
    let offset = defaultArg offset 0
    let count = defaultArg count buffer.Length
    let beginReceive(b,o,c,cb,s) = socket.BeginReceive(b,o,c,SocketFlags.None,cb,s)
    Async.FromBeginEnd(buffer, offset, count, beginReceive, socket.EndReceive)

  member socket.AsyncSend(buffer:byte[], ?offset, ?count) =
    let offset = defaultArg offset 0
    let count = defaultArg count buffer.Length
    let beginSend(b,o,c,cb,s) = socket.BeginSend(b,o,c,SocketFlags.None,cb,s)
    Async.FromBeginEnd(buffer, offset, count, beginSend, socket.EndSend)


let (|IntegerParse|_|) (s : string) =
    match System.Int32.TryParse s with
        | (true,v) -> Some v
        | _ -> None

open Aardvark.Base
type Server(convertEval : string -> string, responder : string -> string) =
    
    let mutable internalServer : IDisposable = Unchecked.defaultof<_>
    let mutable listener : TcpListener = Unchecked.defaultof<_>
    
    member x.ReceivedData : string = ""

    member x.Start(?ipAddress, ?port) =
        
        let ipAddress = defaultArg ipAddress IPAddress.Any
        let port = defaultArg port 80
        let endpoint = IPEndPoint(ipAddress, port)
        let cts = new CancellationTokenSource()

        listener <- new TcpListener(endpoint)
        //listener.Bind(endpoint)
        listener.Start()
        Log.line "[Minerva] Started listening on port %d" port
    
        let rec loop() = async {
            Log.line "[Minerva] Waiting for request ..."
            let! (socket : Socket) = listener.AcceptSocketAsync()
            Log.line "[Minerva] Received request"

            let headerBuffer : byte[] = Array.zeroCreate 100

            try
              //header
              let mutable position = 0
              let mutable spaces = 0
              let mutable inHeader = true
              while inHeader && position < 100 do
                let! readBytes = socket.AsyncReceive(headerBuffer, position, 1)
                if readBytes <> 1 then failwithf "invalid header, so far: %s" (Encoding.ASCII.GetString(headerBuffer, 0, position))
                let c = Convert.ToChar(headerBuffer.[position]);
                if c = ' ' then 
                    spaces <- spaces + 1
                if spaces = 2 then inHeader <- false
                else position <- position + 1
              
              if inHeader then failwith "header rage"


              // stream behind space. e.g. JsonNetworkCommand 10 xxx-payload-xxx....
              let header = Encoding.ASCII.GetString(headerBuffer, 0, position)

              let messageLength = 
                  match header.Split(' ') with
                    | [|"JsonNetworkCommand"; IntegerParse len|] -> len
                    | _ ->  failwithf "could not parse header: %s" header

              let payloadSize = messageLength
              let mutable remaining = payloadSize
              let resultBuffer = Array.zeroCreate remaining

              let mutable read = 0
              while remaining > 0 do
                let! received =  socket.AsyncReceive(resultBuffer, read, remaining)
                read <- read + received
                remaining <- remaining - received
                
              // invariant: remaining = 0, buffer is full
              let fullString =  Encoding.ASCII.GetString(resultBuffer, 0, payloadSize)
              //let parsed = PRo3D.Minerva.Communication.JsonNetworkCommand.fromJson fullString

              //Log.line "[Minerva] %A" parsed

              
              //conversion and response
              fullString |> convertEval |> ignore //|> responder
              //
              //let response = Encoding.ASCII.GetBytes(msg)
              
              //try
              //    let! bytesSent = socket.AsyncSend(response)
              //    Log.line "[Minerva] Sent response: %A (%A)" msg bytesSent
              //    socket.Shutdown(SocketShutdown.Both)              
              //with e -> Log.line "[Minerva] An error occurred responding: %s" e.Message
            with e -> Log.line "[Minerva] An error occurred receiving: %s" e.Message

            socket.Shutdown(SocketShutdown.Both)
            socket.Close()

            return! loop() }

        Async.Start(loop(), cancellationToken = cts.Token)
        internalServer <- { new IDisposable with member x.Dispose() = cts.Cancel(); listener.Stop() }
    
    member x.Close() = 
        internalServer.Dispose()

////////////////////////////////////////////////////////////////////////////////////////////////////
[<DataContract>]
type Calc =
   { 
      [<field: DataMember(Name = "a")>]
      a : int;
      [<field: DataMember(Name = "b")>]
      b : int;
   }
 

 [<DataContract>]
type Result =
   { 
      [<field: DataMember(Name = "result")>]
      result : int;
   }

type WebSocket (address : string, port : int) = 

    let add =
       request (fun r ->
           match r.queryParam "a" with
           | Choice1Of2 a -> 
               match r.queryParam "b" with
               | Choice1Of2 b ->    
                    let a = System.Int32.Parse a
                    let b = System.Int32.Parse b
                    OK (sprintf "a: %d b: %d -> %d" a b (a + b))
               | Choice2Of2 msg -> BAD_REQUEST msg
           | Choice2Of2 msg -> BAD_REQUEST msg)

    let app =
        choose
            [ GET >=> choose
                [ path "/hello" >=> OK "Hello GET"
                  path "/goodbye" >=> OK "Good bye GET" 
                  path "/testAdd" >=> add 

                ]
              POST >=> choose
                [ path "/hello" >=> 
                    OK "Hello POST"
                  path "/goodbye" >=> OK "Good bye POST" 
                  path "/urdar" >=> mapJson (fun (calc:Calc) -> { result = calc.a + calc.b })
                ] 
            ]
     
    let mimeTypes =
        Writers.defaultMimeTypesMap
        @@ (function | ".avi" -> Writers.createMimeType "video/avi" false | _ -> None)
    

    /////////////////////////////////////////////////////////////////////
    let logger = Targets.create Verbose [||]

    /// Or a little more elaborated:

    //let loggingOptions =
    //    { Literate.LiterateOptions.create() with
    //        getLogLevelText = function Verbose->"V" | Debug->"D" | Info->"I" | Warn->"W" | Error->"E" | Fatal->"F" }

    //let logger =
    //    LiterateConsoleTarget(
    //                            name = [|"Suave";"Examples";"Example"|],
    //                            minLevel = Verbose,
    //                            options = loggingOptions,
    //                            outputTemplate = "[{level}] {timestampUtc:o} {message} [{source}]{exceptions}"
    //                            ) :> Logger
    /////////////////////////////////////////////////////////////////////
    member x.Start() =
        let newConfig =
            { bindings              = [ HttpBinding.createSimple HTTP address port]
              serverKey             = Utils.Crypto.generateKey HttpRuntime.ServerKeyLength
              errorHandler          = defaultErrorHandler
              listenTimeout         = TimeSpan.FromMilliseconds 2000.
              cancellationToken     = Async.DefaultCancellationToken
              bufferSize            = 2048
              maxOps                = 100
              autoGrow              = true
              mimeTypesMap          = mimeTypes
              homeFolder            = None
              compressedFilesFolder = None
              logger                = logger
              tcpServerFactory      = new DefaultTcpServerFactory()
              cookieSerialiser      = new BinaryFormatterSerialiser()
              tlsProvider           = new DefaultTlsProvider()
              hideHeader            = false
              maxContentLength = 1000000 }

        startWebServer newConfig app
        
