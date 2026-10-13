namespace ColorLegendDock

open Chiron
open Expecto

open PRo3D
open PRo3D.Base
open PRo3D.Core

/// The render view's color legend dock (docs/ColorLegends.md): tile order, swapping
/// and persistence of the order in the ViewConfigModel.
module Tests =

    let private roundTrip (x : ViewConfigModel) : ViewConfigModel =
        x
        |> Json.serialize
        |> Json.formatWith JsonFormattingOptions.Pretty
        |> Json.parse
        |> Json.deserialize

    let tests () =
        testList "Color legend dock" [

            test "swap exchanges exactly the two legends" {
                let order =
                    ColorLegendKind.all
                    |> ColorLegendKind.swap ColorLegendKind.ColorByCategory ColorLegendKind.ProjectedImage
                Expect.equal order [
                    ColorLegendKind.DipAndStrike
                    ColorLegendKind.ProjectedImage
                    ColorLegendKind.SurfaceScalar
                    ColorLegendKind.SurfaceColorMap
                    ColorLegendKind.ColorByCategory
                    ColorLegendKind.SurfaceComparison
                ] "only the swapped legends move"
            }

            test "swapping twice restores the order" {
                let swap = ColorLegendKind.swap ColorLegendKind.DipAndStrike ColorLegendKind.SurfaceComparison
                Expect.equal (ColorLegendKind.all |> swap |> swap) ColorLegendKind.all "swap is its own inverse"
            }

            test "normalize drops duplicates and appends missing legends" {
                let order =
                    ColorLegendKind.normalize [
                        ColorLegendKind.SurfaceScalar
                        ColorLegendKind.DipAndStrike
                        ColorLegendKind.SurfaceScalar
                    ]
                Expect.equal order [
                    ColorLegendKind.SurfaceScalar
                    ColorLegendKind.DipAndStrike
                    ColorLegendKind.ColorByCategory
                    ColorLegendKind.SurfaceColorMap
                    ColorLegendKind.ProjectedImage
                    ColorLegendKind.SurfaceComparison
                ] "stored order kept, the rest appended in default order"
            }

            test "the swap button's action reorders the config" {
                let m =
                    ConfigProperties.update ViewConfigModel.initial
                        (ConfigProperties.Action.SwapColorLegends (ColorLegendKind.DipAndStrike, ColorLegendKind.ColorByCategory))
                Expect.equal (List.take 2 m.colorLegendOrder)
                    [ ColorLegendKind.ColorByCategory; ColorLegendKind.DipAndStrike ] "first two tiles swapped"
            }

            test "the order round-trips through the scene format" {
                let order = ColorLegendKind.all |> List.rev
                let restored = roundTrip { ViewConfigModel.initial with colorLegendOrder = order }
                Expect.equal restored.colorLegendOrder order "order lost"
            }

            test "a scene without a stored order gets the default order" {
                let json =
                    match Json.serialize { ViewConfigModel.initial with colorLegendOrder = List.rev ColorLegendKind.all } with
                    | Object properties -> Object (Map.remove "colorLegendOrder" properties)
                    | other -> other
                let restored : ViewConfigModel = Json.deserialize json
                Expect.equal restored.colorLegendOrder ColorLegendKind.all "default order expected"
            }

            test "closing a legend is idempotent" {
                let hide = FalseColorLegendApp.SetUseFalseColors false
                let shown = { FalseColorsModel.initDnSLegend with useFalseColors = true }
                let once  = FalseColorLegendApp.update shown hide
                let twice = FalseColorLegendApp.update once hide
                Expect.isFalse once.useFalseColors "legend still shown"
                Expect.isFalse twice.useFalseColors "a second close showed the legend again"
            }
        ]
