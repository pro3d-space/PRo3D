/// Section 23 - Annotation metadata (#887, docs/AnnotationMetadata.md): confidence, comment,
/// shape model and projected images used.
module PRo3D.Tests.Section23_AnnotationMetadata

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI.Primitives

open FSharp.Data.Adaptive
open Chiron

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Viewer
open PRo3D.Tests

let private polyline (points : list<V3d>) =
    let a = Annotation.make Projection.Linear None Geometry.Polyline None { c = C4b.White } Annotation.Initial.thickness "surface"
    { a with points = points |> IndexList.ofList }

let private roundTrip (a : Annotation) : Annotation =
    a |> Json.serialize |> Json.formatWith JsonFormattingOptions.Compact |> Json.parse |> Json.deserialize

let private keysOf (a : Annotation) =
    match Json.serialize a with
    | Object props -> props |> Map.toList |> List.map fst |> Set.ofList
    | _ -> Set.empty

let private newKeys = Set.ofList [ "confidence"; "comment"; "shapeModel"; "projectedImagesUsed" ]

let private sample =
    {
        confidence = Some 3
        comment = "contact unclear near the boulder, \"see\" image 2\nsecond line"
        shapeModel =
            Some { product = "dimorphos_g_0243mm_spc_dtm_0000n00000_v004"
                   modelName = Some "dalyErnst_dimorphos230530"
                   version = Some "004"
                   source = "fits-header"
                   credit = Some "Daly, T., Barnouin, O., Ernst, C., Nair, H., Espiritu, R., Waller, D. (2023). Derived data products for DART shapemodel: dimorphos_model_v004. NASA Planetary Data System. doi:10.26007/0nss-vd15" }
        projectedImagesUsed =
            [ { image = "afc1_a.png"; instrument = "AFC1 (AFC)"; time = "2027-03-21T10:00:00.000Z" }
              { image = "afc1_b.png"; instrument = "AFC1 (AFC)"; time = "2027-03-21T10:05:00.000Z" } ]
    }

let private persistenceTests =
    testList "persistence" [

        test "TC-23.1 the metadata survives save and load" {
            let a = { polyline [ V3d.Zero; V3d.IOO ] with metadata = sample }
            Expect.equal (roundTrip a).metadata sample "all four fields back"
        }

        test "TC-23.1 the annotation version stays 5, so older releases read the scene" {
            let a = { polyline [ V3d.Zero; V3d.IOO ] with metadata = sample }
            Expect.equal Annotation.current 5 "no version bump"
            match Json.serialize a with
            | Object props -> Expect.equal (Map.tryFind "version" props) (Some (Number 5M)) "written as version 5"
            | _ -> failtest "an annotation is a JSON object"
        }

        test "TC-23.1 an unrated annotation writes none of the new keys" {
            let keys = keysOf (polyline [ V3d.Zero; V3d.IOO ])
            Expect.isEmpty (Set.intersect keys newKeys) "same keys as before #887"
        }

        test "TC-23.1 the new keys are added, no existing key changes" {
            let before = keysOf (polyline [ V3d.Zero; V3d.IOO ])
            let after = keysOf { polyline [ V3d.Zero; V3d.IOO ] with metadata = sample }
            Expect.equal (Set.difference after before) newKeys "only the four new keys"
            Expect.isTrue (Set.isSubset before after) "every old key still written"
        }

        test "TC-23.1 an annotation without the keys reads as unrated" {
            let a = roundTrip (polyline [ V3d.Zero; V3d.IOO ])
            Expect.equal a.metadata AnnotationMetadata.empty "empty metadata"
        }

        test "TC-23.1 wrongly typed values are dropped, the annotation still loads" {
            let a = { polyline [ V3d.Zero; V3d.IOO ] with metadata = sample }
            let damaged =
                match Json.serialize a with
                | Object props ->
                    props
                    |> Map.add "confidence" (String "3")
                    |> Map.add "comment" (Number 7M)
                    |> Map.add "projectedImagesUsed" (Object Map.empty)
                    |> Map.add "shapeModel" (Array [])
                    |> Object
                | j -> j
            let back : Annotation = damaged |> Json.format |> Json.parse |> Json.deserialize
            Expect.equal back.metadata AnnotationMetadata.empty "every wrongly typed value dropped"
            Expect.equal back.key a.key "the annotation itself loaded"
        }

        test "TC-23.1 damaged entries are dropped, the annotation still loads" {
            let a = { polyline [ V3d.Zero; V3d.IOO ] with metadata = sample }
            let damaged =
                match Json.serialize a with
                | Object props ->
                    props
                    // no "product": cannot be read
                    |> Map.add "shapeModel" (Object (Map.ofList [ "version", Number 0M ]))
                    |> Map.add "projectedImagesUsed" (Array [ Object (Map.ofList [ "version", Number 0M; "image", String "ok.png" ]); Number 1M ])
                    |> Map.add "confidence" (Number 9M)
                    |> Object
                | j -> j
            let back : Annotation = damaged |> Json.format |> Json.parse |> Json.deserialize
            Expect.isNone back.metadata.shapeModel "unreadable shape model dropped"
            Expect.equal (back.metadata.projectedImagesUsed |> List.map (fun i -> i.image)) [ "ok.png" ] "the readable image kept"
            Expect.isNone back.metadata.confidence "confidence outside 1-4 dropped"
            Expect.equal back.metadata.comment sample.comment "comment intact"
        }
    ]

