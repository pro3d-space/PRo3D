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

    /// Walks the straight line from `a` to `b` in `samplingDistance` steps and projects each step
    /// onto the surface with `samplePoint`, dropping the steps that miss.
    ///
    /// The resulting Segment carries only the *interior* samples in `points`; `getPolylinePoints`
    /// puts `startPoint` and `endPoint` around them when it flattens the annotation.
    ///
    /// Shared by `addPoint` (a freshly drawn segment) and `MoveVertex` (re-sampling the segments
    /// either side of a moved control point) so the two cannot drift apart.
    let resampleSegment (samplingDistance : float) (samplePoint : V3d -> Option<V3d>) (a : V3d) (b : V3d) : Segment =
        let vec    = b - a
        let length = vec.Length
        // A zero-length segment has no direction to walk along, and a non-positive sampling
        // distance would ask for infinitely many steps. Both collapse to a bare start/end pair.
        // The first case is reachable from vertex editing: dropping a point onto its neighbour.
        if length <= 0.0 || samplingDistance <= 0.0 then
            { startPoint = a; endPoint = b; points = IndexList.empty }
        else
            let dir          = vec / length
            let numOfSamples = (length / samplingDistance) |> floor |> int
            let points =
                [ for s in 1 .. numOfSamples do
                    let p = a + dir * (float s) * samplingDistance // world space
                    match samplePoint p with
                    | None -> ()
                    | Some projectedPoint -> yield projectedPoint ]
            { startPoint = a; endPoint = b; points = IndexList.ofList points }

    /// Whether an annotation's segments form a closed ring.
    ///
    /// `closePolyline` appends one extra segment joining the last point back to the first, so a
    /// ring has exactly as many segments as points where an open chain has one fewer. Derived from
    /// the counts rather than from `geometry`, because that is the invariant the segment indices
    /// actually rest on.
    let private hasClosingSegment (pointCount : int) (segmentCount : int) =
        segmentCount >= pointCount

    /// The segments invalidated by moving control point `pointIndex`.
    ///
    /// Segment j runs points[j] -> points[j+1], so an interior point sits between segments j-1 and
    /// j. On a ring the first and last points additionally bound the trailing closing segment.
    let touchedSegments (pointCount : int) (segmentCount : int) (pointIndex : int) : list<int> =
        if segmentCount <= 0 || pointIndex < 0 || pointIndex >= pointCount then
            []
        else
            let isClosed = hasClosingSegment pointCount segmentCount
            let closing  = segmentCount - 1
            let before =
                if pointIndex > 0 then Some (pointIndex - 1)
                elif isClosed then Some closing
                else None
            let after =
                if pointIndex < pointCount - 1 then Some pointIndex
                elif isClosed then Some closing
                else None
            [ before; after ]
            |> List.choose id
            |> List.distinct
            |> List.filter (fun i -> i >= 0 && i < segmentCount)

    /// The two control points segment `segmentIndex` spans, or None if the index is out of range.
    /// The closing segment of a ring runs first -> last, matching how `closePolyline` builds it.
    let segmentEndpoints (points : IndexList<V3d>) (segmentCount : int) (segmentIndex : int) : Option<V3d * V3d> =
        let pointCount = IndexList.count points
        if hasClosingSegment pointCount segmentCount && segmentIndex = segmentCount - 1 then
            match IndexList.tryFirst points, IndexList.tryLast points with
            | Some first, Some last -> Some (first, last)
            | _ -> None
        else
            match IndexList.tryAt segmentIndex points, IndexList.tryAt (segmentIndex + 1) points with
            | Some a, Some b -> Some (a, b)
            | _ -> None

    /// Moves control point `pointIndex` to `position` and brings the annotation back into a
    /// consistent state: the segments either side of the point are re-sampled onto the surface, and
    /// the derived measurements are recomputed.
    ///
    /// Annotations drawn with `Projection.Linear` - which `SetGeometry` selects for every tool
    /// except the two ellipse ones - carry no segments at all, and none are invented here: the
    /// annotation keeps the shape it was drawn with.
    ///
    /// `modelTrafo` is deliberately left alone. It is the precision anchor the packed renderer
    /// works in, and re-anchoring it on every edit would be churn for no gain.
    let moveVertex
        (up : V3d) (north : V3d) (planet : Planet)
        (samplingDistance : float) (samplePoint : V3d -> Option<V3d>)
        (pointIndex : int) (position : V3d)
        (a : Annotation) : Annotation =

        if pointIndex < 0 || pointIndex >= IndexList.count a.points then
            a
        else
            let points = a.points |> IndexList.setAt pointIndex position

            let segmentCount = IndexList.count a.segments
            let segments =
                if segmentCount = 0 then
                    a.segments
                else
                    touchedSegments (IndexList.count points) segmentCount pointIndex
                    |> List.fold (fun (segs : IndexList<Segment>) i ->
                        match segmentEndpoints points segmentCount i with
                        | Some (s, e) -> segs |> IndexList.setAt i (resampleSegment samplingDistance samplePoint s e)
                        | None        -> segs
                    ) a.segments

            let a = { a with points = points; segments = segments }
            let a = { a with dnsResults = points |> DipAndStrike.calculateDipAndStrikeResults up north }
            { a with results = Some (Calculations.calculateAnnotationResults a up north planet) }

    let closePolyline (a:Annotation) =
        let firstP = a.points.[0]
        let lastP = a.points.[(a.points.Count-1)]
        match a.projection with
        | Projection.Viewpoint | Projection.Sky ->
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
                    | Projection.Viewpoint | Projection.Sky when allowSegmentGeneration ->
                        match IndexList.tryAt (IndexList.count w.points-1) w.points with
                        | None -> 
                            annotation, None
                        | Some a ->
                            let segmentIndex = IndexList.count annotation.segments

                            if PRo3D.Config.useAsyncIntersections then
                                let newSegment = { startPoint = a; endPoint = p; points = IndexList.ofList [a;p] }
                                { annotation with segments = IndexList.add newSegment annotation.segments }, Some (newSegment,segmentIndex)
                            else
                                let newSegment = resampleSegment model.samplingDistance samplePoint a p
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
                        with points = IndexList.ofList [p]
                             modelTrafo = Trafo3d.Translation p
                             // fillColor is left as make set it: the active group's default colour
                             showFill = model.fillNewAnnotations
                             fillAlpha = model.defaultFillAlpha
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
        |> List.filter (fun (a : Annotation) -> a.visible)

    let isSelected (model : DrawingModel) =
        let multiSelected =
            model.annotations.selectedLeaves
            |> HashSet.map (fun s -> s.id)
        match model.annotations.singleSelectLeaf with
        | None -> fun (a : Annotation) -> multiSelected |> HashSet.contains a.key
        | Some s -> fun (a : Annotation) -> a.key = s || multiSelected |> HashSet.contains a.key

    // specifies which drawing actions trigger re-export of geo-json files.
    // the idea behind this is to keep out high-frequency updates (mouse move)
    // but blacklist those
    let automaticallyReExportGeoJson (action : DrawingAction) =
        match action with
        | DrawingAction.Move p  -> false
        // picking a control point up and putting it back down again change no geometry; only the
        // drop (MoveVertex) does, and that one falls through to true below
        | GrabVertex _          -> false
        | ArmVertexGrab         -> false
        | CancelVertexEdit      -> false
        | ExportAsAnnotations _ -> false
        | LegacySaveVersioned   -> false
        | _ -> true

    /// Arms the automatic GeoJSON export: remembers the path and switches the
    /// feature on. Nothing is written here — the file is rewritten at the end of
    /// every `update` that changed something worth re-exporting.
    let armAutomaticGeoJsonExport (path : string) (model : DrawingModel) =
        if path.IsNullOrEmpty() then model
        else
            { model with
                automaticGeoJsonExport =
                    { model.automaticGeoJsonExport with lastGeoJsonPathXyz = Some path; enabled = true } }

    /// Disarms the automatic GeoJSON export. Clears the path as well as the flag
    /// so a later re-arm cannot silently resume writing to a forgotten file.
    let disarmAutomaticGeoJsonExport (model : DrawingModel) =
        { model with
            automaticGeoJsonExport =
                { model.automaticGeoJsonExport with enabled = false; lastGeoJsonPathXyz = None } }

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
                // keep the current projection if the new geometry supports it, otherwise
                // fall back to that geometry's first allowed projection.
                let allowed = Geometry.allowedProjections mode
                let projection =
                    if allowed |> List.contains model.projection then model.projection
                    else
                        match allowed with
                        | p :: _ -> p
                        | []     -> model.projection

                { model with geometry = mode; projection = projection; }
            | SetProjection mode, _, _ ->
                // the dropdown greys out projections the current geometry cannot use;
                // ignore them here too in case the message arrives another way.
                if Geometry.allowedProjections model.geometry |> List.contains mode then
                    { model with projection = mode }
                else
                    model

            | ChangeThickness th, _, _ ->
                { model with thickness = Numeric.update model.thickness th }
            | SetFillNewAnnotations b, _, _ ->
                { model with fillNewAnnotations = b }
            | ChangeDefaultFillAlpha a, _, _ ->
                { model with defaultFillAlpha = Numeric.update model.defaultFillAlpha a }
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
            | AddCutStrokePoint p, _, _ ->
                match GroupsModel.tryGetSelectedAnnotation model.annotations with
                | None ->
                    Log.warn "[Drawing] select the annotation to cut before drawing the stroke"
                    model
                | Some target ->
                    let points =
                        match model.cutStroke with
                        | Some s -> s.points |> IndexList.add p
                        | None   -> IndexList.single p
                    // dry-run feedback: the ends-outside precondition is invisible on terrain,
                    // so the stroke itself answers "would this cut" by colour
                    let wouldCut =
                        points.Count >= 2 &&
                        (match PRo3D.Base.Geometry.AnnotationRegionOps.cut (fun _ -> None) target (points |> IndexList.toArray) with
                         | Result.Ok _ -> true
                         | Result.Error _ -> false)
                    let color = if wouldCut then C4b(60uy, 200uy, 90uy) else C4b(230uy, 70uy, 60uy)
                    let stroke =
                        { Annotation.make Projection.Linear None Geometry.Line None { c = color } model.thickness "" with
                            points = points }
                    { model with cutStroke = Some stroke }

            | RemoveLastCutPoint, _, _ ->
                match model.cutStroke with
                | Some s when s.points.Count > 1 ->
                    { model with cutStroke = Some { s with points = s.points |> IndexList.removeAt (s.points.Count - 1) } }
                | Some _ -> { model with cutStroke = None }
                | None -> model

            | ClearCutStroke, _, _ ->
                { model with cutStroke = None }

            | ApplyCutStroke projectToSurface, _, _ ->
                match model.cutStroke, GroupsModel.tryGetSelectedAnnotation model.annotations with
                | Some stroke, Some target when stroke.points.Count >= 2 ->
                    let projectToSurface = projectToSurface |> Option.defaultValue (fun _ -> None)
                    match PRo3D.Base.Geometry.AnnotationRegionOps.cut projectToSurface target (stroke.points |> IndexList.toArray) with
                    | Result.Ok rings ->
                        let up     = smallConfig.up.Get(bigConfig)
                        let north  = smallConfig.north.Get(bigConfig)
                        let planet = smallConfig.planet.Get(bigConfig)

                        let before  = model.annotations
                        let removed = GroupsApp.removeLeafById target.key model.annotations

                        // metadata copied to every piece (the decided design); rings stored
                        // closed like drawn polygons, results recomputed
                        let makePiece (ring : V3d[]) =
                            let closed =
                                if ring.Length > 2 then Array.append ring [| ring.[0] |] else ring
                            let a =
                                { target with
                                    key             = Guid.NewGuid()
                                    geometry        = Geometry.Polygon
                                    points          = IndexList.ofArray closed
                                    segments        = IndexList.empty
                                    dnsResults      = None
                                    ellipticResults = None }
                            { a with results = Some (Calculations.calculateAnnotationResults a up north planet) }

                        let after =
                            rings
                            |> List.fold (fun g ring ->
                                GroupsApp.addLeafToActiveGroup (Leaf.Annotations (makePiece ring)) false g) removed

                        { model with annotations = after; cutStroke = None }
                        |> pushUndo (SnapshotDelta(before, after))
                    | Result.Error refusal ->
                        // the stroke stays on screen so it can be corrected
                        Log.warn "[Drawing] cut refused: %A" refusal
                        model
                | _ ->
                    Log.warn "[Drawing] cutting needs a selected annotation and a stroke of at least two points"
                    model

            | UnionSelectedAnnotations projectToSurface, _, _ ->
                let projectToSurface = projectToSurface |> Option.defaultValue (fun _ -> None)

                // the selection is an unordered set; the depth-first tree walk makes the operand
                // order - and with it the "first wins" metadata policy - deterministic
                let selectedIds =
                    model.annotations.selectedLeaves |> HashSet.map (fun ts -> ts.id)
                let operands =
                    GroupsApp.collectLeaves model.annotations.rootGroup
                    |> IndexList.toList
                    |> List.filter (fun id -> selectedIds |> HashSet.contains id)
                    |> List.choose (fun id ->
                        model.annotations.flat
                        |> HashMap.tryFind id
                        |> Option.bind (fun leaf ->
                            match leaf with
                            | Leaf.Annotations a -> Some a
                            | _ -> None))

                match operands with
                | first :: _ :: _ ->
                    match PRo3D.Base.Geometry.AnnotationRegionOps.union projectToSurface operands with
                    | Result.Ok rings ->
                        let up     = smallConfig.up.Get(bigConfig)
                        let north  = smallConfig.north.Get(bigConfig)
                        let planet = smallConfig.planet.Get(bigConfig)

                        let consumed = operands |> List.map (fun a -> a.key) |> HashSet.ofList
                        let before   = model.annotations

                        let removed =
                            model.annotations.selectedLeaves
                            |> HashSet.toList
                            |> List.filter (fun ts -> consumed |> HashSet.contains ts.id)
                            |> List.fold (fun g ts -> GroupsApp.removeLeaf g ts.id ts.path true) model.annotations

                        // metadata from the first operand in tree order (the decided policy);
                        // geometry becomes Polygon - a union of ellipses is no longer analytic -
                        // and every derived result is recomputed rather than copied
                        let makeAnnotation (ring : V3d[]) =
                            // drawn polygons store their ring *closed* (closePolyline appends the
                            // first point, Drawing-App.fs:68) and a segment-less annotation
                            // renders as an open polyline between consecutive points - an open
                            // ring would lose its closing edge on screen
                            let closed =
                                if ring.Length > 2 then Array.append ring [| ring.[0] |] else ring
                            let a =
                                { first with
                                    key             = Guid.NewGuid()
                                    geometry        = Geometry.Polygon
                                    points          = IndexList.ofArray closed
                                    segments        = IndexList.empty
                                    dnsResults      = None
                                    ellipticResults = None }
                            { a with results = Some (Calculations.calculateAnnotationResults a up north planet) }

                        let after =
                            rings
                            |> List.fold (fun g ring ->
                                GroupsApp.addLeafToActiveGroup (Leaf.Annotations (makeAnnotation ring)) false g) removed

                        { model with annotations = after }
                        |> pushUndo (SnapshotDelta(before, after))
                    | Result.Error refusal ->
                        Log.warn "[Drawing] union refused: %A" refusal
                        model
                | _ ->
                    Log.warn "[Drawing] union needs at least two selected annotations"
                    model

            | DnsColorLegendMessage msg,_, _ ->
                { model with dnsColorLegend = FalseColorLegendApp.update model.dnsColorLegend msg }
            | ColorByCategoryMessage msg,_, _ ->
                // the annotations are needed because FitRangeToData - and SetAttribute,
                // which auto-fits - scan them for the attribute's min/max
                let annotations =
                    model.annotations.flat |> Leaf.toAnnotations |> HashMap.toList |> List.map snd
                { model with
                    colorByCategory = ColorByCategory.update annotations model.colorByCategory msg }
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
                    
                    // selecting a different annotation abandons any control point being moved
                    { model with annotations = annotations; vertexGrab = None }

                | _ -> model

            // ---- vertex editing (Interactions.EditAnnotation) ----------------------------------
            // Same (draw = false, pick = true) gate as annotation selection above: both are picks.

            | GrabVertex (id, pointIndex), false, true ->
                match model.annotations.flat.TryFind id with
                | Some (Leaf.Annotations ann) when Geometry.isVertexEditable ann.geometry ->
                    match IndexList.tryAt pointIndex ann.points with
                    | Some original ->
                        { model with
                            vertexGrab =
                                Some { annotation = id; pointIndex = pointIndex
                                       original = original; movedSinceGrab = false } }
                    | None -> model
                | _ -> model

            | ArmVertexGrab, _, _ ->
                // the preview cursor produced a hit after the grab, so the next click is a drop
                match model.vertexGrab with
                | Some g when not g.movedSinceGrab ->
                    { model with vertexGrab = Some { g with movedSinceGrab = true } }
                | _ -> model

            | CancelVertexEdit, _, _ ->
                // the annotation was never touched while the grab was live, so forgetting the grab
                // is the whole of the undo
                { model with vertexGrab = None }

            | MoveVertex (id, pointIndex, position, samplePoint), false, true ->
                match model.annotations.flat.TryFind id with
                | Some (Leaf.Annotations ann) when Geometry.isVertexEditable ann.geometry ->
                    let up     = smallConfig.up.Get(bigConfig)
                    let north  = smallConfig.north.Get(bigConfig)
                    let planet = smallConfig.planet.Get(bigConfig)

                    let before = model.annotations
                    let after =
                        before
                        |> Groups.updateLeaf id (fun leaf ->
                            match leaf with
                            | Leaf.Annotations a ->
                                a
                                |> moveVertex up north planet model.samplingDistance samplePoint pointIndex position
                                |> Leaf.Annotations
                            | other -> other
                        )

                    // one atomic update, and therefore one undo entry, per drop
                    { model with annotations = after; vertexGrab = None }
                    |> pushUndo (SnapshotDelta(before, after))
                | _ ->
                    { model with vertexGrab = None }

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
        /// true only in Interactions.EditAnnotation - gates the control point handles, which are
        /// both drawn and pickable only while the user is actually editing.
        (vertexEditingAllowed : aval<bool>)
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
                    let c = UI.mkColor model.colorByCategory model.annotations a
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
            // control point index under the cursor, or -1. Read from the red channel of the same
            // pixel hoveredAnnotation comes from, so one download serves both.
            let hoveredVertex = cval -1
            // VertexGrab is not a ModelType, so adaptify leaves this a plain aval of the option
            let grabbedVertex = model.vertexGrab |> AVal.map (function Some g -> g.pointIndex | None -> -1)

            let viewMatrix = view |> AVal.map (fun v -> (CameraView.viewTrafo v).Forward)
            // one cached ordering shared by every packed draw that writes an object id, so the
            // ids the pick target reads back agree across lines, fills and handles by construction
            let ordered = PackedRendering.orderedAnnotations (annoSet |> ASet.map ((fun (g, (s,t)) -> g,s)))
            let lines, pickIds, bb = PackedRendering.linesNoIndirect model.colorByCategory config.offset hoveredAnnotation (model.annotations.selectedLeaves |> ASet.map (fun e -> e.id)) ordered viewMatrix
            let fillGeometry = PackedRendering.fills model.colorByCategory config.offset ordered viewMatrix

            // handles exist only for the single selected annotation, and only in edit mode
            let handleTarget =
                (vertexEditingAllowed, model.annotations.singleSelectLeaf)
                ||> AVal.map2 (fun editing selected -> if editing then selected else None)
            let handleGeometry = PackedRendering.vertexHandles config.offset handleTarget ordered viewMatrix

            let pickRenderTarget = PackedRendering.pickRenderTarget runtime config.pickingTolerance lines fillGeometry handleGeometry view frustum viewport
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
                                // red carries the control point index for handle fragments and -1
                                // for everything else, so this one pixel says both what was hit
                                // and whether it was a handle
                                let sub : int = floor p.R |> int
                                let ids = pickIds.GetValue()
                                if id >= 0 && id < ids.Length  && allowed then
                                    //Log.line "hoverhit %A" (id, ids.[id])
                                    transact (fun _ ->
                                        hoveredAnnotation.Value <- id
                                        hoveredVertex.Value <- sub)
                                    Seq.empty
                                else
                                    transact (fun _ ->
                                        hoveredAnnotation.Value <- -1
                                        hoveredVertex.Value <- -1)
                                    Seq.empty
                            with e -> Seq.empty
                       )
                       Sg.onMouseDown (fun b p ->
                            let id = hoveredAnnotation.GetValue()
                            let ids = pickIds.GetValue()
                            let vertex = hoveredVertex.GetValue()
                            let editing = vertexEditingAllowed.GetValue()
                            let alreadyGrabbed = grabbedVertex.GetValue() >= 0
                            if alreadyGrabbed then
                                // a grab is live: the drop belongs to the viewer's surface-click
                                // path, which is the only place the live hit point exists
                                DrawingAction.Nop
                            elif id >= 0 && id < ids.Length then
                                if vertex >= 0 && editing then
                                    Log.line "vertexhit %A" (id, ids.[id], vertex)
                                    DrawingAction.GrabVertex(ids.[id], vertex)
                                elif editing && model.annotations.singleSelectLeaf.GetValue() = Some ids.[id] then
                                    // A body click on the annotation being edited must not reach
                                    // PickDirectly: addSingleSelectedLeaf *toggles*, so clicking
                                    // the annotation you are editing would deselect it and take its
                                    // handles away - which is exactly what a slightly missed handle
                                    // click looks like.
                                    DrawingAction.Nop
                                else
                                    // a click on the body still re-selects, so you can move
                                    // between annotations without leaving edit mode
                                    Log.line "clickhit %A" (id, ids.[id])
                                    DrawingAction.PickDirectly(ids.[id])
                            else
                                DrawingAction.Nop
                       )
                ]
            let packedPoints =
                PackedRendering.points model.colorByCategory (model.annotations.selectedLeaves |> ASet.map (fun l -> l.id)) (annoSet |> ASet.map ((fun (g, (s,t)) -> g,s))) config.offset viewMatrix
                |> Sg.noEvents

            // listed before the lines so outlines draw on top of their own fill. The fill writes
            // no depth, so this ordering is all that separates them.
            //
            // NOTE: the spice trafo (t) is dropped here exactly as linesNoIndirect drops it, so
            // fill and outline stay aligned. The legacy branch below applies it to both for the
            // same reason. See pro3d-space/PRo3D#672.
            let packedFills =
                PackedRendering.packedFillRender fillGeometry
                |> Sg.noEvents

            // after the lines, so a handle is never hidden by the outline running through it -
            // matching the pick pass, where handles are drawn last for the same reason
            let packedVertexHandles =
                PackedRendering.packedVertexHandleRender hoveredVertex grabbedVertex viewport handleGeometry
                |> Sg.noEvents

            let overlay =
                Sg.ofList [
                    // brush model.hoverPosition;
                    annotations
                    Sg.ofSeq [packedFills; packedLines; packedPoints; packedVertexHandles]
                    Sg.drawWorkingAnnotation config.offset (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption model.working) // TODO v5: why need fully qualified
                    Sg.drawWorkingAnnotation config.offset (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption model.cutStroke)
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
            let viewMatrix = view |> AVal.map (fun v -> (CameraView.viewTrafo v).Forward)
            let annotations =
                annoSet
                |> ASet.map(fun (g,(a,t)) ->
                    let c = UI.mkColor model.colorByCategory model.annotations a
                    let picked = UI.isSingleSelect model.annotations a
                    let showPoints =
                        a.geometry
                        |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)

                    // This branch applies the spice trafo per annotation, unlike the packed one
                    // above which drops it - so the fill has to be built here, under the same
                    // trafo as its outline, or the two separate. See pro3d-space/PRo3D#672.
                    // Used by PRo3D.Snapshots, which turns packed rendering off.
                    let sg =
                        Sg.ofList [
                            // fill first, so the outline draws over it
                            // no pick target in this branch, so the ids are unused - but the
                            // ordering is how fills takes its input
                            PackedRendering.fills model.colorByCategory config.offset (PackedRendering.orderedAnnotations (ASet.single (g, a))) viewMatrix
                            |> PackedRendering.packedFillRender
                            |> Sg.noEvents
                            Sg.finishedAnnotationOld a c config view viewport showPoints picked pickingAllowed
                        ]
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
                    Sg.drawWorkingAnnotation config.offset (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption model.cutStroke)
                ]

            let depthTest = 
                annoSet 
                |> ASet.map(fun (_,(a,t)) -> Sg.finishedAnnotationDiscs a config model.dnsColorLegend view |> Sg.trafo t) |> Sg.set

            (overlay, depthTest)
            
