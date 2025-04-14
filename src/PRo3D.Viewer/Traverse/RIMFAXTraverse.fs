namespace PRo3D.Viewer

open System
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base.Annotation.GeoJSON
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive


module RIMFAXTraverseApp =

    open TraverseUtilities

    let computeSolRotation (sol : Sol) (referenceSystem : ReferenceSystem) : Trafo3d =
        let north = referenceSystem.northO
        let up = referenceSystem.up.value
        let east = Vec.cross up north

        Trafo3d.Identity

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
                    let traverses = m.missions |> AMap.toASetValues |> ASet.toAList |> AList.sortWith compareNatural //(fun x -> x.tName |> AVal.force)
                            
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
    
                                    let descriptionText = sprintf "coordinates: %A" sol.location
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
                                ]                                     
                            ]
                        ]



                })
