namespace Svgplus.HeaderType

open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open CorrelationDrawing
open Svgplus
open Svgplus.TextType
open UIPlus

[<ModelType>]
type Header = {
    centre      : V2d
    dim         : Size2D
    label       : Text
    leftButton  : ArrowType.Arrow
    rightButton : ArrowType.Arrow
    visible     : bool
}

