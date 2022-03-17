namespace Pro3D.AnnotationStatistics

open System
open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive


module AnnotationStatisticsApp =

    type AnnoStatsAction =
        | SetSelected 

    let update (m:AnnoStatsModel) (Aid:Guid) (g:GroupsModel) (a:AnnoStatsAction) =
        match a with
        | SetSelected ->           
            let anno = match (g.flat |> HashMap.tryFind Aid) with
                        | Some l -> Some(Leaf.toAnnotation l)
                        | None -> None
            
            let newList = match anno with 
                        | Some a -> m.selectedAnnotations.Add a
                        | None -> m.selectedAnnotations

            let lengths = newList |> IndexList.map (fun (a:Annotation) -> Vec.Distance(a.points.[0], a.points.[a.points.Count-1]) )
            
            {m with selectedAnnotations = newList; selectedLengths = lengths}            
                         
    
        
    
    let view (m: AdaptiveAnnoStatsModel) =        
        let l = m.selectedLengths               
                   
        let style' = "color: white; font-family:Consolas;"       
                   
        div [clazz "content"] [
            
            yield Incremental.div (AttributeMap.ofList [style style']) (

                l |> AList.map (fun f -> Incremental.text (AVal.constant (sprintf "Length : %f" f)))

            )
        ]


        



        

    

            
            
           
                                                                        
           
        
        
       
            
            
           


        




                                  
                                                               
                                                               

        
                                  
            
            
            




