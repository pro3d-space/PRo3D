module UIMapping
    open Aardvark.Base
    open Aardvark.UI
    open Aardvark.Base.Incremental
        
    let mapAListId 
        (lst      : alist<DomNode<'a>>) 
        (id       : 'id)
        (toAction : ('id * 'a) -> 'b) =
        
        lst
        |> AList.map (fun el -> 
            el 
            |> UI.map (fun x -> 
                toAction (id, x)
            ) 
        )
    
    let mapAList 
        (toAction : 'a -> 'b) 
        (lst      : alist<DomNode<'a>>) =

        lst 
        |> AList.map (fun el -> UI.map toAction el)


