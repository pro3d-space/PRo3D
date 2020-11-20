namespace PRo3D.Viewer

open Aardvark.UI.Primitives

module DockConfigs =

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
            appName "PRo3D - minerva"
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
                        ]                          
                        stack 0.5 (Some "config") [
                            {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "viewplanner"; title = Some " ViewPlanner "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]
                    ]
                ]
            )
            appName "PRo3D - extended"
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
                        stack 0.5 (Some "validation") [                        
                            {id = "surfaces"; title = Some " Surfaces "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "annotations"; title = Some " Annotations "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "validation"; title = Some " Validation "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                        ]                          
                        stack 0.5 (Some "config") [
                            {id = "config"; title = Some " Config "; weight = 0.4; deleteInvisible = None; isCloseable = None }
                            {id = "bookmarks"; title = Some " Bookmarks"; weight = 0.4; deleteInvisible = None; isCloseable = None }                       
                        ]
                    ]
                ]                        
            )
            appName "PRo3D"
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
            appName "PRo3D - render only"
            useCachedConfig false
        }

