/// A STAC catalog for tests and for trying the Image Search without a real one
/// (docs/StacImageSearch.md, "Testing"). Serves the images of one folder: every
/// `<name>.mbi.json` with a `<name>.png` next to it is an item, observed at the time in
/// its name (`..._YYYYMMDD_HHMMSS`), else the file's time. Implements what PRo3D's
/// client uses, nothing more: POST /search (time range, paging through next links;
/// a point query finds only items with footprints) and GET /data/<file>.
///
/// Optionally behind HTTP Basic auth, like the real catalog: accounts that may read,
/// and accounts that are known but refused (403). Every request is logged with
/// whether it carried credentials, so tests can check where they went.
///
/// Standalone: Tests.exe --fake-stac <folder> [--port N] [--user U --password P] [--footprints]
module FakeStacCatalog

open System
open System.Collections.Concurrent
open System.Globalization
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Threading

type Account =
    {
        user     : string
        password : string
        /// false: known, but refused (403)
        allowed  : bool
    }

type Options =
    {
        /// the folder with the sidecars and images
        dataDir    : string
        /// None: open, no sign-in
        accounts   : Option<list<Account>>
        /// true: items have a world-covering footprint and name Dimorphos as target,
        /// so a point query finds them; false: no footprints, like a catalog for a newly
        /// visited body (PRo3D falls back to checking by pointing)
        footprints : bool
        /// items per page, at most: small, so tests go through the next links
        pageSize   : int
        /// put before every item id: a test of its own gets ids (and download cache
        /// folders) of its own
        idPrefix   : string
        /// 0: any free port
        port       : int
    }

let options (dataDir : string) =
    { dataDir = dataDir; accounts = None; footprints = false; pageSize = 3; idPrefix = ""; port = 0 }

/// One request as the catalog saw it.
type Request =
    {
        method        : string
        path          : string
        /// it carried an Authorization header
        authorized    : bool
        /// the answer's status code
        status        : int
    }

type private Item = { name : string; time : DateTime }

let private itemsOf (dir : string) =
    let stamp = Regex(@"(\d{8})_(\d{6})$")
    Directory.GetFiles(dir, "*.mbi.json")
    |> Array.choose (fun mbi ->
        let file = Path.GetFileName mbi
        let name = file.Substring(0, file.Length - ".mbi.json".Length)
        if File.Exists(Path.Combine(dir, name + ".png")) then
            let m = stamp.Match name
            let time =
                if m.Success then
                    DateTime.ParseExact(m.Groups.[1].Value + m.Groups.[2].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal)
                else File.GetLastWriteTimeUtc mbi
            Some { name = name; time = time }
        else None)
    |> Array.sortByDescending (fun i -> i.time)

let private parseTime (s : string) =
    match DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal) with
    | true, t -> Some t
    | _ -> None

/// datetime "a/b", ".." for an open end
let private inInterval (interval : Option<string>) (t : DateTime) =
    match interval with
    | None -> true
    | Some s ->
        match s.Split '/' with
        | [| a; b |] ->
            let after = a = ".." || parseTime a |> Option.forall (fun a -> t >= a)
            let before = b = ".." || parseTime b |> Option.forall (fun b -> t <= b)
            after && before
        | _ -> parseTime s |> Option.forall (fun x -> x = t)

