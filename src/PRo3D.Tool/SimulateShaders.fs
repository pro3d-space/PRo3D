module PRo3D.Tool.SimulateShaders

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Rendering.Effects
open FShade

open PRo3D.SPICE.Shaders   // UniformScope.SunDirectionWorld

// Shaders for the simulate-image verb: Lommel-Seeliger sun lighting over a de-shaded
// texture albedo, procedural micro-structure, and a sun shadow map.
//
// Composed after the standard OPC scaffold (stableImageProjectionTrafo, generateNormal,
// applyNormalFlip), which supplies BodyLocalPos and the per-face LocalNormal. Everything
// here works from patch-local / body-fixed coordinates -- on a body a few hundred metres
// across those stay comfortably inside float32, unlike the planetary-scale world
// coordinates the precision rules forbid in shaders.

type UniformScope with
    /// Body-fixed world -> sun-camera clip space, for the shadow-map lookup.
    member x.SunShadowViewProj : M44f = uniform?SunShadowViewProj
    member x.SunShadowEnabled : bool = uniform?SunShadowEnabled
    /// Depth bias in normalized shadow-map depth, subtracted from the reference depth.
    member x.SunShadowBias : float32 = uniform?SunShadowBias
    /// Divide the baked illumination out of the texture (true) or ignore the texture and
    /// use AlbedoConst (false).
    member x.DeshadeEnabled : bool = uniform?DeshadeEnabled
    /// Use the texture as albedo WITHOUT dividing its baked illumination out. Ignored
    /// when DeshadeEnabled. This is the naive thing to do, on purpose -- it is the frame
    /// a reconstruction is compared against to find out whether de-lighting was necessary.
    member x.TextureAlbedo : bool = uniform?TextureAlbedo
    /// Fitted direction of the illumination baked into the texture, body-fixed frame.
    member x.BakedSunDirection : V3f = uniform?BakedSunDirection
    /// Maps the de-shaded texture value (texture / cos incidence) to normal reflectance.
    member x.DeshadeScale : float32 = uniform?DeshadeScale
    /// Maps the RAW texture value to normal reflectance, with no such division. Set so the
    /// mean lit texel comes out at AlbedoConst, because the overall level of the
    /// un-de-shaded variant is a radiometric calibration, not part of what it demonstrates.
    member x.RawTextureScale : float32 = uniform?RawTextureScale
    /// Texture values at or below this are treated as shadowed/nodata in the source
    /// mosaic; the de-shade division is meaningless there.
    member x.DeshadeShadowFloor : float32 = uniform?DeshadeShadowFloor
    member x.AlbedoConst : float32 = uniform?AlbedoConst
    /// Feature size of the micro-structure noise, metres.
    member x.MicroScale : float32 = uniform?MicroScale
    /// Strength of the normal perturbation; 0 disables.
    member x.MicroAmplitude : float32 = uniform?MicroAmplitude
    member x.AmbientFloor : float32 = uniform?AmbientFloor
    /// Emit a flat disk instead of a shaded body: every fragment on the surface returns
    /// the same value, so the image IS the silhouette. For geometric comparisons, where
    /// shading only obscures the outline that carries the pointing information.
    member x.NoLighting : bool = uniform?NoLighting
    /// Emit the OPC's own texture, unlit and un-deshaded: the mosaic as this camera
    /// sees it. Distinct from the de-shading, which FITS a light direction and divides
    /// it out -- an approximation with a confidence weight and a constant-albedo
    /// fallback, none of which belongs in an image meant as a registration reference.
    member x.TextureOnly : bool = uniform?TextureOnly

type SimVertex =
    {
        [<Position>] pos : V4f
        [<TexCoord>] tc : V2f
        [<Semantic("LocalNormal")>] localNormal : V3f
        [<Semantic("BodyLocalPos")>] localPos : V4f
        /// Fragment position in sun-camera clip space, stashed before stableTrafo.
        [<Semantic("SunShadowNdc")>] shadowPos : V4f
    }

