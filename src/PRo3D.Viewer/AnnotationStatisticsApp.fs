namespace Pro3D.AnnotationStatistics

open System
open PRo3D.Base
open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive

type BinAction =
    | SetStart of Numeric.Action
    | SetEnd of Numeric.Action
    | SetValue of int

type HistogramAction =
    | Compute 
    | AddBin 
    | UpdateBin of BinAction * Guid 

type PropertyAction =    
    | UpdateStats of List<float>
    | UpdateHistogram of HistogramAction

type AnnoStatsAction =
    | SetSelected of Guid * GroupsModel
    | SetProperty of Prop
    | UpdateProperty of PropertyAction * Prop



module BinOperations =

    let update (m:Bin) (action:BinAction) =
        match action with
        | SetStart s -> let ud_start = Numeric.update m.start s
                        {m with start = ud_start}

        | SetEnd e -> let ud_end = Numeric.update m.theEnd e
                      {m with theEnd = ud_end}

        | SetValue v -> {m with value = v}


module HistogramOperations = 

    let update (m:(Pro3D.AnnotationStatistics.Histogram)) (action:HistogramAction) =
        match action with
        | Compute -> m

        | AddBin -> let emptyBin = (AnnoStats.initBin 0 0.0 0.0)
                    let binList = m.bins.Add (Guid.NewGuid(), emptyBin)        
                    {m with bins = binList; numOfBins = (m.numOfBins + 1)}     

        | UpdateBin (act, id)-> match (m.bins |> HashMap.tryFind id) with
                                | Some bin -> let updatedBin = BinOperations.update bin act
                                              let updatedBinList = m.bins |> HashMap.alter id (function None -> None | Some _ -> Some updatedBin)
                                              {m with bins = updatedBinList}
                                | None -> m

module PropertyOperations =


    let private calcMinMaxAvg (l:List<float>) =
        match (l.IsEmpty) with
        | true -> (0.0, 0.0, 0.0)        
        | false -> let min = l |> List.min
                   let max = l |> List.max
                   let avg = l |> List.average
                   (min, max, avg)                      
                   
     

    let update (m:Property) (action:PropertyAction) =
        match action with       
        | UpdateStats d -> let min,max,avg = calcMinMaxAvg d
                           {m with data = (d|> IndexList.ofList); min = min; max = max; avg = avg}

        | UpdateHistogram histAction -> let updatedHist = HistogramOperations.update m.histogram histAction
                                        {m with histogram = updatedHist}
    

