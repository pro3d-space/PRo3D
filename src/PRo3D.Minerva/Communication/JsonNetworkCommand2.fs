namespace PRo3D.Minerva.Communication

open Aardvark.Base
open FSharp.Data
open FSharp.Data.JsonExtensions

module JsonNetworkCommand =

  type Channel = IdList | Positions | Colors

  type Data = 
    {
      idList    : list<string>
      positions : list<V2d>
      colors    : list<C4b>
    }

  type Params =
    {
        names        : list<Channel>
        numOfLists   : int
        numOfEntries : int
    
        image           : option<string>
        imageResolution : option<V2i>
    
        messageID       : string
    }

  type JsonCommand = 
    {
      name       : string
      parameters : Params
      data       : Data
    }

  let attachHeader (input : string) : string =
    failwith ""

  let removeHeader (input : string) : string =
    let headerLength = input.IndexOf(' ', input.IndexOf(' ') + 1)
    Log.line "[Minerva] HeaderLength %d" headerLength
    input.Substring(headerLength + 1)  

  let parseData (channels : list<Channel>)(json : JsonValue) : Data = 
    let idList = 
      if channels |> List.contains Channel.IdList then
        (json?IdList).AsArray() |> Array.map(fun x -> x.AsString()) |> Array.toList
      else []

    //let positions = 
    //  if channels |> List.contains Channel.Positions then
    //    (json?positions).AsArray() |> Array.toList |> List.map(fun x -> x) //pick in threes
    //  else []    

    //let colors = 
    //  if channels |> List.contains Channel.Colors then
    //    (json?colors).AsArray() |> Array.map string |> Array.toList
    //  else []

    {
      idList    = idList
      positions = []
      colors    = []
    }
  
  let parseChannel (c : string) : Channel =
    match c with 
    | "IdList"    -> Channel.IdList
    | "Positions" -> Channel.Positions
    | "Colors"    -> Channel.Colors
    | _ -> c |> sprintf "[Minervar] channel %s is unknown" |> failwith

  let parseParams (json : JsonValue) : Params = 
    let names = (json?Names).AsArray() |> Array.map(fun x -> x.AsString()) |> Array.toList

    {
      names        = names |> List.map parseChannel
      numOfLists   = (json?NumOfLists).AsInteger()
      numOfEntries = (json?NumOfEntries).AsInteger()

      image = None
      imageResolution = None
      messageID = ""
    }
    
  let fromJson (json : string) : JsonCommand =
    let json = ((JsonValue.Parse(json)))
    let root = (json?JsonCommand).AsArray()

    let name       = (root.[0]?Name).AsString()
    let parameters = root.[1]?Param
    let data       = root.[2]?Data

    let parameters = parameters |> parseParams

    {
      name       = name
      parameters = parameters
      data       = data |> parseData parameters.names
    }

  let createJsonName (name : string) = 
    let jsonName = "{ \"Name\": \"" + name + "\"}"
    jsonName
    
  let createJsonParam (par : Params) =
    let mutable jsonParam = "{ \"Param\": {\"Names\": ["
    for n in par.names do 
        jsonParam <- jsonParam + "\"" + n.ToString() + "\","
    jsonParam <- jsonParam.Remove(jsonParam.Length - 1) + "],"
    jsonParam <- jsonParam + "\"NumOfLists\": " + (par.numOfLists |> string) + ", "
    jsonParam <- jsonParam + "\"NumOfEntries\": " + (par.numOfEntries |> string) + ", "
    
    match par.image with
    | Some(s) ->  jsonParam <- jsonParam + "\"Image\": \"" + s + "\", "
    | None -> ()
    
    match par.imageResolution with
    | Some(res) -> jsonParam <- jsonParam + "\"ImageResolution\": [" + (res.X |> string) + "," + (res.Y |> string) + "],"
    | None -> ()
    
    jsonParam <- jsonParam + "\"MessageID\": \"" + par.messageID  + "\"}}"
    
    jsonParam


  let createJsonData (par : Params) (data : Data) =
    let mutable jsonData = "{\"Data\": {\""
    
    if par.names |> List.contains Channel.IdList then
      jsonData <- jsonData + Channel.IdList.ToString() + "\": ["    
      for id in data.idList do
          jsonData <- jsonData + "\"" + (id) + "\","
      jsonData <- jsonData.Remove(jsonData.Length - 1) + "]"
    
    if par.names |> List.contains Channel.Positions then
      jsonData <- jsonData + ", \"" + Channel.Positions.ToString()  + "\": ["
      for pos in data.positions do
          jsonData <- jsonData + (pos.X |> string) + "," + (pos.Y |> string) + ","
      jsonData <- jsonData.Remove(jsonData.Length - 1) + "]"
           
    if par.names |> List.contains Channel.Colors then
      jsonData <- jsonData + ", \"" + Channel.Colors.ToString()  + "\": ["
      for c in data.colors do
          jsonData <- jsonData + (c.R |> string) + "," + (c.G |> string) + "," + (c.B |> string) + "," + (c.A |> string) + ","
      jsonData <- jsonData.Remove(jsonData.Length - 1) + "]"
        
    jsonData <- jsonData + "}}"
    jsonData
    
  
  open Newtonsoft.Json
  open System.Text
  open System.IO

  type JsonTextWriter with
    member x.Object(action : unit -> unit) =
        x.WriteStartObject() 
        action()
        x.WriteEndObject() 
    member x.Array(action : unit -> unit) =
        x.WriteStartArray() 
        action()
        x.WriteEndArray() 
        

  let toJson (cmd : JsonCommand) : string =
    let sb = new StringBuilder()
    let sw = new StringWriter(sb)
    use writer = new JsonTextWriter(sw)
    
    writer.Formatting <- Formatting.None

    writer.Object(fun () ->
        writer.WritePropertyName("JsonCommand")
        writer.Array (fun () ->
            writer.Object(fun () -> //name
                writer.WritePropertyName("Name")
                writer.WriteValue(cmd.name)
            )
            writer.Object(fun () -> //param
                writer.WritePropertyName("Param")
                writer.Object(fun () ->
                    writer.WritePropertyName "Names"
                    writer.Array (fun () ->
                        for n in cmd.parameters.names do
                            writer.WriteValue (string n)
                    )
                    writer.WritePropertyName "NumOfLists"
                    writer.WriteValue cmd.parameters.numOfLists
                    writer.WritePropertyName "NumOfEntries"
                    writer.WriteValue cmd.parameters.numOfEntries

                    // missing image and image resolution
                    match cmd.parameters.image with
                    | Some(image) ->
                        writer.WritePropertyName "Image"
                        writer.WriteValue image
                    | None -> ()

                    match cmd.parameters.imageResolution with
                    | Some(resolution) ->
                        writer.WritePropertyName "ImageResolution"
                        writer.Array (fun () ->
                            writer.WriteValue (string resolution.X)
                            writer.WriteValue (string resolution.Y)
                        )
                    | None -> ()
                )
            )
            writer.Object(fun () -> //data
                writer.WritePropertyName("IdList")
                writer.Array (fun () ->
                    for c in cmd.data.idList do
                        writer.WriteValue c
                )
                writer.WritePropertyName("Positions")
                writer.Array (fun () ->
                    for c in cmd.data.positions do
                        writer.WriteValue c.X
                        writer.WriteValue c.Y
                )
                writer.WritePropertyName("Colors")
                writer.Array (fun () ->
                    for c in cmd.data.colors do
                        writer.WriteValue c.R
                        writer.WriteValue c.G
                        writer.WriteValue c.B
                        writer.WriteValue c.A
                )                
            )
        )
    )

    //let cmdName = cmd.name |> createJsonName
    //let cmdPara = cmd.parameters |> createJsonParam
    //let cmdData = cmd.data |> createJsonData cmd.parameters

    //let header = "JsonNetworkCommand"
    //let command = "{\"JsonCommand\": [" + cmdName + ", " + cmdPara + ", " + cmdData + "]}"
    //let numberOfBytes = command.Length
    //let message = header + " " + (numberOfBytes |> string) + " " + command
    //message
    let jsonCommand = sb.ToString()
    let header = "JsonNetworkCommand"
    let numberOfBytes = jsonCommand.Length
    let msg = System.String.Format ("{0} {1} {2}", header, numberOfBytes, jsonCommand)
    msg

  let toJsonNetworkResponse (msg : string) : string = 
    let cmdName = "{ \"Name\": \"" + msg + "\"}"
    let cmdPara = "{ \"Param\": \"{}}"
    let cmdData = "{ \"Data\": \"{}}"

    let header = "JsonNetworkResponse"
    let command = "{\"JsonCommand\": [" + cmdName + ", " + cmdPara + ", " + cmdData + "]}"
    let numberOfBytes = command.Length
    let message = header + " " + (numberOfBytes |> string) + " " + command
    message