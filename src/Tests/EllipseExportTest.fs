module EllipseExportTest

open System

open FSharp.Data.Adaptive
open Chiron

open Aardvark.Base
open Aardvark.UI.Primitives

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

// last, so nothing opened above shadows `test`
open Expecto

// Ellipses carry their shape (centre, semi-axes, long-axis azimuth) from the
// moment they are constructed; the Boulders export and colour by category only
// read it back. Issue #644.

let private up    = V3d.OOI
let private north = V3d.OIO

/// Finishes a working ellipse the way the drawing tool does, on a flat frame
/// (up +Z, north +Y). The "surface" is the construction plane itself, so no OPC
/// and no GL is needed.
let private drawEllipse (geometry : Geometry) (clicked : list<V3d>) =
    let working =
        { Annotation.initial with
            key        = Guid.NewGuid()
            geometry   = geometry
            projection = Projection.Viewpoint
            points     = IndexList.ofList clicked }
    let model = { DrawingModel.initialdrawing with working = Some working }
    DrawingApp.getFinishedAnnotation
        up north Planet.None None (Some (fun p -> Some p)) FreeFlyController.initial.view model

let private stored (a : Option<Annotation>) =
    match a |> Option.bind (fun a -> a.ellipticResults) with
    | Some e -> e
    | None   -> failtest "the finished ellipse must carry its stored shape"

/// A boulder with a known shape: centre (100, 50, 5), 6 m by 2 m, long axis
/// toward north-east.
let private boulder () =
    let center = V3d(100.0, 50.0, 5.0)
    let major  = V3d(1.0, 1.0, 0.0).Normalized * 3.0
    let minor  = V3d(1.0, -1.0, 0.0).Normalized
    let outline =
        [ for i in 0 .. 11 do
            let t = Constant.PiTimesTwo * float i / 12.0
            // lifted off the plane on one side, as a draped outline would be
            yield center + major * cos t + minor * sin t + V3d(0.0, 0.0, max 0.0 (sin t) * 4.0) ]
    { Annotation.initial with
        key             = Guid.NewGuid()
        text            = "b1"
        surfaceName     = "Dimorphos"
        geometry        = Geometry.AxisEllipse
        points          = IndexList.ofList outline
        ellipticResults = Some (EllipticAnnotations.Measures.ofAxes up north center major minor) }

let private line () =
    { Annotation.initial with
        key      = Guid.NewGuid()
        geometry = Geometry.Polyline
        points   = IndexList.ofList [ V3d.Zero; V3d(10.0, 0.0, 0.0) ] }

let private cell (column : string) (record : ExportRecord) =
    record.fields |> List.tryFind (fst >> (=) column) |> Option.map snd

let private number (column : string) (record : ExportRecord) =
    match cell column record with
    | Some (VNum v) -> Some v
    | _ -> None

let private roundTrip (a : Annotation) : Annotation =
    a
    |> Json.serialize
    |> Json.formatWith JsonFormattingOptions.Pretty
    |> Json.parse
    |> Json.deserialize

let private azimuthTests =
    testList "azimuth" [
        test "axial azimuth is clockwise from north and folded into [0, 180)" {
            let azimuth v = EllipticAnnotations.Measures.axialAzimuth up north v
            for v, expected in [ V3d.OIO, 0.0; V3d.IOO, 90.0; -V3d.OIO, 0.0; -V3d.IOO, 90.0
                                 V3d(1.0, 1.0, 0.0), 45.0; V3d(-1.0, -1.0, 0.0), 45.0
                                 V3d(-1.0, 1.0, 0.0), 135.0; V3d(1.0, -1.0, 0.0), 135.0 ] do
                Expect.floatClose Accuracy.high (azimuth v) expected (sprintf "azimuth of %A" v)
        }

        test "an axis a hair west of north is 0, not 180" {
            let azimuth = EllipticAnnotations.Measures.axialAzimuth up north (V3d(-1e-10, 1.0, 0.0))
            Expect.equal azimuth 0.0 "north is written as 0"
        }

        test "only the horizontal part of a tilted axis counts" {
            let azimuth = EllipticAnnotations.Measures.axialAzimuth up north (V3d(1.0, 0.0, 5.0))
            Expect.floatClose Accuracy.high azimuth 90.0 "steep, but pointing east"
        }

        test "a vertical axis has no azimuth" {
            Expect.isTrue (Double.IsNaN (EllipticAnnotations.Measures.axialAzimuth up north (V3d(0.0, 0.0, 2.0))))
                "straight up has no horizontal direction"
        }

        test "on a body the frame is re-derived at the ellipse centre" {
            // a flat frame keeps the reference system's own vectors ...
            let flatUp, flatNorth = EllipticAnnotations.Measures.localFrame Planet.None up north (V3d(1e6, 0.0, 0.0))
            Expect.equal (flatUp, flatNorth) (up, north) "flat frames are left alone"
            // ... a body does not: at lon 0 on the equator north is the pole axis
            let bodyUp, bodyNorth = EllipticAnnotations.Measures.localFrame Planet.Mars up north (V3d(3396190.0, 0.0, 0.0))
            Expect.isGreaterThan (Vec.dot bodyUp V3d.IOO) 0.99 "up points away from the body"
            Expect.isGreaterThan (Vec.dot bodyNorth V3d.OOI) 0.99 "north points toward the pole"
        }
    ]