module AnnotationStatisticsApp =     
               
    //recursively builds a list of bins and sorts values into them
    //li = bin list to build, sort = list of values to sort into bins; k = number of bins, r = rounds, acc = accumulated rangeStart, binW = bin width
    //let rec binList li (sort:List<float>) k r (acc:int) (binW:int) =
    //    if (r > k) then (li |> IndexList.ofList)
    //    else 
    //        let bin = 
    //                    let rS = acc
    //                    let rE = acc + binW
    //                    let inBin = sort |> List.filter(fun f -> f >= float(rS) && f <= float(rE))                       

    //                    [             
    //                        {                                
    //                            value = inBin.Length
    //                            start = rS
    //                            theEnd = rE
    //                            width = binW
    //                        }
    //                    ]
    //        binList (li @ bin) sort k (r+1) (acc+binW) binW

        
    
    //let updateHistogram (h:Pro3D.AnnotationStatistics.Histogram) (values:List<float>) (title:string) =
        
    //    match (values.IsEmpty) with 
    //     | true -> h
    //     | false -> let n = float(values.Length)                     
    //                let k = int(round(1.0 + 3.322 * (System.Math.Log10 n))) //Sturges' rule, number of bins
    //                let min = int(floor(values |> List.min))
    //                let max = int(round(values |> List.max))
    //                let binWidth = ((max - min)/k) + 1 //+1 to prevent rounding issues

    //                //set up the bins
    //                let bins = binList List.empty values (int(k)) 1 min binWidth
                    
    //                {h with numOfBins = bins.Count; rangeStart = min; rangeEnd = max; bins = bins}                                                

    

    let private calcMinMaxAvg (l:List<float>) =
           match (l.IsEmpty) with
           | true -> (0.0, 0.0, 0.0)        
           | false -> let min = l |> List.min
                      let max = l |> List.max
                      let avg = l |> List.average
                      (min, max, avg)                      
                      
     

    let getPropData (prop:Prop) (selected:List<Annotation>) =

        match prop with
            | Prop.LENGTH -> selected 
                                   |> List.map(fun a -> match a.results with
                                                           | Some r -> Some(r.length)
                                                           | None -> None
                                               )
                                   |> List.choose(fun o -> o)
                                   

            | Prop.BEARING -> selected 
                                    |> List.map(fun a -> match a.results with
                                                            | Some r -> Some(r.bearing)
                                                            | None -> None
                                                )
                                    |> List.choose(fun o -> o)
                                    
            
            | Prop.VERTICALTHICKNESS -> selected 
                                                |> List.map(fun a -> match a.results with
                                                                        | Some r -> Some(r.verticalThickness)
                                                                        | None -> None
                                                            )
                                                |> List.choose(fun o -> o)
                                                
            

    //when a new annotation is added
    let updateAllProperties (m:AnnoStatsModel) (annos:List<Annotation>)=
        let props = m.properties
        match props.IsEmpty with
        | true -> HashMap.empty
        | false -> props |> HashMap.map(fun k v -> 
                                                    let p1 = PropertyOperations.update v (UpdateStats (getPropData k annos))
                                                    PropertyOperations.update p1 (UpdateHistogram Compute)                                                   
                                                

                                           )

    //when the settings of the histogram of one specific property are changed
    let updateOnePropertyHistogram (prop:Property) =
        prop


           



    let update (m:AnnoStatsModel) (a:AnnoStatsAction) =
        match a with
        | SetSelected (id, g) ->         
            
            match (g.flat |> HashMap.tryFind id) with
            | Some l -> let anno = Leaf.toAnnotation l
                        match (m.selectedAnnotations |> HashMap.tryFind id) with
                        | Some _ -> m
                        | None -> let updatedAnnos = m.selectedAnnotations.Add (id,anno)
                                  let updatedProperties = updateAllProperties m (updatedAnnos |> HashMap.toValueList)
                                  {m with selectedAnnotations = updatedAnnos; properties = updatedProperties}
            | None -> m

        
        | SetProperty prop ->                              
                             let properties = 
                                match (m.properties.ContainsKey prop) with
                                | true -> m.properties
                                | false -> let d = getPropData prop (m.selectedAnnotations |> HashMap.toValueList)
                                           let min, max, avg = calcMinMaxAvg d                                                                                      
                                           let property = {
                                                 kind = prop
                                                 data = (d |> IndexList.ofList)
                                                 min = min
                                                 max = max
                                                 avg = avg
                                                 histogram = (AnnoStats.initHistogram min max (d.Length))                                                              
                                               }
                                           m.properties.Add (prop, property)
            
            
                             {m with properties = properties}
        
        | UpdateProperty (act, prop) -> 
            match act with
            | UpdateStats d -> m
            | UpdateHistogram histAction -> match (m.properties.TryFind prop) with
                                            | Some p -> let updatedHist = HistogramOperations.update p.histogram histAction
                                                        let updatedProp = {p with histogram = updatedHist}
                                                        let updatedPropList = m.properties |> HashMap.alter prop (function None -> None | Some _ -> Some updatedProp)
                                                        {m with properties = updatedPropList}
                                                        
                                            | None -> m
                           
    
    //project the domain values to pixel values
    //let projectToPx x min max =
    //    //let a = 0 
    //    let b = 300.0
    //    match (max - min) with
    //    | 0 -> 0
    //    | _ -> let temp1 = float(x - min)
    //           let temp2 = float(max - min)
    //           let temp3 = (temp1 / temp2) * b            
    //           let res = int(temp3)
    //           res
     
    

    
    let drawHistogram (h: AdaptiveHistogram) (width:int) = 
        
        let height = 5
        let offSetY = 100
        let padLR = 10

        let attr (bin:AdaptiveBin) (idx:int)= 
            amap{
                let! n = h.numOfBins
                let! binV = bin.value
                let w = (width-padLR*2) / n                          
                
                yield style "fill:green;fill-opacity:1.0;"                
                yield attribute "x" (sprintf "%ipx" (10 + idx * w))
                yield attribute "y" (sprintf "%ipx" ((height-(binV*10)+offSetY)))
                yield attribute "width" (sprintf "%ipx" w) 
                yield attribute "height" (sprintf "%ipx" (binV*10)) 
            } |> AttributeMap.ofAMap       
             
       
        
        let l = 
                h.bins 
                |> AMap.toASet 
                |> ASet.toAList
                |> AList.map (fun (_,b) -> b)
        
        alist{
            let! idxL = l.Content
            for i in 0..(idxL.Count-1) do
                let bin = idxL.TryGet i
                match bin with
                | Some b -> let attrib = attr b i
                            yield Incremental.Svg.rect attrib
                | None -> yield text "here could be a histogram"
                
          
        }
        

                


    let propDropdown =         

        div [ clazz "ui menu"; style "width:150px; height:20px;padding:0px; margin:0px"] [
            onBoot "$('#__ID__').dropdown('on', 'hover');" (
                div [ clazz "ui dropdown item"; style "width:100%"] [
                    text "Properties"
                    i [clazz "dropdown icon"; style "margin:0px 5px"][] 
                    div [ clazz "ui menu"] [
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetProperty Prop.LENGTH)] [text "Length"]
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetProperty Prop.BEARING)] [text "Bearing"]
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetProperty Prop.VERTICALTHICKNESS)] [text "Vertical Thickness"]     
                    ]
                ]
            )
        ] 

    let propListing (prop:AdaptiveProperty) = 

        require GuiEx.semui (
            Html.table [                                    
                Html.row "Minimum" [Incremental.text (prop.min |> AVal.map (fun f -> sprintf "%.2f" f))]
                Html.row "Maximum" [Incremental.text (prop.max |> AVal.map (fun f -> sprintf "%.2f" f))]
                Html.row "Average" [Incremental.text (prop.avg |> AVal.map (fun f -> sprintf "%.2f" f))]
            ]
          )
     
    
    let button (prop: AdaptiveProperty) = 
               button [clazz "fluid inverted ui button"; onClick (fun _ -> UpdateProperty (UpdateHistogram AddBin, prop.kind))][                        
                   text "Add Bin"
               ]

    let binSelectionView (prop: AdaptiveProperty) =      
               

        Incremental.div (AttributeMap.ofList [style "width:100%; margin: 10 5 5 5"]) (

            alist{                   
                   let rowList = prop.histogram.bins 
                                 |> AMap.map (fun k bin -> 
                                                Html.row ("Bin") 
                                                    [   
                                                        Html.Layout.boxH [text "start"; Numeric.view' [InputBox] bin.start |> UI.map SetStart |> UI.map (fun a -> UpdateBin (a, k)) |> UI.map UpdateHistogram |> UI.map (fun a -> UpdateProperty (a, prop.kind))]
                                                        Html.Layout.boxH [text "end"; Numeric.view' [InputBox] bin.theEnd |> UI.map SetEnd |> UI.map (fun a -> UpdateBin (a, k)) |> UI.map UpdateHistogram |> UI.map (fun a -> UpdateProperty (a, prop.kind))                                                                     ]
                                                    ]                                                       
                                             )
                                |> AMap.toASet 
                                |> ASet.toAList
                                |> AList.map(fun (_,b) -> b) 
                   
                                            
                   Incremental.table ([clazz "ui celled striped inverted table unstackable"] |> AttributeMap.ofList) rowList


                   

            }



        )
        

        
    let view (m: AdaptiveAnnoStatsModel) =          
        

        Incremental.div (AttributeMap.ofList [style "width:100%; margin: 10 0 10 10"]) 
        
            (                                  
               m.properties |> AMap.map(fun p v -> propListing v) |> AMap.toASet |> ASet.toAList |> AList.map(fun (a,b) -> b)                                                                 
            )
        
        //let style' = "color: white; font-family:Consolas;"   
        //let s = m.selectedAnnotations |> AMap.isEmpty        

        //Incremental.div (AttributeMap.ofList [style style']) (
        
        //    alist {

        //        let! empty = s
        //        match empty with
        //            | true -> div [style "width:100%; margin: 10 0 10 10"][text "Please select some annotations"]
                                                   
                          
        //            | false -> let text1 = m.selectedAnnotations |> AMap.count |> AVal.map (fun n -> sprintf "%s annotation(s) selected" (n.ToString()))
        //                       Incremental.div (AttributeMap.ofList [style "width:100%; margin: 10 0 10 10"]) (
        //                            alist{
        //                                Incremental.text text1
        //                            }                                    
        //                          )
                               
        //                       div [style "width:100%; margin: 10 5 10 10"][                                
        //                         text "Please select a property to see statistics" 
        //                         propDropdown
        //                       ]

        //                       Incremental.div (AttributeMap.ofList [style "width:100%; margin: 10 0 10 10"]) (                                   
        //                               m.properties |> AList.map(fun prop -> propListing prop)                                                                       
        //                         )
                              
                                                              

                               
                            
                           
        //    }
        //)




          
        
       
                  
        
        
                   
        


        



        

    

            
            
           
                                                                        
           
        
        
       
            
            
           


        




                                  
                                                               
                                                               

        
                                  
            
            
            




