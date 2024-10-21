namespace PRo3D.Base

open System
open Adaptify
open Aardvark.Base
open FSharp.Data.Adaptive

[<ModelType>]
type Calendar = {
    date    : DateTime
    minDate : option<DateTime>
    maxDate : option<DateTime>
    label   : option<string>
}