let private drawingTests =
    testList "drawing" [
        test "a drawn axis ellipse stores the ellipse its clicks define" {
            // clicked axis 4 m long, third click 3 m off it: the *third* click
            // sets the long axis here, so the stored major must not be Axis0
            let e =
                drawEllipse Geometry.AxisEllipse [ V3d(-2.0, 0.0, 5.0); V3d(2.0, 0.0, 5.0); V3d(1.0, 3.0, 5.0) ]
                |> stored
            Expect.isLessThan (e.center - V3d(0.0, 0.0, 5.0)).Length 1e-9 "centre on the clicked axis"
            Expect.floatClose Accuracy.high e.semiMajorAxis.Length 3.0 "semi-major is the longer one"
            Expect.floatClose Accuracy.high e.semiMinorAxis.Length 2.0 "semi-minor is the clicked half axis"
            Expect.floatClose Accuracy.high (abs (Vec.dot e.semiMajorAxis.Normalized V3d.OIO)) 1.0 "long axis runs north"
            Expect.floatClose Accuracy.high e.majorAxisAzimuth 0.0 "azimuth of the long axis"
            Expect.isTrue e.geographicalEllipse.IsNone "no lon/lat ellipse: the GeoJSON output stays as it was"
        }

        test "a four-point ellipse is stored as the symmetric ellipse of the same extent" {
            // clicked axis along east, 8 m; 2 m on one side, 1 m on the other
            let e =
                drawEllipse Geometry.Axis4PEllipse
                    [ V3d(-4.0, 0.0, 5.0); V3d(4.0, 0.0, 5.0); V3d(0.0, 2.0, 5.0); V3d(0.0, -1.0, 5.0) ]
                |> stored
            Expect.floatClose Accuracy.high e.semiMajorAxis.Length 4.0 "semi-major"
            Expect.floatClose Accuracy.high e.semiMinorAxis.Length 1.5 "half the full 3 m width"
            Expect.isLessThan (e.center - V3d(0.0, 0.5, 5.0)).Length 1e-9 "centre in the middle of the width"
            Expect.floatClose Accuracy.high e.majorAxisAzimuth 90.0 "long axis runs east"
        }
    ]

