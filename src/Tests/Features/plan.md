1. TC-1.3 FlyTo — the real one (Section01_StartingPRo3D.fs)
The test "FlyTo lands the camera on the surface bounding box" asserts:


Expect.floatClose Accuracy.medium (abs (Vec.dot view.Forward axis)) 1.0
Note the abs. I measured that FlyTo lands the camera at bb.Max but with the view direction exactly negated — the camera ends up pointing away from the surface centre, into empty space. abs(dot) accepts either direction, so the test passes despite that. A correct test would assert the camera looks toward the centre and would fail, documenting the bug. This is almost certainly a real defect in the deprecated animateForwardAndLocation sign convention.

Two related things this test also quietly ignores:

The resulting view.Sky came back as [0,0,1] instead of the Mars-local up — I don't assert on it at all.
TC-1.4/1.5 then assert the flown-to camera round-trips through save/load. Save/load is fine, but the camera they faithfully persist is the buggy one — so those tests are green on top of the quirk.
2. TC-2.2 ArcBall drag (Section02)
Originally I wanted to assert "orbiting moves the camera and preserves its distance to the centre." Headless, the orbit never actually moved (the Aardvark controller integrates on the render loop, not the StepTime messages I fed it), so I downgraded the test to only "the explore center stays fixed." That passes trivially. If ArcBall orbit rotation were broken, this test would not catch it. It's more "couldn't exercise it headlessly" than "hiding a known bug," but the effect is the same: weak coverage dressed as a pass.

3. TC-2.1 FreeFly look-drag (same file)
Asserts only that view.Forward changed at all (distance > 1e-4). Wrong axis, wrong magnitude, or inverted look would all still pass. Weak.

Not hidden, but worth knowing
While writing these I hit no-op / stub actions in PRo3D. I omitted them rather than test them (so they're not masked), but they're latent:

AddBookmarkReference (Surface Comparison, would-be TC-14.4) is a stub: | AddBookmarkReference _ -> m, surfaceModel. Does nothing.
ToggleSurfaceHighlighting in ConfigProperties.update returns the model unchanged (the real line is commented out).
And a softer point: the drawing/scalebar tests use Planet.None to keep the maths pure, which means they don't exercise the real Mars geographic-projection code paths (the ellipse tests do use Mars; the rest don't).

Want me to turn TC-1.3 into a test that asserts the correct FlyTo direction — so it fails and stands as an executable bug report — and add a similar strict-but-[<Pending>] marker for the ArcBall orbit? That converts these from "green and hiding it" to "red and documenting it," which is usually what you want from a test protocol