/// The OPC patch's own diffuse texture (for Dimorphos_DRACO1: the DRACO mosaic).
let private diffuseSampler =
    sampler2d {
        texture uniform?DiffuseColorTexture
        filter Filter.MinMagMipLinear
        addressU WrapMode.Clamp
        addressV WrapMode.Clamp
    }

let private sunShadowSampler =
    sampler2dShadow {
        texture uniform?SunShadowMap
        filter Filter.MinMagLinear
        addressU WrapMode.Border
        addressV WrapMode.Border
        borderColor C4f.White
        comparison ComparisonFunction.LessOrEqual
    }

/// Stash the fragment's sun-camera clip position while [<Position>] still holds the
/// patch-local coordinate, i.e. this must precede stableTrafo -- same rule as
/// stableImageProjectionTrafo, and for the same reason.
let stashSunShadowPos (v : SimVertex) =
    vertex {
        return { v with shadowPos = uniform.SunShadowViewProj * (uniform.ModelTrafo * v.pos) }
    }

// --- value noise ------------------------------------------------------------------
// Hash-based 3D value noise, no textures and no tangent frames required. The classic
// sin-dot hash loses quality at large arguments, but a hash only has to look random;
// visual inspection at body scale shows no structure.

[<ReflectedDefinition>]
let private hash3 (p : V3f) : float32 =
    let h = Vec.dot p (V3f(127.1f, 311.7f, 74.7f))
    let s = sin h * 43758.547f
    s - floor s

[<ReflectedDefinition>]
let private valueNoise (p : V3f) : float32 =
    let i = V3f(floor p.X, floor p.Y, floor p.Z)
    let f = p - i
    // smoothstep weights, so the gradient is continuous at cell boundaries
    let u = f * f * (V3f(3.0f, 3.0f, 3.0f) - 2.0f * f)
    let n000 = hash3 i
    let n100 = hash3 (i + V3f(1.0f, 0.0f, 0.0f))
    let n010 = hash3 (i + V3f(0.0f, 1.0f, 0.0f))
    let n110 = hash3 (i + V3f(1.0f, 1.0f, 0.0f))
    let n001 = hash3 (i + V3f(0.0f, 0.0f, 1.0f))
    let n101 = hash3 (i + V3f(1.0f, 0.0f, 1.0f))
    let n011 = hash3 (i + V3f(0.0f, 1.0f, 1.0f))
    let n111 = hash3 (i + V3f(1.0f, 1.0f, 1.0f))
    let nx00 = n000 * (1.0f - u.X) + n100 * u.X
    let nx10 = n010 * (1.0f - u.X) + n110 * u.X
    let nx01 = n001 * (1.0f - u.X) + n101 * u.X
    let nx11 = n011 * (1.0f - u.X) + n111 * u.X
    let nxy0 = nx00 * (1.0f - u.Y) + nx10 * u.Y
    let nxy1 = nx01 * (1.0f - u.Y) + nx11 * u.Y
    nxy0 * (1.0f - u.Z) + nxy1 * u.Z

/// Four octaves, unrolled. The lacunarity is deliberately not exactly 2 and each octave
/// is offset, so octave lattices do not align and produce visible axis-parallel structure.
[<ReflectedDefinition>]
let private fbm (p : V3f) : float32 =
    0.5f      * valueNoise p
    + 0.25f   * valueNoise (p * 2.03f + V3f(17.3f, 9.1f, 4.7f))
    + 0.125f  * valueNoise (p * 4.11f + V3f(31.7f, 2.9f, 11.3f))
    + 0.0625f * valueNoise (p * 8.19f + V3f(5.3f, 23.1f, 7.7f))