let private persistenceTests =
    testList "persistence" [
        test "the stored shape survives a save and load" {
            let a = boulder ()
            let e = stored (Some a)
            let back = stored (Some (roundTrip a))
            Expect.isLessThan (back.center - e.center).Length 1e-9 "centre"
            Expect.isLessThan (back.semiMajorAxis - e.semiMajorAxis).Length 1e-9 "semi-major"
            Expect.isLessThan (back.semiMinorAxis - e.semiMinorAxis).Length 1e-9 "semi-minor"
            Expect.floatClose Accuracy.high back.majorAxisAzimuth e.majorAxisAzimuth "azimuth"
            Expect.isTrue back.geographicalEllipse.IsNone "no lon/lat ellipse appears"
        }

        test "a result written before the metric fields loads, with empty values" {
            // what the dormant geographic construction wrote: lon/lat only
            let old : EllipticAnnotationResult =
                """{ "version": 0, "center": "[10, 20]", "major": "[0.1, 0]", "minor": "[0, 0.05]" }"""
                |> Json.parse
                |> Json.deserialize
            Expect.isTrue old.geographicalEllipse.IsSome "the lon/lat ellipse is kept for GeoJSON"
            Expect.isTrue old.center.AnyNaN "no metric centre"
            Expect.isTrue (Double.IsNaN old.semiMajorAxis.Length) "no semi-major"
            Expect.isTrue (Double.IsNaN old.majorAxisAzimuth) "no azimuth"

            // ... and exports empty cells, never NaN, never a wrong number
            let a = { boulder () with ellipticResults = Some old }
            let settings =
                AnnotationExportSettings.initial |> AnnotationExportSettings.applyPreset ExportPreset.Boulders
            match AnnotationExport.buildRecords settings None HashMap.empty Planet.None up [ a ] with
            | [ row ] ->
                for column in [ "semiMajorAxis"; "semiMinorAxis"; "majorAxisAzimuth" ] do
                    Expect.equal (cell column row) (Some VMissing) (sprintf "%s is empty" column)
            | rows -> failtestf "expected one row, got %d" rows.Length

            // a result with no stored vectors saves without them, and reloads the same way
            let resaved = stored (Some (roundTrip a))
            Expect.isTrue resaved.center.AnyNaN "still no metric centre after a save"
        }
    ]

let private exportTests =
    testList "export" [
        test "the Boulders columns are exactly the documented ones" {
            let settings =
                AnnotationExportSettings.initial |> AnnotationExportSettings.applyPreset ExportPreset.Boulders
            Expect.equal (AnnotationExport.schemaOf settings)
                [ "key"; "text"; "surfaceName"; "groupPath"; "semiMajorAxis"; "semiMinorAxis"; "majorAxisAzimuth"
                  "x"; "y"; "z"; "lat"; "lon"; "alt"; "body"; "latLonAltSource" ]
                "docs/AnnotationExport-CSV.md, Boulders"

            // the window re-sorts the fields; the order must survive that
            let throughWindow =
                AnnotationExportApp.update AnnotationExportModel.initial (SetPreset ExportPreset.Boulders)
                |> AnnotationExportModel.toSettings
            Expect.equal (AnnotationExport.schemaOf throughWindow) (AnnotationExport.schemaOf settings)
                "the window writes the same columns in the same order"
        }

        test "a boulder row carries the stored axes, azimuth and centre" {
            let settings =
                { AnnotationExportSettings.applyPreset ExportPreset.Boulders AnnotationExportSettings.initial with
                    coordinates = CoordinateMode.Cartesian }
            match AnnotationExport.buildRecords settings None HashMap.empty Planet.None up [ boulder () ] with
            | [ row ] ->
                Expect.floatClose Accuracy.high (number "semiMajorAxis" row |> Option.defaultValue nan) 3.0 "semi-major, m"
                Expect.floatClose Accuracy.high (number "semiMinorAxis" row |> Option.defaultValue nan) 1.0 "semi-minor, m"
                Expect.floatClose Accuracy.high (number "majorAxisAzimuth" row |> Option.defaultValue nan) 45.0 "north-east"
                // the draped outline's box centre would sit 2 m higher
                Expect.equal (number "x" row) (Some 100.0) "centre x"
                Expect.equal (number "y" row) (Some 50.0) "centre y"
                Expect.equal (number "z" row) (Some 5.0) "centre z is the ellipse's, not the outline box's"
            | rows -> failtestf "expected one row, got %d" rows.Length
        }

        test "the diameter columns are gone" {
            let names = AnnotationFields.all |> List.map AnnotationFields.columnName
            Expect.isFalse (names |> List.contains "majorDiameter") "majorDiameter retired"
            Expect.isFalse (names |> List.contains "minorDiameter") "minorDiameter retired"
            for f in [ AnnotationField.SemiMajorAxis; AnnotationField.SemiMinorAxis; AnnotationField.MajorAxisAzimuth ] do
                Expect.equal (AnnotationFields.groupOf f) AnnotationFieldGroup.Ellipse (sprintf "%A is an ellipse field" f)
        }

        test "ellipses only keeps the three ellipse geometries" {
            let ofGeometry g = { Annotation.initial with key = Guid.NewGuid(); geometry = g }
            let all =
                [ Geometry.Point; Geometry.Line; Geometry.Polyline; Geometry.Polygon; Geometry.DnS
                  Geometry.TT; Geometry.AxisEllipse; Geometry.Axis4PEllipse; Geometry.Ellipse ]
                |> List.map ofGeometry
            let kept =
                all |> List.filter (ExportTypeFilter.admits ExportTypeFilter.EllipsesOnly) |> List.map (fun a -> a.geometry)
            Expect.equal kept [ Geometry.AxisEllipse; Geometry.Axis4PEllipse; Geometry.Ellipse ] "ellipses only"
            Expect.equal (all |> List.filter (ExportTypeFilter.admits ExportTypeFilter.All)).Length all.Length "all keeps all"
        }

        test "only Boulders narrows the type filter, and every other preset widens it again" {
            let boulders =
                AnnotationExportSettings.initial |> AnnotationExportSettings.applyPreset ExportPreset.Boulders
            Expect.equal boulders.typeFilter ExportTypeFilter.EllipsesOnly "Boulders: ellipses only"
            Expect.equal boulders.scope ExportScope.All "Boulders: all annotations"
            Expect.equal boulders.longitude LongitudeConvention.Native "Boulders: native longitudes"
            Expect.equal boulders.granularity ExportGranularity.PerAnnotation "Boulders: one row per ellipse"
            Expect.equal boulders.format ExportFormat.Csv "Boulders: CSV"

            for preset in ExportPreset.all |> List.filter (fun p -> p <> ExportPreset.Custom && p <> ExportPreset.Boulders) do
                let applied = boulders |> AnnotationExportSettings.applyPreset preset
                Expect.equal applied.typeFilter ExportTypeFilter.All (sprintf "%A resets the type filter" preset)

            let custom = boulders |> AnnotationExportSettings.applyPreset ExportPreset.Custom
            Expect.equal custom.typeFilter ExportTypeFilter.EllipsesOnly "Custom changes nothing"

            // the window: the filter is a control of its own, and touching it makes the preset Custom
            let window = AnnotationExportApp.update AnnotationExportModel.initial (SetPreset ExportPreset.Boulders)
            Expect.equal window.typeFilter ExportTypeFilter.EllipsesOnly "the window shows the filter"
            let edited = AnnotationExportApp.update window (SetTypeFilter ExportTypeFilter.All)
            Expect.equal edited.typeFilter ExportTypeFilter.All "overridable"
            Expect.equal edited.preset ExportPreset.Custom "an edit makes it Custom"
        }
    ]

