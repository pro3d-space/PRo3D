namespace Pro3D.AnnotationStatistics

open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive


module AnnotationStatisticsApp =

    type AnnoStatsAction =
        | SetSelected 

    let update (m:AnnoStatsModel) (g:GroupsModel) (a:AnnoStatsAction) =
        match a with
        | SetSelected -> 
            let annoList = g.selectedLeaves 
                                                    |> HashSet.toList 
                                                    |> List.map (fun x -> match (g.flat |> HashMap.tryFind x.id) with
                                                                                             | Some v -> Some(x.id, v)
                                                                                             | None -> None
                                                                                )
                                                    |> List.choose (fun i -> i)
                                                    |> HashMap.ofList
                                                    |> Leaf.toAnnotations
                                                    |> HashMap.toList
                                                    |> List.map (fun (_,a) -> a)
                                                    |> AList.ofList
            
            let lengths = annoList |> AList.map (fun (a:Annotation) -> Vec.Distance(a.points.[0], a.points.[a.points.Count-1]) )
                        
            {m with selectedAnnotations = annoList; selectedLengths = lengths}   
    

    //let calcLengths (m: AnnoStatsModel) =
    //    m.selectedAnnotations |> AList.map (fun (a:Annotation) -> Vec.Distance(a.points.[0], a.points.[a.points.Count-1]) )
        
    
    let view (m: AdaptiveAnnoStatsModel) =
        
        //let l = m.selectedLengths |> AVal.map (fun v -> )
        let style' = "color: white; font-family:Consolas;"
                     
        div[][
               table [] [
                            tr[][
                                td[style style'][text "Test: "]                                
                            ]
                    
               ]              
        ]
        

    

            
            
           
                                                                        
           
        
        
       
            
            
           


        




                                  
                                                               
                                                               

        
                                  
            
            
            