/// Simulated instrument image: I/F per fragment, greyscale in RGB, alpha = coverage.
///
/// Photometry is Lommel-Seeliger with a 5% Lambert admixture, per Li et al. 2024
/// (PSJ, doi:10.3847/PSJ/ad2b60): Dimorphos follows a lunar-like (LS) function with
/// minimal multiple scattering. Both disk functions are normalized so that i = e = 0
/// returns the albedo, i.e. "albedo" throughout is normal reflectance. The phase-angle
/// factor f(alpha) is intentionally omitted -- it is constant across one image and the
/// PNG is exposure-scaled anyway.
let simulatedImage (v : SimVertex) =
    fragment {
        // Flat mode short-circuits everything below: no normal, no albedo, no sun. The
        // fragment exists, therefore it is body -- which is exactly what a silhouette
        // comparison needs, and it removes the terminator, the texture and the
        // micro-structure as sources of disagreement.
        if uniform.NoLighting then
            return V4f(1.0f, 1.0f, 1.0f, 1.0f)
        elif uniform.TextureOnly then
            let t = diffuseSampler.Sample(v.tc).X
            return V4f(t, t, t, 1.0f)
        else

        // Smooth per-face normal and position in the body-fixed frame. ModelTrafo is the
        // patch's Local2Global; evaluating the noise in body coordinates keeps it
        // continuous across patch borders regardless of per-patch local frames.
        let nSmooth = uniform.ModelTrafo.TransformDir v.localNormal |> Vec.normalize
        let pBody = uniform.ModelTrafo.TransformPos v.localPos.XYZ

        // viewing geometry first: the micro-structure amplitude is faded where the SMOOTH
        // surface is already seen edge-on. There a perturbed normal flips between facing
        // and not facing the camera pixel by pixel, and with the Lommel-Seeliger disk
        // function diverging towards grazing emission that renders as isolated clipped
        // white speckles along the limb -- aliasing, not roughness.
        let viewPos = uniform.ModelViewTrafo * v.localPos
        let toCam = -viewPos.XYZ |> Vec.normalize
        let nSmoothView = uniform.ViewTrafo.TransformDir nSmooth |> Vec.normalize
        let muSmooth = Vec.dot nSmoothView toCam |> max 0.0f
        let limbFade = clamp 0.0f 1.0f (4.0f * muSmooth)

        // micro-structure: tangent-free bump mapping. The fBm gradient, projected into
        // the tangent plane, tilts the shading normal; silhouettes and real occlusion are
        // unaffected (documented limitation).
        let amplitude = uniform.MicroAmplitude * limbFade
        let nBody =
            if amplitude > 0.0f then
                let q = pBody / uniform.MicroScale
                let e = 0.25f
                let gx = fbm (q + V3f(e, 0.0f, 0.0f)) - fbm (q - V3f(e, 0.0f, 0.0f))
                let gy = fbm (q + V3f(0.0f, e, 0.0f)) - fbm (q - V3f(0.0f, e, 0.0f))
                let gz = fbm (q + V3f(0.0f, 0.0f, e)) - fbm (q - V3f(0.0f, 0.0f, e))
                let g = V3f(gx, gy, gz) / (2.0f * e)
                let gT = g - nSmooth * Vec.dot g nSmooth
                Vec.normalize (nSmooth - amplitude * gT)
            else
                nSmooth

        // albedo: divide the baked illumination out of the texture. The divisor uses the
        // SMOOTH normal -- the baked shading happened on the real (smooth-at-this-scale)
        // surface, not on our procedural detail. Where the source mosaic is shadowed or
        // near its own terminator the division is ill-conditioned; fall back to the
        // constant albedo there rather than amplifying noise.
        let texVal = diffuseSampler.Sample(v.tc).X
        let albedo =
            if uniform.DeshadeEnabled then
                let muBake = Vec.dot nSmooth uniform.BakedSunDirection
                // Trustworthiness of the de-shaded value, 0..1: fades out where the source
                // mosaic is dark (shadow/nodata/unseen hemisphere) and where the baked
                // illumination was grazing. A smooth blend, not a threshold -- a hard
                // fallback boundary renders as blocky patchwork across the disk.
                let wTex = smoothstep uniform.DeshadeShadowFloor (2.0f * uniform.DeshadeShadowFloor) texVal
                let wMu = smoothstep 0.15f 0.35f muBake
                let w = wTex * wMu
                // The de-shaded value as a ratio to the nominal albedo, sqrt-compressed:
                // the division systematically under-corrects on the mosaic's well-lit side
                // (Lambert divisor on LS radiance, fitted direction), leaving a broad
                // bright tail that reads as an overlit region after exposure. sqrt pulls
                // 2x down to 1.4x while keeping mid-tones. Then clamp to [0.5x, 2x] --
                // real albedo variation on Dimorphos is small (Li et al. 2024), so
                // anything outside that band is the division misfiring, not surface.
                // Both operations are continuous, so they introduce no edges.
                let ratio = texVal / max 0.15f muBake * uniform.DeshadeScale / uniform.AlbedoConst
                let deshaded =
                    uniform.AlbedoConst * clamp 0.5f 2.0f (sqrt (max 0.0f ratio))
                // Low confidence used to fall back to the flat AlbedoConst, which threw the
                // texture away entirely -- on this body that erased the surface over ~half
                // the disk wherever the BAKED illumination was grazing. Fall back to the
                // texture instead, normalised through the same scale at a fixed mid
                // incidence: it keeps the detail and merely leaves the residual baked
                // shading in, which is the smaller error of the two.
                let plainRatio = texVal / 0.5f * uniform.DeshadeScale / uniform.AlbedoConst
                let plain =
                    uniform.AlbedoConst * clamp 0.5f 2.0f (sqrt (max 0.0f plainRatio))
                plain + w * (deshaded - plain)
            elif uniform.TextureAlbedo then
                // The texture as albedo with NO division: its baked illumination stays in
                // and this frame's sun lights it a second time. Deliberately the naive
                // rendering -- whether de-lighting is necessary is an empirical question
                // about a given reconstruction, and it cannot be asked without the frame
                // that skips the step.
                //
                // Straight through RawTextureScale, with none of the sqrt compression or
                // [0.5x, 2x] clamping the branch above needs: those guard an ill-conditioned
                // DIVISION, and there is no division here. What is kept is the overall
                // level -- the mean lit texel maps to AlbedoConst -- so this frame and the
                // de-lit one differ in the SPATIAL PATTERN of albedo, which is the thing
                // under test, and not in exposure. Borrowing the de-shaded scale instead
                // came out ~1.8x too bright and clipped 55-75 % of the body.
                //
                // The wide clamp only keeps a nodata texel from producing a non-finite
                // albedo; on real data it does not bind.
                clamp 0.0f (4.0f * uniform.AlbedoConst) (texVal * uniform.RawTextureScale)
            else
                uniform.AlbedoConst

        // lighting in view space (precision rule: local -> view, never through world).
        // mu is floored at a small grazing value rather than epsilon: the LS disk term
        // reaches 2x at mu -> 0, and letting single perturbed-normal pixels get there
        // produces clipped speckles instead of the (real) gentle limb brightening.
        let n = uniform.ViewTrafo.TransformDir nBody |> Vec.normalize
        let l = uniform.ViewTrafo.TransformDir uniform.SunDirectionWorld |> Vec.normalize
        let mu0 = Vec.dot n l |> max 0.0f
        let mu = Vec.dot n toCam |> max 0.02f
        let disk = 0.95f * (2.0f * mu0 / (mu0 + mu)) + 0.05f * mu0

        // cast shadows from the sun depth map (2x2 PCF). Outside the map counts as lit;
        // the ortho frustum covers the whole body, so that only happens off the body.
        let shadow =
            if uniform.SunShadowEnabled then
                let p = v.shadowPos.XYZ / v.shadowPos.W
                let tc = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * p
                if tc.X < 0.0f || tc.X > 1.0f || tc.Y < 0.0f || tc.Y > 1.0f then 1.0f
                else
                    let r = 1.5f / float32 (Vec.MaxElement sunShadowSampler.Size)
                    let z = tc.Z - uniform.SunShadowBias
                    (sunShadowSampler.Sample(tc.XY + V2f(-r, -r), z)
                     + sunShadowSampler.Sample(tc.XY + V2f(r, -r), z)
                     + sunShadowSampler.Sample(tc.XY + V2f(-r, r), z)
                     + sunShadowSampler.Sample(tc.XY + V2f(r, r), z)) * 0.25f
            else
                1.0f

        let lit = albedo * disk * shadow
        let iOverF = uniform.AmbientFloor * albedo + (1.0f - uniform.AmbientFloor) * lit
        return V4f(iOverF, iOverF, iOverF, 1.0f)
    }
