namespace PRo3D.Viewer

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

module TraversePropertiesApp =

    let update (model : Traverse) (action : TraversePropertiesAction) : Traverse = 
        match action with
        // Name
        | SetTraverseName s ->
            { model with tName = s }
        // Text
        | ToggleShowText ->
            { model with showText = not model.showText }
        | SetSolTextsize s ->
            { model with tTextSize = Numeric.update model.tTextSize s}
        // Line
        | ToggleShowLines ->
            { model with showLines = not model.showLines }
        | SetTraverseColor tc -> 
            { model with color = ColorPicker.update model.color tc }
        | SetLineWidth w ->
            { model with tLineWidth = Numeric.update model.tLineWidth w}
        | SetHeightOffset w -> 
            { model with heightOffset = Numeric.update model.heightOffset w}
        // Dots 
        | ToggleShowDots ->
            { model with showDots = not model.showDots }


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

        let viewRIMFAXTraverseProperties (m : AdaptiveTraverse) =
            require GuiEx.semui (
                Html.table [
                    Html.row "Name:"       [text m.tName]
                    Html.row "Textsize:"   [Numeric.view' [NumericInputType.InputBox] m.tTextSize |> UI.map SetSolTextsize ]  
                    Html.row "Show Text:"  [GuiEx.iconCheckBox m.showText  ToggleShowText]
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
                    Html.row "Show Lines:" [GuiEx.iconCheckBox m.showLines ToggleShowLines]
                    Html.row "Show Dots:"  [GuiEx.iconCheckBox m.showDots  ToggleShowDots]
                    Html.row "Color:"      [ColorPicker.view m.color |> UI.map SetTraverseColor ]
                    Html.row "Linewidth:"  [Numeric.view' [NumericInputType.InputBox] m.tLineWidth |> UI.map SetLineWidth ]  
                    Html.row "Height offset:"  [Numeric.view' [NumericInputType.InputBox] m.heightOffset |> UI.map SetHeightOffset ]  
                ]
            )

        let viewStrategicAnnotationsTraverseProperties (m : AdaptiveTraverse) =
            require GuiEx.semui (
                Html.table [
                    Html.row "Name:"       [text m.tName]
                    Html.row "Textsize:"   [Numeric.view' [NumericInputType.InputBox] m.tTextSize |> UI.map SetSolTextsize ]  
                    Html.row "Show Text:"  [GuiEx.iconCheckBox m.showText  ToggleShowText]
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
                    Html.row "Color:"      [ColorPicker.view m.color |> UI.map SetTraverseColor ]
                    Html.row "Height offset:"  [Numeric.view' [NumericInputType.InputBox] m.heightOffset |> UI.map SetHeightOffset ]  
                ]
            )
    
module TraverseApp = 
    
    let parseTraverse (traverse : GeoJsonTraverse) = 
        let sols =
            match traverse.traverseType with
            | "WayPoints" -> WayPointsTraverseApp.parseTraverse (traverse), TraverseType.WayPoints
            | "Rover" -> RoverTraverseApp.parseTraverse (traverse), TraverseType.Rover
            | "RIMFAX" -> RIMFAXTraverseApp.parseTraverse (traverse), TraverseType.RIMFAX
            // | "PlannedTargets" -> PlannedTargetsTraverseApp.parseTraverse (traverse), TraverseType.PlannedTargets
            // | "StrategicAnnotations" -> StrategicAnnotationsTraverseApp.parseTraverse (traverse), TraverseType.WayPoints
            | t -> failwithf "Traverse file does not define a valid traverseType. Valid types are WayPoints, Rover, RIMFAX, PlannedTargets and StrategicAnnotations. The given traverseType is: %s" t
        sols

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
                     
                    let sols, traverseType =
                        geojson 
                        |> Json.parse 
                        |> Json.deserialize 
                        |> parseTraverse

                    let name = Path.GetFileName x

                    let color = if traverseType = TraverseType.Rover then C4b.White else C4b.Magenta

                    let traverse = Traverse.initial name sols |> Traverse.withColor color |> Traverse.withTraverseType traverseType
                    traverse |> HashMap.single traverse.guid 
                )
                |> List.fold(fun a b -> HashMap.union a b) (HashMap.unionMany [model.roverTraverses; model.RIMFAXTraverses; model.waypointsTraverses])

            let roverTraverses = 
                traversesJson
                |> HashMap.filter(fun guid traverse ->
                    traverse.traverseType = TraverseType.Rover
                )

            let RIMFAXTraverses = 
                traversesJson
                |> HashMap.filter(fun guid traverse ->
                    traverse.traverseType = TraverseType.RIMFAX
                )

            let waypointsTraverses = 
                traversesJson
                |> HashMap.filter(fun guid traverse ->
                    traverse.traverseType = TraverseType.WayPoints
                )

            { model with 
                roverTraverses = model.roverTraverses |> HashMap.union roverTraverses;
                RIMFAXTraverses = model.RIMFAXTraverses |> HashMap.union RIMFAXTraverses;
                waypointsTraverses = model.waypointsTraverses |> HashMap.union waypointsTraverses;
                selectedTraverse = None }
        | IsVisibleT id ->
            let roverTraverses' =  
                model.roverTraverses 
                |> HashMap.alter id (function None -> None | Some t -> Some { t with isVisibleT = not t.isVisibleT })
            let RIMFAXTraverses' =  
                model.RIMFAXTraverses 
                |> HashMap.alter id (function None -> None | Some m -> Some { m with isVisibleT = not m.isVisibleT })
            let waypointsTraverses' =  
                model.waypointsTraverses 
                |> HashMap.alter id (function None -> None | Some m -> Some { m with isVisibleT = not m.isVisibleT })
            { model with
                roverTraverses = roverTraverses';
                RIMFAXTraverses = RIMFAXTraverses';
                waypointsTraverses = waypointsTraverses'
                }
        | RemoveTraverse id -> 
            let selectedTraverse' = 
                match model.selectedTraverse with
                | Some selT -> if selT = id then None else Some selT
                | None -> None
            let roverTraverses' = HashMap.remove id model.roverTraverses
            let RIMFAXTraverses' = HashMap.remove id model.RIMFAXTraverses
            let waypointsTraverses' = HashMap.remove id model.waypointsTraverses
            { model with 
                roverTraverses = roverTraverses';
                RIMFAXTraverses = RIMFAXTraverses';
                waypointsTraverses = waypointsTraverses';
                selectedTraverse = selectedTraverse' }
        | SelectTraverse id ->
            let selT = HashMap.unionMany [model.roverTraverses; model.RIMFAXTraverses; model.waypointsTraverses] |> HashMap.tryFind id
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
                let selectedT = HashMap.unionMany [model.roverTraverses; model.RIMFAXTraverses; model.waypointsTraverses] |> HashMap.tryFind id
                match selectedT with
                | Some selT ->
                    let traverse = (TraversePropertiesApp.update selT msg)
                    let roverTraverses' = model.roverTraverses |> HashMap.alter selT.guid (function | Some _ -> Some traverse | None -> None )
                    let RIMFAXTraverses' = model.RIMFAXTraverses |> HashMap.alter selT.guid (function | Some _ -> Some traverse | None -> None )
                    let waypointsTraverses' = model.waypointsTraverses |> HashMap.alter selT.guid (function | Some _ -> Some traverse | None -> None )
                    { model with 
                        roverTraverses = roverTraverses';
                        RIMFAXTraverses = RIMFAXTraverses';
                        waypointsTraverses = waypointsTraverses' }
                | None -> model
            | None -> model
        | SelectSol solNumber ->
            match model.selectedTraverse with
            | Some id -> 
                let selectedT = HashMap.unionMany [model.roverTraverses; model.RIMFAXTraverses; model.waypointsTraverses] |> HashMap.tryFind id
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
                    let RIMFAXTraverses' =  
                        model.RIMFAXTraverses 
                        |> HashMap.alter id (function None -> None | Some t -> Some { t with selectedSol = selectedSol })
                    let waypointsTraverses' =  
                        model.waypointsTraverses 
                        |> HashMap.alter id (function None -> None | Some t -> Some { t with selectedSol = selectedSol })
                    { model with 
                        roverTraverses = roverTraverses';
                        RIMFAXTraverses = RIMFAXTraverses';
                        waypointsTraverses = waypointsTraverses' }
                | None -> model
            | None -> model
        | RemoveAllTraverses ->
            { model with 
                roverTraverses = HashMap.empty;
                waypointsTraverses = HashMap.empty;
                RIMFAXTraverses = HashMap.empty;
                selectedTraverse = None } 
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
                    let! traverse = AMap.union (AMap.union model.roverTraverses model.RIMFAXTraverses) model.waypointsTraverses |> AMap.tryFind id
                    match traverse with
                    | Some t -> 
                        match t.traverseType with
                        | TraverseType.Rover -> return (TraversePropertiesApp.UI.viewRoverTraverseProperties t |> UI.map TraversePropertiesMessage)
                        | TraverseType.RIMFAX -> return (TraversePropertiesApp.UI.viewRIMFAXTraverseProperties t |> UI.map TraversePropertiesMessage)
                        | TraverseType.WayPoints -> return (TraversePropertiesApp.UI.viewWayPointsTraverseProperties t |> UI.map TraversePropertiesMessage)
                        | TraverseType.StrategicAnnotations -> return (TraversePropertiesApp.UI.viewStrategicAnnotationsTraverseProperties t |> UI.map TraversePropertiesMessage)
                        | TraverseType.PlannedTargets -> return (TraversePropertiesApp.UI.viewPlannedTargetsTraverseProperties t |> UI.map TraversePropertiesMessage)
                    | None -> return empty
                | None -> return empty
            }  
            
        let viewSols (refSystem : AdaptiveReferenceSystem) (model:AdaptiveTraverseModel) =
            adaptive {
                let! guid = model.selectedTraverse
                let empty = div [ style "font-style:italic"] [ text "no traverse selected" ] |> UI.map TraversePropertiesMessage 
                match guid with
                | Some id -> 
                    let! traverse = AMap.union (AMap.union model.roverTraverses model.RIMFAXTraverses) model.waypointsTraverses |> AMap.tryFind id
                    match traverse with
                    | Some t ->
                        let ui = (RoverTraverseApp.UI.viewSolList refSystem model.RIMFAXTraverses t )
                        return ui
                    | None -> 
                        let! traverse = model.RIMFAXTraverses |> AMap.tryFind id
                        match traverse with
                        | Some t ->
                            let ui = (WayPointsTraverseApp.UI.viewSolList refSystem t )
                            return ui
                        | None -> return empty
                | None -> return empty
            }                
       
    module Sg =
        let drawSolLines (model : AdaptiveTraverse) : ISg<TraverseAction> =
            adaptive {
                let! sols = model.sols
                let! c = model.color.c
                let! w = model.tLineWidth.value
                let lines = 
                    sols 
                    |> List.map(fun x -> x.location)
                    |> List.fold (fun acc sublist -> acc @ sublist) []
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
            let traverses = AMap.union (AMap.union traverseModel.roverTraverses traverseModel.RIMFAXTraverses) traverseModel.waypointsTraverses
            traverses 
            |> AMap.map( fun id traverse ->
                drawSolLines traverse
                |> Sg.trafo (getTraverseOffsetTransform refSystem traverse)
            )
            |> AMap.toASet 
            |> ASet.map snd 
            |> Sg.set
            
        let drawSolTextsFast (view : aval<CameraView>) (near : aval<float>) (traverse : AdaptiveTraverse) = 
            let contents = 
                let viewTrafo = view |> AVal.map CameraView.viewTrafo
                 
                AVal.map2 (fun sols scale -> 
                    sols 
                    |> List.toArray
                    |> Array.map (fun sol -> 
                        let loc = sol.location[0] + sol.location[0].Normalized * 1.5
                        let trafo = (Trafo3d.Scale((float)scale) ) * (Trafo3d.Translation loc)
                        let text = $"{sol.solNumber}"
                        //let scaleTrafo = Sg.invariantScaleTrafo view near ~~loc traverse.tTextSize.value ~~60.0
                        //let dynamicTrafo = scaleTrafo |> AVal.map (fun scale -> scale * trafo)
                        let stableTrafo = viewTrafo |> AVal.map (fun view -> trafo * view) // stable, and a bit slow
                        AVal.constant trafo, AVal.constant text
                    ) 
                ) traverse.sols traverse.tTextSize.value
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


        let viewText (refSystem : AdaptiveReferenceSystem) view near (traverseModel : AdaptiveTraverseModel) =
        
            let traverses = traverseModel.roverTraverses
            traverses 
            |> AMap.map(fun id traverse ->
                drawSolTextsFast
                    view
                    near
                    traverse
                |> Sg.trafo (getTraverseOffsetTransform refSystem traverse)
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

            type InstanceVertex = { [<Semantic("SolNumber")>] solNumber : int; [<Color>] c : V4d }
            type UniformScope with
                member x.SelectedSol : int = uniform?SelectedSol
                member x.SelectionColor : V4d = uniform?SelectionColor

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
            


        let viewTraverseFast  
            (view : aval<CameraView>)
            (refSystem : AdaptiveReferenceSystem) (model : AdaptiveTraverse) : ISg<TraverseAction> = 

            Sg.ofList [
                viewTraverseCoordinateFrames view refSystem model
                viewTraverseDots refSystem view model
            ]
            |> Sg.onOff model.isVisibleT

        let viewTraverse  
            (refSystem : AdaptiveReferenceSystem)
            (model : AdaptiveTraverse)
            (traverseType: TraverseType) : ISg<TraverseAction> = 

            alist {
                let! sols = model.sols
                for sol in sols do
                    let! showDots = model.showDots
                    if showDots then
                        let! selected = model.selectedSol
                        let color =
                            match selected with
                            | Some sel -> 
                                if sel = sol.solNumber then  AVal.constant(C4b.VRVisGreen) else model.color.c
                            | None ->
                                model.color.c
                        yield PRo3D.Core.Drawing.Sg.sphere' color ~~6.0 ~~sol.location[0]

                        let loc =(sol.location[0] + sol.location[0].Normalized * 0.5)
                        let locTranslation = Trafo3d.Translation(loc)
                        let! r = refSystem.Current
                        let rotation = if traverseType = TraverseType.Rover then RoverTraverseApp.computeSolRotation sol r else WayPointsTraverseApp.computeSolRotation sol r
                        yield viewCoordinateCross refSystem ~~(rotation * locTranslation)
            }        
            |> ASet.ofAList         
            |> Sg.set
            |> Sg.onOff model.isVisibleT
            |> Sg.trafo (getTraverseOffsetTransform refSystem model)


        let view
            (view           : aval<CameraView>)
            (refsys         : AdaptiveReferenceSystem) 
            (traverseModel  : AdaptiveTraverseModel) =

            let traverses = traverseModel.roverTraverses
            traverses 
            |> AMap.map(fun id traverse ->
                viewTraverseFast view refsys traverse
            )
            |> AMap.toASet 
            |> ASet.map snd 
            |> Sg.set
