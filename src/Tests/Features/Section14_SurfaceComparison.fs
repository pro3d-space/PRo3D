/// Section 14 — Surface Comparison
///   TC-14.1 (Activate Mode / choose surfaces), TC-14.2 (Select Area),
///   TC-14.3 (Surface Measurements — configuration)
///
///   Exercises the real ComparisonApp.update for surface selection and the area
///   picking that TC-14.2 describes. The statistics computation (and the bookmark-
///   reference length feature, TC-14.4) require real OPC surface data and are not
///   covered here.
module PRo3D.Tests.Section14_SurfaceComparison

open Aardvark.Base
open Aardvark.UI.Primitives              // Numeric

open FSharp.Data.Adaptive

open Expecto

open PRo3D                                // ComparisonApp (module)
open PRo3D.Core                           // SurfaceModel, ReferenceSystem
open PRo3D.Comparison                     // ComparisonAction, DistanceMode
open PRo3D.Tests

module private C =
    /// ComparisonApp.update with empty surfaces/annotations/bookmarks — enough for
    /// the surface-selection and area-picking actions, which do not read them.
    let run m (msg : ComparisonAction) =
        ComparisonApp.update m SurfaceModel.initial ReferenceSystem.initial HashMap.empty HashMap.empty msg
        |> fst

let tests =
    testList "Section 14 — Surface Comparison" [

        // TC-14.1 Activate Mode — choosing the two surfaces to compare

        test "TC-14.1 selecting the first comparison surface" {
            let m = C.run ComparisonApp.init (ComparisonAction.SelectSurface1 "surfaceA")
            Expect.equal m.surface1 (Some "surfaceA") "surface1 should be the chosen surface"
        }

        test "TC-14.1 selecting the second comparison surface" {
            let m = C.run ComparisonApp.init (ComparisonAction.SelectSurface2 "surfaceB")
            Expect.equal m.surface2 (Some "surfaceB") "surface2 should be the chosen surface"
        }

        // TC-14.2 Select Area

        test "TC-14.2 AddSelectionArea creates and selects an area" {
            let m = C.run ComparisonApp.init (ComparisonAction.AddSelectionArea (V3d(1.0, 2.0, 3.0)))
            Expect.equal (m.areas |> HashMap.count) 1 "one area should be created"
            Expect.isSome m.selectedArea "the new area should be selected"
            Expect.isTrue m.isEditingArea "the new area should be in editing mode"
        }

        test "TC-14.2 the created area sits at the clicked location" {
            let m = C.run ComparisonApp.init (ComparisonAction.AddSelectionArea (V3d(1.0, 2.0, 3.0)))
            let area = m.areas |> HashMap.toList |> List.head |> snd
            Expect.equal area.location (V3d(1.0, 2.0, 3.0)) "the area should be centred on the clicked point"
        }

        test "TC-14.2 DeselectArea clears the selection" {
            let withArea = C.run ComparisonApp.init (ComparisonAction.AddSelectionArea (V3d(1.0, 2.0, 3.0)))
            let m = C.run withArea ComparisonAction.DeselectArea
            Expect.isNone m.selectedArea "no area should be selected after deselect"
            Expect.isFalse m.isEditingArea "editing should stop after deselect"
        }

        test "TC-14.2 RemoveArea deletes the area" {
            let withArea = C.run ComparisonApp.init (ComparisonAction.AddSelectionArea (V3d(1.0, 2.0, 3.0)))
            let id = withArea.areas |> HashMap.toList |> List.head |> fst
            let m = C.run withArea (ComparisonAction.RemoveArea id)
            Expect.equal (m.areas |> HashMap.count) 0 "the area should be removed"
        }

        // TC-14.3 Surface Measurements — the measurement configuration; the actual
        //   statistics need real OPC surface data and are out of scope here.

        test "TC-14.3 SetDistanceMode switches the distance mode" {
            let m = C.run ComparisonApp.init (ComparisonAction.SetDistanceMode DistanceMode.SurfaceNormal)
            Expect.equal m.surfaceGeometryType DistanceMode.SurfaceNormal "the distance mode should switch"
        }

        test "TC-14.3 UpdateDefaultAreaSize changes the default area radius" {
            let m = C.run ComparisonApp.init (ComparisonAction.UpdateDefaultAreaSize (Numeric.SetValue 4.0))
            Expect.floatClose Accuracy.high m.initialAreaSize.value 4.0 "the default area size should be 4"
        }
    ]
