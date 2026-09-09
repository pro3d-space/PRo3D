namespace PRo3D.Core

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open FSharp.Data.Adaptive

open PRo3D.Core

/// Update / view for the LatLon graticule overlay. Mirrors ContourLineApp.
/// Kept in its own file (rather than VisualizationAndTFApp.fs) because the
/// interval dropdown needs UI.dropDown'' from UI.fs, which compiles later.
module LatLonShaderApp =

    type Action =
        | ToggleEnabled
        | ToggleLabels
        | SetTextSize    of Numeric.Action
        | SetLatInterval of int
        | SetLonInterval of int
        | SetLineColor   of ColorPicker.Action
        | SetLineWidth   of Numeric.Action

    let update (m : LatLonShaderModel) (action : Action) =
        match action with
        | ToggleEnabled     -> { m with enabled    = not m.enabled }
        | ToggleLabels      -> { m with showLabels = not m.showLabels }
        | SetTextSize a     -> { m with textSize   = Numeric.update m.textSize a }
        | SetLatInterval i  -> if 360 % i = 0 then { m with latInterval = i } else m
        | SetLonInterval i  -> if 360 % i = 0 then { m with lonInterval = i } else m
        | SetLineColor a    -> { m with lineColor  = ColorPicker.update m.lineColor a }
        | SetLineWidth a    -> { m with lineWidth  = Numeric.update m.lineWidth a }

    let private intervalDropdown (selected : aval<int>) (ctor : int -> Action) =
        UI.dropDown''
            (AList.ofList LatLonShaderModel.divisorsOf360)
            (selected |> AVal.map Some)
            (fun o -> ctor (o |> Option.defaultValue LatLonShaderModel.defaultInterval))
            (fun d -> sprintf "%d°" d)

    /// The graticule only makes sense once the reference system is tied to a
    /// celestial body. For JPL / ENU / None there is no lat-lon frame, so the
    /// parameters are hidden and a hint is shown instead.
    let private hasBody (planet : Planet) =
        match planet with
        | Planet.None | Planet.JPL | Planet.ENU -> false
        | _ -> true

    let private parameters (model : AdaptiveLatLonShaderModel) =
        Html.table [
          Html.row ""             []
          Html.row "enabled"      [ GuiEx.iconCheckBox model.enabled ToggleEnabled ]
          Html.row "lat interval" [ intervalDropdown model.latInterval SetLatInterval ]
          Html.row "lon interval" [ intervalDropdown model.lonInterval SetLonInterval ]
          Html.row "line color"   [ ColorPicker.view model.lineColor |> UI.map SetLineColor ]
          Html.row "line width"   [ Numeric.view' [NumericInputType.InputBox] model.lineWidth |> UI.map SetLineWidth ]
          Html.row "show labels"  [ GuiEx.iconCheckBox model.showLabels ToggleLabels ]
          Html.row "text size"    [ Numeric.view' [NumericInputType.InputBox] model.textSize |> UI.map SetTextSize ]
        ]

    let view (planet : aval<Planet>) (model : AdaptiveLatLonShaderModel) =
      require GuiEx.semui (
        Incremental.div AttributeMap.empty (
          planet
          |> AVal.map (fun p ->
              if hasBody p then
                  parameters model
              else
                  div [style "font-style:italic; padding:5px"]
                      [ text "Set reference system to a body to display the latlon shader" ])
          |> AList.ofAValSingle
        )
      )
