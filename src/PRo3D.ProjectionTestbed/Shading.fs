namespace PRo3D.ProjectionTestbed

open System
open Aardvark.Base
open Aardvark.Rendering
open FShade

/// Plain sun-lit shading of the shape model, with no instrument image involved.
///
/// This exists to break the circularity in the projected-texture comparison. When the
/// render camera IS the projector camera, the projected texture lands correctly on any
/// geometry whatsoever, so agreement with the reference proves nothing about the shape
/// model. A sun-lit render, by contrast, is produced without the reference image playing
/// any part: if its highlights and shadows line up with the real image's, the topography
/// genuinely agrees.
module Shading =

    type UniformScope with
        /// Unit vector from the body towards the sun, in the render reference frame
        /// (i.e. the body-fixed frame the OPCs live in).
        member x.SunDirectionWorld : V3f = uniform?SunDirectionWorld
        /// Per-patch approximate outward direction, in patch-local space: supplied by
        /// ImageProjectionOpcRendering.projectionUniformMap as the direction from the body
        /// centre to this patch. Used to settle the face-normal sign, which is NOT a
        /// global constant -- the Didymos and Dimorphos OPCs are wound oppositely, so
        /// `cross edge1 edge2` points outward on one and inward on the other.
        member x.ApproximateBodyNormalLocalSpace : V3f = uniform?ApproximateBodyNormalLocalSpace

    type Vertex =
        {
            [<Position>] pos : V4f
            // Written by ImageProjection.Shaders.generateNormal: the true per-face
            // geometric normal in patch-local space. Deliberately NOT the sphere normal
            // that planetLocalLightingViewSpace supplies -- that one is normal to a
            // fitted sphere, so it would shade the body as a smooth ball and show no
            // topographic relief at all, which is the entire point of this pass.
            [<Semantic("LocalNormal")>] localNormal : V3f
            // Body-local position, stashed by stableImageProjectionTrafo before
            // stableTrafo overwrites [<Position>] with clip space. Needed to recover the
            // view-space position, and hence the direction to the camera.
            [<Semantic("BodyLocalPos")>] localPos : V4f
        }

    /// Lambertian diffuse term. No specular: airless regolith is not glossy, and a
    /// specular lobe would add structure that is not in the shape model.
    ///
    /// Two variants because the sign of `generateNormal`'s face normal relative to the
    /// body frame is not something to assert from first principles -- the same operand
    /// order was already wrong once this session, and whether ModelTrafo's handedness
    /// preserves it is exactly the kind of assumption that has been failing here. So
    /// render both and let the comparison against the real image decide: the correct
    /// sign correlates positively with the instrument image, the wrong one negatively.
    /// A near-zero score for both would mean the shape model has no usable relief.
    let sunDiffuse (v : Vertex) =
        fragment {
            // Both vectors are taken to VIEW space rather than meeting in the body frame.
            // ModelTrafo alone is not enough: a secondary body placed with Sg.trafo (e.g.
            // Dimorphos positioned relative to Didymos) rendered in the correct place but
            // came out uniformly black, because its normals stayed in its own body-fixed
            // frame while the sun vector was in the primary's. ModelViewTrafo is by
            // construction the chain that actually positions the geometry, so if the body
            // draws in the right place its normals are right too.
            // The face-normal sign is per-DATASET, not global: the Didymos and Dimorphos
            // OPCs are wound oppositely, so `cross edge1 edge2` points outward on one and
            // inward on the other, and any single global sign blackens one body.
            //
            // Resolve it geometrically instead of by configuration. On a closed surface a
            // *visible* fragment's outward normal must point towards the camera -- if it
            // pointed away, that facet would be back-facing and something else would be in
            // front of it. So flipping the normal to face the viewer recovers the true
            // outward normal regardless of winding, with no per-dataset knowledge.
            //
            // Rejected alternative: ApproximateBodyNormalLocalSpace. It carries no
            // per-patch information at coarse LOD (one patch covers a whole body here), so
            // it degenerates to a single constant direction and splits each body in half.
            // It would also make lighting LOD-dependent. See debug_outward_sign.png.
            // generateNormal now orients outward per triangle, so the normal arrives with
            // the correct sign for any dataset winding and needs no fixing up here.
            let n = uniform.ModelViewTrafo.TransformDir v.localNormal |> Vec.normalize
            let l = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld |> Vec.normalize
            let d = Vec.dot n l |> max 0.0f
            // A little ambient so the night side is distinguishable from empty space:
            // a pure-black terminator would collapse the silhouette comparison into
            // "where is the body lit", which is not what this pass is measuring.
            let i = 0.05f + 0.95f * d
            return V4f(i, i, i, 1.0f)
        }

    /// Diagnostic: the world-space face normal as RGB, and the sun direction painted into
    /// the corner. Distinguishes the two ways the diffuse term can come out uniformly
    /// zero -- a dead normal (image goes black) from a dead sun uniform (image is
    /// coloured but the corner swatch is black).
    let debugNormal (v : Vertex) =
        fragment {
            let n = uniform.ModelViewTrafo.TransformDir v.localNormal |> Vec.normalize
            let l = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld
            // encode -1..1 into 0..1 so a zero vector reads as mid-grey, not black
            let rgb = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * n
            let swatch = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * l
            let uv = v.pos.XY
            return V4f((if uv.X < 0.0f && uv.Y < 0.0f then swatch else rgb), 1.0f)
        }

    // ---------------------------------------------------------------------------------
    // Photometric angle backplanes, for discussion with the calibration team.
    //
    // These are a VISUALISATION, not a data product: the angle is colour-mapped and then
    // modulated by the projected instrument image so that surface structure and angle
    // field are legible together. A real backplane must be float32 -- quantising angles
    // to 8 bits would destroy the calibration use -- and must carry an illumination mask,
    // since cos(i) > 0 does not mean lit (self-shadowing needs ray casting, which is NOT
    // done here). Treat every shadowed region in these images as wrong.
    // ---------------------------------------------------------------------------------

    // localNormal comes from the shared generateNormal (sign set via NormalFlip).
    type AngleVertex =
        {
            [<Position>] pos : V4f
            [<Semantic("LocalNormal")>] localNormal : V3f
            [<Semantic("BodyLocalPos")>] localPos : V4f
            [<Semantic("ProjectedImagePos")>] projectedPos : V4f
        }

    let private projectedTexture =
        sampler2d {
            texture uniform?ProjectedTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Border
            addressV WrapMode.Border
            // Black, not white: outside the image footprint there is no data, and it
            // should read as absent rather than as a bright surface.
            borderColor C4f.Black
        }

    /// Blue -> cyan -> green -> yellow -> red. Chosen over a perceptual map because the
    /// hue boundaries give readable contours at fixed angles, which is what makes these
    /// images discussable.
    [<ReflectedDefinition>]
    let private colormap (t : float32) =
        let t = min 1.0f (max 0.0f t)
        if t < 0.25f then    let u = t / 0.25f              in V3f(0.0f, u, 1.0f)
        elif t < 0.5f then   let u = (t - 0.25f) / 0.25f    in V3f(0.0f, 1.0f, 1.0f - u)
        elif t < 0.75f then  let u = (t - 0.5f) / 0.25f     in V3f(u, 1.0f, 0.0f)
        else                 let u = (t - 0.75f) / 0.25f    in V3f(1.0f, 1.0f - u, 0.0f)

    /// (incidence, emission, phase) in radians, all in view space.
    /// The normal is oriented towards the viewer for the reason given in sunDiffuse.
    /// NOTE: the normal is used as generateOutwardNormal produced it -- NOT re-oriented
    /// towards the camera. That is deliberate: emission must be free to exceed 90 deg,
    /// because a visible facet whose normal points away from the viewer means the shape
    /// model is inconsistent at that pixel, and that is a quality signal the calibration
    /// team needs rather than something to clamp away.
    [<ReflectedDefinition>]
    let private anglesOf (localNormal : V3f) (localPos : V4f) =
        let n = uniform.ModelViewTrafo.TransformDir localNormal |> Vec.normalize
        let viewPos = uniform.ModelViewTrafo * localPos
        let toCamera = -viewPos.XYZ |> Vec.normalize
        let l = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld |> Vec.normalize
        let i = acos (min 1.0f (max -1.0f (Vec.dot n l)))
        let e = acos (min 1.0f (max -1.0f (Vec.dot n toCamera)))
        let p = acos (min 1.0f (max -1.0f (Vec.dot l toCamera)))
        V3f(i, e, p)

    /// Brightness of the projected instrument image at this fragment, 0 outside footprint.
    [<ReflectedDefinition>]
    let private imageBrightness (projectedPos : V4f) =
        let q = projectedPos.XYZ / projectedPos.W
        let tc = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * q
        if Vec.allGreaterOrEqual tc V3f.OOO && Vec.allSmallerOrEqual tc V3f.III then
            // remap returns V4f (it also serves the false-colour path); the greyscale
            // branch replicates the value across RGB, so any channel is the brightness.
            (projectedTexture.Sample(tc.XY).X |> PRo3D.InstrumentVisualization.Shaders.remap).X
        else
            0.0f

    /// Colour = angle, brightness = the real image. The floor keeps the colour readable
    /// where the image is dark rather than crushing it to black.
    [<ReflectedDefinition>]
    let private weighted (t : float32) (projectedPos : V4f) =
        let img = imageBrightness projectedPos
        colormap t * (0.25f + 0.75f * img)

    let angleIncidence (v : AngleVertex) =
        fragment {
            let a = anglesOf v.localNormal v.localPos
            return V4f(weighted (a.X / (float32 Math.PI * 0.5f)) v.projectedPos, 1.0f)
        }

    /// Emission, with e > 90 deg painted magenta rather than clamped. Those pixels are
    /// visible facets pointing away from the camera -- geometrically impossible on a sound
    /// closed surface, so they mark where the shape model cannot be trusted. Expect them
    /// along the limb, where facets are near edge-on and mesh discretisation dominates.
    let angleEmission (v : AngleVertex) =
        fragment {
            let a = anglesOf v.localNormal v.localPos
            let half = float32 Math.PI * 0.5f
            if a.Y > half then
                let img = imageBrightness v.projectedPos
                return V4f(V3f(1.0f, 0.0f, 1.0f) * (0.25f + 0.75f * img), 1.0f)
            else
                return V4f(weighted (a.Y / half) v.projectedPos, 1.0f)
        }

    let anglePhase (v : AngleVertex) =
        fragment {
            let a = anglesOf v.localNormal v.localPos
            return V4f(weighted (a.Z / float32 Math.PI) v.projectedPos, 1.0f)
        }

    /// Diagnostic: the per-patch outward reference direction as RGB, in patch-local
    /// space. Distinguishes "the uniform never arrived" (flat mid-grey, i.e. a zero
    /// vector encoded through the 0.5 + 0.5*d mapping) from "the uniform arrived but is
    /// geometrically unreliable" (varied colour, blocky per patch).
    let debugOutward (v : Vertex) =
        fragment {
            let o = uniform.ApproximateBodyNormalLocalSpace
            let rgb = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * o
            return V4f(rgb, 1.0f)
        }

    /// Diagnostic: is the Ag ModelTrafo actually reaching the patches?
    ///
    /// FShade's uniform.ModelTrafo resolves through the same Ag semantic that
    /// OpcRenderingExtensions.captureContext reads into Context.modelTrafo, so this probes
    /// exactly the value projectionUniformMap sees. Encodes the translation column:
    /// BLACK  = translation is ~zero, i.e. the Sg.trafo is NOT being inherited
    /// COLOUR = translation is non-zero, direction encoded as RGB
    /// A secondary body placed by Sg.trafo must therefore differ from the primary.
    let debugModelTrafo (v : Vertex) =
        fragment {
            let t = V3f(uniform.ModelTrafo.M03, uniform.ModelTrafo.M13, uniform.ModelTrafo.M23)
            if t.Length < 1.0f then
                return V4f(0.0f, 0.0f, 0.0f, 1.0f)
            else
                let d = Vec.normalize t
                return V4f(V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * d, 1.0f)
        }

    /// Diagnostic: does the face normal agree with the outward reference?
    /// green = agrees (no flip needed), red = disagrees (would be flipped).
    /// Read together with debugOutward: a zero uniform also paints everything green,
    /// so this image alone cannot tell "correct" from "uniform missing".
    let debugOutwardSign (v : Vertex) =
        fragment {
            let o = uniform.ApproximateBodyNormalLocalSpace
            let agrees = Vec.dot v.localNormal o >= 0.0f
            return (if agrees then V4f(0.0f, 1.0f, 0.0f, 1.0f) else V4f(1.0f, 0.0f, 0.0f, 1.0f))
        }

    let sunDiffuseInverted (v : Vertex) =
        fragment {
            let n = uniform.ModelViewTrafo.TransformDir v.localNormal |> Vec.normalize
            let l = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld |> Vec.normalize
            let d = -(Vec.dot n l) |> max 0.0f
            let i = 0.05f + 0.95f * d
            return V4f(i, i, i, 1.0f)
        }
