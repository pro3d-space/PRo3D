module StacSearchTests

open System
open System.Net.Http
open System.Text.Json

open Expecto
open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.GIS
open PRo3D.ImageMapping

/// Shaped like a STAC search response; names and values made up. Includes an item
/// without an image asset (e.g. a mesh), which must be skipped.
let private response = """
{
  "type": "FeatureCollection",
  "numberMatched": 42,
  "numberReturned": 3,
  "features": [
    {
      "type": "Feature", "id": "item-1", "collection": "cam_a",
      "properties": { "title": "CAM_A_TEST", "datetime": "2000-01-02T12:00:00.500000Z", "instruments": ["cam1"], "ssys:targets": ["mars"] },
      "assets": {
        "mbi":            { "href": "https://example.org/data/item-1/cam_a_test.mbi.json", "type": "application/json" },
        "image":          { "href": "https://example.org/data/item-1/cam_a_test.tif", "type": "image/tiff" },
        "image_metadata": { "href": "https://example.org/data/item-1/cam_a_test.tif.json" },
        "overview":       { "href": "https://example.org/data/item-1/cam_a_test.png" }
      }
    },
    {
      "type": "Feature", "id": "item-2", "collection": "cam_b",
      "properties": { "title": "CAM_B_TEST", "datetime": "2000-01-02T13:20:05Z", "instruments": ["cam2"] },
      "assets": {
        "mbi":   { "href": "https://example.org/data/item-2/cam_b_test.mbi.json" },
        "image": { "href": "https://example.org/data/item-2/cam%20b%20test.tif" }
      }
    },
    {
      "type": "Feature", "id": "mesh-1", "collection": "meshes",
      "properties": { "title": "mesh-1", "datetime": "2000-01-01T00:00:00Z" },
      "assets": { "mesh": { "href": "https://example.org/data/meshes/mesh.7z" } }
    }
  ]
}
"""

/// Items without footprint (made up): one naming no target, one naming the body,
/// one of another body, and one with a footprint.
let private withoutFootprint = """
{
  "type": "FeatureCollection",
  "features": [
    { "type": "Feature", "id": "with-footprint", "collection": "c", "geometry": { "type": "Polygon", "coordinates": [[[0,0],[1,0],[1,1],[0,0]]] },
      "properties": { "datetime": "2026-01-01T00:00:00Z" },
      "assets": { "mbi": { "href": "https://example.org/a.mbi.json" }, "image": { "href": "https://example.org/a.tif" } } },
    { "type": "Feature", "id": "no-target", "collection": "c", "geometry": null,
      "properties": { "datetime": "2026-01-01T00:00:00Z" },
      "assets": { "mbi": { "href": "https://example.org/b.mbi.json" }, "image": { "href": "https://example.org/b.tif" } } },
    { "type": "Feature", "id": "didymos", "collection": "c",
      "properties": { "datetime": "2026-01-01T00:00:00Z", "ssys:targets": ["Didymos"] },
      "assets": { "mbi": { "href": "https://example.org/c.mbi.json" }, "image": { "href": "https://example.org/c.tif" } } },
    { "type": "Feature", "id": "other-body", "collection": "c",
      "properties": { "datetime": "2026-01-01T00:00:00Z", "ssys:targets": ["MARS"] },
      "assets": { "mbi": { "href": "https://example.org/d.mbi.json" }, "image": { "href": "https://example.org/d.tif" } } }
  ]
}
"""

