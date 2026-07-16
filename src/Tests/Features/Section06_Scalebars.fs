/// Section 6 — Scalebars
///   TC-6.1 (Place Scalebar), TC-6.2 (Scalebar Properties)
///
///   Exercises the real ScaleBarsApp.update / ScaleBarProperties.update. Planet.None
///   keeps the segment maths pure (no CooTransformation).
module PRo3D.Tests.Section06_Scalebars

open Aardvark.Base
open Aardvark.Rendering                  // CameraView
open Aardvark.UI.Primitives              // Numeric

open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Core
open PRo3D.Tests

module private SB =
    let refSys = { ReferenceSystem.initial with planet = Planet.None }
    let view   = CameraView.lookAt (V3d(0.0, 0.0, 10.0)) V3d.Zero V3d.OOI

    let place (p : V3d) =
        ScaleBarsApp.update ScaleBarsModel.initial
            (ScaleBarsAction.AddScaleBar(p, InitScaleBarsParams.initialScaleBarDrawing, view)) refSys

    /// the single scalebar of the model
    let only (m : ScaleBarsModel) = m.scaleBars |> HashMap.toList |> List.head |> snd
    let onlyId (m : ScaleBarsModel) = m.scaleBars |> HashMap.toList |> List.head |> fst

let tests =
    testList "Section 6 — Scalebars" [

        // TC-6.1 Place Scalebar

        test "TC-6.1 AddScaleBar places one scalebar and selects it" {
            let m = SB.place (V3d(1.0, 2.0, 3.0))
            Expect.equal (m.scaleBars |> HashMap.count) 1 "one scalebar should be placed"
            Expect.equal m.selectedScaleBar (Some (SB.onlyId m)) "the new scalebar should be selected"
        }

        test "TC-6.1 the placed scalebar starts visible" {
            let m = SB.place (V3d(1.0, 2.0, 3.0))
            Expect.isTrue (SB.only m).isVisible "a freshly placed scalebar should be visible"
        }

        // TC-6.2 Scalebar Properties

        test "TC-6.2 IsVisible toggles a scalebar's visibility" {
            let placed = SB.place (V3d(1.0, 2.0, 3.0))
            let id     = SB.onlyId placed
            let before = (placed.scaleBars |> HashMap.find id).isVisible
            let after  = ScaleBarsApp.update placed (ScaleBarsAction.IsVisible id) SB.refSys
            Expect.equal (after.scaleBars |> HashMap.find id).isVisible (not before)
                "visibility should toggle"
        }

        test "TC-6.2 renaming the selected scalebar via its properties" {
            let placed  = SB.place (V3d(1.0, 2.0, 3.0))
            let renamed =
                ScaleBarsApp.update placed
                    (ScaleBarsAction.PropertiesMessage (ScaleBarProperties.Action.SetName "my bar")) SB.refSys
            Expect.equal (SB.only renamed).name "my bar" "the selected scalebar should be renamed"
        }

        test "TC-6.2 setting the length of the selected scalebar" {
            let placed  = SB.place (V3d(1.0, 2.0, 3.0))
            let resized =
                ScaleBarsApp.update placed
                    (ScaleBarsAction.PropertiesMessage (ScaleBarProperties.Action.SetLength (Numeric.SetValue 12.0))) SB.refSys
            Expect.floatClose Accuracy.high (SB.only resized).length.value 12.0
                "the scalebar length should be updated"
        }

        test "TC-6.2 removing a scalebar clears it and its selection" {
            let placed  = SB.place (V3d(1.0, 2.0, 3.0))
            let id      = SB.onlyId placed
            let removed = ScaleBarsApp.update placed (ScaleBarsAction.RemoveSB id) SB.refSys
            Expect.equal (removed.scaleBars |> HashMap.count) 0 "the scalebar should be gone"
            Expect.isNone removed.selectedScaleBar "nothing should be selected after removal"
        }
    ]
