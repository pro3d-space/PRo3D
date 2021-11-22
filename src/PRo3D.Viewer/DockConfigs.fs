namespace PRo3D.Viewer

open Aardvark.UI.Primitives

module DockConfigs =

    let full = 
        config {
            content (                    
                horizontal 1.0 [                                                        
                    stack 0.7 None [
                        {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                        {id = "instrumentview"; title = Some " Instrument View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                    ]                            
                    vertical 0.3 [
                        stack 0.5 (Some "surfaces") [                    
                            {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "minerva"; title = Some " Minerva "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "scalebars"; title = Some " ScaleBars "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "config") [
                            {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "viewplanner"; title = Some " ViewPlanner "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "corr_mappings"; title = Some " RockTypes "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "corr_semantics"; title = Some " Semantics "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]
                    ]
                ]              
            )
            appName "PRo3D"
            useCachedConfig false
        }

    let viewPlanner = 
        config {
            content (                    
                horizontal 1.0 [                                                        
                    stack 0.7 None [
                        {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                        {id = "instrumentview"; title = Some " Instrument View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                    ]                            
                    vertical 0.3 [
                        stack 0.5 (Some "surfaces") [                    
                            {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }                            
                            {id = "scalebars"; title = Some " ScaleBars "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "config") [
                            {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "viewplanner"; title = Some " ViewPlanner "; weight = 0.4; deleteInvisible = None; isCloseable = None }                            
                        ]
                    ]
                ]              
            )
            appName "PRo3D"
            useCachedConfig false
        }

    let m2020 = 
        config {
            content (                    
                horizontal 1.0 [                                                        
                    stack 0.7 None [
                        { id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}                        
                    ]                            
                    vertical 0.3 [
                        stack 0.5 (Some "surfaces") [                    
                            { id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }                            
                            { id = "scalebars"; title = Some " ScaleBars "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "instrumentview"; title = Some " Instrument View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                        ]                          
                        stack 0.5 (Some "config") [
                            { id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "sequencedBookmarks"; title = Some " SequBookmarks "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "viewplanner"; title = Some " ViewPlanner "; weight = 0.4; deleteInvisible = None; isCloseable = None }                    
                            { id = "traverse";   title = Some " Traverse"; weight = 0.4; deleteInvisible = None; isCloseable = None }              
                        ]
                    ]
                ]              
            )
            appName "PRo3D"
            useCachedConfig false
        }

    let minerva =
        config {
            content (                        
                horizontal 1.0 [                                                        
                    vertical 0.7 [
                        stack 0.7 None [
                            {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                            {id = "instrumentview"; title = Some " Instrument View "; weight = 0.6; deleteInvisible = None; isCloseable = None}
                        ]                            
                        stack 0.3 (Some "linking") [
                            {id = "linking"; title = Some " Linking View "; weight = 1.0; deleteInvisible = None; isCloseable = None}
                        ]
                    ]
                    vertical 0.3 [
                        stack 0.5 (Some "minerva") [                    
                            {id = "minerva"; title = Some " Minerva "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "config") [
                            {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "viewplanner"; title = Some " ViewPlanner "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "corr_mappings"; title = Some " RockTypes "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "corr_semantics"; title = Some " Semantics "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]
                    ]
                ]
            )
            appName "PRo3D - full"
            useCachedConfig false
        }

    let correlations = 
        config {
            content (
                horizontal 1.0 [                                                                              
                    vertical 0.7 [
                        stack 0.7 (Some "render") [                    
                          {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}                                      
                        ]
                        stack 0.3 (Some "corr_svg") [
                          {id = "corr_svg"; title = Some " CorrelationPanel "; weight = 0.4; deleteInvisible = None; isCloseable = None }                  
                          {id = "corr_semantics"; title = Some " Semantics "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]
                    ]                                      
                    vertical 0.3 [
                        stack 0.5 (Some "surfaces") [                    
                          {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                          {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }                    
                          {id = "corr_logs"; title = Some " Logs "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "config") [
                          {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                          {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }                    
                          {id = "corr_mappings"; title = Some " RockTypes "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]
                    ]
                ]              
            )
            appName "PRo3D - correlations"
            useCachedConfig false
        }

    let extended =
        config {
            content (                        
                horizontal 1.0 [                                                        
                    stack 0.7 None [
                        {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}                       
                    ]                            
                    vertical 0.3 [
                        stack 0.5 (Some "annotations") [                        
                            {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "sequencedBookmarks"; title = Some " SequBookmarks "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "sceneobjects"; title = Some " SceneObjects "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                           // {id = "validation"; title = Some " Validation "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "properties") [
                            {id = "properties"; title = Some " Properties "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "scalebars"; title = Some " ScaleBars"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "geologicSurf"; title = Some " GeologicSurfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            //{id = "viewplanner"; title = Some " ViewPlanner "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]
                    ]
                ]                        
            )
            appName "PRo3D"
            useCachedConfig false
        }

    let core = 
        config {
            content (                        
                horizontal 1.0 [                                                        
                    stack 0.7 None [
                        {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}                       
                    ]                            
                    vertical 0.3 [
                        stack 0.5 None [                        
                          {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                          {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                          {id = "scalebars"; title = Some " ScaleBars "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "config") [
                          {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                          {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }  
                          {id = "scaletools"; title = Some " Scale Tools"; weight = 0.4; deleteInvisible = None; isCloseable = None }                       
                        ]
                    ]
                ]                        
            )
            appName "PRo3D"
            useCachedConfig false
        }
    let traverse =
        config {
            content (                        
                horizontal 1.0 [                                                        
                    stack 0.7 None [
                        { id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None }
                    ]                            
                    vertical 0.3 [
                        stack 0.5 (Some "annotations") [                        
                            { id = "surfaces";     title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "annotations";  title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "bookmarks";    title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "sceneobjects"; title = Some " Scene Objects "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                           // {id = "validation"; title = Some " Validation "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "properties") [
                            { id = "properties"; title = Some " Properties "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "config";     title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "scalebars";  title = Some " Scale Bars"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            { id = "traverse";   title = Some " Traverse"; weight = 0.4; deleteInvisible = None; isCloseable = None }                            
                        ]
                    ]
                ]                        
            )
            appName "PRo3D"
            useCachedConfig false
        }

    let comparison = 
        config {
            content (                        
                horizontal 1.0 [                                                        
                    stack 0.7 None [
                        {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}                       
                    ]                            
                    vertical 0.3 [
                        stack 0.5 (Some "annotations") [                        
                            {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "comparison"; title = Some "Comparison"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                           // {id = "validation"; title = Some " Validation "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "properties") [
                            {id = "properties"; title = Some " Properties "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]
                    ]
                ]                        
            )
            appName "PRo3D Surface Comparison"
            useCachedConfig false
        }

    let renderOnly =
        config {
            content (                        
                horizontal 1.0 [                                                        
                    stack 1.0 None [
                        {id = "render"; title = Some " Main View "; weight = 0.6; deleteInvisible = None; isCloseable = None}                       
                    ]                                                
                ]                        
            )
            appName "PRo3D"
            useCachedConfig false
        }