let private offline =
    testList "STAC search (offline)" [
        test "a search response yields the projectable items" {
            match StacSearch.Json.parseSearch response with
            | Result.Error e -> failtestf "parse failed: %s" e
            | Result.Ok (items, matched) ->
                Expect.equal matched 42 "numberMatched, not the page size"
                Expect.equal (items |> Array.map (fun i -> i.id)) [| "item-1"; "item-2" |] "the mesh item has no image and is skipped"
                let first = items.[0]
                Expect.equal first.instrument "CAM1" "instrument names upper case, as the sidecars spell them"
                Expect.equal first.time (DateTime(2000, 1, 2, 12, 0, 0, 500, DateTimeKind.Utc)) "datetime as UTC"
                Expect.equal first.time.Kind DateTimeKind.Utc "kind UTC"
                Expect.equal first.assets.imageMetadata (Some "https://example.org/data/item-1/cam_a_test.tif.json") "statistics sidecar"
                Expect.equal items.[1].assets.overview None "optional assets stay optional"
        }

        test "an unusable response is an error, not an empty result" {
            Expect.isError (StacSearch.Json.parseSearch "{\"type\": \"FeatureCollection\"}") "no features"
            Expect.isError (StacSearch.Json.parseSearch "<html>") "not json"
        }

        test "the search asks for the point, the body and a page size" {
            let body = StacSearch.Json.searchBody { point = Some (V2d(158.17, -43.43)); target = Some "MARS"; from = None; until = None; distanceKm = None; limit = 100 }
            use doc = JsonDocument.Parse body
            let root = doc.RootElement
            let coordinates = root.GetProperty("intersects").GetProperty("coordinates")
            Expect.equal (coordinates.[0].GetDouble(), coordinates.[1].GetDouble()) (158.17, -43.43) "GeoJSON order: lon, lat"
            Expect.equal (root.GetProperty("limit").GetInt32()) 100 "limit"
            let args = root.GetProperty("filter").GetProperty("args")
            Expect.equal (args.[0].GetProperty("property").GetString()) "ssys:targets" "filtered on the targets"
            Expect.equal (args.[1].[0].GetString()) "mars" "target in the catalog's lower case"
            Expect.isFalse (fst (root.TryGetProperty "datetime")) "no time range, no datetime"
        }

        test "without point and target, only the time range narrows the search" {
            let from = DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            let body = StacSearch.Json.searchBody { point = None; target = None; from = Some from; until = None; distanceKm = None; limit = 1000 }
            use doc = JsonDocument.Parse body
            let root = doc.RootElement
            Expect.isFalse (fst (root.TryGetProperty "intersects")) "no spatial filter"
            Expect.isFalse (fst (root.TryGetProperty "filter")) "no target filter"
            Expect.equal (root.GetProperty("datetime").GetString()) "2025-03-01T00:00:00.0000000Z/.." "open end as .."
        }

        test "a typed time range: dates, ISO times, open ends, mistakes" {
            let utc y mo d h mi s = DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc)
            Expect.equal (StacSearch.parseTimeRange "" " ") (Ok (None, None)) "both open"
            match StacSearch.parseTimeRange "2025-03-01" "2025-03-12" with
            | Ok (Some f, Some u) ->
                Expect.equal f (utc 2025 3 1 0 0 0) "a start date is its midnight"
                Expect.equal u ((utc 2025 3 13 0 0 0).AddTicks -1L) "an end date includes its whole day"
                Expect.equal f.Kind DateTimeKind.Utc "UTC"
            | r -> failtestf "dates: %A" r
            match StacSearch.parseTimeRange "2025-03-12T06:20:00" "" with
            | Ok (Some f, None) -> Expect.equal f (utc 2025 3 12 6 20 0) "an ISO time is taken as UTC"
            | r -> failtestf "ISO: %A" r
            Expect.isError (StacSearch.parseTimeRange "12.3.2025x" "") "not a time"
            Expect.isError (StacSearch.parseTimeRange "2025-03-12" "2025-03-01") "ends before it starts"
        }

        test "items without footprint are checked by pointing when they name the body or nothing" {
            let parsed =
                match StacSearch.Json.parseSearch withoutFootprint with
                | Ok (items, _) -> items
                | Result.Error e -> failtestf "parse failed: %s" e
            let byId = parsed |> Array.map (fun i -> i.id, i) |> Map.ofArray
            let item id = match Map.tryFind id byId with Some i -> i | None -> failtestf "%s missing" id
            Expect.isTrue (item "with-footprint").hasFootprint "geometry read"
            Expect.isFalse (item "no-target").hasFootprint "null geometry"
            Expect.equal (item "other-body").targets (Some [| "mars" |]) "targets lower case"
            let kept = parsed |> Array.filter (StacSearch.isWithoutFootprintFor "DIDYMOS") |> Array.map (fun i -> i.id)
            Expect.equal kept [| "no-target"; "didymos" |]
                "not the one with a footprint (it would have been found by it), not one of another body"
            let others = parsed |> Array.filter (StacSearch.isOtherTargetFor "DIDYMOS") |> Array.map (fun i -> i.id)
            Expect.equal others [| "with-footprint"; "other-body" |]
                "other targets: another body's, and a footprint on an unnamed body; disjoint from the footprint-less ones"
            Expect.equal (StacSearch.otherTargetLabel (item "other-body")) "mars" "the catalog's target"
            Expect.equal (StacSearch.otherTargetLabel (item "with-footprint")) "no target named" "none named"
        }

        test "longitude is planetocentric and east-positive, like the footprints" {
            let at (lonDeg : float) (latDeg : float) =
                let lon, lat = Conversion.RadiansFromDegrees lonDeg, Conversion.RadiansFromDegrees latDeg
                V3d(cos lat * cos lon, cos lat * sin lon, sin lat) * 3396190.0
            let check (lon : float) (lat : float) =
                let ll = StacSearch.lonLat (at lon lat)
                Expect.floatClose Accuracy.high ll.X lon (sprintf "lon of %.2f, %.2f" lon lat)
                Expect.floatClose Accuracy.high ll.Y lat (sprintf "lat of %.2f, %.2f" lon lat)
            check 77.45 18.44     // Jezero: 77.45 E, not 282.55
            check -158.17 -43.43
            check 0.0 89.0
            Expect.equal (StacSearch.lonLat (V3d(0.0, 1.0, 0.0))).X 90.0 "+Y is 90 degrees east"
        }

        test "an item's files keep their remote names, one folder per item" {
            let assets =
                { mbi = "https://example.org/data/x/cam_b_test.mbi.json"
                  image = "https://example.org/data/x/cam%20b%20test.tif"
                  imageMetadata = None; overview = None }
            let path = StacSearch.localImagePath "a/b:c" assets
            Expect.equal (IO.Path.GetFileName path) "cam b test.tif" "unescaped remote name: the mbi sidecar declares it"
            Expect.isFalse ((IO.Path.GetFileName(IO.Path.GetDirectoryName path)).Contains ":") "item id made a valid folder name"
        }

        test "the incidence filter: inclusive bounds, unknown passes" {
            let day = Range1d(0.0, 90.0)
            Expect.isTrue (StacSearch.incidencePasses day (Some 0.0)) "lower bound included"
            Expect.isTrue (StacSearch.incidencePasses day (Some 90.0)) "upper bound included"
            Expect.isFalse (StacSearch.incidencePasses day (Some 95.0)) "night"
            Expect.isTrue (StacSearch.incidencePasses day None) "no sun position or normal: kept, the filter cannot tell"
        }

        test "the search filter is the panel's shared Filters section" {
            let s = StacSearchModel.initial
            match StacSearch.filterOf s ImageFilter.initial with
            | Ok f ->
                // the Date filter is off (whole range), and its range still goes to the server
                Expect.equal f.from (Some (DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc))) "from 2026-11-01"
                Expect.equal f.until (Some (DateTime.UtcNow.Date.AddDays(1.0).AddSeconds(-1.0))) "to the end of today"
                Expect.isNone f.bounds.time "but PRo3D's own check lets every time pass"
                Expect.equal f.bounds (ImageFilter.bounds ImageFilter.initial) "the library's bounds"
            | Result.Error e -> failtestf "the defaults must make a filter: %s" e
            let t0 = DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc)
            let key t = ImageFilter.timeKey t |> Option.defaultValue nan
            let dated = ImageFilter.update (ImageFilter.update ImageFilter.initial (ResetRange RangeFilterKind.Time)) (SetLower (RangeFilterKind.Time, Aardvark.UI.Primitives.Numeric.SetValue (key t0)))
            match StacSearch.filterOf s dated with
            | Ok f -> Expect.equal f.from (Some t0) "Date set: its start goes to the server"
            | Result.Error e -> failtestf "a date range must make a filter: %s" e
            Expect.isError (StacSearch.filterOf { s with incidenceMin = 80.0; incidenceMax = 20.0 } ImageFilter.initial) "incidence range reversed"
            match StacSearch.filterOf { s with incidenceMin = 55.0; incidenceMax = 80.0 } ImageFilter.initial with
            | Ok f -> Expect.equal f.incidence (Range1d(55.0, 80.0)) "the incidence range goes into the filter"
            | Result.Error e -> failtestf "an incidence range must make a filter: %s" e
        }

        test "a distance range goes to the server in AU, widened, and-ed with the target" {
            let q : StacSearch.Query =
                { point = Some (V2d(10.0, 20.0)); target = Some "DIMORPHOS"; from = None; until = None
                  distanceKm = Some (10.0, 100.0); limit = 10 }
            use doc = JsonDocument.Parse (StacSearch.Json.searchBody q)
            let filter = doc.RootElement.GetProperty "filter"
            Expect.equal (filter.GetProperty("op").GetString()) "and" "the conditions are and-ed"
            let args = filter.GetProperty "args"
            Expect.equal (args.GetArrayLength()) 3 "target, lower and upper distance"
            Expect.equal (args.[0].GetProperty("op").GetString()) "a_contains" "the target first"
            let bound (i : int) = args.[i].GetProperty("op").GetString(), args.[i].GetProperty("args").[0].GetProperty("property").GetString(), args.[i].GetProperty("args").[1].GetDouble()
            let op1, prop1, lo = bound 1
            let op2, prop2, hi = bound 2
            Expect.equal (op1, prop1) (">=", "hera:target_distance") "lower bound"
            Expect.equal (op2, prop2) ("<=", "hera:target_distance") "upper bound"
            Expect.floatClose Accuracy.high lo (10.0 * (1.0 - StacSearch.distanceTolerance) / StacSearch.kmPerAu) "km -> AU, widened down"
            Expect.floatClose Accuracy.high hi (100.0 * (1.0 + StacSearch.distanceTolerance) / StacSearch.kmPerAu) "km -> AU, widened up"
            // no target, distance only: a single condition, no "and"
            use doc2 = JsonDocument.Parse (StacSearch.Json.searchBody { q with target = None })
            Expect.equal (doc2.RootElement.GetProperty("filter").GetProperty("op").GetString()) "and" "two distance bounds: and"
            use doc3 = JsonDocument.Parse (StacSearch.Json.searchBody { q with distanceKm = None })
            Expect.equal (doc3.RootElement.GetProperty("filter").GetProperty("op").GetString()) "a_contains" "target only: as before"
        }

        test "a catalog image's metadata passes the filters exactly as the same image in the library" {
            let dataDir = IO.Path.Combine(__SOURCE_DIRECTORY__, "data")
            let images =
                IO.Directory.EnumerateFiles(dataDir, "*.tif")
                |> Seq.map (fun path -> path, ProjectedImageApp.loadFile path)
                |> List.ofSeq
            Expect.isNonEmpty images "test images"
            let narrowed =
                [ SetUpper (RangeFilterKind.Distance, Aardvark.UI.Primitives.Numeric.SetValue 50.0)
                  SetLower (RangeFilterKind.PhaseAngle, Aardvark.UI.Primitives.Numeric.SetValue 30.0) ]
            for f in ImageFilter.initial :: (narrowed |> List.map (ImageFilter.update ImageFilter.initial)) do
                let b = ImageFilter.bounds f
                for path, img in images do
                    let values = ImageFilter.valuesOf (PRo3D.Core.InstrumentMetadata.tryParseMetadataForImagePath path)
                    Expect.equal values.distance img.distance "same distance"
                    Expect.equal values.resolution img.resolution "same resolution"
                    Expect.equal values.phaseAngle img.phaseAngle "same phase angle"
                    Expect.equal values.time img.time "same time"
                    Expect.equal (ImageFilter.passesValues b values) (ImageFilter.matches f img)
                        (sprintf "same decision for %s" (IO.Path.GetFileName path))
        }

        // Sidecars with zeros for attitude or target position must show up as a red
        // row, not vanish from the list.
        test "a sidecar without attitude or target position is reported, for MbiBased only" {
            let quat = QuaternionD(0.8, 0.1, 0.3, -0.5).Normalized
            let pos = V3d(12.0, -3.0, 7.0)
            Expect.isNone (StacSearch.pointingProblem ProjectionMethod.MbiBased quat pos) "complete sidecar"
            Expect.isSome (StacSearch.pointingProblem ProjectionMethod.MbiBased QuaternionD.Zero pos) "SC_QUAT zero"
            Expect.isSome (StacSearch.pointingProblem ProjectionMethod.MbiBased quat V3d.Zero) "TRG_POS zero"
            Expect.isNone (StacSearch.pointingProblem ProjectionMethod.Spice QuaternionD.Zero V3d.Zero)
                "Spice takes the pose from the kernels"
            Expect.isNone (StacSearch.pointingProblem ProjectionMethod.MbiAttitudeSpiceRange quat V3d.Zero)
                "MbiAttitudeSpiceRange takes the range from SPICE"
            Expect.isSome (StacSearch.pointingProblem ProjectionMethod.MbiAttitudeSpiceRange QuaternionD.Zero pos)
                "but needs the sidecar's attitude"
        }
    ]

/// The live catalog's sign-in, from PRO3D_STAC_USER / PRO3D_STAC_PASSWORD (never in the repo).
let private liveSignIn () =
    match Environment.GetEnvironmentVariable "PRO3D_STAC_USER", Environment.GetEnvironmentVariable "PRO3D_STAC_PASSWORD" with
    | null, _ | "", _ | _, null | _, "" -> None
    | user, password -> Some (StacSearch.basicAuth user (Secret.create password))

