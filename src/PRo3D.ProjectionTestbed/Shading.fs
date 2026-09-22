namespace PRo3D.ProjectionTestbed

open System
open Aardvark.Base
open Aardvark.Rendering
open FShade

/// Plain sun-lit shading, no instrument image involved. Breaks the circularity of the
/// projected-texture comparison (render camera == projector camera lands the texture on
/// any geometry): if the sun-lit highlights line up with the real image, topography agrees.
module Shading =

    type UniformScope with
        /// Unit vector from the body towards the sun, in the render (body-fixed) frame.
        member x.SunDirectionWorld : V3f = uniform?SunDirectionWorld
        /// Per-patch outward direction (body centre -> patch), patch-local. Diagnostic only.
        member x.ApproximateBodyNormalLocalSpace : V3f = uniform?ApproximateBodyNormalLocalSpace

    type Vertex =
        {
            [<Position>] pos : V4f
            // Per-face geometric normal from generateNormal (NOT the sphere normal, which
            // would show no relief). Sign set per dataset via NormalFlip.
            [<Semantic("LocalNormal")>] localNormal : V3f
            [<Semantic("BodyLocalPos")>] localPos : V4f
        }

    /// Lambertian diffuse (no specular: regolith is not glossy). Normal and sun both taken
    /// to view space via ModelViewTrafo, so a body placed by Sg.trafo lights correctly.
    let sunDiffuse (v : Vertex) =
        fragment {
            let n = uniform.ModelViewTrafo.TransformDir v.localNormal |> Vec.normalize
            let l = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld |> Vec.normalize
            let d = Vec.dot n l |> max 0.0f
            // small ambient so the night side is distinguishable from empty space
            let i = 0.05f + 0.95f * d
            return V4f(i, i, i, 1.0f)
        }

    /// Diagnostic: face normal as RGB, sun direction in the lower-left corner. Tells a dead
    /// normal (black) from a dead sun uniform (coloured but black corner swatch).
    let debugNormal (v : Vertex) =
        fragment {
            let n = uniform.ModelViewTrafo.TransformDir v.localNormal |> Vec.normalize
            let l = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld
            // encode -1..1 into 0..1 so a zero vector reads mid-grey, not black
            let rgb = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * n
            let swatch = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * l
            let uv = v.pos.XY
            return V4f((if uv.X < 0.0f && uv.Y < 0.0f then swatch else rgb), 1.0f)
        }

    // Photometric angle backplanes -- a VISUALISATION for discussion, not a data product.
    // A real backplane needs float32 output and an illumination mask (cos(i)>0 is not "lit",
    // self-shadowing is not handled here). See plans/projectionTestbed.md.

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
            borderColor C4f.Black   // outside the footprint reads as absent, not bright
        }

    /// Blue -> cyan -> green -> yellow -> red. Hue boundaries give readable iso-angle contours.
    [<ReflectedDefinition>]
    let private colormap (t : float32) =
        let t = min 1.0f (max 0.0f t)
        if t < 0.25f then    let u = t / 0.25f              in V3f(0.0f, u, 1.0f)
        elif t < 0.5f then   let u = (t - 0.25f) / 0.25f    in V3f(0.0f, 1.0f, 1.0f - u)
        elif t < 0.75f then  let u = (t - 0.5f) / 0.25f     in V3f(u, 1.0f, 0.0f)
        else                 let u = (t - 0.75f) / 0.25f    in V3f(1.0f, 1.0f - u, 0.0f)

    /// (incidence, emission, phase) in radians, view space. Emission may exceed 90 deg (a
    /// visible facet facing away flags an inconsistent shape model) -- deliberately not clamped.
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
            // remap returns V4f (greyscale replicated across RGB), so any channel is the value
            (projectedTexture.Sample(tc.XY).X |> PRo3D.InstrumentVisualization.Shaders.remap).X
        else
            0.0f

    /// Colour = angle, brightness = the real image (with a floor so colour stays readable).
    [<ReflectedDefinition>]
    let private weighted (t : float32) (projectedPos : V4f) =
        let img = imageBrightness projectedPos
        colormap t * (0.25f + 0.75f * img)

    let angleIncidence (v : AngleVertex) =
        fragment {
            let a = anglesOf v.localNormal v.localPos
            return V4f(weighted (a.X / (float32 Math.PI * 0.5f)) v.projectedPos, 1.0f)
        }

    /// Emission, e > 90 deg painted magenta as a shape-model-inconsistency flag (expect it
    /// along the limb where facets are near edge-on).
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

    /// Diagnostic: ApproximateBodyNormalLocalSpace as RGB (mid-grey = zero/absent uniform).
    let debugOutward (v : Vertex) =
        fragment {
            let o = uniform.ApproximateBodyNormalLocalSpace
            let rgb = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * o
            return V4f(rgb, 1.0f)
        }

    /// Diagnostic: is the Ag ModelTrafo reaching the patches? Black = ~zero translation
    /// (Sg.trafo not inherited), colour = non-zero (direction encoded as RGB).
    let debugModelTrafo (v : Vertex) =
        fragment {
            let t = V3f(uniform.ModelTrafo.M03, uniform.ModelTrafo.M13, uniform.ModelTrafo.M23)
            if t.Length < 1.0f then
                return V4f(0.0f, 0.0f, 0.0f, 1.0f)
            else
                let d = Vec.normalize t
                return V4f(V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * d, 1.0f)
        }

    /// Diagnostic: green = face normal agrees with the outward reference, red = disagrees.
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
