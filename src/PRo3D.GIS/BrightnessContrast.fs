namespace PRo3D.ImageMapping

open Aardvark.Base
open FShade

/// Brightness / contrast of a projected image (Selected Image › Properties ›
/// Brightness / Contrast; docs/MultiImageProjection.md). Acts on the image's
/// normalised value -- its Display › Range mapped to 0..1 -- before the colour map,
/// in the 2D preview and in the projection stack on the surface alike.
module BrightnessContrastShader =

    /// Contrast scales around mid grey, brightness shifts; clamped to 0..1, as the
    /// colour map lookup expects. Brightness 0 and contrast 1 leave `t` unchanged.
    [<ReflectedDefinition>]
    let apply (t : float32) (brightness : float32) (contrast : float32) : float32 =
        clamp 0.0f 1.0f ((t - 0.5f) * contrast + 0.5f + brightness)
