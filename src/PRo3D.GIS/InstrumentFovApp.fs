namespace PRo3D.ImageMapping

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base

open FSharp.Data.Adaptive

/// The projected-images panel's Instruments section: per instrument, the field of view
/// image projection uses, prefilled from the kernels. See docs/InstrumentFieldOfView.md.
module InstrumentFovApp =

    let private updateOne (instrument : string) (f : InstrumentFov -> InstrumentFov) (fovs : HashMap<string, InstrumentFov>) =
        match HashMap.tryFind instrument fovs with
        | Some fov -> HashMap.add instrument (f fov) fovs
        | None -> fovs

    let update (fovs : HashMap<string, InstrumentFov>) (msg : InstrumentFovMessage) : HashMap<string, InstrumentFov> =
        match msg with
        | SetHorizontal (instrument, a) ->
            fovs |> updateOne instrument (fun f -> { f with horizontal = Numeric.update f.horizontal a })
        | ToggleFlipRows instrument ->
            fovs |> updateOne instrument (fun f -> { f with flipRows = not f.flipRows })
        | SetVertical (instrument, a) ->
            fovs |> updateOne instrument (fun f -> { f with vertical = Numeric.update f.vertical a })
        | Reset instrument ->
            match InstrumentFov.kernelDefault instrument with
            | Some d -> fovs |> updateOne instrument (fun _ -> InstrumentFov.create instrument d)
            | None -> fovs

    let private kernelText (instrument : string) =
        match InstrumentFov.kernelDefault instrument with
        | Some d -> sprintf "kernel: %g x %g deg" d.horizontal d.vertical
        | None -> "no kernel default"

    /// A narrow number box: the panel is narrow, and two angles share one line.
    let private angleBox (value : AdaptiveNumericInput) (msg : Numeric.Action -> InstrumentFovMessage) =
        Numeric.numericField (msg >> Seq.singleton)
            (AttributeMap.ofList [ style "width: 62px; padding: 1px 4px; text-align: right; color: black" ])
            value NumericInputType.InputBox

    let private flipTitle =
        "Flip rows: mirror this instrument's images vertically, for files whose first row is the bottom of the frame (e.g. FITS order)"

    /// One line per instrument: name, H x V field of view, flip, reset.
    let private row (instrument : string) (fov : AdaptiveInstrumentFov) =
        let changed =
            fov.Current |> AVal.map InstrumentFov.isOverride
        div [style "display: flex; align-items: center; gap: 4px; padding: 2px 0; white-space: nowrap"] [
            // "*": changed in this scene, i.e. saved with it
            span [attribute "title" (kernelText instrument + "; * = changed in this scene")
                  style "flex: 1 1 auto; overflow: hidden; text-overflow: ellipsis"] [
                text instrument
                span [style "color: #fbbd08"] [ Incremental.text (changed |> AVal.map (fun c -> if c then " *" else "")) ]
            ]
            span [attribute "title" "Full field of view along the image width x height, degrees"] [
                angleBox fov.horizontal (fun a -> SetHorizontal (instrument, a))
                text " × "
                angleBox fov.vertical (fun a -> SetVertical (instrument, a))
                text "°"
            ]
            span [attribute "title" flipTitle; style "margin-left: 6px"] [
                GuiEx.iconCheckBox fov.flipRows (ToggleFlipRows instrument)
                text "flip"
            ]
            i [clazz "undo icon"; style "cursor: pointer; margin-left: 4px; opacity: 0.7"
               attribute "title" ("Back to the " + kernelText instrument + ", unflipped"); onClick (fun _ -> Reset instrument)] []
        ]

    /// Rows sorted by instrument name; FOV in degrees, horizontal = along the image width.
    let view (fovs : amap<string, AdaptiveInstrumentFov>) : DomNode<InstrumentFovMessage> =
        let rows =
            fovs
            |> AMap.toASet
            |> ASet.sortBy fst
            |> AList.map (fun (name, fov) -> row name fov)
        div [style "padding: 4px 6px"] [
            Incremental.div AttributeMap.empty rows
            div [style "font-size: smaller; opacity: 0.7; margin-top: 4px"] [
                text "Field of view H × V from the SPICE instrument kernels; saved with the scene only where changed. "
                text "flip = image rows stored bottom-up."
            ]
        ]
