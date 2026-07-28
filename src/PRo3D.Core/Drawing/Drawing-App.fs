#nowarn "44" 
namespace PRo3D.Core.Drawing

open System
open System.IO

open PRo3D.Base

//open System.Windows.Forms
open System.Text
open System.Net.WebSockets
open System.Threading
open System.Collections.Concurrent    

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators


open Aardvark.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.Data.Opc
open Aardvark.Rendering.Text

open Aardvark.UI

open Aardvark.UI    

open PRo3D

open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Base.Annotation
open PRo3D.Core

open Chiron

module DrawingApp =


    let mutable usePackedAnnotationRendering = true

   // open Newtonsoft.Json
        
    let closePolyline (a:Annotation) = 
        let firstP = a.points.[0]
        let lastP = a.points.[(a.points.Count-1)]
        match a.projection with
        | Projection.Viewpoint | Projection.Sky | Projection.Bookmark ->     
            let newSegment = { startPoint = firstP; endPoint = lastP; points = IndexList.ofList [firstP;lastP] }

            if PRo3D.Config.useAsyncIntersections then
                { a with segments = IndexList.add newSegment a.segments }
            else
                let dir = newSegment.endPoint - newSegment.startPoint
                let points = [ 
                        for s in 0 .. PRo3D.Config.sampleCount do
                            yield newSegment.startPoint + dir * (float s / float PRo3D.Config.sampleCount) // world space
                    ]
                let newSegment = { startPoint = firstP; endPoint = lastP; points = IndexList.ofList points }
                { a with segments = IndexList.add newSegment a.segments }
        | _ -> 
            { a with points = a.points |> IndexList.add firstP }
    
    let getFinishedAnnotation up north planet (referenceSystem : Option<SpiceReferenceSystem>) (sampleSurface : Option<V3d -> Option<V3d>>)  (view:CameraView) (model : DrawingModel) =
        match model.working with
        | Some w ->  
            let w = 
                match w.geometry with
                | Geometry.Polygon -> closePolyline w
                | Geometry.TT -> 
                    { 
                        w with 
                            manualDipAngle   = { w.manualDipAngle   with value = 0.0 }
                            manualDipAzimuth = { w.manualDipAzimuth with value = 0.0 }
                    }
                | _ -> w 
        
            let dns = 
                match w.geometry with 
                | Geometry.TT -> 
                    DipAndStrike.calculateManualDipAndStrikeResults up north w
                | _ ->
                    w.points 
                    |> DipAndStrike.calculateDipAndStrikeResults (up) (north)                        

            let w = { w with dnsResults = dns }

            let w = 
                match w.geometry, sampleSurface with
                | Geometry.AxisEllipse, Some sampleSurface
                | Geometry.Axis4PEllipse, Some sampleSurface -> 
                    let geo = false
                    if geo then
                        match EllipticAnnotations.constructAndSampleGeographical planet referenceSystem (IndexList.toArray w.points) sampleSurface with
                        | Some (ellipses, sampledPoints) -> 
                            let points = IndexList.ofArray sampledPoints
                            { w with points = points; ellipticResults = Some { geographicalEllipse = ellipses.[0]; geographicalEllipseAssym = None }}
                        | _ -> 
                            w
                    else
                        match dns with
                        | None -> w
                        | Some dns -> 
                            match EllipticAnnotations.constructAndSampleFromPlane dns.plane (IndexList.toArray w.points) sampleSurface with
                            | Some r -> 
                                let points = IndexList.ofArray r.surfaceProjectedEllipsePoints
                                let ellipses = EllipticAnnotations.ConstructedEllipse.createGeographicalEllipse  planet referenceSystem r
                                { w with points = points; ellipticResults = None }
                            | _ -> 
                                w
                        
                | _ -> 
                    w

            let results = Calculations.calculateAnnotationResults w up north planet

            Some { w with results = Some results; view = view; }
        | None -> None

    let finishAndAppend up north planet (referenceSystem : Option<SpiceReferenceSystem>) (sampleSurface : Option<V3d -> Option<V3d>>) (view:CameraView) (model : DrawingModel)  = 
      
        let groups = 
            match getFinishedAnnotation up north planet referenceSystem sampleSurface view model with
            | Some a -> 
                //let json = a |> JsonTypes.ofAnnotation |> Aardvark.UI.Pickler.jsonToString                 
                //bc.Add json
                model.annotations |> GroupsApp.addLeafToActiveGroup (Leaf.Annotations a) true
            | None -> 
                model.annotations
        
        { model with  working = None; pendingIntersections = ThreadPool.empty; annotations = groups }
    
    //adds new point to working state, if certain conditions are met the annotation finishes itself
    // returns current segment for async computations outside
    let addPoint up north planet (referenceSystem : Option<SpiceReferenceSystem>) (samplePoint : V3d -> Option<V3d>) (p : V3d) view model surfaceName bc bookmarkId =
      
        let working, newSegment = 
            match model.working with
            | Some w ->     
                let annotation = { w with points = w.points |> IndexList.add p }
                Log.line "working contains %d points" annotation.points.Count
                
                // do not generate segments for ellipses as they are sampled when the ellipse is fully constructed (after having the ellipse we know its outline).
                let allowSegmentGeneration = w.geometry <> Geometry.Ellipse && w.geometry <> Geometry.AxisEllipse && w.geometry <> Geometry.Axis4PEllipse

                //fetch current drawing segment (projected, polyline or polygon)
                let result = 
                    match w.projection with
                    | Projection.Viewpoint | Projection.Sky | Projection.Bookmark when allowSegmentGeneration ->                     
                        match IndexList.tryAt (IndexList.count w.points-1) w.points with
                        | None -> 
                            annotation, None
                        | Some a -> 
                            let segmentIndex = IndexList.count annotation.segments
                            let newSegment = { startPoint = a; endPoint = p; points = IndexList.ofList [a;p] }
                            
                            if PRo3D.Config.useAsyncIntersections then
                                { annotation with segments = IndexList.add newSegment annotation.segments }, Some (newSegment,segmentIndex)
                            else
                                let vec = newSegment.endPoint - newSegment.startPoint
                                let dir = vec.Normalized
                                //let step = vec.Length / float PRo3D.Config.sampleCount
                                let numOfSamples = (vec.Length / model.samplingDistance) |> floor |> int

                                let points = [ 
                                    for s in 1 .. numOfSamples do
                                        let p = newSegment.startPoint + dir * (float s) * model.samplingDistance // world space

                                        Log.line "[Drawing] Spawning p: %A at %A" s ((float s) * model.samplingDistance)

                                        match samplePoint p with
                                        | None -> ()
                                        | Some projectedPoint -> yield projectedPoint
                                ]
                                let newSegment = { startPoint = a; endPoint = p; points = IndexList.ofList points }
                                { annotation with segments = IndexList.add newSegment annotation.segments }, None
                    | Projection.Linear ->
                        annotation, None
                    | Projection.Sky when ((w.geometry = Geometry.AxisEllipse) || (w.geometry = Geometry.Axis4PEllipse)) ->
                        annotation, None
                    | _ -> failwith "case does not exist"            
                result 
            | None ->  //no working state, start new working annotation
                // use the active group's default color for newly created annotations
                let groupColor =
                    GroupsApp.getNode model.annotations.activeGroup.path model.annotations.rootGroup
                    |> fun node -> node.defaultColor
                {
                    //annotation states should be immutable after creation
                    //(Annotation.make model.projection model.geometry model.semantic surfaceName)
                    //    with points = IndexList.ofList [p]; modelTrafo = Trafo3d.Translation p
                    (Annotation.make model.projection None model.geometry referenceSystem groupColor model.thickness surfaceName)
                        with points = IndexList.ofList [p]; modelTrafo = Trafo3d.Translation p
                }, None
      
        //let text = 
        //      match model.geometry with
        //          | Geometry.Point -> "x:" + p.X.ToString() + ", y:" + p.Y.ToString() + ", z:" + p.Z.ToString()
        //          | _ -> ""
        //let working' = { working with text = text }
        let model = { model with working = Some working }
        
        match (working.geometry, (working.points |> IndexList.count)) with
        | Geometry.Point, 1 -> 
            Log.line "Picked single point at: %A" (working.points |> IndexList.tryFirst).Value
            finishAndAppend up north planet referenceSystem (Some samplePoint) view model, None
        | Geometry.TT, 2 | Geometry.Line, 2 -> 
            finishAndAppend up north planet referenceSystem (Some samplePoint) view model, None
        | Geometry.Ellipse, 3 -> 
            finishAndAppend up north planet referenceSystem (Some samplePoint) view model, None
        | Geometry.AxisEllipse, 3 -> 
            finishAndAppend up north planet referenceSystem (Some samplePoint) view model, None
        | Geometry.Axis4PEllipse, 4 ->  
            finishAndAppend up north planet referenceSystem (Some samplePoint) view model, None
        | _ -> 
            model, newSegment 

    let addNewSegment samplePoint model (newSegment : Segment, segmentIndex : int) =
        let dir = newSegment.endPoint - newSegment.startPoint
        let id = Guid.NewGuid() |> string

        let computation = 
            proclist {
                let mutable r = []
                let result = MVar.empty()
                let task = 
                    async {
                        do! Async.SwitchToNewThread()
                        let r = 
                            [ for s in 0 .. PRo3D.Config.sampleCount do
                                let p = newSegment.startPoint + dir * (float s / float PRo3D.Config.sampleCount) // world space
                                match samplePoint p with
                                    | None -> ()
                                    | Some projectedPoint -> // projected point in world space
                                        r <- r @ [projectedPoint]
                                        MVar.put result (Choice1Of2 r)
                                        yield projectedPoint
                            ]
                        MVar.put result (Choice2Of2 ())
                    } |> Async.Start
                
                let rec doIt () =
                    proclist {
                         let! r = Proc.Await (MVar.takeAsync result)
                         match r with
                            | Choice1Of2 r -> 
                                printfn "mked it: %A" r
                                let segment = { newSegment with points = IndexList.ofList r}
                                yield SetSegment(segmentIndex,segment)
                                yield! doIt()
                            | Choice2Of2 _ -> ()
                    }
                
                yield! doIt()
            } 
        
        let pool = 
            if model.pendingIntersections.store.ContainsKey id then 
                ThreadPool.remove id model.pendingIntersections
            else 
                model.pendingIntersections
        { model with pendingIntersections = ThreadPool.add id computation pool }
        
    let pickler = MBrace.FsPickler.Json.JsonSerializer(indent=true)

    let pushUndo (delta : AnnotationsDelta) (model : DrawingModel) =
        { model with undoStack = delta :: model.undoStack; redoStack = [] }

    // Apply a delta in reverse (Undo): restore the annotations to the state before the action.
    let applyUndoDelta (groups : GroupsModel) (delta : AnnotationsDelta) : GroupsModel =
        match delta with
        | LeafAdded (leaf, groupPath) ->
            GroupsApp.removeLeaf groups leaf.id groupPath true
        | LeafRemoved (leaf, groupPath) ->
            let flat' = groups.flat |> HashMap.add leaf.id leaf
            let func  = fun (x : Node) -> { x with leaves = x.leaves |> IndexList.prepend leaf.id }
            let root' = GroupsApp.updateNodeAt groupPath func groups.rootGroup
            { groups with flat = flat'; rootGroup = root' }
        | SnapshotDelta (before, _) ->
            before

    // Apply a delta in the forward direction (Redo): re-apply the action.
    let applyRedoDelta (groups : GroupsModel) (delta : AnnotationsDelta) : GroupsModel =
        match delta with
        | LeafAdded (leaf, groupPath) ->
            let flat' = groups.flat |> HashMap.add leaf.id leaf
            let func  = fun (x : Node) -> { x with leaves = x.leaves |> IndexList.prepend leaf.id }
            let root' = GroupsApp.updateNodeAt groupPath func groups.rootGroup
            { groups with flat = flat'; rootGroup = root' }
        | LeafRemoved (leaf, groupPath) ->
            GroupsApp.removeLeaf groups leaf.id groupPath true
        | SnapshotDelta (_, after) ->
            after

    type SmallConfig<'a> =
        {
            up     : Lens<'a,V3d>
            north  : Lens<'a,V3d>
            planet : Lens<'a,Planet>
        }

    type MSmallConfig<'ma> =
        {            
            getNearPlane        : 'ma -> aval<float>
            getHfov             : 'ma -> aval<float>            
            getArrowThickness   : 'ma -> aval<float>
            getArrowLength      : 'ma -> aval<float>
            getDnsPlaneSize     : 'ma -> aval<float>
            getOffset           : 'ma -> aval<float>
            getPickingTolerance : 'ma -> aval<float>
        }
   
    let cylinders width positions = 
        positions 
        |> Array.pairwise 
        |> Array.map(fun (a,b) -> 
            Line3d(a,b)) 
            |> Array.map (fun x -> Cylinder3d(x, width))

    let intersectAnnotation (hit : SceneHit) id (flat : HashMap<Guid,Leaf>) =
        match (flat.TryFind id) with
        | Some (Leaf.Annotations ann) ->                            
            let mutable hit2 = RayHit3d.MaxRange
            let r = hit.globalRay.Ray.Ray
            
            ann.points 
            |> IndexList.toArray 
            |> cylinders 0.05
            |> Array.tryFind(fun x -> 
                r.HitsCylinder(x.P0, x.P1, x.Radius, &hit2))
            |> Option.map(fun x ->
                let hitPoint = hit2.Point
                let p = Plane3d(x.Axis.Direction, hitPoint)
                let mutable projPoint = V3d.NaN
                p.IntersectsLine(x.Axis.P0,x.Axis.P1, Double.Epsilon, &projPoint) |> ignore

                (ann, projPoint))
        | _ -> None

    let extractVisibleAnnotations (model : DrawingModel) : Annotation list =
        model.annotations.flat
        |> Leaf.toAnnotations
        |> HashMap.toList 
        |> List.map snd
        |> List.filter (fun a -> a.visible)

    let isSelected (model : DrawingModel) = 
        match model.annotations.singleSelectLeaf with
        | None -> fun _ -> false
        | Some s -> 
            fun (a : Annotation) -> a.key = s

    // specifies which drawing actions trigger re-export of geo-json files.
    // the idea behind this is to keep out high-frequency updates (mouse move)
    // but blacklist those
    let automaticallyReExportGeoJson (action : DrawingAction) =
        match action with
        | DrawingAction.Move p  -> false
        | ExportAsGeoJSON _     -> false
        | ExportAsAnnotations _ -> false
        | ExportAsCsv _         -> false
        | ExportAsProfileCsv _  -> false
        | ExportMultiAttributeProfile _ -> false
        | ExportAsGeoJSON_xyz _ -> false
        | ExportAsGeoJSONQGIS_latlon _ -> false
        | ExportAsGeoJSONQGIS_xyz _ -> false
        | ExportAsGeoJSONQGIS_both _ -> false
        | LegacySaveVersioned   -> false
        | _ -> true

    // exports geojson, optionally using XYZ format
    let exportGeoJson 
        (xyz         : bool)
        (bigConfig   : 'a)
        (smallConfig : SmallConfig<'a>)
        (model       : DrawingModel) 
        (path        : string) =

        let annotations = extractVisibleAnnotations model

        try
            if xyz then
                GeoJSONExport.writeGeoJSON None path (isSelected model) annotations
            else 
                let planet = smallConfig.planet.Get(bigConfig)            
                GeoJSONExport.writeGeoJSON (Some planet) path (isSelected model) annotations
        with e -> 
            Log.warn "[Drawing] exportGeoJson failed with %A" e

    let exportGeoJsonQGIS
        (cooConfig   : GeoJsonQGIS.CoordinateConfiguration)
        (model       : DrawingModel) 
        (path        : string) =

        let annotations = extractVisibleAnnotations model

        GeoJSONExport.writeGeoJSONQGIS cooConfig path (isSelected model) annotations


    // exports geojson, optionally using XYZ format
    let exportGeoJsonStream  
        (model       : DrawingModel) 
        (path        : string) =

        let annotations = extractVisibleAnnotations model

        GeoJSONExport.writeStreamGeoJSON_XYZ (isSelected model) path annotations

    let finish (bigConfig  : 'a) (smallConfig : SmallConfig<'a> ) (model : DrawingModel) (view : CameraView) =
        let up     = smallConfig.up.Get(bigConfig)
        let north  = smallConfig.north.Get(bigConfig)
        let planet = smallConfig.planet.Get(bigConfig)
        let groupPath  = model.annotations.activeGroup.path
        let flatBefore = model.annotations.flat
        let result = finishAndAppend up north planet None None view model
        let newLeaf =
            result.annotations.flat
            |> HashMap.toList
            |> List.tryFind (fun (id, _) -> not (HashMap.containsKey id flatBefore))
            |> Option.map snd
        match newLeaf with
        | Some leaf -> result |> pushUndo (LeafAdded(leaf, groupPath))
        | None      -> result

    type ProfilePoint = {
        position  : V3d
        elevation : double
    }

    let rec accumulateDistance 
        (input    : list<ProfilePoint * ProfilePoint>)
        (distance : double) 
        : list<double * double> =
        
        match input with
        | (a, b) :: [] ->
            [(distance, a.elevation); (distance + Vec.distance a.position b.position, b.elevation)]
        | (a, b) :: vs ->
            (distance, a.elevation) :: (accumulateDistance vs (distance + (Vec.distance a.position b.position)))
        | _ ->
            []

    let rec update<'a> 
        (bigConfig       : 'a) 
        (smallConfig     : SmallConfig<'a> ) 
        (referenceSystem : Option<SpiceReferenceSystem>)
        (webSocket   : BlockingCollection<string>) 
        (view        : CameraView) 
        (shiftFlag   : bool)
        (model       : DrawingModel) 
        (act         : DrawingAction) =

        let newModel =
            match (act, model.draw, model.pick) with
            | StartDrawing, _, false ->                     
                { model with draw = true }
            | StopDrawing, _, false -> 
                { model with draw = false; hoverPosition = None; pick = false }
            | StartPicking, _, _ ->                                       
                { model with pick = true }
            | StopPicking, _, _ -> 
                { model with pick = false}        
            | DrawingAction.Move p, true, false -> 
                { model with hoverPosition = Some (Trafo3d.Translation p) }
            | AddPointAdv (point, projectSurface, referenceFrame, name, bookmarkId), true, false ->
                let up    = smallConfig.up.Get(bigConfig)
                let north = smallConfig.north.Get(bigConfig)
                let planet = smallConfig.planet.Get(bigConfig)
                let groupPath  = model.annotations.activeGroup.path
                let flatBefore = model.annotations.flat

                let model', newSegment = addPoint up north planet referenceFrame projectSurface point view model name webSocket bookmarkId
                let model' =
                    match newSegment with
                    | None         -> model'
                    | Some segment -> addNewSegment projectSurface model' segment

                // For geometries that auto-finish after N points (Line, Point, Ellipse, …),
                // addPoint calls finishAndAppend internally. Detect that by checking whether
                // a new leaf appeared in the flat map and push the undo delta here.
                let newLeaf =
                    model'.annotations.flat
                    |> HashMap.toList
                    |> List.tryFind (fun (id, _) -> not (HashMap.containsKey id flatBefore))
                    |> Option.map snd
                match newLeaf with
                | Some leaf -> model' |> pushUndo (LeafAdded(leaf, groupPath))
                | None      -> model'
            | RemoveLastPoint, _, _ -> 
              //let annotation = { w with points = w.points |> IndexList.append p }
              // { annotation with segments = IndexList.append newSegment annotation.segments }
          
                match model.working with
                | Some w when w.points.Count > 0->
                  { model with working = Some { w with points = w.points |> IndexList.removeAt (w.points.Count - 1); 
                                                    segments = w.segments |> IndexList.removeAt (w.segments.Count - 1)}}
                | Some _ -> { model with working = None }
                | None -> model
            | SetSegment(segmentIndex,segment), _, _ ->
                match model.working with
                | None -> model
                | Some w ->                         
                    { model with working = Some { w with segments = IndexList.setAt segmentIndex segment w.segments } }
            | Finish, _, _ -> 
                finish bigConfig smallConfig model view
            | Exit, _, _ -> 
                { model with hoverPosition = None }
            | SetSemantic mode, _, _ ->
                let model =
                    match mode with
                    | Semantic.GrainSize -> { model with geometry = Geometry.Line }
                    | _ -> model

                {model with semantic = mode }
            | SetGeometry mode, _, _ ->
                let projection = 
                    match mode with
                    | Geometry.AxisEllipse
                    | Geometry.Axis4PEllipse -> 
                        Projection.Sky
                    // every other tool uses linear as default. TODO: if switching back to default is an UX problem, 
                    // we need to store the user-set projection mode and reset it if needed.
                    | _ -> Projection.Linear

                { model with geometry = mode; projection = projection; }
            | SetProjection mode, _, _ ->
                { model with projection = mode }                  
            | ChangeThickness th, _, _ ->
                { model with thickness = Numeric.update model.thickness th }
            | ChangeSamplingAmount k, _, _ ->
                let samplingAmount = Numeric.update model.samplingAmount k
                { model with samplingAmount = samplingAmount ; samplingDistance = DrawingModel.calculateSamplingDistance samplingAmount model.samplingUnit }
            | SetSamplingUnit k, _, _ ->
                { model with samplingUnit = k; samplingDistance = DrawingModel.calculateSamplingDistance model.samplingAmount k }
            | SetExportPath s, _, _ ->
                { model with exportPath = Some s }        
            | Send, _, _ ->                                                      
                model
            | ClearWorking,_ , _->
                { model with working = None }
            | DrawingAction.Clear,_ , _->
                let before = model.annotations
                let after  = GroupsModel.initial
                { model with annotations = after } |> pushUndo (SnapshotDelta(before, after))
            | DrawingAction.Nop, _, _ -> model
            | Undo, _, _ ->
                match model.undoStack with
                | [] -> model
                | delta :: rest ->
                    let annotations = applyUndoDelta model.annotations delta
                    { model with annotations = annotations; undoStack = rest; redoStack = delta :: model.redoStack }
            | Redo, _, _ ->
                match model.redoStack with
                | [] -> model
                | delta :: rest ->
                    let annotations = applyRedoDelta model.annotations delta
                    { model with annotations = annotations; undoStack = delta :: model.undoStack; redoStack = rest }
            | GroupsMessage msg,_, _ ->
                let annotations = GroupsApp.update model.annotations msg
                let model' = { model with annotations = annotations }
                match msg with
                | GroupsAppAction.RemoveLeaf (id, path) ->
                    match model.annotations.flat.TryFind id with
                    | Some leaf -> model' |> pushUndo (LeafRemoved(leaf, path))
                    | None      -> model'
                | GroupsAppAction.RemoveGroup _ | GroupsAppAction.ClearGroup _ ->
                    model' |> pushUndo (SnapshotDelta(model.annotations, annotations))
                | _ -> model'
            | RecalculateMeasurements, _,_ -> 
                let up    = smallConfig.up.Get(bigConfig)
                let north = smallConfig.north.Get(bigConfig)
                let planet = smallConfig.planet.Get(bigConfig)
                
                let selected = 
                    model.annotations.selectedLeaves
                    |> HashSet.map(fun selection -> selection.id)                    

                let selected = 
                    if selected.IsEmpty then
                        model.annotations.singleSelectLeaf
                        |> Option.map(fun leafGuid -> 
                            HashSet.empty |> HashSet.add leafGuid)
                        |> Option.defaultValue selected
                    else
                        selected

                let annotationsFlat = 
                    selected
                    |> HashSet.fold(fun annotations guid -> 
                        let a = 
                            model.annotations.flat.TryFind guid
                            |> Option.map (fun anno -> anno |> Leaf.toAnnotation)
                            
                        match a with 
                        | Some annotation -> 
                            let results = Calculations.calculateAnnotationResults annotation up north planet
                            let annotation = { annotation with results = Some(results) }
                            annotations |> HashMap.add guid (Leaf.Annotations annotation)
                        | None -> annotations
                        ) model.annotations.flat
                
                { model with annotations = { model.annotations with flat = annotationsFlat }}
            | DnsColorLegendMessage msg,_, _ -> 
                { model with dnsColorLegend = FalseColorLegendApp.update model.dnsColorLegend msg }
            | FlyToAnnotation msg, _, _ ->               
                model        

            // method via bvh
            | PickAnnotation (_, id), false, true | PickDirectly id, false, true ->
                match (model.annotations.flat.TryFind id) with
                | Some (Leaf.Annotations ann) ->       
                            
                    //Log.error "[DrawingApp] shiftflag is %A" shiftFlag
                    // { model with annotations = Groups.addSingleSelectedLeaf model.annotations list.Empty ann.key "" }              
                    let annotations =
                        if shiftFlag then
                            Log.line "[DrawingApp] multi select"
                            GroupsApp.update model.annotations (GroupsAppAction.AddLeafToSelection(List.empty, ann.key, String.Empty))
                        else
                            Log.line "[DrawingApp] single select"
                            GroupsApp.update model.annotations (GroupsAppAction.SingleSelectLeaf(List.empty, ann.key, String.Empty))
                    
                    { model with annotations = annotations }

                | _ -> model        
            | AddAnnotations path, _,_ ->
                match path |> List.tryHead with
                | Some p -> 
                    let annos = DrawingUtilities.IO.loadAnnotationsFromFile p
                    Log.line "[Drawing] Merging annotations"                
                    let merged = GroupsApp.union model.annotations annos.annotations
                    { model with annotations = merged }
                | None ->
                    model
            | ExportAsAnnotations path, _, _ ->
                if path.IsNullOrEmpty() |> not then
                    Drawing.IO.saveVersioned model path
                else
                    model
            | ExportAsCsv path, _, _ ->
                if path.IsNullOrEmpty() |> not then 
                    let up = smallConfig.up.Get(bigConfig)
                    let lookups = GroupsApp.updateGroupsLookup model.annotations
                    let annotations = extractVisibleAnnotations model
                    CSVExport.writeCSV lookups up path annotations
                model      
            | ExportAsProfileCsv path, _, _ ->
                //get selected annotation
                let selected =  GroupsModel.tryGetSelectedAnnotation model.annotations
                match selected with
                | Some a -> 
                    //convert points to profile
                    let points = a |> Annotation.retrievePoints
                        
                    //transform to distance elevation pairs
                    let planet = smallConfig.planet.Get(bigConfig)
                    let convertedOpt =
                        points
                        |> List.map(fun x ->
                            match CooTransformation.tryGetLatLonAlt planet x with
                            | None -> None
                            | Some k ->
                                CooTransformation.tryGetXYZFromLatLonAlt { k with altitude = 0.0 } planet
                                |> Option.map (fun projected ->
                                    { position = projected; elevation = k.altitude }))

                    if convertedOpt |> List.exists Option.isNone then
                        Log.warn "[DrawingApp] profile export aborted: one or more points could not be converted to lat/lon"
                    else
                        let transformed = convertedOpt |> List.choose id |> List.pairwise
                        let profile =
                            accumulateDistance transformed 0.0

                        let csvTable =
                            profile
                            |> List.map (fun (d,e) -> {| distance = d; elevation = e |})
                            |> CSV.Seq.csv "," true id

                        if path.IsEmptyOrNull() |> not then
                            csvTable |> CSV.Seq.write path

                        Log.line "[DrawingApp] wrote %A to %s" profile path
                | None -> 
                    Log.line "please select annotation to export"
                //write csv

                model
            | ExportMultiAttributeProfile _, _, _ ->
                // handled at viewer level (needs access to SurfaceModel)
                model
            | ExportAsGeoJSON path, _, _ ->
                if path.IsNullOrEmpty() |> not then
                    exportGeoJson false bigConfig smallConfig model path
                model

            | ExportAsGeoJSON_xyz path, _, _ ->                       
                if path.IsNullOrEmpty() |> not then         
                    exportGeoJson true bigConfig smallConfig model path
                model

            | ExportAsGeoJSONQGIS_latlon path, _, _ ->     
                if path.IsNullOrEmpty() |> not then 
                    let planet = smallConfig.planet.Get(bigConfig).ToString()
                    exportGeoJsonQGIS (GeoJsonQGIS.CoordinateConfiguration.GeographicOnly planet) model path
                model

            | ExportAsGeoJSONQGIS_xyz path, _, _ ->      
                if path.IsNullOrEmpty() |> not then 
                    exportGeoJsonQGIS GeoJsonQGIS.CoordinateConfiguration.CartesianOnly model path
                model

            | ExportAsGeoJSONQGIS_both path, _, _ ->    
                if path.IsNullOrEmpty() |> not then 
                    let planet = smallConfig.planet.Get(bigConfig).ToString()
                    exportGeoJsonQGIS (GeoJsonQGIS.CoordinateConfiguration.Both planet) model path
                model

            | ContinuouslyGeoJson path, _, _ -> 
                if path.IsNullOrEmpty() |> not then 
                    // remember this path in order to drive the automatic export feature.
                    let updatedPath = { model.automaticGeoJsonExport with lastGeoJsonPathXyz = Some path; enabled = true }
                    { model with automaticGeoJsonExport = updatedPath }
                else
                    model

            | ExportAsAttitude path, _, _ ->
                if path.IsNullOrEmpty() |> not then
                    let annotations = extractVisibleAnnotations model
                    AttitudeExport.writeAttitudeJson path (smallConfig.up.Get(bigConfig)) annotations
                model

            | LegacySaveVersioned, _,_ ->
                let path = "./annotations.json"
                let pathgGrouping = "./annotations.grouping"
            
                Log.line "[Drawing] Writing annotations"
                model.annotations.flat 
                    |> HashMap.toList 
                    |> List.map(fun (_,b) -> b |> Leaf.toAnnotation) // |> Annotation'.convert)
                    |> Json.serialize |> Json.formatWith JsonFormattingOptions.SingleLine |> Serialization.writeToFile path // CHECK-merge IO.
            
                Log.line "[Drawing] Writing grouping"
                let annotations' = 
                    { model.annotations with flat = HashMap.empty } 
                    |> Serialization.save pathgGrouping

                { model with annotations = annotations' }
                //model
            | LegacyLoadVersioned, _,_ ->
                let path = "./annotations.json"
                let pathgGrouping = "./annotations.grouping"

                Log.line "[Drawing] Reading annotations"
                let (annos : list<Annotation>) = path |> Serialization.readFromFile |> Json.parse |> Json.deserialize // CHECK-merge IO.
                let annos = annos |> List.map(fun x -> (x.key,x |> Leaf.Annotations)) |> HashMap.ofList

                Log.line "[Drawing] Reading grouping"
                let grouping = Serialization.loadAs<GroupsModel> pathgGrouping
                let grouping = { grouping with flat = annos }

                { model with annotations = grouping }

            | _ -> model

        // optionally also store geojson to disk
        match automaticallyReExportGeoJson act && newModel.automaticGeoJsonExport.enabled with
        | true -> 
            match newModel.automaticGeoJsonExport.lastGeoJsonPathXyz with
            | Some path -> 
                Log.line "[Drawing] automatically writing geojson.xyz file to %s since the annotations have changed." path
                // virtually finish the annotation (as if closed by interaction) - to let it be part of the exported ones.
                let artificiallyFinishedModel = finish bigConfig smallConfig model view
                exportGeoJsonStream artificiallyFinishedModel path
            | _ -> ()
            newModel
        | false -> 
            newModel
                                    
    let threads (m : DrawingModel) = m.pendingIntersections
    
    let tryToAnnotation : AdaptiveLeafCase -> Option<AdaptiveAnnotation> = 
        function
        | AdaptiveAnnotations ann -> Some ann
        | _ -> None
       
    let viewTextLabels<'ma> 
        (mbigConfig       : 'ma)
        (msmallConfig     : MSmallConfig<'ma>)
        (view             : aval<CameraView>)      
        (model            : AdaptiveDrawingModel) =

        let config : Sg.innerViewConfig = 
            {
                nearPlane        = msmallConfig.getNearPlane        mbigConfig
                hfov             = msmallConfig.getHfov             mbigConfig                    
                arrowLength      = msmallConfig.getArrowLength      mbigConfig
                arrowThickness   = msmallConfig.getArrowThickness   mbigConfig
                dnsPlaneSize     = msmallConfig.getDnsPlaneSize     mbigConfig
                offset           = msmallConfig.getOffset           mbigConfig
                pickingTolerance = msmallConfig.getPickingTolerance mbigConfig
            }

        let labels = 
            model.annotations.flat 
            |> AMap.toASetValues
            |> ASet.chooseA (fun anno -> 
                match anno |> tryToAnnotation with
                | None -> AVal.constant None
                | Some v -> 
                    Sg.shouldTextBeRendered v 
                    |> AVal.map (function | true -> Some (Sg.drawText view config v) | _ -> None)
               ) 
            |> Sg.set

        labels

    let view<'ma> 
        (mbigConfig       : 'ma)
        (msmallConfig     : MSmallConfig<'ma>)
        (observerSystem : aval<Option<ObserverSystem>>)
        (view             : aval<CameraView>)
        (frustum          : aval<Frustum>)
        (runtime          : IRuntime)
        (viewport         : aval<V2i>)
        (pickingAllowed   : aval<bool>)        
        (model            : AdaptiveDrawingModel)
        : ISg<DrawingAction> * ISg<DrawingAction> =
        // order is irrelevant for rendering. change list to set,
        // since set provides more degrees of freedom for the compiler           
        let annoSet = 
            model.annotations.flat 
            |> AMap.choose (fun _ y -> 
                    match y |> tryToAnnotation with 
                    | None -> None
                    | Some v -> 
                        let spiceTrafo = 
                            (v.referenceSystem, observerSystem) ||> AVal.map2 (fun observedSystem observerSystem -> 
                                match observedSystem, observerSystem with
                                | Some observedSystem, Some observerSystem -> 
                                    CooTransformation.transformBody observedSystem.body (Some observedSystem.referenceFrame) observerSystem.body observerSystem.referenceFrame observerSystem.time
                                    |> Option.map (fun t -> t.Trafo) 
                                    |> Option.defaultValue Trafo3d.Identity
                                | _ -> Trafo3d.Identity
                            )
                        Some (v, spiceTrafo)
            ) 
            |> AMap.toASet

        let config : Sg.innerViewConfig = 
            {
                nearPlane        = msmallConfig.getNearPlane        mbigConfig
                hfov             = msmallConfig.getHfov             mbigConfig                    
                arrowLength      = msmallConfig.getArrowLength      mbigConfig
                arrowThickness   = msmallConfig.getArrowThickness   mbigConfig
                dnsPlaneSize     = msmallConfig.getDnsPlaneSize     mbigConfig
                offset           = msmallConfig.getOffset           mbigConfig
                pickingTolerance = msmallConfig.getPickingTolerance mbigConfig
            }
       
        if usePackedAnnotationRendering then

            Log.startTimed "[Drawing] creating finished annotation geometry"
            let annotations =              
                annoSet 
                |> ASet.map(fun (_,(a, t)) -> 
                    let c = UI.mkColor model.annotations a
                    let picked = UI.isSingleSelect model.annotations a
                    let showPoints = 
                        a.geometry 
                        |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)
                
                    let sg = Sg.finishedAnnotation a c config view viewport showPoints picked pickingAllowed
                    sg 
                )
                |> Sg.set               
            Log.stop()

            let hoveredAnnotation = cval -1
            let viewMatrix = view |> AVal.map (fun v -> (CameraView.viewTrafo v).Forward)
            let lines, pickIds, bb = PackedRendering.linesNoIndirect config.offset hoveredAnnotation (model.annotations.selectedLeaves |> ASet.map (fun e -> e.id)) (annoSet |> ASet.map ((fun (g, (s,t)) -> g,s))) viewMatrix
            let pickRenderTarget = PackedRendering.pickRenderTarget runtime config.pickingTolerance lines view frustum viewport
            pickRenderTarget.Acquire()
            let packedLines = 
                let simple (kind : SceneEventKind) (f : SceneHit -> seq<'msg>) =
                    kind, fun evt -> true, Seq.delay (fun () -> (f evt))
                PackedRendering.packedRender lines 
                |> Sg.noEvents
                |> Sg.pickable' (bb |> AVal.map PickShape.Box)
                |> Sg.withEvents [
                       simple SceneEventKind.Move (fun (evt : SceneHit) -> 
                            try
                                let r = pickRenderTarget.GetValue(AdaptiveToken.Top,RenderToken.Empty)
                                let offset = V2i(clamp 0 (r.Size.X - 1) evt.event.evtPixel.X, clamp 0 (r.Size.Y - 1) evt.event.evtPixel.Y)
                                let box = Box2i.FromMinAndSize(offset, V2i(1,1))
                                let r = runtime.Download(r, 0, 0, box) |> unbox<PixImage<float32>>
                                let m = r.GetMatrix<C4f>()
                                let allowed = pickingAllowed.GetValue()
                                let p = m.[0,0]
                                let id : int = floor p.A |> int //BitConverter.SingleToInt32Bits(p.A)
                                let ids = pickIds.GetValue()
                                if id >= 0 && id < ids.Length  && allowed then
                                    //Log.line "hoverhit %A" (id, ids.[id])
                                    transact (fun _ -> hoveredAnnotation.Value <- id)
                                    Seq.empty
                                else 
                                    transact (fun _ -> hoveredAnnotation.Value <- -1)
                                    Seq.empty
                            with e -> Seq.empty
                       )
                       Sg.onMouseDown (fun b p -> 
                            let id = hoveredAnnotation.GetValue()
                            let ids = pickIds.GetValue()
                            if id >= 0 && id < ids.Length then
                                Log.line "clickhit %A" (id, ids.[id])
                                DrawingAction.PickDirectly(ids.[id])
                            else 
                                DrawingAction.Nop
                       )
                ]
            let packedPoints = 
                PackedRendering.points (model.annotations.selectedLeaves |> ASet.map (fun l -> l.id)) (annoSet |> ASet.map ((fun (g, (s,t)) -> g,s))) config.offset viewMatrix
                |> Sg.noEvents

            let overlay = 
                Sg.ofList [
                    // brush model.hoverPosition; 
                    annotations
                    Sg.ofSeq [packedLines; packedPoints]
                    Sg.drawWorkingAnnotation config.offset (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption model.working) // TODO v5: why need fully qualified
                ]

            //let depthTest = 
            //    annoSet 
            //    |> ASet.map(fun (_,a) -> Sg.finishedAnnotationDiscs a config model.dnsColorLegend view) |> Sg.set

            let depthTest = 
                PackedRendering.fastDns config model.dnsColorLegend (annoSet |> ASet.map ((fun (g, (s,t)) -> g,s))) view
                |> Sg.noEvents

            (overlay, depthTest)

        else 
            Log.startTimed "[Drawing] creating finished annotation geometry"
            let annotations =              
                annoSet 
                |> ASet.map(fun (_,(a,t)) -> 
                    let c = UI.mkColor model.annotations a
                    let picked = UI.isSingleSelect model.annotations a
                    let showPoints = 
                        a.geometry 
                        |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)

                    let sg = 
                        Sg.finishedAnnotationOld a c config view viewport showPoints picked pickingAllowed
                        |> Sg.trafo t

                    sg 
                 )
                |> Sg.set               
            Log.stop()
                                  
            let overlay = 
                Sg.ofList [
                    // brush model.hoverPosition; 
                    annotations
                    Sg.drawWorkingAnnotation config.offset (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption model.working) // TODO v5: why need fully qualified
                ]

            let depthTest = 
                annoSet 
                |> ASet.map(fun (_,(a,t)) -> Sg.finishedAnnotationDiscs a config model.dnsColorLegend view |> Sg.trafo t) |> Sg.set

            (overlay, depthTest)
            
