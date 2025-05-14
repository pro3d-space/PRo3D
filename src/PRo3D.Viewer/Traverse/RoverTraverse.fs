namespace PRo3D.Viewer

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base.Annotation.GeoJSON
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive

module RoverTraverseApp =

    open TraverseUtilities

    let computeSolRotation (sol : Sol) (referenceSystem : ReferenceSystem) : Trafo3d =
        Trafo3d.Identity

    let parseTraverse (traverse : GeoJsonTraverse) =

        let parseProperties (sol : Sol) (x : GeoJsonFeature) : Result<Sol, TraverseParseError> =
            let reportErrorAndUseDefault (v : 'a) (r : Result<_,_>) =
                r |> Result.defaultValue' (fun e -> Log.warn "could not parse property: %A\n\n.Using fallback: %A" e v; v)

            result {
                let! solNumber  = parseIntProperty x  "sol"
                let! fromRMC    = parseStringProperty x  "fromRMC"
                let! toRMC  = parseStringProperty x  "toRMC"
                let! length = parseDoubleProperty x  "length"
                let! sclkStart = parseDoubleProperty x  "SCLK_START"
                let! sclkEnd   = parseDoubleProperty x  "SCLK_END" 
                                  
                return 
                    { sol with 
                        solNumber = solNumber
                        fromRMC = fromRMC
                        toRMC = toRMC
                        length = length
                        sclkStart = sclkStart
                        sclkEnd = sclkEnd
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

        let parseFeature (idx : int) (x : GeoJsonFeature) =
            let locations = 
                match x.geometry with
                | GeoJsonGeometry.LineString coordinates ->
                    [coordinates 
                    |> List.map (fun coord -> parseCoordinate coord)]
                | GeoJsonGeometry.MultiLineString coordinates ->
                    coordinates |> List.map (List.map parseCoordinate)
                | _ -> [[]]
            let sols = 
                locations 
                |> List.choose (fun location -> 
                    match parseProperties { Sol.initial with version = Sol.current; location = location; } x with
                    | Result.Ok r -> Some r
                    | Result.Error e -> 
                        // we skip this one in case of errors, see // see https://github.com/pro3d-space/PRo3D/issues/263
                        Report.Warn(
                            String.concat Environment.NewLine [
                                sprintf "[Traverse] could not parse or interpret feature for coordinate %d in the coordinate list.\n" idx 
                                sprintf "[Traverse] the detailled error is: %A" e
                                sprintf "[Traverse] skipping feature of type %A" x.geometry
                            ]
                        )
                        None
                    )
            sols

        let sols = 
            traverse.features        
            |> List.mapi (fun i e -> (i,e)) // tag the elements for better error reporting
            |> List.map (fun (idx, feature) -> parseFeature idx feature) 
            |> List.concat
        sols

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
                    let traverses = m.roverTraverses |> AMap.toASetValues |> ASet.toAList |> AList.sortWith compareNatural //(fun x -> x.tName |> AVal.force)
                            
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
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToSol (computeSolFlyToParameters firstSol refSystem (computeSolRotation firstSol refSystem)))] []
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

        let viewSolList 
            (refSystem : AdaptiveReferenceSystem) 
            (RIMFAXTraverses: amap<Guid, AdaptiveTraverse>)
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

                        let! missionReference = 
                            AVal.custom (fun t -> 
                                let traverseMap = RIMFAXTraverses.Content.GetValue t
                                traverseMap 
                                    |> HashMap.toValueList
                                    |> List.tryPick (fun v ->
                                        let sols = v.sols.GetValue t
                                        if sols[0].fromRMC = sol.RMC then Some v.guid else None
                                    )
                            )
   
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
                                        i [clazz "home icon"; onClick (fun _ -> let refSystem = getCurrentRefSystem() in FlyToSol (
                                        computeSolFlyToParameters
                                            sol
                                            refSystem
                                            (computeSolRotation sol refSystem)))] []
                                    yield 
                                        i [clazz "location arrow icon"; onClick (fun _ -> let refSystem = getCurrentRefSystem() in PlaceRoverAtSol (
                                            computeSolViewplanParameters
                                                sol
                                                refSystem
                                                (computeSolRotation sol refSystem)))] []
                                    match missionReference with
                                    | None -> ()
                                    | Some reference -> 
                                        yield i [clazz "wrench icon blue"; onClick (fun _ -> SelectTraverse reference)] [] 
                                            |> UI.wrapToolTip DataPosition.Bottom "Select Mission"
                                ]                                     
                            ]
                        ]



                })
