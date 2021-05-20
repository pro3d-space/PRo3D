namespace PRo3D

open System
open Aardvark.Rendering
open Aardvark.UI
open PRo3D.Comparison
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Trafos
open Aardvark.UI.Primitives
open PRo3D.Core
open PRo3D.Base
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Base
open Chiron
open PRo3D.Base.Annotation
open Adaptify.FSharp.Core

module CustomGui =
    let dynamicDropdown<'msg when 'msg : equality> (items    : list<aval<string>>)
                                                   (selected : aval<string>) 
                                                   (change   : string -> 'msg) =
        let attributes (name : aval<string>) =
            AttributeMap.ofListCond [
                Incremental.always "value" name
                onlyWhen (AVal.map2 (=) name selected) (attribute "selected" "selected")
            ]
     
        let callback = onChange (fun str -> 
                                    str |> change)

        select [callback; style "color:black"] [
                for name in items do
                    let att = attributes name
                    yield Incremental.option att (AList.ofList [Incremental.text name])
        ] 

    let surfacesDropdown (surfaces : AdaptiveSurfaceModel) (change : string -> 'msg) (noSelection : string)=
        let surfaceToName (s : aval<AdaptiveSurface>) =
            s |> AVal.bind (fun s -> s.name)

        let surfaces = surfaces.surfaces.flat |> toAvalSurfaces
        let surfaceNames = surfaces |> AMap.map (fun g s -> s |> surfaceToName)                                                         
                                    |> AMap.toAVal
        let items = 
          surfaceNames |> AVal.map (fun n -> n.ToValueList ())
            |> AVal.map (fun x -> List.append [(noSelection |> AVal.constant)] x)

        let dropdown = 
            items |> AVal.map (fun items -> dynamicDropdown items (noSelection |> AVal.constant) change)

        Incremental.div (AttributeMap.ofList []) (AList.ofAValSingle dropdown)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ComparisonApp =
    let noSelection = "-None-"

    let init : ComparisonApp = {   
        showMeasurementsSg   = true
        originMode           = OriginMode.ModelOrigin
        surface1             = None
        surface2             = None
        surfaceMeasurements  = 
          {
              measurements1        = None
              measurements2        = None
              comparedMeasurements = None
          }
        annotationMeasurements = []     
    }

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

    let getAxesAngles surface refSystem =
        let trafo = SurfaceTransformations.fullTrafo' surface refSystem

        let mutable rot = V3d.OOO
        let mutable tra = V3d.OOO
        let mutable sca = V3d.OOO
        
        trafo.Decompose (&sca, &rot, &tra)
        //let (x : float) = System.Math.Round (rot.X, 15)
        //let (y : float) = System.Math.Round (rot.Y, 15)
        //let (z : float) = System.Math.Round (rot.Z, 15)
        //V3d (x, y, z)
        rot

    let calculateRayHit (fromLocation : V3d) (direction : V3d)
                        surfaceModel refSystem surfaceFilter = 

        let mutable cache = HashMap.Empty
        let ray = new Ray3d (fromLocation, direction)
        let intersected = SurfaceIntersection.doKdTreeIntersection surfaceModel 
                                                                   refSystem 
                                                                   (FastRay3d(ray)) 
                                                                   surfaceFilter 
                                                                   cache
        match intersected with
        | Some (t,surf), c ->                         
            let hit = ray.GetPointOnRay(t) 
            //Log.warn "ray in direction %s hit surface at %s" (direction.ToString ()) (string hit) // rno debug
            hit |> Some
        |  None, _ ->
            Log.warn "[RayCastSurface] no hit in direction %s" (direction.ToString ())
            None

    let getDimensions (surface : Surface)
                      (surfaceModel : SurfaceModel) 
                      (refSystem    : ReferenceSystem)
                      (originMode   : OriginMode) =
        let surfaceFilter (id : Guid) (l : Leaf) (s : SgSurface) = 
            id = surface.guid
        let trafo = SurfaceTransformations.fullTrafo' surface refSystem
        let mutable rot = V3d.OOO
        let mutable tra = V3d.OOO
        let mutable sca = V3d.OOO

        trafo.Decompose (&sca, &rot, &tra)
        let origin =
            match originMode with
            | OriginMode.ModelOrigin -> tra
            | OriginMode.BoundingBoxCentre -> 
                let sgSurface = HashMap.find surface.guid surfaceModel.sgSurfaces 
                let boundingBox = sgSurface.globalBB.Transformed trafo
                boundingBox.Center
            | _ -> - tra
        
        let rotation = rot |> Trafo3d.RotationEuler 
                           |> Rot3d.FromTrafo3d
        Log.warn "[DEBUG] calc trafo translation = %s" (tra.ToString ())
        Log.warn "[DEBUG] ref system origin = %s" (refSystem.origin.ToString ())
        let localZDir = rotation.Transform refSystem.up.value.Normalized
        let localYDir = rotation.Transform refSystem.north.value.Normalized
        let localXDir = (localZDir.Cross localYDir).Normalized

        let zDirHit = calculateRayHit origin localZDir surfaceModel refSystem surfaceFilter
        let minusZDirHit = calculateRayHit origin -localZDir surfaceModel refSystem surfaceFilter
        let yDirHit = calculateRayHit origin localYDir surfaceModel refSystem surfaceFilter
        let minusYDirHit = calculateRayHit origin -localYDir surfaceModel refSystem surfaceFilter
        let xDirHit = calculateRayHit origin localXDir surfaceModel refSystem surfaceFilter
        let minusXDirHit = calculateRayHit origin -localXDir surfaceModel refSystem surfaceFilter

        let zSize = Option.map2 (fun (a : V3d) b ->  Vec.Distance (a, b)) zDirHit minusZDirHit
        let ySize = Option.map2 (fun (a : V3d) b ->  Vec.Distance (a, b)) yDirHit minusYDirHit
        let xSize = Option.map2 (fun (a : V3d) b ->  Vec.Distance (a, b)) xDirHit minusXDirHit

        match xSize, ySize, zSize with
        | Some x, Some y, Some z ->
            V3d (x, y, z)
        | _,_,_ -> 
            Log.error "[Comparison] Could not calculate surface size along axes."
            V3d.OOO

    let updateSurfaceMeasurements (surfaceModel : SurfaceModel) 
                                  (refSystem    : ReferenceSystem) 
                                  (originMode   : OriginMode)
                                  (surfaceName  : string) =
        let surfaceId = findSurfaceByName surfaceModel surfaceName
        match surfaceId with
        | Some surfaceId ->
            let surface = surfaceModel.surfaces.flat |> HashMap.find surfaceId |> Leaf.toSurface
            let axesAngles = getAxesAngles surface refSystem
            let dimensions = 
                getDimensions surface surfaceModel refSystem originMode
            {SurfaceMeasurements.init with rollPitchYaw = axesAngles
                                           dimensions   = dimensions
            } |> Some
        | None -> None

    let updateMeasurements (m            : ComparisonApp) 
                           (surfaceModel : SurfaceModel) 
                           (annotations  : HashMap<Guid, Annotation.Annotation>) 
                           (bookmarks    : HashMap<Guid, Bookmark>)
                           (refSystem    : ReferenceSystem) =
        Log.line "[Comparison] Calculating surface measurements..."
        let annotationMeasurements =
            match m.surface1, m.surface2 with
            | Some s1, Some s2 ->
                AnnotationComparison.compareAnnotationMeasurements 
                  s1 s2  annotations bookmarks
            | _,_ -> []
        let measurements1 = Option.bind (fun s1 -> updateSurfaceMeasurements 
                                                        surfaceModel refSystem m.originMode s1) 
                                        m.surface1 
        let measurements2 = Option.bind (fun s2 -> updateSurfaceMeasurements 
                                                        surfaceModel refSystem m.originMode s2)
                                        m.surface2
        let surfaceMeasurements = 
            {
                measurements1 = measurements1
                measurements2 = measurements2
                comparedMeasurements =
                    Option.map2 (fun a b -> SurfaceMeasurements.compare a b)
                                measurements1 measurements2
            }
        Log.line "[Comparison] Finished calculating surface measurements."
        {m with surfaceMeasurements    = surfaceMeasurements 
                annotationMeasurements = annotationMeasurements
        }

    let toggleVisible (surfaceId1   : option<Guid>) 
                      (surfaceId2   : option<Guid>)
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

    let update (m            : ComparisonApp) 
               (surfaceModel : SurfaceModel) 
               (refSystem    : ReferenceSystem)
               (annotations  : HashMap<Guid, Annotation.Annotation>) 
               (bookmarks    : HashMap<Guid, Bookmark>)
               (msg          : ComparisonAction) =
        match msg with
        | Update -> 
            let m = updateMeasurements m surfaceModel annotations bookmarks refSystem
            m , surfaceModel
        | SelectSurface1 str -> 
            let m = {m with surface1 = noSelectionToNone str}
            let m =
                match m.surface1, m.surface2 with
                | Some s1, Some s2 ->
                    updateMeasurements m surfaceModel annotations bookmarks refSystem
                | _,_ -> m
            m , surfaceModel
        | SelectSurface2 str -> 
            let m = {m with surface2 = noSelectionToNone str}
            let m =
                match m.surface1, m.surface2 with
                | Some s1, Some s2 ->
                    updateMeasurements m surfaceModel annotations bookmarks refSystem
                | _,_ -> m
            m , surfaceModel
        | ExportMeasurements filepath -> 
            m
              |> Json.serialize 
              |> Json.formatWith JsonFormattingOptions.Pretty 
              |> Serialization.writeToFile filepath
            Log.line "[Comparison] Measurements exported to %s" (System.IO.Path.GetFullPath filepath)
            m , surfaceModel
        | ComparisonAction.ToggleVisible ->
            let surfaceId1 = m.surface1 |> Option.bind (fun x -> findSurfaceByName surfaceModel x)
            let surfaceId2 = m.surface2 |> Option.bind (fun x -> findSurfaceByName surfaceModel x)
            let surfaceModel = toggleVisible surfaceId1 surfaceId2 surfaceModel
            m, surfaceModel
        | AddBookmarkReference bookmarkId ->
            m, surfaceModel
        | SetOriginMode originMode -> 
            let m = {m with originMode = originMode}
            let m = updateMeasurements m surfaceModel annotations bookmarks refSystem
            m, surfaceModel

    let isSelected (surfaceName : aval<string>) (m : AdaptiveComparisonApp) =
        let showSg = 
            AVal.map3 (fun (s1 : option<string>) s2 surfaceName -> 
                          match s1, s2 with
                          | Some s1, Some s2 ->
                            s1 = surfaceName || s2 = surfaceName
                          | Some s1, None -> s1 = surfaceName
                          | None, Some s2 -> s2 = surfaceName
                          | None, None -> false
                      ) m.surface1 m.surface2 surfaceName
        showSg

    let defaultCoordinateCross size trafo (origin : aval<V3d>) =
        let sg = 
            Sg.coordinateCross size
                |> Sg.trafo trafo
                |> Sg.noEvents
                |> Sg.effect [              
                    Shader.stableTrafo |> toEffect 
                    DefaultSurfaces.vertexColor |> toEffect
                ] 
                |> Sg.noEvents
                |> Sg.andAlso (
                    Sg.sphere 12 (C4b.Blue |> AVal.constant) 
                                 (size |> AVal.map (fun x -> x * 0.001)) 
                        |> Sg.trafo (origin |> AVal.map (fun x -> Trafo3d.Translation x))
                        |> Sg.noEvents
                        |> Sg.effect [              
                              Shader.stableTrafo |> toEffect 
                              DefaultSurfaces.vertexColor |> toEffect
                        ] 
                )
        sg

    let measurementsSg (surface     : aval<AdaptiveSurface>)
                       (size        : aval<float>)
                       (trafo       : aval<Trafo3d>) 
                       (referenceSystem : AdaptiveReferenceSystem)
                       (m           : AdaptiveComparisonApp) =    
        let surfaceName = surface |> AVal.bind (fun x -> x.name)
        let pivot = surface |> AVal.bind (fun x -> x.transformation.pivot)

       // let upDir = referenceSystem.up.value |> AVal.map (fun x -> x.Normalized)
      //  let northDir = referenceSystem.northO |> AVal.map (fun x -> x.Normalized)
      //  let east   =  AVal.map2 (fun (north : V3d) up -> north.Cross(up).Normalized) northDir upDir

        let showSg = isSelected surfaceName m

        let sg =
            showSg |> AVal.map (fun show -> 
                                  match show with
                                  | true -> defaultCoordinateCross size trafo pivot
                                  | false -> Sg.empty
                               )
        sg |> Sg.dynamic

    let view (m : AdaptiveComparisonApp) 
             (surfaces : AdaptiveSurfaceModel) =
        let measurementGui (name         : option<string>) 
                           (maesurements : option<SurfaceMeasurements>) =
            match name, maesurements with
            | Some name, Some maesurements -> 
                SurfaceMeasurements.view maesurements
            | _,_    -> 
                div [][]
                 

        let measurement1 = 
            (AVal.map2 (fun (s : option<string>) m -> 
                                measurementGui s m.measurements1) m.surface1 m.surfaceMeasurements)
                       |> AList.ofAValSingle


        let header surf = 
            (surf |> AVal.map (fun name -> 
                                      match name with
                                      | Some name -> sprintf "Measurements for %s"  name
                                      | None      -> "No surface selected"))

        let measurement2 = 
            (AVal.map2 (fun (s : option<string>) m -> 
                                measurementGui s m.measurements2
                       ) m.surface2 m.surfaceMeasurements)
                       |> AList.ofAValSingle

        let compared = 
            m.surfaceMeasurements
                |> (AVal.map (fun x -> 
                                  match x.comparedMeasurements with
                                  | Some m -> 
                                      SurfaceMeasurements.view m
                                  | None -> 
                                      div [] []
                             )
                    ) 

        let surfaceMeasurements =
            alist {
                yield div [] [Incremental.text (header m.surface1)]
                yield! measurement1
                yield div [] [Incremental.text (header m.surface2)]
                yield! measurement2
                yield div [] [text "Difference"]
                let! compared = compared
                yield compared
            }
        let surfaceMeasurements =
            Incremental.div (AttributeMap.ofList []) surfaceMeasurements
        //let header = sprintf "Measurements for %s"  name

        let surfaceMeasurements =
             AVal.map2 (fun (s1 : option<string>) s2 -> 
                            match s1, s2 with
                            | Some s1, Some s2 -> 
                                GuiEx.accordion "Surface Measurements"  "calculator" true [surfaceMeasurements]
                            | _,_ -> GuiEx.accordion "Surface Measurements"  "calculator" true []
                       ) m.surface1 m.surface2
            
        //let measurement1 = 
        //    (AVal.bind2 (fun (s : option<string>) m -> 
        //                        measurementGui s (m.measurements1 
        //               ) m.surface1 m.surfaceMeasurements)
        //               |> AList.ofAValSingle

        //let measurement2 = 
        //    (AVal.bind2 (fun (s : option<string>) m -> 
        //                        measurementGui s (m.measurements2 |> AdaptiveOption.toOption)
        //               ) m.surface2 m.surfaceMeasurements)
        //               |> AList.ofAValSingle

        //let compared = 
        //    m.comparedMeasurements
        //        |> (AVal.map (fun m -> 
        //                          match m with
        //                          | AdaptiveOption.AdaptiveSome m -> 
        //                              SurfaceMeasurements.view m
        //                          | AdaptiveOption.AdaptiveNone -> 
        //                              div [] []
        //                     )
        //            ) 


        let updateButton =
          button [clazz "ui icon button"; onMouseClick (fun _ -> Update )] 
                  [i [clazz "calculator icon"] []]  |> UI.wrapToolTip DataPosition.Bottom "Update"
        let exportButton = 
          button [clazz "ui icon button"
                  onMouseClick (fun _ -> ExportMeasurements "measurements.json")] 
                 [i [clazz "download icon"] [] ]
                    |> UI.wrapToolTip DataPosition.Bottom "Export"

        let annotationComparison =
            let tables = 
                adaptive {
                    let! s1 = m.surface1
                    let! s2 = m.surface2
                    let! measurements = m.annotationMeasurements
                    match s1, s2 with
                    | Some s1, Some s2 ->
                        let lst = 
                            alist {
                                for annoMeasurement in  measurements do
                                    yield (AnnotationComparison.view s1 s2 annoMeasurement)
                            }
                        let content = 
                            Incremental.div ([] |> AttributeMap.ofList) 
                                            lst
                        return content
                    | _,_ ->
                        return div [] []
                
                }
            let header = sprintf "Annotation Length Comparison"
            let content = Incremental.div  ([] |> AttributeMap.ofList) 
                                           (AList.ofAValSingle tables)
            let accordion =
                GuiEx.accordion header  "calculator" true [content]
            accordion

        div [][
            br []
            div [clazz "ui buttons inverted"] 
                [updateButton;exportButton]
            br []
            Html.table [
              Html.row "Origin   " [Html.SemUi.dropDown m.originMode SetOriginMode]
            ]
            br []
            Html.table [
                Html.row "Surface1 " [CustomGui.surfacesDropdown surfaces SelectSurface1 noSelection]
                Html.row "Surface2 " [CustomGui.surfacesDropdown surfaces SelectSurface2 noSelection]
            ]
            br []
            Incremental.div ([] |> AttributeMap.ofList)  
                           (AList.ofAValSingle surfaceMeasurements)
            //GuiEx.accordion "Difference" "calculator" true [
            //   Incremental.div ([] |> AttributeMap.ofList)  (AList.ofAValSingle compared)
            //]
            br []
            annotationComparison
        ]
