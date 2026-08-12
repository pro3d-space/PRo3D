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

open Aardvark.UI.Primitives              // Numeric

open Expecto

open PRo3D.Core                          // AnimationSettings (module)
open PRo3D.Core.SequencedBookmarks       // AnimationSettingsAction, AnimationLoopMode
open PRo3D.Tests

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
    ]
