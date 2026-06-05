namespace PRo3D.Viewer

open Aardvark.UI.Primitives.Golden

type DashboardMode =
    {
        layout : WindowLayout
        name   : string
    }

module DashboardModes =
    let comparison =
        { name = "Comparison"; layout = DockConfigs.comparison }

    let core =
        { name = "Core"; layout = DockConfigs.core }

    let renderOnly =
        { name = "3D-View Only"; layout = DockConfigs.renderOnly }

    let defaultDashboard =
        { name = "default"; layout = DockConfigs.m2020 }

    let provenance =
        { name = "Provenance"; layout = DockConfigs.provenance }

    let gis =
        { name = "GIS"; layout = DockConfigs.gis }
