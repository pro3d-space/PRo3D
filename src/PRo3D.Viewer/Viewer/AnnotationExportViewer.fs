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

    /// Performs the export described by `settings`. `path` comes from the save
    /// dialog.
    let export
        (settings : AnnotationExportSettings)
        (path     : string)
        (drawing  : DrawingModel)
        (refSys   : ReferenceSystem)
        : unit =

        if String.IsNullOrEmpty path then
            Log.warn "[AnnotationExport] no path specified"
        else
            let annotations = annotationsInScope settings.scope drawing.annotations

            if List.isEmpty annotations then
                Log.warn "[AnnotationExport] nothing to export for scope %A" settings.scope
            else
                let up = refSys.up.value.Normalized
                let lookUp = GroupsApp.updateGroupsLookup drawing.annotations

                try
                    AnnotationExport.write settings lookUp refSys.planet up path annotations
                with e ->
                    Log.warn "[AnnotationExport] export failed with %A" e
