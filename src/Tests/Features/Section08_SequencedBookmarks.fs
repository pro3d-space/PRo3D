/// Section 8 — Sequenced Bookmarks
///   TC-8.1 (Sequenced Bookmarks — Add and Animate)
///   TC-8.2 (Record and Generate Images) drives the renderer / an external
///   snapshot process, so it is not covered here.
///
///   Adding a sequenced bookmark captures the full live scene state (surfaces,
///   annotations, config, …), which only exists inside a running viewer; the part
///   that is pure model logic is the animation configuration, exercised here through
///   the real AnimationSettings.update.
module PRo3D.Tests.Section08_SequencedBookmarks

open Aardvark.Base
open Aardvark.Rendering                  // CameraView
open Aardvark.UI.Primitives              // Numeric
open Aardvark.UI.Animation               // IAnimation, Action, LocalTime

open Expecto

open PRo3D.Core                          // AnimationSettings (module)
open PRo3D.Core.BookmarkAnimations       // Primitives.cameraInterpolateSafe
open PRo3D.Core.SequencedBookmarks       // AnimationSettingsAction, AnimationLoopMode
open PRo3D.Tests

/// Drives an animation to the given point (0..1 of its duration) and reads its value.
/// Perform only enqueues an action; the value is produced by Commit, which runs the
/// state machine - so every action has to be followed by a commit.
let private sampleCamera (t : float) (anim : IAnimation<unit, CameraView>) : CameraView =
    let instance = anim.Create (Sym.ofString "test")
    let tick = GlobalTime.zero

    instance.Perform (Action.Start LocalTime.zero)
    instance.Commit((), tick) |> ignore

    instance.Perform (Action.Update (LocalTime.ofNormalizedPosition instance.Duration t, false))
    instance.Commit((), tick) |> ignore

    instance.Value

/// Playback does not use a segment as-is. When both bookmarks carry a scene state -
/// the normal case - interpolateBm combines the camera animation with the focal-length
/// animation via Animation.map2 and then rescales the result to the bookmark's duration
/// (`toNext |> Animation.seconds dst.duration.value`). Composing and rescaling is where
/// a zero-duration component collapses, so the tests have to go through it too.
let private asPlayed (anim : IAnimation<unit, CameraView>) =
    let focalLike = Animation.create (fun (t : float) -> t) |> Animation.seconds 1.0
    Animation.map2 (fun cam _ -> cam) anim focalLike
    |> Animation.seconds 5.0

/// A camera on the Jezero surface, taken from a real sequenced bookmark.
let private jezeroView =
    CameraView(
        V3d(0.20811065578437726, 0.9253978445872588, 0.31674719285615177),      // sky
        V3d(709857.680527099, 3141912.186800803, 1073840.48391507),             // location
        V3d(-0.7031123388120046, -0.27585526813175626, -0.6553906545368723),    // forward
        V3d(-0.2776991696603903, 0.9550167504467744, -0.10404891895648513),     // up
        V3d(0.6546114956065489, 0.10884336180971418, -0.7480888399179051))      // right

/// Two bookmarks that look the same way from different places - the case that regressed.
let private pureDolly =
    let dst = CameraView.orient (V3d(709419.8932611526, 3141740.427727654, 1073432.410173595))
                                jezeroView.Orientation
                                jezeroView.Sky
    jezeroView, dst

/// What CameraView.orient produces from a default (all-zero) rotation: forward +Y.
let private defaultRotForward =
    (CameraView.orient jezeroView.Location Unchecked.defaultof<Rot3d> jezeroView.Sky).Forward