/// POST a raw search body to the PRO3D_STAC_URL catalog; the features and numberMatched.
/// Only counts leave this function: nothing of the catalog's content goes to the log.
let private searchRaw (body : string) : array<JsonElement> * int =
    use c = new HttpClient(Timeout = TimeSpan.FromSeconds 60.0)
    use request = new HttpRequestMessage(HttpMethod.Post, StacSearchModel.defaultServerUrl.Trim().TrimEnd('/') + "/search",
                                         Content = new StringContent(body, Text.Encoding.UTF8, "application/json"))
    liveSignIn () |> Option.iter (fun h -> request.Headers.Authorization <- h)
    use response = c.SendAsync(request).Result
    if not response.IsSuccessStatusCode then failtestf "search answered %d" (int response.StatusCode)
    use doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result)
    let root = doc.RootElement
    let features = [| for f in root.GetProperty("features").EnumerateArray() -> f.Clone() |]
    let matched =
        match root.TryGetProperty "numberMatched" with
        | true, n when n.ValueKind = JsonValueKind.Number -> n.GetInt32()
        | _ -> features.Length
    features, matched


// --- offline: downloads against a local server, the worker, the result list -------------

/// A minimal HTTP server on a free loopback port: `respond` gets the request path
/// and writes the whole answer (status line, headers, body) to the stream.
type private LocalServer(respond : string -> IO.Stream -> unit) =
    let listener = new Net.Sockets.TcpListener(Net.IPAddress.Loopback, 0)
    do listener.Start()
    let port = (listener.LocalEndpoint :?> Net.IPEndPoint).Port
    let requests = ref 0
    let cts = new Threading.CancellationTokenSource()
    let serveOne (client : Net.Sockets.TcpClient) =
        Threading.Tasks.Task.Run(fun () ->
            try
                use client = client
                use stream = client.GetStream()
                // read the request head
                let buffer = Array.zeroCreate<byte> 8192
                let head = Text.StringBuilder()
                while not (head.ToString().Contains "\r\n\r\n") do
                    let n = stream.Read(buffer, 0, buffer.Length)
                    if n = 0 then failwith "closed"
                    head.Append(Text.Encoding.ASCII.GetString(buffer, 0, n)) |> ignore
                // and a POST's body, whole: closing on unread bytes resets the
                // connection, and the client may then lose the answer
                let text = head.ToString()
                let headEnd = text.IndexOf "\r\n\r\n" + 4
                let contentLength =
                    text.Split([| "\r\n" |], StringSplitOptions.None)
                    |> Array.tryPick (fun line ->
                        let parts = line.Split([| ':' |], 2)
                        if parts.Length = 2 && parts.[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase) then
                            match Int32.TryParse(parts.[1].Trim()) with
                            | true, n -> Some n
                            | _ -> None
                        else None)
                    |> Option.defaultValue 0
                let mutable unread = contentLength - Text.Encoding.ASCII.GetByteCount(text.Substring headEnd)
                while unread > 0 do
                    let n = stream.Read(buffer, 0, min buffer.Length unread)
                    if n = 0 then unread <- 0 else unread <- unread - n
                let path = head.ToString().Split(' ').[1]
                Threading.Interlocked.Increment requests |> ignore
                respond path stream
            with _ -> ()) |> ignore
    let loop =
        Threading.Tasks.Task.Run(fun () ->
            try
                while not cts.IsCancellationRequested do
                    serveOne (listener.AcceptTcpClient())
            with _ -> ())
    member _.Url (path : string) = sprintf "http://127.0.0.1:%d%s" port path
    member _.Requests = requests.Value
    interface IDisposable with
        member _.Dispose() =
            cts.Cancel()
            listener.Stop()

let private answer (status : string) (body : byte[]) (stream : IO.Stream) =
    let head = sprintf "HTTP/1.1 %s\r\nContent-Length: %d\r\nConnection: close\r\n\r\n" status body.Length
    let h = Text.Encoding.ASCII.GetBytes head
    stream.Write(h, 0, h.Length)
    stream.Write(body, 0, body.Length)
    stream.Flush()

/// An item id of its own per test, its folder removed afterwards.
let private withItem (f : string -> unit) =
    let id = "test-" + Guid.NewGuid().ToString "N"
    try f id
    finally
        let dir = StacSearch.itemDirectory id
        if IO.Directory.Exists dir then IO.Directory.Delete(dir, true)

let private aScene (lonDeg : float) : StacSearch.Scene =
    let lon = Conversion.RadiansFromDegrees lonDeg
    {
        instruments = Map.empty; frame = "IAU_MARS"; body = "MARS"
        projectionMethod = ProjectionMethod.Spice
        point = V3d(cos lon, sin lon, 0.0) * 1000.0; normal = None
    }

let private aFilter : StacSearch.Filter =
    { serverUrl = "https://example.org/api"; from = None; until = None; bounds = ImageFilter.bounds ImageFilter.initial; incidence = Range1d(0.0, 180.0); otherTargets = false }

