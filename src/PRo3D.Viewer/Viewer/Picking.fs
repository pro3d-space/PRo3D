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

    let pickRay (m : Model) (r : FastRay3d) (surfaceName : string) =
        let ray = r.Ray
        let observerSystem = Gis.GisApp.getObserverSystem m.scene.gisApp
        let observedSystem (v : SurfaceId) = Gis.GisApp.getSpiceReferenceSystem m.scene.gisApp v
                
        Log.startTimed "[PickSurface] try intersect kdtree of %s" surfaceName       
                         
        let onlyActive (id : Guid) (l : Leaf) (s : SgSurface) = l.active
        let onlyVisible (id : Guid) (l : Leaf) (s : SgSurface) = l.visible
        let visibleAndActive (id : Guid) (l : Leaf) (s : SgSurface) = l.visible && l.active

        let surfaceFilter = 
            match m.interaction with
            | Interactions.PickSurface -> visibleAndActive
            | _ -> onlyActive
                      

        let hit = 
            match SurfaceIntersection.doKdTreeIntersection (Optic.get _surfacesModel m) m.scene.referenceSystem observedSystem observerSystem r surfaceFilter cache with
            | Some (hit,surf), c ->                         
                cache <- c
                let t = hit.RayHit.T
                let hitPosOnRay = ray.GetPointOnRay(t)

                Log.line "[PickSurface] surface hit at (new method) %A" hit

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

        Log.stop()

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
        Sg.ofList [
            SgPrimitives.Sg.cone 10 (AVal.constant C4b.White) (AVal.constant 1.0) (AVal.constant 3.0) |> Sg.translate 0.0 0.0 -10.0 
            SgPrimitives.Sg.cylinder 10 (AVal.constant C4b.White) (AVal.constant 0.1) (AVal.constant 10.0) |> Sg.translate 0.0 0.0 -8.0 
        ]
        |> Sg.shader {
            do! DefaultSurfaces.stableTrafo
            do! DefaultSurfaces.stableHeadlight
        }
        |> Sg.trafo t
        |> Sg.onOff (m.surfaceIntersection |> AVal.map Option.isSome)