/// Double-click finishes open-ended annotations and applies the cut stroke (#824,
/// docs/DoubleClickFinish.md). The browser sends click, click, dblclick: both clicks are
/// AddPointAdv / AddCutStrokePoint, the dblclick is FinishOnDoubleClick /
/// ApplyCutStrokeOnDoubleClick. Driven through the real DrawingApp.update, as the viewer does.
module PRo3D.Tests.DoubleClickFinishTests

open Aardvark.Base
open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Tests

/// UserPreferences as older releases define it - no double-click field
type PreferencesBefore824 = { mapInvertForward : bool; mapInvertStrafe : bool }

let private rsys = Draw.refSystemFlat
let private run  = Draw.run rsys

/// the viewer's screen-space test, reduced to "the same point"
let private sameSpot : V3d -> V3d -> bool = fun a b -> Vec.distance a b < 1e-9
let private dblClick = DrawingAction.FinishOnDoubleClick (Some sameSpot)

let private clicks (points : List<V3d>) (m : DrawingModel) =
    points |> List.fold (fun m p -> Draw.click rsys p m) m

let private start (geometry : Geometry) = Draw.startTool rsys geometry

let private workingCount (m : DrawingModel) = m.working |> Option.map (fun w -> w.points.Count)

let private a = V3d(0.0, 0.0, 0.0)
let private b = V3d(4.0, 0.0, 0.0)
let private c = V3d(4.0, 3.0, 0.0)

let private square = [ V3d(0.,0.,0.); V3d(10.,0.,0.); V3d(10.,10.,0.); V3d(0.,10.,0.) ]

/// a square polygon, selected - finishAndAppend single-selects what it adds
let private drawSquare () =
    run (start Geometry.Polygon |> clicks square) DrawingAction.Finish

