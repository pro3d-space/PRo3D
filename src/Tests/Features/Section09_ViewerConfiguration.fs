/// Section 9 — Viewer Configuration
///   TC-9.1 (Viewer Config Settings)
///   TC-9.2 (Screenshots) is a render + file-capture flow with no headless model
///   slice, so it is not covered here.
///
///   Config edits go through the real ConfigProperties.update.
module PRo3D.Tests.Section09_ViewerConfiguration

open Aardvark.Base
open Aardvark.UI.Primitives              // Numeric

open Expecto

open PRo3D                                // ConfigProperties
open PRo3D.Core                           // ViewConfigModel
open PRo3D.Tests

let tests =
    testList "Section 9 — Viewer Configuration" [

        // TC-9.1 Viewer Config Settings

        test "TC-9.1 SetNearPlane updates the near plane" {
            let m = ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SetNearPlane (Numeric.SetValue 0.5))
            Expect.floatClose Accuracy.high m.nearPlane.value 0.5 "near plane should be 0.5"
        }

        test "TC-9.1 SetFarPlane updates the far plane" {
            let m = ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SetFarPlane (Numeric.SetValue 5000.0))
            Expect.floatClose Accuracy.high m.farPlane.value 5000.0 "far plane should be 5000"
        }

        test "TC-9.1 SetNavigationSensitivity updates the sensitivity" {
            let m = ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SetNavigationSensitivity (Numeric.SetValue 3.0))
            Expect.floatClose Accuracy.high m.navigationSensitivity.value 3.0 "sensitivity should be 3"
        }

        test "TC-9.1 ToggleLodColors flips the LoD colouring flag" {
            let m = ConfigProperties.update ViewConfigModel.initial ConfigProperties.Action.ToggleLodColors
            Expect.equal m.lodColoring (not ViewConfigModel.initial.lodColoring) "LoD colouring should toggle"
        }

        test "TC-9.1 ToggleOrientationCube flips the orientation-cube flag" {
            let m = ConfigProperties.update ViewConfigModel.initial ConfigProperties.Action.ToggleOrientationCube
            Expect.equal m.drawOrientationCube (not ViewConfigModel.initial.drawOrientationCube)
                "orientation cube should toggle"
        }

        test "TC-9.1 config edits are independent" {
            // setting the near plane leaves the far plane untouched
            let m = ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SetNearPlane (Numeric.SetValue 0.25))
            Expect.floatClose Accuracy.high m.farPlane.value ViewConfigModel.initial.farPlane.value
                "far plane should be unchanged when only the near plane is set"
        }
    ]
