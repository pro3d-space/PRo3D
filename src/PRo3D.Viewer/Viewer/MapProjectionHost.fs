namespace PRo3D.Viewer

open System
open System.IO

open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

/// Everything the viewer knows about the map projection panel (#772) lives here: which
/// surfaces it draws and where they are placed. The panel itself is PRo3D.MapProjection,
/// which never sees the viewer model.
module MapProjectionHost =

    /// OPC hierarchies of a surface: its expanded paths that contain `Patches` (OBJ surfaces
    /// and missing data have none).
    let private hierarchiesOf (importPath : string) (names : list<string>) =
        Files.expandNamesToPaths importPath names
        |> List.filter (fun d -> Directory.Exists(Path.Combine(d, "Patches")))
        |> List.toArray

    let inputs (m : AdaptiveModel) : PRo3D.MapProjection.MapInputs =
        let sceneBody = Gis.GisApp.sceneBodyAdaptive m.scene.gisApp
        let surfaces =
            m.scene.surfacesModel.surfaces.flat
            |> AMap.toASet
            |> ASet.chooseA (fun (surfaceId, leaf) ->
                match leaf with
                | AdaptiveSurfaces surf ->
                    // built once per surface, outside the evaluation below
                    let placement = SunShadowMap.surfacePlacement m surfaceId surf
                    let system = Gis.GisApp.getSpiceReferenceSystemAdaptive m.scene.gisApp surfaceId
                    adaptive {
                        let! importPath = surf.importPath
                        let! names = surf.opcNames
                        let! sceneBody = sceneBody
                        let! system = system
                        let hierarchies = hierarchiesOf importPath names
                        // a surface of another body (e.g. Didymos in a Dimorphos scene) is not
                        // centred on the map's body; without a GIS scene body, take them all
                        let onSceneBody =
                            match sceneBody with
                            | None -> true
                            | Some body -> system = Some body
                        Log.line "[map] surface %s: %d hierarchies below %s %A, scene body %A, surface body %A"
                            (string surfaceId) hierarchies.Length importPath names sceneBody system
                        if hierarchies.Length > 0 && onSceneBody then
                            return Some ({ hierarchies = hierarchies; placement = placement; visible = surf.isVisible } : PRo3D.MapProjection.MapSg.MapSurface)
                        else
                            return None
                    }
                | _ -> AVal.constant None)
        let annotations : PRo3D.MapProjection.MapAnnotations.AnnotationInputs =
            {
                annotations =
                    m.drawing.annotations.flat
                    |> AMap.choose (fun _ leaf -> PRo3D.Core.Drawing.DrawingApp.tryToAnnotation leaf)
                    |> AMap.toASet
                colorByCategory = m.drawing.colorByCategory
                selected        = m.drawing.annotations.selectedLeaves |> ASet.map (fun l -> l.id)
            }
        { planet = m.scene.referenceSystem.planet; surfaces = surfaces; annotations = annotations }

    let view (m : AdaptiveModel) : DomNode<ViewerAction> =
        PRo3D.MapProjection.MapProjectionApp.view (inputs m) m.mapProjection
        |> UI.map MapProjectionMessage
