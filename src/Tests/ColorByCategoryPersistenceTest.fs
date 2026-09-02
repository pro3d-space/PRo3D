namespace ColorByCategoryPersistence

open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive

open Chiron
open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

/// The Color by Category settings ride along in the sibling `.pro3d.ann` file via the
/// `Annotations` record, whose version was bumped 0 -> 1 to carry them.
module Tests =

    open System.IO

    let private readAnn (fileName : string) =
        let fullPath = Path.Combine(__SOURCE_DIRECTORY__, "Annotations", fileName)
        DrawingUtilities.IO.loadAnnotationsFromFile fullPath

    let private wrap (cbc : ColorByCategoryModel) : Annotations =
        {
            version         = Annotations.current
            annotations     = GroupsModel.initial
            dnsColorLegend  = FalseColorsModel.initDnSLegend
            colorByCategory = cbc
        }

    let private roundTrip (x : Annotations) : Annotations =
        x
        |> Json.serialize
        |> Json.formatWith JsonFormattingOptions.Pretty
        |> Json.parse
        |> Json.deserialize

    let tests () =

        let polylineKey =
            ColorByCategory.categoryKey ColorCategoryAttribute.AnnotationType "Polyline"

        testList "ColorByCategory persistence" [

            test "settings round-trip through the .ann format" {
                let original =
                    wrap
                        { ColorByCategoryModel.initial with
                            enabled        = true
                            attribute      = ColorCategoryAttribute.DipAngle
                            categoryColors = HashMap.ofList [ polylineKey, ({ c = C4b.Red } : ColorInput) ]
                            noValueColor   = { c = C4b.Blue } }

                let restored = (roundTrip original).colorByCategory

                Expect.isTrue restored.enabled "enabled flag lost"
                Expect.equal restored.attribute ColorCategoryAttribute.DipAngle "attribute lost"
                Expect.equal restored.noValueColor.c C4b.Blue "no-value color lost"
                Expect.equal
                    (restored.categoryColors |> HashMap.tryFind polylineKey |> Option.map (fun c -> c.c))
                    (Some C4b.Red)
                    "explicit category color lost"
            }

            test "fitted ramp bounds round-trip" {
                let range = Range1d(3.0, 42.0)
                let original =
                    wrap
                        { ColorByCategoryModel.initial with
                            attribute     = ColorCategoryAttribute.Area
                            numericLegend =
                                { ColorByCategoryModel.initial.numericLegend with
                                    lowerBound = FalseColorsModel.initlb range
                                    upperBound = FalseColorsModel.initub range } }

                let restored = (roundTrip original).colorByCategory

                Expect.floatClose Accuracy.high restored.numericLegend.lowerBound.value 3.0 "lower bound lost"
                Expect.floatClose Accuracy.high restored.numericLegend.upperBound.value 42.0 "upper bound lost"
            }

            // annotation_1.ann is a version 0 file, so this is the regression guard for
            // readV0 after the Annotations version bump
            test "a version 0 .ann file loads with the panel disabled" {
                let annotations = readAnn "annotation_1.ann"
                let cbc = annotations.colorByCategory

                Expect.isFalse cbc.enabled "v0 file should leave the panel disabled"
                Expect.equal cbc.attribute ColorByCategoryModel.initial.attribute "v0 file should use the default attribute"
                Expect.isTrue (HashMap.isEmpty cbc.categoryColors) "v0 file should have no category color overrides"
            }
        ]
