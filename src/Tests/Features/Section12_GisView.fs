/// Section 12 — GIS View
///   TC-12.1 (Load SPICE Kernel), TC-12.2 (Observation Settings),
///   TC-12.4 (Scene body: global planet = GIS observation, #758)
///
///   Kernel loading goes through the real GisApp.loadSpiceKernel; observation
///   settings through ObservationInfo.update.
module PRo3D.Tests.Section12_GisView

open System
open System.IO

open Chiron
open Expecto

open PRo3D.Base                          // Planet, CooTransformation (lat/lon)
open PRo3D.Base.Gis                       // EntitySpiceName, FrameSpiceName, SceneBody, CooTransformation.transformBody
open PRo3D.Core.Gis                       // GisApp, ObservationInfo, ObservationInfoAction
open PRo3D.Tests

let tests =
    testList "Section 12 — GIS View" [

        // TC-12.1 Load SPICE Kernel

        test "TC-12.1 loading a missing kernel reports failure" {
            let m = GisApp.loadSpiceKernel true "no/such/kernel.tpc" (GisApp.initial None)
            Expect.isFalse m.spiceKernelLoadSuccess "a missing kernel cannot be loaded"
        }

        test "TC-12.1 loading the installed default kernel succeeds" {
            // CooTransformation.initCooTrafo (run by the suite) installs pck00010.tpc
            let appData = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ApplicationData, "Pro3D")
            let kernel  = Path.Combine(appData, "JR", "CooTransformationConfig", "pck00010.tpc")
            if File.Exists kernel then
                let m = GisApp.loadSpiceKernel true kernel (GisApp.initial None)
                Expect.isTrue m.spiceKernelLoadSuccess "the installed default kernel should load"
                Expect.isSome m.spiceKernel "the loaded kernel should be recorded"
            else
                skiptest (sprintf "default SPICE kernel not installed at %s" kernel)
        }

        // TC-12.2 Observation Settings

        test "TC-12.2 SetObserver sets the observed body" {
            let m = ObservationInfo.update ObservationInfo.initial
                        (ObservationInfoAction.SetObserver (Some (EntitySpiceName "MARS")))
            Expect.equal m.observer (Some (EntitySpiceName "MARS")) "the observer body should be set"
        }

        test "TC-12.2 SetTarget sets the camera-source body" {
            let m = ObservationInfo.update ObservationInfo.initial
                        (ObservationInfoAction.SetTarget (Some (EntitySpiceName "HERA")))
            Expect.equal m.target (Some (EntitySpiceName "HERA")) "the target body should be set"
        }

        test "TC-12.2 SetReferenceFrame sets the reference frame" {
            let m = ObservationInfo.update ObservationInfo.initial
                        (ObservationInfoAction.SetReferenceFrame (Some (FrameSpiceName "IAU_MARS")))
            Expect.equal m.referenceFrame (Some (FrameSpiceName "IAU_MARS")) "the reference frame should be set"
        }

        test "TC-12.2 SetTime sets the observation time" {
            let t = DateTime(2030, 1, 2, 3, 4, 5)
            let m = ObservationInfo.update ObservationInfo.initial (ObservationInfoAction.SetTime t)
            Expect.equal m.time.date t "the observation time should be set"
        }

        // Observation times are UTC and held as DateTimeKind.Utc (#741): DateTime
        // equality and subtraction ignore the kind, so a local time next to a UTC one is
        // off by the machine's UTC offset without anything noticing.

        test "TC-12.2 SetTime holds the same instant, as UTC" {
            let utc = DateTime(2027, 3, 21, 14, 0, 0, DateTimeKind.Utc)
            let m = ObservationInfo.update ObservationInfo.initial (ObservationInfoAction.SetTime (utc.ToLocalTime()))
            Expect.equal m.time.date.Kind DateTimeKind.Utc "held as UTC"
            Expect.equal m.time.date utc "a local input is converted, not relabelled"
        }

        test "TC-12.2 the default epoch is UTC, on every machine" {
            let d = ObservationInfo.initial.time.date
            Expect.equal d.Kind DateTimeKind.Utc "UTC kind"
            Expect.equal d (DateTime(2025, 3, 10, 19, 8, 12, 600, DateTimeKind.Utc)) "2025-03-10 19:08:12.6 UTC"
        }

        test "TC-12.2 a time without a zone reads as UTC" {
            Expect.equal (PRo3D.Base.Calendar.tryParseUtc "2027-03-21 14:00:00")
                (Some (DateTime(2027, 3, 21, 14, 0, 0, DateTimeKind.Utc))) "no zone = UTC"
            Expect.equal (PRo3D.Base.Calendar.tryParseUtc "2027-03-21T16:00:00+02:00")
                (Some (DateTime(2027, 3, 21, 14, 0, 0, DateTimeKind.Utc))) "an offset converts"
            let parsed = PRo3D.Base.Calendar.tryParseUtc "2027-03-21T14:00:00Z"
            Expect.equal (parsed |> Option.map (fun d -> d.Kind)) (Some DateTimeKind.Utc) "a Z stays UTC, not local"
        }

        test "TC-12.3 the observation time survives save/load as the same UTC instant" {
            let t = DateTime(2027, 3, 21, 20, 0, 0, DateTimeKind.Utc)
            let m = GisApp.initial None
            let m = { m with defaultObservationInfo = ObservationInfo.update m.defaultObservationInfo (ObservationInfoAction.SetTime t) }
            let restored : GisApp =
                m |> Json.serialize |> Json.formatWith JsonFormattingOptions.SingleLine |> Json.parse |> Json.deserialize
            Expect.equal restored.defaultObservationInfo.time.date.Kind DateTimeKind.Utc "read back as UTC"
            Expect.equal restored.defaultObservationInfo.time.date t "same instant"
        }

        test "TC-12.3 winding correction survives save/load and defaults to off" {
            let m = GisApp.initial None
            let on = { m with projectedImageList = { m.projectedImageList with windingCorrection = true } }
            let serialized = on |> Json.serialize |> Json.formatWith JsonFormattingOptions.SingleLine
            let restored : GisApp = serialized |> Json.parse |> Json.deserialize
            Expect.isTrue restored.projectedImageList.windingCorrection "on survives save/load"

            // a scene from before the field existed loads with it off
            let old = Text.RegularExpressions.Regex.Replace(serialized, ",?\\s*\"windingCorrection\"\\s*:\\s*(true|false)", "")
            Expect.isFalse (old.Contains "windingCorrection") "the field is gone from the old scene"
            let restoredOld : GisApp = old |> Json.parse |> Json.deserialize
            Expect.isFalse restoredOld.projectedImageList.windingCorrection "old scenes: off"
        }

        test "TC-12.3 camera follows camera source survives save/load and defaults to on" {
            let m = GisApp.initial None
            Expect.isTrue m.cameraFollowsSource "a new scene follows"
            let off = { m with cameraFollowsSource = false }
            let serialized = off |> Json.serialize |> Json.formatWith JsonFormattingOptions.SingleLine
            let restored : GisApp = serialized |> Json.parse |> Json.deserialize
            Expect.isFalse restored.cameraFollowsSource "off survives save/load"

            // a scene from before the field existed loads with it on
            // the keys are written sorted, so this one comes first: take its trailing comma
            let old = Text.RegularExpressions.Regex.Replace(serialized, "\"cameraFollowsSource\"\\s*:\\s*(true|false)\\s*,?", "")
            Expect.isFalse (old.Contains "cameraFollowsSource") "the field is gone from the old scene"
            let restoredOld : GisApp = old |> Json.parse |> Json.deserialize
            Expect.isTrue restoredOld.cameraFollowsSource "old scenes: on"
        }

        test "TC-12.2 which messages re-aim the camera from the camera source body" {
            let on = GisApp.initial None
            let off = { on with cameraFollowsSource = false }
            let entry = GisApp.getMissionTimeEntriesData () |> List.tryHead
            match entry with
            | None -> failtest "no mission time entries"
            | Some entry ->
                let timeChanges = [
                    GisAppAction.SetTime (entry, FSharp.Data.Adaptive.Index.zero, 0.5)
                    GisAppAction.SetMissionTimesRowAndSetDate (entry, FSharp.Data.Adaptive.Index.zero)
                    GisAppAction.ObservationInfoMessage (ObservationInfoAction.SetTime DateTime.UtcNow)
                ]
                for msg in timeChanges do
                    Expect.isTrue  (GisApp.reaimsCamera on msg)  (sprintf "%A follows while on" msg)
                    Expect.isFalse (GisApp.reaimsCamera off msg) (sprintf "%A leaves the camera while off" msg)
                let settings = [
                    ObservationInfoAction.Reset
                    ObservationInfoAction.SetTarget (Some (EntitySpiceName "HERA"))
                    ObservationInfoAction.SetObserver (Some (EntitySpiceName "DIMORPHOS"))
                    ObservationInfoAction.SetReferenceFrame (Some (FrameSpiceName "DIMORPHOS_FIXED"))
                ]
                for msg in settings do
                    Expect.isTrue (GisApp.reaimsCamera off (GisAppAction.ObservationInfoMessage msg)) (sprintf "%A always re-aims" msg)
                // fly-to moves the time too, but frames the image itself
                let flyTo = GisAppAction.ProjectedImageListMessage (PRo3D.ImageMapping.ProjectedImageListMessage.FlyToImage (Guid.NewGuid()))
                Expect.isFalse (GisApp.reaimsCamera on flyTo) "fly-to keeps its own camera"
                let loadSpiceAndTime = GisAppAction.ProjectedImageListMessage (PRo3D.ImageMapping.ProjectedImageListMessage.LoadSpiceAndTime "x")
                Expect.isFalse (GisApp.reaimsCamera on loadSpiceAndTime) "Load Spice and Time keeps the camera"
        }

        // TC-12.4 Scene body (#758) — the global planet and the GIS observation are one
        // setting. A body PRo3D knows, observed in its own fixed frame, is what lets the
        // planet-based features (MapView, lat/lon, up/north) read world coordinates.

        test "TC-12.4 every body Planet knows has a SPICE body and fixed frame that map back" {
            for planet in [ Planet.Mars; Planet.Earth; Planet.Moon; Planet.Phobos; Planet.Deimos; Planet.Didymos; Planet.Dimorphos ] do
                match SceneBody.trySpice planet with
                | Some (body, frame) ->
                    Expect.equal (SceneBody.tryPlanet body) (Some planet) (sprintf "%A: body maps back" planet)
                    Expect.equal (SceneBody.tryFixedFrame body) (Some frame) (sprintf "%A: fixed frame" planet)
                    Expect.equal (SceneBody.tryBodyFixedPlanet (Some body) (Some frame)) (Some planet) (sprintf "%A: body-fixed" planet)
                | None -> failtestf "%A has no SPICE body" planet
            for planet in [ Planet.None; Planet.ENU; Planet.JPL ] do
                Expect.isNone (SceneBody.trySpice planet) (sprintf "%A is no body" planet)
            Expect.equal (SceneBody.trySpice Planet.Dimorphos)
                (Some (EntitySpiceName "Dimorphos", FrameSpiceName "DIMORPHOS_FIXED")) "Dimorphos is observed in DIMORPHOS_FIXED"
        }

        test "TC-12.4 a body-fixed observation is recognised regardless of name case, any other is not" {
            let bodyFixed o f = SceneBody.tryBodyFixedPlanet (Some (EntitySpiceName o)) (Some (FrameSpiceName f))
            Expect.equal (bodyFixed "DIMORPHOS" "dimorphos_fixed") (Some Planet.Dimorphos) "SPICE names ignore case"
            Expect.isNone (bodyFixed "Dimorphos" "J2000") "a scene in J2000 is not body-fixed"
            Expect.isNone (bodyFixed "Dimorphos" "DIDYMOS_FIXED") "another body's frame is not body-fixed"
            Expect.isNone (bodyFixed "HERA" "HERA_SPACECRAFT") "a spacecraft is no scene body"
            Expect.isNone (SceneBody.tryBodyFixedPlanet None (Some (FrameSpiceName "IAU_MARS"))) "no observed body"
        }

        test "TC-12.4 a body observed from itself in its own frame is placed at the identity, without SPICE" {
            // no kernel needed: the answer does not depend on any ephemeris
            let t = DateTime(2027, 3, 21, 20, 0, 0, DateTimeKind.Utc)
            match CooTransformation.transformBody (EntitySpiceName "Dimorphos") (Some (FrameSpiceName "DIMORPHOS_FIXED"))
                                                  (EntitySpiceName "DIMORPHOS") (FrameSpiceName "dimorphos_fixed") t with
            | Some placed ->
                Expect.equal placed.position Aardvark.Base.V3d.Zero "at the origin"
                Expect.equal placed.alignBodyToObserverFrame Aardvark.Base.M33d.Identity "unrotated"
                Expect.equal placed.Trafo.Forward Aardvark.Base.M44d.Identity "identity placement"
            | None -> failtest "a body observed from itself must always resolve"
        }

        test "TC-12.4 observing a known body snaps the frame to its fixed frame; other bodies keep it" {
            let m = ObservationInfo.update ObservationInfo.initial (ObservationInfoAction.SetReferenceFrame (Some (FrameSpiceName "J2000")))
            let dimorphos = ObservationInfo.update m (ObservationInfoAction.SetObserver (Some (EntitySpiceName "Dimorphos")))
            Expect.equal dimorphos.referenceFrame (Some (FrameSpiceName "DIMORPHOS_FIXED")) "the scene body's fixed frame"
            let hera = ObservationInfo.update m (ObservationInfoAction.SetObserver (Some (EntitySpiceName "HERA")))
            Expect.equal hera.referenceFrame (Some (FrameSpiceName "J2000")) "a spacecraft keeps the chosen frame"
        }

        test "TC-12.4 the global planet is mirrored into the GIS observation and back" {
            let gis = GisApp.initial None |> GisApp.withScenePlanet Planet.Dimorphos
            Expect.equal gis.defaultObservationInfo.observer (Some (EntitySpiceName "Dimorphos")) "observes Dimorphos"
            Expect.equal gis.defaultObservationInfo.referenceFrame (Some (FrameSpiceName "DIMORPHOS_FIXED")) "in its fixed frame"
            Expect.equal (GisApp.scenePlanet gis) (Some Planet.Dimorphos) "and reads back as the planet"
            let cleared = gis |> GisApp.withScenePlanet Planet.None
            Expect.isNone cleared.defaultObservationInfo.observer "no body, no observation"
            Expect.isNone (GisApp.scenePlanet cleared) "and no scene planet"
        }

        test "TC-12.4 unassigned surfaces inherit the scene body, only while the scene is body-fixed" {
            let unassigned = Guid.NewGuid()
            let halfAssigned = Guid.NewGuid()
            let assigned = Guid.NewGuid()
            let gis =
                let m = GisApp.initial None |> GisApp.withScenePlanet Planet.Dimorphos
                let surfaces =
                    [
                        halfAssigned, GisSurface.fromBody halfAssigned (Some (EntitySpiceName "Didymos"))
                        assigned, { surfaceId = assigned; entity = Some (EntitySpiceName "Didymos"); referenceFrame = Some (FrameSpiceName "DIDYMOS_FIXED") }
                    ]
                { m with gisSurfaces = FSharp.Data.Adaptive.HashMap.ofList surfaces }

            let dimorphos : Option<SpiceReferenceSystem> = Some { body = EntitySpiceName "Dimorphos"; referenceFrame = FrameSpiceName "DIMORPHOS_FIXED" }
            let didymos : Option<SpiceReferenceSystem> = Some { body = EntitySpiceName "Didymos"; referenceFrame = FrameSpiceName "DIDYMOS_FIXED" }
            Expect.equal (GisApp.getSpiceReferenceSystem gis unassigned) dimorphos "inherits the scene body"
            Expect.equal (GisApp.getSpiceReferenceSystem gis assigned) didymos "its own assignment wins"
            Expect.isNone (GisApp.getSpiceReferenceSystem gis halfAssigned) "a half assignment stays unplaced, as before"

            // a scene saved in J2000 loads as it always did: nothing is inherited
            let j2000 =
                { gis with defaultObservationInfo = { gis.defaultObservationInfo with referenceFrame = Some (FrameSpiceName "J2000") } }
            Expect.isNone (GisApp.getSpiceReferenceSystem j2000 unassigned) "no inheritance outside a body-fixed scene"
        }

        test "TC-12.4 first-import inference keeps a scene body it cannot recognise" {
            let dimorphosSized = Aardvark.Base.V3d(80.0, 0.0, 0.0)
            Expect.equal (Planet.suggestedSystem dimorphosSized Planet.Dimorphos) Planet.Dimorphos "Dimorphos is kept"
            Expect.equal (Planet.suggestedSystem dimorphosSized Planet.Mars) Planet.None "Mars at 80 m is still corrected"
            let marsSized = Aardvark.Base.V3d(3390000.0, 0.0, 0.0)
            Expect.equal (Planet.suggestedSystem marsSized Planet.Dimorphos) Planet.Mars "Mars-sized data is recognised"
        }

        test "TC-12.4 lat/lon on a GIS body uses that body's convention (Dimorphos is spherical)" {
            // Dimorphos has no PGRREC pole (see CooTransformation.getConvention); by name it
            // used to go through PGRREC. Spherical needs no SPICE at all.
            match CooTransformation.tryGetLatLonAltOfBody "Dimorphos" (Aardvark.Base.V3d(0.0, 100.0, 0.0)) with
            | Some sc ->
                Expect.floatClose Accuracy.high sc.latitude 0.0 "latitude"
                Expect.floatClose Accuracy.high sc.longitude 90.0 "longitude"
                Expect.floatClose Accuracy.high sc.altitude 100.0 "radial distance"
            | None -> failtest "spherical lat/lon cannot fail"
        }

        // TC-12.3 Persistence — the sun/lighting mode is part of the scene: the batch
        // renderer (PRo3D.Snapshots.exe) restores scenes through this codec, so a mode
        // that does not survive save/load silently resets to Off in every batch render.

        test "TC-12.3 the lighting mode survives a GisApp save/load roundtrip" {
            let m = GisApp.initial None
            let m =
                { m with
                    projectedImageList =
                        { m.projectedImageList with
                            lightingMode = PRo3D.ImageMapping.LightingMode.SunShadow } }

            let serialized = m |> Json.serialize |> Json.formatWith JsonFormattingOptions.SingleLine
            let restored : GisApp = serialized |> Json.parse |> Json.deserialize
            Expect.equal restored.projectedImageList.lightingMode
                PRo3D.ImageMapping.LightingMode.SunShadow "SunShadow should survive save/load"

            // and the default stays Off, so old scenes without the field load unchanged
            let off = GisApp.initial None |> Json.serialize |> Json.formatWith JsonFormattingOptions.SingleLine
            let restoredOff : GisApp = off |> Json.parse |> Json.deserialize
            Expect.equal restoredOff.projectedImageList.lightingMode
                PRo3D.ImageMapping.LightingMode.Off "the default lighting mode is Off"
        }
    ]
