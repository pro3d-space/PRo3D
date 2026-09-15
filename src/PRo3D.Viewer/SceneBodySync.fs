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

    /// A planet change moves every surface's local reference system (it is built from the
    /// planet's up/north at the surface) and can leave the drawing tool on a geometry that
    /// needs a reference body.
    let private applyPlanetToSurfacesAndDrawing (planet : Planet) (m : Model) =
        let flat =
            m.scene.surfacesModel.surfaces.flat
            |> HashMap.map (fun k v ->
                match v, HashMap.tryFind k m.scene.surfacesModel.sgSurfaces with
                | Leaf.Surfaces s, Some sgSurface ->
                    let bbCenter = sgSurface.globalBB.Center
                    Leaf.Surfaces {
                        s with transformation =
                                    TransformationApp.update s.transformation TransformationApp.Action.UpdatePlanetInLocalRefSys m.scene.referenceSystem bbCenter
                    }
                | _ -> v)
        let m = { m with scene = { m.scene with surfacesModel = { m.scene.surfacesModel with surfaces = { m.scene.surfacesModel.surfaces with flat = flat }}}}
        // the annotation toolbar greys out geometries that need a real reference body
        // (DnS/TT/ellipses) while Planet.None is selected; drop an active one back to
        // Line so the drawing tool never sits on a disabled - and for ellipses crashing
        // - geometry. Line allows every projection, so projection is left untouched.
        if planet = Planet.None && Geometry.needsReferenceBody m.drawing.geometry then
            { m with drawing = { m.drawing with geometry = Geometry.Line } }
        else
            m

    /// Every derived annotation value depends on the reference system - bearing, slope, dip
    /// and strike, altitudes - and a Color by Category ramp fitted to the old numbers no
    /// longer matches them, nor does its legend. Refit, exactly as switching the attribute
    /// does. fitRange leaves categorical and cyclic attributes alone (a hue wheel has no
    /// bounds to fit), and a switched-off panel is not touched at all.
    let private recalculateAnnotations (m : Model) =
        let refSystem = m.scene.referenceSystem
        Log.startTimed "[SceneBodySync] recalculating angular values in annos"
        let flat =
            m.drawing.annotations.flat
            |> HashMap.map (fun _ v ->
                match v with
                | Leaf.Annotations a ->
                    let results = Calculations.calculateAnnotationResults a refSystem.up.value refSystem.northO refSystem.planet
                    let dnsResults = DipAndStrike.reCalculateDipAndStrikeResults refSystem.up.value refSystem.northO a
                    Leaf.Annotations { a with results = Some results; dnsResults = dnsResults }
                | _ -> v)
        Log.stop()

        let colorByCategory =
            if m.drawing.colorByCategory.enabled then
                let annotations = flat |> HashMap.toValueList |> List.choose (function Leaf.Annotations a -> Some a | _ -> None)
                ColorByCategory.update annotations m.drawing.colorByCategory ColorByCategoryAction.FitRangeToData
            else
                m.drawing.colorByCategory

        { m with drawing = { m.drawing with annotations = { m.drawing.annotations with flat = flat }; colorByCategory = colorByCategory } }

    /// A reference-system action and everything that follows from it. The camera is the
    /// caller's: whether its sky moved is a comparison across this call.
    let applyReferenceSystemAction (a : ReferenceSystemAction) (m : Model) =
        let refSystem, _ =
            ReferenceSystemApp.update m.scene.config LenseConfigs.referenceSystemConfig m.scene.referenceSystem a
        let m = { m with scene = { m.scene with referenceSystem = refSystem } }
        let m =
            match a with
            | ReferenceSystemAction.SetPlanet planet -> applyPlanetToSurfacesAndDrawing planet m
            | _ -> m
        recalculateAnnotations m

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