/// Waits (polling) until `cond` holds, failing after `ms`.
let private waitFor (ms : int) (what : string) (cond : unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()
    while not (cond ()) do
        if sw.ElapsedMilliseconds > int64 ms then failtestf "timed out waiting for %s" what
        Threading.Thread.Sleep 10

let private local =
    testList "STAC search (offline, local server)" [
        test "item folders: one per id, inside the cache, whatever the id" {
            let dir = StacSearch.itemDirectory
            let cache = StacSearch.cacheDirectory ()
            for id in [ ".."; "."; ""; "a/b:c"; "Item"; "item" ] do
                let d = dir id
                Expect.equal (IO.Path.GetDirectoryName d) cache (sprintf "%A lands in the cache folder" id)
                Expect.isFalse (IO.Path.GetFileName(d).StartsWith ".") (sprintf "%A: no dot name" id)
            Expect.notEqual (dir "Item") (dir "item") "ids differing in case get folders of their own (Windows)"
            Expect.notEqual (dir "a/b") (dir "a_b") "ids differing in an invalid character too"
            Expect.equal (dir "x") (dir "x") "stable"
        }

        test "the sun direction needs a sun position" {
            Expect.isNone (StacSearch.sunFromTarget V3d.Zero (V3d(10.0, 0.0, 0.0)))
                "no SUN_POS: unknown, not the direction to the camera"
            Expect.equal (StacSearch.sunFromTarget (V3d(1e8, 0.0, 0.0)) (V3d(10.0, 0.0, 0.0))) (Some (V3d(1e8 - 10.0, 0.0, 0.0)))
                "target -> sun"
            Expect.isSome (StacSearch.sunFromTarget (V3d(1e8, 0.0, 0.0)) V3d.Zero) "TRG_POS = 0: spacecraft -> sun"
        }

        test "other targets: listed after the footprint hits, from the time range alone, only when asked for" {
            // the first request is the footprint query, the second the time range alone
            let footprintHit = """
{ "type": "FeatureCollection", "features": [
    { "type": "Feature", "id": "hit", "collection": "c", "geometry": { "type": "Polygon", "coordinates": [[[0,0],[1,0],[1,1],[0,0]]] },
      "properties": { "datetime": "2026-01-01T00:00:00Z", "ssys:targets": ["Didymos"] },
      "assets": { "mbi": { "href": "https://example.org/h.mbi.json" }, "image": { "href": "https://example.org/h.tif" } } } ] }"""
            let serve (first : string) =
                let n = ref 0
                new LocalServer(fun _ stream ->
                    let i = Threading.Interlocked.Increment n
                    answer "200 OK" (Text.Encoding.UTF8.GetBytes (if i = 1 then first else withoutFootprint)) stream)
            let scene = { aScene 0.0 with body = "DIDYMOS"; frame = "IAU_DIDYMOS" }
            let list (server : LocalServer) (otherTargets : bool) =
                let filter = { aFilter with serverUrl = server.Url "/api"; otherTargets = otherTargets }
                match (StacSearch.listFor filter 100 scene).Result with
                | Ok l -> l
                | Result.Error e -> failtestf "listing failed: %s" e
            let ids (items : array<StacSearch.Item>) = items |> Array.map (fun i -> i.id)
            do
                use server = serve footprintHit
                let l = list server false
                Expect.equal (ids l.items, ids l.others, l.listing) ([| "hit" |], [||], ByFootprint) "off: the footprint hits alone"
                Expect.equal server.Requests 1 "off: no second query"
            do
                use server = serve footprintHit
                let l = list server true
                Expect.equal (ids l.items) [| "hit" |] "the footprint hits first"
                Expect.equal (ids l.others) [| "with-footprint"; "other-body" |] "then the other targets"
                Expect.equal server.Requests 2 "one more query"
            do
                // no footprint hit: the one time-range query serves both lists
                use server = serve """{ "type": "FeatureCollection", "features": [] }"""
                let l = list server true
                Expect.equal (ids l.items, l.listing) ([| "no-target"; "didymos" |], WithoutFootprint) "the footprint-less ones"
                Expect.equal (ids l.others) [| "with-footprint"; "other-body" |] "and the other targets"
                Expect.equal server.Requests 2 "the time-range query once"
        }

        test "two downloads of one file at once: one request, both get the file" {
            let body = Text.Encoding.ASCII.GetBytes "sidecar"
            use server = new LocalServer(fun _ stream ->
                Threading.Thread.Sleep 300
                answer "200 OK" body stream)
            withItem (fun id ->
                let url = server.Url "/data/x.mbi.json"
                let a = StacSearch.download id url
                let b = StacSearch.download id url
                let ra, rb = a.Result, b.Result
                Expect.equal ra rb "the same answer"
                match ra with
                | Ok path ->
                    Expect.equal (IO.File.ReadAllBytes path) body "the whole file"
                    Expect.isEmpty (IO.Directory.GetFiles(IO.Path.GetDirectoryName path, "*.part")) "no partial file left"
                | Result.Error e -> failtestf "download failed: %s" e
                Expect.equal server.Requests 1 "joined, not downloaded twice"
                // there now: no request at all
                Expect.isOk (StacSearch.download id url).Result "cached"
                Expect.equal server.Requests 1 "served from the cache")
        }

        test "a failed download is an error, and leaves nothing behind" {
            use server = new LocalServer(fun _ stream -> answer "404 Not Found" [||] stream)
            withItem (fun id ->
                match (StacSearch.download id (server.Url "/data/missing.tif")).Result with
                | Ok _ -> failtest "404 must not succeed"
                | Result.Error e -> Expect.stringContains e "404" "the status in the message"
                let dir = StacSearch.itemDirectory id
                if IO.Directory.Exists dir then
                    Expect.isEmpty (IO.Directory.GetFiles dir) "no file")
        }

        test "a stalled download times out instead of hanging" {
            use server = new LocalServer(fun _ stream ->
                // promise 1000 bytes, send 10, then nothing
                let h = Text.Encoding.ASCII.GetBytes "HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n0123456789"
                stream.Write(h, 0, h.Length)
                stream.Flush()
                Threading.Thread.Sleep 5000)
            let saved = StacSearch.downloadTimeout
            StacSearch.downloadTimeout <- TimeSpan.FromSeconds 1.0
            try
                withItem (fun id ->
                    let sw = Diagnostics.Stopwatch.StartNew()
                    match (StacSearch.download id (server.Url "/data/slow.tif")).Result with
                    | Ok _ -> failtest "an incomplete body must not succeed"
                    | Result.Error e -> Expect.stringContains e "within" "says it timed out"
                    Expect.isLessThan sw.Elapsed.TotalSeconds 4.0 "gave up after the timeout"
                    let dir = StacSearch.itemDirectory id
                    if IO.Directory.Exists dir then
                        Expect.isEmpty (IO.Directory.GetFiles dir) "no partial file left")
            finally
                StacSearch.downloadTimeout <- saved
        }

        test "worker: a point is searched once unless forced, and only a new point abandons a running search" {
            let posted = Collections.Concurrent.ConcurrentQueue<StacSearchMessage>()
            let started = Collections.Concurrent.ConcurrentQueue<float>()
            let abandoned = Collections.Concurrent.ConcurrentQueue<float>()
            let release = ref false
            let search (_ : StacSearch.Filter) (scene : StacSearch.Scene) (isStale : unit -> bool) (_ : StacSearch.Progress -> unit) =
                Threading.Tasks.Task.Run(fun () ->
                    let lon = Math.Round((StacSearch.lonLat scene.point).X, 6)
                    started.Enqueue lon
                    // runs until released or stale, like a long check
                    while not release.Value && not (isStale ()) do Threading.Thread.Sleep 5
                    if isStale () then
                        abandoned.Enqueue lon
                        Ok None
                    else Ok (Some StacDropped.none))
            let w = StacSearch.Worker(posted.Enqueue, search, 0)
            let finished () = posted |> Seq.filter (function QueryFinished _ -> true | _ -> false) |> Seq.length

            // a point, searched to the end
            release.Value <- true
            w.Search(aFilter, aScene 10.0)
            waitFor 3000 "the first search" (fun () -> finished () = 1)
            // the same point again: nothing
            w.Search(aFilter, aScene 10.0)
            Threading.Thread.Sleep 200
            Expect.equal started.Count 1 "the same point is not searched twice"
            // forced (a click): searched again
            w.SearchNow(aFilter, aScene 10.0)
            waitFor 3000 "the forced search" (fun () -> finished () = 2)
            // a changed filter re-runs the last point
            w.Rerun { aFilter with bounds = { aFilter.bounds with phaseAngle = Some (0.0, 60.0) } }
            waitFor 3000 "the rerun" (fun () -> finished () = 3)

            // a long search, then jitter on the spot: not abandoned
            release.Value <- false
            w.Search(aFilter, aScene 20.0)
            waitFor 3000 "the long search to start" (fun () -> started.Count = 4)
            w.Search(aFilter, aScene 20.0)
            Threading.Thread.Sleep 200
            Expect.isEmpty abandoned "the same point does not abandon the running search"
            // a different point: the running search is abandoned, the new one runs
            w.Search(aFilter, aScene 30.0)
            waitFor 3000 "the abandon" (fun () -> abandoned.Count = 1)
            waitFor 3000 "the new search to start" (fun () -> started.Count = 5)
            // cancel (results cleared): the running one stops, nothing finishes
            w.Cancel()
            waitFor 3000 "the cancel" (fun () -> abandoned.Count = 2)
            Expect.equal (finished ()) 3 "an abandoned or cancelled search reports no result"
            Expect.equal (List.ofSeq abandoned) [ 20.0; 30.0 ] "abandoned in order"
        }

        test "the result list keeps the best maxResults by pixel size, and counts all" {
            let n = StacSearchModel.maxResults + 50
            let candidate (i : int) : StacCandidate =
                {
                    itemId = sprintf "i%d" i; collection = "c"; title = sprintf "t%d" i
                    time = DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc); instrument = "CAM1"
                    assets = { mbi = "m"; image = "i"; imageMetadata = None; overview = None }
                    check =
                        // pixel sizes n .. 1: the finest last in the server's order
                        Resolved { rangeM = 1.0; pixelSizeM = float (n - i); pointIncidenceDeg = None
                                   pixel = V2d.Zero }
                    values = None
                    otherTarget = None
                }
            let red : StacCandidate = { candidate n with itemId = "red"; check = Unresolved "no attitude" }
            let s =
                { StacSearchModel.initial with
                    query = Some { seq = 7; body = "MARS"; lon = 0.0; lat = 0.0 }
                    status = StacSearchStatus.Checking (n + 1, n + 1, ByFootprint)
                    candidates = IndexList.ofList ([ for i in 0 .. n - 1 -> candidate i ] @ [ red ]) }
            let m = { ProjectedImageListApp.initial with stacSearch = s }
            let m' = ProjectedImageListApp.update m (StacMessage (QueryFinished (7, Ok StacDropped.none)))
            let shown = m'.stacSearch.candidates |> IndexList.toList
            Expect.equal shown.Length StacSearchModel.maxResults "cut to the best maxResults"
            let sizes = shown |> List.choose (fun c -> match c.check with Resolved v -> Some v.pixelSizeM | _ -> None)
            Expect.equal sizes (List.sort sizes) "finest first"
            Expect.equal (List.tryHead sizes) (Some 1.0) "the finest is in"
            match m'.stacSearch.status with
            | StacSearchStatus.Found (_, kept, _, dropped) ->
                Expect.equal kept n "kept counts the resolved images, all of them"
                Expect.equal dropped.unresolvedListed 1 "the red row counted apart"
            | other -> failtestf "not found: %A" other
            // an answer of an older search changes nothing
            let stale = ProjectedImageListApp.update m (StacMessage (QueryFinished (6, Ok StacDropped.none)))
            Expect.equal stale.stacSearch.candidates.Count (n + 1) "an older search's answer is dropped"
        }

        test "results sort by the image list's columns, again the other way round; unchecked rows last" {
            let resolved = Resolved { rangeM = 1.0; pixelSizeM = 1.0; pointIncidenceDeg = None; pixel = V2d.Zero }
            let candidate (id : string) (day : int) (values : Option<StacImageValues>) : StacCandidate =
                { itemId = id; collection = "c"; title = id; time = DateTime(2026, 11, day, 0, 0, 0, DateTimeKind.Utc); instrument = "afc"
                  assets = { mbi = "m"; image = "i"; imageMetadata = None; overview = None }
                  check = resolved; values = values; otherTarget = None }
            let v instrument d r = Some { instrument = instrument; distanceKm = d; resolution = r }
            let rows =
                IndexList.ofList [
                    candidate "near" 3 (v "AFC2" 5.0 (Some 0.5))
                    candidate "far" 1 (v "AFC1" 50.0 (Some 5.0))
                    candidate "unknown" 2 (v "HSH" 20.0 None)
                ]
            let s = { StacSearchModel.initial with candidates = rows }
            let m = { ProjectedImageListApp.initial with stacSearch = s }
            let sortBy key (m : ProjectedImageListModel) = ProjectedImageListApp.update m (StacMessage (SortCandidatesBy key))
            let ids (m : ProjectedImageListModel) = m.stacSearch.candidates |> IndexList.toList |> List.map (fun c -> c.itemId)
            let byDistance = sortBy SortDistance m
            Expect.equal (ids byDistance) [ "near"; "unknown"; "far" ] "distance, ascending"
            Expect.equal byDistance.stacSearch.sort (Some (SortDistance, true)) "remembered"
            Expect.equal (ids (sortBy SortDistance byDistance)) [ "far"; "unknown"; "near" ] "again: descending"
            Expect.equal (ids (sortBy SortResolution m)) [ "near"; "far"; "unknown" ] "resolution; none last"
            Expect.equal (ids (sortBy SortDate m)) [ "far"; "unknown"; "near" ] "date"
            Expect.equal (ids (sortBy SortInstrument m)) [ "far"; "near"; "unknown" ] "instrument, from the metadata"
            // a check's values are stored on its row
            let checking = { (candidate "x" 1 None) with check = StacCheck.Checking }
            let listed =
                { ProjectedImageListApp.initial with
                    stacSearch = { StacSearchModel.initial with query = Some { seq = 1; body = "DIMORPHOS"; lon = 0.0; lat = 0.0 }; candidates = IndexList.single checking } }
            let checkedM = ProjectedImageListApp.update listed (StacMessage (QueryChecked (1, "x", Some resolved, v "AFC1" 12.0 (Some 0.2))))
            match checkedM.stacSearch.candidates |> IndexList.tryFirst with
            | Some c -> Expect.equal c.values (v "AFC1" 12.0 (Some 0.2)) "the metadata values arrive with the check"
            | None -> failtest "the row is gone"
        }
    ]

