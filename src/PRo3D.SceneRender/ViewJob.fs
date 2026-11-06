namespace PRo3D.SceneRender

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

open Aardvark.Base

/// One image of a render-view job: a named camera, an optional sun, size and LoD.
type ViewSpec =
    {
        name     : string
        camera   : ViewCamera
        sun      : Option<SunInput>
        settings : RenderSettings
    }

/// A render-view job, resolved: everything the batch needs.
type ViewJob =
    {
        /// Which frame all numbers are in -- a label only, never interpreted.
        coordinatesAreIn : Option<string>
        out              : string
        opcs             : list<OpcInput>
        views            : list<ViewSpec>
    }

/// One image written by ViewJob.run, as listed in the manifest.
type RenderedView =
    {
        view         : ViewSpec
        file         : string
        /// `up` as rendered: orthogonalised against `forward`
        up           : V3d
        /// towards the sun from the OPCs' centre, as rendered; None without sun
        sunDirection : Option<V3d>
    }

/// A view as the job file has it; size and LoD default to the job's top level.
type ViewFile =
    {
        name     : string
        location : V3d
        forward  : V3d
        up       : V3d
        fov      : float
        width    : Option<int>
        height   : Option<int>
        lodLevel : Option<int>
        sun      : Option<SunInput>
    }

/// The top level of a render-view job file, each field optional so that the command
/// line can fill in or override it.
type ViewJobFile =
    {
        coordinatesAreIn : Option<string>
        out              : Option<string>
        opcs             : Option<list<OpcInput>>
        width            : Option<int>
        height           : Option<int>
        lodLevel         : Option<int>
        views            : Option<list<ViewFile>>
    }

/// What the command line can change in a job: the top level, sun off, a view selection.
type ViewJobOverrides =
    {
        out      : Option<string>
        width    : Option<int>
        height   : Option<int>
        lodLevel : Option<int>
        noSun    : bool
        /// view names, `*` / `?` wildcards; None: every view
        views    : Option<list<string>>
    }

