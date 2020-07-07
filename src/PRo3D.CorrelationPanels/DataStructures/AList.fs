namespace DS

  module AList =
    open Aardvark.Base
    open Aardvark.Base.Incremental

    let bindIMod (lst : alist<IMod<'a>>) =
      alist {
        for a in lst do
          let! a = a
          yield a
      }

    let tryHead (lst : alist<'a>) =
      adaptive {
        let! plst = lst.Content
        return 
          match plst.IsEmptyOrNull () with
            | true -> None
            | false -> Some (plst.Item 0)        
      }
   
    let fromAMap (input : amap<_,'a>) : alist<'a> = 
      input |> AMap.toASet |> AList.ofASet |> AList.map snd 

    let isEmpty (alst: alist<'a>) =
      alst.Content 
        |> Mod.map (fun x -> (x.Count < 1))

    let exists (f : 'a -> IMod<bool>) (alst: alist<'a>) = //performance :(
      let res = 
        alist {
          for a in alst do 
            let! b = (f a)
            if b then yield true
        }
      Mod.map (fun c -> (c > 0)) (AList.count res)
    
    let findAll (alst : alist<'a>) (f : 'a -> bool) =
      alist {
        let count = ref 0
        for a in alst do
          if f a then 
            incr count
            yield (count.Value, a)
      }

    let findFirst (alst : alist<'a>) (f : 'a -> bool) =
      let all = findAll alst f //runtime :(
      all
        |> AList.map snd
        |> tryHead
    
      

    let reduce (f : 'a -> 'a -> 'a) (alst: alist<'a>) = //TODO tryReduce
      alst.Content
        |> Mod.map (fun (x : plist<'a>) -> 
                        let r =
                          PList.toList x
                             |> List.reduce f
                        r
                    )

    let tryReduce (f : 'a -> 'a -> 'a) (alst: alist<'a>) = //TODO tryReduce
      adaptive {
        let! count = AList.count alst
        match count = 0 with
          | true -> return None
          | false ->
            let! cont = alst.Content
            let res = 
              cont 
                |> PList.toList 
                |> List.reduce f
            return Some res
      }

    let minBy (f : 'a -> 'b) (alst : alist<'a>) =
      alst
        |> tryReduce (fun x y -> if (f x) < (f y) then x else y)
      
    let min (alst : alist<'a>) =
       alst |>
        tryReduce (fun x y -> if x < y then x else y)

    let maxBy (f : 'a -> 'b) (alst : alist<'a>)  =
      alst
        |> tryReduce (fun x y -> if (f x) > (f y) then x else y)
      
    let tryMax (alst : alist<'a>) =
      alst
        |> tryReduce (fun x y -> if x > y then x else y)

    let average (alst : alist<float>) =
      let sum =
        alst |> reduce (fun x y -> x + y)
      Mod.map2 (fun s c -> s / (float c)) sum (AList.count alst)

    let tryAverage (alst : alist<float>) =
      let sum =
        alst |> tryReduce (fun x y -> x + y)
      adaptive {
        let! sum = sum
        let! c = AList.count alst
        return Option.map (fun s -> s / (float c)) sum
      }

    let sortByDescending (f : 'a -> 'b)  (alst : alist<'a>)  =
      alst |> AList.sortWith (fun c d -> compare (f d) (f c))    

      //|> AList.map mapper
      //     |> bindIMod

    let averageOf (f : 'a -> float) (alst : alist<'a>) = //TODO make dynamic
      alst
        |> AList.map f
        |> average

    let filter' (f : 'a -> IMod<bool>) (alst : alist<'a>) =
      alist {
        for el in alst do
          let! fil = f el
          if fil then yield el
      }

    let filterNone (lst : alist<option<'a>>) =
      lst
        |> AList.filter (fun el -> 
                          match el with
                            | Some el -> true
                            | None    -> false)
        |> AList.map (fun el -> el.Value)


    let zip (lst1 : alist<'a>) (lst2 : alist<'b>) =
      alist {
        let! plst1 = lst1.Content
        let! plst2 = lst2.Content
        let zipped = (PList.zip plst1 plst2)
        for z in zipped do
          yield z
      }
      
      