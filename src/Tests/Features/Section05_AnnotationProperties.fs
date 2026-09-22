/// Section 5 — Annotation Properties and Measurements
///   TC-5.1 (Annotation Properties Panel), TC-5.2 (Annotation Measurements),
///   TC-5.3 (Annotation Text Note)
///
///   Property edits go through the real AnnotationProperties.update; measurements
///   through the real Calculations helpers.
module PRo3D.Tests.Section05_AnnotationProperties

open System

open Aardvark.Base
open Aardvark.UI                         // ColorInput
open Aardvark.UI.Primitives              // NumericInput, ColorPicker

open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Tests

let private propertyTests =
    testList "Annotation property mutations" [

        let refSystem = ReferenceSystem.initial

        let baseAnn () =
            Annotation.make Projection.Linear None Geometry.Line None
                ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"

        // TC-5.1 Annotation Properties Panel

        test "TC-5.1 SetVisible false hides a visible annotation" {
            let ann  = baseAnn ()
            Expect.isTrue ann.visible "pre: annotation should be visible"
            let ann' = AnnotationProperties.update refSystem ann (AnnotationProperties.Action.SetVisible false)
            Expect.isFalse ann'.visible "annotation should be hidden"
        }

        test "TC-5.1 SetVisible back to true restores visibility" {
            let ann  = baseAnn ()
            let ann' =
                ann
                |> fun a -> AnnotationProperties.update refSystem a (AnnotationProperties.Action.SetVisible false)
                |> fun a -> AnnotationProperties.update refSystem a (AnnotationProperties.Action.SetVisible true)
            Expect.isTrue ann'.visible "re-checking the box should restore visibility"
        }

        test "TC-5.1 ChangeColor changes annotation color" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann
                           (AnnotationProperties.Action.ChangeColor (ColorPicker.Action.SetColor C4b.Green))
            Expect.equal ann'.color.c C4b.Green "color should be green"
        }

        test "TC-5.1 SetShowDns false clears showDns on DnS annotation" {
            let ann =
                Annotation.make Projection.Linear None Geometry.DnS None
                    ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surf"
            Expect.isTrue ann.showDns "DnS annotation should start with showDns = true"
            let ann' = AnnotationProperties.update refSystem ann (AnnotationProperties.Action.SetShowDns false)
            Expect.isFalse ann'.showDns "showDns should be set to false"
        }

        test "TC-5.1 SetGeometry changes the geometry" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann
                           (AnnotationProperties.Action.SetGeometry Geometry.Polyline)
            Expect.equal ann'.geometry Geometry.Polyline "geometry should be changed to Polyline"
        }

        test "TC-5.1 SetProjection changes the projection" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann
                           (AnnotationProperties.Action.SetProjection Projection.Sky)
            Expect.equal ann'.projection Projection.Sky "projection should be Sky"
        }

        // TC-5.3 Annotation Text Note

        test "TC-5.3 SetText sets the annotation text" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann (AnnotationProperties.Action.SetText "hello")
            Expect.equal ann'.text "hello" "text should be updated"
        }

        test "TC-5.3 SetText to empty string clears text" {
            let ann  = baseAnn ()
            let ann' = AnnotationProperties.update refSystem ann (AnnotationProperties.Action.SetText "")
            Expect.equal ann'.text "" "text should be cleared"
        }

        test "TC-5.3 SetShowText false clears showText" {
            let ann  = baseAnn ()
            Expect.isTrue ann.showText "annotation should start with showText = true"
            let ann' = AnnotationProperties.update refSystem ann (AnnotationProperties.Action.SetShowText false)
            Expect.isFalse ann'.showText "showText should be set to false"
        }
    ]

