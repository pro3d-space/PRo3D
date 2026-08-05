namespace PRo3D.Base.Annotation

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D
open PRo3D.Base

open Chiron
open Adaptify

#nowarn "0686"

/// Attribute the annotation set can be colored by. Values are persisted, so existing
/// cases must keep their numbers; new ones get appended.
type ColorCategoryAttribute =
    // categorical
    | AnnotationType    = 0
    | Semantic          = 1
    | SurfaceName       = 2
    // numeric, from AnnotationResults
    | Slope             = 3
    | Bearing           = 4
    | Length            = 5
    | WayLength         = 6
    | Height            = 7
    | HeightDelta       = 8
    | AvgAltitude       = 9
    | TrueThickness     = 10
    | VerticalThickness = 11
    | Area              = 12
    // numeric, from ellipticResults / set on the annotation itself
    | MajorDiameter     = 13
    | MinorDiameter     = 14
    | Thickness         = 15
    // numeric, from DipAndStrikeResults
    | DipAngle          = 16
    | DipAzimuth        = 17
    | StrikeAzimuth     = 18

/// Action lives here rather than in the app module because DrawingAction
/// (Drawing-Model.fs) has to reference it and that file compiles first —
/// same arrangement as FalseColorLegendApp.Action.
type ColorByCategoryAction =
    | ToggleEnabled
    | SetAttribute      of ColorCategoryAttribute
    | LegendMessage     of FalseColorLegendApp.Action
    | SetCategoryColor  of string * ColorPicker.Action
    | SetNoValueColor   of ColorPicker.Action
    | FitRangeToData
    | ResetCategoryColors

[<ModelType>]
type ColorByCategoryModel = {
    version        : int
    enabled        : bool
    attribute      : ColorCategoryAttribute

    /// ramp for numeric attributes — reuses the existing false-color machinery.
    /// Its `useFalseColors` field doubles as the panel's single "show legend" toggle,
    /// for the numeric bar and the categorical swatches alike.
    numericLegend  : FalseColorsModel

    /// explicit per-category overrides, keyed by `"<attribute>/<label>"`. Absent keys
    /// fall back to a deterministic palette pick, so this starts empty and never needs
    /// a pre-population pass.
    /// TreatAsValue: edits replace the whole map, which is exactly the invalidation a
    /// global recolor wants, and it keeps the token-based renderer path a single read.
    [<TreatAsValue>]
    categoryColors : HashMap<string, ColorInput>

    /// used when an annotation has no value for the attribute (NaN result, a
    /// non-ellipse asked for a diameter, an annotation with no planar fit, …)
    noValueColor   : ColorInput
}

module ColorByCategoryModel =

    let current = 0

    let initial = {
        version        = current
        enabled        = false
        attribute      = ColorCategoryAttribute.AnnotationType
        numericLegend  = FalseColorsModel.initDefinedScalarsLegend (Range1d(0.0, 1.0))
        categoryColors = HashMap.empty
        noValueColor   = { c = C4b.Gray }
    }

    let readV0 =
        json {
            let! enabled        = Json.read "enabled"
            let! attribute      = Json.read "attribute"
            let! numericLegend  = Json.read "numericLegend"
            let! categoryColors = Json.read "categoryColors"
            let! noValueColor   = Json.read "noValueColor"

            return {
                version        = current
                enabled        = enabled
                attribute      = attribute |> enum<ColorCategoryAttribute>
                numericLegend  = numericLegend
                categoryColors =
                    categoryColors
                    |> List.map (fun (k, c) -> k, ({ c = C4b.Parse c } : ColorInput))
                    |> HashMap.ofList
                noValueColor   = { c = C4b.Parse noValueColor }
            }
        }

type ColorByCategoryModel with
    static member FromJson(_ : ColorByCategoryModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ColorByCategoryModel.readV0
            | _ ->
                return! v
                |> sprintf "don't know version %A of ColorByCategoryModel"
                |> Json.error
        }

    static member ToJson (x : ColorByCategoryModel) =
        json {
            do! Json.write "version"        x.version
            do! Json.write "enabled"        x.enabled
            do! Json.write "attribute"      (int x.attribute)
            do! Json.write "numericLegend"  x.numericLegend
            do! Json.write "categoryColors"
                    (x.categoryColors
                     |> HashMap.toList
                     |> List.map (fun (k, (c : ColorInput)) -> k, c.c.ToString()))
            do! Json.write "noValueColor"   (x.noValueColor.c.ToString())
        }
