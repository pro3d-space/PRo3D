namespace PRo3D.Viewer

open System
open System.IO
open System.Threading

open Aardvark.Base
open Aardvark.UI.Primitives.Golden

open Newtonsoft.Json
open Newtonsoft.Json.Linq

/// The panels the viewer can show in its window layout. A panel id is the `?page=`
/// value the GUI routes (`Gui.Pages.pageRouting`); every layout that reaches Golden
/// Layout is reduced to these ids, so an unknown id from a newer PRo3D or a
/// hand-edited file never becomes an empty tab.
module LayoutPanels =

    type Panel = { id : string; title : string }

    /// The main 3D view. Never closable and always present in a layout.
    let render = "render"

    let all : list<Panel> = [
        { id = render;               title = "Main View" }
        { id = "instrumentview";     title = "Instrument View" }
        { id = "surfaces";           title = "Surfaces" }
        { id = "annotations";        title = "Annotations" }
        { id = "scalebars";          title = "ScaleBars" }
        { id = "sceneobjects";       title = "Scene Objects" }
        { id = "geologicSurf";       title = "Geologic Surfaces" }
        { id = "config";             title = "Config" }
        { id = "bookmarks";          title = "Bookmarks" }
        { id = "sequencedBookmarks"; title = "Seq. Bookmarks" }
        { id = "viewplanner";        title = "Viewplans" }
        { id = "properties";         title = "Properties" }
        { id = "traverse";           title = "Traverses" }
        { id = "gis";                title = "GIS View" }
        { id = "comparison";         title = "Comparison" }
        { id = "provenance";         title = "Provenance" }
        { id = "validation";         title = "Validation" }
    ]

    let private byId = all |> List.map (fun p -> p.id, p) |> Map.ofList

    let tryFind (id : string) = Map.tryFind id byId

