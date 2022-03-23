namespace Pro3D.AnnotationStatistics

open System
open PRo3D.Base
open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open Aardvark.SceneGraph
open PRo3D.Core
open FSharp.Data.Adaptive


module AnnotationStatisticsApp =

    type AnnoStatsAction =
        | SetSelected 

       
    
    //recursively builds a list of bins and sorts values into them
    //li = bin list to build, sort = list of values to sort into bins; k = number of bins, r = rounds, acc = accumulated rangeStart, binW = bin width
    let rec binList li sort k r acc binW =
        if (r > k) then (li |> IndexList.ofList)
        else 
            let bin = 
                        let rS = acc
                        let rE = acc + binW
                        let inBin = sort |> List.filter(fun f -> f >= rS && f <= rE)                       

                        [             
                            {
                                id = Guid.NewGuid()
                                value = inBin.Length
                                rangeStart = rS
                                rangeEnd = rE
                            }
                        ]
            binList (li @ bin) sort k (r+1) (acc+binW) binW

        
    
    let calculateHistogram (m:AnnoStatsModel) (values:List<float>) =
        
        match (values.IsEmpty) with 
         | true -> m.annoStatistics.histogram
         | false -> let n = float(values.Length) 
                    let k = 1.0 + 3.322 * (log n) //Sturges' rule, number of bins
                    let min = values |> List.min
                    let max = values |> List.max
                    let binWidth = (max - min)/k

                    //set up the bins
                    let bins = binList List.empty values (int(k)) 1 min binWidth
                  
                    {
                        id = Guid.NewGuid()
                        numOfBins = bins.Count
                        bins = bins
                    }                               

    
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
           let hist = calculateHistogram m bearings
           
           (lengthStats, bearingStats, hist)


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
            
            let lens,bears,hist = calculateStats m (newList |> HashMap.toValueList)
            
            
            

            let stats = 
                        {
                            lengthStats = lens
                            bearingStats = bears
                            histogram = hist
                        }
            
            {m with selectedAnnotations = newList; annoStatistics = stats}            
                         
    
    let drawHistogram (h: AdaptiveHistogram) =

        let attr (bin:Bin) = 
            amap{
                yield style "fill:white;stroke:green;stroke-width:2;fill-opacity:0.1;stroke-opacity:0.9"                
                yield attribute "x" (sprintf "%ipx" (int(bin.rangeStart)))
                yield attribute "y" (sprintf "%ipx" (int(bin.rangeStart + float(bin.value))))
                yield attribute "width" (sprintf "%ipx" (int(bin.rangeEnd - bin.rangeStart)))
                yield attribute "height" (sprintf "%ipx" (bin.value * 10))
            } |> AttributeMap.ofAMap


        h.bins |> AList.map (fun b -> Incremental.Svg.rect (attr b))


        
    let view (m: AdaptiveAnnoStatsModel) =          
        
        let style' = "color: white; font-family:Consolas;"   
        let s = m.selectedAnnotations |> AMap.isEmpty        

        Incremental.div (AttributeMap.ofList [style style']) (
        
            alist {

            let! empty = s
            match empty with
                | true -> Incremental.text (AVal.constant "No annotations selected")
                | false -> let stats = m.annoStatistics                            
                                                      
                           let lengthStats = ("",stats.lengthStats) ||> AMap.fold(fun str (k:string) (v:float) -> sprintf "%s %s:%.2f  " str k v)
                           let bearingStats = ("",stats.bearingStats) ||> AMap.fold(fun str (k:string) (v:float) -> sprintf "%s %s:%.2f  " str k v)                              
                           

                           GuiEx.accordion "Statistics of selected" "Asterisk" true [
                            require GuiEx.semui (
                               Html.table [      
                                   Html.row "Length:"      [Incremental.text lengthStats] 
                                   Html.row "Bearing:"     [Incremental.text bearingStats]                              
                               ]
                            )

                            //for testing purposes
                            Svg.svg [style "width:200px;height:200px"] [
                            
                                Svg.rect (
                                              [
                                                 style "fill:white;stroke:green;stroke-width:2;fill-opacity:0.1;stroke-opacity:0.9"                
                                                 attribute "x" "0"
                                                 attribute "y" "20"
                                                 attribute "width" "20"
                                                 attribute "height" "20"
                                              ]
                                          )

                            ]
                           
                           ]
                            
                           
            }
        )
        
        

          
        
       
                  
        
        
                   
        


        



        

    

            
            
           
                                                                        
           
        
        
       
            
            
           


        




                                  
                                                               
                                                               

        
                                  
            
            
            




