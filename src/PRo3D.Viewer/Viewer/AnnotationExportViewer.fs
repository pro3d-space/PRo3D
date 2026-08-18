namespace PRo3D.Viewer

open System

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

/// Runs the annotation export. Resolving the export scope needs the group tree
/// and the reference system, which is why this sits at viewer level rather than
/// in `AnnotationExportApp`.
module AnnotationExportViewer =

    /// Annotations in the order the user sees them in the group tree. The flat
    /// `HashMap` alone would give hash order, which differs between runs.
    let private inTreeOrder (groups : GroupsModel) =
        let rec go (node : Node) =
            [ yield! node.leaves |> IndexList.toList
              yield! node.subNodes |> IndexList.toList |> List.collect go ]

        go groups.rootGroup
        |> List.choose (fun key ->
            match groups.flat |> HashMap.tryFind key with
            | Some (Leaf.Annotations a) -> Some a
            | _ -> None)

    /// Multi-selected leaves plus the single-selected one — the previous
    /// exports only ever looked at `singleSelectLeaf`, so annotations selected
    /// via the multi-select box were silently dropped.
    let private selectedKeys (groups : GroupsModel) =
        let fromMulti = groups.selectedLeaves |> HashSet.map (fun s -> s.id)
        match groups.singleSelectLeaf with
        | Some key -> fromMulti |> HashSet.add key
        | None     -> fromMulti

    let annotationsInScope (scope : ExportScope) (groups : GroupsModel) =
        let ordered = inTreeOrder groups
        match scope with
        | ExportScope.Visible  -> ordered |> List.filter (fun a -> a.visible)
        | ExportScope.Selected ->
            let keys = selectedKeys groups
            ordered |> List.filter (fun a -> keys |> HashSet.contains a.key)
        | _ -> ordered

    /// Why a scope produced no annotations, phrased so the user knows what to
    /// change. `scopeLabel` alone would not say what to do about it.
    let private emptyScopeMessage (scope : ExportScope) =
        match scope with
        | ExportScope.Selected ->
            "No annotations are selected, so there is nothing to export. Select them in the \
             annotation list, or choose a different scope."
        | ExportScope.Visible ->
            "No annotations are visible, so there is nothing to export. Make at least one \
             visible, or choose a different scope."
        | _ ->
            "There are no annotations to export."

    /// Performs the export described by `settings`. `path` comes from the save
    /// dialog.
    ///
    /// Returns a message for the user when the export did not happen and they
    /// need to know — the window shows it and stays open so the settings can be
    /// corrected. `None` means the window may close: the file was written, or
    /// the save dialog was dismissed.
    let export
        (settings : AnnotationExportSettings)
        (path     : string)
        (drawing  : DrawingModel)
        (refSys   : ReferenceSystem)
        : Option<string> =

        if String.IsNullOrEmpty path then
            // the save dialog was cancelled; nothing went wrong
            None
        else
            let annotations = annotationsInScope settings.scope drawing.annotations

            if List.isEmpty annotations then
                Log.warn "[AnnotationExport] nothing to export for scope %A" settings.scope
                Some (emptyScopeMessage settings.scope)
            else
                let up = refSys.up.value.Normalized
                // full path, not just the immediate parent: keeps nested groups
                // exportable and reconstructable on a later reimport
                let groupPath = GroupsApp.groupPathLookup drawing.annotations

                try
                    AnnotationExport.write settings groupPath refSys.planet up path annotations
                    None
                with e ->
                    // same reasoning as the empty scope: a silently closing window
                    // would look like a successful export
                    Log.warn "[AnnotationExport] export failed with %A" e
                    Some (sprintf "Writing %s failed: %s" path e.Message)
