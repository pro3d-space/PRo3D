namespace PRo3D

open System
open Aardvark.Base
open Aardvark.Application
open Aardvark.UI
    
open PRo3D
open PRo3D.Base
open PRo3D.Groups
open Chiron
open Chiron.Mapping

open PRo3D.Base
open PRo3D.Base.Annotation

open FSharp.Data.Adaptive

module DrawingUtilities =
      
    let semantics = ["unit boundary"; "bed set boundary"; "x beds"] |> List.map(fun x -> x.ToLower())

    open Aardvark.Application
    open Aardvark.UI
    open System
    
    open PRo3D
    
     //(semanticsModel : CorrelationDrawing.SemanticTypes.SemanticsModel)

    module CorrelationHelpers =        

        let tryReadGroupMappingsFile() : option<HashMap<string,SemanticId>> =
            if System.IO.File.Exists "groupmappings" then
                let lines = 
                    File.readAllLines "groupmappings"
                    |> List.ofArray
                    |> List.map(fun x -> 
                        let line = x |> String.split '='
                        (line.[0], (line.[1] |> String.split ';'))
                    )

                [
                    
                    for (sem, groups) in lines do
                        for g in groups do
                            yield (g.ToLower(), sem |> SemanticId)
                ]
                |> HashMap.ofList |> Some
            else
                Log.error "[Correlations] Can't find groupmappings file"
                None
            
        let performMapping (mappings : HashMap<string,SemanticId>)(groupName : string) =
            
            match mappings |> HashMap.tryFind (groupName.ToLower()) with
            | Some sem ->                
                match sem with 
                | SemanticId "Horizon0" -> sem, SemanticType.Hierarchical
                | SemanticId "Horizon1" -> sem, SemanticType.Hierarchical
                | SemanticId "Horizon2" -> sem, SemanticType.Hierarchical
                | SemanticId "Horizon3" -> sem, SemanticType.Hierarchical
                | SemanticId "Horizon4" -> sem, SemanticType.Hierarchical
                | SemanticId "Horizon5" -> sem, SemanticType.Hierarchical
                | SemanticId "Crossbed" -> sem, SemanticType.Angular                    
                | _ -> sem |> Fail.with1 "[Correlations] unknown semantic %A"
            | None -> 
                (SemanticId "Crossbed"), SemanticType.Undefined
                //groupName |> Fail.with1 "[Correlations] unknown groupName %A"

        let inferSemanticsFromGrouping (annotationGroups : GroupsModel) : GroupsModel = 
            
            let groups = 
                annotationGroups.rootGroup 
                |> Group.flatNodes
            
            let annosFlat =
                annotationGroups.flat 
                |> HashMap.map(fun _ x -> Leaf.toAnnotation x)

            //pairing annotation ids and groupnames
            let annoGroupPairs =
                groups                  
                |> List.map(fun x -> x.name, x.leaves |> IndexList.toList)
                |> List.map(fun (groupName, leaves) ->
                    leaves |> List.map(fun l -> l,groupName)
                )
                |> List.concat

            let mappings = tryReadGroupMappingsFile()
            match mappings with 
            | Some mapps ->

                let annotations =
                    annoGroupPairs                
                    |> List.choose(fun (annoId, groupName) ->
                        let semId, semType = 
                            groupName 
                            |> performMapping mapps
                        
                        Log.line "[Correlation] mapping %A -> %A %A" groupName semId semType
                                                
                        let anno = 
                            annosFlat 
                            |> HashMap.tryFind annoId

                        anno |> Option.map(fun a -> a, semId, semType)
                    )
                    |> List.map(fun (anno, semId, semType) -> 
                        { anno with semanticId = semId; semanticType = semType }
                        |> Leaf.Annotations 
                        |> HashMap.single anno.key                    
                    ) 
                    |> List.fold(fun a b -> HashMap.union a b) annotationGroups.flat
    
                { annotationGroups with flat = annotations}
            | None -> 
                Log.error "[Correlations] unable to retrieve group mappings"
                annotationGroups


    module Calculations =
        //let getHeightDelta (p:V3d) (upVec:V3d) = (p * upVec).Length

        let getHeightDelta2 (p:V3d) (upVec:V3d) (planet:Planet) = 
            CooTransformation.getHeight p upVec planet

        let calcResultsPoint (model:Annotation) (upVec:V3d) (planet:Planet) : AnnotationResults =
            //let p = point //trafo.Forward.TransformPos(point)
            { 
                version = AnnotationResults.current
                height = 0.0
                heightDelta = 0.0
                avgAltitude = CooTransformation.getAltitude model.points.[0] upVec planet
                length = 0.0
                wayLength = 0.0
                bearing = 0.0
                slope = 0.0
            }
        

        let getDistance (points:list<V3d>) = 
          points
            |> List.pairwise
            |> List.map (fun (a,b) -> Vec.Distance(a,b))
            |> List.sum

        let getSegmentDistance (s:Segment) = 
          getDistance
            [ 
              yield s.startPoint
              for p in s.points do
                 yield p
              yield s.endPoint 
            ] 
   
        let computeWayLength (segments:IndexList<Segment>) = 
          [ for s in segments do
               yield getSegmentDistance s
          ] |> List.sum
                                                               

        let calcResultsLine (model:Annotation) (upVec:V3d) (northVec:V3d) (planet:Planet) : AnnotationResults =
            let count = model.points.Count
            let dist = Vec.Distance(model.points.[0], model.points.[count-1])
            let wayLength =
                if model.segments.IsEmpty then
                    computeWayLength model.segments
                else
                    dist

            let heights = 
                model.points 
                // |> IndexList.map(fun x -> model.modelTrafo.Forward.TransformPos(x))
                |> IndexList.map(fun p -> getHeightDelta2 p upVec planet ) 
                |> IndexList.toList

            let hcount = heights.Length

            let line = new Line3d(model.points.[0], model.points.[count-1])
            let bearing = DipAndStrike.bearing upVec northVec line.Direction.Normalized
            let slope = DipAndStrike.pitch upVec line.Direction.Normalized

            {   
                version     = AnnotationResults.current
                height      = (heights |> List.max) - (heights |> List.min)
                heightDelta = Fun.Abs (heights.[hcount-1] - heights.[0])
                avgAltitude = (heights |> List.average)
                length      = dist
                wayLength   = wayLength
                bearing     = bearing
                slope       = slope
            }

        let calculateAnnotationResults (model:Annotation) (upVec:V3d) (northVec:V3d) (planet:Planet) : AnnotationResults =
            match model.points.Count with
                | x when x > 1 -> calcResultsLine model upVec northVec planet
                | _ -> calcResultsPoint model upVec planet

        let recalcBearing (model:Annotation) (upVec:V3d) (northVec:V3d) = 
            match model.results with 
            | Some r ->
                let count = model.points.Count
                match count with
                | x when x > 1 ->
                    let line = new Line3d(model.points.[0], model.points.[count-1])
                    let bearing = DipAndStrike.bearing upVec northVec line.Direction.Normalized
                    Some {r with bearing = bearing }
                | _ -> Some r
            | None -> None

    module IO =         
        
        type AnnotationsPaths =
            {
               groups    : string
               versioned : string
               depr      : string
            }
                                           
        module AnnotationsPaths =
            let create(annotationPath : string) =
                {
                    groups    = annotationPath
                    versioned = annotationPath |> Serialization.changeExtension ".ann.json"
                    depr      = annotationPath |> Serialization.changeExtension ".ann_old" 
                }
        
        //TODO refactor to types
        let loadAnnotations annotationPath =             
            
            let (annotations : Annotations) = 
                annotationPath
                |> Serialization.readFromFile
                |> Json.parse 
                |> Json.deserialize                                    
            
            let annotations =
                { annotations with 
                    annotations = { 
                        annotations.annotations with 
                            rootGroup = 
                                annotations.annotations.rootGroup 
                                |> GroupsApp.repairGroupNodesGuid
                    }
                }

            let annos = annotations.annotations
            let flat  = annos.flat

            Log.line "[DrawingIO] loaded %d annotations" flat.Count           
            
            //detect floating leaves and add to root group
            let flatGroupIds = annos.rootGroup |> Group.flatten
            let floatingLeaves = 
                flat
                |> HashMap.values
                |> Seq.toList
                |> List.filter(fun x -> (HashSet.contains x.id flatGroupIds) |> not)
                        
            Log.error "[ViewerIO] found %d floating leaves" (floatingLeaves.Length)
            
            let annoModel = 
                annos
                |> GroupsApp.addLeaves List.empty (floatingLeaves |> IndexList.ofList)
                |> CorrelationHelpers.inferSemanticsFromGrouping 
                    
            { annotations with annotations = annoModel} 
      
