namespace PRo3D.ImageMapping

open Aardvark.UI.Primitives
open Adaptify
open PRo3D.SPICE

open FSharp.Data.Adaptive

/// One instrument's field of view as a scene uses it for image projection: prefilled
/// from the kernels (InstrumentProjection.defaultFovs), editable in the projected-images
/// panel's Instruments section. See docs/InstrumentFieldOfView.md.
[<ModelType>]
type InstrumentFov =
    {
        /// SPICE frame name, e.g. "HERA_AFC-1" -- the key projection looks up.
        [<NonAdaptive>]
        instrument : string
        /// Full field of view along the image's width, degrees.
        horizontal : NumericInput
        /// Full field of view along the image's height, degrees.
        vertical   : NumericInput
    }

module InstrumentFov =

    let private degrees (v : float) =
        { Numeric.init with min = 0.001; max = 179.0; step = 0.001; format = "{0:0.0000}"; value = v }

    let create (instrument : string) (fov : InstrumentProjection.Fov) : InstrumentFov =
        { instrument = instrument; horizontal = degrees fov.horizontal; vertical = degrees fov.vertical }

    let toFov (f : InstrumentFov) : InstrumentProjection.Fov =
        { horizontal = f.horizontal.value; vertical = f.vertical.value }

    let kernelDefault (instrument : string) : Option<InstrumentProjection.Fov> =
        Map.tryFind instrument InstrumentProjection.defaultFovs

    /// Differs from the kernel default (or has none): what a scene saves.
    let isOverride (f : InstrumentFov) =
        match kernelDefault f.instrument with
        | Some d -> d <> toFov f
        | None -> true

    /// Every instrument with a kernel default, at that default.
    let defaults : HashMap<string, InstrumentFov> =
        InstrumentProjection.defaultFovs
        |> Map.toList
        |> List.map (fun (name, fov) -> name, create name fov)
        |> HashMap.ofList

    /// The kernel defaults with these scene values over them.
    let withOverrides (overrides : list<string * InstrumentProjection.Fov>) : HashMap<string, InstrumentFov> =
        overrides
        |> List.fold (fun m (name, fov) -> HashMap.add name (create name fov) m) defaults

    /// What projection takes (InstrumentProjection.instrumentsWith).
    let toMap (fovs : HashMap<string, InstrumentFov>) : Map<string, InstrumentProjection.Fov> =
        fovs |> HashMap.fold (fun m name f -> Map.add name (toFov f) m) Map.empty

type InstrumentFovMessage =
    | SetHorizontal of instrument : string * Numeric.Action
    | SetVertical   of instrument : string * Numeric.Action
    /// Back to the kernel value.
    | Reset         of instrument : string
