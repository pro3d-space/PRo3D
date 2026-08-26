module AdaptiveNestingTests

open System
open Expecto
open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core.Drawing
open PRo3D.Tests

// ---------------------------------------------------------------------------------------------
// Building an adaptive value *inside* another one's evaluation
//
// PackedRendering used to do exactly that: `Drawing.Sg.getPolylinePoints` returns an AVal.custom,
// and linesNoIndirect called it from inside its own AVal.custom and read it immediately. The
// symptom was an edited annotation not redrawing until some unrelated change (drawing the next
// annotation) marked the packed geometry dirty by another route.
//
// Why it happens: in FSharp.Data.Adaptive dependency edges point forwards only and are weak.
// IAdaptiveObject carries `Outputs : IWeakOutputSet` and no Inputs collection, and AVal.custom
// retains only its compute function - not the values that function read. So `source -> inner`
// and `inner -> outer` are both weak, and after the outer's evaluation returns, `inner` is a
// local that nothing holds. Collect it and the whole chain from source to outer is gone; the
// outer is never marked out of date and keeps serving its cached value.
//
// The first three tests are free of PRo3D types so the pattern can be judged on its own.
// ---------------------------------------------------------------------------------------------

/// Builds a fresh AVal.custom on every evaluation and reads it immediately.
let private nestedReader (source : cval<int>) : aval<int> =
    AVal.custom (fun t ->
        let intermediate = AVal.custom (fun t -> source.GetValue t * 10)
        intermediate.GetValue t)

/// AVal.map has the same shape - it also allocates a node per evaluation.
let private mappedReader (source : cval<int>) : aval<int> =
    AVal.custom (fun t ->
        let intermediate = source |> AVal.map (fun x -> x * 10)
        intermediate.GetValue t)

/// The same computation, reading the long-lived cval against the caller's token.
let private directReader (source : cval<int>) : aval<int> =
    AVal.custom (fun t -> source.GetValue t * 10)

let private collect () =
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()

/// evaluate, collect, change the source, evaluate again
let private roundTrip (build : cval<int> -> aval<int>) =
    let source = cval 1
    let out = build source
    let before = AVal.force out
    collect ()
    transact (fun () -> source.Value <- 2)
    before, AVal.force out

/// The hazard needs the intermediate to actually be collected. It always has been here, but a
/// runtime that keeps it alive would make the value correct rather than wrong - so treat that as
/// "not observable", never as a failure.
let private expectStale (after : int) (what : string) =
    if after = 20 then
        skiptestf "%s: the intermediate survived this collection, so the hazard is not observable here" what
    Expect.equal after 10 (sprintf "%s: the outer kept its cached value" what)

let tests () =
    testList "adaptive nesting" [

        test "reading the source directly propagates across a collection" {
            let before, after = roundTrip directReader
            Expect.equal before 10 "initial value"
            Expect.equal after 20 "the change reached the outer computation"
        }

        test "an AVal.custom built inside another's evaluation does not" {
            let before, after = roundTrip nestedReader
            Expect.equal before 10 "initial value"
            expectStale after "AVal.custom inside AVal.custom"
        }

        test "an AVal.map built inside another's evaluation does not" {
            let before, after = roundTrip mappedReader
            Expect.equal before 10 "initial value"
            expectStale after "AVal.map inside AVal.custom"
        }

        // The regression test proper: the real flattening, in the shape linesNoIndirect uses it.
        // This fails with Drawing.Sg.getPolylinePoints and passes with getPolylinePointsAt.
        test "the packed flattening sees a moved control point after a collection" {
            let drawn =
                Draw.drawFull Draw.refSystemFlat Geometry.Polyline false
                    [ V3d.Zero; V3d(1.0, 0.0, 0.0); V3d(2.0, 0.0, 0.0) ]
            let anno = Draw.theAnnotation "adaptive nesting" drawn
            let adaptiveAnno = AdaptiveAnnotation(anno)

            let outer =
                AVal.custom (fun t -> PRo3D.Core.Drawing.Sg.getPolylinePointsAt adaptiveAnno t)

            let before = AVal.force outer
            Expect.equal before.Length 3 "three control points to start with"

            collect ()

            let target = V3d(1.0, 5.0, 0.0)
            let moved = { anno with points = anno.points |> IndexList.setAt 1 target }
            transact (fun () -> adaptiveAnno.Update moved)

            let after = AVal.force outer
            Expect.equal after.[1] target "the moved control point reached the flattening"
        }
    ]
