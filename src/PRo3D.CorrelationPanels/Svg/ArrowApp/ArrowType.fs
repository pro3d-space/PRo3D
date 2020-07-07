namespace Svgplus.ArrowType

open Aardvark.Base
open Aardvark.Base.Incremental
open CorrelationDrawing

[<DomainType>]
type Arrow = {
  centre        : V2d
  direction     : Direction
  length        : float
  height        : float
  horz          : float
  vert          : float
  stroke        : float
  fill          : bool
  colour        : C4b
  onEnter       : Arrow -> Arrow
  onLeave       : Arrow -> Arrow
}


