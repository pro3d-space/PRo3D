namespace PRo3D.ImageMapping

open Aardvark.UI.Primitives
open Adaptify
open PRo3D.SPICE

open FSharp.Data.Adaptive

/// How a scene projects one instrument's images: its field of view, prefilled from the
/// kernels (InstrumentProjection.defaultFovs), and whether its image rows are mirrored.
/// One row of the projected-images panel's Instruments section. See
/// docs/InstrumentFieldOfView.md.
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
        /// The image files store their rows bottom-up (e.g. FITS order) while the
        /// projector expects the top row first: mirror the projection vertically.
        flipRows   : bool
    }

module InstrumentFov =

    let private degrees (v : float) =
        { Numeric.init with min = 0.001; max = 179.0; step = 0.001; format = "{0:0.0000}"; value = v }

    let create (instrument : string) (fov : InstrumentProjection.Fov) : InstrumentFov =
        { instrument = instrument; horizontal = degrees fov.horizontal; vertical = degrees fov.vertical; flipRows = false }

    let toFov (f : InstrumentFov) : InstrumentProjection.Fov =
        { horizontal = f.horizontal.value; vertical = f.vertical.value }

    /// Everything projection needs to know about this instrument.
    let toSetup (f : InstrumentFov) : InstrumentProjection.InstrumentSetup =
        { fov = toFov f; flipRows = f.flipRows }

    let kernelDefault (instrument : string) : Option<InstrumentProjection.Fov> =
        Map.tryFind instrument InstrumentProjection.defaultFovs

    /// Differs from the kernel default (or has none), or is flipped: what a scene saves.
    let isOverride (f : InstrumentFov) =
        f.flipRows ||
        match kernelDefault f.instrument with
        | Some d -> d <> toFov f
        | None -> true

    /// Every instrument with a kernel default, at that default, unflipped.
    let defaults : HashMap<string, InstrumentFov> =
        InstrumentProjection.defaultFovs
        |> Map.toList
        |> List.map (fun (name, fov) -> name, create name fov)
        |> HashMap.ofList

    /// The kernel defaults with these scene values over them.
    let withOverrides (overrides : list<string * InstrumentProjection.InstrumentSetup>) : HashMap<string, InstrumentFov> =
        overrides
        |> List.fold (fun m (name, s) -> HashMap.add name { create name s.fov with flipRows = s.flipRows } m) defaults

    /// The fields of view alone, per instrument.
    let toMap (fovs : HashMap<string, InstrumentFov>) : Map<string, InstrumentProjection.Fov> =
        fovs |> HashMap.fold (fun m name f -> Map.add name (toFov f) m) Map.empty

    /// What projection takes (Visualization.resolveProjectorWith).
    let toInstruments (fovs : HashMap<string, InstrumentFov>) : InstrumentProjection.Instruments =
        fovs |> HashMap.fold (fun m name f -> Map.add name (toSetup f) m) Map.empty

type InstrumentFovMessage =
    | SetHorizontal of instrument : string * Numeric.Action
    | SetVertical   of instrument : string * Numeric.Action
    | ToggleFlipRows of instrument : string
    /// Back to the kernel value, unflipped.
    | Reset         of instrument : string