let private draco =
    """
    {
        "product_information": { "product_type": "opc" },
        "fits2mbi": {
            "fits_header": [
                { "MISSION":  { "value": "DART", "comment": "Name of mission" },
                  "DATASRC":  { "value": "SPC", "comment": "" },
                  "DATASRCV": { "value": "dalyErnst_dimorphos230530", "comment": "Name and version of SPC model employed" },
                  "PRODNAME": { "value": "dimorphos_g_0243mm_spc_dtm_0000n00000_v004.fits", "comment": "" },
                  "MAP_VER":  { "value": "004", "comment": "Product version number" },
                  "GSD":      { "value": 243.86045508155618, "comment": "[mm]" },
                  "EXTEND":   { "value": true, "comment": "" } }
            ]
        }
    }
    """

let private shapeModelTests =
    testList "shape model" [

        test "TC-23.2 the FITS header of a global OPC is read" {
            let md = OpcMetadata.ofJsonString "inline" draco
            Expect.equal (Map.tryFind "DATASRCV" md.fitsHeader) (Some "dalyErnst_dimorphos230530") "model name"
            Expect.equal (Map.tryFind "MAP_VER" md.fitsHeader) (Some "004") "map version"
            Expect.equal (Map.tryFind "EXTEND" md.fitsHeader) (Some "T") "boolean as FITS T"
            Expect.isSome (Map.tryFind "GSD" md.fitsHeader) "number"
        }

        test "TC-23.2 the shape model comes from the FITS header when there is one" {
            let md = OpcMetadata.ofJsonString "inline" draco
            let sm = OpcMetadata.shapeModelOf "x/dimorphos_g_0243mm_spc_dtm_0000n00000_v004.opcx" (Some md)
            Expect.equal sm.modelName (Some "dalyErnst_dimorphos230530") "model name"
            Expect.equal sm.version (Some "004") "version"
            Expect.equal sm.product "dimorphos_g_0243mm_spc_dtm_0000n00000_v004" "product without .fits"
            Expect.equal sm.source "fits-header" "source"
        }

        test "TC-23.2 without a header the version comes from the SPC file name" {
            let md = OpcMetadata.ofJsonString "inline" """{ "product_information": { "product_type": "opc" } }"""
            let sm = OpcMetadata.shapeModelOf "x/dimorphos_l_0050mm_spc_obj_0897s26313_v004.opcx" (Some md)
            Expect.equal sm.version (Some "v004") "version from file name"
            Expect.isNone sm.modelName "no model name"
            Expect.equal sm.source "file-name" "source"
            Expect.stringContains (AnnotationMetadata.describeShapeModel sm) "from file name" "says where the version came from"
        }

        test "TC-23.2 the sidecar path itself names the product too" {
            let sm = OpcMetadata.shapeModelOf "x/dimorphos_l_0050mm_spc_obj_0897s26313_v004.opc.json" None
            Expect.equal sm.version (Some "v004") "version"
            Expect.equal sm.product "dimorphos_l_0050mm_spc_obj_0897s26313_v004" "no .opc suffix"
        }

        test "TC-23.2 no header and no version in the name" {
            let sm = OpcMetadata.shapeModelOf "x/Dimorphos.opcx" None
            Expect.equal sm.source "none" "source"
            Expect.isNone sm.version "no version"
        }

        test "TC-23.6 Dimorphos v004 gets the PDS citation, global and local" {
            let global' = OpcMetadata.shapeModelOf "x/dimorphos_g_0243mm_spc_dtm_0000n00000_v004.opcx" (Some (OpcMetadata.ofJsonString "inline" draco))
            match global'.credit with
            | Some c ->
                Expect.stringContains c "Ernst, C." "Ernst credited"
                Expect.stringContains c "doi:10.26007/0nss-vd15" "with the collection DOI"
            | None -> failtest "no credit"
            // no header: matched by the file name
            let local = OpcMetadata.shapeModelOf "x/dimorphos_l_0050mm_spc_obj_0897s26313_v004.opcx" None
            Expect.equal local.credit global'.credit "the local model is part of the same collection"
            // by the SPC model name alone
            Expect.isSome (ShapeModelCredits.tryCredit [ "dalyErnst_dimorphos230530" ]) "model name"
        }

        test "TC-23.6 a credit the product states wins over the guess" {
            let stated = draco.Replace("\"product_type\": \"opc\"", "\"product_type\": \"opc\", \"credit\": \"Model by the DART SPC team\"")
            let sm = OpcMetadata.shapeModelOf "x/a_v004.opcx" (Some (OpcMetadata.ofJsonString "inline" stated))
            Expect.equal sm.credit (Some "Model by the DART SPC team") "product_information.credit"
            let author = draco.Replace("\"MISSION\":", "\"AUTHOR\": { \"value\": \"C. Ernst, T. Daly\" }, \"MISSION\":")
            let sm = OpcMetadata.shapeModelOf "x/a_v004.opcx" (Some (OpcMetadata.ofJsonString "inline" author))
            Expect.equal sm.credit (Some "C. Ernst, T. Daly") "FITS AUTHOR"
        }

        test "TC-23.6 no credit for products the table does not know" {
            Expect.isNone (OpcMetadata.shapeModelOf "x/g_01960mm_spc_dtm_dimo_0000n00000_v003.opcx" None).credit "another model version"
            Expect.isNone (OpcMetadata.shapeModelOf "x/Dimorphos.opcx" None).credit "no hint at all"
            Expect.isNone (ShapeModelCredits.tryCredit [ "dimorphos_g_0243mm_spc_dtm_0000n00000_v0041" ]) "anchored, no prefix match"
        }

        test "TC-23.7 the Source section rows" {
            let credit, rows, header = OpcMetadata.sourceRows "x/dimorphos_g_0243mm_spc_dtm_0000n00000_v004.opcx" (Some (OpcMetadata.ofJsonString "inline" draco))
            Expect.isSome credit "credit line"
            let labels = rows |> List.map fst
            Expect.containsAll labels [ "Shape model"; "Version"; "Method"; "Product"; "Grid spacing"; "Mission"; "Sidecar" ] "rows"
            Expect.equal (rows |> List.tryFind (fun (l, _) -> l = "Grid spacing") |> Option.map snd) (Some "243.9 mm") "GSD formatted"
            Expect.isNonEmpty header "FITS header for the fold-out"
            let _, rows, header = OpcMetadata.sourceRows "x/dimorphos_l_0050mm_spc_obj_0897s26313_v004.opcx" None
            Expect.isEmpty header "no header"
            Expect.stringContains (rows |> List.tryFind (fun (l, _) -> l = "Version") |> Option.map snd |> Option.defaultValue "") "from the file name" "version source said"
        }

        test "TC-23.2 the HERA V4 OPCs (PRO3D_V4_OPCS)" {
            match Environment.GetEnvironmentVariable "PRO3D_V4_OPCS" with
            | null | "" -> skiptest "set PRO3D_V4_OPCS to the OPCs_V4 folder"
            | dir when not (Directory.Exists dir) -> skiptest $"{dir} does not exist"
            | dir ->
                let opcxs = Directory.EnumerateFiles(dir, "*.opcx", SearchOption.AllDirectories) |> Seq.toList
                Expect.isNonEmpty opcxs "OPCs found"
                for opcx in opcxs do
                    let sm = OpcMetadata.shapeModelOf opcx (OpcMetadata.tryReadForOpcx opcx)
                    Log.line "[Test] %s: %s" (Path.GetFileName opcx) (AnnotationMetadata.describeShapeModel sm)
                    Expect.isSome sm.version $"{Path.GetFileName opcx}: a version"
        }
    ]

