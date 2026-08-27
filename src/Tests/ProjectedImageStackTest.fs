module ProjectedImageStackTest

open System
open Expecto
open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.ImageMapping
open PRo3D.Core

// Pure model tests for the multi-image projection stack (see
// plans/multiImageProjection.md): effectiveStack composition, the stack
// actions, sorting-vs-stack independence, and the COP sidecar fallbacks
// (per-image time from the file name, metres-vs-km auto-detection).

let private mkImage (name : string) (distance : float) (time : DateTime) : ProjectedImageModel =
    { ProjectedImageApp.initial with
        id = Guid.NewGuid()
        texture = name
        distance = distance
        time = time }

let private mkModel (images : ProjectedImageModel list) : ProjectedImageListModel =
    { ProjectedImageListModel.initial with images = IndexList.ofList images }

let private stackIds (m : ProjectedImageListModel) =
    m.stack |> IndexList.toList

let tests () =
    testList "projectedImageStack" [

        testList "effectiveStack" [
            test "hovering a library image previews it on top" {
                let a = mkImage "a" 1.0 DateTime.MinValue
                let b = mkImage "b" 2.0 DateTime.MinValue
                let m = { mkModel [a; b] with stack = IndexList.ofList [a.id]; hoveredImage = Some b.id }
                let eff = ProjectedImageListModel.effectiveStack m |> IndexList.toList
                Expect.equal eff [a.id; b.id] "hovered image must be appended as the TOP layer"
            }

            test "hovering an image already in the stack adds no duplicate layer" {
                let a = mkImage "a" 1.0 DateTime.MinValue
                let b = mkImage "b" 2.0 DateTime.MinValue
                let m = { mkModel [a; b] with stack = IndexList.ofList [a.id; b.id]; hoveredImage = Some a.id }
                let eff = ProjectedImageListModel.effectiveStack m |> IndexList.toList
                Expect.equal eff [a.id; b.id] "the stack must be unchanged"
            }

            test "no hover means the plain stack" {
                let a = mkImage "a" 1.0 DateTime.MinValue
                let m = { mkModel [a] with stack = IndexList.ofList [a.id] }
                let eff = ProjectedImageListModel.effectiveStack m |> IndexList.toList
                Expect.equal eff [a.id] "stack passes through"
            }

            test "a full stack plus hover preview drops the BOTTOM layer, not the preview" {
                let images = List.init (ProjectedImages.maxCount + 1) (fun i -> mkImage (string i) (float i) DateTime.MinValue)
                let inStack = images |> List.take ProjectedImages.maxCount
                let hovered = images |> List.last
                let m =
                    { mkModel images with
                        stack = inStack |> List.map (fun i -> i.id) |> IndexList.ofList
                        hoveredImage = Some hovered.id }
                let eff = ProjectedImageListModel.effectiveStack m |> IndexList.toList
                Expect.equal eff.Length ProjectedImages.maxCount "capped at maxCount"
                Expect.equal (List.last eff) hovered.id "the preview must be the visible top layer"
                Expect.isFalse (List.contains (inStack.Head.id) eff) "the bottom stack layer gives way"
            }
        ]

        testList "stack actions" [
            test "AddToStack appends on top, ignores duplicates and unknown ids" {
                let a = mkImage "a" 1.0 DateTime.MinValue
                let b = mkImage "b" 2.0 DateTime.MinValue
                let m = mkModel [a; b]
                let m = ProjectedImageListApp.update m (AddToStack a.id)
                let m = ProjectedImageListApp.update m (AddToStack b.id)
                Expect.equal (stackIds m) [a.id; b.id] "bottom -> top order of addition"
                let m = ProjectedImageListApp.update m (AddToStack a.id)
                Expect.equal (stackIds m) [a.id; b.id] "adding again must not duplicate"
                let m = ProjectedImageListApp.update m (AddToStack (Guid.NewGuid()))
                Expect.equal (stackIds m) [a.id; b.id] "ids without a library image are ignored"
            }

            test "AddToStack stops at the cap" {
                let images = List.init (ProjectedImages.maxCount + 3) (fun i -> mkImage (string i) (float i) DateTime.MinValue)
                let m =
                    images
                    |> List.fold (fun m img -> ProjectedImageListApp.update m (AddToStack img.id)) (mkModel images)
                Expect.equal m.stack.Count ProjectedImages.maxCount "the stack never exceeds maxCount"
            }

            test "MoveInStack clamps at both ends" {
                let a = mkImage "a" 1.0 DateTime.MinValue
                let b = mkImage "b" 2.0 DateTime.MinValue
                let c = mkImage "c" 3.0 DateTime.MinValue
                let m = { mkModel [a; b; c] with stack = IndexList.ofList [a.id; b.id; c.id] }
                let m1 = ProjectedImageListApp.update m (MoveInStack (b.id, 99))
                Expect.equal (stackIds m1) [a.id; c.id; b.id] "over-the-top moves clamp to the top"
                let m2 = ProjectedImageListApp.update m (MoveInStack (b.id, -5))
                Expect.equal (stackIds m2) [b.id; a.id; c.id] "below-the-bottom moves clamp to the bottom"
                let m3 = ProjectedImageListApp.update m (MoveInStack (Guid.NewGuid(), 1))
                Expect.equal (stackIds m3) (stackIds m) "unknown ids are a no-op"
            }

            test "RemoveFromStack removes exactly the given layer" {
                let a = mkImage "a" 1.0 DateTime.MinValue
                let b = mkImage "b" 2.0 DateTime.MinValue
                let m = { mkModel [a; b] with stack = IndexList.ofList [a.id; b.id] }
                let m = ProjectedImageListApp.update m (RemoveFromStack a.id)
                Expect.equal (stackIds m) [b.id] "only the removed id goes"
            }

            test "sorting the library touches neither stack nor selection" {
                let a = mkImage "a" 3.0 (DateTime(2027, 3, 1))
                let b = mkImage "b" 1.0 (DateTime(2027, 2, 1))
                let c = mkImage "c" 2.0 (DateTime(2027, 4, 1))
                let m =
                    { mkModel [a; b; c] with
                        stack = IndexList.ofList [c.id; a.id]
                        selectedImage = Some a.id
                        editImages = HashSet.ofList [b.id] }
                let sorted = ProjectedImageListApp.update m SortEntriesByDistance
                Expect.equal (sorted.images |> IndexList.toList |> List.map (fun i -> i.texture)) ["b"; "c"; "a"] "library sorted by distance"
                Expect.equal (stackIds sorted) [c.id; a.id] "the stack must not move"
                Expect.equal sorted.selectedImage (Some a.id) "selection must survive"
                Expect.isTrue (sorted.editImages |> HashSet.contains b.id) "edit set must survive"
                let sorted2 = ProjectedImageListApp.update sorted SortEntriesByDate
                Expect.equal (sorted2.images |> IndexList.toList |> List.map (fun i -> i.texture)) ["b"; "a"; "c"] "library sorted by date"
                Expect.equal (stackIds sorted2) [c.id; a.id] "the stack must still not move"
            }
        ]

        testList "copSidecarFallbacks" [
            // the COP delivery's defects, distilled (docs/COP-sidecar-issues.md)
            let copSidecar = """{
                "instrument": "AFC1",
                "fits_hdu_headers": [ {
                    "INSTRUME": { "value": "AFC1", "comment": "" },
                    "DATE":     { "value": "2027-02-05T01:00:00.000", "comment": "constant across the delivery" },
                    "DATE-OBS": { "value": "AFC1-Synthetic", "comment": "not a date" },
                    "SPICE_MK": { "value": "", "comment": "name only in the comment" },
                    "TARGET":   { "value": "Didymos", "comment": "" },
                    "SUN_POSX": { "value": -67289534067.9055, "comment": "metres despite [km]" },
                    "SUN_POSY": { "value": -132073164991.70976, "comment": "" },
                    "SUN_POSZ": { "value": -55805082163.72677, "comment": "" },
                    "EARTPOSX": { "value": 0.0, "comment": "" },
                    "EARTPOSY": { "value": 0.0, "comment": "" },
                    "EARTPOSZ": { "value": 0.0, "comment": "" },
                    "TRG_POSX": { "value": -11082.237154245377, "comment": "" },
                    "TRG_POSY": { "value": -6034.208193421364, "comment": "" },
                    "TRG_POSZ": { "value": -7821.941547095776, "comment": "" },
                    "SC_QUAT0": { "value": 0.7028475895867948, "comment": "" },
                    "SC_QUAT1": { "value": 0.4412388066043912, "comment": "" },
                    "SC_QUAT2": { "value": -0.2038905891274264, "comment": "" },
                    "SC_QUAT3": { "value": 0.5193671235490709, "comment": "" }
                } ],
                "bands": [ { "label": "", "file_path": "" } ]
            }"""

            test "file-name timestamp beats the delivery-constant DATE" {
                match InstrumentMetadata.Tiff_Mbi_Json.tryParseJsonForFile (Some "HERA_AFC_2317_20270301_040000_COP.png") copSidecar with
                | Result.Ok mbi ->
                    Expect.equal (mbi.obs_date.ToUniversalTime()) (DateTime(2027, 3, 1, 4, 0, 0, DateTimeKind.Utc)) "obs time must come from the file name (UTC)"
                | Result.Error e -> failtestf "parse failed: %A" e
            }

            test "without a file name, DATE remains the last resort" {
                match InstrumentMetadata.Tiff_Mbi_Json.tryParseJson copSidecar with
                | Result.Ok mbi ->
                    Expect.equal (mbi.obs_date.ToUniversalTime()) (DateTime(2027, 2, 5, 1, 0, 0, DateTimeKind.Utc)) "falls back to DATE (UTC)"
                | Result.Error e -> failtestf "parse failed: %A" e
            }

            test "metre positions are detected via the sun distance and normalized to km" {
                match InstrumentMetadata.Tiff_Mbi_Json.tryParseJson copSidecar with
                | Result.Ok mbi ->
                    let au = 1.495978707e8
                    let sunAu = mbi.sunPos.Length / au
                    Expect.isTrue (sunAu > 0.9 && sunAu < 1.3) (sprintf "sun distance must land near 1 AU in km (got %.3f AU)" sunAu)
                    Expect.floatClose Accuracy.low mbi.targetPos.Length 14.846 "target position scales with the same factor (km)"
                | Result.Error e -> failtestf "parse failed: %A" e
            }

            test "empty SPICE_MK and filled TARGET are read as intended" {
                match InstrumentMetadata.Tiff_Mbi_Json.tryParseJson copSidecar with
                | Result.Ok mbi ->
                    Expect.isNone mbi.spiceMk "empty string must not count as a kernel name"
                    Expect.equal mbi.target (Some "Didymos") "TARGET is the projection body"
                | Result.Error e -> failtestf "parse failed: %A" e
            }

            test "km positions stay untouched" {
                // a Mars-era sidecar: sun at ~1.5 AU in km
                let marsish = copSidecar.Replace("-67289534067.9055", "-224000000.0")
                                        .Replace("-132073164991.70976", "0.0")
                                        .Replace("-55805082163.72677", "0.0")
                match InstrumentMetadata.Tiff_Mbi_Json.tryParseJson marsish with
                | Result.Ok mbi ->
                    Expect.floatClose Accuracy.medium mbi.sunPos.Length 2.24e8 "km values must not be rescaled"
                | Result.Error e -> failtestf "parse failed: %A" e
            }

            test "file names without a timestamp yield no time" {
                Expect.isNone
                    (InstrumentMetadata.Tiff_Mbi_Json.tryParseTimestampFromFileName "HSH_0CR7B2_250312T062000_1B_Stacked.tif")
                    "the yyMMddTHHmmss convention must not be misparsed"
                Expect.equal
                    (InstrumentMetadata.Tiff_Mbi_Json.tryParseTimestampFromFileName "HERA_AFC_2317_20270301_040000_COP.png"
                     |> Option.map (fun d -> d.ToUniversalTime()))
                    (Some (DateTime(2027, 3, 1, 4, 0, 0, DateTimeKind.Utc)))
                    "the _yyyyMMdd_HHmmss_ convention parses"
            }
        ]
    ]
