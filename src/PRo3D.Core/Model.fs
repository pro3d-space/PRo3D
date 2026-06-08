namespace PRo3D.Core

open FSharp.Data.Adaptive

type Interactions =
    | PickExploreCenter     = 0
    | PlaceCoordinateSystem = 1  // compute up north vector at that point
    | DrawAnnotation        = 2
    | PlaceRoverViewPlan    = 3
    | PlaceRoverModel       = 4
    | TrafoControls         = 5
    | PlaceSurface          = 6
    | PickAnnotation        = 7
    | PickSurface           = 8
    | PickMinervaProduct    = 9
    | PickMinervaFilter     = 10
    | PickLinking           = 11
    | DrawLog               = 12
    | PickLog               = 13
    | PlaceValidator        = 14
    | TrueThickness         = 15 // CHECK-merge
    | SelectArea            = 16
    | PlaceScaleBar         = 17
    | PlaceSceneObject      = 18
    | PickPivotPoint        = 19
    | PickSurfaceRefSys     = 20
    | PickDistancePoint     = 21
    
    

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