let private withSelected (annotations : list<Annotation>) (single : Option<Guid>) =
    let drawing = initDrawing ()
    let groups =
        annotations |> List.fold (fun g a -> addLeaf (Leaf.Annotations a) g) drawing.annotations
    let selected =
        match single with
        | Some _ -> HashSet.empty
        | None ->
            annotations
            |> List.map (fun a -> { id = a.key; path = groups.activeGroup.path; name = "" } : TreeSelection)
            |> HashSet.ofList
    { drawing with annotations = { groups with selectedLeaves = selected; singleSelectLeaf = single } }

let private annotationOf (drawing : DrawingModel) (key : Guid) =
    match HashMap.tryFind key drawing.annotations.flat with
    | Some (Leaf.Annotations a) -> a
    | _ -> failtest "annotation missing"

let private appTests =
    testList "keys and dialog" [

        test "TC-23.3 keys go to confidence only with a selection and nothing being drawn" {
            let a = polyline [ V3d.Zero; V3d.IOO ]
            Expect.isFalse (AnnotationMetadataApp.takesKeys (initDrawing ())) "nothing selected: semantic keys"
            let drawing = withSelected [ a ] None
            Expect.isTrue (AnnotationMetadataApp.takesKeys drawing) "selected: confidence keys"
            let drawing = { drawing with working = Some (polyline [ V3d.Zero ]) }
            Expect.isFalse (AnnotationMetadataApp.takesKeys drawing) "drawing: semantic keys"
        }

        test "TC-23.3 confidence goes on every selected annotation, in one undo step" {
            let a = polyline [ V3d.Zero; V3d.IOO ]
            let b = polyline [ V3d.OIO; V3d.OOI ]
            let drawing = withSelected [ a; b ] None
            let drawing', _, feedback = AnnotationMetadataApp.update drawing AnnotationMetadataModel.initial (SetConfidence 2)
            Expect.equal (annotationOf drawing' a.key).metadata.confidence (Some 2) "a rated"
            Expect.equal (annotationOf drawing' b.key).metadata.confidence (Some 2) "b rated"
            Expect.equal drawing'.undoStack.Length (drawing.undoStack.Length + 1) "one undo entry"
            Expect.equal feedback (Some "Confidence 2 set on 2 annotations") "message"
            let undone = DrawingApp.applyUndoDelta drawing'.annotations drawing'.undoStack.Head
            Expect.isNone (match HashMap.tryFind a.key undone.flat with Some (Leaf.Annotations x) -> x.metadata.confidence | _ -> Some 0) "undo clears it"
        }

        test "TC-23.3 confidence outside 1-4 is ignored" {
            let a = polyline [ V3d.Zero; V3d.IOO ]
            let drawing = withSelected [ a ] (Some a.key)
            let drawing', _, feedback = AnnotationMetadataApp.update drawing AnnotationMetadataModel.initial (SetConfidence 5)
            Expect.isNone (annotationOf drawing' a.key).metadata.confidence "not rated"
            Expect.isNone feedback "no message"
        }

        test "TC-23.4 the comment dialog opens on one annotation and saves on commit" {
            let a = { polyline [ V3d.Zero; V3d(2.0, 0.0, 0.0) ] with metadata = { AnnotationMetadata.empty with comment = "old" } }
            let drawing = withSelected [ a ] (Some a.key)
            let drawing, model, _ = AnnotationMetadataApp.update drawing AnnotationMetadataModel.initial OpenComment
            match model.comment with
            | None -> failtest "dialog not open"
            | Some ed ->
                Expect.equal ed.initial "old" "pre-filled"
                Expect.equal ed.anchor (V3d(1.0, 0.0, 0.0)) "anchored at the centre"
            let drawing', model', feedback = AnnotationMetadataApp.update drawing model (CommitComment "new")
            Expect.equal (annotationOf drawing' a.key).metadata.comment "new" "saved"
            Expect.isNone model'.comment "dialog closed"
            Expect.equal feedback (Some "Comment saved") "message"
        }

        test "TC-23.4 a blank comment removes the comment, trailing blanks are dropped" {
            let a = { polyline [ V3d.Zero; V3d.IOO ] with metadata = { AnnotationMetadata.empty with comment = "old" } }
            let drawing = withSelected [ a ] (Some a.key)
            let drawing, model, _ = AnnotationMetadataApp.update drawing AnnotationMetadataModel.initial OpenComment
            let drawing', _, feedback = AnnotationMetadataApp.update drawing model (CommitComment "  \n ")
            Expect.equal (annotationOf drawing' a.key).metadata.comment "" "removed"
            Expect.equal feedback (Some "Comment removed") "message"
            let drawing, model, _ = AnnotationMetadataApp.update drawing' AnnotationMetadataModel.initial OpenComment
            let drawing', _, _ = AnnotationMetadataApp.update drawing model (CommitComment "line one\nline two\n")
            Expect.equal (annotationOf drawing' a.key).metadata.comment "line one\nline two" "inner line break kept, trailing one dropped"
        }

        test "TC-23.4 cancel leaves the comment as it was" {
            let a = { polyline [ V3d.Zero; V3d.IOO ] with metadata = { AnnotationMetadata.empty with comment = "old" } }
            let drawing = withSelected [ a ] (Some a.key)
            let drawing, model, _ = AnnotationMetadataApp.update drawing AnnotationMetadataModel.initial OpenComment
            let drawing', model', _ = AnnotationMetadataApp.update drawing model CancelComment
            Expect.equal (annotationOf drawing' a.key).metadata.comment "old" "unchanged"
            Expect.isNone model'.comment "dialog closed"
            Expect.equal drawing'.undoStack.Length drawing.undoStack.Length "no undo entry"
        }

        test "TC-23.4 two selected annotations: no dialog, a hint" {
            let a = polyline [ V3d.Zero; V3d.IOO ]
            let b = polyline [ V3d.OIO; V3d.OOI ]
            let drawing = withSelected [ a; b ] None
            let _, model, feedback = AnnotationMetadataApp.update drawing AnnotationMetadataModel.initial OpenComment
            Expect.isNone model.comment "no dialog"
            Expect.isSome feedback "hint"
        }

        test "TC-23.4 the dialog's screen position follows the camera" {
            let view = CameraView.lookAt (V3d(0.0, 0.0, 10.0)) V3d.Zero V3d.OIO
            let frustum = Frustum.perspective 60.0 0.1 100.0 1.0
            match AnnotationMetadataApp.screenPosition view frustum V3d.Zero with
            | Some p -> Expect.isTrue (Vec.distance p (V2d(0.5, 0.5)) < 1e-6) $"centre of the view, got {p}"
            | None -> failtest "in front of the camera"
            // right of the axis is right on screen, above it is up (top-left origin)
            match AnnotationMetadataApp.screenPosition view frustum (V3d(1.0, 1.0, 0.0)) with
            | Some p -> Expect.isTrue (p.X > 0.5 && p.Y < 0.5) $"upper right, got {p}"
            | None -> failtest "in front of the camera"
            Expect.isNone (AnnotationMetadataApp.screenPosition view frustum (V3d(0.0, 0.0, 20.0))) "behind the camera"
        }

        test "TC-23.5 captureFinished updates the annotation and its undo entry" {
            let a = polyline [ V3d.Zero; V3d.IOO ]
            // the model's copy differs from the undo entry's, so the test sees whether the
            // entry is replaced by the captured annotation
            let inModel = { a with metadata = { a.metadata with comment = "in the model" } }
            let drawing = initDrawing ()
            let groups = addLeaf (Leaf.Annotations inModel) drawing.annotations
            let drawing =
                { drawing with annotations = groups }
                |> DrawingApp.pushUndo (LeafAdded (Leaf.Annotations a, groups.activeGroup.path))
            // no surfaces, no projected images: the shape model stays unknown, nothing fails
            let gis = Gis.GisApp.initial None
            let surfaces = SurfaceModel.initial
            let refSystem = ReferenceSystem.initial
            let drawing', _ = AnnotationMetadataApp.captureFinished gis surfaces refSystem HashMap.empty (Some a) drawing
            let a' = annotationOf drawing' a.key
            Expect.isNone a'.metadata.shapeModel "unknown surface"
            Expect.isEmpty a'.metadata.projectedImagesUsed "nothing projected"
            match drawing'.undoStack with
            | LeafAdded (Leaf.Annotations x, _) :: _ ->
                // compared by key and metadata: the record holds NaN (manual dip), never equal
                Expect.equal x.key a'.key "the undo entry is the annotation"
                Expect.equal x.metadata a'.metadata "the undo entry carries the captured metadata"
                Expect.equal x.metadata.comment "in the model" "replaced, not the stale copy"
            | _ -> failtest "undo entry"
        }
    ]

let tests =
    testList "TC-23 Annotation metadata" [
        persistenceTests
        shapeModelTests
        appTests
    ]
