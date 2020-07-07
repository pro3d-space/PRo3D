namespace DS

  module List =
    let averageOrZero (lst : list<float>) = 
      match lst with
        | [] -> 0.0
        | li -> List.average li

    let maxOrZero (lst : list<float>) = 
      match lst with
        | [] -> 0.0
        | li -> List.max li

    let tryMax (lst : list<float>) = 
      match lst with
        | [] -> None
        | li -> Some (List.max li)


    let contains' (f : 'a -> bool) (lst : List<'a>)  =
      match lst with
        | []  -> false
        | l   ->
          lst |> List.map (fun x -> f x)
              |> List.reduce (fun x y -> x || y)

    let contains'' (f : 'a -> 'b) (a : 'a)  (lst : List<'a>) =
      match lst with
        | []  -> false
        | l   ->
          lst
             |> List.map f
             |> List.contains (f a)

  //  let l = [1..5]
  //  l |> (contains'' (fun (x : int) -> (sprintf "%i" x)) 7)

    let reduce' (f1 : 'a -> 'b) (f2 : 'b -> 'b -> 'b) (lst : List<'a>) =
      lst
        |> List.map f1
        |> List.reduce f2

    let filterNone (lst : list<option<'a>>) =
      lst
        |> List.filter (fun el -> 
                          match el with
                            | Some el -> true
                            | None    -> false)
        |> List.map (fun el -> el.Value)


    let _swap (indFirst : int) (indSec : int) (items : List<'a>) =
       if    indFirst < 0 
          || indSec   < 0 
          || indFirst > items.Length - 1
          || indSec   > items.Length - 1
        then items
        else
          let _swap (first : int) (second : int) (i : int) =
            match i with
              | i when i = first -> List.item second items
              | i when i = second -> List.item first items
              | _ -> List.item i items

          let shifted = List.mapi (fun i x -> _swap indFirst indSec i) items
          shifted

    let swap (first : 'a) (second : 'a) (items : List<'a>) =
      let indFirst =
          List.findIndex (fun x -> x = first) items
      let indSec =
          List.findIndex (fun x -> x = second) items
      _swap indFirst indSec items

    let shiftLeft (shift : 'a) (items : List<'a>) =
      let indFirst =
          List.findIndex (fun x -> x = shift) items
      let indSec = indFirst - 1
      _swap indFirst indSec items

    let shiftRight (shift : 'a) (items : List<'a>) =
      let indFirst =
          List.findIndex (fun x -> x = shift) items
      let indSec = indFirst + 1
      _swap indFirst indSec items


    //let a = [[1;5];[2;3];[4;7];[6;2];[7;8]]

    let rec flattenLists (plst : list<list<'a>>) =
      let head = List.tryHead plst
      match head with
        | None  -> []
        | Some lst -> 
          lst @ (flattenLists (List.tail plst))      

    //let b = flattenLists a
    //b