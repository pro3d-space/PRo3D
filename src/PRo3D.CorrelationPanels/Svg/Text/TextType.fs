namespace Svgplus.TextType

open Aardvark.Base
open Aardvark.Base.Incremental
open CorrelationDrawing
open Svgplus
open UIPlus

[<DomainType>]
type Text = {
  centre      : V2d
  dim         : Size2D
  textInput   : TextInput
  bold        : bool
  onEnter     : Text -> Text
  onLeave     : Text -> Text
  fontSize    : FontSize
}  