/// Pure operations on golden layouts: validation, structural comparison, panel edits.
module LayoutOps =

    let private sizeValue (s : Size) =
        match s with
        | Size.Weight w     -> Size.Weight (clamp 1 10000 w)
        | Size.Percentage p -> Size.Percentage (clamp 1 100 p)

    /// Ids of all panels in the layout, main window first, then popouts, in order.
    let panelIds (layout : WindowLayout) : list<string> =
        let rec go (acc : list<string>) (l : Layout) =
            match l with
            | Layout.Element e      -> e.Id :: acc
            | Layout.Stack s        -> s.Content |> List.fold (fun acc e -> e.Id :: acc) acc
            | Layout.RowOrColumn rc -> rc.Content |> List.fold go acc
        let main = layout.Root |> Option.fold go []
        layout.PopoutWindows |> List.fold (fun acc p -> go acc p.Root) main |> List.rev

    /// Brings a layout from any source (the browser, a file, a scene sidecar) into the
    /// shape PRo3D renders: unknown and duplicate panels are dropped (first occurrence
    /// wins), titles and closability come from `LayoutPanels`, sizes are clamped, empty
    /// containers disappear and the main view is guaranteed to exist.
    let sanitize (layout : WindowLayout) : WindowLayout =
        let seen = System.Collections.Generic.HashSet<string>()

        // the deserializer reports the default buttons explicitly; None means the same
        let buttons (b : Option<Buttons>) =
            match b with
            | Some Buttons.All -> None
            | b -> b

        let element (e : Element) =
            match LayoutPanels.tryFind e.Id with
            | Some p when seen.Add p.id ->
                Some {
                    e with
                        Title     = p.title
                        Closable  = p.id <> LayoutPanels.render
                        // no PRo3D UI hides headers; a missing header in a stored layout
                        // is a serialization artefact that would make the tab unreachable
                        Header    = Some (e.Header |> Option.defaultValue Header.Top)
                        Buttons   = buttons e.Buttons
                        Size      = sizeValue e.Size
                        KeepAlive = true
                }
            | _ -> None

        let rec go (l : Layout) : Option<Layout> =
            match l with
            | Layout.Element e ->
                element e |> Option.map Layout.Element
            | Layout.Stack s ->
                match s.Content |> List.choose element with
                | [] -> None
                | content -> Some (Layout.Stack { s with Content = content; Size = sizeValue s.Size; Buttons = buttons s.Buttons })
            | Layout.RowOrColumn rc ->
                match rc.Content |> List.choose go with
                | [] -> None
                | content -> Some (Layout.RowOrColumn { rc with Content = content; Size = sizeValue rc.Size })

        let root = layout.Root |> Option.bind go
        let popouts =
            layout.PopoutWindows |> List.choose (fun p ->
                go p.Root |> Option.map (fun root -> { p with Root = root })
            )

        let renderStack =
            Layout.Stack {
                Header  = Header.Top
                Buttons = None
                Size    = Size.Weight 7
                Content = [
                    { Id = LayoutPanels.render; Title = "Main View"; Closable = false; Header = Some Header.Top
                      Buttons = None; MinSize = None; Size = Size.Weight 1; KeepAlive = true }
                ]
            }

        let root =
            if seen.Contains LayoutPanels.render then root
            else
                match root with
                | None -> Some renderStack
                | Some (Layout.RowOrColumn rc) when rc.IsRow ->
                    Some (Layout.RowOrColumn { rc with Content = renderStack :: rc.Content })
                | Some other ->
                    Some (Layout.RowOrColumn { IsRow = true; Size = Size.Weight 1; Content = [renderStack; other] })

        let root =
            match root with
            | None -> Some renderStack
            | r -> r

        { Root = root; PopoutWindows = popouts }

    /// Screen positions are only meaningful on the machine that stored them; a shared
    /// layout lets the window system place its popouts.
    let withoutPopoutPositions (layout : WindowLayout) =
        { layout with PopoutWindows = layout.PopoutWindows |> List.map (fun p -> { p with Position = None }) }

    /// The arrangement of a layout without sizes and screen geometry: which panels sit
    /// together in which containers. Two layouts with the same shape differ only by
    /// splitter positions, which is not worth asking the user about.
    let shape (layout : WindowLayout) : string =
        let b = Text.StringBuilder()
        let rec go (l : Layout) =
            match l with
            | Layout.Element e -> b.Append(e.Id).Append(';') |> ignore
            | Layout.Stack s ->
                b.Append("s[") |> ignore
                for e in s.Content do b.Append(e.Id).Append(';') |> ignore
                b.Append(']') |> ignore
            | Layout.RowOrColumn rc ->
                b.Append(if rc.IsRow then "r[" else "c[") |> ignore
                for c in rc.Content do go c
                b.Append(']') |> ignore
        layout.Root |> Option.iter go
        for p in layout.PopoutWindows do
            b.Append("p[") |> ignore
            go p.Root
            b.Append(']') |> ignore
        b.ToString()

    /// Panels the user can bring back: known to PRo3D but not in the layout.
    let closedPanels (layout : WindowLayout) : list<LayoutPanels.Panel> =
        let present = panelIds layout |> Set.ofList
        LayoutPanels.all |> List.filter (fun p -> not (Set.contains p.id present))

    /// Adds a closed panel as a tab of the largest stack without the main view, or as a
    /// new stack right of everything else. Unknown or already present panels leave the
    /// layout unchanged.
    let addPanel (panelId : string) (layout : WindowLayout) : WindowLayout =
        match LayoutPanels.tryFind panelId with
        | None -> layout
        | Some _ when panelIds layout |> List.contains panelId -> layout
        | Some panel ->
            let element =
                { Id = panel.id; Title = panel.title; Closable = true; Header = Some Header.Top
                  Buttons = None; MinSize = None; Size = Size.Weight 1; KeepAlive = true }

            // the stack to add to: most tabs first, first in layout order on ties
            let rec stacks (acc : list<Stack>) (l : Layout) =
                match l with
                | Layout.Stack s when not (s.Content |> List.exists (fun e -> e.Id = LayoutPanels.render)) -> s :: acc
                | Layout.RowOrColumn rc -> rc.Content |> List.fold stacks acc
                | _ -> acc

            let target =
                layout.Root
                |> Option.map (stacks [] >> List.rev)
                |> Option.defaultValue []
                |> List.fold (fun (best : Option<Stack>) s ->
                    match best with
                    | Some b when List.length b.Content >= List.length s.Content -> best
                    | _ -> Some s
                ) None

            let root =
                match target, layout.Root with
                | Some target, Some root ->
                    let mutable added = false
                    let rec go (l : Layout) =
                        match l with
                        | Layout.Stack s when not added && obj.ReferenceEquals(s, target) ->
                            added <- true
                            Layout.Stack { s with Content = s.Content @ [element] }
                        | Layout.RowOrColumn rc -> Layout.RowOrColumn { rc with Content = rc.Content |> List.map go }
                        | other -> other
                    go root
                | None, root ->
                    let stack = Layout.Stack { Header = Header.Top; Buttons = None; Size = Size.Weight 3; Content = [element] }
                    match root with
                    | Some (Layout.RowOrColumn rc) when rc.IsRow ->
                        Layout.RowOrColumn { rc with Content = rc.Content @ [stack] }
                    | Some other ->
                        Layout.RowOrColumn { IsRow = true; Size = Size.Weight 1; Content = [other; stack] }
                    | None -> stack
                | Some _, None -> Layout.Stack { Header = Header.Top; Buttons = None; Size = Size.Weight 3; Content = [element] }

            sanitize { layout with Root = Some root }

