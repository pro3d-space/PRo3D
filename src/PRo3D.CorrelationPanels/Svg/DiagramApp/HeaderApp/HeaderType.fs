namespace Svgplus.HeaderType

open Aardvark.Base
open Aardvark.Base.Incremental
open CorrelationDrawing
open Svgplus
open Svgplus.TextType
open UIPlus

[<DomainType>]
type Header = {
    centre      : V2d
    dim         : Size2D
    label       : Text
    leftButton  : ArrowType.Arrow
    rightButton : ArrowType.Arrow
    visible     : bool
}

