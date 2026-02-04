namespace PRo3D

open System
open Aardvark.Base
open Aardvark.UI

open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Base.Gis

open PRo3D.Viewer
open PRo3D.Base.Annotation
open PRo3D.Core.Drawing

open Aardvark.Rendering

open Aether
open FSharp.Data.Adaptive


open ViewerLenses

module Picking =

    let mutable cache = HashMap.Empty

    let pickRay (m : Model) (r : FastRay3d) (surfaceName : Option<string>) =
        let ray = r.Ray
        let observerSystem = Gis.GisApp.getObserverSystem m.scene.gisApp
        let observedSystem (v : SurfaceId) = Gis.GisApp.getSpiceReferenceSystem m.scene.gisApp v
                
        let endLog = 
            if Config.diagnosticTimings then 
                match surfaceName with
                | None -> Log.startTimed "[PickSurface] general surface picking without surface restriction"
                | Some surfaceName -> Log.startTimed "[PickSurface] try intersect kdtree of %s" surfaceName    
            Config.diagnosticTimings
                         
        let onlyActive (id : Guid) (l : Leaf) (s : SgSurface) = l.active
        let onlyVisible (id : Guid) (l : Leaf) (s : SgSurface) = l.visible
        let visibleAndActive (id : Guid) (l : Leaf) (s : SgSurface) = l.visible && l.active

        let surfaceFilter = 
            match m.interaction with
            | Interactions.PickSurface -> visibleAndActive
            | _ -> onlyActive
                      

        let hit = 
            match SurfaceIntersection.doKdTreeIntersection (Optic.get _surfacesModel m) (Some m.scene.traverses) m.scene.referenceSystem observedSystem observerSystem r surfaceFilter cache Config.diagnosticTimings with
            | Some (hit,surf), c ->                         
                cache <- c
                let t = hit.RayHit.T
                let hitPosOnRay = ray.GetPointOnRay(t)

                if Config.diagnosticTimings then Log.line "[PickSurface] surface hit at (new method) %A" hit

                //let cameraLocation = m.navigation.camera.view.Location //navigation'.camera.view.Location 
                //let hitF = hitF cameraLocation

                //let observedSystem = observedSystem surf.guid
                //let spiceTrafo = 
                //    match observedSystem, observerSystem with
                //    | Some observedSystem, Some observerSystem -> 
                //        CooTransformation.transformBody observedSystem.body (Some observedSystem.referenceFrame) observerSystem.body observerSystem.referenceFrame observerSystem.time
                //        |> Option.map (fun t -> t.Trafo) 
                //        |> Option.defaultValue Trafo3d.Identity
                //    | _ -> Trafo3d.Identity

                //let toLocal (v : V3d) = spiceTrafo.Backward.TransformPos(v)

                //hitF >> Option.map toLocal
                Some (hit, hitPosOnRay)
            | _ -> 
                None

        if endLog then Log.stop()

        hit 

    let pickVisualization (m : AdaptiveModel) =
        
        let t = 
            m.surfaceIntersection 
            |> AVal.map (function 
                | None -> Trafo3d.Identity 
                | Some s -> 
                    let t = Trafo3d.Translation(s.hitPoint)
                    match s.normal with
                    | Some n -> Trafo3d.RotateInto(V3d.OOI, n) * t 
                    | None -> t
            )


        (*
                | cylinder ends at 0
                | cone ends at 20% of the full size
                v cone starts at surface (-height)
        ___________________ surface
        
        *)

        let surfaceStart = m.scene.config.previewIntersectionWorldSize.value |> AVal.map (fun s -> Trafo3d.Translation(0.0,0.0, -s))
        let cylinderStart = m.scene.config.previewIntersectionWorldSize.value |> AVal.map (fun s -> Trafo3d.Translation(0.0,0.0, -s * 0.8))
        let radiusCyliner = m.scene.config.previewIntersectionWorldSize.value |> AVal.map (fun s -> 0.01 * s)
        let radiusCone = m.scene.config.previewIntersectionWorldSize.value |> AVal.map (fun s -> 0.1 * s)

        Sg.ofList [
            SgPrimitives.Sg.cone 10 (AVal.constant C4b.White) radiusCone (m.scene.config.previewIntersectionWorldSize.value  |> AVal.map (fun s -> s * 0.3)) |> Sg.trafo surfaceStart
            SgPrimitives.Sg.cylinder 10 (AVal.constant C4b.White) radiusCyliner (m.scene.config.previewIntersectionWorldSize.value) |> Sg.trafo cylinderStart
        ]
        |> Sg.shader {
            do! DefaultSurfaces.stableTrafo
            do! DefaultSurfaces.stableHeadlight
        }
        |> Sg.trafo t
        |> Sg.onOff m.scene.config.showPreviewIntersection
        |> Sg.onOff (m.surfaceIntersection |> AVal.map Option.isSome)