/// The PRO3D_STAC_URL catalog, no time range.
let private catalog : StacSearch.Filter =
    { serverUrl = StacSearchModel.defaultServerUrl; from = None; until = None; bounds = ImageFilter.bounds ImageFilter.initial; incidence = Range1d(0.0, 180.0); otherTargets = false }

/// Skips unless the PRO3D_STAC_URL catalog answers; signs the session in when it asks
/// for that and PRO3D_STAC_USER / _PASSWORD are set, and says so when they are not --
/// a catalog behind a sign-in must not look like an unreachable one.
let private catalogReachable () =
    if String.IsNullOrWhiteSpace StacSearchModel.defaultServerUrl then skiptest "PRO3D_STAC_URL not set"
    let header = liveSignIn ()
    match (StacSearch.probe StacSearchModel.defaultServerUrl header).Result with
    | ProbeOk -> header |> Option.iter (StacSearch.signIn StacSearchModel.defaultServerUrl)
    | ProbeNeedsSignIn -> skiptest "the catalog (PRO3D_STAC_URL) asks to sign in: set PRO3D_STAC_USER and PRO3D_STAC_PASSWORD"
    | ProbeRejected -> failtest "the catalog (PRO3D_STAC_URL) refused PRO3D_STAC_USER / PRO3D_STAC_PASSWORD"
    | ProbeFailed e -> skiptestf "STAC catalog (PRO3D_STAC_URL) not usable: %s" e

/// The search finds images only through the catalog: the items need a footprint
/// (geometry) and the body in ssys:targets. Without them the server answers nothing
/// and no local pointing check ever runs -- the list stays empty. This needs no
/// kernels: it asks the catalog what the search would ask, and on failure says which
/// of the two is missing, with counts only.
let private catalogCoverage =
    testList "STAC catalog coverage (PRO3D_STAC_URL)" [
        for body in [ "didymos"; "dimorphos" ] do
          yield
            test (sprintf "the catalog finds %s images by footprint" body) {
                catalogReachable ()
                // four points around the equator (few requests, not to hammer the server): some footprint
                // of an approach or orbit image covers one of these points
                let points = [ for lon in [ -90.0; 0.0; 90.0; 180.0 ] -> lon, 0.0 ]
                let found =
                    points |> List.sumBy (fun (lon, lat) ->
                        let q : StacSearch.Query = { point = Some (V2d(lon, lat)); target = Some body; from = None; until = None; distanceKm = None; limit = 1 }
                        snd (searchRaw (StacSearch.Json.searchBody q)))
                if found = 0 then
                    // why: no item names the body, or none of those has a footprint
                    let byTarget, targeted =
                        searchRaw (sprintf """{"limit":100,"filter-lang":"cql2-json","filter":{"op":"a_contains","args":[{"property":"ssys:targets"},["%s"]]}}""" body)
                    let withFootprint =
                        byTarget |> Array.filter (fun f ->
                            match f.TryGetProperty "geometry" with
                            | true, g -> g.ValueKind = JsonValueKind.Object
                            | _ -> false)
                    // the newest items, whatever they show
                    let newest, _ = searchRaw """{"limit":200,"sortby":[{"field":"properties.datetime","direction":"desc"}]}"""
                    let noGeometry, noTargets =
                        newest |> Array.fold (fun (g, t) f ->
                            let hasGeometry =
                                match f.TryGetProperty "geometry" with
                                | true, x -> x.ValueKind = JsonValueKind.Object
                                | _ -> false
                            let hasTargets =
                                match f.GetProperty("properties").TryGetProperty "ssys:targets" with
                                | true, x -> x.ValueKind = JsonValueKind.Array && x.GetArrayLength() > 0
                                | _ -> false
                            (if hasGeometry then g else g + 1), (if hasTargets then t else t + 1)) (0, 0)
                    failtestf
                        "no %s image at %d points. Items with ssys:targets containing \"%s\": %d (%d of the first %d with a footprint). \
                         Of the newest %d items, %d have no footprint and %d no ssys:targets. \
                         The search needs both from the catalog; PRo3D's pointing check runs only on what the server returns."
                        body points.Length body targeted withFootprint.Length byTarget.Length
                        newest.Length noGeometry noTargets
            }

        // Whatever the footprints say: a point on Didymos must still get candidates,
        // the footprint-less items, which the pointing check then sorts out. Needs no
        // kernels -- only the listing is asked for. A Didymos-sized sphere; the body
        // name is what the surface's SPICE binding gives.
        yield test "a Didymos point gets candidates even without footprints, narrowed by time" {
            catalogReachable ()
            let scene : StacSearch.Scene =
                {
                    instruments = Map.empty; frame = "IAU_DIDYMOS"; body = "DIDYMOS"
                    projectionMethod = ProjectionMethod.Spice
                    point = V3d(390.0, 0.0, 0.0); normal = Some V3d.IOO
                }
            let list (filter : StacSearch.Filter) =
                match (StacSearch.listFor filter 100 scene).Result with
                | Ok r -> r.items, r.items.Length, r.listing
                | Result.Error e -> failtestf "search failed: %s" e
            let items, matched, listing = list catalog
            Expect.isGreaterThan matched 0 "candidates for Didymos, by footprint or without"
            if listing = WithoutFootprint then
                Expect.isTrue (items |> Array.forall (fun i -> not i.hasFootprint)) "only footprint-less items"
            // a time range narrows the candidates, without the test naming catalog dates:
            // from the newest item's day on, nothing older is listed
            let newest = items |> Array.fold (fun t i -> max t i.time) DateTime.MinValue
            let from = newest.Date
            let narrowed, _, _ = list { catalog with from = Some from }
            Expect.isNonEmpty narrowed "the newest day's items"
            Expect.isTrue (narrowed |> Array.forall (fun i -> i.time >= from)) "nothing before the range"
            Expect.isLessThanOrEqual narrowed.Length items.Length "the range narrows"
            let empty, _, _ = list { catalog with from = Some (newest.AddDays 1.0) }
            Expect.isEmpty empty "nothing after the newest item"
        }
    ]

