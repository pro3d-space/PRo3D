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

    let private row (instrument : string) (fov : AdaptiveInstrumentFov) =
        let changed =
            fov.Current |> AVal.map InstrumentFov.isOverride
        tr [] [
            // "*": changed in this scene, i.e. saved with it
            td [attribute "title" (kernelText instrument + "; * = changed in this scene"); style "white-space: nowrap"] [
                text instrument
                span [style "color: #fbbd08"] [ Incremental.text (changed |> AVal.map (fun c -> if c then " *" else "")) ]
            ]
            td [] [ Numeric.view' [NumericInputType.InputBox] fov.horizontal |> UI.map (fun a -> SetHorizontal (instrument, a)) ]
            td [] [ Numeric.view' [NumericInputType.InputBox] fov.vertical   |> UI.map (fun a -> SetVertical (instrument, a)) ]
            td [] [
                button [clazz "ui button mini"; attribute "title" ("Back to the " + kernelText instrument); onClick (fun _ -> Reset instrument)] [
                    i [clazz "undo icon"] []
                ]
            ]
        ]

    /// Rows sorted by instrument name; FOV in degrees, horizontal = along the image width.
    let view (fovs : amap<string, AdaptiveInstrumentFov>) : DomNode<InstrumentFovMessage> =
        let rows =
            fovs
            |> AMap.toASet
            |> ASet.sortBy fst
            |> AList.map (fun (name, fov) -> row name fov)
        div [style "padding: 5px"] [
            table [clazz "ui inverted compact table"; style "background: transparent"] [
                thead [] [
                    tr [] [
                        th [] [ text "Instrument" ]
                        th [attribute "title" "Full field of view along the image width, degrees"] [ text "H FOV (deg)" ]
                        th [attribute "title" "Full field of view along the image height, degrees"] [ text "V FOV (deg)" ]
                        th [] []
                    ]
                ]
                Incremental.tbody AttributeMap.empty rows
            ]
            div [style "font-size: smaller; opacity: 0.7"] [
                text "Prefilled from the SPICE instrument kernels. Saved with the scene only where changed."
            ]
        ]
