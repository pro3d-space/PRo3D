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
        
module Double =
    let parse x =
        Double.Parse(x, Globalization.CultureInfo.InvariantCulture)

module M20 =
    let parseDouble x =
        match x with
        | Json.String p ->
            p |> Double.parse  
        | Json.Number p ->
            double p
        | _ -> 
            0.0

    let parseInt x =
        match x with
        | Json.String p ->
            p |> int
        | Json.Number p ->
            int p
        | _ -> 
            0

    let parseString x =
        match x with
        | Json.String p -> p
        | Json.Number p ->
            p.ToString() 
        | _ -> 
            ""

module TraverseUtilities =
    type TraverseParseError =
        | PropertyNotFound         of propertyName : string * feature : GeoJsonFeature
        | PropertyHasWrongType     of propertyName : string * feature : GeoJsonFeature * expected : string * got : string * str : string
        | GeometryTypeNotSupported of geometryType : string
    
    let (.|) (point : GeoJsonFeature) (propertyName : string) : Result<Json, TraverseParseError> =
        match point.properties |> Map.tryFind propertyName with
        | None -> PropertyNotFound(propertyName, point) |> error
        | Some v -> v |> Ok

    // the parsing functions are a bit verbose but focus on good error reporting....
    let parseIntProperty (feature : GeoJsonFeature) (propertyName : string) : Result<int, TraverseParseError> =
        result {
            let json = feature.|propertyName
            match json with
            | Result.Ok(Json.String p) -> 
                return! 
                    Result.Int.tryParse p 
                    |> Result.mapError (fun _ -> 
                        PropertyHasWrongType(propertyName, feature, "int", "string which could not be parsed to an int.", sprintf "%A" json)
                    )
            | Result.Ok(Json.Number n) -> 
                if ((float n) % 1.0) = 0 then  // here we might have gotten a double, instead of inplicity truncating, we report is an error
                    return int n
                else
                    return! 
                        PropertyHasWrongType(propertyName, feature, "int", "decimal which was not an integer.", sprintf "%A" n)
                        |> error
            | Result.Ok(e) -> 
                return! 
                    error (
                        PropertyHasWrongType(propertyName, feature, "Json.Number", e.ToString(), sprintf "%A" json)
                    )
            | Result.Error e -> 
                return! Result.Error e
        }
    
    let parseDoubleProperty (feature : GeoJsonFeature) (propertyName : string) : Result<float, TraverseParseError> =
        result {
            let json = feature.|propertyName 
            match json with
            | Result.Ok(Json.String p) -> 
                return! 
                    Result.Double.tryParse p 
                    |> Result.mapError (fun _ -> 
                        PropertyHasWrongType(propertyName, feature, "double", "string which could not be parsed to a double", sprintf "%A" json)
                    )
            | Result.Ok(Json.Number n) -> 
                return double n
            | Result.Ok(e) -> 
                return! 
                    error (
                        PropertyHasWrongType(propertyName, feature, "Json.Number", sprintf "%A" e, sprintf "%A" json)
                    )
            | Result.Error e -> 
                return! Result.Error e
        }

    let parseStringProperty (feature : GeoJsonFeature) (propertyName : string) =
        match feature.|propertyName with
        | Result.Ok(Json.String p) -> Result.Ok p
        | Result.Ok(e) -> Result.Error (PropertyHasWrongType(propertyName, feature, "Json.String", e.ToString(), e.ToString()))
        | Result.Error(e) -> Result.Error(e)

