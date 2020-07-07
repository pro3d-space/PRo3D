namespace DS

module PList =
  open Aardvark.Base.Incremental
  open Aardvark.Base


  let fromHMap (input : hmap<_,'a>) : plist<'a> = 
    input |> HMap.toSeq |> PList.ofSeq |> PList.map snd 

  let contains (f : 'a -> bool) (lst : plist<'a>) =
    let filtered = 
      lst 
        |> PList.filter f
    not (filtered.IsEmpty ())  

  let mapiInt (lst : plist<'a>) =
    let i = ref 0
    seq {
      for item in lst do
        yield (item, i.Value)
        i := !i + 1
    }
    |> PList.ofSeq

  let deleteFirst (lst : plist<'a>) (f : 'a -> bool) =
    match lst.FirstIndexOf f with
      | ind when ind = -1 -> (false, lst)
      | ind -> (true, lst.RemoveAt ind)

  

  


  let rec deleteAll (f : 'a -> bool) (lst : plist<'a>) =
    match deleteFirst lst f with
      | (true, li)  -> deleteAll f li
      | (false, li) -> li

  let filterNone (lst : plist<option<'a>>) =
    lst
      |> PList.filter (fun (el : option<'a>) -> el.IsSome)
      |> PList.map (fun el -> el.Value)

  let filterEmptyLists (lst : plist<list<'a>>) =
    lst
      |> PList.filter (fun a -> List.isEmpty a)




  let min (lst : plist<'a>) : 'a =
    lst
      |> PList.toList
      |> List.min

  let mapMin (f : 'a -> 'b) (lst : plist<'a>) : 'b =
    lst |> PList.toList
        |> List.map f
        |> List.min


  //let moveLeft (shiftLeft : 'a) (lst : plist<'a>)  =
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
  //  PList.mapi f lst

  //let moveRight (shiftRight : 'a) (lst : plist<'a>)  =
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
  //  PList.mapi f lst


  let minBy (f : 'a -> 'b) (lst : plist<'a>) : 'a =
    lst
      |> PList.toList
      |> List.reduce (fun x y -> if (f x) < (f y) then x else y)

  let maxBy (f : 'a -> 'b) (lst : plist<'a>) : 'a =
    lst
      |> PList.toList
      |> List.reduce (fun x y -> if (f x) > (f y) then x else y)

  let tryMinBy (f : 'a -> 'b) (lst : plist<'a>) : option<'a> =
    match lst.IsEmptyOrNull() with
      | true  -> None
      | false -> Some (minBy f lst)

  let tryMaxBy (f : 'a -> 'b) (lst : plist<'a>) : option<'a> =
    match lst.IsEmptyOrNull() with
      | true  -> None
      | false -> Some (maxBy f lst)

  let minMapBy (mapTo: 'a -> 'c) (minBy : 'a -> 'b) (lst : plist<'a>) : 'c =
    lst
      |> PList.toList
      |> List.reduce (fun x y -> if (minBy x) < (minBy y) then x else y)
      |> mapTo 

  let maxMapBy (mapTo: 'a -> 'c) (maxBy : 'a -> 'b) (lst : plist<'a>) : 'c =
    lst
      |> PList.toList
      |> List.reduce (fun x y -> if (maxBy x) < (maxBy y) then x else y)
      |> mapTo 

  let average (lst : plist<float>) : float =
    lst
      |> PList.toList
      |> List.average

  let tail (lst : plist<'a>) =
    match lst.IsEmptyOrNull () with
      | true  -> PList.empty
      | false -> PList.removeAt 0 lst

  let tryHead (lst : plist<'a>) =
    match lst.IsEmptyOrNull () with
      | true  -> None
      | false -> Some (lst.Item 0)

  let rec reduce (f : 'a -> 'a -> 'a) 
                 (acc : 'a) 
                 (lst : plist<'a>) =
    match (tryHead lst) with
      | None           -> acc
      | Some h         -> 
        reduce f (f acc h) (tail lst)

  let rec flattenLists (plst : plist<list<'a>>) =
    let head = tryHead plst
    match head with
      | None  -> []
      | Some lst -> 
        lst @ (flattenLists (tail plst))

  let rec allTrueOrEmpty (f : 'a -> bool) (lst : plist<'a>) =
    match lst.IsEmptyOrNull () with
      | true  -> true
      | false -> 
        match f (lst.Item 0) with
          | true  -> allTrueOrEmpty f (tail lst)
          | false -> false

  let rec anyTrue (f : 'a -> bool) (lst : plist<'a>) =
    match lst.IsEmptyOrNull () with
      | true -> false
      | false -> 
        match f (lst.Item 0) with
          | true  -> true
          | false -> anyTrue f (tail lst)
        
  let averageOrZero (lst : plist<float>) =
    match lst.IsEmptyOrNull () with
      | true -> 0.0
      | false -> average lst

  let rec mapPrev (lst  : plist<'a>) 
                  (prev : option<'a>)
                  (f    : 'a -> 'a -> 'a) =
    let current = tryHead lst
    match prev, current with
      | None, None     -> PList.empty
      | None, Some c   -> 
        PList.append c (mapPrev (tail lst) current f)
      | Some p, None   -> PList.empty
      | Some p, Some c -> 
        let foo = 
          mapPrev (tail lst) current f
        let bar =
          PList.append (f p c) foo
        bar   

  let rec mapPrev' 
    (keys  : plist<'key>) 
    (items : hmap<'key, 'b>)
    (prev  : option<'key>)
    (f     : 'b -> 'b -> 'b) : hmap<'key, 'b> =

    let current = tryHead keys
    let result = 
      match prev, current with
        | None, None     -> HMap.empty
        | None, Some c   -> 
          HMap.add c (items.Item c) (mapPrev' (tail keys) items current f)
        | Some p, None   -> HMap.empty
        | Some p, Some c -> 
          let prev = items.Item p
          let curr = items.Item c
          let _current = (f prev curr)
          let _items   = HMap.update c (fun optv -> _current) items
          let rest = 
            mapPrev' (tail keys) _items current f
          let bar =
            HMap.add c _current rest
          bar   
    result

  let removeLast (plst : plist<'a>) =
    PList.remove (PList.lastIndex plst) plst

  let reverse (plst : plist<'a>) =
    plst
      |> PList.toListBack 
      |> PList.ofList


  let zip (plst1 : plist<'a>) (plst2 : plist<'b>) =
    let rec _zip (res : plist<'a*'b>) (lst1 : plist<'a>) (lst2 : plist<'b>) =
      match lst1.IsEmptyOrNull (), lst2.IsEmptyOrNull () with
      | true, true -> res
      | false, false ->
        let last1 =  PList.last lst1
        let last2 =  PList.last lst2
        let result = PList.prepend (last1, last2) res
        let rest1 = removeLast lst1
        let rest2 = removeLast lst2
        (_zip result rest1 rest2)
      | _,_ -> failwith "lists must have the same size"
    _zip PList.empty plst1 plst2

    

      