/// A layout as stored on disk: in the AppData library, as the last used layout, or
/// beside a scene. Every read and write in here is total — failures are values, never
/// exceptions — because a layout is never worth failing a scene load or save for.
module LayoutFile =

    [<Literal>]
    let Format = "pro3d-layout"

    [<Literal>]
    let Version = 1

    type LayoutFile =
        {
            /// Display name (library entry name, dashboard name or scene name).
            name   : string
            layout : WindowLayout
        }

    /// `GoldenLayout.Json.serialize` writes sizes the way Golden Layout's config expects
    /// them (`"size": "7fr"`), but `GoldenLayout.Json.deserialize` reads the resolved form
    /// the browser reports (`"size": 7, "sizeUnit": "fr"`) and turns anything else into
    /// weight 1. Files are stored in the resolved form so sizes survive a round trip.
    let rec private resolveSizes (t : JToken) =
        match t with
        | :? JObject as o ->
            match o.TryGetValue "size" with
            | true, (:? JValue as v) when v.Type = JTokenType.String ->
                let s = string v.Value
                let unit = if s.EndsWith "fr" then Some "fr" elif s.EndsWith "%" then Some "%" else None
                match unit with
                | Some unit ->
                    match Int32.TryParse(s.Substring(0, s.Length - unit.Length)) with
                    | true, n ->
                        o.["size"]     <- JToken.op_Implicit n
                        o.["sizeUnit"] <- JToken.op_Implicit unit
                    | _ -> ()
                | None -> ()
            | _ -> ()
            for p in o.Properties() do resolveSizes p.Value
        | :? JArray as a ->
            for c in a do resolveSizes c
        | _ -> ()

    let serialize (file : LayoutFile) : string =
        let layout = JObject.Parse(GoldenLayout.Json.serialize LayoutConfig.Default file.layout)
        resolveSizes layout
        let o = JObject()
        o.["format"]  <- JToken.op_Implicit Format
        o.["version"] <- JToken.op_Implicit Version
        o.["name"]    <- JToken.op_Implicit file.name
        o.["layout"]  <- layout
        o.ToString Formatting.Indented

    /// The layout the browser reports, already sanitized.
    let tryParseGolden (json : string) : Result<WindowLayout, string> =
        try
            GoldenLayout.Json.deserialize json |> LayoutOps.sanitize |> Ok
        with e ->
            Result.Error e.Message

    let tryParse (text : string) : Result<LayoutFile, string> =
        try
            match JToken.Parse text with
            | :? JObject as o ->
                let str (key : string) =
                    match o.TryGetValue key with
                    | true, (:? JValue as v) when v.Type = JTokenType.String -> Some (string v.Value)
                    | _ -> None
                let version =
                    match o.TryGetValue "version" with
                    | true, (:? JValue as v) when v.Type = JTokenType.Integer -> Some (Convert.ToInt32 v.Value)
                    | _ -> None
                match str "format", version, o.TryGetValue "layout" with
                | Some Format, Some v, (true, (:? JObject as layout)) when v <= Version ->
                    match tryParseGolden (layout.ToString Formatting.None) with
                    | Ok l -> Ok { name = str "name" |> Option.defaultValue ""; layout = l }
                    | Result.Error e -> Result.Error (sprintf "invalid layout: %s" e)
                | Some Format, Some v, _ when v > Version ->
                    Result.Error (sprintf "layout file version %d is newer than this PRo3D supports (%d)" v Version)
                | _ ->
                    Result.Error "not a PRo3D layout file"
            | _ -> Result.Error "not a PRo3D layout file"
        with e ->
            Result.Error e.Message

    let tryRead (path : string) : Result<Option<LayoutFile>, string> =
        try
            if File.Exists path then
                File.ReadAllText path |> tryParse |> Result.map Some
            else
                Ok None
        with e ->
            Result.Error e.Message

    /// Writes `text` next to `path` first and moves it into place, so a crash or a full
    /// disk never leaves a truncated file behind.
    let tryWriteAtomic (path : string) (text : string) : Result<unit, string> =
        let temp = path + ".tmp"
        try
            let dir = Path.GetDirectoryName(Path.GetFullPath path)
            Directory.CreateDirectory dir |> ignore
            File.WriteAllText(temp, text)
            File.Move(temp, path, true)
            Ok ()
        with e ->
            try if File.Exists temp then File.Delete temp with _ -> ()
            Result.Error e.Message

    let tryWrite (path : string) (file : LayoutFile) : Result<unit, string> =
        let text = try Ok (serialize file) with e -> Result.Error e.Message
        text |> Result.bind (tryWriteAtomic path)

