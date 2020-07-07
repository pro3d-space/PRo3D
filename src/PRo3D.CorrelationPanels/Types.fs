namespace CorrelationDrawing

open Aardvark.Base
open Aardvark.Base.Incremental

type CorrelationPanelResources = CorrelationPanelResources

type Orientation  = Horizontal | Vertical
type TextAnchor = Start | Middle | End

type Alignment = LEFT | RIGHT | CENTRE
  with 
    member this.clazz =
      match this with
      | Alignment.LEFT -> "left aligned"
      | Alignment.CENTRE -> "left aligned"
      | Alignment.RIGHT -> "right aligned"


type Direction =
| Up
| Down
| Left
| Right
with
  member this.toString =
    match this with
      | Up    -> "up"
      | Down  -> "down"
      | Left  -> "left"
      | Right -> "right"

type Size =
  | Mini
  | Tiny
  | Small
  | Normal
  | Large
  | Big
  | Huge
  | Massive
  with
    member this.toString =
      match this with
        | Mini      -> "mini"
        | Tiny      -> "tiny"
        | Small     -> "small"
        | Normal    -> ""
        | Large     -> "large"
        | Big       -> "big"
        | Huge      -> "huge"
        | Massive   -> "massive"

type Size2D = {
  width  : float
  height : float
} with 
    member this.X = this.width
    member this.Y = this.height

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Size2D =
  let init = {width = 0.0; height = 0.0}

type SvgWeight = {
  value : float
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SvgWeight =
  let init = {value = 2.0}


type BorderColors = {
  upper : C4b
  lower : C4b
}

module Math = 
    type Angle = {
        radians : float
    } with
        static member (*)  (this : Angle, other : Angle) : Angle =
            {radians = (this.radians * other.radians) % Constant.PiTimesTwo}
        static member (*)  (this : Angle, other : float) : Angle =
            {radians = (this.radians * other) % Constant.PiTimesTwo}
        static member (+)  (this : Angle, other : Angle) : Angle =
            {radians = (this.radians + other.radians) % Constant.PiTimesTwo}
        static member (+)  (this : Angle, other : float) : Angle =
            {radians = (this.radians + other) % Constant.PiTimesTwo}
        member this.degrees =
            this.radians.DegreesFromRadians()

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module Angle =
        let twoPi = {radians = Constant.PiTimesTwo}
        let halfPi = {radians = Constant.PiHalf}
        let quarterPi = {radians = Constant.PiQuarter}
        let eigthPi = {radians = Constant.PiQuarter / 2.0}
        let sixteenthPi = {radians = Constant.PiQuarter / 4.0}

        let init radians = {radians = radians % twoPi.radians}