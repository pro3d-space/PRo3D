/// Section 22 — Supporting profiles (#833, docs/SupportingProfiles.md)
///   Profiles generated from a polyline (perpendicular, every d metres) and from an ellipse
///   (4 through the centre, rim to rim), the ProfileLink they carry, and the Craters export.
///
///   The surface is the plane z = 0 (or a sampler with a hole in it), so the geometry is
///   exact and needs no OPC; the Playwright spec supporting-profiles.spec.ts covers Dimorphos.
module PRo3D.Tests.Section22_SupportingProfiles

open System

open Aardvark.Base
open Aardvark.UI.Primitives

open FSharp.Data.Adaptive
open Chiron

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Tests

let private up    = V3d.OOI
let private north = V3d.OIO

/// drops a point straight down onto z = 0
let private flat : V3d -> Option<V3d> = fun p -> Some (V3d(p.X, p.Y, 0.0))

let private ok label (r : Result<'a, string>) =
    match r with
    | Result.Ok v    -> v
    | Result.Error e -> failtestf "%s: unexpected refusal: %s" label e

let private polyline (text : string) (points : list<V3d>) =
    { Annotation.initial with
        key        = Guid.NewGuid()
        text       = text
        geometry   = Geometry.Polyline
        projection = Projection.Sky
        points     = IndexList.ofList points }

/// 6 m by 2 m ellipse around (100, 50, 0), long axis toward north-east, with a 64-point
/// outline on the plane. `bulge` pushes the outline out on the minor axis's positive side,
/// as the two different widths of a four-point ellipse do.
let private ellipse (text : string) (bulge : float) =
    let center = V3d(100.0, 50.0, 0.0)
    let major  = V3d(1.0, 1.0, 0.0).Normalized * 3.0
    let minor  = V3d(1.0, -1.0, 0.0).Normalized
    let outline =
        [ for i in 0 .. 63 do
            let t = Constant.PiTimesTwo * float i / 64.0
            let s = sin t
            let s = if s > 0.0 then s * bulge else s
            yield center + major * cos t + minor * s ]
    { Annotation.initial with
        key             = Guid.NewGuid()
        text            = text
        geometry        = Geometry.AxisEllipse
        projection      = Projection.Sky
        points          = IndexList.ofList outline
        ellipticResults = Some (EllipticAnnotations.Measures.ofAxes up north center major minor) }

/// horizontal angle between two directions, degrees, 0..90 (axial)
let private axialAngle (a : V3d) (b : V3d) =
    let a = V3d(a.X, a.Y, 0.0).Normalized
    let b = V3d(b.X, b.Y, 0.0).Normalized
    acos (abs (Vec.dot a b) |> min 1.0) * Constant.DegreesPerRadian

/// `parent` as the only annotation, single-selected, as the panel sees it
let private withSelected (parent : Annotation) =
    let groups = DrawingModel.initialdrawing.annotations |> GroupsApp.addLeafToActiveGroup (Leaf.Annotations parent) false
    let m = { DrawingModel.initialdrawing with annotations = groups }
    Draw.run Draw.refSystemFlat m (DrawingAction.GroupsMessage (GroupsAppAction.SingleSelectLeaf([], parent.key, parent.text)))

let private generatorTests =
    testList "generator" [

        test "a bearing a hair off north is written as 0" {
            Expect.equal (SupportingProfiles.azimuth up north (V3d(1e-17, 1.0, 0.0))) 0.0 "a hair east"
            Expect.equal (SupportingProfiles.azimuth up north (V3d(-1e-17, 1.0, 0.0))) 0.0 "a hair west"
            Expect.floatClose Accuracy.high (SupportingProfiles.azimuth up north V3d.IOO) 90.0 "east"
        }

        test "stations every d along the line, and the end itself" {
            Expect.equal (SupportingProfiles.stations 10.0 25.0) [| 0.0; 10.0; 20.0; 25.0 |] "25 m, d = 10: the end gets its own"
            Expect.equal (SupportingProfiles.stations 10.0 30.0) [| 0.0; 10.0; 20.0; 30.0 |] "30 m, d = 10: the last multiple is the end"
            Expect.isEmpty (SupportingProfiles.stations 0.0 30.0) "no spacing, no stations"
        }

        test "spacing mode: start and end switch off the profiles at 0 and at the end" {
            let st a b = SupportingProfiles.stationsFor (SupportingProfiles.Spacing 10.0) a b 25.0 |> ok "spacing"
            Expect.equal (st true true)   [| 0.0; 10.0; 20.0; 25.0 |] "both"
            Expect.equal (st false true)  [| 10.0; 20.0; 25.0 |] "no start"
            Expect.equal (st true false)  [| 0.0; 10.0; 20.0 |] "no end"
            Expect.equal (st false false) [| 10.0; 20.0 |] "neither"
            // the end is a multiple of the spacing: the profile at 30 is the end one
            let st30 = SupportingProfiles.stationsFor (SupportingProfiles.Spacing 10.0) true false 30.0 |> ok "30"
            Expect.equal st30 [| 0.0; 10.0; 20.0 |] "no end at 30"
        }

        test "count mode: N profiles over the whole polyline, gaps equal, excluded ends count as gaps" {
            let st a b n = SupportingProfiles.stationsFor (SupportingProfiles.Count n) a b 30.0 |> ok "count"
            Expect.equal (st true true 4)   [| 0.0; 10.0; 20.0; 30.0 |] "both ends: 3 gaps"
            Expect.equal (st true false 3)  [| 0.0; 10.0; 20.0 |] "start only: 3 gaps"
            Expect.equal (st false true 3)  [| 10.0; 20.0; 30.0 |] "end only: 3 gaps"
            Expect.equal (st false false 2) [| 10.0; 20.0 |] "neither: 3 gaps"
            Expect.equal (st false false 1) [| 15.0 |] "one profile, no ends: the middle"
            Expect.equal (st true false 1)  [| 0.0 |] "one profile at the start"
            Expect.isError (SupportingProfiles.stationsFor (SupportingProfiles.Count 1) true true 30.0) "one profile cannot sit on both ends"
            Expect.isError (SupportingProfiles.stationsFor (SupportingProfiles.Count 0) false false 30.0) "zero profiles is refused"
        }

        test "more than MaxProfiles at once is refused" {
            Expect.isError (SupportingProfiles.stationsFor (SupportingProfiles.Spacing 0.01) true true 30.0) "3001 profiles by spacing"
            Expect.isError (SupportingProfiles.stationsFor (SupportingProfiles.Count 1001) true true 30.0) "1001 by count"
            Expect.isOk (SupportingProfiles.stationsFor (SupportingProfiles.Count 1000) true true 30.0) "1000 is fine"
        }

        test "a point clicked twice is skipped, not a division by zero" {
            // the second point repeats the first: a zero-length first segment
            let parent = polyline "dup" [ V3d(0.0, 0.0, 0.0); V3d(0.0, 0.0, 0.0); V3d(20.0, 0.0, 0.0) ]
            let specs = SupportingProfiles.forPolyline Planet.None up north flat (SupportingProfiles.Spacing 10.0) true true 4.0 parent |> ok "dup"
            Expect.equal specs.Length 3 "0, 10, 20"
            for s in specs do
                Expect.isFalse (s.start.AnyNaN || s.stop.AnyNaN) "finite ends"
                Expect.floatClose Accuracy.medium (Vec.distance s.start s.stop) 4.0 "full length"
        }

        test "a setting that leaves no profile is refused, not an empty success" {
            let parent = polyline "short" [ V3d(0.0, 0.0, 0.0); V3d(5.0, 0.0, 0.0) ]
            match SupportingProfiles.forPolyline Planet.None up north flat (SupportingProfiles.Spacing 10.0) false false 2.0 parent with
            | Result.Ok specs -> failtestf "expected a refusal, got %d profiles" specs.Length
            | Result.Error e  -> Expect.stringContains e "no profile is left" "says why"
        }

        test "count mode through the drawing app: equal gaps over the whole polyline" {
            // 30 m with a bend at 10 m; 4 profiles without the end: stations 0, 7.5, 15, 22.5
            let parent = polyline "lin" [ V3d(0.0, 0.0, 0.0); V3d(10.0, 0.0, 0.0); V3d(10.0, 20.0, 0.0) ]
            let m = withSelected parent
            let m = Draw.run Draw.refSystemFlat m (DrawingAction.SetLineamentMode LineamentProfileMode.ByCount)
            let m = Draw.run Draw.refSystemFlat m (DrawingAction.SetProfileCount (Numeric.SetValue 4.0))
            let m = Draw.run Draw.refSystemFlat m (DrawingAction.SetProfileAtEnd false)
            let m = Draw.run Draw.refSystemFlat m (DrawingAction.GenerateSupportingProfiles (Some flat))
            let stations =
                Draw.annotations m
                |> List.choose (fun a -> a.profileOf |> Option.bind (fun l -> l.station))
                |> List.sort
            Expect.equal stations [ 0.0; 7.5; 15.0; 22.5 ] "4 profiles, end excluded"
        }

        test "polyline profiles cross their segment at a right angle, start on the left, and carry on through a bend" {
            // east 20 m, then north 20 m
            let parent = polyline "trench" [ V3d(0.0, 0.0, 0.0); V3d(20.0, 0.0, 0.0); V3d(20.0, 20.0, 0.0) ]
            let specs = SupportingProfiles.forPolyline Planet.None up north flat (SupportingProfiles.Spacing 10.0) true true 4.0 parent |> ok "polyline"
            Expect.equal specs.Length 5 "stations 0, 10, 20, 30, 40"
            Expect.equal (specs |> List.map (fun s -> s.link.station)) [ Some 0.0; Some 10.0; Some 20.0; Some 30.0; Some 40.0 ] "stations along the 3D length"
            Expect.equal (specs |> List.map (fun s -> s.link.index)) [ 0 .. 4 ] "indices in order"

            let at10 = specs.[1]
            Expect.floatClose Accuracy.high (Vec.distance at10.start (V3d(10.0, 2.0, 0.0))) 0.0 "left of an eastward line is north"
            Expect.floatClose Accuracy.high (Vec.distance at10.stop (V3d(10.0, -2.0, 0.0))) 0.0 "and it is 4 m long, centred"
            Expect.floatClose Accuracy.high at10.link.azimuth 0.0 "centre to start points north"

            // 20 m is the vertex: the segment leaving it runs north, so the profile runs east-west
            let atVertex = specs.[2]
            Expect.floatClose Accuracy.high (Vec.distance atVertex.start (V3d(18.0, 0.0, 0.0))) 0.0 "at the vertex: perpendicular to the outgoing segment"
            Expect.floatClose Accuracy.high atVertex.link.azimuth 270.0 "left of a northward line is west"

            let at30 = specs.[3]
            Expect.floatClose Accuracy.high (Vec.distance at30.start (V3d(18.0, 10.0, 0.0))) 0.0 "the distance carries on through the bend"
            for s in specs do
                Expect.equal s.link.direction "" "polyline profiles have no compass label"
                Expect.equal s.link.parent parent.key "linked to the polyline"
        }

        test "every polyline profile is perpendicular to its segment" {
            let parent = polyline "zigzag" [ V3d(0.0, 0.0, 0.0); V3d(7.0, 3.0, 0.0); V3d(9.0, 12.0, 0.0); V3d(-4.0, 15.0, 0.0) ]
            let specs = SupportingProfiles.forPolyline Planet.None up north flat (SupportingProfiles.Spacing 1.5) true true 3.0 parent |> ok "zigzag"
            let pts = parent.points |> IndexList.toArray
            let cumulative = Array.scan (+) 0.0 [| for i in 1 .. pts.Length - 1 -> Vec.distance pts.[i - 1] pts.[i] |]
            for s in specs do
                let station = s.link.station |> Option.defaultValue nan
                let j = [| 0 .. pts.Length - 2 |] |> Array.filter (fun j -> cumulative.[j] <= station) |> Array.last
                let angle = axialAngle (s.stop - s.start) (pts.[j + 1] - pts.[j])
                Expect.floatClose Accuracy.medium angle 90.0 (sprintf "profile %d at %.2f m" s.link.index station)
                Expect.floatClose Accuracy.medium (Vec.distance s.start s.stop) 3.0 "length L"
        }

        test "one end off the surface refuses the whole polyline" {
            let parent = polyline "edge" [ V3d(0.0, 0.0, 0.0); V3d(30.0, 0.0, 0.0) ]
            let holey : V3d -> Option<V3d> = fun p -> if p.X > 15.0 && p.Y > 0.0 then None else flat p
            match SupportingProfiles.forPolyline Planet.None up north holey (SupportingProfiles.Spacing 10.0) true true 4.0 parent with
            | Result.Ok specs -> failtestf "expected a refusal, got %d profiles" specs.Length
            | Result.Error e  -> Expect.stringContains e "misses the surface" "says why"
        }

        test "compass profiles of an ellipse: N-S, NE-SW, E-W, NW-SE, rim to rim through the centre" {
            let parent = ellipse "craterA" 1.0
            let specs = SupportingProfiles.forEllipse Planet.ENU up north EllipseProfileMode.Compass parent |> ok "compass"
            Expect.equal (specs |> List.map (fun s -> s.link.direction)) [ "N-S"; "NE-SW"; "E-W"; "NW-SE" ] "labels"
            let azimuths = specs |> List.map (fun s -> s.link.azimuth)
            // exact: written into the CSV as is, so 1e-16 instead of 0 would show
            Expect.equal azimuths [ 0.0; 45.0; 90.0; 315.0 ] "bearing from the centre to the start end"
            let c = V3d(100.0, 50.0, 0.0)
            let ns = specs.[0]
            Expect.isGreaterThan ns.start.Y c.Y "N-S starts at the north rim"
            Expect.floatClose Accuracy.medium ns.start.X c.X "due north of the centre"
            Expect.floatClose Accuracy.medium ns.stop.X c.X "and ends due south"
            // the ends sit on the outline: the 6 x 2 ellipse equation within the
            // chord error of a 64-point outline
            let major = V3d(1.0, 1.0, 0.0).Normalized
            let minor = V3d(1.0, -1.0, 0.0).Normalized
            for s in specs do
                for p in [ s.start; s.stop ] do
                    let u = Vec.dot (p - c) major / 3.0
                    let v = Vec.dot (p - c) minor / 1.0
                    Expect.floatClose { absolute = 0.01; relative = 0.0 } (u * u + v * v) 1.0 (sprintf "%s end on the rim" s.link.direction)
                // a straight profile through a symmetric ellipse is centred on it
                Expect.floatClose { absolute = 1e-6; relative = 0.0 } (Vec.distance ((s.start + s.stop) * 0.5) c) 0.0 (sprintf "%s through the centre" s.link.direction)
            Expect.isTrue (specs |> List.forall (fun s -> s.link.station.IsNone)) "ellipse profiles have no station"
        }

        test "major-axis profiles start at the northern end of the major axis, 45 degrees apart" {
            let parent = ellipse "craterB" 1.0
            let specs = SupportingProfiles.forEllipse Planet.None up north EllipseProfileMode.MajorAxis parent |> ok "axis"
            Expect.equal (specs |> List.map (fun s -> s.link.direction)) [ "major"; "major+45"; "minor"; "major+135" ] "labels"
            let c = V3d(100.0, 50.0, 0.0)
            let major = specs.[0]
            Expect.floatClose { absolute = 1e-4; relative = 0.0 } (axialAngle (major.start - c) (V3d(1.0, 1.0, 0.0))) 0.0 "along the major axis (acos near 1 loses digits)"
            Expect.isGreaterThan major.start.Y c.Y "from its northern end"
            Expect.floatClose Accuracy.medium (Vec.distance major.start c) 3.0 "rim to rim: the semi-major axis"
            Expect.floatClose Accuracy.medium major.link.azimuth 45.0 "bearing of that end"
            Expect.floatClose Accuracy.medium specs.[2].link.azimuth 135.0 "minor: 90 degrees clockwise"
            Expect.floatClose Accuracy.medium (Vec.distance specs.[2].start c) 1.0 "the semi-minor axis"
        }

        test "on an asymmetric outline the profiles end on the drawn rim, not the stored ellipse" {
            let parent = ellipse "lopsided" 2.0
            let specs = SupportingProfiles.forEllipse Planet.None up north EllipseProfileMode.MajorAxis parent |> ok "lopsided"
            let c = V3d(100.0, 50.0, 0.0)
            let minorProfile = specs.[2]
            let lengths = [ Vec.distance minorProfile.start c; Vec.distance minorProfile.stop c ] |> List.sort
            Expect.floatClose Accuracy.medium lengths.[0] 1.0 "the narrow side"
            Expect.floatClose Accuracy.medium lengths.[1] 2.0 "the bulged side"
        }

        test "compass directions need a reference body" {
            match SupportingProfiles.forEllipse Planet.None up north EllipseProfileMode.Compass (ellipse "c" 1.0) with
            | Result.Ok _    -> failtest "expected a refusal on Planet.None"
            | Result.Error e -> Expect.stringContains e "reference body" "says why"
        }

        test "an ellipse without a stored shape is refused" {
            let parent = { ellipse "old" 1.0 with ellipticResults = None }
            Expect.isError (SupportingProfiles.forEllipse Planet.ENU up north EllipseProfileMode.Compass parent) "no shape, no profiles"
        }

        test "names are p<parent>_<n>, with the key when the parent has no label" {
            let parent = polyline "trench" [ V3d.Zero; V3d.IOO ]
            Expect.equal (SupportingProfiles.name parent 3) "ptrench_3" "labelled parent"
            let unlabelled = polyline "" [ V3d.Zero; V3d.IOO ]
            Expect.equal (SupportingProfiles.name unlabelled 0) (sprintf "p%s_0" (unlabelled.key.ToString().Substring(0, 8))) "key prefix"
        }
    ]

// --- through the drawing app, as the viewer sends it ------------------------------------

let private refSystemEnu = { Draw.refSystemFlat with planet = Planet.ENU }

let private profiles (m : DrawingModel) =
    Draw.annotations m |> List.filter (fun a -> a.profileOf.IsSome) |> List.sortBy (fun a -> a.profileOf.Value.index)

let private appTests =
    testList "drawing app" [

        test "Generate on a selected polyline adds Sky lines, linked and named, in one undo step" {
            let parent = polyline "trench" [ V3d(0.0, 0.0, 0.0); V3d(25.0, 0.0, 0.0) ]
            let m = withSelected parent
            // spacing 10 m (the default), length 6 m
            let m = Draw.run Draw.refSystemFlat m (DrawingAction.SetProfileLength (Numeric.SetValue 6.0))
            let m = Draw.run Draw.refSystemFlat m (DrawingAction.GenerateSupportingProfiles (Some flat))
            let ps = profiles m
            Expect.equal ps.Length 4 "stations 0, 10, 20, 25"
            for p in ps do
                Expect.equal p.geometry Geometry.Line "a line"
                Expect.equal p.projection Projection.Sky "sky projected"
                Expect.equal (p.points |> IndexList.count) 2 "two picked points"
                Expect.equal (p.segments |> IndexList.count) 1 "one sampled segment between them"
                Expect.equal (p.profileOf |> Option.map (fun l -> l.parent)) (Some parent.key) "linked to the polyline"
                Expect.isSome p.results "measured like a drawn line"
                Expect.isFalse p.showText "labels off: named, but not drawn"
            Expect.equal (ps |> List.map (fun p -> p.text)) [ "ptrench_0"; "ptrench_1"; "ptrench_2"; "ptrench_3" ] "names"
            match ps.[0].results with
            | Some r -> Expect.floatClose Accuracy.medium r.length 6.0 "the requested length"
            | None   -> ()
            Expect.stringContains (m.supportingProfiles.message |> Option.defaultValue "") "4 profiles" "reports what it did"

            let undone = Draw.run Draw.refSystemFlat m DrawingAction.Undo
            Expect.isEmpty (profiles undone) "one undo removes every generated profile"
            Expect.equal (Draw.annotations undone |> List.length) 1 "and keeps the polyline"
        }

        test "Generate on a selected ellipse adds its 4 compass profiles" {
            let parent = ellipse "craterA" 1.0
            let m = withSelected parent
            let m = Draw.run refSystemEnu m (DrawingAction.GenerateSupportingProfiles (Some flat))
            Expect.equal (profiles m |> List.map (fun p -> p.profileOf.Value.direction)) [ "N-S"; "NE-SW"; "E-W"; "NW-SE" ] "four, in order"
        }

        test "a refusal changes nothing and says why" {
            let parent = polyline "edge" [ V3d(0.0, 0.0, 0.0); V3d(30.0, 0.0, 0.0) ]
            let m = withSelected parent
            let holey : V3d -> Option<V3d> = fun p -> if p.X > 15.0 then None else flat p
            let after = Draw.run Draw.refSystemFlat m (DrawingAction.GenerateSupportingProfiles (Some holey))
            Expect.equal (Draw.annotations after |> List.length) 1 "no profile was added"
            Expect.equal after.undoStack.Length m.undoStack.Length "nothing to undo"
            Expect.stringContains (after.supportingProfiles.message |> Option.defaultValue "") "No profiles generated" "the panel says so"
        }

        test "toolbar auto profiles: a finished polyline is picked up only while Lineament profiles is on" {
            let pts = [ V3d(0.0, 0.0, 0.0); V3d(10.0, 0.0, 0.0); V3d(20.0, 5.0, 0.0) ]
            let drawn (auto : bool) =
                let started = Draw.startTool Draw.refSystemFlat Geometry.Polyline
                let started = Draw.run Draw.refSystemFlat started (DrawingAction.SetAutoLineamentProfiles auto)
                let working = pts |> List.fold (fun m p -> Draw.click Draw.refSystemFlat p m) started
                Draw.runFinishing Draw.refSystemFlat working DrawingAction.Finish
            let auto (on : bool) =
                let after, finished = drawn on
                Expect.equal (finished |> Option.map (fun a -> a.geometry)) (Some Geometry.Polyline) "Finish returns the finished polyline"
                Expect.equal (Draw.annotations after |> List.map (fun a -> a.key)) (finished |> Option.map (fun a -> a.key) |> Option.toList) "the one it added"
                DrawingApp.autoProfileParent after.supportingProfiles finished
            Expect.isSome (auto true) "a finished polyline with Lineament profiles on asks for profiles"
            Expect.isNone (auto false) "off: nothing"
        }

        test "toolbar auto profiles: only drawing input that finishes something returns it" {
            let started = Draw.run Draw.refSystemFlat (Draw.startTool Draw.refSystemFlat Geometry.Polyline) (DrawingAction.SetAutoLineamentProfiles true)
            let one, finished = Draw.runFinishing Draw.refSystemFlat started (DrawingAction.AddPointAdv(V3d.Zero, Draw.identityHit, None, "test-surface", None))
            Expect.isNone finished "a click that does not finish a polyline"
            let two = Draw.click Draw.refSystemFlat (V3d(10.0, 0.0, 0.0)) one
            let closed, _ = Draw.runFinishing Draw.refSystemFlat two DrawingAction.Finish
            // undo/redo push and pop LeafAdded too, but finish nothing
            let undone, finished = Draw.runFinishing Draw.refSystemFlat closed DrawingAction.Undo
            Expect.isNone finished "undo"
            let _, finished = Draw.runFinishing Draw.refSystemFlat undone DrawingAction.Redo
            Expect.isNone finished "redo"
            // a Line finishes itself on its second pick
            let line = Draw.startTool Draw.refSystemFlat Geometry.Line
            let line = Draw.click Draw.refSystemFlat V3d.Zero line
            let _, finished = Draw.runFinishing Draw.refSystemFlat line (DrawingAction.AddPointAdv(V3d(5.0, 0.0, 0.0), Draw.identityHit, None, "test-surface", None))
            Expect.equal (finished |> Option.map (fun a -> a.geometry)) (Some Geometry.Line) "the pick that completes a Line returns it"
        }

        test "toolbar auto profiles: Crater profiles picks up a finished ellipse, not a polyline" {
            let s = { DrawingModel.initialdrawing.supportingProfiles with autoCrater = true }
            let started = { Draw.startTool Draw.refSystemFlat Geometry.Polyline with supportingProfiles = s }
            let working = [ V3d.Zero; V3d(10.0, 0.0, 0.0) ] |> List.fold (fun m p -> Draw.click Draw.refSystemFlat p m) started
            let _, finished = Draw.runFinishing Draw.refSystemFlat working DrawingAction.Finish
            Expect.isNone (DrawingApp.autoProfileParent s finished) "a polyline is not a crater"
            let parent = ellipse "c" 1.0
            Expect.equal (DrawingApp.autoProfileParent s (Some parent) |> Option.map (fun a -> a.key)) (Some parent.key) "an ellipse is"
            Expect.isNone (DrawingApp.autoProfileParent s (Some { parent with profileOf = Some { parent = parent.key; index = 0; direction = "N-S"; azimuth = 0.0; station = None } })) "a generated profile never is"
        }

        test "generating again replaces the earlier profiles, in one undo step" {
            let parent = polyline "trench" [ V3d(0.0, 0.0, 0.0); V3d(25.0, 0.0, 0.0) ]
            let first = Draw.run Draw.refSystemFlat (withSelected parent) (DrawingAction.GenerateSupportingProfiles (Some flat))
            let firstKeys = profiles first |> List.map (fun p -> p.key)
            Expect.equal firstKeys.Length 4 "spacing 10 m: 0, 10, 20, 25"
            let m = Draw.run Draw.refSystemFlat first (DrawingAction.SetProfileSpacing (Numeric.SetValue 5.0))
            let second = Draw.run Draw.refSystemFlat m (DrawingAction.GenerateSupportingProfiles (Some flat))
            let secondSet = profiles second
            Expect.equal secondSet.Length 6 "spacing 5 m: 0, 5, ..., 25 - and none of the first set left"
            Expect.isTrue (secondSet |> List.forall (fun p -> not (List.contains p.key firstKeys))) "the first set is gone"
            Expect.stringContains (second.supportingProfiles.message |> Option.defaultValue "") "replacing the 4" "says so"
            let undone = Draw.run Draw.refSystemFlat second DrawingAction.Undo
            Expect.equal (profiles undone |> List.map (fun p -> p.key)) firstKeys "one undo brings the first set back"
        }

        test "nothing selected: a message, no change" {
            let after = Draw.run Draw.refSystemFlat DrawingModel.initialdrawing (DrawingAction.GenerateSupportingProfiles (Some flat))
            Expect.isEmpty (Draw.annotations after) "nothing added"
            Expect.stringContains (after.supportingProfiles.message |> Option.defaultValue "") "select a polyline or an ellipse" "says what to do"
        }
    ]

// --- persistence -------------------------------------------------------------------------

let private roundTrip (a : Annotation) : Annotation =
    a |> Json.serialize |> Json.formatWith JsonFormattingOptions.Compact |> Json.parse |> Json.deserialize

let private persistenceTests =
    testList "persistence" [

        test "the profile link survives save and load" {
            let link = { parent = Guid.NewGuid(); index = 2; direction = "E-W"; azimuth = 90.0; station = None }
            let a = { polyline "p" [ V3d.Zero; V3d.IOO ] with profileOf = Some link }
            Expect.equal (roundTrip a).profileOf (Some link) "ellipse profile link"
            let polyLink = { link with direction = ""; station = Some 12.5 }
            Expect.equal (roundTrip { a with profileOf = Some polyLink }).profileOf (Some polyLink) "polyline profile link"
        }

        test "a NaN azimuth survives save and load as NaN" {
            let link = { parent = Guid.NewGuid(); index = 0; direction = ""; azimuth = nan; station = Some 3.0 }
            let a = { polyline "p" [ V3d.Zero; V3d.IOO ] with profileOf = Some link }
            match (roundTrip a).profileOf with
            | Some l -> Expect.isTrue (Double.IsNaN l.azimuth) "NaN back"; Expect.equal l.station (Some 3.0) "the rest intact"
            | None   -> failtest "the link was lost"
        }

        test "a profileOf from a later version, or a damaged one, loads as no link instead of failing the scene" {
            let a = { polyline "p" [ V3d.Zero; V3d.IOO ] with profileOf = Some { parent = Guid.NewGuid(); index = 0; direction = ""; azimuth = 0.0; station = None } }
            let text = a |> Json.serialize |> Json.formatWith JsonFormattingOptions.Compact
            // the link's own object: flat, so the first } after its start closes it
            let start = text.IndexOf("\"profileOf\":{")
            let link = text.Substring(start, text.IndexOf("}", start) - start + 1)
            for broken in [ text.Replace(link, link.Replace("\"version\":0", "\"version\":99"))
                            text.Replace(a.profileOf.Value.parent.ToString(), "not-a-guid") ] do
                Expect.notEqual broken text "the test really changed the file"
                let loaded : Annotation = broken |> Json.parse |> Json.deserialize
                Expect.isNone loaded.profileOf "dropped"
                Expect.equal loaded.key a.key "the annotation itself loads"
        }

        test "importing gives new keys and keeps every profile pointing at its parent" {
            let parent = ellipse "crater" 1.0
            let child =
                { polyline "pcrater_0" [ V3d.Zero; V3d.IOO ] with
                    profileOf = Some { parent = parent.key; index = 0; direction = "N-S"; azimuth = 0.0; station = None } }
            let groups =
                GroupsModel.initial
                |> GroupsApp.addLeafToActiveGroup (Leaf.Annotations parent) false
                |> GroupsApp.addLeafToActiveGroup (Leaf.Annotations child) false
            let imported = PRo3D.ViewerApp.rekeyImported groups
            let annos = imported.flat |> Leaf.toAnnotations |> HashMap.toList |> List.map snd
            let newParent = annos |> List.tryFind (fun a -> a.text = "crater")
            let newChild  = annos |> List.tryFind (fun a -> a.text = "pcrater_0")
            match newParent, newChild with
            | Some p, Some c ->
                Expect.notEqual p.key parent.key "the parent got a new key"
                Expect.equal (c.profileOf |> Option.map (fun l -> l.parent)) (Some p.key) "the profile follows it"
            | _ -> failtest "both annotations survive the import"
        }

        test "an annotation without the key loads with no link, and none is written for it" {
            let a = polyline "plain" [ V3d.Zero; V3d.IOO ]
            let text = a |> Json.serialize |> Json.formatWith JsonFormattingOptions.Compact
            Expect.isFalse (text.Contains "profileOf") "ordinary annotations write no profileOf key"
            Expect.isNone (roundTrip a).profileOf "and read back None"
        }
    ]

// --- export --------------------------------------------------------------------------------

let private cell (column : string) (record : ExportRecord) =
    record.fields |> List.tryFind (fst >> (=) column) |> Option.map snd

let private exportTests =
    testList "Craters export" [

        test "the Craters preset is Boulders plus the profiles file" {
            let craters = AnnotationExportSettings.initial |> AnnotationExportSettings.applyPreset ExportPreset.Craters
            let boulders = AnnotationExportSettings.initial |> AnnotationExportSettings.applyPreset ExportPreset.Boulders
            Expect.isTrue craters.withProfiles "writes the profiles file"
            Expect.equal { craters with withProfiles = false } boulders "otherwise exactly Boulders"
            for preset in ExportPreset.all |> List.filter (fun p -> p <> ExportPreset.Custom && p <> ExportPreset.Craters && p <> ExportPreset.Lineaments) do
                Expect.isFalse (craters |> AnnotationExportSettings.applyPreset preset).withProfiles (sprintf "%A does not" preset)
        }

        test "the Lineaments preset: one row per polyline, plus the profiles file" {
            let lineaments = AnnotationExportSettings.initial |> AnnotationExportSettings.applyPreset ExportPreset.Lineaments
            Expect.isTrue lineaments.withProfiles "writes the profiles file"
            Expect.equal lineaments.granularity ExportGranularity.PerAnnotation "one row per polyline"
            Expect.equal lineaments.typeFilter ExportTypeFilter.LinesOnly "lines only"
            Expect.equal lineaments.scope ExportScope.All "all of them"
            for f in [ AnnotationField.Key; AnnotationField.Length; AnnotationField.WayLength; AnnotationField.Bearing ] do
                Expect.contains lineaments.annotationFields f (sprintf "%A" f)
        }

        test "lines only admits lines and polylines, but not the profiles generated from them" {
            let parent = polyline "trench" [ V3d.Zero; V3d(10.0, 0.0, 0.0) ]
            let line = { polyline "l" [ V3d.Zero; V3d.IOO ] with geometry = Geometry.Line }
            let profile =
                { line with
                    key = Guid.NewGuid()
                    profileOf = Some { parent = parent.key; index = 0; direction = ""; azimuth = 0.0; station = Some 0.0 } }
            let admits = ExportTypeFilter.admits ExportTypeFilter.LinesOnly
            Expect.isTrue (admits parent) "a polyline"
            Expect.isTrue (admits line) "a hand-drawn line"
            Expect.isFalse (admits profile) "a generated profile"
            Expect.isFalse (admits (ellipse "e" 1.0)) "an ellipse"
        }

        test "polyline profile rows carry parent and station" {
            let parent = polyline "trench" [ V3d(0.0, 0.0, 0.0); V3d(25.0, 0.0, 0.0) ]
            let m = withSelected parent
            let m = Draw.run Draw.refSystemFlat m (DrawingAction.GenerateSupportingProfiles (Some flat))
            let settings =
                AnnotationExportSettings.initial
                |> AnnotationExportSettings.applyPreset ExportPreset.Lineaments
                |> AnnotationExport.profilesSettings
            let records =
                AnnotationExport.buildRecords settings None HashMap.empty Planet.None up
                    (AnnotationExport.profilesOf [ parent ] (Draw.annotations m))
            let stations = records |> List.choose (cell "profileStation") |> List.distinct
            Expect.equal stations [ VNum 0.0; VNum 10.0; VNum 20.0; VNum 25.0 ] "one station per profile, in order"
            for r in records do
                Expect.equal (cell "parentKey" r) (Some (VText (parent.key.ToString()))) "every row names its polyline"
        }

        test "Surface properties applies to the profiles file, opt-in as in Profile" {
            let craters = AnnotationExportSettings.initial |> AnnotationExportSettings.applyPreset ExportPreset.Craters
            Expect.isFalse (AnnotationExport.profilesSettings craters).sampleSurfaceProperties "off by default"
            let ticked = { craters with sampleSurfaceProperties = true }
            Expect.isTrue (AnnotationExport.profilesSettings ticked).sampleSurfaceProperties "the checkbox reaches the profiles file"
        }

        test "the profiles file sits beside the main one" {
            Expect.equal (AnnotationExport.profilesPath @"C:\out\craters.csv") @"C:\out\craters_profiles.csv" "stem + _profiles.csv"
        }

        test "profiles are collected per crater, in index order, and only for exported craters" {
            let a = ellipse "A" 1.0
            let b = ellipse "B" 1.0
            let profile (parent : Annotation) i =
                { polyline (sprintf "p%s_%d" parent.text i) [ V3d.Zero; V3d.IOO ] with
                    profileOf = Some { parent = parent.key; index = i; direction = ""; azimuth = 0.0; station = None } }
            let candidates = [ profile b 1; profile a 1; profile b 0; profile a 0; polyline "plain" [ V3d.Zero; V3d.IOO ] ]
            let picked = AnnotationExport.profilesOf [ a; b ] candidates |> List.map (fun p -> p.text)
            Expect.equal picked [ "pA_0"; "pA_1"; "pB_0"; "pB_1" ] "grouped by crater, then by index"
            Expect.equal (AnnotationExport.profilesOf [ a ] candidates |> List.length) 2 "B was not exported"
        }

        test "profile rows carry the link on every point" {
            let parent = ellipse "craterA" 1.0
            let m = withSelected parent
            let m = Draw.run refSystemEnu m (DrawingAction.GenerateSupportingProfiles (Some flat))
            let settings =
                AnnotationExportSettings.initial
                |> AnnotationExportSettings.applyPreset ExportPreset.Craters
                |> AnnotationExport.profilesSettings
            let records =
                AnnotationExport.buildRecords settings None HashMap.empty Planet.None up
                    (AnnotationExport.profilesOf [ parent ] (Draw.annotations m))
            Expect.isGreaterThan records.Length 8 "one row per sampled point of 4 profiles"
            let schema = AnnotationExport.schemaFor settings records
            for c in [ "parentKey"; "profileIndex"; "profileDirection"; "profileAzimuth"; "profileStation"; "pointIndex"; "distance" ] do
                Expect.contains schema c (sprintf "the profiles file has %s" c)
            for r in records do
                Expect.equal (cell "parentKey" r) (Some (VText (parent.key.ToString()))) "every row names its crater"
            let directions = records |> List.choose (cell "profileDirection") |> List.distinct
            Expect.equal directions [ VText "N-S"; VText "NE-SW"; VText "E-W"; VText "NW-SE" ] "the four directions, in order"
        }
    ]

let tests =
    testList "Section 22 - Supporting profiles" [
        generatorTests
        appTests
        persistenceTests
        exportTests
    ]