let tests =
    testList "Double-click finish" [

        test "a polyline finishes on double-click without the second click's duplicate point" {
            // click a, click b, double-click at c = click c, click c, dblclick
            let m   = start Geometry.Polyline |> clicks [ a; b; c; c ]
            let ann = run m dblClick |> Draw.theAnnotation "polyline"
            Expect.equal (ann.points |> IndexList.toList) [ a; b; c ] "the duplicate is gone, nothing else"
            Expect.isSome ann.results "measurements are computed as on Enter"
        }

        test "nothing is dropped when the second click did not add a point" {
            // the second click missed the surface: no duplicate to remove
            let m   = start Geometry.Polyline |> clicks [ a; b; c ]
            let ann = run m dblClick |> Draw.theAnnotation "polyline"
            Expect.equal (ann.points |> IndexList.toList) [ a; b; c ] "every real point is kept"
        }

        test "without the screen-space test nothing is dropped" {
            let m   = start Geometry.Polyline |> clicks [ a; b; c; c ]
            let ann = run m (DrawingAction.FinishOnDoubleClick None) |> Draw.theAnnotation "polyline"
            Expect.equal (ann.points |> IndexList.count) 4 "an unknown test must never delete a point"
        }

        test "a polygon finishes closed" {
            let m   = start Geometry.Polygon |> clicks [ a; b; c; c ]
            let ann = run m dblClick |> Draw.theAnnotation "polygon"
            Expect.equal (ann.points |> IndexList.toList) [ a; b; c; a ] "three vertices, ring closed back to the first"
        }

        test "dip and strike finishes with a fitted plane" {
            let m   = start Geometry.DnS |> clicks [ a; b; c; c ]
            let ann = run m dblClick |> Draw.theAnnotation "dns"
            Expect.equal (ann.points |> IndexList.count) 3 "three points"
            Expect.isSome ann.dnsResults "the plane is fitted as on Enter"
        }

        test "too few points: the duplicate goes, drawing continues" {
            let polygon = run (start Geometry.Polygon |> clicks [ a; b; b ]) dblClick
            Expect.isEmpty (Draw.annotations polygon) "a two-point polygon is not finished"
            Expect.equal (workingCount polygon) (Some 2) "the polygon keeps its two real points"

            let polyline = run (start Geometry.Polyline |> clicks [ a; a ]) dblClick
            Expect.isEmpty (Draw.annotations polyline) "a one-point polyline is not finished"
            Expect.equal (workingCount polyline) (Some 1) "the polyline keeps its first point"

            let dns = run (start Geometry.DnS |> clicks [ a; b; b ]) dblClick
            Expect.isEmpty (Draw.annotations dns) "two points fit no plane"
        }

        test "drawing continues normally after a too-early double-click" {
            let m   = run (start Geometry.Polyline |> clicks [ a; a ]) dblClick
            let ann = run (m |> clicks [ b; c; c ]) dblClick |> Draw.theAnnotation "polyline"
            Expect.equal (ann.points |> IndexList.toList) [ a; b; c ] "the kept point is the first vertex"
        }

        test "a projected polyline loses the duplicate's segment too" {
            let m = start Geometry.Polyline
            let m = run m (DrawingAction.SetProjection Projection.Viewpoint)
            let m = m |> clicks [ a; b; c; c ]
            Expect.equal (m.working |> Option.map (fun w -> w.segments.Count)) (Some 3) "one segment per consecutive pair"
            let ann = run m dblClick |> Draw.theAnnotation "projected polyline"
            Expect.equal (ann.points |> IndexList.count) 3 "three points"
            Expect.equal (ann.segments |> IndexList.count) 2 "two segments, the zero-length one gone"
            Expect.equal (ann.segments |> IndexList.toList |> List.map (fun s -> s.startPoint, s.endPoint)) [ a, b; b, c ]
                "the remaining segments connect the remaining points"
        }

        test "undo removes a double-click-finished annotation in one step" {
            let finished = run (start Geometry.Polyline |> clicks [ a; b; b ]) dblClick
            Expect.equal (Draw.annotations finished |> List.length) 1 "finished"
            Expect.isEmpty (Draw.annotations (run finished DrawingAction.Undo)) "one undo takes it back"
        }

        test "fixed-count geometries ignore the double-click" {
            // Point: each click is an annotation; the dblclick finds nothing to finish
            let points = run (start Geometry.Point |> clicks [ a; a ]) dblClick
            Expect.equal (Draw.annotations points |> List.length) 2 "the two clicks, nothing more"
            Expect.isNone points.working "no working annotation"

            // Line: the first click of the double-click completes the line, the second starts a
            // new one - exactly what two clicks do without the dblclick
            let lines = start Geometry.Line |> clicks [ a; b; b ]
            let after = run lines dblClick
            Expect.equal (Draw.annotations after |> List.length) 1 "one line"
            Expect.equal (workingCount after) (workingCount lines) "the dblclick changed nothing"
        }

        test "a double-click with nothing drawn does nothing" {
            let m = start Geometry.Polyline
            let after = run m dblClick
            Expect.isEmpty (Draw.annotations after) "nothing drawn"
            Expect.isNone after.working "nothing started"
        }

        // --- cut ----------------------------------------------------------------------------

        test "a double-click applies the cut stroke, without the duplicate stroke point" {
            let m = drawSquare ()
            let m = run m (DrawingAction.AddCutStrokePoint (V3d(-5.,5.,0.)))
            let m = run m (DrawingAction.AddCutStrokePoint (V3d(15.,5.,0.)))
            let m = run m (DrawingAction.AddCutStrokePoint (V3d(15.,5.,0.)))
            let after = run m (DrawingAction.ApplyCutStrokeOnDoubleClick (Some sameSpot, Some Draw.identityHit))
            Expect.isNone after.cutStroke "the stroke is consumed by the cut"
            let pieces = Draw.annotations after
            Expect.equal pieces.Length 2 "two pieces"
            let total = pieces |> List.sumBy (fun p -> Calculations.calculatePolygonArea p.points)
            Expect.floatClose Accuracy.medium total 100.0 "areas sum to the original"
        }

        test "a double-click on a one-point stroke cuts nothing and keeps the point" {
            let m = drawSquare ()
            let m = run m (DrawingAction.AddCutStrokePoint (V3d(-5.,5.,0.)))
            let m = run m (DrawingAction.AddCutStrokePoint (V3d(-5.,5.,0.)))
            let after = run m (DrawingAction.ApplyCutStrokeOnDoubleClick (Some sameSpot, Some Draw.identityHit))
            Expect.equal (Draw.annotations after |> List.length) 1 "the square is untouched"
            Expect.equal (after.cutStroke |> Option.map (fun s -> s.points.Count)) (Some 1) "the real stroke point stays"
        }

        // --- the preference (per computer, userPreferences.json) --------------------------------

        test "a preferences file from before the switch reads as double-click on" {
            let old = """{ "mapInvertForward": false, "mapInvertStrafe": true }"""
            let prefs = Newtonsoft.Json.JsonConvert.DeserializeObject<PRo3D.UserPreferences>(old)
            Expect.isFalse prefs.disableDoubleClickFinish "a missing field must mean on, the default"
            Expect.isTrue prefs.mapInvertStrafe "the existing settings survive"
        }

        test "older releases still read a preferences file that has the switch" {
            let json = Newtonsoft.Json.JsonConvert.SerializeObject { PRo3D.UserPreferences.initial with disableDoubleClickFinish = true; mapInvertForward = true }
            let old = Newtonsoft.Json.JsonConvert.DeserializeObject<PreferencesBefore824>(json)
            Expect.isTrue old.mapInvertForward "the fields an older release knows are read as before"
            let back = Newtonsoft.Json.JsonConvert.DeserializeObject<PRo3D.UserPreferences>(json)
            Expect.isTrue back.disableDoubleClickFinish "and the switch round-trips"
        }

        // --- the helpers on their own -------------------------------------------------------

        test "dropCoincidentLastPoint only ever drops the last point" {
            let ann = { Annotation.make Projection.Linear None Geometry.Polyline None { c = C4b.White } Annotation.Initial.thickness "" with
                          points = IndexList.ofList [ a; b; b ] }
            let dropped = DrawingApp.dropCoincidentLastPoint sameSpot ann
            Expect.equal (dropped.points |> IndexList.toList) [ a; b ] "the duplicate goes"
            let again = DrawingApp.dropCoincidentLastPoint sameSpot dropped
            Expect.equal (again.points |> IndexList.toList) [ a; b ] "distinct points stay"
            let single = { ann with points = IndexList.single a }
            Expect.equal (DrawingApp.dropCoincidentLastPoint (fun _ _ -> true) single).points single.points
                "a single point is never dropped"
        }
    ]
