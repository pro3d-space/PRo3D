namespace PRo3D.Viewer

open System
open Aardvark.Base
open Aardvark.UI
open Chiron
open PRo3D.Base.Annotation.GeoJSON
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators

module Double =
    let parse x =
        Double.Parse(x, Globalization.CultureInfo.InvariantCulture)

module TraverseApp =
    let parseTraverse (traverse : GeoJsonFeatureCollection) = 
        let sols = 
            traverse.features                 
            |> List.map(fun x ->
                match x.geometry with
                | GeoJsonGeometry.Point p ->
                    match p with
                    | Coordinate.TwoDim y ->
                        //x ... lon
                        //y ... lat

                        let latLonAlt = 
                            V3d (
                                y.Y, 
                                360.0 - y.X, 
                                x.properties.["elev_geoid"] |> Double.parse                                
                            )

                        let xyz = CooTransformation.getXYZFromLatLonAlt' latLonAlt Planet.Mars

                        { 
                            version        = Sol.current
                            location       = xyz
                            solNumber      = x.properties.["sol"]          |> int
                            site           = x.properties.["site"]         |> int
                            yaw            = x.properties.["yaw"]          |> Double.parse
                            pitch          = x.properties.["pitch"]        |> Double.parse
                            roll           = x.properties.["roll"]         |> Double.parse
                            tilt           = x.properties.["tilt"]         |> Double.parse
                            note           = x.properties.["Note"]
                            distanceM      = x.properties.["dist_m"]       |> Double.parse
                            totalDistanceM = x.properties.["dist_total_m"] |> Double.parse
                        }

                    | _ -> failwith "not twodim"
                | _ -> failwith "not point"
            )                

        sols

    let update (model : Traverse) (action : TraverseAction) : Traverse = 
        match action with
        | LoadTraverse path ->
            if System.IO.File.Exists path then
                Log.line "[Traverse] Loading %s" path
                let geojson = System.IO.File.ReadAllText path
                 
                let sols =
                    geojson 
                    |> Json.parse 
                    |> Json.deserialize 
                    |> parseTraverse

                { model with sols = sols }
            else
                model
        | SelectSol solNumber ->
            let selectedSol =
                match solNumber, model.selectedSol with
                | number, None -> Some number
                | number, Some n -> 
                    if n = number then None else Some number

            { model with selectedSol = selectedSol }
        | FlyToSol _ ->
            model
        | ToggleShowText ->
            { model with showText = not model.showText }
        | ToggleShowLines ->
            { model with showLines = not model.showLines }
        | ToggleShowDots ->
            { model with showDots = not model.showDots }
            
    let viewLines (model : AdaptiveTraverse) : ISg<TraverseAction> =
        adaptive {
            let! sols = model.sols
            let lines = 
                sols 
                |> List.map(fun x -> x.location)
                |> List.toArray
                |> PRo3D.Core.Drawing.Sg.lines C4b.White 2.0 
            
            return lines
        }
        |> Sg.dynamic
        |> Sg.onOff model.showLines

    let viewCoordinateCross (refSystem : AdaptiveReferenceSystem) (trafo : aval<Trafo3d>) =
        
        let up = refSystem.up.value
        let north = refSystem.northO
        let east = AVal.map2(Vec.cross) up north

        [
            Sg.drawSingleLine ~~V3d.Zero up    ~~C4b.Blue  ~~2.0 trafo
            Sg.drawSingleLine ~~V3d.Zero north ~~C4b.Red   ~~2.0 trafo
            Sg.drawSingleLine ~~V3d.Zero east  ~~C4b.Green ~~2.0 trafo
        ] 
        |> Sg.ofList
        
    let computeSolRotation (sol : Sol) (referenceSystem : ReferenceSystem) : Trafo3d =
        let north = referenceSystem.northO
        let up = referenceSystem.up.value
        let east = Vec.cross up north                
        
        let yawRotation    = Trafo3d.RotationInDegrees(up, -sol.yaw)
        let pitchRotation  = Trafo3d.RotationInDegrees(east, sol.pitch)
        let rollRotation   = Trafo3d.RotationInDegrees(north, sol.roll)

        yawRotation * pitchRotation * rollRotation

    let computeSolFlyToParameters (sol : Sol) (referenceSystem : ReferenceSystem) : V3d * V3d * V3d =
        let rotation = computeSolRotation sol referenceSystem

        let north = rotation.Forward.TransformDir referenceSystem.northO
        let up    = rotation.Forward.TransformDir referenceSystem.up.value

        north, up, (sol.location + 2.0 * up)

    module Sg =
        let view view near (refSystem : AdaptiveReferenceSystem) (model : AdaptiveTraverse) : ISg<TraverseAction> = 
            alist {
                let! sols = model.sols
                for sol in sols |> List.rev do
                    let loc = ~~(sol.location + sol.location.Normalized * 1.5)
                    let trafo = loc |> AVal.map Trafo3d.Translation
                    let text = 
                        Sg.text view near (AVal.constant 60.0) loc trafo ~~0.05  (~~sol.solNumber.ToString())
                    
                    let! showText = model.showText
                    if showText then
                        yield text

                    let! showDots = model.showDots
                    if showDots then
                        let! selected = model.selectedSol
                        let color =
                            match selected with
                            | Some sel -> 
                                AVal.constant (if sel = sol.solNumber then C4b.VRVisGreen else C4b.White)
                            | None ->
                                AVal.constant C4b.White
                        yield PRo3D.Core.Drawing.Sg.sphere' color ~~6.0 ~~sol.location

                        let loc =(sol.location + sol.location.Normalized * 0.5)
                        let locTranslation = Trafo3d.Translation(loc)
                        let! r = refSystem.Current
                        let rotation = computeSolRotation sol r
                        yield viewCoordinateCross refSystem ~~(rotation * locTranslation)
            }        
            |> ASet.ofAList         
            |> Sg.set

    module UI =

        let viewTraverseProperties (m : AdaptiveTraverse) =
            require GuiEx.semui (
                Html.table [
                    Html.row "Show Text:"  [GuiEx.iconCheckBox m.showText  ToggleShowText]
                    Html.row "Show Lines:" [GuiEx.iconCheckBox m.showLines ToggleShowLines]
                    Html.row "Show dots:"  [GuiEx.iconCheckBox m.showDots  ToggleShowDots]
                ]
            )

        let viewSolList (refSystem : AdaptiveReferenceSystem) (m : AdaptiveTraverse) =

            let itemAttributes =
                amap {
                    yield clazz "ui divided list inverted segment"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap

            Incremental.div itemAttributes (
                alist {

                    let! selected = m.selectedSol
                    let! sols = m.sols
                
                    for sol in sols do
                                                
                        let color =
                            match selected with
                            | Some sel -> 
                                AVal.constant (if sel = sol.solNumber then C4b.VRVisGreen else C4b.Gray) 
                            | None ->
                                AVal.constant C4b.Gray

                        let headerText = sprintf "Sol %i" sol.solNumber                    
            
                        let white = sprintf "color: %s" (Html.ofC4b C4b.White)
                        let! c = color
                        let bgc = sprintf "color: %s" (Html.ofC4b c)
                        
                        yield div [clazz "item"; style white] [
                            i [clazz "bookmark middle aligned icon"; onClick (fun _ -> SelectSol sol.solNumber); style bgc][]
                            div [clazz "content"; style white] [                     
                                Incremental.div (AttributeMap.ofList [style white])(
                                    alist {
                                        
                                        yield div[clazz "header"; style bgc][
                                            span [onClick (fun _ -> SelectSol sol.solNumber)] [text headerText]
                                        ]                

                                        let descriptionText = sprintf "yaw %A | pitch %A | roll %A" sol.yaw sol.pitch sol.roll
                                        yield div[clazz "description"][text descriptionText]

                                        let! refSystem = refSystem.Current
                                        yield i [clazz "home icon"; onClick (fun _ -> FlyToSol (computeSolFlyToParameters sol refSystem)) ][]
                                            |> UI.wrapToolTip DataPosition.Bottom "Fly to Sol"
                                    } 
                                )                                     
                            ]
                        ]
                })
        

