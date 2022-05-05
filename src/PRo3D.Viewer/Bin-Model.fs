namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Aardvark.UI
open Adaptify
open FSharp.Data.Adaptive

[<ModelType>]
type BinModel = 
    {    
         count         : int 
         range         : Range1d
         annotationIDs : List<Guid>  //to keep track which annotations are responsible for the count       
    }   

module BinModel =

    let getBinMaxValue (bins:List<BinModel>) =
        bins |> List.map (fun b -> b.count) |> List.max