/// Against the live STAC catalog named by PRO3D_STAC_URL and the HERA kernels: a Mars sphere stands in for a
/// Mars surface. Every image the search keeps must see the point where its own
/// pointing says -- and the images must *not* see the point mirrored east-west, which
/// is what a longitude convention mix-up between PRo3D's body-fixed frame and the
/// catalog's footprints would produce.
let private online =
    testList "STAC search (PRO3D_STAC_URL catalog + HERA kernels)" [
        test "images found at a Mars point see that point, and not its mirror" {
            if not HeraSpiceTests.hasHera then skiptest "HERA kernels not found"
            catalogReachable ()
            HeraSpiceTests.ensureOpsKernel ()

            let radius = 3396190.0
            let onSphere (lonDeg : float) (latDeg : float) =
                let lon, lat = Conversion.RadiansFromDegrees lonDeg, Conversion.RadiansFromDegrees latDeg
                V3d(cos lat * cos lon, cos lat * sin lon, sin lat) * radius
            let scene (p : V3d) : StacSearch.Scene =
                {
                    instruments = Map.empty; frame = "IAU_MARS"; body = "MARS"
                    projectionMethod = ProjectionMethod.MbiBased
                    point = p; normal = Some (Vec.normalize p)
                }

            // a Mars point to search at
            let lon, lat = 158.17, -43.43
            match (StacSearch.runToEnd catalog 100 (scene (onSphere lon lat))).Result with
            | Result.Error e -> failtestf "search failed: %s" e
            | Result.Ok (kept, matched, _) ->
                let resolved =
                    kept |> Array.choose (fun c -> match c.check with Resolved m -> Some (c, m) | _ -> None)
                // counts only: titles and per-image values are catalog content and must
                // not end up in console or CI logs
                printfn "[STAC] %d matched, %d kept, %d resolved" matched kept.Length resolved.Length
                Expect.isGreaterThan matched 0 "the catalog has footprints over this point"
                Expect.isGreaterThan resolved.Length (matched / 2)
                    "most footprints must really contain the point by the images' own pointing"
                resolved |> Array.iteri (fun i (_, m) ->
                    Expect.isGreaterThan m.pixelSizeM 0.0 (sprintf "pixel size of resolved image #%d" i)
                    Expect.isTrue (m.pointIncidenceDeg |> Option.forall (fun a -> a >= 0.0 && a <= 180.0)) (sprintf "incidence of resolved image #%d" i))

                let sees (p : V3d) (c : StacCandidate) =
                    match StacSearch.metrics (scene p) (StacSearch.localImagePath c.itemId c.assets) with
                    | Result.Ok (Some _) -> true
                    | _ -> false

                // The antipode projects into every full-disc frame: only the horizon
                // test keeps it out.
                let antipode = onSphere (lon - 180.0) (-lat)
                let seeingAntipode = resolved |> Array.filter (fun (c, _) -> sees antipode c)
                Expect.equal seeingAntipode.Length 0 "the far side is hidden (count of images seeing the antipode)"

                // Mirrored east-west, 44 degrees away: a full-disc frame legitimately sees
                // both, so ask the close frames, whose footprint is small. With a
                // longitude mix-up, the catalog's footprints and PRo3D's body-fixed
                // frame would disagree: these frames would see the mirror, not the point.
                let close = resolved |> Array.filter (fun (_, m) -> m.rangeM < 50.0e6)
                Expect.isNonEmpty close "close frames are among the hits"
                let mirrored = onSphere -lon lat
                let seeingMirror = close |> Array.filter (fun (c, _) -> sees mirrored c)
                Expect.equal seeingMirror.Length 0
                    "close frames must not see the east-west mirrored point (count)"

                // a newer point waiting stops the search at its next candidate
                let checkedCount = ref 0
                let listedCount = ref 0
                let report = function
                    | StacSearch.Listed (candidates, _, _, _) -> listedCount.Value <- candidates.Length
                    | StacSearch.Checked _ -> checkedCount.Value <- checkedCount.Value + 1
                let stale () = checkedCount.Value >= 1
                match (StacSearch.run catalog 100 (scene (onSphere lon lat)) stale report).Result with
                | Result.Ok finished ->
                    let finished = Option.isSome finished
                    Expect.isFalse finished "abandoned, not finished"
                    Expect.equal checkedCount.Value 1 "no candidate checked after the point went stale"
                    Expect.isGreaterThan listedCount.Value 1 "the server's list was reported first"
                | Result.Error e -> failtestf "search failed: %s" e
        }
    ]

// ---- signing in (docs/StacImageSearch.md, "Signing in") ----------------------------

/// four made-up items: sidecars that are not real ones, and a few PNG bytes
let private fakeData () =
    let dir = IO.Path.Combine(IO.Path.GetTempPath(), "pro3d-fake-stac-" + Guid.NewGuid().ToString "N")
    IO.Directory.CreateDirectory dir |> ignore
    for t in [ "20000101_120000"; "20000101_130000"; "20000101_140000"; "20000101_150000" ] do
        let name = "CAM_" + t
        IO.File.WriteAllText(IO.Path.Combine(dir, name + ".mbi.json"), "{}")
        IO.File.WriteAllBytes(IO.Path.Combine(dir, name + ".png"), [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy |])
        IO.File.WriteAllText(IO.Path.Combine(dir, name + ".png.json"), "{}")
    dir

/// made-up accounts of the fake catalog
let private reader : FakeStacCatalog.Account = { user = "tester"; password = "not-a-secret"; allowed = true }
let private outsider : FakeStacCatalog.Account = { user = "outsider"; password = "also-not-a-secret"; allowed = false }

let private readerHeader () = StacSearch.basicAuth reader.user (Secret.create reader.password)

/// A fake catalog of its own (item ids and cache folders of its own); afterwards the
/// session's sign-in is dropped and everything it downloaded removed.
let private withCatalog (accounts : Option<list<FakeStacCatalog.Account>>) (f : FakeStacCatalog.Server -> unit) =
    let dir = fakeData ()
    let prefix = "signin-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-"
    try
        use server = new FakeStacCatalog.Server({ FakeStacCatalog.options dir with accounts = accounts; idPrefix = prefix })
        f server
    finally
        StacSearch.signOut ()
        try IO.Directory.Delete(dir, true) with _ -> ()
        let cache = StacSearch.cacheDirectory ()
        if IO.Directory.Exists cache then
            for d in IO.Directory.GetDirectories(cache, prefix + "*") do
                try IO.Directory.Delete(d, true) with _ -> ()

let private allItems : StacSearch.Query = { point = None; target = None; from = None; until = None; distanceKm = None; limit = 100 }

/// the worker's messages, with a wait for the next ConnectFinished
let private connectMessages () =
    let messages = new Collections.Concurrent.BlockingCollection<StacSearchMessage>()
    let nextConnect (timeoutMs : int) =
        let deadline = DateTime.UtcNow.AddMilliseconds(float timeoutMs)
        let mutable found = None
        while found.IsNone && DateTime.UtcNow < deadline do
            let mutable message = Unchecked.defaultof<StacSearchMessage>
            if messages.TryTake(&message, 100) then
                match message with
                | ConnectFinished (user, probe) -> found <- Some (user, probe)
                | _ -> ()
        found
    messages, nextConnect

