/// Section 21 — Outcrop Traces
///   TC-21.1 (settings), TC-21.2 (bed thickness fitting)
///
///   Everything goes through OutcropTraceApp.update. The attitude maths that decides what
///   is actually drawn is covered separately in OutcropTraceAttitudeTest.
module PRo3D.Tests.Section21_OutcropTraces

open Aardvark.Base
open Aardvark.UI.Primitives              // Numeric, ColorPicker

open Expecto

open PRo3D.Core

let tests =
    testList "Outcrop traces" [

        test "TC-21.1 outcrop traces start disabled, with DnS as the only source" {
            let m = OutcropTraceModel.initial
            Expect.isFalse m.enabled "traces should be off by default"
            Expect.isTrue m.useDnS "dip and strike annotations should contribute by default"
            Expect.isFalse m.usePolyline
                "polylines should not contribute by default - their plane fit is poorly conditioned"
        }

        test "TC-21.1 ToggleEnabled turns outcrop traces on" {
            let m = OutcropTraceApp.update OutcropTraceModel.initial OutcropTraceAction.ToggleEnabled
            Expect.isTrue m.enabled "traces should be enabled after toggling"
        }

        test "TC-21.1 SetBedThickness changes the spacing of the sequence" {
            let m = OutcropTraceApp.update OutcropTraceModel.initial (OutcropTraceAction.SetBedThickness (Numeric.SetValue 2.5))
            Expect.floatClose Accuracy.high m.bedThickness.value 2.5 "bed thickness should be 2.5"
        }

        test "TC-21.1 SetTraceWidth changes the drawn band width" {
            let m = OutcropTraceApp.update OutcropTraceModel.initial (OutcropTraceAction.SetTraceWidth (Numeric.SetValue 0.5))
            Expect.floatClose Accuracy.high m.traceWidth.value 0.5 "trace width should be 0.5"
        }

        test "TC-21.1 SetProjectionFactor and SetProjectionFloor change the extrapolation" {
            let m =
                OutcropTraceModel.initial
                |> fun x -> OutcropTraceApp.update x (OutcropTraceAction.SetProjectionFactor (Numeric.SetValue 3.0))
                |> fun x -> OutcropTraceApp.update x (OutcropTraceAction.SetProjectionFloor (Numeric.SetValue 50.0))
            Expect.floatClose Accuracy.high m.projectionFactor.value 3.0 "projection factor should be 3"
            Expect.floatClose Accuracy.high m.projectionFloor.value 50.0 "projection floor should be 50"
        }

        test "TC-21.1 SetColor changes the trace colour" {
            let m = OutcropTraceApp.update OutcropTraceModel.initial (OutcropTraceAction.SetColor (ColorPicker.SetColor C4b.Green))
            Expect.equal m.color.c C4b.Green "the trace colour should be green"
        }

        test "TC-21.2 FitBedThickness puts about eight traces across the projection radius" {
            // 2 * radius / 8: the seeded value should show a legible sequence immediately
            // rather than a solid wash or a single line.
            let m = OutcropTraceApp.update OutcropTraceModel.initial (OutcropTraceAction.FitBedThickness 40.0)
            Expect.floatClose Accuracy.high m.bedThickness.value 10.0 "80 m across the sequence, eight beds"
        }

        test "TC-21.2 FitBedThickness stays inside the control's range" {
            let m = OutcropTraceApp.update OutcropTraceModel.initial (OutcropTraceAction.FitBedThickness 1e9)
            Expect.isLessThanOrEqual m.bedThickness.value OutcropTraceModel.initial.bedThickness.max
                "a huge selection must not push the control past its maximum"
        }
    ]
