namespace Test

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI.Primitives
open Svgplus.RoseDiagramModel
open Svgplus.ArrowType

type Primitive =
    | Box
    | Sphere


[<ModelType>]
type TestModel =
    {
        currentModel    : Primitive
        svgButton       : Svgplus.Button
        arrow           : Arrow
        header          : Svgplus.HeaderType.Header
        roseDiagram     : RoseDiagram
        //diagramApp      : Svgplus.DA.Diagram
    }