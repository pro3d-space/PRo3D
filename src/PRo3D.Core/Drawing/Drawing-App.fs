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

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators


open Aardvark.Base.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering.Text
open Aardvark.VRVis
open Aardvark.VRVis.Opc

open Aardvark.UI
open Aardvark.UI.Primitives    

open PRo3D

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

open Chiron

module DrawingApp =

   // open Newtonsoft.Json
        
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
        | _ -> { a with points = a.points |> IndexList.add firstP }

    let getFinishedAnnotation up north planet (view:CameraView) (model : DrawingModel) =
        match model.working with
        | Some w ->  
            let w = 
                match w.geometry with
                | Geometry.Polygon -> closePolyline w
                | _-> w 
        
            let dns = 
                w.points 
                |> DipAndStrike.calculateDipAndStrikeResults (up) (north)
                //match w.points.Count with 
                //    | x when x > 2 ->
                //        //let up = 
                //        //let north = up.Cross(V3d.OOI.Cross(up))
                //        let result = w.points |> DipAndStrike.calculateDipAndStrikeResults (up) (north)
                //        Some result //more acc. by using segments as well?
                //    | _ -> None 

            let w =
                match w.geometry with 
                | Geometry.TT -> { w with manualDipAngle = { w.manualDipAngle with value = 0.0 }}
                | _ -> w

            let results = Calculations.calculateAnnotationResults w up north planet

            Some { w with dnsResults = dns ; results = Some results; view = view }
        | None -> None

    let finishAndAppendAndSend up north planet (view:CameraView) (model : DrawingModel) (bc : BlockingCollection<string>) = 
      
        let groups = 
            match getFinishedAnnotation up north planet view model with
            | Some a -> 
                //let json = a |> JsonTypes.ofAnnotation |> Aardvark.UI.Pickler.jsonToString                 
                //bc.Add json
                model.annotations |> GroupsApp.addLeafToActiveGroup (Leaf.Annotations a) true
            | None -> 
                model.annotations
        
        { model with  working = None; pendingIntersections = ThreadPool.empty; annotations = groups }
    
    //adds new point to working state, if certain conditions are met the annotation finishes itself
    // returns current segment for async computations outside
    let addPoint up north planet samplePoint (p : V3d) view model surfaceName bc =
      
        let working, newSegment = 
            match model.working with
            | Some w ->     
                let annotation = { w with points = w.points |> IndexList.add p }
                Log.line "working contains %d points" annotation.points.Count
                
                //fetch current drawing segment (projected, polyline or polygon)
                let result = 
                    match w.projection with
                    | Projection.Viewpoint | Projection.Sky ->                     
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
                                let step = vec.Length / float PRo3D.Config.sampleCount
                                let points = [ 
                                    for s in 0 .. PRo3D.Config.sampleCount do
                                        let p = newSegment.startPoint + dir * (float s) * step // world space
                                        match samplePoint p with
                                        | None -> ()
                                        | Some projectedPoint -> yield projectedPoint
                                ]
                                let newSegment = { startPoint = a; endPoint = p; points = IndexList.ofList points }
                                { annotation with segments = IndexList.add newSegment annotation.segments }, None
                    | Projection.Linear ->
                        annotation, None
                    | _ -> failwith "case does not exist"            
                result 
            | None ->  //no working state, start new working annotation
                { 
                    //annotation states should be immutable after creation
                    //(Annotation.make model.projection model.geometry model.semantic surfaceName)  
                    //    with points = IndexList.ofList [p]; modelTrafo = Trafo3d.Translation p
                    (Annotation.make model.projection model.geometry model.color model.thickness surfaceName)
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
            finishAndAppendAndSend up north planet view model bc, None
        | Geometry.TT, 2 | Geometry.Line, 2 -> 
            finishAndAppendAndSend up north planet view model bc, None
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

    let stash (model : DrawingModel) =
        { model with past = Some model; future = None }

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
                r.HitsCylinder(x, 0.0, 100.0, &hit2))
            |> Option.map(fun x ->
                let hitPoint = hit2.Point
                let p = Plane3d(x.Axis.Direction, hitPoint)
                let mutable projPoint = V3d.NaN
                p.IntersectsLine(x.Axis.P0,x.Axis.P1, Double.Epsilon, &projPoint) |> ignore

                (ann, projPoint))
        | _ -> None

    let update<'a> 
        (bigConfig   : 'a) 
        (smallConfig : SmallConfig<'a> ) 
        (webSocket   : BlockingCollection<string>) 
        (view        : CameraView) 
        (shiftFlag   : bool)
        (model       : DrawingModel) 
        (act         : DrawingAction) =

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
        | AddPointAdv (point, hitFunction, name), true, false ->
            let up    = smallConfig.up.Get(bigConfig)
            let north = smallConfig.north.Get(bigConfig)
            let planet = smallConfig.planet.Get(bigConfig)

            let model, newSegment = addPoint up north planet hitFunction point view model name webSocket
            
            match newSegment with
            | None         -> model
            | Some segment -> addNewSegment hitFunction model segment
            |> stash
        | RemoveLastPoint, _, _ -> 
          //let annotation = { w with points = w.points |> IndexList.append p }
          // { annotation with segments = IndexList.append newSegment annotation.segments }
          
            match model.working with
            | Some w when w.points.Count > 0->
              { model with working = Some { w with points = w.points |> IndexList.removeAt (w.points.Count - 1) }}
            | Some _ -> { model with working = None }
            | None -> model
        | SetSegment(segmentIndex,segment), _, _ ->
            match model.working with
            | None -> model
            | Some w ->                         
                { model with working = Some { w with segments = IndexList.setAt segmentIndex segment w.segments } }
        | Finish, _, _ -> 
            let up     = smallConfig.up.Get(bigConfig)
            let north  = smallConfig.north.Get(bigConfig)
            let planet = smallConfig.planet.Get(bigConfig)

            (finishAndAppendAndSend up north planet view model webSocket) |> stash
        | Exit, _, _ -> 
            { model with hoverPosition = None }
        | SetSemantic mode, _, _ ->
            let model =
                match mode with
                | Semantic.GrainSize -> { model with geometry = Geometry.Line }
                | _ -> model

            {model with semantic = mode }
        | SetGeometry mode, _, _ ->
            { model with geometry = mode }
        | SetProjection mode, _, _ ->
            { model with projection = mode }                  
        | ChangeColor c, _, _ -> 
            { model with color = ColorPicker.update model.color c }
        | ChangeThickness th, _, _ ->
            { model with thickness = Numeric.update model.thickness th }
        | SetExportPath s, _, _ ->
            { model with exportPath = Some s }
        | Export, _, _ ->
            //let path = Path.combine([model.exportPath; "drawing.json"])
            //printf "Writing %i annotations to %s" (model.annotations |> IndexList.count) path
            //let json = model.annotations |> IndexList.map JsonTypes.ofAnnotation |> JsonConvert.SerializeObject
            //Serialization.writeToFile path json 
            failwith "export not implemented"
            model
        | Send, _, _ ->                                                      
            model
        | ClearWorking,_ , _->
            { model with working = None }
        | DrawingAction.Clear,_ , _->
            { model with annotations = GroupsModel.initial }
        | DrawingAction.Nop, _, _ -> model                   
        | Undo, _, _ -> 
            match model.past with
            | Some p -> { p with future = Some model }
            | None -> model
        | Redo, _, _ ->
            match model.future with
            | Some f -> f
            | None -> model           
        | DrawingAction.GroupsMessage msg,_, _ ->
            let m = { model with annotations = GroupsApp.update model.annotations msg}
            m
        | DnsColorLegendMessage msg,_, _ -> 
            { model with dnsColorLegend = FalseColorLegendApp.update model.dnsColorLegend msg }
        | FlyToAnnotation msg, _, _ ->               
            model        
        | PickAnnotation (_hit, id), false, true ->
            match (model.annotations.flat.TryFind id) with
            | Some (Leaf.Annotations ann) ->       
                            
                //Log.error "[DrawingApp] shiftflag is %A" shiftFlag
                // { model with annotations = Groups.addSingleSelectedLeaf model.annotations list.Empty ann.key "" }              
                let annotations =
                    if shiftFlag then
                        Log.line "[DrawingApp] single select"
                        GroupsApp.update model.annotations (GroupsAppAction.AddLeafToSelection(List.empty, ann.key, String.Empty))
                    else
                        Log.line "[DrawingApp] multi select"
                        GroupsApp.update model.annotations (GroupsAppAction.SingleSelectLeaf(List.empty, ann.key, String.Empty))
                    
                { model with annotations = annotations }

            | _ -> model        
        
        | AddAnnotations path, _,_ ->
            match path |> List.tryHead with
            | Some p -> 
                let annos = DrawingUtilities.IO.loadAnnotations p
                Log.line "[Drawing] Merging annotations"                
                let merged = GroupsApp.union model.annotations annos.annotations
                { model with annotations = merged }
            | None ->
                model
        | ExportAsAnnotations path, _, _ ->
            Drawing.IO.saveVersioned model path
        | ExportAsCsv p, _, _ ->           
            let lookups = GroupsApp.updateGroupsLookup model.annotations
            let annotations =
                model.annotations.flat
                |> Leaf.toAnnotations
                |> HashMap.toList 
                |> List.map snd
                |> List.filter(fun a -> a.visible)
                |> List.map (Csv.exportAnnotation lookups)
            
            let csvTable = Csv.Seq.csv "," true id annotations
            if p.IsEmptyOrNull() |> not then Csv.Seq.write (p) csvTable
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
                                    
    let threads (m : DrawingModel) = m.pendingIntersections
    
    let tryToAnnotation : AdaptiveLeafCase -> Option<AdaptiveAnnotation> = 
        function
        | AdaptiveAnnotations ann -> Some ann
        | _ -> None
       
    let viewAnnotationToolsHorizontal (model:AdaptiveDrawingModel) =
        Html.Layout.horizontal [
            Html.Layout.boxH [ i [clazz "large Write icon"][] ]
            Html.Layout.boxH [ Html.SemUi.dropDown model.geometry SetGeometry ]
            Html.Layout.boxH [ Html.SemUi.dropDown model.projection SetProjection ]
            Html.Layout.boxH [ ColorPicker.view model.color |> UI.map ChangeColor; div[][] ]
            Html.Layout.boxH [ Numeric.view' [InputBox] model.thickness |> UI.map ChangeThickness ]
        //  Html.Layout.boxH [ Html.SemUi.dropDown model.semantic SetSemantic ]                
        ]

    let view<'ma> 
        (mbigConfig       : 'ma)
        (msmallConfig     : MSmallConfig<'ma>)
        (view             : aval<CameraView>)
        (pickingAllowed   : aval<bool>)        
        (model            : AdaptiveDrawingModel)
        : ISg<DrawingAction> * ISg<DrawingAction> =
        // order is irrelevant for rendering. change list to set,
        // since set provides more degrees of freedom for the compiler           
        let annoSet = 
            model.annotations.flat 
            |> AMap.choose (fun _ y -> y |> tryToAnnotation) // TODO v5: here we collapsed some avals - check correctness
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
       
        Log.startTimed "[Drawing] creating finished annotation geometry"
        let annotations =              
            annoSet 
            |> ASet.map(fun (_,a) -> 
                let c = AnnotationGroups.mkColor model.annotations a
                let picked = AnnotationGroups.isSingleSelect model.annotations a
                let showPoints = 
                  a.geometry 
                    |> AVal.map(function | Geometry.Point | Geometry.DnS -> true | _ -> false)
                
                let sg = Sg.finishedAnnotation a c config view showPoints picked pickingAllowed
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
            |> ASet.map(fun (_,a) -> Sg.finishedAnnotationDiscs a config model.dnsColorLegend view) |> Sg.set

        (overlay, depthTest)
            
