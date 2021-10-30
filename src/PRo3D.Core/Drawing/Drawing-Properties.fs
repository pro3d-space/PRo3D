namespace PRo3D.Core.Drawing

open System
open Adaptify.FSharp.Core
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Static.Svg

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core

module AnnotationProperties = 
            
    type Action = 
    | SetGeometry     of Geometry
    | SetProjection   of Projection
    | SetSemantic     of Semantic
    | ChangeThickness of Numeric.Action
    | ChangeColor     of ColorPicker.Action
    | SetText         of string
    | SetTextSize     of Numeric.Action
    | ToggleVisible
    | ToggleShowDns
    | PrintPosition   
    | SetManualDippingAngle of Numeric.Action
    | SetManualDippingAzimuth of Numeric.Action
       
    let horizontalDistance (points:list<V3d>) (up:V3d) = 
        match points.Length with
        | 1 -> 0.0
        | _ -> 
            let a = points |> List.head
            let b = points |> List.last
            let v = (a - b)
            let vertical = (v |> Vec.dot up.Normalized)

            (v.LengthSquared - (vertical |> Fun.Square)) |> Fun.Sqrt

    let verticalDistance (points:list<V3d>) (up:V3d) = 
        match points.Length with
        | 1 -> 0.0
        | _ -> 
            let a = points |> List.head
            let b = points |> List.last
            let v = (b - a)

            (v |> Vec.dot up.Normalized)
            
    let update (model : Annotation) (planet : Planet) (act : Action) =
        match act with
        | SetGeometry mode ->
            { model with geometry = mode }
        | SetSemantic mode ->
            { model with semantic = mode }
        | SetProjection mode ->
            { model with projection = mode }
        | ChangeThickness a ->
            { model with thickness = Numeric.update model.thickness a }
        | SetText t ->
            { model with text = t }
        | SetTextSize s ->
            { model with textsize = Numeric.update model.textsize s }
        | ToggleVisible ->
            { model with visible = (not model.visible) }
        | ToggleShowDns ->
            { model with showDns = (not model.showDns) }
        | ChangeColor a ->
            { model with color = ColorPicker.update model.color a }
        | PrintPosition ->            
            match model.geometry with
            | Geometry.Point -> 
                match model.points |> IndexList.tryFirst with 
                | Some p -> 
                    Log.line "--- Printing Point Coordinates ---"
                    Log.line "XYZ: %A" p
                    Log.line "LatLonAlt: %A" (CooTransformation.getLatLonAlt planet p|> CooTransformation.SphericalCoo.toV3d)
                    Log.line "--- Done ---"
                | None -> failwith "[DrawingProperties] point geometry without point is invalid"
            | _ -> ()
            
            model
        | SetManualDippingAngle a ->                
            { model with manualDipAngle = Numeric.update model.manualDipAngle a}
        | SetManualDippingAzimuth a ->
            { model with manualDipAzimuth = Numeric.update model.manualDipAzimuth a }

    let view (paletteFile : string) (model : AdaptiveAnnotation) = 

        require GuiEx.semui (
            Html.table [                                            
                Html.row "Geometry:"    [Incremental.text (model.geometry |> AVal.map (fun x -> sprintf "%A" x ))]
                Html.row "Projection:"  [Incremental.text (model.projection |> AVal.map (fun x -> sprintf "%A" x ))]
                Html.row "Semantic:"    [Html.SemUi.dropDown model.semantic SetSemantic]      
                Html.row "Thickness:"   [Numeric.view' [InputBox] model.thickness |> UI.map ChangeThickness ]
                Html.row "Color:"       [ColorPicker.viewAdvanced ColorPicker.defaultPalette paletteFile "pro3d" model.color |> UI.map ChangeColor ]
                Html.row "Text:"        [Html.SemUi.textBox model.text SetText ]
                Html.row "TextSize:"    [Numeric.view' [InputBox] model.textsize |> UI.map SetTextSize ]
                Html.row "Visible:"     [GuiEx.iconCheckBox model.visible ToggleVisible ]
                Html.row "Show DnS:"    [GuiEx.iconCheckBox model.showDns ToggleShowDns ]
                Html.row "Dip Angle:"   [Numeric.view' [InputBox] model.manualDipAngle |> UI.map SetManualDippingAngle]
                Html.row "Dip Azimuth:" [Numeric.view' [InputBox] model.manualDipAzimuth |> UI.map SetManualDippingAzimuth]
            ]

        )

    // TODO v5: remove this duplicate
    module AdaptiveOption =
        let toOption (a : AdaptiveOptionCase<_,_,_>) =
            match a with
            | AdaptiveSome a -> Some a
            | AdaptiveNone -> None

    let viewResults (model : AdaptiveAnnotation) (up:aval<V3d>) =   
        
        let results       = AVal.map AdaptiveOption.toOption model.results
        let height        = AVal.bindOption results Double.NaN (fun a -> a.height)
        let heightD       = AVal.bindOption results Double.NaN (fun a -> a.heightDelta)
        let alt           = AVal.bindOption results Double.NaN (fun a -> a.avgAltitude)
        let length        = AVal.bindOption results Double.NaN (fun a -> a.length)
        let wLength       = AVal.bindOption results Double.NaN (fun a -> a.wayLength)
        let bearing       = AVal.bindOption results Double.NaN (fun a -> a.bearing)
        let slope         = AVal.bindOption results Double.NaN (fun a -> a.slope)
        let trueThickness = AVal.bindOption results Double.NaN (fun a -> a.trueThickness)

        let pos = 
            AVal.map( 
                fun x -> 
                    match x with 
                    | Geometry.Point -> 
                        let points = model.points |> AList.force |> IndexList.toArray
                        points.[0].ToString()
                    | _ -> "" 
            ) model.geometry
        
        // TODO refactor: why so complicated to list stuff?, not incremental
        let vertDist = AVal.map( fun u -> verticalDistance   (model.points |> AList.force |> IndexList.toList) u ) up 
        let horDist  = AVal.map( fun u -> horizontalDistance (model.points |> AList.force |> IndexList.toList) u ) up

        //apparent thickness
        //vertical thickness
        //true thickness
      
        require GuiEx.semui (
            Html.table [   
                //yield Html.row "Position:"              
                //    [
                //        Incremental.text (pos |> AVal.map  (fun d -> d));                         
                //        //button [clazz "ui button tiny"; onClick (fun _ -> PrintPosition)][i[clazz "ui icon print"][]]
                //    ]

                yield Html.row "PrintPosition:"         [button [clazz "ui button tiny"; onClick (fun _ -> PrintPosition )][i[clazz "ui icon print"][]]]
                yield Html.row "Height:"                [Incremental.text (height  |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
                yield Html.row "HeightDelta:"           [Incremental.text (heightD |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
                yield Html.row "Avg Altitude:"          [Incremental.text (alt     |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
                yield Html.row "Length:"                [Incremental.text (length  |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
                yield Html.row "WayLength:"             [Incremental.text (wLength |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
                yield Html.row "Bearing:"               [Incremental.text (bearing |> AVal.map  (fun d -> sprintf "%.4f deg" (d)))]
                yield Html.row "Slope:"                 [Incremental.text (slope   |> AVal.map  (fun d -> sprintf "%.4f deg" (d)))]
                yield Html.row "Vertical Distance:"     [Incremental.text (vertDist  |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
                yield Html.row "Horizontal Distance:"   [Incremental.text (horDist   |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
                yield Html.row "True Thickness:"        [Incremental.text (trueThickness |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
            ]
        )
       
    //let app = 
    //    {
    //        unpersist = Unpersist.instance
    //        threads   = fun _ -> ThreadPool.empty
    //        initial   = Annotation.initial
    //        update    = update
    //        view      = view
    //    }

    //let start() = App.start app

