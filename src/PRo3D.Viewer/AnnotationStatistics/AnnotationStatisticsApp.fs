﻿namespace Pro3D.AnnotationStatistics

open System
open PRo3D.Base
open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive


module AnnotationStatisticsApp =

    type HistogramAction =
        | Update of list<float>

    type AnnoStatsAction =
        | SetSelected 
        | DrawHistogram of HistogramAction

    

       
    
    //recursively builds a list of bins and sorts values into them
    //li = bin list to build, sort = list of values to sort into bins; k = number of bins, r = rounds, acc = accumulated rangeStart, binW = bin width
    let rec binList li (sort:List<float>) k r (acc:int) (binW:int) =
        if (r > k) then (li |> IndexList.ofList)
        else 
            let bin = 
                        let rS = acc
                        let rE = acc + binW
                        let inBin = sort |> List.filter(fun f -> f >= float(rS) && f <= float(rE))                       

                        [             
                            {
                                id = Guid.NewGuid()
                                value = inBin.Length
                                start = rS
                                theEnd = rE
                                width = binW
                            }
                        ]
            binList (li @ bin) sort k (r+1) (acc+binW) binW

        
    
    let updateHistogram (h:Pro3D.AnnotationStatistics.Histogram) (values:List<float>) (title:string) =
        
        match (values.IsEmpty) with 
         | true -> h
         | false -> let n = float(values.Length)                     
                    let k = int(round(1.0 + 3.322 * (System.Math.Log10 n))) //Sturges' rule, number of bins
                    let min = int(floor(values |> List.min))
                    let max = int(round(values |> List.max))
                    let binWidth = ((max - min)/k) + 1 //+1 to prevent rounding issues

                    //set up the bins
                    let bins = binList List.empty values (int(k)) 1 min binWidth
                    
                    {h with title = title; numOfBins = bins.Count; rangeStart = min; rangeEnd = max; bins = bins}                                                

    
    let private calcMinMaxAvg (l:List<float>) =
           match (l.IsEmpty) with
           | true -> HashMap.empty        
           | false -> let min = l |> List.min
                      let max = l |> List.max
                      let avg = l |> List.average
                      [("min", min); ("max", max); ("avg", avg)] |> HashMap.ofList
                      
       



    let calculateStats (m: AnnoStatsModel) (selected:List<Annotation>) =

           let lengths = selected 
                                   |> List.map(fun a -> match a.results with
                                                               | Some r -> Some(r.length)
                                                               | None -> None
                                                   )
                                   |> List.choose(fun o -> o)

           let bearings = selected 
                                   |> List.map(fun a -> match a.results with
                                                               | Some r -> Some(r.bearing)
                                                               | None -> None
                                                   )
                                   |> List.choose(fun o -> o)

           let lengthStats = calcMinMaxAvg lengths  
           let bearingStats = calcMinMaxAvg bearings

           //testing - histogram of lengths
           //let hist = updateHistogram m.annoStatistics.histogram bearings "Bearing"
           
           (lengthStats, bearingStats)


    let update (m:AnnoStatsModel) (Aid:Guid) (g:GroupsModel) (a:AnnoStatsAction) =
        match a with
        | SetSelected ->           
            let anno = match (g.flat |> HashMap.tryFind Aid) with
                        | Some l -> Some(Leaf.toAnnotation l)
                        | None -> None
            
            let newList = match anno with 
                            | Some a -> let exists = m.selectedAnnotations |> HashMap.tryFind Aid
                                        match exists with
                                        | Some v -> m.selectedAnnotations
                                        | None -> m.selectedAnnotations.Add (Aid,a)                            
                            
                            | None -> m.selectedAnnotations   
            
            //let lens,bears= calculateStats m (newList |> HashMap.toValueList)            
                        

            //let stats = {m.annoStatistics with lengthStats = lens; bearingStats = bears}                        
            
            {m with selectedAnnotations = newList}            
    
    //project the domain values to pixel values
    let projectToPx x min max =
        //let a = 0 
        let b = 300.0
        match (max - min) with
        | 0 -> 0
        | _ -> let temp1 = float(x - min)
               let temp2 = float(max - min)
               let temp3 = (temp1 / temp2) * b            
               let res = int(temp3)
               res
     
    

    
    let drawHistogram (h: AdaptiveHistogram) = 
        
        let w = 15
        let height = 5
        let offSetY = 100


        let attr (bin:Bin) = 
            amap{
                let! min = h.rangeStart
                let! max = h.rangeEnd           
                
                yield style "fill:green;fill-opacity:1.0;"                
                yield attribute "x" (sprintf "%ipx" (projectToPx (bin.start+w) min max))
                yield attribute "y" (sprintf "%ipx" ((height-(bin.value*10)+offSetY)))
                yield attribute "width" (sprintf "%ipx" w) 
                yield attribute "height" (sprintf "%ipx" (bin.value * 10)) 
            } |> AttributeMap.ofAMap
        
        let labelAttr (bin: Bin) = 
            amap {
                let! min = h.rangeStart
                let! max = h.rangeEnd
                yield attribute "x" (sprintf "%ipx" (projectToPx (bin.start+w) min max))
                yield attribute "y" "90"
                yield attribute "text-anchor" "center"
                yield attribute "font-size" "7"
                yield attribute "fill" "#ffffff"               
            }|> AttributeMap.ofAMap
        
        alist {
                let bins = h.bins
                for b in bins do
                    let text = sprintf "%i-%i" b.start b.theEnd
                    yield  Incremental.Svg.rect (attr b)
                    yield  Incremental.Svg.text (labelAttr b)(AVal.constant text)
                
                //let! t = h.title
               //Svg.text ([])
        }


        
    let view (m: AdaptiveAnnoStatsModel) =          
        
        let style' = "color: white; font-family:Consolas;"   
        let s = m.selectedAnnotations |> AMap.isEmpty        

        Incremental.div (AttributeMap.ofList [style style']) (
        
            alist {

            let! empty = s
            match empty with
                | true -> Incremental.text (AVal.constant "No annotations selected")
                          
                | false -> Incremental.text (AVal.constant "Placeholder domnode")
                           //let stats = m.annoStatistics                            
                                                      
                           //let lengthStats = ("",stats.lengthStats) ||> AMap.fold(fun str (k:string) (v:float) -> sprintf "%s %s:%.2f  " str k v)
                           //let bearingStats = ("",stats.bearingStats) ||> AMap.fold(fun str (k:string) (v:float) -> sprintf "%s %s:%.2f  " str k v)                              
                           

                           //GuiEx.accordion "Statistics of selected" "Asterisk" true [
                                
                           //     //table of min, max, avg
                           //     require GuiEx.semui (
                           //         Html.table [      
                           //             Html.row "Length:"      [Incremental.text lengthStats] 
                           //             Html.row "Bearing:"     [Incremental.text bearingStats]      
                                         
                           //                    ]
                           //     )
                                                              

                            
                           //     //histogram
                           //     Incremental.Svg.svg (AttributeMap.ofList [style "width:300px;height:300px";]) 
                           //         (drawHistogram stats.histogram)
                            

                           
                           //]
                            
                           
            }
        )
        
        

          
        
       
                  
        
        
                   
        


        



        

    

            
            
           
                                                                        
           
        
        
       
            
            
           


        




                                  
                                                               
                                                               

        
                                  
            
            
            



