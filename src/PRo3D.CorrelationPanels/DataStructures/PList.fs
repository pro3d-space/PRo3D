namespace DS

module PList =
  open FSharp.Data.Adaptive
  open Aardvark.Base


  let fromHMap (input : HashMap<_,'a>) : IndexList<'a> = 
    input |> HashMap.toSeq |> IndexList.ofSeq |> IndexList.map snd 

  let contains (f : 'a -> bool) (lst : IndexList<'a>) =
    IndexList.exists (fun _ v -> f v) lst


  let mapiInt (lst : IndexList<'a>) =
    let i = ref 0
    seq {
      for item in lst do
        yield (item, i.Value)
        i := !i + 1
    }
    |> IndexList.ofSeq

  let deleteFirst (lst : IndexList<'a>) (f : 'a -> bool) =
    match lst.FirstIndexOf f with
      | ind when ind = -1 -> (false, lst)
      | ind -> (true, lst.RemoveAt ind)

  

  


  let rec deleteAll (f : 'a -> bool) (lst : IndexList<'a>) =
    match deleteFirst lst f with
      | (true, li)  -> deleteAll f li
      | (false, li) -> li

  let filterNone (lst : IndexList<option<'a>>) =
    lst
      |> IndexList.filter (fun (el : option<'a>) -> el.IsSome)
      |> IndexList.map (fun el -> el.Value)

  let filterEmptyLists (lst : IndexList<list<'a>>) =
    lst
      |> IndexList.filter (fun a -> List.isEmpty a)




  let min (lst : IndexList<'a>) : 'a =
    lst
      |> IndexList.toList
      |> List.min

  let mapMin (f : 'a -> 'b) (lst : IndexList<'a>) : 'b =
    lst |> IndexList.toList
        |> List.map f
        |> List.min


  //let moveLeft (shiftLeft : 'a) (lst : IndexList<'a>)  =
  //  let f (ind : Index) (current : 'a) =
  //    match lst.TryGet (ind.After ()), lst.TryGet (ind.Before ()) with
  //      | Some next, None -> 
  //        if shiftLeft = next then
  //          next
  //        else
  //          current
  //      | None, Some prev ->
  //        if shiftLeft = current then
  //          prev
  //        else
  //          current
  //      | Some next, Some prev ->
  //        if shiftLeft = next then
  //          next
  //        elif shiftLeft = current then
  //          prev
  //        else
  //          current
  //      | None, None   -> current
  //  IndexList.mapi f lst

  //let moveRight (shiftRight : 'a) (lst : IndexList<'a>)  =
  //  let f (ind : Index) (current : 'a) =
  //    match lst.TryGet (ind.After ()), lst.TryGet (ind.Before ()) with
  //      | Some next, None -> 
  //        if shiftRight = current then
  //          next
  //        else
  //          current
  //      | None, Some prev ->
  //        if shiftRight = prev then
  //          prev
  //        else
  //          current
  //      | Some next, Some prev ->
  //        if shiftRight = current then
  //          prev
  //        elif shiftRight = prev then
  //          prev
  //        else
  //          current
  //      | None, None   -> current
  //  IndexList.mapi f lst


  let minBy (f : 'a -> 'b) (lst : IndexList<'a>) : 'a =
    lst
      |> IndexList.toList
      |> List.reduce (fun x y -> if (f x) < (f y) then x else y)

  let maxBy (f : 'a -> 'b) (lst : IndexList<'a>) : 'a =
    lst
      |> IndexList.toList
      |> List.reduce (fun x y -> if (f x) > (f y) then x else y)

  let tryMinBy (f : 'a -> 'b) (lst : IndexList<'a>) : option<'a> =
    match lst.IsEmptyOrNull() with
      | true  -> None
      | false -> Some (minBy f lst)

  let tryMaxBy (f : 'a -> 'b) (lst : IndexList<'a>) : option<'a> =
    match lst.IsEmptyOrNull() with
      | true  -> None
      | false -> Some (maxBy f lst)

  let minMapBy (mapTo: 'a -> 'c) (minBy : 'a -> 'b) (lst : IndexList<'a>) : 'c =
    lst
      |> IndexList.toList
      |> List.reduce (fun x y -> if (minBy x) < (minBy y) then x else y)
      |> mapTo 

  let maxMapBy (mapTo: 'a -> 'c) (maxBy : 'a -> 'b) (lst : IndexList<'a>) : 'c =
    lst
      |> IndexList.toList
      |> List.reduce (fun x y -> if (maxBy x) < (maxBy y) then x else y)
      |> mapTo 

  let average (lst : IndexList<float>) : float =
    lst
      |> IndexList.toList
      |> List.average

  let tail (lst : IndexList<'a>) =
    match lst.IsEmptyOrNull () with
      | true  -> IndexList.empty
      | false -> IndexList.removeAt 0 lst

  let tryHead (lst : IndexList<'a>) =
    match lst.IsEmptyOrNull () with
      | true  -> None
      | false -> Some (lst.Item 0)

  let rec reduce (f : 'a -> 'a -> 'a) 
                 (acc : 'a) 
                 (lst : IndexList<'a>) =
    match (tryHead lst) with
      | None           -> acc
      | Some h         -> 
        reduce f (f acc h) (tail lst)

  let rec flattenLists (plst : IndexList<list<'a>>) =
    let head = tryHead plst
    match head with
      | None  -> []
      | Some lst -> 
        lst @ (flattenLists (tail plst))

  let rec allTrueOrEmpty (f : 'a -> bool) (lst : IndexList<'a>) =
    match lst.IsEmptyOrNull () with
      | true  -> true
      | false -> 
        match f (lst.Item 0) with
          | true  -> allTrueOrEmpty f (tail lst)
          | false -> false

  let rec anyTrue (f : 'a -> bool) (lst : IndexList<'a>) =
    match lst.IsEmptyOrNull () with
      | true -> false
      | false -> 
        match f (lst.Item 0) with
          | true  -> true
          | false -> anyTrue f (tail lst)
        
  let averageOrZero (lst : IndexList<float>) =
    match lst.IsEmptyOrNull () with
      | true -> 0.0
      | false -> average lst

  let rec mapPrev (lst  : IndexList<'a>) 
                  (prev : option<'a>)
                  (f    : 'a -> 'a -> 'a) =
    let current = tryHead lst
    match prev, current with
      | None, None     -> IndexList.empty
      | None, Some c   -> 
        IndexList.add c (mapPrev (tail lst) current f)
      | Some p, None   -> IndexList.empty
      | Some p, Some c -> 
        let foo = 
          mapPrev (tail lst) current f
        let bar =
          IndexList.add (f p c) foo
        bar   

  let rec mapPrev' 
    (keys  : IndexList<'key>) 
    (items : HashMap<'key, 'b>)
    (prev  : option<'key>)
    (f     : 'b -> 'b -> 'b) : HashMap<'key, 'b> =

    let current = tryHead keys
    let result = 
      match prev, current with
        | None, None     -> HashMap.empty
        | None, Some c   -> 
          HashMap.add c (items.Item c) (mapPrev' (tail keys) items current f)
        | Some p, None   -> HashMap.empty
        | Some p, Some c -> 
          let prev = items.Item p
          let curr = items.Item c
          let _current = (f prev curr)
          let _items   = HashMap.update c (fun optv -> _current) items
          let rest = 
            mapPrev' (tail keys) _items current f
          let bar =
            HashMap.add c _current rest
          bar   
    result

  let removeLast (plst : IndexList<'a>) =
    IndexList.remove (IndexList.lastIndex plst) plst

  let reverse (plst : IndexList<'a>) =
    plst
      |> IndexList.toListBack 
      |> IndexList.ofList


  let zip (plst1 : IndexList<'a>) (plst2 : IndexList<'b>) =
    let rec _zip (res : IndexList<'a*'b>) (lst1 : IndexList<'a>) (lst2 : IndexList<'b>) =
      match lst1.IsEmptyOrNull (), lst2.IsEmptyOrNull () with
      | true, true -> res
      | false, false ->
        let last1 =  IndexList.last lst1
        let last2 =  IndexList.last lst2
        let result = IndexList.prepend (last1, last2) res
        let rest1 = removeLast lst1
        let rest2 = removeLast lst2
        (_zip result rest1 rest2)
      | _,_ -> failwith "lists must have the same size"
    _zip IndexList.empty plst1 plst2

    

      