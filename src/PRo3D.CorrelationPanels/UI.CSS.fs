namespace GUI


    module CSS =
      open System
      open Aardvark.Base
      open Aardvark.Base.Incremental
      open Aardvark.UI
      open CorrelationDrawing


      let myCss = [
          { kind = Stylesheet; name = "semui"; url = "https://cdn.jsdelivr.net/semantic-ui/2.2.6/semantic.min.css" }
          { kind = Stylesheet; name = "semui-overrides"; url = "semui-overrides.css" }
          { kind = Script; name = "semui"; url = "https://cdn.jsdelivr.net/semantic-ui/2.2.6/semantic.min.js" }
        ]

      let colorToHexStr (color : C4b) = 
        let bytes = [| color.R; color.G; color.B |]
        let str =
            bytes 
                |> (Array.map (fun (x : byte) -> System.String.Format("{0:X2}", x)))
                |> (String.concat System.String.Empty)
        String.concat String.Empty ["#";str] 

      let (--) (attStr1 : string) (attStr2 : string) =
        sprintf "%s; %s" attStr1 attStr2

      let styles (attList : list<string>) =
        attList 
          |> List.reduce (fun x y -> x -- y)

      // ATTRIBUTES
      let transition (seconds : float) =
        (sprintf "transition: all %.2fs ease" seconds)

      let saturation (sat : int) =
        (sprintf "filter: saturate(%i)" sat)

      let bgColorAttr (color : C4b) =
        style (sprintf "background: %s" (colorToHexStr color))

      let bgColorStr (color : C4b) =
        (sprintf "background: %s" (colorToHexStr color))

      let incrBgColorAMap (colorMod : IMod<C4b>) =      
        amap { 
          let! col =  colorMod
          let str = (sprintf "background: %s" (colorToHexStr col))
          yield style str
        }

      let incrBgColorAttr (colorMod : IMod<C4b>) =
        colorMod 
          |> Mod.map (fun x -> 
                        style (sprintf "background-color: %s" (colorToHexStr x)))      

       
      let modColorToColorAttr (c : IMod<C4b>) =
        let styleStr = Mod.map (fun x -> (sprintf "color:%s" (colorToHexStr x))) c
        Mod.map (fun x -> style x) styleStr  

      let noPadding  = "padding: 0px 0px 0px 0px"
      let tinyPadding  = "padding: 1px 1px 1px 1px"
      let lrPadding = "padding: 1px 4px 1px 4px"

      module Incremental =
        open Aardvark.Base.Incremental

        let style (lst : list<IMod<String>>) = 
          let res = 
            List.fold (fun s1 s2 -> 
                        Mod.map2 (fun a b -> 
                                    sprintf ("%s%s") a b) s1 s2
                      ) (Mod.constant "") lst
          amap {
            let! r = res
            yield style r
          }
          

      //  let saturation (sat : IMod<int>) =
          //FIREFOX: Mod.map (fun s -> sprintf "filter: saturate(%i%%);" s) sat
         
            
          
        let transition (sec : IMod<float>) =
           Mod.map (fun s -> (sprintf "transition: all %.2fs ease;" s)) sec
           
          

        