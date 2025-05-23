namespace PRo3D.Core

open FSharp.Data.Adaptive

type Interactions =
    | PickExploreCenter     = 0
    | PlaceCoordinateSystem = 1  // compute up north vector at that point
    | DrawAnnotation        = 2
    | PlaceRover            = 3
    | TrafoControls         = 4
    | PlaceSurface          = 5
    | PickAnnotation        = 6
    | PickSurface           = 7
    | PickMinervaProduct    = 8
    | PickMinervaFilter     = 9
    | PickLinking           = 10
    | DrawLog               = 11
    | PickLog               = 12
    | PlaceValidator        = 13
    | TrueThickness         = 14 // CHECK-merge
    | SelectArea            = 15
    | PlaceScaleBar         = 16
    | PlaceSceneObject      = 17
    | PickPivotPoint        = 18
    | PickSurfaceRefSys     = 19
    | PickDistanePoint      = 20
    

module Interactions =
    // excludes interactions from dropdown in topmenu
    let hideSet = 
        [            
            //Interactions.PickExploreCenter    
            //Interactions.PlaceCoordinateSystem
            //Interactions.DrawAnnotation       
            //Interactions.PlaceRover           
            Interactions.TrafoControls        
            Interactions.PlaceSurface         
            //Interactions.PickAnnotation       
            //Interactions.PickSurface          
            Interactions.PickMinervaProduct   
            Interactions.PickMinervaFilter    
            Interactions.PickLinking          
            Interactions.DrawLog              
            Interactions.PickLog              
            Interactions.PlaceValidator       
            Interactions.TrueThickness        
            //Interactions.PlaceScaleBar        
            //Interactions.PlaceSceneObject     
        ] |> HashSet.ofList