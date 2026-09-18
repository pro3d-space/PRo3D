namespace PRo3D.Viewer

open Aardvark.UI.Primitives.Golden

/// A built-in window layout, offered in the Layout menu.
type DashboardMode =
    {
        layout : WindowLayout
        name   : string
    }

module DashboardModes =
    let m2020 =
        { name = "M2020"; layout = DockConfigs.m2020 }

    let core =
        { name = "PRo3D Core"; layout = DockConfigs.core }

    let comparison =
        { name = "Surface Comparison"; layout = DockConfigs.comparison }

    let renderOnly =
        { name = "Render Only"; layout = DockConfigs.renderOnly }

    let provenance =
        { name = "Provenance"; layout = DockConfigs.provenance }

    let gis =
        { name = "GIS"; layout = DockConfigs.gis }

    /// the layout of a first start, and of a broken or missing current layout
    let defaultDashboard = m2020

    /// in menu order
    let all = [ m2020; core; comparison; renderOnly; provenance; gis ]