// TC-5.2 Annotation Measurements
let private measurementTests =
    testList "Geometric measurement calculations" [

        let up = V3d.OOI

        // verticalDelta

        test "TC-5.2 verticalDelta with single point returns 0" {
            Expect.floatClose Accuracy.high
                (Calculations.verticalDelta [ V3d(1.0, 2.0, 3.0) ] up) 0.0
                "single-point vertical delta should be 0"
        }

        test "TC-5.2 verticalDelta same height returns 0" {
            Expect.floatClose Accuracy.high
                (Calculations.verticalDelta [ V3d(0.0, 0.0, 5.0); V3d(3.0, 4.0, 5.0) ] up) 0.0
                "same-height points: vertical delta should be 0"
        }

        test "TC-5.2 verticalDelta ascending returns positive" {
            Expect.floatClose Accuracy.high
                (Calculations.verticalDelta [ V3d.OOO; V3d(0.0, 0.0, 10.0) ] up) 10.0
                "ascending by 10 m: vertical delta should be 10"
        }

        test "TC-5.2 verticalDelta descending returns negative" {
            Expect.floatClose Accuracy.high
                (Calculations.verticalDelta [ V3d(0.0, 0.0, 10.0); V3d.OOO ] up) -10.0
                "descending by 10 m: vertical delta should be -10"
        }

        // horizontalDelta

        test "TC-5.2 horizontalDelta with single point returns 0" {
            Expect.floatClose Accuracy.high
                (Calculations.horizontalDelta [ V3d(1.0, 2.0, 3.0) ] up) 0.0
                "single-point horizontal delta should be 0"
        }

        test "TC-5.2 horizontalDelta 3-4-5 triangle in horizontal plane" {
            Expect.floatClose Accuracy.high
                (Calculations.horizontalDelta [ V3d.OOO; V3d(3.0, 4.0, 0.0) ] up) 5.0
                "3-4-5 triangle: horizontal distance should be 5"
        }

        test "TC-5.2 horizontalDelta for points differing only in Z returns 0" {
            Expect.floatClose Accuracy.high
                (Calculations.horizontalDelta [ V3d.OOO; V3d(0.0, 0.0, 10.0) ] up) 0.0
                "vertical-only movement: horizontal delta should be 0"
        }

        // getDistance

        test "TC-5.2 getDistance for two coincident points is 0" {
            let p = V3d(1.0, 2.0, 3.0)
            Expect.floatClose Accuracy.high (Calculations.getDistance [p; p]) 0.0
                "coincident points: distance should be 0"
        }

        test "TC-5.2 getDistance for a 3-4-5 triangle leg is 5" {
            Expect.floatClose Accuracy.high
                (Calculations.getDistance [ V3d.OOO; V3d(3.0, 4.0, 0.0) ]) 5.0
                "3-4-5: distance should be 5"
        }

        test "TC-5.2 getDistance accumulates over multiple segments" {
            Expect.floatClose Accuracy.high
                (Calculations.getDistance [ V3d.OOO; V3d(1.0, 0.0, 0.0); V3d(2.0, 0.0, 0.0) ]) 2.0
                "two 1-unit segments: total distance should be 2"
        }

        // pitch

        test "TC-5.2 pitch of a horizontal vector is 0 degrees" {
            Expect.floatClose Accuracy.high (Calculations.pitch up V3d.IOO) 0.0
                "horizontal direction: pitch should be 0"
        }

        test "TC-5.2 pitch of the up vector is 90 degrees" {
            Expect.floatClose Accuracy.high (Calculations.pitch up up) 90.0
                "up direction: pitch should be 90"
        }

        test "TC-5.2 pitch of a downward vector is -90 degrees" {
            Expect.floatClose Accuracy.high (Calculations.pitch up (-up)) -90.0
                "down direction: pitch should be -90"
        }

        // computeAzimuth

        test "TC-5.2 computeAzimuth returns a finite number" {
            let az = Calculations.computeAzimuth V3d.IOO V3d.OIO up
            Expect.isTrue (Double.IsFinite az) "azimuth should be finite"
        }
    ]

let tests =
    testList "Section 5 — Annotation Properties and Measurements" [
        propertyTests
        measurementTests
    ]
