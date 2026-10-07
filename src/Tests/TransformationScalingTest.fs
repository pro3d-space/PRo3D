namespace TransformationScaling

open System

open Chiron
open Expecto

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

/// The surface scale factor has no upper bound. Scenes store the scaling NumericInput with
/// its bounds, so scenes saved with the old cap of 50 have to lose it on load.
module Tests =

    let private roundTrip (t : Transformations) : Transformations =
        t
        |> Json.serialize
        |> Json.formatWith JsonFormattingOptions.Pretty
        |> Json.parse
        |> Json.deserialize

    let tests () =
        testList "transformation scaling" [

            test "an unbounded scale survives save and load" {
                let t = { Init.transformations with
                            scaling = { Transformations.Initial.scaling with value = 1.0e9 } }
                let back = roundTrip t
                Expect.equal back.scaling.max Double.MaxValue "no upper bound after loading"
                Expect.equal back.scaling.value 1.0e9 "the factor is kept"
            }

            test "a scene saved with the old cap of 50 loads without it" {
                let old = { Init.transformations with
                              scaling = { Transformations.Initial.scaling with value = 42.0; max = 50.0 } }
                let back = roundTrip old
                Expect.equal back.scaling.max Double.MaxValue "the stored cap is replaced"
                Expect.equal back.scaling.value 42.0 "the stored factor is kept"
            }

            // Chiron writes numbers as decimals; these used to throw and abort the save
            for label, v in [ "NaN", nan; "+Infinity", infinity; "-Infinity", -infinity; "Double.MaxValue", Double.MaxValue; "1e29", 1e29 ] do
                test (sprintf "a NumericInput holding %s saves and loads" label) {
                    let t = { Init.transformations with yaw = { Init.transformations.yaw with value = v; max = v } }
                    let back = roundTrip t
                    Expect.isTrue (Double.IsNaN back.yaw.value) "an unrepresentable number loads as NaN"
                }
        ]
