namespace PRo3D.Core

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open FSharp.Data.Adaptive

open PRo3D.Core

/// Update / view for the LatLon graticule overlay. Mirrors ContourLineApp.
/// Kept in its own file (rather than VisualizationAndTFApp.fs) because the
/// view needs helpers from UI.fs, which compiles later.
module LatLonShaderApp =

    /// The three graticule granularities, in degrees. Each has a fixed screen
    /// line width (see Shader.latLonLines): 1° → 0.5 px, 5° → 1.0 px, 15° → 2.0 px.
    let levels = [ 1; 5; 15 ]

    type Action =
        | ToggleEnabled
        | ToggleLat    of int          // 1 | 5 | 15
        | ToggleLon    of int          // 1 | 5 | 15
        | SetLineColor of ColorPicker.Action

    let update (m : LatLonShaderModel) (action : Action) =
        match action with
        | ToggleEnabled  -> { m with enabled = not m.enabled }
        | ToggleLat 1    -> { m with lat1  = not m.lat1 }
        | ToggleLat 5    -> { m with lat5  = not m.lat5 }
        | ToggleLat 15   -> { m with lat15 = not m.lat15 }
        | ToggleLat _    -> m
        | ToggleLon 1    -> { m with lon1  = not m.lon1 }
        | ToggleLon 5    -> { m with lon5  = not m.lon5 }
        | ToggleLon 15   -> { m with lon15 = not m.lon15 }
        | ToggleLon _    -> m
        | SetLineColor a -> { m with lineColor = ColorPicker.update m.lineColor a }

    /// The graticule only makes sense once the reference system is tied to a
    /// celestial body. For JPL / ENU / None there is no lat-lon frame, so the
    /// parameters are hidden and a hint is shown instead.
    let private hasBody (planet : Planet) =
        CooTransformation.getConvention planet <> CooTransformation.NonPlanetary

    /// A row of 1° / 5° / 15° checkboxes for one axis.
    let private levelChecks (toggle : int -> Action) (b1 : aval<bool>) (b5 : aval<bool>) (b15 : aval<bool>) =
        div [ style "display:inline-flex; gap:16px; align-items:center" ] [
            span [] [ GuiEx.iconCheckBox b1  (toggle 1);  text " 1°"  ]
            span [] [ GuiEx.iconCheckBox b5  (toggle 5);  text " 5°"  ]
            span [] [ GuiEx.iconCheckBox b15 (toggle 15); text " 15°" ]
        ]

    let private parameters (model : AdaptiveLatLonShaderModel) =
        Html.table [
          Html.row ""            []
          Html.row "enabled"     [ GuiEx.iconCheckBox model.enabled ToggleEnabled ]
          Html.row "lat lines"   [ levelChecks ToggleLat model.lat1 model.lat5 model.lat15 ]
          Html.row "lon lines"   [ levelChecks ToggleLon model.lon1 model.lon5 model.lon15 ]
          Html.row "line color"  [ ColorPicker.view model.lineColor |> UI.map SetLineColor ]
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