/// The layout written beside a scene file, so a scene can be shared together with the
/// arrangement it was worked in. Written after the scene; its failure never fails a save.
module SceneLayoutSidecar =

    let path (scenePath : string) = scenePath + ".layout"

    let tryWrite (scenePath : string) (layout : WindowLayout) : Result<unit, string> =
        try
            let file : LayoutFile.LayoutFile = {
                name   = Path.GetFileNameWithoutExtension scenePath
                layout = LayoutOps.withoutPopoutPositions layout
            }
            LayoutFile.tryWrite (path scenePath) file
        with e ->
            Result.Error e.Message

    let tryRead (scenePath : string) : Result<Option<LayoutFile.LayoutFile>, string> =
        try
            LayoutFile.tryRead (path scenePath)
            |> Result.map (Option.map (fun f -> { f with layout = LayoutOps.withoutPopoutPositions f.layout }))
        with e ->
            Result.Error e.Message

/// Per-user layouts under `%APPDATA%/Pro3D/layouts` (or `PRO3D_LAYOUT_DIR`): the last
/// used layout (`current.json`) and the named layouts of the library (`library/*.json`).
module LayoutLibrary =

    let directory () =
        match Environment.GetEnvironmentVariable "PRO3D_LAYOUT_DIR" with
        | null | "" -> Path.Combine(PRo3D.Config.configPath, "layouts")
        | dir -> dir

    let currentPath (dir : string) = Path.Combine(dir, "current.json")
    let libraryDir  (dir : string) = Path.Combine(dir, "library")

    /// File name for a library entry; None if nothing usable is left of the name.
    let fileName (name : string) : Option<string> =
        let invalid = Path.GetInvalidFileNameChars()
        let cleaned =
            name.Trim()
            |> String.map (fun c -> if Array.contains c invalid then '_' else c)
            |> fun s -> s.TrimEnd('.', ' ')
        let cleaned = if cleaned.Length > 64 then cleaned.Substring(0, 64) else cleaned
        if String.IsNullOrWhiteSpace cleaned then None else Some (cleaned + ".json")

    let entryPath (dir : string) (name : string) =
        fileName name |> Option.map (fun f -> Path.Combine(libraryDir dir, f))

    /// A file that cannot be read is kept as `<name>.corrupt` for inspection and no
    /// longer gets in the way.
    let private quarantine (path : string) =
        try File.Move(path, path + ".corrupt", true)
        with e -> Log.warn "[Layouts] could not move unreadable %s aside: %s" path e.Message

    /// The last used layout. A file that cannot be read is moved aside and reported.
    let tryLoadCurrent (dir : string) : Result<Option<LayoutFile.LayoutFile>, string> =
        let path = currentPath dir
        match LayoutFile.tryRead path with
        | Ok file -> Ok file
        | Result.Error e ->
            Log.warn "[Layouts] ignoring unreadable %s: %s" path e
            quarantine path
            Result.Error e

    type Entry =
        {
            /// display name: the name stored in the file, or the file name if it cannot be read
            name   : string
            path   : string
            /// the stored layout, or why it cannot be read
            layout : Result<WindowLayout, string>
        }

    /// Library entries sorted by name. Unreadable files are listed too, so opening one
    /// can tell the user what is wrong with it.
    let list (dir : string) : list<Entry> =
        try
            let lib = libraryDir dir
            if Directory.Exists lib then
                Directory.GetFiles(lib, "*.json")
                |> Array.toList
                |> List.choose (fun path ->
                    let fileName = Path.GetFileNameWithoutExtension path
                    match LayoutFile.tryRead path with
                    | Ok (Some f) ->
                        let name = if String.IsNullOrWhiteSpace f.name then fileName else f.name
                        Some { name = name; path = path; layout = Ok f.layout }
                    | Ok None -> None
                    | Result.Error e ->
                        Log.warn "[Layouts] unreadable library entry %s: %s" path e
                        Some { name = fileName; path = path; layout = Result.Error e }
                )
                |> List.sortBy (fun e -> e.name.ToLowerInvariant())
            else []
        with e ->
            Log.warn "[Layouts] could not list %s: %s" dir e.Message
            []

    let tryFind (dir : string) (name : string) =
        list dir |> List.tryFind (fun e -> e.name = name)

    let trySave (dir : string) (file : LayoutFile.LayoutFile) : Result<unit, string> =
        match entryPath dir file.name with
        | Some path -> LayoutFile.tryWrite path { file with name = file.name.Trim() }
        | None -> Result.Error "a layout needs a name"

    let tryDelete (dir : string) (name : string) : Result<unit, string> =
        try
            match tryFind dir name with
            | Some entry ->
                File.Delete entry.path
                Ok ()
            | None -> Result.Error (sprintf "no layout named '%s'" name)
        with e ->
            Result.Error e.Message

    let tryRename (dir : string) (oldName : string) (newName : string) : Result<unit, string> =
        match tryFind dir oldName, entryPath dir newName with
        | None, _ -> Result.Error (sprintf "no layout named '%s'" oldName)
        | _, None -> Result.Error "a layout needs a name"
        | Some { layout = Result.Error e }, _ -> Result.Error e
        | Some ({ layout = Ok layout } as entry), Some newPath ->
            let samePath = String.Equals(Path.GetFullPath entry.path, Path.GetFullPath newPath, StringComparison.OrdinalIgnoreCase)
            let taken = tryFind dir (newName.Trim()) |> Option.exists (fun e -> e.path <> entry.path)
            if taken || (not samePath && File.Exists newPath) then
                Result.Error (sprintf "a layout named '%s' already exists" (newName.Trim()))
            else
                LayoutFile.tryWrite newPath { name = newName.Trim(); layout = layout }
                |> Result.bind (fun () ->
                    try
                        if not samePath then File.Delete entry.path
                        Ok ()
                    with e -> Result.Error e.Message
                )

    /// Persists the current layout off the update thread. Layout changes arrive in
    /// bursts (dragging a splitter); only the last one of a burst is written.
    type CurrentWriter(dir : string) =
        let pending : ref<Option<LayoutFile.LayoutFile>> = ref None
        let timer =
            new Timer((fun _ ->
                let file = lock pending (fun () -> let f = pending.Value in pending.Value <- None; f)
                match file with
                | Some file ->
                    match LayoutFile.tryWrite (currentPath dir) file with
                    | Ok () -> ()
                    | Result.Error e -> Log.warn "[Layouts] could not store the current layout: %s" e
                | None -> ()
            ), null, Timeout.Infinite, Timeout.Infinite)

        member x.Post(file : LayoutFile.LayoutFile) =
            lock pending (fun () -> pending.Value <- Some file)
            try timer.Change(500, Timeout.Infinite) |> ignore with _ -> ()

    let private writers = System.Collections.Concurrent.ConcurrentDictionary<string, CurrentWriter>()

    let storeCurrent (dir : string) (file : LayoutFile.LayoutFile) =
        writers.GetOrAdd(dir, fun d -> CurrentWriter d).Post file