type Server(options : Options, onRequest : Request -> unit) =
    let listener = new TcpListener(IPAddress.Loopback, options.port)
    do listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    let baseUrl = sprintf "http://127.0.0.1:%d" port
    let items = itemsOf options.dataDir
    let requests = ConcurrentQueue<Request>()
    let cts = new CancellationTokenSource()

    let respond (stream : Stream) (status : int) (reason : string) (headers : list<string * string>) (body : byte[]) =
        let head = StringBuilder()
        head.AppendFormat("HTTP/1.1 {0} {1}\r\n", status, reason) |> ignore
        for (k, v) in headers do head.AppendFormat("{0}: {1}\r\n", k, v) |> ignore
        head.AppendFormat("Content-Length: {0}\r\nConnection: close\r\n\r\n", body.Length) |> ignore
        let h = Encoding.ASCII.GetBytes(head.ToString())
        stream.Write(h, 0, h.Length)
        stream.Write(body, 0, body.Length)
        stream.Flush()

    let json (stream : Stream) (node : JsonNode) =
        respond stream 200 "OK" [ "Content-Type", "application/geo+json" ] (Encoding.UTF8.GetBytes(node.ToJsonString()))

    let text (stream : Stream) (status : int) (reason : string) (headers : list<string * string>) (message : string) =
        respond stream status reason headers (Encoding.UTF8.GetBytes message)

    /// 200 (go on), 401 or 403
    let check (authorization : Option<string>) =
        match options.accounts with
        | None -> 200
        | Some accounts ->
            let given =
                match authorization with
                | Some a when a.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ->
                    try
                        let plain = Encoding.UTF8.GetString(Convert.FromBase64String(a.Substring 6))
                        match plain.IndexOf ':' with
                        | -1 -> None
                        | i -> Some (plain.Substring(0, i), plain.Substring(i + 1))
                    with _ -> None
                | _ -> None
            match given with
            | Some (user, password) ->
                match accounts |> List.tryFind (fun a -> a.user = user && a.password = password) with
                | Some a when a.allowed -> 200
                | Some _ -> 403
                | None -> 401
            | None -> 401

    let feature (item : Item) =
        let id = options.idPrefix + item.name
        let href (file : string) = sprintf "%s/data/%s" baseUrl (Uri.EscapeDataString file)
        let asset (file : string) = JsonObject(dict [ "href", JsonValue.Create(href file) :> JsonNode ])
        let properties = JsonObject()
        properties.["datetime"] <- JsonValue.Create(item.time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
        properties.["title"] <- JsonValue.Create(item.name)
        properties.["instruments"] <- JsonArray(JsonValue.Create "afc1")
        if options.footprints then properties.["ssys:targets"] <- JsonArray(JsonValue.Create "dimorphos")
        let geometry : JsonNode =
            if options.footprints then
                JsonNode.Parse """{"type":"Polygon","coordinates":[[[-180,-90],[180,-90],[180,90],[-180,90],[-180,-90]]]}"""
            else null
        let f = JsonObject()
        f.["type"] <- JsonValue.Create "Feature"
        f.["stac_version"] <- JsonValue.Create "1.0.0"
        f.["id"] <- JsonValue.Create id
        f.["collection"] <- JsonValue.Create "fake_afc"
        f.["geometry"] <- geometry
        f.["properties"] <- properties
        let assets = JsonObject()
        assets.["mbi"] <- asset (item.name + ".mbi.json")
        assets.["image"] <- asset (item.name + ".png")
        if File.Exists(Path.Combine(options.dataDir, item.name + ".png.json")) then
            assets.["image_metadata"] <- asset (item.name + ".png.json")
        assets.["overview"] <- asset (item.name + ".png")
        f.["assets"] <- assets
        f

    let search (stream : Stream) (body : string) =
        let request = try JsonNode.Parse(if String.IsNullOrWhiteSpace body then "{}" else body) :?> JsonObject with _ -> JsonObject()
        let intValue (key : string) (fallback : int) =
            match request.[key] with
            | null -> fallback
            | v -> try v.GetValue<int>() with _ -> fallback
        let limit = intValue "limit" 10 |> max 1
        let offset = intValue "token" 0 |> max 0
        let interval = match request.["datetime"] with null -> None | v -> Some (v.GetValue<string>())
        let pointQuery = not (isNull request.["intersects"])
        let matching =
            items
            |> Array.filter (fun i -> inInterval interval i.time)
            // a point query matches footprints, and there are none without them
            |> Array.filter (fun _ -> options.footprints || not pointQuery)
        let page = matching |> Array.skip (min offset matching.Length) |> Array.truncate (min limit options.pageSize)
        let answer = JsonObject()
        answer.["type"] <- JsonValue.Create "FeatureCollection"
        answer.["numberMatched"] <- JsonValue.Create matching.Length
        answer.["features"] <- JsonArray(page |> Array.map (fun i -> feature i :> JsonNode))
        let links = JsonArray()
        if offset + page.Length < matching.Length then
            let next = request.DeepClone() :?> JsonObject
            next.["token"] <- JsonValue.Create(offset + page.Length)
            let link = JsonObject()
            link.["rel"] <- JsonValue.Create "next"
            link.["href"] <- JsonValue.Create(baseUrl + "/api/search")
            link.["method"] <- JsonValue.Create "POST"
            link.["body"] <- next
            links.Add link
        answer.["links"] <- links
        json stream answer

    let file (stream : Stream) (name : string) =
        let name = Uri.UnescapeDataString name
        // a plain file name in the folder, nothing else
        if name = "" || name.Contains "/" || name.Contains "\\" || name.Contains ".." then
            text stream 404 "Not Found" [] "not found"
        else
            let path = Path.Combine(options.dataDir, name)
            if File.Exists path then
                let mime =
                    if name.EndsWith ".png" then "image/png"
                    elif name.EndsWith ".json" then "application/json"
                    else "application/octet-stream"
                respond stream 200 "OK" [ "Content-Type", mime ] (File.ReadAllBytes path)
            else text stream 404 "Not Found" [] "not found"

    let serveOne (client : TcpClient) =
        Tasks.Task.Run(fun () ->
            try
                use client = client
                use stream = client.GetStream()
                // the head, then a body of Content-Length bytes
                let buffer = Array.zeroCreate<byte> 8192
                let received = new MemoryStream()
                let mutable headEnd = -1
                while headEnd < 0 do
                    let n = stream.Read(buffer, 0, buffer.Length)
                    if n = 0 then failwith "closed"
                    received.Write(buffer, 0, n)
                    let all = Encoding.ASCII.GetString(received.ToArray())
                    headEnd <- all.IndexOf "\r\n\r\n"
                let bytes = received.ToArray()
                let head = Encoding.ASCII.GetString(bytes, 0, headEnd)
                let lines = head.Split("\r\n")
                let requestLine = lines.[0].Split(' ')
                let method, target = requestLine.[0], requestLine.[1]
                let header (name : string) =
                    lines |> Array.tryPick (fun l ->
                        match l.IndexOf ':' with
                        | i when i > 0 && l.Substring(0, i).Trim().Equals(name, StringComparison.OrdinalIgnoreCase) -> Some (l.Substring(i + 1).Trim())
                        | _ -> None)
                let length = header "Content-Length" |> Option.bind (fun v -> match Int32.TryParse v with | true, n -> Some n | _ -> None) |> Option.defaultValue 0
                let body = new MemoryStream()
                body.Write(bytes, headEnd + 4, bytes.Length - headEnd - 4)
                while body.Length < int64 length do
                    let n = stream.Read(buffer, 0, buffer.Length)
                    if n = 0 then failwith "closed"
                    body.Write(buffer, 0, n)
                let bodyText = Encoding.UTF8.GetString(body.ToArray())
                let path = target.Split('?').[0]
                let authorization = header "Authorization"
                let status =
                    match check authorization with
                    | 401 ->
                        text stream 401 "Unauthorized" [ "WWW-Authenticate", "Basic realm=\"Fake catalog\"" ] "sign in"
                        401
                    | 403 ->
                        text stream 403 "Forbidden" [] "not for you"
                        403
                    | _ ->
                        match method, path with
                        | "POST", "/api/search" -> search stream bodyText; 200
                        | "GET", p when p.StartsWith "/data/" -> file stream (p.Substring 6); 200
                        | _ -> text stream 404 "Not Found" [] "not found"; 404
                let r = { method = method; path = path; authorized = authorization.IsSome; status = status }
                requests.Enqueue r
                onRequest r
            with _ -> ()) |> ignore

    let loop =
        Tasks.Task.Run(fun () ->
            try
                while not cts.IsCancellationRequested do
                    serveOne (listener.AcceptTcpClient())
            with _ -> ())

    new(options : Options) = new Server(options, ignore)

    /// The API root, as typed into PRo3D's Catalog field.
    member _.ApiUrl = baseUrl + "/api"
    member _.BaseUrl = baseUrl
    member _.ItemCount = items.Length
    member _.Requests = requests.ToArray()

    interface IDisposable with
        member _.Dispose() =
            cts.Cancel()
            listener.Stop()

/// Tests.exe --fake-stac <folder> [--port N] [--user U --password P] [--footprints]:
/// serves until stdin closes or Ctrl+C. Prints the API root on the first line.
let runStandalone (args : list<string>) =
    let rec parse (o : Options) (user : Option<string>) (password : Option<string>) args =
        match args with
        | "--port" :: p :: rest -> parse { o with port = int p } user password rest
        | "--user" :: u :: rest -> parse o (Some u) password rest
        | "--password" :: p :: rest -> parse o user (Some p) rest
        | "--footprints" :: rest -> parse { o with footprints = true } user password rest
        | _ :: rest -> parse o user password rest
        | [] ->
            match user, password with
            | Some u, Some p -> { o with accounts = Some [ { user = u; password = p; allowed = true } ] }
            | _ -> o
    match args with
    | dir :: rest when Directory.Exists dir ->
        let o = parse (options dir) None None rest
        // one line per request, for a test watching from outside: where did credentials go
        let log (r : Request) = printfn "[request] %s %s %d %s" r.method r.path r.status (if r.authorized then "signed" else "unsigned"); Console.Out.Flush()
        use server = new Server(o, log)
        printfn "%s" server.ApiUrl
        printfn "fake STAC catalog: %d item(s) from %s%s" server.ItemCount dir
            (match o.accounts with Some _ -> ", sign-in required" | None -> ", open")
        Console.Out.Flush()
        // until stdin closes (a parent process ends) or Ctrl+C
        try Console.In.ReadToEnd() |> ignore with _ -> ()
        0
    | _ ->
        eprintfn "usage: --fake-stac <folder with *.mbi.json and *.png> [--port N] [--user U --password P] [--footprints]"
        1
