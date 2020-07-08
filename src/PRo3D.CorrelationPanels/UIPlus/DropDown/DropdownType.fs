namespace UIPlus.DropdownType

open Aardvark.Base
open FSharp.Data.Adaptive

[<ModelType>]
type DropdownList<'a> = {
   valueList          : IndexList<'a>
   selected           : option<'a>
   color              : C4b
   searchable         : bool
   //changeFunction     : (option<'a> -> 'msg) @Thomas proper way?
   //labelFunction      : ('a -> aval<string>)
   //getIsSelected      : ('a -> aval<bool>) 
 } 