module MissionTraverseApp =

    open TraverseUtilities

    let computeSolRotation (sol : Sol) (referenceSystem : ReferenceSystem) : Trafo3d =
        let north = referenceSystem.northO
        let up = referenceSystem.up.value
        let east = Vec.cross up north
        
        let yawRotation    = Trafo3d.RotationInDegrees(up, -sol.yaw)
        let pitchRotation  = Trafo3d.RotationInDegrees(east, sol.pitch)
        let rollRotation   = Trafo3d.RotationInDegrees(north, sol.roll)

        yawRotation * pitchRotation * rollRotation

    let parseRIMFAXTraverse (traverse : GeoJsonFeatureCollection) =
        let parseProperties (sol : Sol) (x : GeoJsonFeature) : Result<Sol, TraverseParseError> =
            let reportErrorAndUseDefault (v : 'a) (r : Result<_,_>) =
                r |> Result.defaultValue' (fun e -> Log.warn "could not parse property: %A\n\n.Using fallback: %A" e v; v)

            result {
                let! fromRMC    = parseStringProperty x  "fromRMC" // not optional
                let! toRMC      = parseStringProperty x  "toRMC"     // not optional
                let! length     = parseDoubleProperty x  "length"   // not optional 
                let! solNumber  = parseIntProperty  x  "sol"    // not optional
                let! SCLK_START = parseDoubleProperty x  "SCLK_START"    // not optional
                let! SCLK_END   = parseDoubleProperty x  "SCLK_END"    // not optional
                                  
                return 
                    { sol with 
                        solType = Source.RIMFAX
                        solNumber = solNumber;
                        length = length;
                        fromRMC = fromRMC;
                        toRMC = toRMC; 
                        SCLK_START = SCLK_START;
                        SCLK_END = SCLK_END;
                    }
            }

        let parseCoordinate (coord: Coordinate) =
            match coord with
                | Coordinate.TwoDim y ->
                    //x ... lon
                    //y ... lat
                    let latLonAlt = 
                        V3d (
                            y.Y, 
                            360.0 - y.X,
                            0 // orti implements the correct projection
                        )
                    CooTransformation.getXYZFromLatLonAlt' latLonAlt Planet.Mars
                | Coordinate.ThreeDim y ->
                    let latLonAlt =  //y.YXZ
                        V3d (
                            y.Y,
                            360.0 - y.X,
                            y.Z
                        )
                    CooTransformation.getXYZFromLatLonAlt' latLonAlt Planet.Mars 


        let parseFeature (x : GeoJsonFeature) (coord: Coordinate) =
            result {
                let! sol = parseProperties { Sol.initial with version = Sol.current; location = parseCoordinate coord; } x

                // either choose dist_total_m or dist_total - or default to zero if nothing? @ThomasOrnter - is this defaulting correct or an error?
                match parseDoubleProperty x "dist_total_m", parseDoubleProperty x "dist_total" with
                | Result.Ok dist, _ | _, Result.Ok  dist -> 
                    return { sol with totalDistanceM = dist }
                | _ ->
                    return { sol with totalDistanceM = 0.0 }
            }

        let sols = 
            match traverse.features[0].geometry with
            | GeoJsonGeometry.LineString coordinates ->
                coordinates 
                |> List.mapi (fun idx coord -> (idx, coord)) 
                |> List.choose (fun (idx, coord) ->
                        match parseFeature traverse.features[0] coord with
                        | Result.Ok r -> Some r
                        | Result.Error e -> 
                            // we skip this one in case of errors, see // see https://github.com/pro3d-space/PRo3D/issues/263
                            Report.Warn(
                                String.concat Environment.NewLine [
                                    sprintf "[Traverse] could not parse or interpret feature for coordinate %d in the coordinate list.\n" idx 
                                    sprintf "[Traverse] the detailled error is: %A" e
                                    sprintf "[Traverse] skipping coordinate %A" coord
                                ]
                            )
                            None
                    ) 
            | _ -> []

        sols

module RoverTraverseApp =

    open TraverseUtilities

    let computeSolRotation (sol : Sol) (referenceSystem : ReferenceSystem) : Trafo3d =
        let north = referenceSystem.northO
        let up = referenceSystem.up.value
        let east = Vec.cross up north
        
        let yawRotation    = Trafo3d.RotationInDegrees(up, -sol.yaw)
        let pitchRotation  = Trafo3d.RotationInDegrees(east, sol.pitch)
        let rollRotation   = Trafo3d.RotationInDegrees(north, sol.roll)

        yawRotation * pitchRotation * rollRotation

    let parseRoverTraverse (traverse : GeoJsonFeatureCollection) =

        let parseProperties (sol : Sol) (x : GeoJsonFeature) : Result<Sol, TraverseParseError> =
            let reportErrorAndUseDefault (v : 'a) (r : Result<_,_>) =
                r |> Result.defaultValue' (fun e -> Log.warn "could not parse property: %A\n\n.Using fallback: %A" e v; v)

            result {
                let! solNumber      = parseIntProperty x  "sol" // not optional
                let! yaw            = parseDoubleProperty x  "yaw"     // not optional
                let! pitch          = parseDoubleProperty x  "pitch"   // not optional 
                let! roll           = parseDoubleProperty x  "roll"    // not optional

                // those are optional (still print a warning)
                let! tilt      = parseDoubleProperty x  "tilt"    |> reportErrorAndUseDefault  0.0    
                let! distanceM = parseDoubleProperty x  "dist_m"  |> reportErrorAndUseDefault  0.0    

                // not sure whether site should be optional (make it optional if needed), https://github.com/pro3d-space/PRo3D/issues/263
                let! site      = parseIntProperty x  "site"  
                                  
                return 
                    { sol with 
                        solType = Source.Rover
                        solNumber = solNumber
                        site = site
                        yaw = yaw
                        pitch = pitch
                        roll = roll
                        tilt = tilt
                        distanceM = distanceM
                    }
            }

        let parseFeature (x : GeoJsonFeature) =
            result {
                let! position = 
                    result {
                        match x.geometry with
                        | GeoJsonGeometry.Point p ->
                            match p with
                            | Coordinate.TwoDim y ->
                                //x ... lon
                                //y ... lat

                                let! elev_goid = parseDoubleProperty x "elev_geoid"                        
                                let latLonAlt = 
                                    V3d (
                                        y.Y, 
                                        360.0 - y.X, 
                                        elev_goid                            
                                    )

                                return CooTransformation.getXYZFromLatLonAlt' latLonAlt Planet.Mars
                            | Coordinate.ThreeDim y ->
                                let latLonAlt =  //y.YXZ
                                    V3d (
                                        y.Y, 
                                        360.0 - y.X, 
                                        y.Z                                 
                                    )

                                return CooTransformation.getXYZFromLatLonAlt' latLonAlt Planet.Mars
                        | e -> 
                            return! error (GeometryTypeNotSupported (string e))
                    }
                        
                let! sol = parseProperties { Sol.initial with version = Sol.current; location = position; } x

                // note is (now) optional for all cases. 
                // Previsouly note was mandatory in 2D, and optional in 3D - not sure whether this was intentioanl @ThomasOrtner
                let sol = 
                    match parseStringProperty x "Note" with
                    | Result.Ok note -> { sol with note = note }
                    | _ -> sol
        

                // either choose dist_total_m or dist_total - or default to zero if nothing? @ThomasOrnter - is this defaulting correct or an error?
                match parseDoubleProperty x "dist_total_m", parseDoubleProperty x "dist_total" with
                | Result.Ok dist, _ | _, Result.Ok  dist -> 
                    return { sol with totalDistanceM = dist }
                | _ ->
                    return { sol with totalDistanceM = 0.0 }
            }

        let sols = 
            traverse.features        
            |> List.mapi (fun i e -> (i,e)) // tag the elements for better error reporting
            |> List.choose (fun (idx, feature) ->
                match parseFeature feature with
                | Result.Ok r -> Some r
                | Result.Error e -> 
                    // we skip this one in case of errors, see // see https://github.com/pro3d-space/PRo3D/issues/263
                    Report.Warn(
                        String.concat Environment.NewLine [
                            sprintf "[Traverse] could not parse or interpret feature %d in the feature list.\n" idx 
                            sprintf "[Traverse] the detailled error is: %A" e
                            sprintf "[Traverse] skipping feature of type %A" feature.geometry
                        ]
                    )
                    None
            ) 

        sols

module TraversePropertiesApp =
    let update (model : Traverse) (action : TraversePropertiesAction) : Traverse = 
        match action with
        | ToggleShowText ->
            { model with showText = not model.showText }
        | ToggleShowLines ->
            { model with showLines = not model.showLines }
        | ToggleShowDots ->
            { model with showDots = not model.showDots }
        | SetTraverseName s ->
            { model with tName = s }
        | SetSolTextsize s ->
            { model with tTextSize = Numeric.update model.tTextSize s}
        | SetTraverseColor tc -> 
            { model with color = ColorPicker.update model.color tc }
        | SetLineWidth w ->
            { model with tLineWidth = Numeric.update model.tLineWidth w}
        | SetHeightOffset w -> 
            { model with heightOffset = Numeric.update model.heightOffset w}

    let computeSolFlyToParameters
        (sol : Sol) 
        (referenceSystem : ReferenceSystem) 
        : V3d * V3d * V3d =

        let rotation = if sol.solType = Source.Rover then RoverTraverseApp.computeSolRotation sol referenceSystem else MissionTraverseApp.computeSolRotation sol referenceSystem

        let north = rotation.Forward.TransformDir referenceSystem.northO
        let up    = rotation.Forward.TransformDir referenceSystem.up.value

        north, up, (sol.location + 2.0 * up)

    let computeSolViewplanParameters
        (sol : Sol)
        (referenceSystem : ReferenceSystem)
        : (string * Trafo3d * V3d * ReferenceSystem) = 

        let rotTrafo = if sol.solType = Source.Rover then RoverTraverseApp.computeSolRotation sol referenceSystem else MissionTraverseApp.computeSolRotation sol referenceSystem

        //let loc =(sol.location + sol.location.Normalized * 0.5)
        //let locTranslation = Trafo3d.Translation(loc)        

        let name = sprintf "Sol %d" sol.solNumber

        name, rotTrafo, sol.location, referenceSystem

    module UI =
    
        let viewTraverseProperties (m : AdaptiveTraverse) =
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
    
        let viewSolList 
            (refSystem : AdaptiveReferenceSystem) 
            (m : AdaptiveTraverse) =
    
            let listAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : hidden"
                } |> AttributeMap.ofAMap
    
            Incremental.div listAttributes (
                alist {
    
                    let! selected = m.selectedSol
                    let! sols = m.sols

                    let reversedSols = sols |> List.rev
                    
                    for sol in reversedSols do
                                                    
                        let color =
                            match selected with
                            | Some sel -> 
                                AVal.constant (if sel = sol.solNumber then C4b.VRVisGreen else C4b.Gray) 
                            | None ->
                                AVal.constant C4b.Gray
    
                        let headerText = sprintf "Sol %i" sol.solNumber                    
                
                        let white = sprintf "color: %s" (Html.color C4b.White)
                        let! c = color
                        let bgc = sprintf "color: %s" (Html.color c)
                            
                        //if (sol.solNumber = 238) then
                        //items
                        //yield div [clazz "item"; style white] [
                        //    i [clazz "bookmark middle aligned icon"; onClick (fun _ -> SelectSol sol.solNumber); style bgc] []
                        //    div [clazz "content"; style white] [                     
                        //        Incremental.div (AttributeMap.ofList [style white])(
                        //            alist {
                                            
                        //                yield div [clazz "header"; style bgc] [
                        //                    span [onClick (fun _ -> SelectSol sol.solNumber)] [text headerText]
                        //                ]                
    
                        //                let descriptionText = sprintf "yaw %A | pitch %A | roll %A" sol.yaw sol.pitch sol.roll
                        //                yield div [clazz "description"] [text descriptionText]
    
                        //                let! refSystem = refSystem.Current
                        //                yield i [clazz "home icon"; onClick (fun _ -> FlyToSol (computeSolFlyToParameters sol refSystem))] []
                        //                    |> UI.wrapToolTip DataPosition.Bottom "Fly to Sol"
                        //                yield i [clazz "location arrow icon"; onClick (fun _ -> PlaceRoverAtSol (computeSolViewplanParameters sol refSystem))] []
                        //                    |> UI.wrapToolTip DataPosition.Bottom "Make Viewplan"
                        //            } 
                        //        )                                     
                        //    ]
                        //]

                        // only to be called in callback
                        let getCurrentRefSystem () =
                            refSystem.Current.GetValue()

                        yield div [clazz "item"; style white] [
                            i [clazz "bookmark middle aligned icon"; onClick (fun _ -> SelectSol sol.solNumber); style bgc] []
                            div [clazz "content"; style white] [                     
                                div [style white] [
                                    yield div [clazz "header"; style bgc] [
                                        span [onClick (fun _ -> SelectSol sol.solNumber)] [text headerText]
                                    ]                
    
                                    let descriptionText = sprintf "yaw %A | pitch %A | roll %A" sol.yaw sol.pitch sol.roll
                                    yield div [clazz "description"] [text descriptionText]
    
                                    yield 
                                        i [clazz "home icon"; onClick (fun _ -> let refSystem = getCurrentRefSystem() in FlyToSol (computeSolFlyToParameters sol refSystem))] []
                                    yield 
                                        i [clazz "location arrow icon"; onClick (fun _ -> let refSystem = getCurrentRefSystem() in PlaceRoverAtSol (computeSolViewplanParameters sol refSystem))] []
                                ]                                     
                            ]
                        ]
                })

module TraverseApp = 

    let parseTraverse (traverse : GeoJsonFeatureCollection) = 
        let sols =
            if (List.length traverse.features = 1 && traverse.features[0].properties.ContainsKey("fromRMC")) then
                MissionTraverseApp.parseRIMFAXTraverse (traverse)
            else
                RoverTraverseApp.parseRoverTraverse (traverse)
        sols
    
    let compareNatural (left: AdaptiveTraverse) (right: AdaptiveTraverse) =
        Sorting.compareNatural left.tName right.tName

    let assignColorsToTraverse (traverses : List<string>) : List<string * C4b> =
        traverses |> List.map(fun x -> (x, C4b.Pink))

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
                |> assignColorsToTraverse
                |> List.map(fun (x, color) ->
                    Log.line "[Traverse] Loading %s" x
                    let geojson = System.IO.File.ReadAllText x
                     
                    let sols =
                        geojson 
                        |> Json.parse 
                        |> Json.deserialize 
                        |> parseTraverse

                    let name = Path.GetFileName x

                    let traverse = Traverse.initial name sols |> Traverse.withColor color
                    traverse |> HashMap.single traverse.guid        
                )
                |> List.fold(fun a b -> HashMap.union a b) model.traverses

            let traversesRover = 
                traversesJson
                |> HashMap.filter(fun guid traverse ->
                    traverse.sols[0].solType = Source.Rover
                )

            let traversesMissions = 
                traversesJson
                |> HashMap.filter(fun guid traverse ->
                    traverse.sols[0].solType = Source.RIMFAX
                )

            { model with traverses = model.traverses |> HashMap.union traversesRover; missions = model.missions |> HashMap.union traversesMissions; selectedTraverse = None }
        | IsVisibleT id ->
            let traverses' =  
                model.traverses 
                |> HashMap.alter id (function None -> None | Some t -> Some { t with isVisibleT = not t.isVisibleT })
            { model with traverses = traverses' }
        | RemoveTraverse id -> 
            let selectedTraverse' = 
                match model.selectedTraverse with
                | Some selT -> if selT = id then None else Some selT
                | None -> None

            let traverses' = HashMap.remove id model.traverses
            { model with traverses = traverses'; selectedTraverse = selectedTraverse' }
        | SelectTraverse id ->
            let selT = model.traverses |> HashMap.tryFind id
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
                let selectedT = model.traverses |> HashMap.tryFind id
                match selectedT with
                | Some selT ->
                    let traverse = (TraversePropertiesApp.update selT msg)
                    let traverses' = model.traverses |> HashMap.alter selT.guid (function | Some _ -> Some traverse | None -> None )
                    { model with traverses = traverses'} 
                | None -> model
            | None -> model
        | SelectSol solNumber ->
            match model.selectedTraverse with
            | Some id -> 
                let selectedT = model.traverses |> HashMap.tryFind id
                match selectedT with
                | Some selT ->
                    let selectedSol =
                        match solNumber, selT.selectedSol with
                        | number, None -> Some number
                        | number, Some n -> 
                            if n = number then None else Some number

                    let traverses' =  
                        model.traverses 
                        |> HashMap.alter id (function None -> None | Some t -> Some { t with selectedSol = selectedSol })
                    { model with traverses = traverses' }
                | None -> model
            | None -> model
        | RemoveAllTraverses ->
            { model with traverses = HashMap.empty; selectedTraverse = None }            
        |_-> model

    module UI =
        let viewTraverses
            (refSystem : AdaptiveReferenceSystem) 
            (m : AdaptiveTraverseModel) =

            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div itemAttributes (
                alist {

                    let! selected = m.selectedTraverse
                    let traverses = m.traverses |> AMap.toASetValues |> ASet.toAList |> AList.sortWith compareNatural //(fun x -> x.tName |> AVal.force)
                            
                    for traverse in traverses do
                        
                        let! sols = traverse.sols
                        let firstSol = sols.[0]
                        let infoc = sprintf "color: %s" (Html.color C4b.White)
            
                        let traverseID = traverse.guid
                        let toggleIcon = 
                            AVal.map( fun toggle -> if toggle then "unhide icon" else "hide icon") traverse.isVisibleT

                        let toggleMap = 
                            amap {
                                let! toggleIcon = toggleIcon
                                yield clazz toggleIcon
                                yield onClick (fun _ -> IsVisibleT traverseID)
                            } |> AttributeMap.ofAMap  

                       
                        let color =
                            match selected with
                            | Some sel -> 
                                AVal.constant (if sel = (traverse.guid) then C4b.VRVisGreen else C4b.Gray) 
                            | None -> AVal.constant C4b.Gray

                        let headerText = traverse.tName

                        let headerAttributes =
                            amap {
                                yield onClick (fun _ -> SelectTraverse traverseID)
                            } 
                            |> AttributeMap.ofAMap
            
                        let! c = color
                        let bgc = sprintf "color: %s" (Html.color c)
                        yield div [clazz "item"; style infoc] [
                            div [clazz "content"; style infoc] [                     
                                yield Incremental.div (AttributeMap.ofList [style infoc])(
                                    alist {
                                        //let! hc = headerColor
                                        yield div [clazz "header"; style bgc] [
                                            Incremental.span headerAttributes ([text headerText] |> AList.ofList)
                                         ]                           
                                        // fly to first sol of traverse
                                        let! refSystem = refSystem.Current
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToSol (TraversePropertiesApp.computeSolFlyToParameters firstSol refSystem))] []
                                            |> UI.wrapToolTip DataPosition.Bottom "Fly to traverse"          
            
                                        yield Incremental.i toggleMap AList.empty 
                                        |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                        yield i [clazz "Remove icon red"; onClick (fun _ -> RemoveTraverse traverseID)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Remove"                                            
                                    } 
                                )                                     
                            ]
                        ]
                } )

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
                    let! traverse = model.traverses |> AMap.tryFind id
                    match traverse with
                    | Some t -> return (TraversePropertiesApp.UI.viewTraverseProperties t |> UI.map TraversePropertiesMessage)
                    | None -> return empty
                | None -> return empty
            }  
            
        let viewSols (refSystem : AdaptiveReferenceSystem) (model:AdaptiveTraverseModel) =
            adaptive {
                let! guid = model.selectedTraverse
                let empty = div [ style "font-style:italic"] [ text "no traverse selected" ] |> UI.map TraversePropertiesMessage 
                
                match guid with
                | Some id -> 
                    let! traverse = model.traverses |> AMap.tryFind id
                    match traverse with
                    | Some t -> return (TraversePropertiesApp.UI.viewSolList refSystem t ) //|> UI.map TraverseAction)
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
                    let north, up, east = PRo3D.Core.Surface.TransformationApp.getNorthAndUpFromPivot sol.location refSystem
                    Trafo3d.Translation(offset * up)
            )

        let viewLines (refSystem: AdaptiveReferenceSystem) (traverseModel : AdaptiveTraverseModel) =

            let traverses = traverseModel.traverses
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
                        let loc = sol.location + sol.location.Normalized * 1.5
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
                        let loc = ~~(sol.location + sol.location.Normalized * 1.5)
                        let trafo = loc |> AVal.map Trafo3d.Translation
                        
                        yield Sg.text view near (AVal.constant 60.0) loc trafo model.tTextSize.value  (~~sol.solNumber.ToString()) (AVal.constant C4b.White)
            } 
            |> ASet.ofAList 
            |> Sg.set
            |> Sg.onOff model.isVisibleT


        let viewText (refSystem : AdaptiveReferenceSystem) view near (traverseModel : AdaptiveTraverseModel) =
        
            let traverses = traverseModel.traverses
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
                    sols |> List.toArray |> Array.map (fun sol -> Trafo3d.Translation(sol.location) * shift * viewTrafo) :> Array
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

        let viewTraverseCoordinateFrames (view : aval<CameraView>) (refSystem : AdaptiveReferenceSystem)  (traverse : AdaptiveTraverse) =
            let shift = getTraverseOffsetTransform refSystem traverse
            let solTrafosInRefSystem = 
                (traverse.sols, view, refSystem.Current)
                |||> AVal.bind3 (fun sols view refSystem -> 
                    let viewTrafo = view.ViewTrafo
                    shift |> AVal.map (fun shift -> 
                        sols |> List.toArray |> Array.map (fun sol -> 
                            let rotation = if sol.solType = Source.Rover then RoverTraverseApp.computeSolRotation sol refSystem else MissionTraverseApp.computeSolRotation sol refSystem
                            let loc = sol.location + sol.location.Normalized * 0.5 // when porting to instancing kept it 0.5
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
            (refSystem : AdaptiveReferenceSystem) (model : AdaptiveTraverse) : ISg<TraverseAction> = 

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
                        yield PRo3D.Core.Drawing.Sg.sphere' color ~~6.0 ~~sol.location

                        let loc =(sol.location + sol.location.Normalized * 0.5)
                        let locTranslation = Trafo3d.Translation(loc)
                        let! r = refSystem.Current
                        let rotation = if sol.solType = Source.Rover then RoverTraverseApp.computeSolRotation sol r else MissionTraverseApp.computeSolRotation sol r
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

            let traverses = traverseModel.traverses
            traverses 
            |> AMap.map(fun id traverse ->
                viewTraverseFast view refsys traverse
            )
            |> AMap.toASet 
            |> ASet.map snd 
            |> Sg.set