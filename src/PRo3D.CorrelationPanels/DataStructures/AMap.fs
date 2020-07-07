namespace DS

module AMap =
    open Aardvark.Base.Incremental
    
    open Aardvark.Base
    
    let tryFind (map : amap<'k, 'a>) (key : 'k) =
       adaptive {
          let! content = map.Content
          return content.TryFind key
       }
    
    let valuesToASet (map : amap<'k, 'a>) =
      map
        |> AMap.toASet 
        |> ASet.map (snd)
    
    let valuesToAList (map : amap<'k, 'a>) =
      map
        |> AMap.toASet 
        |> ASet.map (fun (k,a) -> a)
        |> AList.ofASet
    
    let toFlatAList (mp : amap<'k, alist<'a>>) =
      let keys = mp |> Aardvark.UI.AMap.keys |> ASet.toAList
      alist 
        {
          for id in keys  do
            let! rview = AMap.find id mp
            yield! rview
        }
    
    let toOrderedAList (map : amap<'k, 'v>) (order : alist<'k>) =
      alist {
        for id in order do
          let! opt = AMap.tryFind id map
          match opt with
          | Some x -> yield x
          | None -> ()
      }
    
