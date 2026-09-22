namespace PRo3D.Core

open Aardvark.Base
open Aardvark.Rendering

/// Per-pixel illumination geometry (incidence, emission, phase) as a data product.
///
/// This is the float32 counterpart of the colour-mapped angle visualisations in
/// PRo3D.ProjectionTestbed's `Shading` module. Those quantise to 8-bit for eyeballing;
/// this writes the angles themselves, unquantised, for downstream analysis.
///
/// LIMITATION -- these are *local* illumination angles, computed from the surface normal
/// and the sun/observer directions at each fragment. They say nothing about whether the
/// point is actually lit: terrain self-shadowing is not evaluated, so a pixel lying in
/// the shadow of a ridge still reports its geometric incidence angle. Consumers needing
/// true illumination must combine these with a shadow test.
module SunAngles =

    module Shaders =

        open FShade
        open PRo3D.SPICE.Shaders   // UniformScope.SunDirectionWorld

        /// Consumes what ImageProjection.Shaders.stableImageProjectionTrafo and
        /// generateNormal put in place: the object-space position stashed before the
        /// stable transform, and the per-face geometric normal.
        type AngleVertex =
            {
                [<Position>] pos : V4f
                [<Semantic("LocalNormal")>] localNormal : V3f
                [<Semantic("BodyLocalPos")>] localPos : V4f
            }

        /// (incidence, emission, phase) in radians, evaluated in view space.
        ///
        /// Deliberately not clamped to 0..pi/2: an emission angle above 90 degrees means a
        /// facet that faces away from the observer was nevertheless rasterised, which flags
        /// an inconsistent shape model. Clamping would hide that.
        [<ReflectedDefinition>]
        let anglesOf (localNormal : V3f) (localPos : V4f) =
            let n = uniform.ModelViewTrafo.TransformDir localNormal |> Vec.normalize
            let viewPos = uniform.ModelViewTrafo * localPos
            let toCamera = -viewPos.XYZ |> Vec.normalize
            let l = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld |> Vec.normalize
            let i = acos (min 1.0f (max -1.0f (Vec.dot n l)))
            let e = acos (min 1.0f (max -1.0f (Vec.dot n toCamera)))
            let p = acos (min 1.0f (max -1.0f (Vec.dot l toCamera)))
            V3f(i, e, p)

        /// Blue -> cyan -> green -> yellow -> red. Hue boundaries give readable iso-angle
        /// contours, which a luminance ramp does not.
        ///
        /// Marked ReflectedDefinition so it can be used in a shader, but it is an ordinary
        /// function and is also called from the CPU when colour-coding a float readback.
        /// Keeping one definition is what makes pro3d-tool's false-colour output directly
        /// comparable with PRo3D.ProjectionTestbed's angle visualisations.
        [<ReflectedDefinition>]
        let colormap (t : float32) =
            let t = min 1.0f (max 0.0f t)
            if t < 0.25f then    let u = t / 0.25f              in V3f(0.0f, u, 1.0f)
            elif t < 0.5f then   let u = (t - 0.25f) / 0.25f    in V3f(0.0f, 1.0f, 1.0f - u)
            elif t < 0.75f then  let u = (t - 0.5f) / 0.25f     in V3f(u, 1.0f, 0.0f)
            else                 let u = (t - 0.75f) / 0.25f    in V3f(1.0f, 1.0f - u, 0.0f)

        /// Packs the three angles into one RGBA32F attachment, with alpha as a coverage
        /// mask: 1 where the surface was rasterised, and whatever the target was cleared to
        /// elsewhere. One attachment rather than three means one render pass and one
        /// readback; the CPU splits the channels into separate single-band rasters and uses
        /// alpha to decide which pixels are nodata.
        let sunAnglesFloat (v : AngleVertex) =
            fragment {
                let a = anglesOf v.localNormal v.localPos
                return V4f(a.X, a.Y, a.Z, 1.0f)
            }