let private colorTests =
    testList "colour by category" [
        test "the ellipse attributes read the stored shape, and nothing else has them" {
            let b = boulder ()
            Expect.floatClose Accuracy.high (ColorByCategory.valueOf ColorCategoryAttribute.SemiMajorAxis b) 3.0 "semi-major"
            Expect.floatClose Accuracy.high (ColorByCategory.valueOf ColorCategoryAttribute.SemiMinorAxis b) 1.0 "semi-minor"
            Expect.floatClose Accuracy.high (ColorByCategory.valueOf ColorCategoryAttribute.MajorAxisAzimuth b) 45.0 "azimuth"
            for attr in [ ColorCategoryAttribute.SemiMajorAxis; ColorCategoryAttribute.SemiMinorAxis
                          ColorCategoryAttribute.MajorAxisAzimuth ] do
                Expect.isTrue (Double.IsNaN (ColorByCategory.valueOf attr (line ()))) (sprintf "a line has no %A" attr)
        }

        test "the long-axis azimuth is axial, like strike" {
            Expect.equal (ColorByCategory.cyclicPeriod ColorCategoryAttribute.MajorAxisAzimuth) (Some 180.0) "0-180 hue wheel"
            Expect.equal (ColorByCategory.unitOf ColorCategoryAttribute.MajorAxisAzimuth) "°" "degrees"
            Expect.equal (ColorByCategory.unitOf ColorCategoryAttribute.SemiMajorAxis) "m" "metres"
        }
    ]

let tests () =
    testList "ellipse export" [
        azimuthTests
        drawingTests
        persistenceTests
        exportTests
        colorTests
    ]
