/// Section 4 — Surface Properties and Controls
///   TC-4.1 (Surface Properties Panel), TC-4.2 (Surface Visibility Toggle),
///   TC-4.3 (Surface Color Correction), TC-4.4 (Surface Translation),
///   TC-4.5 (Surface FillMode (Wireframe))
///
///   Property edits go through the real SurfaceProperties.update,
///   ColorCorrectionProperties.update and TransformationApp.update on a Surface
///   built exactly as ViewerAction.ImportSurface builds it.
module PRo3D.Tests.Section04_SurfaceProperties

open Aardvark.Base
open Aardvark.Rendering                  // FillMode
open Aardvark.UI.Primitives              // Numeric, ColorPicker

open Expecto

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Tests

let tests =
    testList "Section 4 — Surface Properties and Controls" [

        // TC-4.1 Surface Properties Panel

        test "TC-4.1 SetName renames the surface" {
            let s' = SurfaceProperties.update (makeSurface "surf") (SurfaceProperties.Action.SetName "renamed")
            Expect.equal s'.name "renamed" "the surface name should update"
        }

        test "TC-4.1 SetPriority updates the priority value" {
            let s' = SurfaceProperties.update (makeSurface "surf") (SurfaceProperties.Action.SetPriority (Numeric.SetValue 3.0))
            Expect.floatClose Accuracy.high s'.priority.value 3.0 "priority should be set to 3"
        }

        test "TC-4.1 ToggleIsActive flips the active flag" {
            let s  = makeSurface "surf"
            let s' = SurfaceProperties.update s SurfaceProperties.Action.ToggleIsActive
            Expect.equal s'.isActive (not s.isActive) "active flag should toggle"
        }

        // TC-4.2 Surface Visibility Toggle

        test "TC-4.2 ToggleVisible hides a visible surface" {
            let s = makeSurface "surf"
            Expect.isTrue s.isVisible "a fresh surface should be visible"
            let s' = SurfaceProperties.update s SurfaceProperties.Action.ToggleVisible
            Expect.isFalse s'.isVisible "surface should be hidden after toggle"
        }

        test "TC-4.2 ToggleVisible twice restores visibility" {
            let s' =
                makeSurface "surf"
                |> fun s -> SurfaceProperties.update s SurfaceProperties.Action.ToggleVisible
                |> fun s -> SurfaceProperties.update s SurfaceProperties.Action.ToggleVisible
            Expect.isTrue s'.isVisible "double toggle should restore visibility"
        }

        // TC-4.3 Surface Color Correction

        test "TC-4.3 UseColor toggles the colour-correction tint on/off" {
            let cc  = (makeSurface "surf").colorCorrection
            let cc' = ColorCorrectionProperties.update cc ColorCorrectionProperties.Action.UseColor
            Expect.equal cc'.useColor (not cc.useColor) "useColor should toggle"
        }

        test "TC-4.3 SetContrast updates the contrast value" {
            let cc  = (makeSurface "surf").colorCorrection
            let cc' = ColorCorrectionProperties.update cc (ColorCorrectionProperties.Action.SetContrast (Numeric.SetValue 0.5))
            Expect.floatClose Accuracy.high cc'.contrast.value 0.5 "contrast should be set to 0.5"
        }

        test "TC-4.3 SetColor changes the tint colour" {
            let cc  = (makeSurface "surf").colorCorrection
            let cc' = ColorCorrectionProperties.update cc
                          (ColorCorrectionProperties.Action.SetColor (ColorPicker.Action.SetColor C4b.Red))
            Expect.equal cc'.color.c C4b.Red "tint colour should be red"
        }

        // TC-4.4 Surface Translation

        test "TC-4.4 SetPickedTranslation moves the surface and marks the trafo changed" {
            let t  = (makeSurface "surf").transformation
            let p  = V3d(1.0, 2.0, 3.0)
            let t' = TransformationApp.update t (TransformationApp.Action.SetPickedTranslation p) ReferenceSystem.initial V3d.Zero
            Expect.equal t'.translation.value p "translation should be the picked point"
            Expect.isTrue t'.trafoChanged "the transform should be flagged as changed"
        }

        test "TC-4.4 SetPickedReferenceSystem places the frame and makes it visible" {
            // `showTrafoRefSys` is whatever the surface was loaded with, and read6 defaults
            // it to false when the scene JSON has no such field - so placing a reference
            // system could store it correctly and draw nothing. Placing one shows it.
            let t  = { (makeSurface "surf").transformation with showTrafoRefSys = false }
            let p  = V3d(1.0, 2.0, 3.0)
            let t' = TransformationApp.update t (TransformationApp.Action.SetPickedReferenceSystem p) ReferenceSystem.initial V3d.Zero
            match t'.refSys with
            | None -> failtest "a picked reference system should be stored"
            | Some af -> Expect.equal af.Trans p "the frame should sit at the picked point"
            Expect.isTrue t'.showTrafoRefSys "placing a reference system should make it visible"
        }

        test "TC-4.4 Yaw on Earth turns the surface around the local up, not Earth's spin axis" {
            // A geocentric Earth scene at ~37 N (ExoMars field test site). Earth used to
            // take the identity basis, so yaw rotated around ECEF z - 53 degrees off the
            // local vertical - and the surface swung off sideways.
            let pivot = V3d(5087315.589125863, -208810.77352202998, 3837346.3202168946)
            let up    = pivot.Normalized
            let north = V3d(-0.6017485208893706, 0.02469506020043715, 0.7983037464581708)
            let north = (north - up * north.Dot(up)).Normalized
            let east  = north.Cross(up).Normalized
            let frame = Affine3d(M33d.FromCols(north, east, up), pivot)

            let t = (makeSurface "surf").transformation
            let t =
                { t with
                    pivotMode = PivotMode.PickPivot
                    pivot     = { t.pivot with value = pivot }
                    refSys    = Some frame
                    yaw       = { t.yaw with value = 90.0 } }
            let refSys = { ReferenceSystem.initial with planet = Planet.Earth; origin = pivot }

            let trafo = TransformationApp.fullTrafo' t refSys None None
            let onAxis = pivot + up * 10.0
            let aside  = pivot + north * 10.0

            Expect.isLessThan (Vec.distance (trafo.Forward.TransformPos pivot) pivot) 1e-6 "the pivot should stay put"
            Expect.isLessThan (Vec.distance (trafo.Forward.TransformPos onAxis) onAxis) 1e-6 "a point above the pivot is on the yaw axis and should stay put"
            let turned = trafo.Forward.TransformPos aside - pivot
            Expect.isLessThan (abs (turned.Dot up)) 1e-6 "a point north of the pivot should stay in the horizontal plane"
            Expect.isGreaterThan (abs (turned.Normalized.Dot east)) (1.0 - 1e-9) "a 90 degree yaw should turn north onto the east-west axis"
        }

        // TC-4.5 Surface FillMode (Wireframe)

        test "TC-4.5 SetFillMode switches the surface to wireframe" {
            let s' = SurfaceProperties.update (makeSurface "surf") (SurfaceProperties.Action.SetFillMode FillMode.Line)
            Expect.equal s'.fillMode FillMode.Line "fill mode should be wireframe (Line)"
        }

        test "TC-4.5 SetFillMode back to Fill restores solid rendering" {
            let s' =
                makeSurface "surf"
                |> fun s -> SurfaceProperties.update s (SurfaceProperties.Action.SetFillMode FillMode.Line)
                |> fun s -> SurfaceProperties.update s (SurfaceProperties.Action.SetFillMode FillMode.Fill)
            Expect.equal s'.fillMode FillMode.Fill "fill mode should be solid again"
        }
    ]
