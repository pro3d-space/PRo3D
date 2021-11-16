namespace PRo3D.Viewer

open Aardvark.Base
open Aardvark.UI

type TraverseAction =
| LoadTraverse of string

module TraverseApp =
    let update (model : Traverse) (action : TraverseAction) : Traverse = 
        match action with
        | LoadTraverse path ->
            Log.line "[Traverse] Loading %s" path
            model

    let view (model : AdaptiveTraverse) : DomNode<TraverseAction> = 
        failwith ""
        

