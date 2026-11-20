namespace PRo3D.GIS

open System
open System.Globalization
open System.IO
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

open Aardvark.Base
open PRo3D.Base
open PRo3D.Core
open PRo3D.SPICE
open PRo3D.ImageMapping

/// Live search of a STAC catalog (server from PRO3D_STAC_URL or the GIS tab) for instrument
/// images that contain a surface point, and fetching an item's files so the
/// projected-image library can load it. See docs/StacImageSearch.md.
///
/// The server narrows the catalog by footprint (a lon/lat polygon per item); the
/// footprints are coarse (about a dozen vertices), so every hit is checked locally
/// by projecting the point with the image's own pointing -- the same projector the
/// projection shader uses.
module StacSearch =

    /// What one search asks the server for.
    type Query =
        {
            /// footprint contains this point: planetocentric, east-positive lon/lat,
            /// degrees -- the footprints' convention (measured against the spacecraft
            /// geometry, docs/StacImageSearch.md). None: no spatial filter.
            point  : Option<V2d>
            /// the items' ssys:targets contain this, lower case, e.g. "mars".
            /// None: no target filter.
            target : Option<string>
            /// observation time range, UTC, inclusive; None = open
            from   : Option<DateTime>
            until  : Option<DateTime>
            /// distance to the target, km, inclusive (the shared Distance filter);
            /// None = any. Sent as hera:target_distance (AU), widened by
            /// `distanceTolerance`: a pre-selection only, the exact check is PRo3D's.
            distanceKm : Option<float * float>
            limit  : int
        }

    /// The server's hera:target_distance is in astronomical units.
    [<Literal>]
    let kmPerAu = 149597870.7

    /// How far the server-side distance range is widened, relative, on each side: the
    /// catalog's "observation target" and the sidecar's TRG_POS need not be the same
    /// body (Didymos vs Dimorphos), and the exact check after download decides anyway.
    [<Literal>]
    let distanceTolerance = 0.05

    /// What the user narrows every search with: the server and the panel's shared
    /// filters (ImageFilter, the same as the Image Library's). The time range goes to
    /// the server; everything is checked again on the downloaded metadata
    /// (ImageFilter.passesValues), so a catalog image passes exactly when it would
    /// pass in the library.
    type Filter =
        {
            serverUrl : string
            from      : Option<DateTime>
            until     : Option<DateTime>
            bounds    : ImageFilter.Bounds
            // SUN INCIDENCE (disabled, see docs/StacImageSearch.md)
            // /// sun incidence at the picked point, degrees, inclusive
            // incidence : Range1d
        }

    /// Whether an image's sun incidence at the picked point passes the filter. An
    /// unknown incidence (no sun position in the sidecar, no normal) passes: the
    /// filter cannot tell, and hiding the image would hide that it exists.
    let incidencePasses (range : Range1d) (incidenceDeg : Option<float>) =
        match incidenceDeg with
        | Some a -> a >= range.Min && a <= range.Max
        | None -> true

    type Item =
        {
            id           : string
            collection   : string
            title        : string
            time         : DateTime
            instrument   : string
            assets       : StacAssets
            /// the item has a footprint (geometry)
            hasFootprint : bool
            /// ssys:targets, lower case; None when the item names none
            targets      : Option<array<string>>
        }

    /// A typed time, UTC: "YYYY-MM-DD" or an ISO 8601 time. A bare date as the end
    /// of a range means the end of that day. Empty: open.
    let parseTime (isEnd : bool) (s : string) : Result<Option<DateTime>, string> =
        let s = s.Trim()
        if s = "" then Ok None
        else
            let styles = DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal
            match DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, styles) with
            | true, d -> Ok (Some (if isEnd then d.AddDays(1.0).AddTicks(-1L) else d))
            | _ ->
                match DateTime.TryParse(s, CultureInfo.InvariantCulture, styles) with
                | true, t -> Ok (Some t)
                | _ -> Result.Error (sprintf "not a time: \"%s\" (YYYY-MM-DD or ISO 8601, UTC)" s)

    let parseTimeRange (from : string) (until : string) : Result<Option<DateTime> * Option<DateTime>, string> =
        match parseTime false from, parseTime true until with
        | Ok (Some f), Ok (Some u) when f > u -> Result.Error "the time range ends before it starts"
        | Ok f, Ok u -> Ok (f, u)
        | Result.Error e, _ | _, Result.Error e -> Result.Error e

    /// The filter for a search: the catalog from the STAC panel, the filters from the
    /// panel's shared Filters section. A Result, so a filter that cannot be used is
    /// reported on the status line (none at the moment: the shared ranges are always
    /// valid).
    let filterOf (s : StacSearchModel) (f : ImageFilter) : Result<Filter, string> =
        let bounds = ImageFilter.bounds f
        let from, until =
            match bounds.time with
            | Some (lo, hi) -> Some (ImageFilter.timeOfKey lo), Some (ImageFilter.timeOfKey hi)
            | None -> None, None
        // SUN INCIDENCE (disabled, see docs/StacImageSearch.md)
        // if s.incidenceMin > s.incidenceMax then Result.Error "the incidence range ends before it starts"
        Ok { serverUrl = s.serverUrl; from = from; until = until; bounds = bounds }

    /// The items worth checking by pointing alone when no footprint contains the
    /// point: those without a footprint (one with a footprint would have been found
    /// by it), naming the body or no target at all.
    let isWithoutFootprintFor (body : string) (item : Item) =
        not item.hasFootprint
        && (match item.targets with
            | None -> true
            | Some targets -> Array.contains (body.ToLowerInvariant()) targets)

    /// Planetocentric east longitude and latitude, degrees, of a body-fixed position.
    /// Longitude in (-180, 180], as GeoJSON wants it.
    let lonLat (p : V3d) : V2d =
        let r = p.Length
        if r = 0.0 then V2d.Zero
        else V2d(atan2 p.Y p.X |> Conversion.DegreesFromRadians,
                 asin (clamp -1.0 1.0 (p.Z / r)) |> Conversion.DegreesFromRadians)

    module Json =

        let private tryString (e : JsonElement) (name : string) =
            match e.TryGetProperty name with
            | true, v when v.ValueKind = JsonValueKind.String -> Some (v.GetString())
            | _ -> None

        let private tryHref (assets : JsonElement) (key : string) =
            match assets.TryGetProperty key with
            | true, a when a.ValueKind = JsonValueKind.Object -> tryString a "href"
            | _ -> None

        let private tryTime (s : string) =
            match DateTime.TryParse(s, CultureInfo.InvariantCulture,
                                    DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal) with
            | true, t -> Some t
            | _ -> None

        /// One feature of a search response. None for items without the two files a
        /// projection needs (a catalog may also list e.g. meshes).
        let tryItem (f : JsonElement) : Option<Item> =
            let props =
                match f.TryGetProperty "properties" with
                | true, p when p.ValueKind = JsonValueKind.Object -> Some p
                | _ -> None
            let assets =
                match f.TryGetProperty "assets" with
                | true, a when a.ValueKind = JsonValueKind.Object -> Some a
                | _ -> None
            match tryString f "id", props, assets with
            | Some id, Some props, Some assets ->
                match tryHref assets "mbi", tryHref assets "image" with
                | Some mbi, Some image ->
                    let instrument =
                        match props.TryGetProperty "instruments" with
                        | true, a when a.ValueKind = JsonValueKind.Array ->
                            let names = ResizeArray()
                            for x in a.EnumerateArray() do
                                if x.ValueKind = JsonValueKind.String then names.Add(x.GetString().ToUpperInvariant())
                            String.Join(", ", names)
                        | _ -> ""
                    let targets =
                        match props.TryGetProperty "ssys:targets" with
                        | true, a when a.ValueKind = JsonValueKind.Array && a.GetArrayLength() > 0 ->
                            let names = ResizeArray()
                            for x in a.EnumerateArray() do
                                if x.ValueKind = JsonValueKind.String then names.Add(x.GetString().ToLowerInvariant())
                            Some (names.ToArray())
                        | _ -> None
                    Some {
                        id         = id
                        collection = tryString f "collection" |> Option.defaultValue ""
                        title      = tryString props "title" |> Option.defaultValue id
                        time       = tryString props "datetime" |> Option.bind tryTime |> Option.defaultValue DateTime.MinValue
                        instrument = instrument
                        assets     =
                            {
                                mbi           = mbi
                                image         = image
                                imageMetadata = tryHref assets "image_metadata"
                                overview      = tryHref assets "overview"
                            }
                        hasFootprint =
                            match f.TryGetProperty "geometry" with
                            | true, g -> g.ValueKind = JsonValueKind.Object
                            | _ -> false
                        targets = targets
                    }
                | _ -> None
            | _ -> None

        /// A search response: the usable items, and how many items the server matched
        /// (numberMatched when it says, else the features returned).
        let parseSearch (json : string) : Result<array<Item> * int, string> =
            try
                use doc = JsonDocument.Parse json
                let root = doc.RootElement
                match root.TryGetProperty "features" with
                | true, features when features.ValueKind = JsonValueKind.Array ->
                    let items = ResizeArray()
                    for f in features.EnumerateArray() do
                        match tryItem f with
                        | Some item -> items.Add item
                        | None -> ()
                    let matched =
                        match root.TryGetProperty "numberMatched" with
                        | true, n when n.ValueKind = JsonValueKind.Number -> n.GetInt32()
                        | _ -> features.GetArrayLength()
                    Ok (items.ToArray(), matched)
                | _ -> Result.Error "the response has no features"
            with e ->
                Result.Error (sprintf "unreadable response: %s" e.Message)

        /// The next page of a search response (STAC API paging): its href, and the
        /// body to POST when the link says so (else a GET). None on the last page.
        let tryNextPage (json : string) : Option<string * Option<string>> =
            try
                use doc = JsonDocument.Parse json
                match doc.RootElement.TryGetProperty "links" with
                | true, links when links.ValueKind = JsonValueKind.Array ->
                    links.EnumerateArray()
                    |> Seq.tryPick (fun l ->
                        match tryString l "rel", tryString l "href" with
                        | Some "next", Some href ->
                            let post = tryString l "method" |> Option.exists (fun m -> m.ToUpperInvariant() = "POST")
                            let body =
                                match l.TryGetProperty "body" with
                                | true, b when post && b.ValueKind = JsonValueKind.Object -> Some (b.GetRawText())
                                | _ -> None
                            if post && body.IsNone then None else Some (href, body)
                        | _ -> None)
                | _ -> None
            with _ -> None

        /// STAC datetime interval, ".." for an open end; None when both are open.
        let datetimeInterval (from : Option<DateTime>) (until : Option<DateTime>) : Option<string> =
            let format (t : Option<DateTime>) =
                match t with
                | Some t -> t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture)
                | None -> ".."
            match from, until with
            | None, None -> None
            | _ -> Some (format from + "/" + format until)

        /// POST /search body: items whose footprint contains the point, whose
        /// targets include the body and that were taken in the time range (each
        /// only when given), newest first.
        let searchBody (q : Query) : string =
            use ms = new MemoryStream()
            (
                use w = new Utf8JsonWriter(ms)
                w.WriteStartObject()
                match q.point with
                | Some p ->
                    w.WriteStartObject "intersects"
                    w.WriteString("type", "Point")
                    w.WriteStartArray "coordinates"
                    w.WriteNumberValue p.X
                    w.WriteNumberValue p.Y
                    w.WriteEndArray()
                    w.WriteEndObject()
                | None -> ()
                match datetimeInterval q.from q.until with
                | Some interval -> w.WriteString("datetime", interval)
                | None -> ()
                w.WriteNumber("limit", q.limit)
                // CQL2 conditions, and-ed: the target, and the distance range (in AU,
                // widened by distanceTolerance -- a pre-selection, see Query)
                let targetClause (target : string) () =
                    w.WriteStartObject()
                    w.WriteString("op", "a_contains")
                    w.WriteStartArray "args"
                    w.WriteStartObject()
                    w.WriteString("property", "ssys:targets")
                    w.WriteEndObject()
                    w.WriteStartArray()
                    w.WriteStringValue(target.ToLowerInvariant())
                    w.WriteEndArray()
                    w.WriteEndArray()
                    w.WriteEndObject()
                let comparison (op : string) (property : string) (value : float) () =
                    w.WriteStartObject()
                    w.WriteString("op", op)
                    w.WriteStartArray "args"
                    w.WriteStartObject()
                    w.WriteString("property", property)
                    w.WriteEndObject()
                    w.WriteNumberValue value
                    w.WriteEndArray()
                    w.WriteEndObject()
                let clauses =
                    [
                        match q.target with
                        | Some target -> yield targetClause target
                        | None -> ()
                        match q.distanceKm with
                        | Some (lo, hi) ->
                            yield comparison ">=" "hera:target_distance" (lo * (1.0 - distanceTolerance) / kmPerAu)
                            yield comparison "<=" "hera:target_distance" (hi * (1.0 + distanceTolerance) / kmPerAu)
                        | None -> ()
                    ]
                match clauses with
                | [] -> ()
                | [ single ] ->
                    w.WriteString("filter-lang", "cql2-json")
                    w.WritePropertyName "filter"
                    single ()
                | several ->
                    w.WriteString("filter-lang", "cql2-json")
                    w.WriteStartObject "filter"
                    w.WriteString("op", "and")
                    w.WriteStartArray "args"
                    for clause in several do clause ()
                    w.WriteEndArray()
                    w.WriteEndObject()
                // TODO instruments: send the Instrument filter as
                // {"op":"a_overlaps","args":[{"property":"instruments"},[...]]} once the
                // catalog's instrument spellings are mapped to the sidecars' INSTRUME
                // names; until then PRo3D applies it after download only
                w.WriteStartArray "sortby"
                w.WriteStartObject()
                w.WriteString("field", "properties.datetime")
                w.WriteString("direction", "desc")
                w.WriteEndObject()
                w.WriteEndArray()
                w.WriteEndObject()
            )
            Encoding.UTF8.GetString(ms.ToArray())

    /// One client for the process, as HttpClient is meant to be used. Credentials are
    /// not on the client but added per request (`send`), for the catalog's host only.
    let private http =
        lazy (
            let c = new HttpClient(Timeout = TimeSpan.FromSeconds 60.0)
            c.DefaultRequestHeaders.UserAgent.ParseAdd "PRo3D"
            c)

    /// Sidecar downloads of one search run in parallel, but not a hundred at once.
    let private downloadSlots = new SemaphoreSlim(8)

    /// scheme://host:port, lower case: what credentials are bound to.
    let originOf (url : string) : Option<string> =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri when uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps ->
            Some (uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant())
        | _ -> None

    /// HTTP Basic: user name and password, as the catalog's sign-in asks for them.
    let basicAuth (user : string) (password : Secret) =
        Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + Secret.reveal password)))

    /// The session's sign-in: the catalog's origin and the header for it. In memory
    /// only; never written anywhere, never logged.
    let mutable private credentials : Option<string * Headers.AuthenticationHeaderValue> = None
    let private credentialsGate = obj()

    let private credentialsFor (url : string) =
        lock credentialsGate (fun () ->
            match credentials, originOf url with
            | Some (origin, header), Some o when o = origin -> Some header
            | _ -> None)

    /// From now on, requests to this catalog's host carry the sign-in. Requests to
    /// any other host (an asset link elsewhere) never do; nor does a redirect, which
    /// HttpClient strips the header from.
    let signIn (serverUrl : string) (header : Headers.AuthenticationHeaderValue) =
        lock credentialsGate (fun () ->
            credentials <- originOf serverUrl |> Option.map (fun o -> o, header))

    let signOut () = lock credentialsGate (fun () -> credentials <- None)

    /// Credentials only go over https, or to this computer (a local test catalog).
    let allowsCredentials (url : string) =
        match Uri.TryCreate(url.Trim(), UriKind.Absolute) with
        | true, uri -> uri.Scheme = Uri.UriSchemeHttps || uri.IsLoopback
        | _ -> false

    /// What a 401 turns into: the catalog wants a sign-in, or a new one (the
    /// password changed, the account was closed). The panel reopens the dialog on it.
    [<Literal>]
    let signInNeeded = "the catalog asks to sign in again"

    let isSignInNeeded (error : string) = error = signInNeeded

    /// Send, with the session's credentials when the request goes to the catalog.
    let private send (request : HttpRequestMessage) (completion : HttpCompletionOption) (ct : CancellationToken) =
        match credentialsFor (string request.RequestUri) with
        | Some header -> request.Headers.Authorization <- header
        | None -> ()
        http.Value.SendAsync(request, completion, ct)

    let private errorOf (url : string) (response : HttpResponseMessage) =
        if response.StatusCode = Net.HttpStatusCode.Unauthorized then signInNeeded
        else sprintf "%s answered %d %s" url (int response.StatusCode) response.ReasonPhrase

    /// One page: POST with a body, or GET.
    let private searchPage (url : string) (body : Option<string>) : Task<Result<string, string>> =
        task {
            try
                use request =
                    match body with
                    | Some body ->
                        new HttpRequestMessage(HttpMethod.Post, url, Content = new StringContent(body, Encoding.UTF8, "application/json"))
                    | None -> new HttpRequestMessage(HttpMethod.Get, url)
                use! response = send request HttpCompletionOption.ResponseContentRead CancellationToken.None
                let! text = response.Content.ReadAsStringAsync()
                if response.IsSuccessStatusCode then return Ok text
                else return Result.Error (errorOf url response)
            with e ->
                return Result.Error (sprintf "%s: %s" url e.Message)
        }

    /// Ask the catalog for one item: is it there, is it a STAC catalog, does it
    /// want a sign-in, does it take this one. Sends `header` (or nothing), not the
    /// session's credentials: a wrong password must not replace a working one.
    let probe (serverUrl : string) (header : Option<Headers.AuthenticationHeaderValue>) : Task<StacProbe> =
        task {
            if String.IsNullOrWhiteSpace serverUrl then
                return ProbeFailed "no catalog set (the Catalog field, or the PRO3D_STAC_URL environment variable)"
            else
                match originOf (serverUrl.Trim()) with
                | None -> return ProbeFailed "the catalog must be an http or https URL"
                | Some _ when (match Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute) with | true, u -> u.UserInfo <> "" | _ -> false) ->
                    // it would be stored (stac.json) and logged with the URL: PRo3D asks for them
                    return ProbeFailed "the catalog URL must not contain a user name or password; PRo3D asks for them when the catalog wants a sign-in"
                | Some _ ->
                    let url = serverUrl.Trim().TrimEnd('/') + "/search"
                    try
                        use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 30.0)
                        use request = new HttpRequestMessage(HttpMethod.Post, url, Content = new StringContent("""{"limit":1}""", Encoding.UTF8, "application/json"))
                        header |> Option.iter (fun h -> request.Headers.Authorization <- h)
                        use! response = http.Value.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                        match response.StatusCode with
                        | Net.HttpStatusCode.Unauthorized ->
                            return (if header.IsSome then ProbeRejected else ProbeNeedsSignIn)
                        | Net.HttpStatusCode.Forbidden ->
                            return ProbeFailed "the catalog refuses access for this user (403)"
                        | Net.HttpStatusCode.MethodNotAllowed | Net.HttpStatusCode.NotFound ->
                            // No POST search here. STAC requires GET /search and makes POST
                            // optional: ask that way too, to tell a GET-only catalog from a
                            // URL that is not the API root (e.g. the website, without /api).
                            let status = int response.StatusCode
                            use get = new HttpRequestMessage(HttpMethod.Get, url + "?limit=1")
                            header |> Option.iter (fun h -> get.Headers.Authorization <- h)
                            use! answer = http.Value.SendAsync(get, HttpCompletionOption.ResponseContentRead, timeout.Token)
                            let! text = answer.Content.ReadAsStringAsync()
                            if answer.IsSuccessStatusCode && (Json.parseSearch text |> Result.isOk) then
                                return ProbeFailed (sprintf "the catalog searches only by GET (POST %s answered %d); PRo3D searches by POST" url status)
                            elif answer.StatusCode = Net.HttpStatusCode.Unauthorized || answer.StatusCode = Net.HttpStatusCode.Forbidden then
                                return ProbeFailed (sprintf "POST %s answered %d, and GET asks to sign in (%d): the catalog may not allow this user to search" url status (int answer.StatusCode))
                            else
                                return ProbeFailed (sprintf "no search at %s (%d): is the catalog URL the API root, usually ending in /api?" url status)
                        | _ when not response.IsSuccessStatusCode ->
                            return ProbeFailed (sprintf "the catalog answered %d %s" (int response.StatusCode) response.ReasonPhrase)
                        | _ ->
                            let! text = response.Content.ReadAsStringAsync()
                            match Json.parseSearch text with
                            | Ok _ -> return ProbeOk
                            | Result.Error _ -> return ProbeFailed "the URL answers, but not like a STAC catalog's /search"
                    with
                    | :? OperationCanceledException -> return ProbeFailed "the catalog did not answer within 30 s"
                    | e -> return ProbeFailed (sprintf "the catalog is not reachable: %s" e.Message)
        }

    /// Search, following the server's next-page links until `q.limit` items: a
    /// server may hand out fewer per page than asked for. The usable items, and how
    /// many the server matched in all.
    let search (serverUrl : string) (q : Query) : Task<Result<array<Item> * int, string>> =
        task {
            if String.IsNullOrWhiteSpace serverUrl then
                return Result.Error "no STAC server set (GIS tab, or the PRO3D_STAC_URL environment variable)"
            else
                let url = serverUrl.Trim().TrimEnd('/') + "/search"
                let items = ResizeArray<Item>()
                let mutable matched = 0
                let mutable error = None
                let mutable next = Some (url, Some (Json.searchBody q))
                let mutable pages = 0
                // a page limit too, against a server whose next link never ends
                while next.IsSome && error.IsNone && items.Count < q.limit && pages < 50 do
                    match next with
                    | Some (pageUrl, body) ->
                        pages <- pages + 1
                        match! searchPage pageUrl body with
                        | Result.Error e -> error <- Some e
                        | Ok text ->
                            match Json.parseSearch text with
                            | Result.Error e -> error <- Some e
                            | Ok (page, m) ->
                                if pages = 1 then matched <- m
                                items.AddRange page
                                // not by the page's usable items: a page of meshes only
                                // is empty here and still has a next page
                                next <- Json.tryNextPage text
                    | None -> ()
                match error with
                | Some e when items.Count = 0 -> return Result.Error e
                | Some e ->
                    // the pages so far are still worth checking
                    Log.warn "[STAC] paging stopped after %d item(s): %s" items.Count e
                    return Ok (items.ToArray() |> Array.truncate q.limit, matched)
                | None -> return Ok (items.ToArray() |> Array.truncate q.limit, matched)
        }

    /// Where all downloads land: %TEMP%\PRo3D\stac.
    let cacheDirectory () = Path.Combine(Path.GetTempPath(), "PRo3D", "stac")

    /// Where an item's files land: one folder per item, so the folder's mbi index
    /// (InstrumentMetadata.buildMbiIndexCached) sees exactly that item's sidecar.
    /// The id made a valid name, plus a short hash of the id itself: ids that differ
    /// only in case (one folder on Windows) or only in invalid characters, and ids
    /// like "." or "..", still get folders of their own inside the cache.
    /// The characters replaced are Windows' on every platform, not
    /// Path.GetInvalidFileNameChars: on Linux and macOS that is only '/' and NUL,
    /// so ':' or '*' would stay in the name there, and the same item would get a
    /// different folder name per platform.
    let itemDirectory (itemId : string) =
        let invalid (c : char) =
            c < ' ' || c = '<' || c = '>' || c = ':' || c = '"' || c = '/' || c = '\\' || c = '|' || c = '?' || c = '*'
        let safe =
            String(itemId.ToCharArray() |> Array.map (fun c -> if invalid c then '_' else c))
                .Trim('.', ' ')
        let hash =
            use sha = Security.Cryptography.SHA256.Create()
            sha.ComputeHash(Encoding.UTF8.GetBytes itemId)
            |> Array.take 4
            |> Array.map (fun b -> b.ToString "x2")
            |> String.concat ""
        let name = if safe = "" then hash else safe + "_" + hash
        Path.Combine(cacheDirectory (), name)

    /// The remote file name, kept locally: the mbi sidecar names its band files, so
    /// the image must keep the name the sidecar declares.
    let fileName (url : string) =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri -> Path.GetFileName(Uri.UnescapeDataString uri.AbsolutePath)
        | _ -> Path.GetFileName url

    /// The local path of an item's image. Its sidecars resolve the pointing on their
    /// own, so this is usable before the image itself has been downloaded.
    let localImagePath (itemId : string) (assets : StacAssets) =
        Path.Combine(itemDirectory itemId, fileName assets.image)

    /// Delete the download cache (the panel's broom). Files in use, e.g. an image
    /// projected right now, stay; everything else goes.
    let clearCache () =
        let dir = cacheDirectory ()
        if Directory.Exists dir then
            let mutable failed = 0
            for sub in Directory.GetDirectories dir do
                try Directory.Delete(sub, true) with _ -> failed <- failed + 1
            if failed > 0 then Log.warn "[STAC] cache: %d item folder(s) in use, kept" failed
            else Log.line "[STAC] cache cleared"

    /// A whole download, headers and body, must finish within this. HttpClient's own
    /// timeout covers only the headers when the body is streamed: a stalled transfer
    /// would otherwise hold its download slot for good.
    let mutable downloadTimeout = TimeSpan.FromMinutes 5.0

    /// Downloads running now, by target path: a second request for the same file
    /// joins the running one instead of writing the same file twice.
    let private running =
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<Result<string, string>>>>()

    let private fetchTo (url : string) (target : string) : Task<Result<string, string>> =
        task {
            do! downloadSlots.WaitAsync()
            try
                try
                    use timeout = new CancellationTokenSource(downloadTimeout)
                    Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
                    // a name of its own, so even a stray second writer cannot collide
                    let partial = sprintf "%s.%s.part" target (Guid.NewGuid().ToString "N")
                    try
                        use request = new HttpRequestMessage(HttpMethod.Get, url)
                        use! response = send request HttpCompletionOption.ResponseHeadersRead timeout.Token
                        if not response.IsSuccessStatusCode then
                            return Result.Error (errorOf url response)
                        else
                            // its own task, so the file is closed before the move
                            do! (task {
                                    use file = File.Create partial
                                    do! response.Content.CopyToAsync(file, timeout.Token)
                                } : Task)
                            File.Move(partial, target, true)
                            return Ok target
                    finally
                        if File.Exists partial then
                            try File.Delete partial with _ -> ()
                with
                | :? OperationCanceledException ->
                    return Result.Error (sprintf "%s: no complete answer within %.0f s" url downloadTimeout.TotalSeconds)
                | e ->
                    return Result.Error (sprintf "%s: %s" url e.Message)
            finally
                downloadSlots.Release() |> ignore
        }

    /// Download into the item's folder, unless already there. Written to a temporary
    /// name first, so an interrupted download never looks complete; one download per
    /// file at a time, however many searches and clicks ask for it.
    let download (itemId : string) (url : string) : Task<Result<string, string>> =
        let target = Path.Combine(itemDirectory itemId, fileName url)
        if File.Exists target then Task.FromResult(Ok target)
        else
            let lazyTask = running.GetOrAdd(target, fun _ -> lazy (fetchTo url target))
            let t = lazyTask.Value
            // forget it once done: a failed download may be tried again later
            t.ContinueWith(fun (_ : Task<Result<string, string>>) -> running.TryRemove(Collections.Generic.KeyValuePair(target, lazyTask)) |> ignore) |> ignore
            t

    /// Thumbnails go through PRo3D's own web server, not straight from the catalog:
    /// an <img> in the GUI would be fetched by the browser, which has no sign-in.
    /// The viewer serves `overviewPath` (Program.fs), downloading the overview with
    /// the session's credentials into the item's cache folder on first request.
    /// Only overviews of listed results are served (registered by candidateOf), so
    /// the route cannot be used to fetch arbitrary URLs.
    let private overviews = System.Collections.Concurrent.ConcurrentDictionary<string, string * string>()

    [<Literal>]
    let overviewRoute = "/stac-overview/"

    let private overviewKey (itemId : string) =
        use sha = Security.Cryptography.SHA256.Create()
        sha.ComputeHash(Encoding.UTF8.GetBytes itemId)
        |> Array.take 12
        |> Array.map (fun b -> b.ToString "x2")
        |> String.concat ""

    /// The GUI's thumbnail URL for an item.
    let overviewPath (itemId : string) = overviewRoute + overviewKey itemId

    let registerOverview (itemId : string) (url : string) =
        overviews.[overviewKey itemId] <- (itemId, url)

    /// The local file for a thumbnail route's key, downloading it first. Blocks: the
    /// web server calls it per request, off the update thread.
    let thumbnailTimeout = TimeSpan.FromSeconds 30.0

    let overviewFile (key : string) : Option<string> =
        match overviews.TryGetValue key with
        | true, (itemId, url) ->
            // a thumbnail is small: not worth holding a web server thread for long
            let t = download itemId url
            if not (t.Wait thumbnailTimeout) then None
            else
                match t.Result with
                | Ok path -> Some path
                | Result.Error e ->
                    Log.warn "[STAC] thumbnail: %s" e
                    None
        | _ -> None

    /// After a Disconnect: the listed thumbnails are no longer served.
    let clearOverviews () = overviews.Clear()

    /// The scene around a picked point, captured when the pick happened.
    type Scene =
        {
            /// the scene's instrument setup (FOV, row flip), as the projection uses it
            instruments      : InstrumentProjection.Instruments
            /// SPICE names: the surface's body-fixed frame and body
            frame            : string
            body             : string
            projectionMethod : ProjectionMethod
            /// the picked point and its surface normal, body-fixed, metres
            point            : V3d
            normal           : Option<V3d>
            /// a body-fixed ray against the loaded surfaces: hit point and normal, body-fixed
            pick             : Ray3d -> Option<V3d * Option<V3d>>
        }

    /// Tolerance on "the point is in the image", in pixels. The AFC field of view is
    /// known to be about half a percent off (#801); without slack, points at the
    /// image edge would flicker in and out of the list.
    let edgeTolerancePx = 4.0

    /// Why a sidecar cannot give this projection method a pose, if it cannot.
    /// MbiBased places the camera from the sidecar alone: SC_QUAT for the attitude,
    /// TRG_POS for the position. Some sidecars carry zeros there (no attitude, no
    /// target position); the projector built from them points nowhere in particular
    /// or sits in the body's centre, so the point would land far off and the image
    /// silently vanish from the list -- although the footprint rightly contains it.
    /// Spice takes both from the kernels and needs neither.
    let pointingProblem (projectionMethod : ProjectionMethod) (scQuat : QuaternionD) (targetPosKm : V3d) : Option<string> =
        let q = scQuat
        let noAttitude = q.W * q.W + q.X * q.X + q.Y * q.Y + q.Z * q.Z < 1e-12
        let noTarget = targetPosKm.Length < 1e-9
        match projectionMethod with
        | ProjectionMethod.MbiBased when noAttitude ->
            Some "the sidecar carries no attitude (SC_QUAT is zero); try Projection Method = Spice"
        | ProjectionMethod.MbiBased when noTarget ->
            Some "the sidecar carries no target position (TRG_POS is zero); try Projection Method = MbiAttitudeSpiceRange"
        // attitude from the sidecar, range from SPICE: TRG_POS not needed
        | ProjectionMethod.MbiAttitudeSpiceRange when noAttitude ->
            Some "the sidecar carries no attitude (SC_QUAT is zero); try Projection Method = Spice"
        | _ -> None

    /// Target -> sun, J2000, from the sidecar's SUN_POS and TRG_POS (both measured
    /// from the spacecraft). None without a sun position (read as zero): -TRG_POS
    /// alone would point at the camera, and the "incidence" would be the emission
    /// angle. With TRG_POS = 0 this is spacecraft -> sun: off by the spacecraft's
    /// distance to the target, far below a degree.
    let sunFromTarget (sunPos : V3d) (targetPos : V3d) : Option<V3d> =
        let v = sunPos - targetPos
        if sunPos.Length = 0.0 || v.Length = 0.0 then None else Some v

    let private angleDeg (a : V3d) (b : V3d) =
        let c = Vec.dot (Vec.normalize a) (Vec.normalize b)
        acos (clamp -1.0 1.0 c) |> Conversion.DegreesFromRadians

    /// Observation geometry of one image at the scene's picked point.
    ///
    /// Ok None: the image's pointing puts the point outside the image (the server's
    /// footprint is coarser than the image), or the surface hides it from the camera.
    /// Error: the pointing did not resolve --
    /// no sidecar, no size, an instrument without a frustum, or no SPICE coverage,
    /// in which case the image would not project either.
    let metrics (scene : Scene) (imagePath : string) : Result<Option<StacMetrics>, string> =
        let metadata = InstrumentMetadata.tryParseMetadataForImagePath imagePath
        match metadata with
        | None, _ -> Result.Error "no usable mbi sidecar"
        | _, None -> Result.Error "no image statistics sidecar (image size unknown)"
        | Some mbi, Some tif ->
            match InstrumentProjection.instrument2SpiceName mbi.instrument,
                  pointingProblem scene.projectionMethod mbi.sc_quat mbi.targetPos with
            | None, _ -> Result.Error (sprintf "no SPICE frame known for instrument %s" mbi.instrument)
            | Some _, Some problem -> Result.Error problem
            | Some spiceName, None ->
                let size = V2i(tif.image_width, tif.image_height)
                let resolved : ResolvedImage =
                    { path = imagePath; metadata = metadata; mbi = mbi; spiceName = spiceName; size = Some size }
                match InstrumentObservation.projectorCameraWith scene.instruments None scene.frame scene.body scene.projectionMethod resolved with
                | Result.Error e -> Result.Error e
                | Ok cam ->
                    let inside (p : V2d) =
                        p.X >= -0.5 - edgeTolerancePx && p.Y >= -0.5 - edgeTolerancePx
                        && p.X <= float size.X - 0.5 + edgeTolerancePx && p.Y <= float size.Y - 0.5 + edgeTolerancePx
                    let eye = cam.view.Backward.TransformPos V3d.Zero
                    // one pixel's angle at the centre of a symmetric frustum,
                    // (2 / width) * tan(fov / 2) with M00 = 1 / tan(fov / 2)
                    let pixelAngle = 2.0 / (cam.proj.Forward.M00 * float size.X)
                    // Inside the frame is not enough: the far side of the body projects
                    // into a full-disc image too, and terrain can hide the point. Hidden
                    // when the ray towards it hits the surface first -- by more than a
                    // few pixels' footprint, below which occlusion is not resolvable.
                    let hidden () =
                        let toPoint = scene.point - eye
                        let distance = toPoint.Length
                        let tolerance = max 1.0 (3.0 * distance * pixelAngle)
                        match scene.pick (Ray3d(eye, toPoint / distance)) with
                        | Some (hit, _) -> Vec.length (hit - eye) < distance - tolerance
                        | None -> false
                    match InstrumentObservation.projectToPixel cam size InstrumentObservation.PixelConvention.Image scene.point with
                    | Some pixel when inside pixel && not (hidden ()) ->
                        let boresight =
                            let far = cam.full.Backward.TransformPosProj(V3d(0.0, 0.0, 1.0))
                            Ray3d(eye, Vec.normalize (far - eye))
                        // where the central pixel lands; the picked point when the
                        // boresight misses what is loaded (a limb frame, a partial OPC)
                        let spot, normal, onSurface =
                            match scene.pick boresight with
                            | Some (hit, normal) -> hit, normal, true
                            | None -> scene.point, scene.normal, false
                        let range = Vec.length (spot - eye)
                        let pixelSize = range * pixelAngle
                        // the sun direction in the body-fixed frame (sunFromTarget)
                        let toSun =
                            sunFromTarget mbi.sunPos mbi.targetPos
                            |> Option.bind (fun toSunJ2000 ->
                                InstrumentProjection.withSpiceLock (fun () ->
                                    CooTransformation.getRotationTrafo "J2000" scene.frame mbi.obs_date)
                                |> Option.map (fun rot -> rot.Forward.TransformDir toSunJ2000))
                        let incidenceAt (p : V3d) (normal : Option<V3d>) =
                            match normal, toSun with
                            | Some n, Some toSun ->
                                // face the normal to the camera: OPC winding is not
                                // reliably outward (see Winding Correction)
                                let n = if Vec.dot n (eye - p) < 0.0 then -n else n
                                Some (angleDeg n toSun)
                            | _ -> None
                        Ok (Some {
                            rangeM            = range
                            pixelSizeM        = pixelSize
                            incidenceDeg      = incidenceAt spot normal
                            pointIncidenceDeg = incidenceAt scene.point scene.normal
                            pixel             = pixel
                            centralOnSurface  = onSurface
                        })
                    | _ -> Ok None

    let private candidateOf (item : Item) (check : StacCheck) : StacCandidate =
        item.assets.overview |> Option.iter (registerOverview item.id)
        {
            itemId     = item.id
            collection = item.collection
            title      = item.title
            time       = item.time
            instrument = item.instrument
            assets     = item.assets
            check      = check
            values     = None
        }

    /// Resolved images first, finest pixels first; then unresolved ones and the ones
    /// not checked yet, newest first.
    let sortCandidates (candidates : array<StacCandidate>) =
        candidates
        |> Array.sortBy (fun c ->
            match c.check with
            | Resolved m -> 0, m.pixelSizeM, 0L
            | Unresolved _ -> 1, 0.0, -c.time.Ticks
            | StacCheck.Checking -> 2, 0.0, -c.time.Ticks)

    /// Sort by a column, ascending or not; rows without the value (not checked yet,
    /// no metadata) go last either way. Stable, so equal rows keep their order.
    let sortByOptional (ascending : bool) (key : 'a -> Option<'k>) (rows : array<'a>) : array<'a> when 'k : comparison =
        let keyed = rows |> Array.choose (fun r -> key r |> Option.map (fun k -> k, r)) |> Array.sortBy fst |> Array.map snd
        let without = rows |> Array.filter (fun r -> (key r).IsNone)
        Array.append (if ascending then keyed else Array.rev keyed) without

    /// The results sorted by a column of the image list (ImageSortKey).
    let sortCandidatesBy ((key, ascending) : ImageSortKey * bool) (candidates : array<StacCandidate>) =
        let checkedValues (c : StacCandidate) =
            match c.check with
            | StacCheck.Checking -> None
            | _ -> c.values
        match key with
        | SortInstrument ->
            candidates |> sortByOptional ascending (fun c ->
                match c.check with
                | StacCheck.Checking -> None
                | _ -> Some (c.values |> Option.map (fun v -> v.instrument) |> Option.defaultValue c.instrument))
        | SortDistance -> candidates |> sortByOptional ascending (checkedValues >> Option.map (fun v -> v.distanceKm))
        | SortDate -> candidates |> sortByOptional ascending (fun c -> match c.check with StacCheck.Checking -> None | _ -> Some c.time)
        | SortResolution -> candidates |> sortByOptional ascending (checkedValues >> Option.bind (fun v -> v.resolution))

    /// The next sort for a click on a column's header: ascending, or the other way
    /// round when that column is the current sort.
    let nextSort (current : Option<ImageSortKey * bool>) (key : ImageSortKey) =
        match current with
        | Some (k, ascending) when k = key -> key, not ascending
        | _ -> key, true

    /// How a search is getting on, reported as it goes.
    type Progress =
        /// the server's answer: every candidate still Checking, the match count, and
        /// whether the footprints found them
        | Listed  of array<StacCandidate> * matched : int * StacListing
        /// one candidate checked; None when the point is not (visibly) in the image.
        /// With the metadata values the image lists show, once the sidecars are there.
        | Checked of itemId : string * Option<StacCheck> * Option<StacImageValues>

    /// The sidecars one check needs: the mbi, and the statistics sidecar for the
    /// image size.
    let private sidecars (item : Item) : Task<Result<unit, string>> =
        task {
            let! mbi = download item.id item.assets.mbi
            let! _ =
                match item.assets.imageMetadata with
                | Some url -> download item.id url
                | None -> Task.FromResult(Ok "")
            match mbi with
            | Ok _ -> return Ok ()
            | Result.Error e -> return Result.Error e
        }

    /// A check that failed because SPICE could not place or turn something at the
    /// image's time: the loaded kernel does not cover it (InstrumentProjection's
    /// "SPICE has no position ..." / "SPICE cannot rotate ..."). A kernel that covers
    /// the time fixes these, unlike a sidecar without attitude.
    let isSpiceCoverage (error : string) =
        error.Contains "SPICE has no position" || error.Contains "SPICE cannot rotate"

    /// Said before searching when no kernel is loaded: every check would fail.
    [<Literal>]
    let noSpiceKernel = "no SPICE kernel loaded: checking an image's pointing needs one that covers its time. Load it in the top bar: Load SPICE kernel, next to Reference System"

    /// Runs one check (KdTree loads, SPICE): the viewer sets it to its background busy
    /// indicator, so a long check shows up there (CLAUDE.md, busy indicator).
    let mutable checkScope : (unit -> unit) -> unit = fun f -> f ()

    /// How many items one search checks at most, newest first. A narrower time
    /// range gets the older ones in.
    let checkLimit = 1000

    /// How many sidecar downloads run ahead of the checks. A search abandoned for a
    /// newer point leaves at most these behind (they finish into the cache).
    let downloadAhead = 16

    /// The server's items for the point: those whose footprint contains it; when
    /// there are none, the ones without any footprint (catalogs that have not
    /// computed footprints yet, e.g. for a newly visited body). The count is of the
    /// items listed, i.e. checked.
    let listFor (filter : Filter) (limit : int) (scene : Scene) : Task<Result<array<Item> * int * StacListing, string>> =
        task {
            let ll = lonLat scene.point
            let q = { point = Some ll; target = Some scene.body; from = filter.from; until = filter.until
                      distanceKm = filter.bounds.distance; limit = limit }
            let warnMore (matched : int) (listed : int) =
                if matched > listed then
                    Log.warn "[STAC] the catalog matched %d items, only the newest %d are checked; narrow the time range" matched listed
            match! search filter.serverUrl q with
            | Result.Error e -> return Result.Error e
            | Ok (items, matched) when items.Length > 0 ->
                warnMore matched items.Length
                return Ok (items, items.Length, ByFootprint)
            | Ok _ ->
                // without the target filter: such items often name no target either
                let q = { q with point = None; target = None }
                match! search filter.serverUrl q with
                | Result.Error e -> return Result.Error e
                | Ok (items, matched) ->
                    warnMore matched items.Length
                    let items = items |> Array.filter (isWithoutFootprintFor scene.body)
                    return Ok (items, items.Length, WithoutFootprint)
        }

    /// Search one point: ask the server, then check its hits against the images'
    /// own pointing, reporting each step. Ok (Some dropped): all hits checked;
    /// Ok None: abandoned because `isStale` said a newer point is waiting.
    ///
    /// Footprint hits whose pointing does not resolve are listed (in red): the
    /// footprint says they show the point. Footprint-less items are only listed
    /// when their pointing shows the point; unresolvable ones are just counted.
    ///
    /// The sidecars download in parallel, but the checks run one at a time, in the
    /// server's order: a check picks the terrain, and concurrent picks miss the same
    /// cold KdTree patch together and each load it (seen: one MOLA patch loaded 64
    /// times, the viewer's own preview pick stalled behind them).
    let run (filter : Filter) (limit : int) (scene : Scene)
            (isStale : unit -> bool) (report : Progress -> unit) : Task<Result<Option<StacDropped>, string>> =
        task {
            match! listFor filter limit scene with
            | Result.Error e -> return Result.Error e
            | Ok (items, matched, listing) ->
                if isStale () then
                    return Ok None
                else
                    report (Listed (items |> Array.map (fun item -> candidateOf item StacCheck.Checking), matched, listing))
                    // the sidecars of the next `downloadAhead` items download while
                    // the current one is checked
                    let downloads : array<Option<Task<Result<unit, string>>>> = Array.create items.Length None
                    let start (j : int) =
                        if j < items.Length && downloads.[j].IsNone then downloads.[j] <- Some (sidecars items.[j])
                    for j in 0 .. downloadAhead - 1 do start j
                    let mutable abandoned = false
                    let mutable dropped = StacDropped.none
                    let mutable i = 0
                    while not abandoned && i < items.Length do
                        if isStale () then
                            abandoned <- true
                        else
                            let item = items.[i]
                            start i
                            start (i + downloadAhead)
                            let! files =
                                match downloads.[i] with
                                | Some t -> t
                                | None -> sidecars item
                            // the shared filters, on the downloaded metadata by the library's
                            // own rules -- before the (costly) pointing check
                            let values =
                                match files with
                                | Ok () ->
                                    localImagePath item.id item.assets
                                    |> InstrumentMetadata.tryParseMetadataForImagePath
                                    |> ImageFilter.valuesOf
                                    |> Some
                                | Result.Error _ -> None
                            let passesFilters =
                                values |> Option.forall (ImageFilter.passesValues filter.bounds)
                            // what the image lists show: the same values as a library image's
                            let shown =
                                values |> Option.map (fun v ->
                                    { instrument = v.instrument; distanceKm = v.distance; resolution = v.resolution })
                            let check =
                                let resolved =
                                    match files with
                                    | Result.Error e -> Result.Error e
                                    | Ok () when not passesFilters -> Ok None
                                    | Ok () ->
                                        let r = ref (Ok None)
                                        checkScope (fun () -> r.Value <- metrics scene (localImagePath item.id item.assets))
                                        r.Value
                                match resolved, listing with
                                | _ when not passesFilters ->
                                    dropped <- { dropped with outsideFilters = dropped.outsideFilters + 1 }
                                    None
                                // SUN INCIDENCE (disabled, see docs/StacImageSearch.md)
                                // | Ok (Some m), _ when not (incidencePasses filter.incidence m.pointIncidenceDeg) ->
                                //     dropped <- { dropped with outsideIncidence = dropped.outsideIncidence + 1 }
                                //     None
                                | Ok (Some m), _ -> Some (Resolved m)
                                | Ok None, _ -> None
                                | Result.Error e, ByFootprint -> Some (Unresolved e)
                                | Result.Error e, WithoutFootprint when isSpiceCoverage e ->
                                    dropped <- { dropped with noSpiceCoverage = dropped.noSpiceCoverage + 1 }
                                    None
                                | Result.Error e, WithoutFootprint ->
                                    dropped <- { dropped with count = dropped.count + 1; reason = dropped.reason |> Option.orElse (Some e) }
                                    None
                            report (Checked (item.id, check, shown))
                            i <- i + 1
                    return Ok (if abandoned then None else Some dropped)
        }

    /// `run` to the end, collected: the kept candidates, sorted, the server's
    /// match count, and how the list came about.
    let runToEnd (filter : Filter) (limit : int) (scene : Scene) : Task<Result<array<StacCandidate> * int * StacListing, string>> =
        task {
            let listed = ref ([||], 0, ByFootprint)
            let checks = System.Collections.Generic.Dictionary<string, Option<StacCheck> * Option<StacImageValues>>()
            let report = function
                | Listed (candidates, matched, listing) -> listed.Value <- (candidates, matched, listing)
                | Checked (id, check, values) -> checks.[id] <- (check, values)
            match! run filter limit scene (fun () -> false) report with
            | Result.Error e -> return Result.Error e
            | Ok _ ->
                let candidates, matched, listing = listed.Value
                let kept =
                    candidates
                    |> Array.choose (fun c ->
                        match checks.TryGetValue c.itemId with
                        | true, (Some check, values) -> Some { c with check = check; values = values }
                        | _ -> None)
                    |> sortCandidates
                return Ok (kept, matched, listing)
        }

    /// Download all files of an item. Returns the local image path.
    let fetch (itemId : string) (assets : StacAssets) : Task<Result<string, string>> =
        task {
            let! mbi = download itemId assets.mbi
            let! metadata =
                match assets.imageMetadata with
                | Some url -> download itemId url
                | None -> Task.FromResult(Ok "")
            let! image = download itemId assets.image
            match mbi, metadata, image with
            | Ok _, Ok _, Ok image -> return Ok image
            | Result.Error e, _, _ | _, Result.Error e, _ | _, _, Result.Error e -> return Result.Error e
        }

    /// The search a Worker runs: filter, scene, isStale, report.
    type SearchFunction = Filter -> Scene -> (unit -> bool) -> (Progress -> unit) -> Task<Result<Option<StacDropped>, string>>

    /// Runs searches off the UI thread, newest point wins: while one search runs,
    /// a different point replaces the pending one -- and makes the running search
    /// stop at its next candidate. A short settle delay lets a moving cursor come to
    /// rest before the server is asked. The point being searched, or the last one
    /// searched to the end, is not searched again unless forced (a click, a changed
    /// filter): a cursor jittering on the spot must not restart a long search.
    /// Remembers the last point, so a changed filter searches it again (Rerun).
    ///
    /// All mutable state is under `gate`; `post` runs outside it.
    type Worker(post : StacSearchMessage -> unit, runSearch : SearchFunction, settleMs : int) =
        let gate = obj()
        let mutable pending : Option<Filter * Scene> = None
        let mutable searches = 0
        /// the point being searched now
        let mutable current : Option<Filter * V2d * string> = None
        /// the last point searched to the end
        let mutable last : Option<Filter * V2d * string> = None
        let mutable lastScene : Option<Scene> = None
        let mutable cancelled = false
        /// counts Connect and Disconnect calls: an attempt that is no longer the
        /// latest when it returns is dropped
        let mutable connections = 0
        let wake = new SemaphoreSlim(0)

        /// Points closer than this (degrees) count as the same point: the result
        /// would not change, and every mouse move is a pick.
        let samePointDeg = 1e-5

        let key (filter : Filter) (scene : Scene) = filter, lonLat scene.point, scene.body

        let same (a : Option<Filter * V2d * string>) (filter : Filter, ll : V2d, body : string) =
            match a with
            | Some (f, l, b) ->
                f = filter && b = body
                && abs (l.X - ll.X) < samePointDeg && abs (l.Y - ll.Y) < samePointDeg
            | None -> false

        let take () =
            lock gate (fun () ->
                let p = pending
                pending <- None
                match p with
                | Some (filter, scene) ->
                    current <- Some (key filter scene)
                    cancelled <- false
                    searches <- searches + 1
                    Some (filter, scene, searches)
                | None -> None)

        let finish (completed : bool) =
            lock gate (fun () ->
                // abandoned: forget the point, so returning to it searches again
                last <- if completed then current else None
                current <- None)

        let loop =
            Task.Run(fun () ->
                task {
                    while true do
                        do! wake.WaitAsync()
                        if settleMs > 0 then do! Task.Delay settleMs
                        // absorb the wake-ups that piled up while settling
                        while wake.CurrentCount > 0 do wake.Wait 0 |> ignore
                        match take () with
                        | Some (filter, scene, s) ->
                            try
                                let ll = lonLat scene.point
                                post (QueryStarted { seq = s; body = scene.body; lon = ll.X; lat = ll.Y })
                                // a newer point waiting, or a cancel, makes this search stale
                                let isStale () = lock gate (fun () -> Option.isSome pending || cancelled)
                                let report = function
                                    | Listed (candidates, matched, listing) -> post (QueryListed (s, candidates, matched, listing))
                                    | Checked (id, check, values) -> post (QueryChecked (s, id, check, values))
                                match! runSearch filter scene isStale report with
                                | Ok (Some dropped) ->
                                    finish true
                                    post (QueryFinished (s, Ok dropped))
                                | Ok None -> finish false
                                | Result.Error e ->
                                    finish false
                                    post (QueryFinished (s, Result.Error e))
                            with e ->
                                finish false
                                Log.warn "[STAC] search failed: %s" e.Message
                                post (QueryFinished (s, Result.Error e.Message))
                        | None -> ()
                } :> Task)

        new(post : StacSearchMessage -> unit) =
            Worker(post, (fun filter scene isStale report -> run filter checkLimit scene isStale report), 250)

        /// Search this point, unless it is being searched or was the last one searched
        /// to the end (with the same filter), and `force` is not set.
        member _.Search(filter : Filter, scene : Scene, force : bool) =
            let queued =
                lock gate (fun () ->
                    let k = key filter scene
                    if not force && (same current k || (same last k && Option.isNone pending)) then false
                    else
                        if force then last <- None
                        pending <- Some (filter, scene)
                        lastScene <- Some scene
                        true)
            if queued then wake.Release() |> ignore

        /// A preview pick: searched once the cursor rests, skipped when unchanged.
        member x.Search(filter : Filter, scene : Scene) = x.Search(filter, scene, false)

        /// Search the last point again although it has not moved, with a changed
        /// filter (server, time range, incidence). Nothing when no point was searched yet.
        member x.Rerun(filter : Filter) =
            match lock gate (fun () -> lastScene) with
            | Some scene -> x.Search(filter, scene, true)
            | None -> ()

        /// Search this point even if it was the last one (a click).
        member x.SearchNow(filter : Filter, scene : Scene) = x.Search(filter, scene, true)

        /// Stop the running search at its next candidate and drop a pending one
        /// (the results were cleared).
        member _.Cancel() =
            lock gate (fun () ->
                pending <- None
                cancelled <- true
                last <- None)

        member _.Fetch(itemId : string, assets : StacAssets) =
            Task.Run(fun () ->
                task {
                    let! result =
                        task {
                            try return! fetch itemId assets
                            with e -> return Result.Error e.Message
                        }
                    post (FetchFinished (itemId, result))
                } :> Task) |> ignore

        /// Ask the catalog, with this sign-in or none, off the update thread. On
        /// success the sign-in becomes the session's (signIn); a refused one changes
        /// nothing. Posts ConnectFinished either way.
        member _.Connect(serverUrl : string, user : Option<string * Secret>) =
            let generation = lock gate (fun () -> connections <- connections + 1; connections)
            Task.Run(fun () ->
                task {
                    let header = user |> Option.map (fun (name, password) -> basicAuth name password)
                    let! result =
                        task {
                            match header with
                            // Basic auth is the password in clear text: not over plain http
                            | Some _ when not (allowsCredentials serverUrl) ->
                                return ProbeFailed "signing in needs an https catalog URL: over http the password would travel unencrypted"
                            | _ ->
                                try return! probe serverUrl header
                                with e -> return ProbeFailed e.Message
                        }
                    // a Disconnect (or a newer attempt) since: install nothing. The check
                    // and the install in one step under `gate`, as Disconnect's signOut: a
                    // Disconnect cannot slip in between and be overwritten.
                    let current =
                        lock gate (fun () ->
                            if connections = generation then
                                match result, header with
                                | ProbeOk, Some h -> signIn serverUrl h
                                | ProbeOk, None -> signOut ()
                                | _ -> ()
                                true
                            else false)
                    if current then post (ConnectFinished (user |> Option.map fst, result))
                } :> Task) |> ignore

        /// Forget the sign-in and stop searching. An attempt still running is ignored
        /// when it comes back. The listed thumbnails are no longer served either.
        member x.Disconnect() =
            lock gate (fun () ->
                connections <- connections + 1
                signOut ())
            clearOverviews ()
            x.Cancel()
