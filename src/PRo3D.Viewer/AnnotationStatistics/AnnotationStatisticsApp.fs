namespace Pro3D.AnnotationStatistics

open System
open PRo3D.Base
open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive


module AnnotationStatisticsApp =

    type AnnoStatsAction =
        | SetSelected 

    let private calcMinMaxAvg (l:List<float>) =
        match (l.IsEmpty) with
        | true -> HashMap.empty        
        | false -> let min = l |> List.min
                   let max = l |> List.max
                   let avg = l |> List.average
                   [("min", min); ("max", max); ("avg", avg)] |> HashMap.ofList
                   


    let calculateStats (selected:List<Annotation>) =

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
            
            let lens,bears = calculateStats (newList |> HashMap.toValueList)
            
            let stats = 
                        {
                            lengthStats = lens
                            bearingStats = bears
                        }
            
            {m with selectedAnnotations = newList; annoStatistics = stats}            
                         
    
        
    
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
                               div [clazz "content"] [
                                   table [] [
                                       tr[][                                            
                                           td[style style'][text "Length: "]                                        
                                       ]
                                       tr[][
                                            
                                           td[style style'][Incremental.text lengthStats]                                         
                                       ]
                                       tr[][                                            
                                           td[style style'][text "Bearing: "]                                        
                                       ]
                                       tr[][
                                           td[style style'][Incremental.text bearingStats]                                          
                                       ]
                                   ]
                                   
                               ]            
                           ]
            }
        )     

          
        
       
                  
        
        
                   
        


        



        

    

            
            
           
                                                                        
           
        
        
       
            
            
           


        




                                  
                                                               
                                                               

        
                                  
            
            
            




