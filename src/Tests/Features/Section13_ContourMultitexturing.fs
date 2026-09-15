/// Section 13 — Contour Lines and Multitexturing
///   TC-13.1 (Contour Lines), TC-13.2 (Multitexturing), TC-13.3 (Cross Sections)
///
///   Contour lines go through ContourLineApp.update, multitexturing through
///   SurfaceProperties.update, cross sections through CrossSectionApp.update.
module PRo3D.Tests.Section13_ContourMultitexturing

open Aardvark.Base
open Aardvark.UI.Primitives              // Numeric, ColorPicker

open Expecto

open PRo3D.Base                          // TextureCombiner
open PRo3D.Core
open PRo3D.Core.Surface                  // SurfaceProperties
open PRo3D.Tests

// TC-13.1 Contour Lines
let private contourTests =
    testList "Contour lines" [

        test "TC-13.1 contour lines start disabled" {
            Expect.isFalse ContourLineModel.initial.enabled "contours should be off by default"
        }

        test "TC-13.1 ToggleEnabled turns contour lines on" {
            let m = ContourLineApp.update ContourLineModel.initial ContourLineApp.Action.ToggleEnabled
            Expect.isTrue m.enabled "contour lines should be enabled after toggling"
        }

        test "TC-13.1 SetDistance changes the contour spacing" {
            let m = ContourLineApp.update ContourLineModel.initial (ContourLineApp.Action.SetDistance (Numeric.SetValue 25.0))
            Expect.floatClose Accuracy.high m.distance.value 25.0 "contour spacing should be 25"
        }

        test "TC-13.1 SetLineWidth changes the contour line width" {
            let m = ContourLineApp.update ContourLineModel.initial (ContourLineApp.Action.SetLineWidth (Numeric.SetValue 3.0))
            Expect.floatClose Accuracy.high m.width.value 3.0 "contour line width should be 3"
        }
    ]

// TC-13.4 LatLon Shader (graticule overlay)
let private latLonTests =
    testList "LatLon shader" [

        test "TC-13.4 the graticule starts disabled" {
            Expect.isFalse LatLonShaderModel.initial.enabled "the LatLon shader should be off by default"
        }

        test "TC-13.4 ToggleEnabled turns the graticule on" {
            let m = LatLonShaderApp.update LatLonShaderModel.initial LatLonShaderApp.Action.ToggleEnabled
            Expect.isTrue m.enabled "the graticule should be enabled after toggling"
        }

        test "TC-13.4 ToggleLat flips the parallel granularity it names" {
            let m = LatLonShaderApp.update LatLonShaderModel.initial (LatLonShaderApp.Action.ToggleLat 1)
            Expect.isTrue m.lat1 "the 1° parallels should be on after toggling"
            let m2 = LatLonShaderApp.update m (LatLonShaderApp.Action.ToggleLat 15)
            Expect.isFalse m2.lat15 "the 15° parallels should be off after toggling (on by default)"
            Expect.isTrue m2.lat1 "toggling 15° must not touch the 1° flag"
        }

        test "TC-13.4 ToggleLon flips the meridian granularity it names" {
            let m = LatLonShaderApp.update LatLonShaderModel.initial (LatLonShaderApp.Action.ToggleLon 5)
            Expect.isFalse m.lon5 "the 5° meridians should be off after toggling (on by default)"
            Expect.equal m.lon1 LatLonShaderModel.initial.lon1 "toggling 5° must not touch the 1° flag"
        }

        test "TC-13.4 ToggleLat ignores an unknown granularity" {
            let m = LatLonShaderApp.update LatLonShaderModel.initial (LatLonShaderApp.Action.ToggleLat 7)
            Expect.equal m LatLonShaderModel.initial "7° is not an offered level, model unchanged"
        }

        test "TC-13.4 the offered levels are 1, 5 and 15" {
            Expect.equal LatLonShaderApp.levels [ 1; 5; 15 ] "the graticule granularities are 1°, 5° and 15°"
        }

        test "TC-13.4 SetLineColor changes the line colour" {
            let m = LatLonShaderApp.update LatLonShaderModel.initial (LatLonShaderApp.Action.SetLineColor (ColorPicker.Action.SetColor C4b.Red))
            Expect.equal m.lineColor.c C4b.Red "the line colour should be red"
        }
    ]

// TC-13.2 Multitexturing
let private multiTextureTests =
    testList "Multitexturing" [

        test "TC-13.2 SetTextureCombiner selects the blend combiner" {
            let s = SurfaceProperties.update (makeSurface "s") (SurfaceProperties.Action.SetTextureCombiner TextureCombiner.Blend)
            Expect.equal s.transferFunction.textureCombiner TextureCombiner.Blend "the texture combiner should be Blend"
        }

        test "TC-13.2 SetBlendFactor sets the blend factor" {
            let s = SurfaceProperties.update (makeSurface "s") (SurfaceProperties.Action.SetBlendFactor 0.5)
            Expect.floatClose Accuracy.high s.transferFunction.blendFactor 0.5 "the blend factor should be 0.5"
        }

        test "TC-13.2 SetSecondaryTextureChannel selects the channel" {
            let s = SurfaceProperties.update (makeSurface "s") (SurfaceProperties.Action.SetSecondaryTextureChannel (Some 1))
            Expect.equal s.secondaryTextureLayer (Some 1) "the secondary texture channel should be 1"
        }
    ]

// TC-13.3 Cross Sections
let private crossSectionTests =
    testList "Cross sections" [

        test "TC-13.3 the curtain starts disabled" {
            Expect.isFalse CrossSectionModel.initial.curtainEnabled "the curtain should be off by default"
        }

        test "TC-13.3 ToggleCurtainEnabled turns the curtain on" {
            let m = CrossSectionApp.update CrossSectionModel.initial CrossSectionAction.ToggleCurtainEnabled
            Expect.isTrue m.curtainEnabled "the curtain should be enabled after toggling"
        }

        test "TC-13.3 ToggleClippingEnabled flips clipping" {
            let m = CrossSectionApp.update CrossSectionModel.initial CrossSectionAction.ToggleClippingEnabled
            Expect.equal m.clippingEnabled (not CrossSectionModel.initial.clippingEnabled) "clipping should toggle"
        }

        test "TC-13.3 SetCurtainExtrusionDepth sets the depth" {
            let m = CrossSectionApp.update CrossSectionModel.initial (CrossSectionAction.SetCurtainExtrusionDepth (Numeric.SetValue 7.0))
            Expect.floatClose Accuracy.high m.curtainExtrusionDepth.value 7.0 "the extrusion depth should be 7"
        }

        test "TC-13.3 ChangeCurtainBaseColor sets the curtain colour" {
            let m = CrossSectionApp.update CrossSectionModel.initial
                        (CrossSectionAction.ChangeCurtainBaseColor (ColorPicker.Action.SetColor C4b.Red))
            Expect.equal m.curtainBaseColor.c C4b.Red "the curtain base colour should be red"
        }
    ]

let tests =
    testList "Section 13 — Contour Lines and Multitexturing" [
        contourTests
        latLonTests
        multiTextureTests
        crossSectionTests
    ]
