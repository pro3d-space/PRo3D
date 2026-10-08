namespace PRo3D.Composition

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface

/// What follows from a reference-system change for the surfaces and the annotations - the part
/// of the Viewer's SceneBodySync that does not involve the GIS observation (docs/SceneBody.md).
module ReferenceSystemSync =

    /// A planet change moves every surface's local reference system (it is built from the
    /// planet's up/north at the surface).
    let applyPlanetToSurfaces (refSystem : ReferenceSystem) (surfaces : SurfaceModel) : SurfaceModel =
        let flat =
            surfaces.surfaces.flat
            |> HashMap.map (fun k v ->
                match v, HashMap.tryFind k surfaces.sgSurfaces with
                | Leaf.Surfaces s, Some sgSurface ->
                    let bbCenter = sgSurface.globalBB.Center
                    Leaf.Surfaces {
                        s with transformation =
                                    TransformationApp.update s.transformation TransformationApp.Action.UpdatePlanetInLocalRefSys refSystem bbCenter
                    }
                | _ -> v)
        { surfaces with surfaces = { surfaces.surfaces with flat = flat } }

    /// The annotation toolbar greys out geometries that need a real reference body
    /// (DnS/TT/ellipses) while Planet.None is selected; drop an active one back to Line so the
    /// drawing tool never sits on a disabled - and for ellipses crashing - geometry. Line allows
    /// every projection, so projection is left untouched.
    let applyPlanetToDrawing (planet : Planet) (drawing : DrawingModel) : DrawingModel =
        if planet = Planet.None && Geometry.needsReferenceBody drawing.geometry then
            { drawing with geometry = Geometry.Line }
        else
            drawing

    /// Every derived annotation value depends on the reference system - bearing, slope, dip
    /// and strike, altitudes - and a Color by Category ramp fitted to the old numbers no longer
    /// matches them, nor does its legend. Refit, exactly as switching the attribute does.
    /// fitRange leaves categorical and cyclic attributes alone, and a switched-off panel is not
    /// touched at all.
    let recalculateAnnotations (refSystem : ReferenceSystem) (drawing : DrawingModel) : DrawingModel =
        Log.startTimed "[ReferenceSystemSync] recalculating angular values in annos"
        let flat =
            drawing.annotations.flat
            |> HashMap.map (fun _ v ->
                match v with
                | Leaf.Annotations a ->
                    let results = Calculations.calculateAnnotationResults a refSystem.up.value refSystem.northO refSystem.planet
                    let dnsResults = DipAndStrike.reCalculateDipAndStrikeResults refSystem.up.value refSystem.northO a
                    Leaf.Annotations { a with results = Some results; dnsResults = dnsResults }
                | _ -> v)
        Log.stop()

        let colorByCategory =
            if drawing.colorByCategory.enabled then
                let annotations = flat |> HashMap.toValueList |> List.choose (function Leaf.Annotations a -> Some a | _ -> None)
                ColorByCategory.update annotations drawing.colorByCategory ColorByCategoryAction.FitRangeToData
            else
                drawing.colorByCategory

        { drawing with annotations = { drawing.annotations with flat = flat }; colorByCategory = colorByCategory }

    /// A reference-system action and everything that follows from it for surfaces and
    /// annotations. The camera is the caller's: whether its sky moved is a comparison across
    /// this call.
    let apply
        (config    : ViewConfigModel)
        (action    : ReferenceSystemAction)
        (refSystem : ReferenceSystem)
        (surfaces  : SurfaceModel)
        (drawing   : DrawingModel) : ReferenceSystem * SurfaceModel * DrawingModel =
        let refSystem, _ = ReferenceSystemApp.update config LenseConfigs.referenceSystemConfig refSystem action
        let surfaces, drawing =
            match action with
            | ReferenceSystemAction.SetPlanet planet ->
                applyPlanetToSurfaces refSystem surfaces, applyPlanetToDrawing planet drawing
            | _ -> surfaces, drawing
        refSystem, surfaces, recalculateAnnotations refSystem drawing
