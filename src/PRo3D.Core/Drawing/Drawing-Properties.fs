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
    | ToggleShowText
    | PrintPosition   
    | SetManualDippingAngle of Numeric.Action
    | SetManualDippingAzimuth of Numeric.Action
        
    let update (referenceSystem : ReferenceSystem) (model : Annotation) (act : Action) =
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
        | ToggleShowText ->
            { model with showText = (not model.showText) }
        | ChangeColor a ->
            { model with color = ColorPicker.update model.color a }
        | PrintPosition ->            
            match model.geometry with
            | Geometry.Point -> 
                match model.points |> IndexList.tryFirst with 
                | Some firstPoint -> 
                    Log.line "--- Printing Point Coordinates ---"
                    Log.line "XYZ: %A" firstPoint
                    Log.line "LatLonAlt: %A" (CooTransformation.getLatLonAlt referenceSystem.planet firstPoint |> CooTransformation.SphericalCoo.toV3d)
                    Log.line "--- Done ---"
                | None -> failwith "[DrawingProperties] point geometry without point is invalid"
            | _ -> ()
            
            model
        | SetManualDippingAngle a ->                
            let model = { model with manualDipAngle = Numeric.update model.manualDipAngle a }
            let dnsResults = DipAndStrike.calculateManualDipAndStrikeResults referenceSystem.up.value referenceSystem.northO model
            { model with dnsResults = dnsResults }
        | SetManualDippingAzimuth a ->
            let model ={ model with manualDipAzimuth = Numeric.update model.manualDipAzimuth a }
            let dnsResults = DipAndStrike.calculateManualDipAndStrikeResults referenceSystem.up.value referenceSystem.northO model            
            { model with dnsResults = dnsResults }


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
                Html.row "Show Text:"   [GuiEx.iconCheckBox model.showText ToggleShowText ]
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
        
        let results           = AVal.map AdaptiveOption.toOption model.results
        let height            = AVal.bindOption results Double.NaN (fun a -> a.height)
        let heightD           = AVal.bindOption results Double.NaN (fun a -> a.heightDelta)
        let alt               = AVal.bindOption results Double.NaN (fun a -> a.avgAltitude)
        let length            = AVal.bindOption results Double.NaN (fun a -> a.length)
        let wLength           = AVal.bindOption results Double.NaN (fun a -> a.wayLength)
        let bearing           = AVal.bindOption results Double.NaN (fun a -> a.bearing)
        let slope             = AVal.bindOption results Double.NaN (fun a -> a.slope)
        let trueThickness     = AVal.bindOption results Double.NaN (fun a -> a.trueThickness)
        let verticalThickness = AVal.bindOption results Double.NaN (fun a -> a.verticalThickness)
 
        // TODO refactor: why so complicated to list stuff?, not incremental
        let vertDist = AVal.map( fun u -> Calculations.verticalDelta   (model.points |> AList.force |> IndexList.toList) u ) up 
        let horDist  = AVal.map( fun u -> Calculations.horizontalDelta (model.points |> AList.force |> IndexList.toList) u ) up

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

                yield Html.row "PrintPosition:"         [button [clazz "ui button tiny"; onClick (fun _ -> PrintPosition )] [i [clazz "ui icon print"] []]]
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
                yield Html.row "Vertical Thickness:"    [Incremental.text (verticalThickness |> AVal.map  (fun d -> sprintf "%.4f m" (d)))]
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

