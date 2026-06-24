namespace PRo3D.Viewer

open PRo3D
open System
open System.IO
open Aardvark.Base
open Aardvark.Rendering.Text
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Chiron
open PRo3D.Base.Annotation.GeoJSON
open PRo3D.Base
open PRo3D.Core
open Aardvark.Rendering
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open PRo3D.Core.Surface
open OpcViewer.Base
open Viewer

module TraversePropertiesApp =

    let update (model : Traverse) (action : TraversePropertiesAction) : Traverse = 
        match action with
        // Name
        | SetTraverseName s ->
            { model with tName = s }
        // Text
        | ToggleShowText ->
            { model with showText = not model.showText }
        | ToggleFastText ->
            { model with fastText = not model.fastText }
        | ToggleshowRimfaxSurfaces ->
            { model with showRimfaxSurfaces = not model.showRimfaxSurfaces }
        | SetSolTextsize s ->
            { model with tTextSize = Numeric.update model.tTextSize s}
        // Line
        | ToggleShowLines ->
            { model with showLines = not model.showLines }
        | SetTraverseColor tc -> 
            { model with color = ColorPicker.update model.color tc }
        | SetLineWidth w ->
            { model with tLineWidth = Numeric.update model.tLineWidth w}
        | SetHeightOffset h -> 
            { model with heightOffset = Numeric.update model.heightOffset h}
        // Dots 
        | ToggleShowDots ->
            { model with showDots = not model.showDots }
        | SetPriority p ->
            { model with priority = Numeric.update model.priority p }
        | TogglePriorityRenderingEnabled ->             
            { model with priorityEnabled = not model.priorityEnabled }


    module UI =
    
        let viewRoverTraverseProperties (m : AdaptiveTraverse) =
            require GuiEx.semui (
                Html.table [
                    Html.row "Name:"       [text m.tName]
                    Html.row "Color:"      [ColorPicker.view m.color |> UI.map SetTraverseColor ]
                    Html.row "Linewidth:"  [Numeric.view' [NumericInputType.InputBox] m.tLineWidth |> UI.map SetLineWidth ]  
                    Html.row "Height offset:"  [Numeric.view' [NumericInputType.InputBox] m.heightOffset |> UI.map SetHeightOffset ]  
                ]
            )

        let viewRimfaxTraverseProperties (m : AdaptiveTraverse) =
            require GuiEx.semui (
                Html.table [
                    Html.row "Name:"       [text m.tName]
                    Html.row "Textsize:"   [Numeric.view' [NumericInputType.InputBox] m.tTextSize |> UI.map SetSolTextsize ]  
                    Html.row "Show Text:"  [GuiEx.iconCheckBox m.showText  ToggleShowText]
                    Html.row "Fast Text:"  [GuiEx.iconCheckBox m.fastText  ToggleFastText]
                    Html.row "Show Surfaces:"  [GuiEx.iconCheckBox m.showRimfaxSurfaces ToggleshowRimfaxSurfaces]
                    Html.row "Color:"      [ColorPicker.view m.color |> UI.map SetTraverseColor ]
                    Html.row "Linewidth:"  [Numeric.view' [NumericInputType.InputBox] m.tLineWidth |> UI.map SetLineWidth ]  
                    Html.row "Height offset:"  [Numeric.view' [NumericInputType.InputBox] m.heightOffset |> UI.map SetHeightOffset ]  
                ]
            )

        let viewWayPointsTraverseProperties (m : AdaptiveTraverse) =
            require GuiEx.semui (
                Html.table [
                    Html.row "Name:"       [text m.tName]
                    Html.row "Textsize:"   [Numeric.view' [NumericInputType.InputBox] m.tTextSize |> UI.map SetSolTextsize ]  
                    Html.row "Show Text:"  [GuiEx.iconCheckBox m.showText  ToggleShowText]
                    Html.row "Fast Text:"  [GuiEx.iconCheckBox m.fastText  ToggleFastText]
                    Html.row "Show Lines:" [GuiEx.iconCheckBox m.showLines ToggleShowLines]
                    Html.row "Show Dots:"  [GuiEx.iconCheckBox m.showDots  ToggleShowDots]
                    Html.row "Color:"      [ColorPicker.view m.color |> UI.map SetTraverseColor ]
                    Html.row "Linewidth:"  [Numeric.view' [NumericInputType.InputBox] m.tLineWidth |> UI.map SetLineWidth ]  
                    Html.row "Height offset:"  [Numeric.view' [NumericInputType.InputBox] m.heightOffset |> UI.map SetHeightOffset ]  
                    Html.row "Priority:"    [Numeric.view' [NumericInputType.InputBox] m.priority |> UI.map SetPriority ] 
                    Html.row "Use Priority"  [GuiEx.iconCheckBox m.priorityEnabled  TogglePriorityRenderingEnabled]
                ]
            )

        let viewStrategicAnnotationsTraverseProperties (m : AdaptiveTraverse) =
            require GuiEx.semui (
                Html.table [
                    Html.row "Name:"       [text m.tName]
                    Html.row "Textsize:"   [Numeric.view' [NumericInputType.InputBox] m.tTextSize |> UI.map SetSolTextsize ]  
                    Html.row "Show Text:"  [GuiEx.iconCheckBox m.showText  ToggleShowText]
                    Html.row "Fast Text:"  [GuiEx.iconCheckBox m.fastText  ToggleFastText]
                    Html.row "Color:"      [ColorPicker.view m.color |> UI.map SetTraverseColor ]
                    Html.row "Linewidth:"  [Numeric.view' [NumericInputType.InputBox] m.tLineWidth |> UI.map SetLineWidth ]  
                    Html.row "Height offset:"  [Numeric.view' [NumericInputType.InputBox] m.heightOffset |> UI.map SetHeightOffset ]  
                ]
            )

        let viewPlannedTargetsTraverseProperties (m : AdaptiveTraverse) =
            require GuiEx.semui (
                Html.table [
                    Html.row "Name:"       [text m.tName]
                    Html.row "Textsize:"   [Numeric.view' [NumericInputType.InputBox] m.tTextSize |> UI.map SetSolTextsize ]  
                    Html.row "Show Text:"  [GuiEx.iconCheckBox m.showText  ToggleShowText]
                    Html.row "Fast Text:"  [GuiEx.iconCheckBox m.fastText  ToggleFastText]
                    Html.row "Color:"      [ColorPicker.view m.color |> UI.map SetTraverseColor ]
                    Html.row "Height offset:"  [Numeric.view' [NumericInputType.InputBox] m.heightOffset |> UI.map SetHeightOffset ]  
                ]
            )
    
module TraverseApp = 

    let parseTraverse (name : string) (traverse : GeoJsonFeatureCollection) = 
        match traverse.properties with
            | Some p ->
                let (sols, traverseType, showLines, showText, showDots) =
                    match p.traverseType with
                    | "waypoints" -> WayPointsTraverseApp.parseTraverse (traverse), TraverseType.WayPoints, false, true, true
                    | "rover" -> RoverTraverseApp.parseTraverse (traverse), TraverseType.Rover, true, false, false
                    | "rimfax" -> RimfaxTraverseApp.parseTraverse (traverse), TraverseType.Rimfax, true, false, false
                    // | "plannedTargets" -> PlannedTargetsTraverseApp.parseTraverse (traverse), TraverseType.PlannedTargets
                    // | "strategicAnnotations" -> StrategicAnnotationsTraverseApp.parseTraverse (traverse), TraverseType.WayPoints
                    | t -> failwithf "Traverse file does not define a valid traverseType. Valid types are WayPoints, Rover, Rimfax, PlannedTargets and StrategicAnnotations. The given traverseType is: %s" t
                Some (sols, traverseType, showLines, showText, showDots)
            | None -> 
                match traverse.features with 
                    | [] -> 
                        Log.error "[TraverseApp] Missing properties for traverse %s; Empty feature list. Not able to load traverse. " name
                        None 
                    | h::_ -> 
                        match h.geometry with
                        | GeoJsonGeometry.Point(p, _) -> 
                            Log.warn "[TraverseApp] Missing properties for traverse %s. Fallback waypoint traverse " name
                            Some (WayPointsTraverseApp.parseTraverse (traverse), TraverseType.WayPoints, false, true, true)
                        | e -> 
                            Log.warn "[TraverseApp] Missing properties for traverse %s; Features not having waypoint geometry type. Not able to load traverse. " name
                            None


    let assignColorsToTraverse (traverses : List<string>) : List<string * C4b> =
        // this function is not in use at the moment
        if traverses.Length > 1 then
            let colors = ColorBrewer.twelveClassPaired |> List.map ColorBrewer.toMaxValue
            traverses |> ColorBrewer.assignColors colors
        else    
            traverses |> List.map(fun x -> (x, C4b.White))
            
    let update 
        (model : TraverseModel) 
        (action : TraverseAction) : TraverseModel = 
        match action with
        | LoadTraverses paths ->    
            let traversesJson =             
                paths 
                |> List.filter(fun x ->
                    let fileExists = File.Exists x
                    if not fileExists then
                        Log.warn "[Traverse] File %s does not exist." x

                    fileExists
                )
                |> List.map(fun x ->
                    Log.line "[Traverse] Loading %s" x
                    let geojson = System.IO.File.ReadAllText x
                     
                    (geojson 
                    |> Json.parse 
                    |> Json.deserialize 
                    |> parseTraverse (Path.GetFileName x),
                    Path.GetFileName x
                    )
                )
                |> List.choose (fun (parsedTraverse, name) ->
                    match parsedTraverse with
                    | Some v -> Some (v, name)
                    | None -> None)
                |> List.map(fun ((sols, traverseType, showLines, showText, showDots), name) ->
                    let color = if traverseType = TraverseType.Rover then C4b.White else C4b.Magenta

                    let traverse = 
                        Traverse.initial name sols 
                        |> Traverse.withColor color
                        |> Traverse.withTraverseType traverseType 
                        |> Traverse.withProperties showLines showText showDots
                    traverse |> HashMap.single traverse.guid 
                )
                |> List.fold(fun a b -> HashMap.union a b) HashMap.empty

            let roverTraverses = 
                traversesJson
                |> HashMap.filter(fun guid traverse ->
                    traverse.traverseType = TraverseType.Rover
                )

            let rimfaxTraverses = 
                traversesJson
                |> HashMap.filter(fun guid traverse ->
                    traverse.traverseType = TraverseType.Rimfax
                )

            let waypointsTraverses = 
                traversesJson
                |> HashMap.filter(fun guid traverse ->
                    traverse.traverseType = TraverseType.WayPoints
                )

            { model with 
                roverTraverses = model.roverTraverses |> HashMap.union roverTraverses;
                rimfaxTraverses = model.rimfaxTraverses |> HashMap.union rimfaxTraverses;
                waypointsTraverses = model.waypointsTraverses |> HashMap.union waypointsTraverses;
                selectedTraverse = None;
                selectedRimfaxSurface = None;
            }
        | IsVisibleT id ->
            let roverTraverses' =  
                model.roverTraverses 
                |> HashMap.alter id (function None -> None | Some t -> Some { t with isVisibleT = not t.isVisibleT })
            let rimfaxTraverses' =  
                model.rimfaxTraverses 
                |> HashMap.alter id (function None -> None | Some m -> Some { m with isVisibleT = not m.isVisibleT })
            let waypointsTraverses' =  
                model.waypointsTraverses 
                |> HashMap.alter id (function None -> None | Some m -> Some { m with isVisibleT = not m.isVisibleT })
            { model with
                roverTraverses = roverTraverses';
                rimfaxTraverses = rimfaxTraverses';
                waypointsTraverses = waypointsTraverses'
            }
        | IsVisibleRimfaxSurface (traverseId, solId) ->
            let rimfaxTraverses' =  
                model.rimfaxTraverses 
                |> HashMap.alter traverseId (
                    function 
                        None -> None 
                        | Some m ->
                            Some { m with sols = (m.sols 
                                |> List.map (fun sol -> 
                                        match sol.solMetrics with 
                                        | Some (SolMetrics.RimfaxM solMetrics) -> 
                                            if solId = sol.solNumber then 
                                                match solMetrics.rimfaxSurfaceProperties with
                                                | Some rimfaxSurfaceProperties -> { sol with solMetrics = Some (RimfaxM { solMetrics with rimfaxSurfaceProperties = Some { rimfaxSurfaceProperties with isVisibleS = not rimfaxSurfaceProperties.isVisibleS }})}
                                                | _ -> sol
                                            else sol
                                        | _ -> sol 
                                )
                            ) }
                    )
            { model with
                rimfaxTraverses = rimfaxTraverses';
            }
        | RemoveTraverse id -> 
            let selectedTraverse' = 
                match model.selectedTraverse with
                | Some selT -> if selT = id then None else Some selT
                | None -> None
            let roverTraverses' = HashMap.remove id model.roverTraverses
            let rimfaxTraverses' = HashMap.remove id model.rimfaxTraverses
            let waypointsTraverses' = HashMap.remove id model.waypointsTraverses
            { model with 
                roverTraverses = roverTraverses';
                rimfaxTraverses = rimfaxTraverses';
                waypointsTraverses = waypointsTraverses';
                selectedTraverse = selectedTraverse' }
        | SelectTraverse id ->
            let selT = HashMap.unionMany [model.roverTraverses; model.rimfaxTraverses; model.waypointsTraverses] |> HashMap.tryFind id
            match selT, model.selectedTraverse with
            | Some a, Some b -> 
                if a.guid = b then 
                    { model with selectedTraverse = None }
                else 
                    { model with selectedTraverse = Some a.guid }
            | Some a, None -> 
                { model with selectedTraverse = Some a.guid }
            | None, _ -> model
        | TraversePropertiesMessage msg ->  
            match model.selectedTraverse with
            | Some id -> 
                let selectedT = HashMap.unionMany [model.roverTraverses; model.rimfaxTraverses; model.waypointsTraverses] |> HashMap.tryFind id
                match selectedT with
                | Some selT ->
                    let traverse = (TraversePropertiesApp.update selT msg)
                    let roverTraverses' = model.roverTraverses |> HashMap.alter selT.guid (function | Some _ -> Some traverse | None -> None )
                    let rimfaxTraverses' = model.rimfaxTraverses |> HashMap.alter selT.guid (function | Some _ -> Some traverse | None -> None )
                    let waypointsTraverses' = model.waypointsTraverses |> HashMap.alter selT.guid (function | Some _ -> Some traverse | None -> None )
                    { model with 
                        roverTraverses = roverTraverses';
                        rimfaxTraverses = rimfaxTraverses';
                        waypointsTraverses = waypointsTraverses' }
                | None -> model
            | None -> model
        | SelectSol solNumber ->
            match model.selectedTraverse with
            | Some id -> 
                let selectedT = HashMap.unionMany [model.roverTraverses; model.rimfaxTraverses; model.waypointsTraverses] |> HashMap.tryFind id
                match selectedT with
                | Some selT ->
                    let selectedSol =
                        match solNumber, selT.selectedSol with
                        | number, None -> Some number
                        | number, Some n -> 
                            if n = number then None else Some number

                    let roverTraverses' =  
                        model.roverTraverses 
                        |> HashMap.alter id (function None -> None | Some t -> Some { t with selectedSol = selectedSol })
                    let rimfaxTraverses' =  
                        model.rimfaxTraverses 
                        |> HashMap.alter id (function None -> None | Some t -> Some { t with selectedSol = selectedSol })
                    let waypointsTraverses' =  
                        model.waypointsTraverses 
                        |> HashMap.alter id (function None -> None | Some t -> Some { t with selectedSol = selectedSol })
                    { model with 
                        roverTraverses = roverTraverses';
                        rimfaxTraverses = rimfaxTraverses';
                        waypointsTraverses = waypointsTraverses' }
                | None -> model
            | None -> model
        | RemoveAllTraverses ->
            { model with 
                roverTraverses = HashMap.empty;
                waypointsTraverses = HashMap.empty;
                rimfaxTraverses = HashMap.empty;
                selectedTraverse = None } 
        | LoadRimfaxSurface (rootDirectoy, traverseID) ->
            match rootDirectoy  with
            | [path] when path <> "" ->
                //let objPaths = Directory.GetFiles(path, "*.obj", SearchOption.AllDirectories) |> Array.filter (fun filePath -> (Path.GetDirectoryName(filePath).Contains("026") && not (Path.GetDirectoryName(filePath).Contains("1219"))  && not (Array.contains "1220" (Path.GetDirectoryName(filePath).Split(Path.DirectorySeparatorChar)))))
                let pathBelongsToSol (filePath : string) (solNumber : int)  =
                    let folders = Path.GetDirectoryName(filePath).Split(Path.DirectorySeparatorChar)
                    if folders.Length >= 2 then
                        let secondLast = folders.[folders.Length - 2]
                        match System.Int32.TryParse(secondLast) with
                        | true, parsed -> parsed = solNumber
                        | false, _ -> false
                    else
                        false

                let sols = 
                        model.rimfaxTraverses[traverseID].sols 
                        |> List.map (fun sol ->
                            match sol.solMetrics with
                            | Some (SolMetrics.RimfaxM solMetrics) ->
                                let objSurfaces =                   
                                    Directory.GetFiles(path, "*.obj", SearchOption.AllDirectories) 
                                    |> Array.filter (fun filePath -> (pathBelongsToSol filePath sol.solNumber))
                                    |> Array.toList
                                    |> List.map(fun file -> SurfaceUtils.mk SurfaceType.Mesh MeshLoaderType.Wavefront Int32.MaxValue file) 
                                let rimfaxSurfaces = SurfaceUtils.ObjectFiles.CustomWavefrontLoader.createSgObjectsWavefront (IndexList.ofList objSurfaces)
                                let rimfaxImageModeOptions =
                                    Directory.GetFiles(path, "*.obj", SearchOption.AllDirectories) 
                                    |> Array.filter (fun filePath -> (pathBelongsToSol filePath sol.solNumber))
                                    |> Array.toList
                                    |> List.map(fun file -> 
                                        let folders = Path.GetDirectoryName(file).Split(Path.DirectorySeparatorChar)
                                        folders.[folders.Length - 1]
                                        )

                                let rimfaxSurfaceProperties = 
                                    match rimfaxImageModeOptions.Length with
                                    | 0 -> None
                                    | _ ->
                                        Some { 
                                            version = RimfaxSurfaceMetrics.current
                                            rimfaxSurfaces = rimfaxSurfaces
                                            rimfaxImageModeOptions = (rimfaxImageModeOptions)
                                            rimfaxImageMode = rimfaxImageModeOptions.[0]
                                            isVisibleS = true
                                        }

                                {
                                    sol with solMetrics = Some (RimfaxM {solMetrics with rimfaxSurfaceProperties = rimfaxSurfaceProperties})
                                }
                            | _ -> sol
                    )
                
                let rimfaxTraverses' =  
                    model.rimfaxTraverses 
                        |> HashMap.alter traverseID (function None -> None | Some t -> Some { t with sols = sols; rimfaxRootDirectory = path})
                { model with
                    rimfaxTraverses = rimfaxTraverses'
                }
            | _ -> 
                Log.line "[Viewer] can only import exactly one file, given: %d" (List.length rootDirectoy)
                model   
        | SetRimfaxImageMode (rimfaxImageMode, traverseID, solID) ->
            let sols = 
                model.rimfaxTraverses[traverseID].sols
                |> List.map (fun sol ->
                    match sol.solMetrics with
                    | Some (SolMetrics.RimfaxM solMetrics) ->
                        if sol.solNumber = solID then
                            let rimfaxSurfaceProperties =
                                match solMetrics.rimfaxSurfaceProperties with 
                                | None -> None
                                | Some rimfaxSurfaceProperties -> Some {rimfaxSurfaceProperties with rimfaxImageMode = rimfaxImageMode }
                            { sol with solMetrics = Some (RimfaxM { solMetrics with rimfaxSurfaceProperties = rimfaxSurfaceProperties} ) }
                        else
                            sol
                    | _ -> sol
                )
            let rimfaxTraverses' =  
                model.rimfaxTraverses 
                    |> HashMap.alter traverseID (function None -> None | Some t -> Some { t with sols = sols})
            { model with
                rimfaxTraverses = rimfaxTraverses'
            }
        | PickRimfaxSurface (surfaceID, traverseID, solNumber) ->
            let rimfaxTraverses' =  
                model.rimfaxTraverses 
                    |> HashMap.alter traverseID (function None -> None | Some t -> Some { t with selectedSol = Some solNumber})
            { model with
                selectedRimfaxSurface = Some surfaceID
                selectedTraverse = Some traverseID
                rimfaxTraverses = rimfaxTraverses'
            }
        |_-> model

    module UI =

        let viewActions (model:AdaptiveTraverseModel) =
            adaptive {
                return Html.table [                            
                    div [clazz "ui buttons inverted"] [
                        onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (
                            button [clazz "ui icon button"; onMouseClick (fun _ -> RemoveAllTraverses)] [
                                i [clazz "remove icon red"] [] ] |> UI.wrapToolTip DataPosition.Right "Remove All"
                        )
                    ] 
                ] 
            }

        let viewProperties (model:AdaptiveTraverseModel) =
            adaptive {
                let! guid = model.selectedTraverse
                let empty = div [ style "font-style:italic"] [ text "no traverse selected" ] |> UI.map TraversePropertiesMessage 
                
                match guid with
                | Some id -> 
                    let! traverse = AMap.union (AMap.union model.roverTraverses model.rimfaxTraverses) model.waypointsTraverses |> AMap.tryFind id
                    match traverse with
                    | Some t -> 
                        match t.traverseType with
                        | TraverseType.Rover -> return (TraversePropertiesApp.UI.viewRoverTraverseProperties t |> UI.map TraversePropertiesMessage)
                        | TraverseType.Rimfax -> return (TraversePropertiesApp.UI.viewRimfaxTraverseProperties t |> UI.map TraversePropertiesMessage)
                        | TraverseType.WayPoints -> return (TraversePropertiesApp.UI.viewWayPointsTraverseProperties t |> UI.map TraversePropertiesMessage)
                        //| TraverseType.StrategicAnnotations -> return (TraversePropertiesApp.UI.viewStrategicAnnotationsTraverseProperties t |> UI.map TraversePropertiesMessage)
                        //| TraverseType.PlannedTargets -> return (TraversePropertiesApp.UI.viewPlannedTargetsTraverseProperties t |> UI.map TraversePropertiesMessage)
                    | None -> return empty
                | None -> return empty
            }  
            
        let viewSols (refSystem : AdaptiveReferenceSystem) (model:AdaptiveTraverseModel) =
            adaptive {
                let! guid = model.selectedTraverse
                let empty = div [ style "font-style:italic"] [ text "no traverse selected" ] |> UI.map TraversePropertiesMessage 
                match guid with
                | Some id -> 
                    let! traverse = AMap.union (AMap.union model.roverTraverses model.rimfaxTraverses) model.waypointsTraverses |> AMap.tryFind id
                    match traverse with
                    | Some t ->
                        match t.traverseType with
                        | TraverseType.Rover -> return RoverTraverseApp.UI.viewSolList refSystem model.roverTraverses t
                        | TraverseType.Rimfax -> return RimfaxTraverseApp.UI.viewSolList refSystem t
                        | TraverseType.WayPoints -> return WayPointsTraverseApp.UI.viewSolList refSystem t
                        //| TraverseType.StrategicAnnotations -> return StrategicAnnotationsTraverseApp.UI.viewSolList refSystem t
                        //| TraverseType.PlannedTargets -> return PlannedTargetsTraverseApp.UI.viewSolList refSystem model.rimfaxTraverses t
                    | None -> 
                        let! traverse = model.rimfaxTraverses |> AMap.tryFind id
                        match traverse with
                        | Some t ->
                            let ui = (WayPointsTraverseApp.UI.viewSolList refSystem t )
                            return ui
                        | None -> return empty
                | None -> return empty
            }                
       
    module Sg =

        let drawSolLine (model: AdaptiveTraverse) (segment: V3d list) : ISg<TraverseAction> =
            adaptive {
                let! c = model.color.c
                let! w = model.tLineWidth.value
                return 
                    segment
                    |> List.toArray
                    |> PRo3D.Core.Drawing.Sg.lines c w
            }
            |> Sg.dynamic
            |> Sg.onOff model.showLines
            |> Sg.onOff model.isVisibleT

        let drawSolLines (model: AdaptiveTraverse) : ISg<TraverseAction> =
            adaptive {
                let! sols = model.sols
                match model.traverseType with
                | TraverseType.Rimfax ->
                    let segments = sols |> List.map (fun x -> x.location)

                    let segmentSgs =
                        segments
                        |> List.map (drawSolLine model)

                    return Sg.ofList segmentSgs
                | TraverseType.Rover
                | TraverseType.WayPoints ->
                    let! sols = model.sols
                    let! c = model.color.c
                    let! w = model.tLineWidth.value
                    let lines = 
                        sols 
                        |> List.map(fun x -> x.location)
                        |> List.collect id
                        |> List.toArray
                        |> PRo3D.Core.Drawing.Sg.lines c w 
                
                    return lines
            }
            |> Sg.dynamic
            |> Sg.onOff model.showLines
            |> Sg.onOff model.isVisibleT

        let getTraverseOffsetTransform (refSystem : AdaptiveReferenceSystem) (model : AdaptiveTraverse) =
            (refSystem.Current, model.Current, model.heightOffset.value) |||> AVal.map3 (fun refSystem current offset ->
                match current.sols |> List.tryHead with
                | None -> Trafo3d.Identity
                | Some sol -> 
                    let north, up, east = PRo3D.Core.Surface.TransformationApp.getNorthAndUpFromPivot sol.location[0] refSystem
                    Trafo3d.Translation(offset * up)
            )

        let viewLines (refSystem: AdaptiveReferenceSystem) (traverseModel : AdaptiveTraverseModel) =
            let traverses = AMap.union (AMap.union traverseModel.roverTraverses traverseModel.rimfaxTraverses) traverseModel.waypointsTraverses
            traverses 
            |> AMap.map( fun id traverse ->
                drawSolLines traverse
                |> Sg.trafo (getTraverseOffsetTransform refSystem traverse)
            )
            |> AMap.toASet 
            |> ASet.map snd 
            |> Sg.set
            
        let drawSolTextsFast (view : aval<CameraView>) (horizontalFovInDegrees : aval<float>) (near : aval<float>) (traverse : AdaptiveTraverse) = 
            let contents = 
                let viewTrafo = view |> AVal.map CameraView.viewTrafo
                 
                AVal.custom (fun token -> 
                    let sols = traverse.sols.GetValue(token)
                    let view = view.GetValue(token)
                    let size = traverse.tTextSize.value.GetValue(token)
                    let hfov = horizontalFovInDegrees.GetValue(token)
                    sols 
                    |> Seq.toArray
                    |> Array.map (fun sol -> 
                        let scaleTrafo = 
                            let screenSpaceScaling = true
                            if screenSpaceScaling then
                                let distance = Vec.distance sol.location[0] view.Location
                                let scaling = size * 2.0 * distance * Math.Tan(Conversion.RadiansFromDegrees hfov)
                                Trafo3d.Scale(scaling)
                            else
                                Trafo3d.Scale(size) 
                                
                        let loc = sol.location[0] + sol.location[0].Normalized * 1.5
                        let trafo = scaleTrafo * (Trafo3d.Translation loc)

                        let text = $"{sol.solNumber}"
                        //let scaleTrafo = Sg.invariantScaleTrafo view near ~~loc traverse.tTextSize.value ~~60.0
                        //let dynamicTrafo = scaleTrafo |> AVal.map (fun scale -> scale * trafo)
                        let stableTrafo = viewTrafo |> AVal.map (fun view -> trafo * view) // stable, and a bit slow
                        AVal.constant trafo, AVal.constant text
                    )
                )
                |> ASet.ofAVal
            let sg = 
                let config = { Text.TextConfig.Default with renderStyle = RenderStyle.Billboard; color = C4b.White }
                Sg.textsWithConfig config contents
                |> Sg.noEvents
                |> Sg.onOff ((traverse.isVisibleT, traverse.showText) ||> AVal.map2 (&&))
                //|> Sg.viewTrafo' Trafo3d.Identity
            sg 

        let drawSolText view near (model : AdaptiveTraverse) =
            alist {
                let! sols = model.sols
                let! showText = model.showText
     
                if showText then
                    for sol in sols do
                        let loc = ~~(sol.location[0] + sol.location[0].Normalized * 1.5)
                        let trafo = loc |> AVal.map Trafo3d.Translation
                        
                        yield Sg.text view near (AVal.constant 60.0) loc trafo model.tTextSize.value  (~~sol.solNumber.ToString()) (AVal.constant C4b.White)
            } 
            |> ASet.ofAList 
            |> Sg.set
            |> Sg.onOff model.isVisibleT


        let viewTextForTraverse (refSystem : AdaptiveReferenceSystem)
                                (view : aval<CameraView>) (horiztonalFieldOfViewInDegrees : aval<float>)
                                (near : aval<float>) (traverse : AdaptiveTraverse)  =
                traverse.fastText
                |> AVal.map (fun fast ->
                    if fast then
                        // batched billboards: fast, but jitters at planet scale
                        drawSolTextsFast view horiztonalFieldOfViewInDegrees near traverse
                    else
                        // per-label stable-trafo text: slower, numerically stable
                        drawSolText view near traverse)
                |> Sg.dynamic
                |> Sg.trafo (getTraverseOffsetTransform refSystem traverse)

        [<Obsolete("draw with sg.view")>]
        let viewText (refSystem : AdaptiveReferenceSystem) (view : aval<CameraView>) (horiztonalFieldOfViewInDegrees : aval<float>) 
                    (near : aval<float>) (traverseModel : AdaptiveTraverseModel) =
        
            let traverses = AMap.union (AMap.union traverseModel.roverTraverses traverseModel.rimfaxTraverses) traverseModel.waypointsTraverses
            traverses 
            |> AMap.map(fun id traverse ->
                viewTextForTraverse refSystem view horiztonalFieldOfViewInDegrees near traverse
            )
            |> AMap.toASet 
            |> ASet.map snd 
            |> Sg.set


        let viewCoordinateCross 
            (refSystem : AdaptiveReferenceSystem) 
            (trafo : aval<Trafo3d>) =
            
            let up = refSystem.up.value
            let north = refSystem.northO
            let east = AVal.map2(Vec.cross) up north

            [
                Sg.drawSingleLine ~~V3d.Zero up    ~~C4b.Blue  ~~2.0 trafo
                Sg.drawSingleLine ~~V3d.Zero north ~~C4b.Red   ~~2.0 trafo
                Sg.drawSingleLine ~~V3d.Zero east  ~~C4b.Green ~~2.0 trafo
            ] 
            |> Sg.ofList


        module Shader =
            open FShade
            open FShade.Effect

            type InstanceVertex = { [<Semantic("SolNumber")>] solNumber : int; [<Color>] c : V4f }
            type UniformScope with
                member x.SelectedSol : int = uniform?SelectedSol
                member x.SelectionColor : V4f = uniform?SelectionColor

            let selectedColor (v : InstanceVertex) =
                vertex {
                    let c = 
                        if v.solNumber = uniform.SelectedSol then
                            uniform.SelectionColor
                        else
                            v.c
                    return { v with c = c }
                }

        let viewTraverseDots (refSystem: AdaptiveReferenceSystem) (view : aval<CameraView>) (traverse : AdaptiveTraverse) =
            let shift = getTraverseOffsetTransform refSystem traverse
            let solCenterTrafo = 
                (traverse.sols, view, shift)
                |||> AVal.map3 (fun sols view shift -> 
                    let viewTrafo = view.ViewTrafo
                    sols |> List.toArray |> Array.map (fun sol -> Trafo3d.Translation(sol.location[0]) * shift * viewTrafo) :> Array
                )
                
            let solNumbers =
                traverse.sols 
                |> AVal.map (fun sols -> 
                    sols |> List.toArray |> Array.map (fun s -> s.solNumber) :> Array
                )

            let attributes = 
                Map.ofList [
                    ("ModelTrafo", (typeof<Trafo3d>, solCenterTrafo))
                    ("SolNumber", (typeof<int>, solNumbers))
                ]
            Sg.sphere 4 traverse.color.c ~~0.3
            |> Sg.shader {
                do! DefaultSurfaces.trafo // stable via modelTrafo = model view track trick
                do! Shader.selectedColor
            }
            |> Sg.viewTrafo' Trafo3d.Identity // modelTrafo = model view track trick
            |> Sg.uniform "SelectionColor" ~~C4b.VRVisGreen
            |> Sg.uniform "SelectedSol" (traverse.selectedSol |> AVal.map (Option.defaultValue (-1)))
            |> Sg.instanced' attributes
            |> Sg.noEvents
            |> Sg.onOff traverse.showDots

        let viewTraverseCoordinateFrames (view : aval<CameraView>) (refSystem : AdaptiveReferenceSystem) (traverse : AdaptiveTraverse) =
            let shift = getTraverseOffsetTransform refSystem traverse
            let solTrafosInRefSystem = 
                (traverse.sols, view, refSystem.Current)
                |||> AVal.bind3 (fun sols view refSystem -> 
                    let viewTrafo = view.ViewTrafo
                    shift |> AVal.map (fun shift -> 
                        sols |> List.toArray |> Array.map (fun sol -> 
                            let rotation =
                                if traverse.traverseType = TraverseType.Rover then
                                    RoverTraverseApp.computeSolRotation sol refSystem
                                else
                                    WayPointsTraverseApp.computeSolRotation sol refSystem
                            let loc = sol.location[0] + sol.location[0].Normalized * 0.5 // when porting to instancing kept it 0.5
                            let shiftedSol = Trafo3d.Translation loc
                            rotation * shiftedSol * shift * viewTrafo
                        ) 
                    )
                )
            Sg.coordinateCross ~~2.0
            |> Sg.shader {
                do! DefaultSurfaces.trafo // stable via modelTrafo = model view track trick
            }
            |> Sg.viewTrafo' Trafo3d.Identity // modelTrafo = model view track trick
            |> Sg.instanced solTrafosInRefSystem
            |> Sg.noEvents
            |> Sg.onOff traverse.showDots


        let viewRimfaxSurfaces
            (refSystem : AdaptiveReferenceSystem)
            (traverse : AdaptiveTraverse) 
            (traverseModel  : AdaptiveTraverseModel)
            : ISg<TraverseAction> =

            let pickable
                (bb : aval<Box3d>)
                (trafo : aval<Trafo3d>) = 
                (bb, trafo)
                ||>  AVal.map2( fun (a:Box3d) (b:Trafo3d) -> 
                    { shape = PickShape.Box (a); trafo = Trafo3d.Identity }
                ) 

            let createSg 
                (surface : SgSurface)
                (traverseId : Guid)
                (solNumber : int)=
                let isSelected = 
                    adaptive {
                        let! (selected : Option<Guid>) =  traverseModel.selectedRimfaxSurface
                        match selected with
                        | Some id -> return (id = surface.surface)
                        | None -> return false
                    }

                let colorTransformationExpr =
                    <@ fun (c : V4f) ->
                        let tolerance = 0.05f
                        let isWhite =
                            abs (c.X - 1.0f) < tolerance &&
                            abs (c.Y - 1.0f) < tolerance &&
                            abs (c.Z - 1.0f) < tolerance

                        if isWhite then
                            V4f(1.0f, 1.0f, 0.0f, c.W)
                        else
                            c
                    @>

                let sg = 
                    surface.sceneGraph 
                    |> Sg.pickable' (pickable (adaptive { return surface.globalBB }) (adaptive { return surface.trafo.previewTrafo }))
                    |> Sg.noEvents
                    |> Sg.withEvents [
                        SceneEventKind.Click, (
                            fun (sceneHit : SceneHit) -> 
                                true, Seq.ofList [PickRimfaxSurface (surface.surface, traverseId, solNumber)])
                        ] 
                    |> Sg.uniform "selected" isSelected
                    |> Sg.uniform "selectionColor" (AVal.constant (C4b (200uy,200uy,255uy,255uy)))
                    // imported RIMFAX surfaces always discard their white bands automatically
                    |> Sg.uniform "WhiteDiscardEnabled"   (AVal.constant true)
                    |> Sg.uniform "WhiteDiscardThreshold" (AVal.constant 0.9f)

                let sg = 
                    isSelected
                    |> AVal.map (fun s ->
                        sg
                        |> Sg.shader {
                            do! DefaultSurfaces.stableTrafo
                            do! DefaultSurfaces.diffuseTexture
                            do! PRo3D.Base.OPCFilter.discardWhiteBands
                            if s then do! DefaultSurfaces.transformColor colorTransformationExpr
                        }
                    )
                    |> Sg.dynamic

                sg

            let getRimfaxImageModeFromPath (filePath : string) : option<string> =
                let folders = Path.GetDirectoryName(filePath).Split(Path.DirectorySeparatorChar)
                if folders.Length >= 1 then
                    Some folders.[folders.Length - 1]
                else
                    None

            let surfaceSg =
                traverse.sols
                |> AVal.map (List.map (fun sol -> 
                    match sol.solMetrics with
                    | Some (RimfaxM solMetrics) ->
                        match solMetrics.rimfaxSurfaceProperties with
                        | Some rimfaxSurfaceProperties ->
                            rimfaxSurfaceProperties.rimfaxSurfaces
                            |> HashMap.filter(fun guid surf -> 
                                ((getRimfaxImageModeFromPath surf.sgImportPath) = Some rimfaxSurfaceProperties.rimfaxImageMode && rimfaxSurfaceProperties.isVisibleS)
                            )
                            |> HashMap.map (fun x value -> createSg value traverse.guid sol.solNumber)
                        | None -> HashMap.Empty
                    | _ -> HashMap.Empty
                ) 
                >> HashMap.unionMany
                )

            surfaceSg
            |> AMap.ofAVal
            |> ASet.ofAMap
            |> ASet.map (snd)
            |> Sg.set
            |> Sg.noEvents 

            
        let viewTraverseFast  
            (view : aval<CameraView>)
            (refSystem : AdaptiveReferenceSystem)
            (traverse : AdaptiveTraverse)
            (traverseModel  : AdaptiveTraverseModel) : ISg<TraverseAction> = 
            Sg.ofList [
                viewTraverseCoordinateFrames view refSystem traverse
                viewTraverseDots refSystem view traverse
                viewRimfaxSurfaces refSystem traverse traverseModel |> Sg.onOff traverse.showRimfaxSurfaces
            ]
            |> Sg.onOff traverse.isVisibleT


        let viewTraverse  
            (refSystem : AdaptiveReferenceSystem)
            (traverse : AdaptiveTraverse)
            (traverseType: TraverseType) : ISg<TraverseAction> = 

            alist {
                let! sols = traverse.sols
                for sol in sols do
                    let! showDots = traverse.showDots
                    if showDots then
                        let! selected = traverse.selectedSol
                        let color =
                            match selected with
                            | Some sel -> 
                                if sel = sol.solNumber then  AVal.constant(C4b.VRVisGreen) else traverse.color.c
                            | None ->
                                traverse.color.c
                        yield PRo3D.Core.Drawing.Sg.sphere' color ~~6.0 ~~sol.location[0]

                        let loc =(sol.location[0] + sol.location[0].Normalized * 0.5)
                        let locTranslation = Trafo3d.Translation(loc)
                        let! r = refSystem.Current
                        let rotation = if traverseType = TraverseType.Rover then RoverTraverseApp.computeSolRotation sol r else WayPointsTraverseApp.computeSolRotation sol r
                        yield viewCoordinateCross refSystem ~~(rotation * locTranslation)
            }        
            |> ASet.ofAList         
            |> Sg.set
            |> Sg.onOff traverse.isVisibleT
            |> Sg.trafo (getTraverseOffsetTransform refSystem traverse)


        let view
            (view           : aval<CameraView>)
            (nearPlane      : aval<float>)
            (hfovInDegrees  : aval<float>)
            (refsys         : AdaptiveReferenceSystem) 
            (traverseModel  : AdaptiveTraverseModel)
            (filterPriority : aval<Option<int>>) // if Some, only render traverses with this priority
            (surfacePriorityExists : int -> aval<bool>)
            = 
            let traverses = AMap.union (AMap.union traverseModel.roverTraverses traverseModel.rimfaxTraverses) traverseModel.waypointsTraverses

            traverses 
            |> AMap.filterA (fun k v -> 
                (filterPriority, v.priority.value, v.priorityEnabled) 
                |||> AVal.bind3 (fun filterPriority p enabled -> 
                    match filterPriority, enabled with
                    | Some priority, true-> // we have it priorities enabled and we are in a surface pass. check if this is the right prio
                        AVal.constant (int p = priority)
                    | Some _, false -> // we are in a surface pass here, but priorty rendering is not enabled => skip
                         AVal.constant false
                    | None, true -> 
                        // we are in overlay pass here.
                        // but it has priority enabled -> it was already rendered with the surfaces?
                        let surfaceExists = surfacePriorityExists (int p)
                        surfaceExists |> AVal.map not // if it does not exist, render it now.
                    | None, false ->  // we are in overlay pass here and prios are not enabled => we need to render it now.
                        AVal.constant true
                )
            )
            |> AMap.map(fun id traverse ->
                let dots = viewTraverseFast view refsys traverse traverseModel
                let lines = viewLines refsys traverseModel
                let text = viewTextForTraverse refsys view hfovInDegrees nearPlane traverse
                Sg.ofList [dots; lines; text]
            )
            |> AMap.toASet 
            |> ASet.map snd 
            |> Sg.set
