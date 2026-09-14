/// Section 12 — GIS View
///   TC-12.1 (Load SPICE Kernel), TC-12.2 (Observation Settings)
///
///   Kernel loading goes through the real GisApp.loadSpiceKernel; observation
///   settings through ObservationInfo.update.
module PRo3D.Tests.Section12_GisView

open System
open System.IO

open Chiron
open Expecto

open PRo3D.Base.Gis                       // EntitySpiceName, FrameSpiceName
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
