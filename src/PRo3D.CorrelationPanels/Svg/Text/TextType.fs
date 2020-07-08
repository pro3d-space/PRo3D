namespace Svgplus.TextType

open Aardvark.Base
open FSharp.Data.Adaptive
open CorrelationDrawing
open Svgplus
open UIPlus

[<ModelType>]
type Text = {
  centre      : V2d
  dim         : Size2D
  textInput   : TextInput
  bold        : bool
  onEnter     : Text -> Text
  onLeave     : Text -> Text
  fontSize    : FontSize
}  