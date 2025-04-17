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
                let! sclkStart = parseIntProperty x  "SCLK_START"
                let! sclkEnd   = parseIntProperty x  "SCLK_END" 
                                  
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
                        
                let! sol = parseProperties { Sol.initial with version = Sol.current; location = [position]; } x

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
                    let traverses = m.RIMFAXTraverses |> AMap.toASetValues |> ASet.toAList |> AList.sortWith compareNatural //(fun x -> x.tName |> AVal.force)
                            
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
