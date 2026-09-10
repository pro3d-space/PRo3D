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

    /// The export was told to take lat/lon/alt from the OPC, and no loaded
    /// surface has any. Unlike the warnings above this refuses the export: the
    /// file would otherwise be full of SPICE values under a setting that says
    /// they came from the file.
    let private noAaraDataMessage =
        "No loaded surface ships per-vertex LonLatRad data, so lat/lon/alt cannot be taken \
         from the file. Nothing was exported — set Lat/Lon/Alt Source to SPICE, or load a \
         surface whose OPC contains .aara attribute layers."

    /// Some points were outside the per-vertex data and had to be resolved by
    /// SPICE instead. The file is written; this only says how much of it is not
    /// what the setting asked for.
    let private partialAaraMessage (fallenBack : int) (total : int) =
        sprintf
            "The file was written, but %d of %d sampled positions had no LonLatRad data \
             underneath them, so their lat/lon/alt come from SPICE. The latLonAltSource \
             column says which rows those are. A per-annotation export samples at the \
             bounding-box centre, which often lies off the surface."
            fallenBack total

    /// What the sampler observed, for the warnings above. Counted rather than
    /// inferred: only the sampler knows how many points actually resolved.
    type private SampleTally = {
        /// points that produced at least one surface property
        withProperties : unit -> int
        /// points that produced no LonLatRad value
        withoutLonLatRad : unit -> int
        /// points sampled in total
        total : unit -> int
    }

    /// Builds the per-point surface sampler, together with a tally of what it
    /// managed to resolve — a sampler that silently returns nothing for every
    /// point is worth telling the user about.
    let private surfaceSampler
        (refSys            : ReferenceSystem)
        (context           : SurfaceSamplingContext)
        (wantsProperties   : bool)
        : SurfacePropertySampler * SampleTally =

        let mutable withProperties = 0
        let mutable withoutLonLatRad = 0
        let mutable total = 0

        let sample (position : V3d) =
            let result, cache =
                // no up vector passed: sampleAt derives the body-local one at each point.
                // Handing it refSys.up made the export depend on the camera - see its docs
                ProfileAttributeExtraction.sampleAt
                    context.surfaces refSys context.observedSystem context.observerSystem
                    // chasing layers into the attribute textures costs an image
                    // decode per layer per patch, so only pay for it when the
                    // surface-property columns were actually asked for
                    wantsProperties
                    PRo3D.Picking.cache position

            // shared with interactive picking on purpose: the KdTrees loaded for
            // the export stay loaded for the next pick, and vice versa
            PRo3D.Picking.cache <- cache

            total <- total + 1

            match result with
            | Some hit ->
                let lonLatRadius = ProfileAttributeExtraction.tryLonLatRadius hit
                if lonLatRadius |> Option.isNone then withoutLonLatRad <- withoutLonLatRad + 1
                if not hit.attributes.IsEmpty then withProperties <- withProperties + 1

                { lonLatRadius = lonLatRadius
                  properties =
                    if not wantsProperties then []
                    else
                        hit.attributes
                        |> List.map (fun a ->
                            AnnotationExport.surfaceColumnName a.name, ExportValue.ofChannels a.values)
                        // by column name, so the order does not depend on which
                        // patch happened to be hit first
                        |> List.sortBy fst }
            | None ->
                withoutLonLatRad <- withoutLonLatRad + 1
                SurfaceSample.empty

        sample,
        { withProperties   = fun () -> withProperties
          withoutLonLatRad = fun () -> withoutLonLatRad
          total            = fun () -> total }

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
                // fixed-schema formats ignore every attribute setting — sampling
                // for those would only cost time and then warn about columns
                // nobody asked for.
                let perPoint =
                    settings.granularity = ExportGranularity.PerPoint
                    && not (AnnotationExportSettings.hasFixedSchema settings.format)

                let wantsProperties = settings.sampleSurfaceProperties && perPoint

                // Cartesian exports write no lat/lon at all, so the source is
                // irrelevant there and must not drag a ray cast — or the refusal
                // below — into an otherwise ordinary export.
                //
                // Unlike the surface properties above this is *not* limited to
                // per-point exports: a per-annotation row samples once, at the
                // bounding-box centre, falling back to SPICE where that misses.
                let wantsAaraCoordinates =
                    settings.latLonAltSource = LatLonAltSource.AaraFile
                    && AnnotationExport.wantsGeographic settings.coordinates
                    && not (AnnotationExportSettings.hasFixedSchema settings.format)

                if wantsAaraCoordinates
                   && not (ProfileAttributeExtraction.hasLonLatRadLayer surfaces.surfaces) then
                    Log.warn "[AnnotationExport] lat/lon/alt from file requested, but no surface ships LonLatRad"
                    Some noAaraDataMessage
                else

                let sampler =
                    if wantsProperties || wantsAaraCoordinates then
                        Some (surfaceSampler refSys surfaces wantsProperties)
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
                      | Some (_, tally) ->
                          if wantsProperties && tally.withProperties () = 0 then
                              Log.warn "[AnnotationExport] surface properties requested but no point hit a surface"
                              yield noSurfacePropertiesMessage

                          // The all-missed case is not special-cased: the scene
                          // *has* the layer (checked above), so this is about
                          // where the annotation lies, and the row count says it
                          // better than a separate message would.
                          if wantsAaraCoordinates && tally.withoutLonLatRad () > 0 then
                              Log.warn "[AnnotationExport] %d of %d points fell back to SPICE lat/lon/alt"
                                       (tally.withoutLonLatRad ()) (tally.total ())
                              yield partialAaraMessage (tally.withoutLonLatRad ()) (tally.total ())
                      | None -> () ]
                    |> function
                       | []       -> None
                       | messages -> Some (messages |> String.concat " ")
                with e ->
                    // same reasoning as the empty scope: a silently closing window
                    // would look like a successful export
                    Log.warn "[AnnotationExport] export failed with %A" e
                    Some (sprintf "Writing %s failed: %s" path e.Message)
