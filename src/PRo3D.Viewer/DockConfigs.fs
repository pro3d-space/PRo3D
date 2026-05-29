namespace PRo3D.Viewer

open Aardvark.UI.Primitives.Golden

module DockConfigs =

    let full =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render";         title "Main View" }
                    element { id "instrumentview"; title "Instrument View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";      title "Surfaces" }
                        element { id "annotations";   title "Annotations" }
                        element { id "minerva";       title "Minerva" }
                        element { id "scalebars";     title "ScaleBars" }
                        element { id "gis";           title "GIS View" }
                    }
                    stack {
                        weight 5
                        element { id "config";         title "Config" }
                        element { id "bookmarks";      title "Bookmarks" }
                        element { id "viewplanner";    title "ViewPlanner" }
                        element { id "corr_mappings";  title "RockTypes" }
                        element { id "corr_semantics"; title "Semantics" }
                    }
                }
            }
        }

    let viewPlanner =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render";         title "Main View" }
                    element { id "instrumentview"; title "Instrument View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";    title "Surfaces" }
                        element { id "annotations"; title "Annotations" }
                        element { id "scalebars";   title "ScaleBars" }
                    }
                    stack {
                        weight 5
                        element { id "config";      title "Config" }
                        element { id "bookmarks";   title "Bookmarks" }
                        element { id "viewplanner"; title "ViewPlanner" }
                    }
                }
            }
        }

    let m2020 =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render"; title "Main View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";       title "Surfaces" }
                        element { id "annotations";    title "Annotations" }
                        element { id "scalebars";      title "ScaleBars" }
                        element { id "instrumentview"; title "Instrument View" }
                    }
                    stack {
                        weight 5
                        element { id "config";              title "Config" }
                        element { id "bookmarks";           title "Bookmarks" }
                        element { id "sequencedBookmarks";  title "Seq. Bookmarks" }
                        element { id "viewplanner";         title "Viewplans" }
                        element { id "properties";          title "Properties" }
                        element { id "traverse";            title "Traverses" }
                    }
                }
            }
        }

    let gis =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render"; title "Main View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";       title "Surfaces" }
                        element { id "annotations";    title "Annotations" }
                        element { id "scalebars";      title "ScaleBars" }
                        element { id "instrumentview"; title "Instrument View" }
                        element { id "gis";            title "GIS View" }
                    }
                    stack {
                        weight 5
                        element { id "config";             title "Config" }
                        element { id "bookmarks";          title "Bookmarks" }
                        element { id "sequencedBookmarks"; title "Seq. Bookmarks" }
                        element { id "viewplanner";        title "Viewplans" }
                        element { id "properties";         title "Properties" }
                        element { id "traverse";           title "Traverses" }
                    }
                }
            }
        }

    let minerva =
        layout {
            row {
                column {
                    weight 7
                    stack {
                        weight 7
                        element { id "render";         title "Main View" }
                        element { id "instrumentview"; title "Instrument View" }
                    }
                    stack {
                        weight 3
                        element { id "linking"; title "Linking View" }
                    }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "minerva";     title "Minerva" }
                        element { id "surfaces";    title "Surfaces" }
                        element { id "annotations"; title "Annotations" }
                    }
                    stack {
                        weight 5
                        element { id "config";         title "Config" }
                        element { id "bookmarks";      title "Bookmarks" }
                        element { id "viewplanner";    title "ViewPlanner" }
                        element { id "corr_mappings";  title "RockTypes" }
                        element { id "corr_semantics"; title "Semantics" }
                    }
                }
            }
        }

    let correlations =
        layout {
            row {
                column {
                    weight 7
                    stack {
                        weight 7
                        element { id "render"; title "Main View" }
                    }
                    stack {
                        weight 3
                        element { id "corr_svg";       title "CorrelationPanel" }
                        element { id "corr_semantics"; title "Semantics" }
                    }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";    title "Surfaces" }
                        element { id "annotations"; title "Annotations" }
                        element { id "corr_logs";   title "Logs" }
                    }
                    stack {
                        weight 5
                        element { id "config";        title "Config" }
                        element { id "bookmarks";     title "Bookmarks" }
                        element { id "corr_mappings"; title "RockTypes" }
                    }
                }
            }
        }

    let extended =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render"; title "Main View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";           title "Surfaces" }
                        element { id "annotations";        title "Annotations" }
                        element { id "bookmarks";          title "Bookmarks" }
                        element { id "sequencedBookmarks"; title "Seq. Bookmarks" }
                        element { id "sceneobjects";       title "Scene Objects" }
                    }
                    stack {
                        weight 5
                        element { id "properties";   title "Properties" }
                        element { id "config";        title "Config" }
                        element { id "scalebars";     title "Scale Bars" }
                        element { id "geologicSurf";  title "Geologic Surfaces" }
                    }
                }
            }
        }

    let core =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render"; title "Main View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";    title "Surfaces" }
                        element { id "annotations"; title "Annotations" }
                        element { id "scalebars";   title "ScaleBars" }
                    }
                    stack {
                        weight 5
                        element { id "config";      title "Config" }
                        element { id "bookmarks";   title "Bookmarks" }
                        element { id "scaletools";  title "Scale Tools" }
                    }
                }
            }
        }

    let traverse =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render"; title "Main View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";     title "Surfaces" }
                        element { id "annotations";  title "Annotations" }
                        element { id "bookmarks";    title "Bookmarks" }
                        element { id "sceneobjects"; title "Scene Objects" }
                    }
                    stack {
                        weight 5
                        element { id "properties"; title "Properties" }
                        element { id "config";     title "Config" }
                        element { id "scalebars";  title "Scale Bars" }
                        element { id "traverse";   title "Traverse" }
                    }
                }
            }
        }

    let comparison =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render"; title "Main View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";    title "Surfaces" }
                        element { id "annotations"; title "Annotations" }
                        element { id "bookmarks";   title "Bookmarks" }
                        element { id "comparison";  title "Comparison" }
                    }
                    stack {
                        weight 5
                        element { id "properties"; title "Properties" }
                        element { id "config";     title "Config" }
                    }
                }
            }
        }

    let renderOnly =
        layout {
            stack {
                element { id "render"; title "Main View" }
            }
        }

    let provenance =
        layout {
            row {
                stack {
                    weight 7
                    element { id "render"; title "Main View" }
                }
                column {
                    weight 3
                    stack {
                        weight 5
                        element { id "surfaces";    title "Surfaces" }
                        element { id "annotations"; title "Annotations" }
                        element { id "scalebars";   title "ScaleBars" }
                        element { id "provenance";  title "Provenance" }
                    }
                    stack {
                        weight 5
                        element { id "config";     title "Config" }
                        element { id "bookmarks";  title "Bookmarks" }
                        element { id "scaletools"; title "Scale Tools" }
                    }
                }
            }
        }