module ViewJob =

    open JsonRead

    let noOverrides : ViewJobOverrides =
        { out = None; width = None; height = None; lodLevel = None; noSun = false; views = None }

    // ---- reading ---------------------------------------------------------------------

    let private sunKeys  = [ "position"; "shadows" ]
    let private opcKeys  = [ "path"; "trafo"; "textured" ]
    let private viewKeys = [ "name"; "location"; "forward"; "up"; "fov"; "width"; "height"; "lodLevel"; "sun" ]
    let private jobKeys  = [ "coordinatesAreIn"; "out"; "opcs"; "width"; "height"; "lodLevel"; "views" ]

    let private required (where : string) (name : string) (r : Result<Option<'a>, string>) : Result<'a, string> =
        match r with
        | Ok (Some v) -> Ok v
        | Ok None -> Result.Error (sprintf "%s: '%s' is required" where name)
        | Result.Error e -> Result.Error (sprintf "%s: %s" where e)

    let private within (where : string) (r : Result<'a, string>) =
        r |> Result.mapError (sprintf "%s: %s" where)

    let private readSun (where : string) (n : JsonNode) : Result<SunInput, string> =
        object' where n
        |> Result.bind (fun o ->
            onlyKnown where sunKeys o
            |> Result.bind (fun () -> required where "position" (field o "position" vec3))
            |> Result.bind (fun position ->
                field o "shadows" bool' |> within where
                |> Result.map (fun shadows -> ({ position = position; shadows = Option.defaultValue true shadows } : SunInput))))

    /// world = M · p_opc must be invertible to be a placement.
    let private toTrafo (where : string) (m : M44d) : Result<Trafo3d, string> =
        let det = m.Determinant
        if not (Double.IsFinite det) || abs det < 1e-12 then
            Result.Error (sprintf "%s: 'trafo' is not invertible (determinant %g)" where det)
        else Ok (Trafo3d(m, m.Inverse))

    let private readOpc (baseDir : string) (i : int) (n : JsonNode) : Result<OpcInput, string> =
        let where = sprintf "opcs[%d]" i
        object' where n
        |> Result.bind (fun o ->
            onlyKnown where opcKeys o
            |> Result.bind (fun () -> required where "path" (field o "path" str))
            |> Result.bind (fun path ->
                field o "trafo" m44 |> within where
                |> Result.bind (function
                    | None -> Ok None
                    | Some m -> toTrafo where m |> Result.map Some)
                |> Result.bind (fun trafo ->
                    field o "textured" bool' |> within where
                    |> Result.map (fun textured ->
                        ({ path = Path.GetFullPath(Path.Combine(baseDir, path)); trafo = trafo
                           textured = Option.defaultValue true textured } : OpcInput)))))

    let private readView (i : int) (n : JsonNode) : Result<ViewFile, string> =
        let where = sprintf "views[%d]" i
        object' where n
        |> Result.bind (fun o ->
            match onlyKnown where viewKeys o with
            | Result.Error e -> Result.Error e
            | Ok () ->
            let where = match o.TryGetPropertyValue "name" with | true, (:? JsonValue as v) -> sprintf "%s ('%O')" where v | _ -> where
            match required where "name" (field o "name" str), required where "location" (field o "location" vec3),
                  required where "forward" (field o "forward" vec3), required where "up" (field o "up" vec3),
                  required where "fov" (field o "fov" float') with
            | Ok name, Ok location, Ok forward, Ok up, Ok fov ->
                match field o "width" int' |> within where, field o "height" int' |> within where,
                      field o "lodLevel" int' |> within where,
                      (match o.TryGetPropertyValue "sun" with
                       | true, null -> Ok None
                       | true, s -> readSun (where + ".sun") s |> Result.map Some
                       | _ -> Ok None) with
                | Ok w, Ok h, Ok l, Ok sun ->
                    Ok ({ name = name; location = location; forward = forward; up = up; fov = fov
                          width = w; height = h; lodLevel = l; sun = sun } : ViewFile)
                | Result.Error e, _, _, _ | _, Result.Error e, _, _ | _, _, Result.Error e, _ | _, _, _, Result.Error e -> Result.Error e
            | Result.Error e, _, _, _, _ | _, Result.Error e, _, _, _ | _, _, Result.Error e, _, _
            | _, _, _, Result.Error e, _ | _, _, _, _, Result.Error e -> Result.Error e)

    let private arrayOf (name : string) (read : int -> JsonNode -> Result<'a, string>) (n : JsonNode) : Result<list<'a>, string> =
        match n with
        | :? JsonArray as a -> a |> Seq.mapi read |> List.ofSeq |> sequence
        | _ -> Result.Error (sprintf "'%s' must be an array" name)

    /// Parses a job file's text; relative paths are resolved against `baseDir`.
    let parse (baseDir : string) (json : string) : Result<ViewJobFile, string> =
        parseObject json
        |> Result.bind (fun o ->
            match onlyKnown "the job file" jobKeys o with
            | Result.Error e -> Result.Error e
            | Ok () ->
            match field o "coordinatesAreIn" str, field o "out" str, field o "opcs" (fun n v -> arrayOf n (readOpc baseDir) v),
                  field o "width" int', field o "height" int', field o "lodLevel" int', field o "views" (fun n v -> arrayOf n readView v) with
            | Ok c, Ok out, Ok opcs, Ok w, Ok h, Ok l, Ok views ->
                Ok ({ coordinatesAreIn = c; out = out |> Option.map (fun p -> Path.GetFullPath(Path.Combine(baseDir, p)))
                      opcs = opcs; width = w; height = h; lodLevel = l; views = views } : ViewJobFile)
            | Result.Error e, _, _, _, _, _, _ | _, Result.Error e, _, _, _, _, _ | _, _, Result.Error e, _, _, _, _
            | _, _, _, Result.Error e, _, _, _ | _, _, _, _, Result.Error e, _, _ | _, _, _, _, _, Result.Error e, _
            | _, _, _, _, _, _, Result.Error e -> Result.Error e)

    let load (path : string) : Result<ViewJobFile * string, string> =
        let full = Path.GetFullPath path
        if not (File.Exists full) then Result.Error (sprintf "no job file at %s" full)
        else
            let text =
                try Ok (File.ReadAllText full)
                with e -> Result.Error e.Message
            text
            |> Result.bind (parse (Path.GetDirectoryName full))
            |> Result.map (fun f -> f, full)
            |> Result.mapError (sprintf "%s: %s" full)

    // ---- resolving -------------------------------------------------------------------

    let private sequenceList = sequence

    /// The job with the command line applied: `o` over the file's top level, each view's
    /// own values over both. Validates everything a render would trip over, so that a
    /// bad view fails before the first image. `jobPath` names the default output.
    let resolve (o : ViewJobOverrides) (jobPath : string) (f : ViewJobFile) : Result<ViewJob, string> =
        let (<|>) (a : Option<'a>) (b : Option<'a>) = match a with Some _ -> a | None -> b
        let width    = o.width <|> f.width |> Option.defaultValue RenderSettings.defaults.size.X
        let height   = o.height <|> f.height |> Option.defaultValue RenderSettings.defaults.size.Y
        let lodLevel = o.lodLevel <|> f.lodLevel
        let out =
            o.out <|> f.out
            |> Option.defaultValue (Path.Combine(Path.GetDirectoryName jobPath, Path.GetFileNameWithoutExtension jobPath + "-renders"))
        let toView (v : ViewFile) : Result<ViewSpec, string> =
            let where = sprintf "view '%s'" v.name
            let size = V2i(v.width |> Option.defaultValue width, v.height |> Option.defaultValue height)
            let lod = v.lodLevel <|> lodLevel
            let camera : ViewCamera = { location = v.location; forward = v.forward; up = v.up; fov = v.fov }
            if size.X <= 0 || size.Y <= 0 then Result.Error (sprintf "%s: image size must be positive, got %dx%d" where size.X size.Y)
            else
                match lod, ViewCamera.toCameraView camera with
                | Some l, _ when l < 0 -> Result.Error (sprintf "%s: LoD level must be >= 0, got %d" where l)
                | _, Result.Error e -> Result.Error (sprintf "%s: %s" where e)
                | _, Ok _ ->
                    Ok ({ name = v.name; camera = camera
                          sun = if o.noSun then None else v.sun
                          settings = ({ size = size; lodLevel = lod } : RenderSettings) } : ViewSpec)
        match f.opcs, f.views with
        | None, _ | Some [], _ -> Result.Error "no OPCs given ('opcs' in the job file)"
        | _, None | _, Some [] -> Result.Error "no views given ('views' in the job file)"
        | Some opcs, Some views ->
            match opcs |> List.tryFind (fun x -> not (Directory.Exists x.path)) with
            | Some missing -> Result.Error (sprintf "no OPC directory at %s" missing.path)
            | None ->
            // file names: unique on case-insensitive file systems, no path characters
            let invalid = Path.GetInvalidFileNameChars()
            let badName = views |> List.tryFind (fun v -> String.IsNullOrWhiteSpace v.name || v.name.IndexOfAny invalid >= 0)
            let dup =
                views |> List.countBy (fun v -> v.name.ToLowerInvariant()) |> List.tryFind (fun (_, n) -> n > 1)
            match badName, dup with
            | Some v, _ -> Result.Error (sprintf "view name '%s' cannot be a file name" v.name)
            | _, Some (n, _) -> Result.Error (sprintf "two views are named '%s' (names are file names)" n)
            | None, None ->
            let selected =
                match o.views with
                | None -> Ok views
                | Some patterns ->
                    let matches (p : string) (v : ViewFile) = (Bookmarks.wildcard p).IsMatch v.name
                    match patterns |> List.filter (fun p -> views |> List.exists (matches p) |> not) with
                    | [] -> Ok (views |> List.filter (fun v -> patterns |> List.exists (fun p -> matches p v)))
                    | missing ->
                        Result.Error (sprintf "no view matches %s; views: %s"
                                        (missing |> List.map (sprintf "'%s'") |> String.concat ", ")
                                        (views |> List.map (fun v -> v.name) |> String.concat ", "))
            selected
            |> Result.bind (List.map toView >> sequenceList)
            |> Result.map (fun vs -> ({ coordinatesAreIn = f.coordinatesAreIn; out = out; opcs = opcs; views = vs } : ViewJob))

    // ---- running ---------------------------------------------------------------------

    let manifestName = "renders.json"

    let private v3 (v : V3d) =
        let a = JsonArray()
        a.Add(JsonValue.Create v.X); a.Add(JsonValue.Create v.Y); a.Add(JsonValue.Create v.Z)
        a

    let private manifestJson (job : ViewJob) (images : list<RenderedView>) =
        let o = JsonObject()
        o["coordinatesAreIn"] <- (match job.coordinatesAreIn with Some c -> JsonValue.Create c :> JsonNode | None -> null)
        let opcs = JsonArray()
        for x in job.opcs do
            let e = JsonObject()
            e["path"] <- JsonValue.Create x.path
            e["textured"] <- JsonValue.Create x.textured
            e["trafo"] <-
                match x.trafo with
                | Some t ->
                    let a = JsonArray()
                    for r in 0 .. 3 do
                        for c in 0 .. 3 do a.Add(JsonValue.Create t.Forward.[r, c])
                    a :> JsonNode
                | None -> null
            opcs.Add e
        o["opcs"] <- opcs
        let arr = JsonArray()
        for i in images do
            let v = i.view
            let e = JsonObject()
            e["name"]     <- JsonValue.Create v.name
            e["file"]     <- JsonValue.Create (Path.GetFileName i.file)
            e["location"] <- v3 v.camera.location
            e["forward"]  <- v3 v.camera.forward.Normalized
            e["up"]       <- v3 i.up
            e["fov"]      <- JsonValue.Create v.camera.fov
            e["width"]    <- JsonValue.Create v.settings.size.X
            e["height"]   <- JsonValue.Create v.settings.size.Y
            e["lodLevel"] <- (match v.settings.lodLevel with Some l -> JsonValue.Create l :> JsonNode | None -> null)
            e["sun"] <-
                match v.sun, i.sunDirection with
                | Some s, Some d ->
                    let so = JsonObject()
                    so["position"] <- v3 s.position
                    so["direction"] <- v3 d
                    so["shadows"] <- JsonValue.Create s.shadows
                    so :> JsonNode
                | _ -> null
            arr.Add e
        o["views"] <- arr
        o.ToJsonString(JsonSerializerOptions(WriteIndented = true))

    /// Renders every view of `job` with `renderer` (opened on job.opcs), writes
    /// `<view name>.png` per view into job.out and a manifest (renders.json) listing them.
    let run (log : string -> unit) (renderer : OpcRenderer) (job : ViewJob) : Result<list<RenderedView>, string> =
        Directory.CreateDirectory job.out |> ignore
        let total = List.length job.views
        let images =
            job.views |> List.mapi (fun i v ->
                let up =
                    match ViewCamera.toCameraView v.camera with
                    | Ok (view, turned) ->
                        if turned > 0.01 then log (sprintf "%s: up turned by %.3f deg to be orthogonal to forward" v.name turned)
                        view.Up
                    | Result.Error _ -> v.camera.up   // resolve validated it
                let file = Path.Combine(job.out, v.name + ".png")
                let image = renderer.Render(v.camera, v.sun, v.settings)
                image.Save file
                log (sprintf "[%d/%d] %s -> %s" (i + 1) total v.name file)
                ({ view = v; file = file; up = up; sunDirection = v.sun |> Option.bind (SunInput.direction renderer.Bounds) } : RenderedView))
        File.WriteAllText(Path.Combine(job.out, manifestName), manifestJson job images)
        Ok images
