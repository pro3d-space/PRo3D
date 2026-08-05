namespace PRo3D.Base.Annotation

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Operators
open Aardvark.UI.Primitives

open Adaptify.FSharp.Core

open PRo3D
open PRo3D.Base

/// Colors annotations by an attribute instead of their own `color` field. This is a pure
/// display override: no annotation is ever written, so switching the panel off restores
/// the stored colors exactly. Mirrors how the Dip&Strike legend colors discs.
module ColorByCategory =

    let label (a : ColorCategoryAttribute) =
        match a with
        | ColorCategoryAttribute.AnnotationType    -> "Annotation type"
        | ColorCategoryAttribute.Semantic          -> "Semantic"
        | ColorCategoryAttribute.SurfaceName       -> "Surface"
        | ColorCategoryAttribute.Slope             -> "Slope"
        | ColorCategoryAttribute.Bearing           -> "Bearing"
        | ColorCategoryAttribute.Length            -> "Length"
        | ColorCategoryAttribute.WayLength         -> "Way length"
        | ColorCategoryAttribute.Height            -> "Height"
        | ColorCategoryAttribute.HeightDelta       -> "Height delta"
        | ColorCategoryAttribute.AvgAltitude       -> "Avg altitude"
        | ColorCategoryAttribute.TrueThickness     -> "True thickness"
        | ColorCategoryAttribute.VerticalThickness -> "Vertical thickness"
        | ColorCategoryAttribute.Area              -> "Area"
        | ColorCategoryAttribute.MajorDiameter     -> "Major diameter"
        | ColorCategoryAttribute.MinorDiameter     -> "Minor diameter"
        | ColorCategoryAttribute.Thickness         -> "Line thickness"
        | ColorCategoryAttribute.DipAngle          -> "Dip angle"
        | ColorCategoryAttribute.DipAzimuth        -> "Dip azimuth"
        | ColorCategoryAttribute.StrikeAzimuth     -> "Strike azimuth"
        | _                                       -> sprintf "%A" a

    let isCategorical (a : ColorCategoryAttribute) =
        match a with
        | ColorCategoryAttribute.AnnotationType
        | ColorCategoryAttribute.Semantic
        | ColorCategoryAttribute.SurfaceName -> true
        | _ -> false

    /// Azimuths wrap — 359° and 1° are one degree apart, so a linear two-color ramp puts
    /// near-identical orientations at opposite ends of the scale. These get a full-circle
    /// hue wheel instead, which ignores the ramp bounds and colors.
    let isCyclic (a : ColorCategoryAttribute) =
        match a with
        | ColorCategoryAttribute.Bearing
        | ColorCategoryAttribute.DipAzimuth
        | ColorCategoryAttribute.StrikeAzimuth -> true
        | _ -> false

    let all =
        Enum.GetValues(typeof<ColorCategoryAttribute>)
        |> unbox<ColorCategoryAttribute[]>
        |> Array.toList

    let unitOf (a : ColorCategoryAttribute) =
        match a with
        | ColorCategoryAttribute.Slope
        | ColorCategoryAttribute.Bearing
        | ColorCategoryAttribute.DipAngle
        | ColorCategoryAttribute.DipAzimuth
        | ColorCategoryAttribute.StrikeAzimuth -> "°"
        | ColorCategoryAttribute.Area          -> "m²"
        | a when isCategorical a               -> ""
        | _                                    -> "m"

    /// Qualitative palette. Annotation colors default to a sequential blue ramp, which is
    /// poor for telling categories apart, so categorical coloring uses its own set.
    let palette : C4b[] =
        [|
            C4b(  0uy, 114uy, 178uy, 255uy)
            C4b(230uy, 159uy,   0uy, 255uy)
            C4b(  0uy, 158uy, 115uy, 255uy)
            C4b(204uy, 121uy, 167uy, 255uy)
            C4b( 86uy, 180uy, 233uy, 255uy)
            C4b(213uy,  94uy,   0uy, 255uy)
            C4b(240uy, 228uy,  66uy, 255uy)
            C4b(148uy, 103uy, 189uy, 255uy)
            C4b(140uy,  86uy,  75uy, 255uy)
            C4b( 23uy, 190uy, 207uy, 255uy)
            C4b(188uy, 189uy,  34uy, 255uy)
            C4b(127uy, 127uy, 127uy, 255uy)
        |]

    /// Plain snapshot of the panel settings, so per-annotation resolution stays pure and
    /// can be hoisted out of the renderer's annotation loop.
    type Settings = {
        attribute      : ColorCategoryAttribute
        lower          : float
        upper          : float
        interval       : float
        lowerColor     : C4b
        upperColor     : C4b
        invert         : bool
        categoryColors : HashMap<string, C4b>
        noValue        : C4b
    }

    // ---------------------------------------------------------------- pure core

    let categoryKey (attr : ColorCategoryAttribute) (label : string) =
        sprintf "%A/%s" attr label

    /// `hash` may be negative and `abs Int32.MinValue` throws, hence the double modulo
    let paletteIndex (ordinal : Option<int>) (label : string) =
        let n = palette.Length
        let raw = match ordinal with | Some i -> i | None -> hash label
        ((raw % n) + n) % n

    /// explicit override if the user set one, else a deterministic palette pick
    let colorOfCategory (s : Settings) (label : string) (ordinal : Option<int>) =
        match s.categoryColors |> HashMap.tryFind (categoryKey s.attribute label) with
        | Some c -> c
        | None   -> palette.[paletteIndex ordinal label]

    let colorOfAzimuth (degrees : float) =
        let d = ((degrees % 360.0) + 360.0) % 360.0
        HSVf(float32 (d / 360.0), 1.0f, 1.0f).ToC3f().ToC4b()

    let colorOfValue (s : Settings) (v : float) =
        if Double.IsNaN v || Double.IsInfinity v then s.noValue
        elif isCyclic s.attribute then colorOfAzimuth v
        else
            FalseColorLegendApp.Draw.getColorForValue
                s.lower s.upper s.interval
                s.lowerColor s.upperColor s.invert
                v

    // -------------------------------------------------- non-adaptive extraction

    /// category label plus a stable ordinal used to pick a palette color;
    /// None means "no natural order, hash the label instead"
    let categoryOf (attr : ColorCategoryAttribute) (a : Annotation) : string * Option<int> =
        match attr with
        | ColorCategoryAttribute.AnnotationType -> sprintf "%A" a.geometry, Some (int a.geometry)
        | ColorCategoryAttribute.Semantic       -> sprintf "%A" a.semantic, Some (int a.semantic)
        | ColorCategoryAttribute.SurfaceName    -> a.surfaceName, None
        | _                                     -> "", None

    /// NaN whenever the annotation has no value for the attribute — a polyline asked for
    /// a diameter, an annotation with no planar fit asked for dip, uncomputed results
    let valueOf (attr : ColorCategoryAttribute) (a : Annotation) : float =
        let fromResults (f : AnnotationResults -> float) =
            match a.results with | Some r -> f r | None -> Double.NaN
        let fromDns (f : DipAndStrikeResults -> float) =
            match a.dnsResults with | Some r -> f r | None -> Double.NaN
        // Axis0/Axis1 are the semi-axis *vectors*, so a diameter is twice their length
        let fromEllipse (f : Ellipse2d -> float) =
            match a.ellipticResults with
            | Some r -> f r.geographicalEllipse
            | None   -> Double.NaN

        match attr with
        | ColorCategoryAttribute.Slope             -> fromResults (fun r -> r.slope)
        | ColorCategoryAttribute.Bearing           -> fromResults (fun r -> r.bearing)
        | ColorCategoryAttribute.Length            -> fromResults (fun r -> r.length)
        | ColorCategoryAttribute.WayLength         -> fromResults (fun r -> r.wayLength)
        | ColorCategoryAttribute.Height            -> fromResults (fun r -> r.height)
        | ColorCategoryAttribute.HeightDelta       -> fromResults (fun r -> r.heightDelta)
        | ColorCategoryAttribute.AvgAltitude       -> fromResults (fun r -> r.avgAltitude)
        | ColorCategoryAttribute.TrueThickness     -> fromResults (fun r -> r.trueThickness)
        | ColorCategoryAttribute.VerticalThickness -> fromResults (fun r -> r.verticalThickness)
        | ColorCategoryAttribute.Area              -> fromResults (fun r -> r.area)
        | ColorCategoryAttribute.MajorDiameter     -> fromEllipse (fun e -> 2.0 * e.Axis0.Length)
        | ColorCategoryAttribute.MinorDiameter     -> fromEllipse (fun e -> 2.0 * e.Axis1.Length)
        | ColorCategoryAttribute.Thickness         -> a.thickness.value
        | ColorCategoryAttribute.DipAngle          -> fromDns (fun r -> r.dipAngle)
        | ColorCategoryAttribute.DipAzimuth        -> fromDns (fun r -> r.dipAzimuth)
        | ColorCategoryAttribute.StrikeAzimuth     -> fromDns (fun r -> r.strikeAzimuth)
        | _                                        -> Double.NaN

    let colorOf (s : Settings) (a : Annotation) =
        if isCategorical s.attribute then
            let (label, ordinal) = categoryOf s.attribute a
            colorOfCategory s label ordinal
        else
            valueOf s.attribute a |> colorOfValue s

    /// Categories offered in the panel and the legend. Enum-backed attributes list every
    /// case so colors can be set up front; surfaces only list what is actually loaded.
    let categories (attr : ColorCategoryAttribute) (surfaceNames : seq<string>) : list<string * Option<int>> =
        match attr with
        | ColorCategoryAttribute.AnnotationType ->
            Enum.GetValues(typeof<Geometry>)
            |> unbox<Geometry[]>
            |> Array.toList
            |> List.map (fun g -> sprintf "%A" g, Some (int g))
        | ColorCategoryAttribute.Semantic ->
            Enum.GetValues(typeof<Semantic>)
            |> unbox<Semantic[]>
            |> Array.toList
            |> List.map (fun s -> sprintf "%A" s, Some (int s))
        | ColorCategoryAttribute.SurfaceName ->
            surfaceNames |> Seq.distinct |> Seq.sort |> Seq.map (fun n -> n, None) |> Seq.toList
        | _ -> []

    // ------------------------------------------------------- adaptive resolution

    let readSettings (m : AdaptiveColorByCategoryModel) (t : AdaptiveToken) : Settings =
        {
            attribute      = m.attribute.GetValue t
            lower          = m.numericLegend.lowerBound.value.GetValue t
            upper          = m.numericLegend.upperBound.value.GetValue t
            interval       = m.numericLegend.interval.value.GetValue t
            lowerColor     = m.numericLegend.lowerColor.c.GetValue t
            upperColor     = m.numericLegend.upperColor.c.GetValue t
            invert         = m.numericLegend.invertMapping.GetValue t
            categoryColors = m.categoryColors.GetValue t |> HashMap.map (fun _ (c : ColorInput) -> c.c)
            noValue        = m.noValueColor.c.GetValue t
        }

    let private categoryOfAdaptive (attr : ColorCategoryAttribute) (a : AdaptiveAnnotation) (t : AdaptiveToken) =
        match attr with
        | ColorCategoryAttribute.AnnotationType ->
            let g = a.geometry.GetValue t
            sprintf "%A" g, Some (int g)
        | ColorCategoryAttribute.Semantic ->
            let s = a.semantic.GetValue t
            sprintf "%A" s, Some (int s)
        | ColorCategoryAttribute.SurfaceName -> a.surfaceName.GetValue t, None
        | _ -> "", None

    let private valueOfAdaptive (attr : ColorCategoryAttribute) (a : AdaptiveAnnotation) (t : AdaptiveToken) =
        let fromResults (f : AdaptiveAnnotationResults -> aval<float>) =
            match a.results.GetValue t with
            | AdaptiveSome r -> (f r).GetValue t
            | _ -> Double.NaN
        let fromDns (f : AdaptiveDipAndStrikeResults -> aval<float>) =
            match a.dnsResults.GetValue t with
            | AdaptiveSome r -> (f r).GetValue t
            | _ -> Double.NaN
        // ellipticResults is a plain option, not a ModelType, so it reads directly
        let fromEllipse (f : Ellipse2d -> float) =
            match a.ellipticResults.GetValue t with
            | Some r -> f r.geographicalEllipse
            | None   -> Double.NaN

        match attr with
        | ColorCategoryAttribute.Slope             -> fromResults (fun r -> r.slope)
        | ColorCategoryAttribute.Bearing           -> fromResults (fun r -> r.bearing)
        | ColorCategoryAttribute.Length            -> fromResults (fun r -> r.length)
        | ColorCategoryAttribute.WayLength         -> fromResults (fun r -> r.wayLength)
        | ColorCategoryAttribute.Height            -> fromResults (fun r -> r.height)
        | ColorCategoryAttribute.HeightDelta       -> fromResults (fun r -> r.heightDelta)
        | ColorCategoryAttribute.AvgAltitude       -> fromResults (fun r -> r.avgAltitude)
        | ColorCategoryAttribute.TrueThickness     -> fromResults (fun r -> r.trueThickness)
        | ColorCategoryAttribute.VerticalThickness -> fromResults (fun r -> r.verticalThickness)
        | ColorCategoryAttribute.Area              -> fromResults (fun r -> r.area)
        | ColorCategoryAttribute.MajorDiameter     -> fromEllipse (fun e -> 2.0 * e.Axis0.Length)
        | ColorCategoryAttribute.MinorDiameter     -> fromEllipse (fun e -> 2.0 * e.Axis1.Length)
        | ColorCategoryAttribute.Thickness         -> a.thickness.value.GetValue t
        | ColorCategoryAttribute.DipAngle          -> fromDns (fun r -> r.dipAngle)
        | ColorCategoryAttribute.DipAzimuth        -> fromDns (fun r -> r.dipAzimuth)
        | ColorCategoryAttribute.StrikeAzimuth     -> fromDns (fun r -> r.strikeAzimuth)
        | _                                        -> Double.NaN

    /// Token-based resolution for the packed renderer. Every input is pulled with
    /// `GetValue t`, so the caller's AVal.custom picks up the dependencies and the color
    /// buffers rebuild on any panel edit — no explicit invalidation needed.
    let resolve (s : Settings) (a : AdaptiveAnnotation) (t : AdaptiveToken) : C4b =
        if isCategorical s.attribute then
            let (label, ordinal) = categoryOfAdaptive s.attribute a t
            colorOfCategory s label ordinal
        else
            valueOfAdaptive s.attribute a t |> colorOfValue s

    /// aval form, for the annotation list icons and the per-annotation SG path
    let resolveAdaptive (m : AdaptiveColorByCategoryModel) (a : AdaptiveAnnotation) : aval<C4b> =
        AVal.custom (fun t -> resolve (readSettings m t) a t)

    /// Permanently-disabled instance, for callers that do not offer the feature (OpcViewer).
    let disabled = AdaptiveColorByCategoryModel(ColorByCategoryModel.initial)

    // ------------------------------------------------------------------- update

    /// data range of the attribute, ignoring annotations that have no value for it
    let rangeOfData (attr : ColorCategoryAttribute) (annotations : seq<Annotation>) : Option<Range1d> =
        let values =
            annotations
            |> Seq.map (valueOf attr)
            |> Seq.filter (fun v -> not (Double.IsNaN v || Double.IsInfinity v))
            |> Seq.toList

        match values with
        | [] -> None
        | vs ->
            let mn, mx = List.min vs, List.max vs
            // a zero-width range would make the ramp divide by zero
            if mx - mn < 1e-9 then Some (Range1d(mn - 0.5, mx + 0.5)) else Some (Range1d(mn, mx))

    let private intervalFor (range : Range1d) : NumericInput =
        let span = range.Max - range.Min
        let step = if span > 0.0 then span / 20.0 else 1.0
        {
            value  = step
            min    = 0.0
            max    = (if span > 0.0 then span else 1.0)
            step   = step / 10.0
            format = "{0:0.0000}"
        }

    let private fitRange (annotations : seq<Annotation>) (model : ColorByCategoryModel) =
        if isCategorical model.attribute || isCyclic model.attribute then
            model
        else
            match rangeOfData model.attribute annotations with
            | None -> model
            | Some range ->
                // rebuilt rather than pushed through Numeric.update, which would clamp the
                // new value to the *old* input's min/max and silently refuse the fit
                { model with
                    numericLegend =
                        { model.numericLegend with
                            lowerBound = FalseColorsModel.initlb range
                            upperBound = FalseColorsModel.initub range
                            interval   = intervalFor range } }

    let update (annotations : seq<Annotation>) (model : ColorByCategoryModel) (act : ColorByCategoryAction) =
        match act with
        | ToggleEnabled ->
            { model with enabled = not model.enabled }
        | SetAttribute a ->
            // auto-fit so switching attribute immediately shows a useful gradient
            { model with attribute = a } |> fitRange annotations
        | LegendMessage msg ->
            { model with numericLegend = FalseColorLegendApp.update model.numericLegend msg }
        | SetCategoryColor (key, msg) ->
            let current =
                model.categoryColors
                |> HashMap.tryFind key
                |> Option.defaultValue ({ c = C4b.White } : ColorInput)
            { model with categoryColors = model.categoryColors |> HashMap.add key (ColorPicker.update current msg) }
        | SetNoValueColor msg ->
            { model with noValueColor = ColorPicker.update model.noValueColor msg }
        | FitRangeToData ->
            fitRange annotations model
        | ResetCategoryColors ->
            // only the current attribute's overrides; other attributes keep theirs
            let prefix = sprintf "%A/" model.attribute
            { model with
                categoryColors = model.categoryColors |> HashMap.filter (fun k _ -> not (k.StartsWith prefix)) }

    // --------------------------------------------------------------------- view

    /// distinct surface names across the loaded annotations
    let private surfaceNamesOf (annotations : aset<AdaptiveAnnotation>) =
        AVal.custom (fun t ->
            annotations.Content.GetValue t
            |> HashSet.toList
            |> List.map (fun a -> a.surfaceName.GetValue t)
            |> List.distinct
            |> List.sort)

    /// A category's color is synthesised (explicit override, else palette fallback) rather
    /// than stored per category, so there is no AdaptiveColorInput to hand the picker —
    /// wrap the current value in a fresh one. The row is rebuilt when the map changes,
    /// which is acceptable for a settings panel.
    let private colorPickerOf (paletteFile : string) (storageKey : string) (current : C4b) =
        ColorPicker.viewAdvanced
            ColorPicker.defaultPalette paletteFile storageKey true
            (Aardvark.UI.AdaptiveColorInput({ c = current }))

    let private tinyButton (caption : string) (msg : ColorByCategoryAction) =
        div [ style "padding: 4px 0px" ] [
            div [ clazz "ui tiny button"; onClick (fun _ -> msg) ] [ text caption ]
        ]

    let view (paletteFile : string) (annotations : aset<AdaptiveAnnotation>) (m : AdaptiveColorByCategoryModel) =
        let noValueRow =
            Html.row "no value:" [
                ColorPicker.viewAdvanced ColorPicker.defaultPalette paletteFile "pro3dCbcNoValue" true m.noValueColor
                |> UI.map SetNoValueColor
            ]

        let body =
            alist {
                let! attr = m.attribute

                if isCategorical attr then
                    let! names  = surfaceNamesOf annotations
                    let! colors = m.categoryColors
                    let cats = categories attr names

                    if List.isEmpty cats then
                        yield div [ style "font-style:italic; padding:5px" ] [ text "no categories to show" ]
                    else
                        yield Html.table [
                            for (lbl, ordinal) in cats do
                                let key = categoryKey attr lbl
                                let current =
                                    colors
                                    |> HashMap.tryFind key
                                    |> Option.map (fun (c : ColorInput) -> c.c)
                                    |> Option.defaultValue palette.[paletteIndex ordinal lbl]
                                yield Html.row lbl [
                                    colorPickerOf paletteFile (sprintf "pro3dCbc%d" (paletteIndex ordinal lbl)) current
                                    |> UI.map (fun a -> SetCategoryColor(key, a))
                                ]
                            yield noValueRow
                        ]
                        yield tinyButton "reset colors" ResetCategoryColors

                elif isCyclic attr then
                    // the ramp bounds and colors do not apply to a hue wheel, so only the
                    // legend toggle and the no-value color are offered
                    yield Html.table [
                        Html.row "show legend:" [
                            GuiEx.iconCheckBox m.numericLegend.useFalseColors (LegendMessage FalseColorLegendApp.UseFalseColors)
                        ]
                        noValueRow
                    ]
                    yield div [ style "font-style:italic; padding:5px" ] [
                        text (sprintf "%s wraps at 360°, so it is colored on a hue wheel." (label attr))
                    ]

                else
                    yield FalseColorLegendApp.UI.viewScalarMappingProperties paletteFile m.numericLegend
                          |> UI.map LegendMessage
                    yield Html.table [ noValueRow ]
                    yield tinyButton "fit range to data" FitRangeToData
            }

        require GuiEx.semui (
            Incremental.div AttributeMap.empty (
                alist {
                    yield Html.table [
                        Html.row "enable:"    [ GuiEx.iconCheckBox m.enabled ToggleEnabled ]
                        Html.row "attribute:" [
                            Html.SemUi.dropDown' (AList.ofList all) m.attribute SetAttribute label
                        ]
                    ]
                    yield! body
                }
            )
        )

    // ------------------------------------------------------------------- legend

    module Draw =

        let private plate (heightPx : int) =
            Svg.rect [
                "fill"         => "#EEEEEE"
                "width"        => "150px"
                "height"       => sprintf "%dpx" heightPx
                "x"            => "8px"
                "y"            => "4px"
                "rx"           => "5"
                "ry"           => "5"
                "stroke"       => "black"
                "stroke-width" => "1px"
                "opacity"      => "0.5"
            ]

        let private caption (attr : ColorCategoryAttribute) =
            let u = unitOf attr
            if u = "" then label attr else sprintf "%s [%s]" (label attr) u

        /// Numeric ramp -> the existing false-color bar; azimuths -> a hue wheel strip;
        /// categories -> a swatch list. Gated on `enabled` plus the panel's show-legend
        /// toggle, which is `numericLegend.useFalseColors`.
        let legend (annotations : aset<AdaptiveAnnotation>) (m : AdaptiveColorByCategoryModel) =
            alist {
                let! enabled = m.enabled
                let! show    = m.numericLegend.useFalseColors

                if enabled && show then
                    let! attr = m.attribute

                    if isCategorical attr then
                        let! names  = surfaceNamesOf annotations
                        let! colors = m.categoryColors
                        let cats = categories attr names

                        yield plate (24 + 16 * cats.Length)
                        yield Svg.text
                                [ "x" => "16px"; "y" => "20px"; "font-size" => "11"
                                  "fill" => "#ffffff"; "pointer-events" => "none" ]
                                (caption attr)

                        for (i, (lbl, ordinal)) in List.indexed cats do
                            let c =
                                colors
                                |> HashMap.tryFind (categoryKey attr lbl)
                                |> Option.map (fun (x : ColorInput) -> x.c)
                                |> Option.defaultValue palette.[paletteIndex ordinal lbl]
                            let y = 28 + 16 * i
                            yield Svg.rect [
                                "fill"         => sprintf "rgb(%i,%i,%i)" c.R c.G c.B
                                "width"        => "12px"
                                "height"       => "12px"
                                "x"            => "16px"
                                "y"            => sprintf "%dpx" y
                                "stroke"       => "white"
                                "stroke-width" => "1px"
                                "rx"           => "2"
                                "ry"           => "2"
                            ]
                            yield Svg.text
                                    [ "x" => "34px"; "y" => sprintf "%dpx" (y + 10)
                                      "font-size" => "10"; "fill" => "#ffffff"
                                      "pointer-events" => "none" ]
                                    lbl

                    elif isCyclic attr then
                        let gradientId = "ColorByCategoryCyclicLegend"
                        let stops =
                            [ 0 .. 12 ]
                            |> List.map (fun i ->
                                let f = float i / 12.0
                                // top of the bar is 360°, bottom 0°, matching the numeric bar
                                let c = colorOfAzimuth ((1.0 - f) * 360.0)
                                FalseColorLegendApp.Draw.buildSvgStop (float32 f) (c.ToC3b()))
                            |> AList.ofList

                        yield Svg.defs [] [
                            onBoot ("$('#__ID__').attr('id','" + gradientId + "')") (
                                Incremental.Svg.linearGradient
                                    (AttributeMap.ofList [
                                        "x1" => "0%"; "y1" => "0%"
                                        "x2" => "0%"; "y2" => "100%"
                                        "pointer-events" => "none" ])
                                    stops
                            )]

                        yield Svg.rect [
                            "fill"         => "#EEEEEE"
                            "width"        => "60px"
                            "height"       => "98%"
                            "x"            => "8px"
                            "y"            => "1.75%"
                            "rx"           => "5"
                            "ry"           => "5"
                            "stroke"       => "black"
                            "stroke-width" => "1px"
                            "opacity"      => "0.5"
                        ]
                        yield Svg.rect [
                            "style"        => "fill:url(#" + gradientId + ")"
                            "width"        => "10px"
                            "height"       => "90%"
                            "x"            => "12px"
                            "y"            => "6%"
                            "stroke"       => "white"
                            "stroke-width" => "1px"
                            "rx"           => "3"
                            "ry"           => "3"
                        ]
                        yield Svg.text
                                [ "x" => "10px"; "y" => "4%"; "font-size" => "10"
                                  "fill" => "#ffffff"; "pointer-events" => "none" ]
                                (caption attr)

                        for i in 0 .. 4 do
                            let deg = 360.0 - 90.0 * float i
                            let y = 6.0 + 90.0 * (float i / 4.0)
                            yield Svg.text
                                    [ "x" => "25px"; "y" => sprintf "%f%%" y
                                      "font-size" => "10"; "fill" => "#ffffff"
                                      "pointer-events" => "none" ]
                                    (sprintf "%.0f°" deg)

                    else
                        yield! FalseColorLegendApp.Draw.createFalseColorLegendBasics
                                    "ColorByCategoryLegend" m.numericLegend
            }
