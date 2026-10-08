namespace PRo3D

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Viewer

/// The scene body (#758): the global planet (`ReferenceSystem.planet`) and the GIS
/// observation (observed body + reference frame) are one setting. Everything that writes
/// either goes through here, so the two cannot drift apart:
///
/// - picking a planet globally points the GIS at that body in its fixed frame (`setPlanet`),
/// - picking an observed body or frame in the GIS view sets the planet (`followObservation`),
/// - loading a scene whose GIS observation is body-fixed fills in the planet (`reconcileOnLoad`).
///
/// A scene observed in another frame (e.g. J2000, saved before this) is left as saved.
/// See docs/SceneBody.md.
/// (Named apart from PRo3D.Base.Gis.SceneBody, the pure body/frame table this builds on.)
module SceneBodySync =

    /// A reference-system action and everything that follows from it for surfaces and
    /// annotations (PRo3D.Composition.ReferenceSystemSync, shared with PRo3D.Lite). The camera
    /// is the caller's: whether its sky moved is a comparison across this call.
    let applyReferenceSystemAction (a : ReferenceSystemAction) (m : Model) =
        let refSystem, surfaces, drawing =
            PRo3D.Composition.ReferenceSystemSync.apply
                m.scene.config a m.scene.referenceSystem m.scene.surfacesModel m.drawing
        { m with
            scene   = { m.scene with referenceSystem = refSystem; surfacesModel = surfaces }
            drawing = drawing }

    /// The planet only; the GIS observation is the caller's.
    let private setPlanetOnly (planet : Planet) (m : Model) =
        applyReferenceSystemAction (ReferenceSystemAction.SetPlanet planet) m

    /// The global planet choice: the planet, and the GIS observing its body in its fixed
    /// frame - which is what makes image projection and the sun work without further setup.
    /// A planet that is no body (None, ENU, JPL) ends a body-fixed observation and leaves
    /// any other alone (GisApp.withScenePlanet).
    let setPlanet (planet : Planet) (m : Model) =
        let m = setPlanetOnly planet m
        let gisApp = PRo3D.Core.Gis.GisApp.withScenePlanet planet m.scene.gisApp
        { m with scene = { m.scene with gisApp = gisApp } }

    /// After the GIS observed body or frame changed: a body-fixed observation of a body
    /// PRo3D knows makes that body the planet. Any other observed body - a spacecraft, or
    /// a body observed in another frame - has no planet (None): the planet-based features
    /// would read its world coordinates wrongly. Clearing the observation leaves the planet.
    let followObservation (m : Model) =
        let info = m.scene.gisApp.defaultObservationInfo
        let planet =
            match info.observer with
            | None -> m.scene.referenceSystem.planet
            | Some _ -> PRo3D.Core.Gis.GisApp.scenePlanet m.scene.gisApp |> Option.defaultValue Planet.None
        if planet = m.scene.referenceSystem.planet then m
        else
            Log.line "[SceneBodySync] GIS observation %A in %A -> planet %A" info.observer info.referenceFrame planet
            setPlanetOnly planet m

    /// A loaded scene whose GIS observation is body-fixed gets that body as its planet -
    /// scenes set up in the GIS view only used to keep Planet.None, which disables MapView.
    /// Nothing else is touched: a scene observed in another frame (J2000) loads as saved.
    let reconcileOnLoad (m : Model) =
        match PRo3D.Core.Gis.GisApp.scenePlanet m.scene.gisApp with
        | Some planet when planet <> m.scene.referenceSystem.planet ->
            Log.line "[SceneBodySync] scene observes %A body-fixed; planet %A -> %A"
                m.scene.gisApp.defaultObservationInfo.observer m.scene.referenceSystem.planet planet
            setPlanetOnly planet m
        | _ -> m
