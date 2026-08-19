namespace PRo3D.Viewer

open System

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Base.Gis
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface

/// Runs the annotation export. Resolving the export scope needs the group tree
/// and the reference system, which is why this sits at viewer level rather than
/// in `AnnotationExportApp`.
module AnnotationExportViewer =

    /// What sampling the surface properties under a point needs. Bundled rather
    /// than passed as four more positional arguments of similar-looking type.
    type SurfaceSamplingContext = {
        surfaces       : SurfaceModel
        observedSystem : SurfaceId -> Option<SpiceReferenceSystem>
        observerSystem : Option<ObserverSystem>
    }

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

    /// The file was written, but without a geographic frame every geographic
    /// value in it is empty. Worth saying out loud: the export looks successful
    /// and the emptiness only shows up once the file is opened elsewhere.
    let private noFrameMessage (format : ExportFormat) =
        let common =
            "as no reference frame is set. Pick the body in the top menu bar, \
             or export cartesian coordinates instead."
        match format with
        | ExportFormat.GeoJson ->
            sprintf
                "The file was written, but the geometry attribute is exported with value \
                 null and lat, lon and alt are empty, %s"
                common
        | _ ->
            sprintf
                "The file was written, but the lat, lon and alt columns are empty, %s"
                common

    /// The export asked for surface properties but not one point produced any.
    /// Like the missing frame above, the file looks fine until it is opened
    /// somewhere else and the surface columns simply are not there.
    let private noSurfacePropertiesMessage =
        "The file was written, but no surface properties could be sampled: no visible, \
         active OPC surface was hit underneath the exported points. Check that the \
         surface the annotation was drawn on is switched on, and that it has \
         attribute layers besides its base texture."

    /// Builds the per-point surface sampler, together with a counter of how many
    /// points it actually got values for — a sampler that silently returns
    /// nothing for every point is worth telling the user about.
    let private surfaceSampler
        (refSys  : ReferenceSystem)
        (context : SurfaceSamplingContext)
        : SurfacePropertySampler * (unit -> int) =

        let up = refSys.up.value.Normalized
        let mutable sampled = 0

        let sample (position : V3d) =
            let result, cache =
                ProfileAttributeExtraction.sampleAt
                    up context.surfaces refSys context.observedSystem context.observerSystem
                    PRo3D.Picking.cache position

            // shared with interactive picking on purpose: the KdTrees loaded for
            // the export stay loaded for the next pick, and vice versa
            PRo3D.Picking.cache <- cache

            match result with
            | Some hit when not hit.attributes.IsEmpty ->
                sampled <- sampled + 1
                hit.attributes
                |> List.map (fun a ->
                    AnnotationExport.surfaceColumnName a.name, ExportValue.ofChannels a.values)
                // by column name, so the order does not depend on which patch
                // happened to be hit first
                |> List.sortBy fst
            | _ -> []

        sample, fun () -> sampled

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
        (surfaces : SurfaceSamplingContext)
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

                // Only per-point exports have a position to sample at, and the
                // fixed-schema formats ignore every attribute setting — building
                // a sampler for those would only cost time and then warn about
                // columns nobody asked for.
                let sampler =
                    if settings.sampleSurfaceProperties
                       && settings.granularity = ExportGranularity.PerPoint
                       && not (AnnotationExportSettings.hasFixedSchema settings.format) then
                        Some (surfaceSampler refSys surfaces)
                    else None

                try
                    AnnotationExport.write
                        settings (sampler |> Option.map fst) groupPath refSys.planet up path annotations

                    // written successfully, but possibly without values the
                    // settings asked for
                    [ if AnnotationExport.geographicWithoutFrame settings refSys.planet then
                          Log.warn "[AnnotationExport] %A has no geographic frame; lat/lon/alt are empty" refSys.planet
                          yield noFrameMessage settings.format

                      match sampler with
                      | Some (_, sampledCount) when sampledCount () = 0 ->
                          Log.warn "[AnnotationExport] surface properties requested but no point hit a surface"
                          yield noSurfacePropertiesMessage
                      | _ -> () ]
                    |> function
                       | []       -> None
                       | messages -> Some (messages |> String.concat " ")
                with e ->
                    // same reasoning as the empty scope: a silently closing window
                    // would look like a successful export
                    Log.warn "[AnnotationExport] export failed with %A" e
                    Some (sprintf "Writing %s failed: %s" path e.Message)
