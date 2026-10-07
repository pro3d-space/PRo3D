namespace TransformationScaling

open System

open Chiron
open Expecto

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

/// The surface scale factor is bounded only by 1e15. Scenes store the scaling NumericInput
/// with its bounds, so scenes saved with the old cap of 50 have to lose it on load.
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
                Expect.equal back.scaling.max 1.0e15 "the bound survives save and load"
                Expect.equal back.scaling.value 1.0e9 "the factor is kept"
            }

            test "a scene saved with the old cap of 50 loads without it" {
                let old = { Init.transformations with
                              scaling = { Transformations.Initial.scaling with value = 42.0; max = 50.0 } }
                let back = roundTrip old
                Expect.equal back.scaling.max 1.0e15 "the stored cap is replaced"
                Expect.equal back.scaling.value 42.0 "the stored factor is kept"
            }

            // Chiron writes numbers as decimals; these used to throw and abort the save
            test "a NumericInput holding NaN saves and loads as NaN" {
                let t = { Init.transformations with yaw = { Init.transformations.yaw with value = nan } }
                Expect.isTrue (Double.IsNaN (roundTrip t).yaw.value) "NaN round-trips"
            }

            for label, v, expected in [ "+Infinity", infinity, Json.maxWritableFloat
                                        "-Infinity", -infinity, -Json.maxWritableFloat
                                        "Double.MaxValue", Double.MaxValue, Json.maxWritableFloat
                                        "1e29", 1e29, Json.maxWritableFloat ] do
                test (sprintf "a NumericInput holding %s saves as a finite bound" label) {
                    let t = { Init.transformations with yaw = { Init.transformations.yaw with value = v; max = v } }
                    let back = roundTrip t
                    Expect.equal back.yaw.value expected "saturates to the largest writable magnitude"
                    Expect.equal back.yaw.max expected "a bound stays finite, so older releases can clamp against it"
                }
        ]
