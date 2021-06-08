namespace PRo3D.Comparison

open Aardvark.Base
open Aardvark.UI
open PRo3D.Comparison
open FSharp.Data.Adaptive
open PRo3D.Base
open Aardvark.UI
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Base
open Aardvark.Rendering
open Adaptify.FSharp.Core


module ComparisonUtils =
    let noSelection = "-None-"

    let noSelectionToNone (str : string) =
         if str = noSelection then None else Some str

    let findSurfaceByName (surfaceModel : SurfaceModel) (name : string) =
         let surfacesWithName = 
             surfaceModel.surfaces.flat
               |> HashMap.map (fun x -> Leaf.toSurface)
               |> HashMap.filter (fun k v -> v.name = name)
         if surfacesWithName.Count > 0 then
             surfacesWithName |> HashMap.keys |> HashSet.toList |> List.tryHead
         else None

    let almostFullTrafo surface refSystem=
         let incompleteTrafo = SurfaceTransformations.fullTrafo' surface refSystem
         let sc = surface.scaling.value
         let t = surface.preTransform
         Trafo3d.Scale(sc) * (t * incompleteTrafo)

    let toggleVisible (surfaceId1   : option<System.Guid>) 
                      (surfaceId2   : option<System.Guid>)
                      (surfaceModel : SurfaceModel) =
        match surfaceId1, surfaceId2 with
        | Some id1, Some id2 ->
            let s1 = surfaceModel.surfaces.flat |> HashMap.find id1
                                                |> Leaf.toSurface
            let s2 = surfaceModel.surfaces.flat |> HashMap.find id2
                                                |> Leaf.toSurface
            let s1, s2 =
                match s1.isVisible, s2.isVisible with
                | true, true | false, false ->
                    let s1 = {s1 with isVisible = true
                                      isActive  = true}
                    let s2 = {s2 with isVisible = false
                                      isActive  = false}
                    s1, s2
                | _, _ ->
                    let s1 = {s1 with isVisible = not s1.isVisible
                                      isActive  = not s1.isVisible}
                    let s2 = {s2 with isVisible = not s2.isVisible
                                      isActive  = not s2.isVisible}
                    s1, s2
            surfaceModel
              |> SurfaceModel.updateSingleSurface s1
              |> SurfaceModel.updateSingleSurface s2
        | _,_ -> surfaceModel

    let mutable cache = HashMap.Empty
    let calculateRayHit (fromLocation : V3d) (direction : V3d)
                        surfaceModel refSystem surfaceFilter = 

    
        let ray = new Ray3d (fromLocation, direction)
        let intersected = SurfaceIntersection.doKdTreeIntersection surfaceModel 
                                                                   refSystem 
                                                                   (FastRay3d(ray)) 
                                                                   surfaceFilter 
                                                                   cache
        match intersected with
        | Some (t,surf), c ->       
            cache <- c
            let hit = ray.GetPointOnRay(t) 
            //Log.warn "ray in direction %s hit surface at %s" (direction.ToString ()) (string hit) // rno debug
            hit |> Some
        |  None, c ->
            cache <- c
            Log.warn "[RayCastSurface] no hit in direction %s" (direction.ToString ())
            None