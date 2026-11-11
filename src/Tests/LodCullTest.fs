module LodCullTest

open Expecto

open Aardvark.Base
open Aardvark.Rendering

/// Frustum test the OPC LoD deciders (Surface.Sg.fs) use before refining a patch:
/// ViewProjection.intersects (Aardvark.Rendering.Common).
///
/// The fixture is a real failing case: camera inside a patch box, half its corners behind
/// the camera; the old NDC test (project corners, divide by w, box them) culled it.

module private Fixture =

    let camera =
        let location = V3d(3026384.1895809504, 1150452.2352767338, -1030581.2660720154)
        let forward  = V3d(-0.7205105442302986, -0.18610517825332354, -0.6680040555866841)
        let sky      = V3d(0.8915565763284992, 0.33924384935278734, -0.30006746221759595)
        CameraView.lookAt location (location + forward) sky

    let viewProj =
        let frustum =
            { left = -0.0577560975609756; right = 0.0577560975609756
              bottom = -0.0430398842208317; top = 0.0430398842208317
              near = 0.1; far = 500000.0; isOrtho = false }
        CameraView.viewTrafo camera * Frustum.projTrafo frustum

    /// Bounding box of the failing patch.
    let patchBox =
        Box3d(V3d(3025430.695853888, 1150215.017260046, -1032812.982063703),
              V3d(3026671.997407306, 1150742.897552272, -1029021.485962757))

    /// The test the deciders used before, kept here only to show the fixture hits it.
    let oldNdcTest (box : Box3d) (viewProj : Trafo3d) =
        let boundsNdc = Box3d(box.ComputeCorners() |> Array.map viewProj.TransformPosProj)
        Box3d(-V3d.IIO, V3d.III).Intersects boundsNdc

    /// A box of half-size `r` centred `d` metres along a camera axis.
    let boxAlong (axis : V3d) (d : float) (r : float) =
        Box3d.FromCenterAndSize(camera.Location + axis * d, V3d(2.0 * r))

let tests () =
    testList "LoD frustum cull" [
        test "fixture: camera is inside the patch box and the old NDC test culls it" {
            Expect.isTrue (Fixture.patchBox.Contains Fixture.camera.Location) "camera inside box"
            Expect.isFalse (Fixture.oldNdcTest Fixture.patchBox Fixture.viewProj)
                "the old test must reproduce the bug, or this fixture proves nothing"
        }

        test "patch box around the camera is visible" {
            Expect.isTrue (ViewProjection.intersects Fixture.patchBox Fixture.viewProj) "visible"
        }

        test "box ahead of the camera is visible" {
            let box = Fixture.boxAlong Fixture.camera.Forward 500.0 50.0
            Expect.isTrue (ViewProjection.intersects box Fixture.viewProj) "visible"
        }

        test "thin box between near and 2*near is visible (OpenGL clip space, not DirectX)" {
            // near = 0.1. A DirectX-convention test (0 <= z) culls everything closer
            // than ~0.2 m with our PerspectiveProjectionGL; this pins the GL variant.
            let box = Fixture.boxAlong Fixture.camera.Forward 0.15 0.01
            Expect.isFalse (box.IntersectsFrustum Fixture.viewProj.Forward) "DX test culls it"
            Expect.isTrue (ViewProjection.intersects box Fixture.viewProj) "visible"
        }

        test "box behind the camera is culled" {
            let box = Fixture.boxAlong Fixture.camera.Forward -500.0 50.0
            Expect.isFalse (ViewProjection.intersects box Fixture.viewProj) "culled"
        }

        test "box beside the camera, outside the field of view, is culled" {
            let box = Fixture.boxAlong Fixture.camera.Right 500.0 50.0
            Expect.isFalse (ViewProjection.intersects box Fixture.viewProj) "culled"
        }

        test "box beyond the far plane is culled" {
            let box = Fixture.boxAlong Fixture.camera.Forward 600000.0 50.0
            Expect.isFalse (ViewProjection.intersects box Fixture.viewProj) "culled"
        }
    ]
