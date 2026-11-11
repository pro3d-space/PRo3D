namespace PRo3D.ImageMapping

open System

open FSharp.Data.Adaptive

open Aardvark.UI
open Aardvark.UI.Primitives

/// Two-handle range slider: two range inputs over one track, the lower and the upper
/// bound of a range (styles: .pro3d-dual-range in semui-overrides.css). Used by the
/// Projected Images library filters and by an image's display range (Properties ›
/// Display). A bound is sent on release (onchange) -- the browser moves the thumb on
/// its own while dragging, so the update thread is not flooded.
module DualRangeSlider =

    let private invariant (v : float) = v.ToString("R", Globalization.CultureInfo.InvariantCulture)

    /// `active` colours the span between the thumbs (amber: a filter that hides rows);
    /// `nop` is sent for an unparsable value.
    let view (lower : AdaptiveNumericInput) (upper : AdaptiveNumericInput) (active : aval<bool>)
             (setLower : float -> 'msg) (setUpper : float -> 'msg) (nop : 'msg) : DomNode<'msg> =
        let thumb (input : AdaptiveNumericInput) (isLower : bool) =
            let attributes =
                amap {
                    yield attribute "type" "range"
                    let! min = input.min
                    let! max = input.max
                    let! step = input.step
                    let! value = input.value
                    yield attribute "min" (invariant min)
                    yield attribute "max" (invariant max)
                    yield attribute "step" (invariant step)
                    yield attribute "value" (invariant value)
                    // a lower thumb pushed to the far right would sit under the
                    // upper one and could never be dragged back: lift it then
                    let! upperValue = upper.value
                    if isLower && value > min + 0.5 * (max - min) && value >= upperValue then
                        yield style "z-index: 2"
                    yield onChange (fun s ->
                        match Double.TryParse(s.Trim('"'), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                        | true, v -> if isLower then setLower v else setUpper v
                        | _ -> nop)
                } |> AttributeMap.ofAMap
            Incremental.input attributes

        let fillAttributes =
            amap {
                let! lowerValue = lower.value
                let! upperValue = upper.value
                let! min = lower.min
                let! max = upper.max
                let span = if max > min then max - min else 1.0
                let left = 100.0 * (lowerValue - min) / span
                let width = 100.0 * (upperValue - lowerValue) / span
                yield clazz "pro3d-dual-range-fill"
                yield style (sprintf "left: %.3f%%; width: %.3f%%" left width)
            } |> AttributeMap.ofAMap

        let containerAttributes =
            amap {
                let! active = active
                yield clazz (if active then "pro3d-dual-range active" else "pro3d-dual-range")
            } |> AttributeMap.ofAMap

        Incremental.div containerAttributes (
            AList.ofList [
                div [clazz "pro3d-dual-range-track"] []
                Incremental.div fillAttributes AList.empty
                thumb lower true
                thumb upper false
            ])
