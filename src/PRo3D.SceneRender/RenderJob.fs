namespace PRo3D.SceneRender

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

open Aardvark.Base
open PRo3D.Core

/// Total readers for job files: every failure is an Error naming the field.
module internal JsonRead =

    let sequence (xs : list<Result<'a, string>>) : Result<list<'a>, string> =
        List.foldBack (fun x acc ->
            match x, acc with
            | Ok v, Ok vs -> Ok (v :: vs)
            | Result.Error e, _ | _, Result.Error e -> Result.Error e) xs (Ok [])

    let str (field : string) (n : JsonNode) : Result<string, string> =
        match n with
        | :? JsonValue as v ->
            match v.TryGetValue<string>() with
            | true, s -> Ok s
            | _ -> Result.Error (sprintf "'%s' must be a string" field)
        | _ -> Result.Error (sprintf "'%s' must be a string" field)

    let int' (field : string) (n : JsonNode) : Result<int, string> =
        match n with
        | :? JsonValue as v ->
            match v.TryGetValue<int>() with
            | true, i -> Ok i
            | _ -> Result.Error (sprintf "'%s' must be an integer" field)
        | _ -> Result.Error (sprintf "'%s' must be an integer" field)

    /// A list of strings; a single string is accepted as a list of one.
    let strings (field : string) (n : JsonNode) : Result<list<string>, string> =
        match n with
        | :? JsonArray as a ->
            a |> Seq.mapi (fun i x ->
                    match x with
                    | null -> Result.Error (sprintf "'%s'[%d] is null" field i)
                    | x -> str (sprintf "%s[%d]" field i) x)
              |> List.ofSeq |> sequence
        | n -> str field n |> Result.map List.singleton

    let field (o : JsonObject) (name : string) (read : string -> JsonNode -> Result<'a, string>) : Result<Option<'a>, string> =
        match o.TryGetPropertyValue name with
        | true, null -> Ok None
        | true, n -> read name n |> Result.map Some
        | _ -> Ok None

    /// Misspelt keys would otherwise be ignored and the default silently used.
    let onlyKnown (where : string) (known : list<string>) (o : JsonObject) : Result<unit, string> =
        let unknown = o |> Seq.map (fun kv -> kv.Key) |> Seq.filter (fun k -> List.contains k known |> not) |> List.ofSeq
        match unknown with
        | [] -> Ok ()
        | ks -> Result.Error (sprintf "unknown key%s %s in %s (known: %s)"
                                (if ks.Length > 1 then "s" else "")
                                (ks |> List.map (sprintf "'%s'") |> String.concat ", ")
                                where (String.concat ", " known))

    let float' (field : string) (n : JsonNode) : Result<float, string> =
        match n with
        | :? JsonValue as v ->
            match v.TryGetValue<double>() with
            | true, f when Double.IsFinite f -> Ok f
            | true, _ -> Result.Error (sprintf "'%s' must be a finite number" field)
            | _ -> Result.Error (sprintf "'%s' must be a number" field)
        | _ -> Result.Error (sprintf "'%s' must be a number" field)

    let bool' (field : string) (n : JsonNode) : Result<bool, string> =
        match n with
        | :? JsonValue as v ->
            match v.TryGetValue<bool>() with
            | true, b -> Ok b
            | _ -> Result.Error (sprintf "'%s' must be true or false" field)
        | _ -> Result.Error (sprintf "'%s' must be true or false" field)

    /// Exactly `count` numbers.
    let floats (count : int) (field : string) (n : JsonNode) : Result<float[], string> =
        match n with
        | :? JsonArray as a when a.Count = count ->
            a |> Seq.mapi (fun i x ->
                    match x with
                    | null -> Result.Error (sprintf "'%s'[%d] is null" field i)
                    | x -> float' (sprintf "%s[%d]" field i) x)
              |> List.ofSeq |> sequence |> Result.map Array.ofList
        | _ -> Result.Error (sprintf "'%s' must be an array of %d numbers" field count)

    /// [x, y, z]
    let vec3 (field : string) (n : JsonNode) : Result<V3d, string> =
        floats 3 field n |> Result.map (fun a -> V3d(a.[0], a.[1], a.[2]))

    /// 16 numbers, row-major: the first four are the matrix's first row.
    let m44 (field : string) (n : JsonNode) : Result<M44d, string> =
        floats 16 field n |> Result.map M44d

    /// An object node, or an error saying what `where` must be.
    let object' (where : string) (n : JsonNode) : Result<JsonObject, string> =
        match n with
        | :? JsonObject as o -> Ok o
        | _ -> Result.Error (sprintf "%s must be an object" where)

    /// Parses JSON text that must hold an object; comments and trailing commas allowed.
    let parseObject (json : string) : Result<JsonObject, string> =
        let node =
            try Ok (JsonNode.Parse(json, documentOptions = JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)))
            with e -> Result.Error (sprintf "not valid JSON: %s" e.Message)
        match node with
        | Ok (:? JsonObject as o) -> Ok o
        | Ok _ -> Result.Error "the job file must hold a JSON object"
        | Result.Error e -> Result.Error e


/// One round of images: these bookmarks with these settings. A job with several passes
/// renders e.g. the same bookmarks at several fixed levels of detail.
type RenderPass =
    {
        /// Bookmark names (`*` / `?` wildcards) or GUIDs; see Bookmarks.select.
        bookmarks : list<string>
        settings  : RenderSettings
        /// Appended to each file name, so passes do not overwrite each other.
        suffix    : string
    }

/// Everything a batch needs: what the job file holds, merged with the command line.
type RenderJob =
    {
        scene  : string
        out    : string
        passes : list<RenderPass>
    }

/// One image written by RenderJob.run, as listed in the manifest.
type RenderedImage =
    {
        bookmark : string
        key      : Guid
        file     : string
        size     : V2i
        lodLevel : Option<int>
    }

/// The top-level fields of a job file, each optional so that the command line can fill
/// in or override them.
type JobFile =
    {
        scene     : Option<string>
        out       : Option<string>
        bookmarks : Option<list<string>>
        width     : Option<int>
        height    : Option<int>
        lodLevel  : Option<int>
        /// Each pass's fields default to the top level's.
        passes    : Option<list<PassFile>>
    }

and PassFile =
    {
        bookmarks : Option<list<string>>
        width     : Option<int>
        height    : Option<int>
        lodLevel  : Option<int>
        suffix    : Option<string>
    }

module JobFile =

    open JsonRead

    let empty : JobFile =
        { scene = None; out = None; bookmarks = None; width = None; height = None; lodLevel = None; passes = None }

    let private passKeys = [ "bookmarks"; "width"; "height"; "lodLevel"; "suffix" ]
    let private jobKeys  = [ "scene"; "out"; "bookmarks"; "width"; "height"; "lodLevel"; "passes" ]

    let private readPass (i : int) (n : JsonNode) : Result<PassFile, string> =
        match n with
        | :? JsonObject as o ->
            match onlyKnown (sprintf "passes[%d]" i) passKeys o with
            | Result.Error e -> Result.Error e
            | Ok () ->
            match field o "bookmarks" strings, field o "width" int', field o "height" int',
                  field o "lodLevel" int', field o "suffix" str with
            | Ok b, Ok w, Ok h, Ok l, Ok s -> Ok ({ bookmarks = b; width = w; height = h; lodLevel = l; suffix = s } : PassFile)
            | Result.Error e, _, _, _, _ | _, Result.Error e, _, _, _ | _, _, Result.Error e, _, _
            | _, _, _, Result.Error e, _ | _, _, _, _, Result.Error e -> Result.Error (sprintf "passes[%d]: %s" i e)
        | _ -> Result.Error (sprintf "passes[%d] must be an object" i)

    let private readPasses (name : string) (n : JsonNode) : Result<list<PassFile>, string> =
        match n with
        | :? JsonArray as a -> a |> Seq.mapi readPass |> List.ofSeq |> sequence
        | _ -> Result.Error (sprintf "'%s' must be an array" name)

    /// Parses a job file's text. Relative `scene` / `out` paths are resolved against
    /// `baseDir`, the job file's directory.
    let parse (baseDir : string) (json : string) : Result<JobFile, string> =
        let node =
            try Ok (JsonNode.Parse(json, documentOptions = JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)))
            with e -> Result.Error (sprintf "not valid JSON: %s" e.Message)
        match node with
        | Result.Error e -> Result.Error e
        | Ok (:? JsonObject as o) ->
            let resolve = Option.map (fun (p : string) -> Path.GetFullPath(Path.Combine(baseDir, p)))
            match onlyKnown "the job file" jobKeys o with
            | Result.Error e -> Result.Error e
            | Ok () ->
            match field o "scene" str, field o "out" str, field o "bookmarks" strings, field o "width" int',
                  field o "height" int', field o "lodLevel" int', field o "passes" readPasses with
            | Ok sc, Ok ou, Ok b, Ok w, Ok h, Ok l, Ok p ->
                Ok ({ scene = resolve sc; out = resolve ou; bookmarks = b; width = w; height = h; lodLevel = l; passes = p } : JobFile)
            | Result.Error e, _, _, _, _, _, _ | _, Result.Error e, _, _, _, _, _ | _, _, Result.Error e, _, _, _, _
            | _, _, _, Result.Error e, _, _, _ | _, _, _, _, Result.Error e, _, _ | _, _, _, _, _, Result.Error e, _
            | _, _, _, _, _, _, Result.Error e -> Result.Error e
        | Ok _ -> Result.Error "the job file must hold a JSON object"

    let load (path : string) : Result<JobFile, string> =
        let full = Path.GetFullPath path
        if not (File.Exists full) then Result.Error (sprintf "no job file at %s" full)
        else
            let text =
                try Ok (File.ReadAllText full)
                with e -> Result.Error e.Message
            text
            |> Result.bind (parse (Path.GetDirectoryName full))
            |> Result.mapError (sprintf "%s: %s" full)

    /// `overrides` wins field by field (the command line over the file). Passes are taken
    /// whole from `overrides` when it has any.
    let merge (overrides : JobFile) (file : JobFile) : JobFile =
        let (<|>) (a : Option<'a>) (b : Option<'a>) = match a with Some _ -> a | None -> b
        ({
            scene     = overrides.scene     <|> file.scene
            out       = overrides.out       <|> file.out
            bookmarks = overrides.bookmarks <|> file.bookmarks
            width     = overrides.width     <|> file.width
            height    = overrides.height    <|> file.height
            lodLevel  = overrides.lodLevel  <|> file.lodLevel
            passes    = overrides.passes    <|> file.passes
        } : JobFile)

    /// The job, with defaults: every bookmark, 1920x1080, the viewer's level of detail,
    /// output beside the scene in `<scene name>-renders`.
    let toJob (f : JobFile) : Result<RenderJob, string> =
        match f.scene with
        | None -> Result.Error "no scene given (--scene, or \"scene\" in the job file)"
        | Some scene ->
            let width  = f.width  |> Option.defaultValue RenderSettings.defaults.size.X
            let height = f.height |> Option.defaultValue RenderSettings.defaults.size.Y
            let top : PassFile = { bookmarks = f.bookmarks; width = Some width; height = Some height; lodLevel = f.lodLevel; suffix = None }
            let passFiles = f.passes |> Option.defaultValue [ top ]
            let several = List.length passFiles > 1
            let toPass (i : int) (p : PassFile) : Result<RenderPass, string> =
                let w = p.width |> Option.defaultValue width
                let h = p.height |> Option.defaultValue height
                let lod = match p.lodLevel with Some l -> Some l | None -> f.lodLevel
                let where = if several then sprintf "pass %d: " i else ""
                if w <= 0 || h <= 0 then Result.Error (sprintf "%simage size must be positive, got %dx%d" where w h)
                else
                    match lod with
                    | Some l when l < 0 -> Result.Error (sprintf "%sLoD level must be >= 0, got %d" where l)
                    | _ ->
                        let suffix =
                            match p.suffix, lod with
                            | Some s, _ -> s
                            | None, Some l -> sprintf "_lod%d" l
                            | None, None -> ""
                        Ok ({
                                bookmarks = p.bookmarks |> Option.orElse f.bookmarks |> Option.defaultValue [ "*" ]
                                settings  = ({ size = V2i(w, h); lodLevel = lod } : RenderSettings)
                                suffix    = suffix
                            } : RenderPass)
            match passFiles |> List.mapi toPass |> sequence with
            | Result.Error e -> Result.Error e
            | Ok [] -> Result.Error "'passes' is empty"
            | Ok passes ->
                let out =
                    f.out |> Option.defaultValue (
                        Path.Combine(Path.GetDirectoryName(Path.GetFullPath scene),
                                     Path.GetFileNameWithoutExtension scene + "-renders"))
                Ok ({ scene = scene; out = out; passes = passes } : RenderJob)

module RenderJob =

    /// Resolves every pass's bookmarks and file names up front, so that a typo or two
    /// passes writing the same file fail before the first image is rendered.
    let plan (bookmarks : list<Bookmark>) (job : RenderJob) : Result<list<RenderPass * Bookmark * string>, string> =
        let perPass =
            job.passes |> List.map (fun pass ->
                Bookmarks.select pass.bookmarks bookmarks
                |> Result.map (fun selected ->
                    Bookmarks.fileNames selected
                    |> List.map (fun (b, name) -> pass, b, Path.Combine(job.out, name + pass.suffix + ".png"))))
        match perPass |> List.tryPick (function Result.Error e -> Some e | Ok _ -> None) with
        | Some e -> Result.Error e
        | None ->
            let all = perPass |> List.collect (function Ok xs -> xs | Result.Error _ -> [])
            let clashes =
                all |> List.countBy (fun (_, _, file) -> file.ToLowerInvariant())
                    |> List.filter (fun (_, n) -> n > 1) |> List.map fst
            match clashes with
            | [] -> Ok all
            | files -> Result.Error (sprintf "several images would be written to %s; give the passes distinct 'suffix'es"
                                        (String.concat ", " files))

    let private manifestJson (job : RenderJob) (images : list<RenderedImage>) =
        let o = JsonObject()
        o["scene"] <- JsonValue.Create job.scene
        let arr = JsonArray()
        for i in images do
            let e = JsonObject()
            e["bookmark"] <- JsonValue.Create i.bookmark
            e["key"]      <- JsonValue.Create (i.key.ToString())
            e["file"]     <- JsonValue.Create (Path.GetFileName i.file)
            e["width"]    <- JsonValue.Create i.size.X
            e["height"]   <- JsonValue.Create i.size.Y
            e["lodLevel"] <- (match i.lodLevel with Some l -> JsonValue.Create l :> JsonNode | None -> null)
            arr.Add e
        o["images"] <- arr
        o.ToJsonString(JsonSerializerOptions(WriteIndented = true))

    /// The manifest RenderJob.run writes beside the images.
    let manifestName = "renders.json"

    /// Renders every pass of `job` with `renderer` (opened on job.scene), writes one PNG
    /// per bookmark and pass into job.out, and a manifest (renders.json) listing them.
    let run (log : string -> unit) (renderer : SceneRenderer) (job : RenderJob) : Result<list<RenderedImage>, string> =
        match plan renderer.Bookmarks job with
        | Result.Error e -> Result.Error e
        | Ok planned ->
            Directory.CreateDirectory job.out |> ignore
            let total = List.length planned
            let images =
                planned |> List.mapi (fun i (pass, bookmark, file) ->
                    let image = renderer.Render(bookmark, pass.settings)
                    image.Save file
                    log (sprintf "[%d/%d] %s -> %s" (i + 1) total bookmark.name file)
                    ({ bookmark = bookmark.name; key = bookmark.key; file = file
                       size = pass.settings.size; lodLevel = pass.settings.lodLevel } : RenderedImage))
            File.WriteAllText(Path.Combine(job.out, manifestName), manifestJson job images)
            Ok images
