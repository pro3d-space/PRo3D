namespace Test

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI.Primitives
open Svgplus.RoseDiagramModel
open Svgplus.ArrowType

type Primitive =
    | Box
    | Sphere


[<DomainType>]
type TestModel =
    {
        currentModel    : Primitive
        svgButton       : Svgplus.Button
        arrow           : Arrow
        header          : Svgplus.HeaderType.Header
        roseDiagram     : RoseDiagram
        //diagramApp      : Svgplus.DA.Diagram
    }