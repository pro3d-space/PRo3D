namespace PRo3D.Core

open Adaptify.FSharp
open Aardvark.Base
open PRo3D.Core
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open FSharp.Data.Adaptive
open PRo3D.Core
open PRo3D.Core.Surface

module ContourLineApp =

    type Action =
        | SetDistance  of Numeric.Action
        | SetLineWidth of Numeric.Action
        | SetBorder    of Numeric.Action
        | ToggleEnabled
        | SetTargetTexture of Option<TextureLayer>
        


    let update (m : ContourLineModel) (action : Action) =
        match action with
        | SetDistance      a -> { m with distance = Numeric.update m.distance a }
        | SetLineWidth     a -> { m with width    = Numeric.update m.width    a }
        | SetBorder        a -> { m with border   = Numeric.update m.border   a }
        | ToggleEnabled      -> { m with enabled  = not m.enabled }  
        | SetTargetTexture a -> { m with targetLayer = a }
        

    let view  (model : AdaptiveContourLineModel) =        
      require GuiEx.semui (
        Html.table [  
          Html.row ""                     []
          Html.row "enabled"              [GuiEx.iconCheckBox model.enabled ToggleEnabled]
          Html.row "distance: "           [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.distance  |> UI.map SetDistance ] 
          Html.row "width: "              [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.width     |> UI.map SetLineWidth ] 
          Html.row "border: "             [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.border    |> UI.map SetBorder ] 
        ]
      )