let tests =
    testList "Section 8 — Sequenced Bookmarks" [

        // TC-8.1 Add and Animate — the animation configuration

        test "TC-8.1 SetGlobalDuration sets the total animation duration" {
            let m = AnimationSettings.update AnimationSettings.init (AnimationSettingsAction.SetGlobalDuration (Numeric.SetValue 10.0))
            Expect.floatClose Accuracy.high m.globalDuration.value 10.0 "global duration should be 10 s"
        }

        test "TC-8.1 ToggleGlobalAnimation flips the global-animation flag" {
            let m = AnimationSettings.update AnimationSettings.init AnimationSettingsAction.ToggleGlobalAnimation
            Expect.equal m.useGlobalAnimation (not AnimationSettings.init.useGlobalAnimation)
                "the global-animation flag should toggle"
        }

        test "TC-8.1 ToggleUseEasing flips easing" {
            let m = AnimationSettings.update AnimationSettings.init AnimationSettingsAction.ToggleUseEasing
            Expect.equal m.useEasing (not AnimationSettings.init.useEasing) "easing should toggle"
        }

        test "TC-8.1 ToggleUseSmoothing flips path smoothing" {
            let m = AnimationSettings.update AnimationSettings.init AnimationSettingsAction.ToggleUseSmoothing
            Expect.equal m.smoothPath (not AnimationSettings.init.smoothPath) "path smoothing should toggle"
        }

        test "TC-8.1 SetSmoothingFactor sets the smoothing factor" {
            let m = AnimationSettings.update AnimationSettings.init (AnimationSettingsAction.SetSmoothingFactor (Numeric.SetValue 0.75))
            Expect.floatClose Accuracy.high m.smoothingFactor.value 0.75 "smoothing factor should be 0.75"
        }

        test "TC-8.1 SetLoopMode sets the playback loop mode" {
            let m = AnimationSettings.update AnimationSettings.init (AnimationSettingsAction.SetLoopMode AnimationLoopMode.Repeat)
            Expect.equal m.loopMode AnimationLoopMode.Repeat "loop mode should be Repeat"
        }

        // TC-8.3 Playback camera interpolation
        //
        // Playing a sequence drives the camera through BookmarkAnimations.Primitives
        // .cameraInterpolateSafe. A segment where one of position/orientation does not
        // change is the interesting case: an implementation that animates the two
        // components separately, with a duration derived from how far each one travels,
        // lets the static component collapse to zero duration and sample to its default
        // rather than hold. That produced Rot3d() - whose forward is +Y - for a pure
        // dolly, i.e. the camera looking off into the sky for the whole segment.
        //
        // Of these, only "a pure rotation keeps the position" actually fails against the
        // implementation that shipped in 6.0.0-rc2; the rest pass on both and are guards.
        //
        // TODO: the reported symptom - a pure *dolly* losing its orientation - is not
        // reproduced at this level. It stays green on the old implementation whether the
        // segment is sampled bare, rescaled the way interpolateBm rescales it, or composed
        // with a focal animation through Animation.map2 as the both-scene-states branch
        // does. Reproducing it needs a test one level up, over interpolateBm /
        // pathWithPausing with real SequencedBookmarkModels carrying scene states, where
        // Animation.sequential, the pause segment and the global-duration rescale are all
        // in play. Until then that case is covered by manual verification only.

        test "TC-8.3 a pure dolly keeps the orientation of both endpoints" {
            let a, b = pureDolly
            let mid = sampleCamera 0.5 (asPlayed (Primitives.cameraInterpolateSafe a b))

            Expect.isLessThan (Vec.distance mid.Forward a.Forward) 1e-6
                "forward should hold: both endpoints look the same way"
            Expect.isGreaterThan (Vec.distance mid.Forward defaultRotForward) 1e-3
                "forward must not collapse to the default rotation's +Y"
        }

        test "TC-8.3 a pure dolly interpolates the position" {
            let a, b = pureDolly
            let mid = sampleCamera 0.5 (asPlayed (Primitives.cameraInterpolateSafe a b))

            Expect.isLessThan (Vec.distance mid.Location ((a.Location + b.Location) * 0.5)) 1e-6
                "midpoint of the segment should be the midpoint of the two locations"
        }

        test "TC-8.3 a pure rotation keeps the position" {
            let a = jezeroView
            let b = CameraView.orient a.Location (Rot3d.RotationZ 0.4 * a.Orientation) a.Sky
            let mid = sampleCamera 0.5 (asPlayed (Primitives.cameraInterpolateSafe a b))

            Expect.isLessThan (Vec.distance mid.Location a.Location) 1e-6
                "location should hold when only the orientation changes"
        }

        test "TC-8.3 coincident endpoints hold the view" {
            let a = jezeroView
            let mid = sampleCamera 0.5 (asPlayed (Primitives.cameraInterpolateSafe a a))

            Expect.isFalse (obj.ReferenceEquals(mid, null)) "a coincident segment must not sample to null"
            Expect.isLessThan (Vec.distance mid.Forward a.Forward) 1e-6 "forward should hold"
            Expect.isLessThan (Vec.distance mid.Location a.Location) 1e-6 "location should hold"
        }

        test "TC-8.3 endpoints are reproduced at t=0 and t=1" {
            let a, b = pureDolly
            let anim = asPlayed (Primitives.cameraInterpolateSafe a b)

            let atStart = sampleCamera 0.0 anim
            let atEnd   = sampleCamera 1.0 anim

            Expect.isLessThan (Vec.distance atStart.Location a.Location) 1e-6 "t=0 should be the source location"
            Expect.isLessThan (Vec.distance atEnd.Location   b.Location) 1e-6 "t=1 should be the destination location"
        }
    ]