let private signIn =
    testList "signing in" [
        test "the catalog URL and user name are remembered, never a password" {
            let file = IO.Path.Combine(IO.Path.GetTempPath(), "pro3d-stac-remembered-" + Guid.NewGuid().ToString "N" + ".json")
            let original = StacSearchModel.rememberedFile
            StacSearchModel.rememberedFile <- file
            try
                Expect.equal (StacSearchModel.remembered ()) ("", "") "nothing yet"
                StacSearchModel.remember " http://127.0.0.1:9/api " (Some "tester")
                Expect.equal (StacSearchModel.remembered ()) ("http://127.0.0.1:9/api", "tester") "url and user"
                // an open catalog: the URL changes, the user stays
                StacSearchModel.remember "http://127.0.0.1:10/api" None
                Expect.equal (StacSearchModel.remembered ()) ("http://127.0.0.1:10/api", "tester") "user kept"
                let text = IO.File.ReadAllText file
                Expect.isFalse (text.ToLowerInvariant().Contains "password") "no password field"
                IO.File.WriteAllText(file, "not json")
                Expect.equal (StacSearchModel.remembered ()) ("", "") "a broken file is nothing, not a crash"
            finally
                StacSearchModel.rememberedFile <- original
                try IO.File.Delete file with _ -> ()
        }

        test "no password over plain http, and none inside the URL" {
            let messages, nextConnect = connectMessages ()
            let worker = StacSearch.Worker(messages.Add, (fun _ _ _ _ -> Threading.Tasks.Task.FromResult(Ok None)), 0)
            // not sent at all: example.invalid does not resolve, and must not be asked
            worker.Connect("http://example.invalid/api", Some ("tester", Secret.create "not-a-secret"))
            match nextConnect 10000 with
            | Some (_, ProbeFailed e) -> Expect.stringContains e "https" "says why"
            | other -> failtestf "plain http with a password must fail, got %A" other
            Expect.isTrue (StacSearch.allowsCredentials "https://example.invalid/api") "https"
            Expect.isTrue (StacSearch.allowsCredentials "http://127.0.0.1:9/api") "this computer"
            Expect.isFalse (StacSearch.allowsCredentials "http://example.invalid/api") "plain http elsewhere"
            match (StacSearch.probe "http://someone:secret@127.0.0.1:1/api" None).Result with
            | ProbeFailed e -> Expect.stringContains e "must not contain" "a URL with credentials in it"
            | other -> failtestf "expected a failure, got %A" other
        }

        test "a disconnect or another catalog drops the results, and no dialog follows" {
            let stac (msg : StacSearchMessage) (m : ProjectedImageListModel) = ProjectedImageListApp.update m (StacMessage msg)
            let row : StacCandidate =
                { itemId = "a"; collection = "c"; title = "t"; time = DateTime(2000, 1, 1); instrument = "CAM"
                  assets = { mbi = "https://example.org/a.mbi.json"; image = "https://example.org/a.tif"; imageMetadata = None; overview = None }
                  check = StacCheck.Checking; values = None; otherTarget = None }
            let connected =
                let m = ProjectedImageListModel.initial
                { m with stacSearch = { m.stacSearch with connection = Connected (Some "tester"); candidates = IndexList.single row } }
            for leave in [ Disconnect; SetServerUrl "https://example.org/other/api" ] do
                let off = connected |> stac leave
                Expect.isEmpty off.stacSearch.candidates (sprintf "%A: no results left to click" leave)
                Expect.equal ((off |> stac (Fetch "a")).stacSearch.downloading.Count) 0 "no download while disconnected"
                Expect.equal ((off |> stac (FetchFinished ("a", Result.Error StacSearch.signInNeeded))).stacSearch.connection) Disconnected "a late 401 opens no dialog"
        }

        test "a missing SPICE coverage is told apart from other failures" {
            // the wording of InstrumentProjection's SPICE errors
            Expect.isTrue (StacSearch.isSpiceCoverage "HERA_AFC-1 at 2000-01-01 (frame F): SPICE has no position of HERA relative to X at 2000-01-01 (no SPK coverage?)") "no position"
            Expect.isTrue (StacSearch.isSpiceCoverage "SPICE cannot rotate F to J2000 at 2000-01-01") "no rotation"
            Expect.isFalse (StacSearch.isSpiceCoverage "the sidecar's SC_QUAT is zero") "a sidecar problem is not SPICE's"
            Expect.isTrue (StacSearch.isSpiceCoverage "SPICE cannot place the image's target MARS relative to DIDYMOS at 2000-01-01") "an other target SPICE cannot place"
            Expect.isFalse (StacSearch.isSpiceCoverage "the sidecar names no TARGET, so an image of another body cannot be placed") "no TARGET is the sidecar's problem"
        }

        test "a password never prints" {
            let s = Secret.create "hunter2"
            for printed in [ string s; sprintf "%A" s; sprintf "%O" s; sprintf "%A" (SignIn ("tester", s)) ] do
                Expect.isFalse (printed.Contains "hunter2") (sprintf "printed as %s" printed)
            Expect.equal (Secret.reveal s) "hunter2" "Secret.reveal gives the text"
        }

        test "an open catalog connects without a sign-in" {
            withCatalog None (fun server ->
                Expect.equal (StacSearch.probe server.ApiUrl None).Result ProbeOk "open")
        }

        test "a protected catalog asks to sign in, and checks the password" {
            withCatalog (Some [ reader; outsider ]) (fun server ->
                Expect.equal (StacSearch.probe server.ApiUrl None).Result ProbeNeedsSignIn "no credentials: sign in"
                let wrong = StacSearch.basicAuth reader.user (Secret.create "wrong")
                Expect.equal (StacSearch.probe server.ApiUrl (Some wrong)).Result ProbeRejected "wrong password"
                Expect.equal (StacSearch.probe server.ApiUrl (Some (readerHeader ()))).Result ProbeOk "right password"
                match (StacSearch.probe server.ApiUrl (Some (StacSearch.basicAuth outsider.user (Secret.create outsider.password)))).Result with
                | ProbeFailed e -> Expect.stringContains e "403" "a known but refused account says so"
                | other -> failtestf "403 must fail, got %A" other
                // a probe does not sign the session in
                match (StacSearch.search server.ApiUrl allItems).Result with
                | Result.Error e -> Expect.isTrue (StacSearch.isSignInNeeded e) "still not signed in"
                | Ok _ -> failtest "a probe must not install credentials")
        }

        test "what is not a catalog fails with a reason" {
            withCatalog None (fun server ->
                match (StacSearch.probe (server.BaseUrl + "/nothing") None).Result with
                | ProbeFailed e -> Expect.stringContains e "404" "the status"
                | other -> failtestf "expected a failure, got %A" other)
            match (StacSearch.probe "http://127.0.0.1:1" None).Result with
            | ProbeFailed e -> Expect.stringContains e "not reachable" "unreachable"
            | other -> failtestf "expected a failure, got %A" other
            match (StacSearch.probe "ftp://example.invalid/api" None).Result with
            | ProbeFailed e -> Expect.stringContains e "http" "not http(s)"
            | other -> failtestf "expected a failure, got %A" other
        }

        test "signed in, the search pages and the downloads carry the credentials" {
            withCatalog (Some [ reader ]) (fun server ->
                StacSearch.signIn server.ApiUrl (readerHeader ())
                match (StacSearch.search server.ApiUrl allItems).Result with
                | Ok (items, matched) ->
                    Expect.equal items.Length 4 "all items, over two pages of three"
                    Expect.equal matched 4 "matched"
                    match items |> Array.tryHead with
                    | Some item -> Expect.isOk (StacSearch.download item.id item.assets.mbi).Result "a download"
                    | None -> failtest "no item"
                | Result.Error e -> failtestf "search failed: %s" e
                let requests = server.Requests
                Expect.isGreaterThanOrEqual (requests |> Array.filter (fun r -> r.path = "/api/search") |> Array.length) 2 "the next page was followed"
                Expect.all requests (fun r -> r.authorized && r.status = 200) "every request signed in and answered")
        }

        test "without the sign-in, searching and downloading say to sign in again" {
            withCatalog (Some [ reader ]) (fun server ->
                match (StacSearch.search server.ApiUrl allItems).Result with
                | Result.Error e -> Expect.isTrue (StacSearch.isSignInNeeded e) (sprintf "search: %s" e)
                | Ok _ -> failtest "must not succeed"
                match (StacSearch.download "signin-none" (server.BaseUrl + "/data/CAM_20000101_120000.png")).Result with
                | Result.Error e -> Expect.isTrue (StacSearch.isSignInNeeded e) (sprintf "download: %s" e)
                | Ok _ -> failtest "must not succeed")
        }

        test "the credentials go to the catalog's host only" {
            withCatalog (Some [ reader ]) (fun catalog ->
                StacSearch.signIn catalog.ApiUrl (readerHeader ())
                // another host, e.g. an asset link elsewhere
                withCatalog None (fun other ->
                    StacSearch.signIn catalog.ApiUrl (readerHeader ())
                    let id = "signin-elsewhere-" + Guid.NewGuid().ToString "N"
                    try
                        Expect.isOk (StacSearch.download id (other.BaseUrl + "/data/CAM_20000101_120000.png")).Result "downloaded"
                        Expect.isNonEmpty other.Requests "the other host was asked"
                        Expect.all other.Requests (fun r -> not r.authorized) "without the catalog's credentials"
                    finally
                        try IO.Directory.Delete(StacSearch.itemDirectory id, true) with _ -> ()))
        }

        test "thumbnails go through PRo3D, signed in, and only for listed items" {
            withCatalog (Some [ reader ]) (fun server ->
                StacSearch.signIn server.ApiUrl (readerHeader ())
                let id = "signin-thumb-" + Guid.NewGuid().ToString "N"
                try
                    StacSearch.registerOverview id (server.BaseUrl + "/data/CAM_20000101_120000.png")
                    let key = (StacSearch.overviewPath id).Substring StacSearch.overviewRoute.Length
                    match StacSearch.overviewFile key with
                    | Some path -> Expect.equal (IO.File.ReadAllBytes path) [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy |] "the overview"
                    | None -> failtest "no thumbnail"
                    Expect.all server.Requests (fun r -> r.authorized) "fetched signed in"
                    Expect.isNone (StacSearch.overviewFile "0123456789abcdef01234567") "an unknown key is nothing"
                finally
                    try IO.Directory.Delete(StacSearch.itemDirectory id, true) with _ -> ())
        }

        test "the worker connects, keeps a working sign-in, and a disconnect drops it" {
            withCatalog (Some [ reader ]) (fun server ->
                let messages, nextConnect = connectMessages ()
                let worker = StacSearch.Worker(messages.Add, (fun _ _ _ _ -> Threading.Tasks.Task.FromResult(Ok None)), 0)
                worker.Connect(server.ApiUrl, None)
                Expect.equal (nextConnect 10000) (Some (None, ProbeNeedsSignIn)) "asks to sign in"
                worker.Connect(server.ApiUrl, Some (reader.user, Secret.create reader.password))
                Expect.equal (nextConnect 10000) (Some (Some reader.user, ProbeOk)) "signed in"
                Expect.isOk (StacSearch.search server.ApiUrl allItems).Result "searches work"
                // a wrong password afterwards does not replace the working one
                worker.Connect(server.ApiUrl, Some (reader.user, Secret.create "wrong"))
                Expect.equal (nextConnect 10000) (Some (Some reader.user, ProbeRejected)) "rejected"
                Expect.isOk (StacSearch.search server.ApiUrl allItems).Result "still signed in"
                worker.Disconnect()
                match (StacSearch.search server.ApiUrl allItems).Result with
                | Result.Error e -> Expect.isTrue (StacSearch.isSignInNeeded e) "signed out"
                | Ok _ -> failtest "a disconnect must drop the sign-in")
        }

        test "an attempt overtaken by a disconnect installs nothing" {
            withCatalog (Some [ reader ]) (fun server ->
                let messages, nextConnect = connectMessages ()
                let worker = StacSearch.Worker(messages.Add, (fun _ _ _ _ -> Threading.Tasks.Task.FromResult(Ok None)), 0)
                worker.Connect(server.ApiUrl, Some (reader.user, Secret.create reader.password))
                worker.Disconnect()
                Expect.isNone (nextConnect 2000) "no answer for an attempt nobody waits for"
                match (StacSearch.search server.ApiUrl allItems).Result with
                | Result.Error e -> Expect.isTrue (StacSearch.isSignInNeeded e) "not signed in"
                | Ok _ -> failtest "the overtaken sign-in must not be installed")
        }

        test "the connection state follows the attempts" {
            let stac (msg : StacSearchMessage) (m : ProjectedImageListModel) = ProjectedImageListApp.update m (StacMessage msg)
            let connection (m : ProjectedImageListModel) = m.stacSearch.connection
            let m0 = ProjectedImageListModel.initial
            Expect.equal (connection m0) Disconnected "off at start"
            let m1 = m0 |> stac Connect
            Expect.equal (connection m1) Connecting "connecting"
            let m2 = m1 |> stac (ConnectFinished (None, ProbeNeedsSignIn))
            Expect.equal (connection m2) (SigningIn { message = None; busy = false }) "the dialog opens"
            let m3 = m2 |> stac (SignIn ("tester", Secret.create "wrong"))
            Expect.equal (connection m3) (SigningIn { message = None; busy = true }) "trying"
            Expect.equal m3.stacSearch.lastUser "tester" "the name is kept for the dialog"
            let m4 = m3 |> stac (ConnectFinished (Some "tester", ProbeRejected))
            match connection m4 with
            | SigningIn { message = Some _; busy = false } -> ()
            | other -> failtestf "a rejected password stays in the dialog with a message, got %A" other
            let m5 = m4 |> stac (SignIn ("tester", Secret.create "right")) |> stac (ConnectFinished (Some "tester", ProbeOk))
            Expect.equal (connection m5) (Connected (Some "tester")) "connected"
            // a 401 later: the dialog again
            let m6 = { m5 with stacSearch = { m5.stacSearch with query = Some { seq = 7; body = "X"; lon = 0.0; lat = 0.0 } } }
            match connection (m6 |> stac (QueryFinished (7, Result.Error StacSearch.signInNeeded))) with
            | SigningIn { message = Some _; busy = false } -> ()
            | other -> failtestf "a 401 during the session reopens the dialog, got %A" other
            // an answer nobody waits for changes nothing
            let off = m5 |> stac Disconnect
            Expect.equal (connection off) Disconnected "disconnected"
            Expect.equal (connection (off |> stac (ConnectFinished (Some "tester", ProbeOk)))) Disconnected "a late answer is ignored"
            // the first attempt failing: off, with the reason
            match connection (m1 |> stac (ConnectFinished (None, ProbeFailed "unreachable"))) with
            | ConnectionFailed e -> Expect.equal e "unreachable" "the reason"
            | other -> failtestf "expected ConnectionFailed, got %A" other
            // another catalog: the sign-in was for the old one
            Expect.equal (connection (m5 |> stac (SetServerUrl "http://127.0.0.1:9/api"))) Disconnected "a new URL disconnects"
        }
    ]

