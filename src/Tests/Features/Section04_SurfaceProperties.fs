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
