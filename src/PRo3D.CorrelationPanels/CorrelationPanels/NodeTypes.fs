namespace CorrelationDrawing.Types

open FSharp.Data.Adaptive

type State = New | Edit | Display

type LogNodeType = Hierarchical | HierarchicalLeaf | Metric | Angular | PosInfinity | NegInfinity | Infinity | Empty

type LogNodeBoxType = SimpleBox | TwoColorBox | FancyBox

type NodeLevel = NodeLevel of int

module NodeLevel =
    
    let apply f (NodeLevel l) = f l
    let value e = apply id e
    
    let LEVEL_MAX = 8
    let invalid = NodeLevel -1
    
    let init (integer : int) : NodeLevel = 
      (min integer LEVEL_MAX) |> NodeLevel
    
    let isInvalid nodeLevel =
      nodeLevel = invalid
    
    let availableLevels =
      alist {
        for i in 0..LEVEL_MAX do
          yield i
      }

type NodeLevel
with 
    member this.value =
        NodeLevel.value this