/// The Download Directory row: where downloads go, what is remembered, and what the
/// trash may delete.
let private downloadDirectory =
    testList "download directory" [
        test "item folder names are recognized, nothing else" {
            for id in [ "item"; "a/b:c"; ".."; ""; "Item_X" ] do
                let name = IO.Path.GetFileName(StacSearch.itemDirectory id)
                Expect.isTrue (StacSearch.isItemFolderName name) (sprintf "%A -> %s is an item folder" id name)
            for name in [ "stac"; "Documents"; "foo_123"; "foo_DEADBEEF"; "foodeadbeef"; "deadbeef1"; "x_deadbee" ] do
                Expect.isFalse (StacSearch.isItemFolderName name) (sprintf "%s is not an item folder" name)
        }

        test "downloads go into the chosen folder's stac subfolder; the trash deletes only item folders" {
            let original = StacSearch.downloadRoot
            let root = IO.Path.Combine(IO.Path.GetTempPath(), "pro3d-stac-download-" + Guid.NewGuid().ToString "N")
            try
                StacSearch.setDownloadRoot root
                let cache = StacSearch.cacheDirectory ()
                Expect.equal cache (IO.Path.Combine(root, "stac")) "the stac subfolder"
                Expect.isTrue (IO.Directory.Exists cache) "created at once, for the folder icon"
                let item = StacSearch.itemDirectory "some-item"
                Expect.equal (IO.Path.GetDirectoryName item) cache "items land in it"
                IO.Directory.CreateDirectory item |> ignore
                IO.File.WriteAllText(IO.Path.Combine(item, "x.png"), "x")
                let foreign = IO.Path.Combine(cache, "my-own-folder")
                IO.Directory.CreateDirectory foreign |> ignore
                let loose = IO.Path.Combine(cache, "notes.txt")
                IO.File.WriteAllText(loose, "keep")
                let beside = IO.Path.Combine(root, "beside")
                IO.Directory.CreateDirectory beside |> ignore
                StacSearch.clearCache ()
                Expect.isFalse (IO.Directory.Exists item) "the item folder is deleted"
                Expect.isTrue (IO.Directory.Exists foreign) "a folder not made by PRo3D stays"
                Expect.isTrue (IO.File.Exists loose) "a loose file stays"
                Expect.isTrue (IO.Directory.Exists beside) "the chosen folder's other folders stay"
            finally
                StacSearch.downloadRoot <- original
                try IO.Directory.Delete(root, true) with _ -> ()
        }

        test "the download directory is remembered beside the catalog and user" {
            let file = IO.Path.Combine(IO.Path.GetTempPath(), "pro3d-stac-remembered-" + Guid.NewGuid().ToString "N" + ".json")
            let original = StacSearchModel.rememberedFile
            StacSearchModel.rememberedFile <- file
            try
                Expect.equal (StacSearchModel.initialDownloadDirectory ()) (StacSearchModel.defaultDownloadDirectory ()) "the temp folder by default"
                StacSearchModel.remember "http://127.0.0.1:9/api" (Some "tester")
                let dir = IO.Path.Combine(IO.Path.GetTempPath(), "pro3d-chosen")
                StacSearchModel.rememberDownloadDirectory dir
                Expect.equal (StacSearchModel.initialDownloadDirectory ()) dir "the chosen folder next time"
                Expect.equal (StacSearchModel.remembered ()) ("http://127.0.0.1:9/api", "tester") "url and user kept"
                StacSearchModel.remember "http://127.0.0.1:10/api" None
                Expect.equal (StacSearchModel.rememberedDownloadDirectory ()) dir "a connect keeps the folder"
                StacSearchModel.rememberDownloadDirectory "not/rooted"
                Expect.equal (StacSearchModel.initialDownloadDirectory ()) (StacSearchModel.defaultDownloadDirectory ()) "an unusable path falls back"
            finally
                StacSearchModel.rememberedFile <- original
                try IO.File.Delete file with _ -> ()
        }

        test "another folder clears the results; the trash asks first" {
            let stac (msg : StacSearchMessage) (m : ProjectedImageListModel) = ProjectedImageListApp.update m (StacMessage msg)
            let row : StacCandidate =
                { itemId = "a"; collection = "c"; title = "t"; time = DateTime(2000, 1, 1); instrument = "CAM"
                  assets = { mbi = "https://example.org/a.mbi.json"; image = "https://example.org/a.tif"; imageMetadata = None; overview = None }
                  check = StacCheck.Checking; values = None; otherTarget = None }
            let m =
                let m = ProjectedImageListModel.initial
                { m with stacSearch = { m.stacSearch with candidates = IndexList.single row; fetched = HashMap.single "a" (Guid.NewGuid()) } }
            let same = m |> stac (SetDownloadDirectory m.stacSearch.downloadDirectory)
            Expect.equal same.stacSearch.candidates.Count 1 "the same folder changes nothing"
            let moved = m |> stac (SetDownloadDirectory "/somewhere/else")
            Expect.equal moved.stacSearch.downloadDirectory "/somewhere/else" "the new folder"
            Expect.isTrue moved.stacSearch.candidates.IsEmpty "the results go"
            let asking = m |> stac ClearCache
            Expect.isTrue asking.stacSearch.confirmClearCache "the dialog opens"
            Expect.isFalse asking.stacSearch.fetched.IsEmpty "nothing dropped yet"
            let cancelled = asking |> stac CancelClearCache
            Expect.isFalse cancelled.stacSearch.confirmClearCache "closed"
            Expect.isFalse cancelled.stacSearch.fetched.IsEmpty "nothing dropped"
            let confirmed = asking |> stac ConfirmClearCache
            Expect.isFalse confirmed.stacSearch.confirmClearCache "closed"
            Expect.isTrue confirmed.stacSearch.fetched.IsEmpty "the downloads' images go"
        }
    ]

let tests () =
    // All of it one at a time: the live tests in parallel hit the server at once and time
    // out, and the sign-in tests and the live ones share the session's credentials.
    testSequenced (testList "STAC image search" [ offline; local; signIn; downloadDirectory; catalogCoverage; online ])
