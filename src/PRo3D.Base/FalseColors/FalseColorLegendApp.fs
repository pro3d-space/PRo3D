namespace PRo3D

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Operators
open Aardvark.UI.Primitives
open Aardvark.Rendering.Text

open PRo3D.Base

module FalseColorLegendApp = 
            
    type Action =
        | UseFalseColors    
        | SetLowerBound     of Numeric.Action
        | SetUpperBound     of Numeric.Action
        | SetInterval       of Numeric.Action
        | InvertMapping     
        | SetLowerColor     of ColorPicker.Action //C4b
        | SetUpperColor     of ColorPicker.Action //C4b  

    let bindOption (m : aval<Option<'a>>) (defaultValue : 'b) (project : 'a -> aval<'b>)  : aval<'b> =
        m |> AVal.bind (function | None   -> AVal.constant defaultValue       
                                 | Some v -> project v)
    
   
    let update (model : FalseColorsModel) (act : Action) =
        match act with
        | UseFalseColors -> 
            { model with useFalseColors = (not model.useFalseColors) }                    
        | SetLowerBound l -> 
            { model with lowerBound = Numeric.update model.lowerBound l }
        | SetUpperBound u -> 
            { model with upperBound = Numeric.update model.upperBound u }
        | SetInterval i -> 
            { model with interval = Numeric.update model.interval i }
        | InvertMapping ->
            { model with invertMapping = not model.invertMapping }
        | SetLowerColor lc -> 
            { model with lowerColor = ColorPicker.update model.lowerColor lc }
        | SetUpperColor uc -> 
            { model with upperColor = ColorPicker.update model.upperColor uc }
            
            
    //let myCss = { kind = Stylesheet; name = "semui-overrides"; url = "semui-overrides.css" }

    module UI =
        let viewScalarMappingProperties (paletteFile : string) (model:AdaptiveFalseColorsModel) = 
            require GuiEx.semui (
                Html.table [  
                    Html.row "show legend:"             [GuiEx.iconCheckBox model.useFalseColors UseFalseColors ]
                    Html.row "max:"                     [Numeric.view' [InputBox] model.upperBound |> UI.map SetUpperBound ]
                    Html.row "min:"                     [Numeric.view' [InputBox] model.lowerBound |> UI.map SetLowerBound ]
                    Html.row "interval:"                [Numeric.view' [InputBox] model.interval |> UI.map SetInterval ]
                    Html.row "upper color:"             [ColorPicker.viewAdvanced ColorPicker.defaultPalette paletteFile "pro3d" true model.upperColor |> UI.map SetUpperColor ]
                    Html.row "lower color:"             [ColorPicker.viewAdvanced ColorPicker.defaultPalette paletteFile "pro3d" true model.lowerColor |> UI.map SetLowerColor ]
                    Html.row "invert mapping:"          [GuiEx.iconCheckBox model.invertMapping InvertMapping ]
                ]                
            )

        let viewDepthLegendProperties (model:AdaptiveFalseColorsModel) = 
            require GuiEx.semui (
                Html.table [  
                    Html.row "show legend:"             [GuiEx.iconCheckBox model.useFalseColors UseFalseColors ]
                    Html.row "max:"                     [Numeric.view' [InputBox] model.upperBound |> UI.map SetUpperBound ]
                    Html.row "min:"                     [Numeric.view' [InputBox] model.lowerBound |> UI.map SetLowerBound ]
                    Html.row "interval:"                [Numeric.view' [InputBox] model.interval |> UI.map SetInterval ]
                    //Html.row "upper color:"             [ColorPicker.viewAdvanced ColorPicker.defaultPalette colorPaletteStore "pro3d" model.upperColor |> UI.map SetUpperColor ]
                    //Html.row "lower color:"             [ColorPicker.viewAdvanced ColorPicker.defaultPalette colorPaletteStore "pro3d" model.lowerColor |> UI.map SetLowerColor ]
                    //tml.row "invert mapping:"          [GuiEx.iconCheckBox model.invertMapping InvertMapping ]
                ]                
            )
            


    module Draw =
        open System.Linq.Expressions

        let getColor (hue : float32) =
            let currHSV     = HSVf(hue, 1.0f, 1.0f)
            currHSV.ToC3f().ToC3b()

        let getColorDnS (model : AdaptiveFalseColorsModel) (angle: aval<float>) =
            adaptive {
                let! dipAngle = angle
                let! fcInterval     = model.interval.value
                let! startColor     = model.upperColor.c
                let! endColor       = model.lowerColor.c
                let! invertMapping  = model.invertMapping
                let! fcUpperBound   = model.upperBound.value
                let! fcLowerBound   = model.lowerBound.value
                                
                let hsvStart = HSVf.FromC3f (startColor.ToC3f())
                let hsvEnd   = HSVf.FromC3f (endColor.ToC3f())

                let range = fcUpperBound - fcLowerBound
                let numOfRangeGaps = int (System.Math.Round (range / fcInterval))
                let numOfStops = if (numOfRangeGaps + 2) > 100 then 100 else (numOfRangeGaps + 2)

                let hStepSize =
                  if hsvStart.H < hsvEnd.H then 
                    (hsvEnd.H - hsvStart.H) / (single (numOfStops-1)) 
                  else 
                    ((hsvEnd.H + 1.0f) - hsvStart.H) / (single (numOfStops-1))
                
                let pos = 
                    match fcLowerBound < dipAngle with
                         | true -> int (System.Math.Round ( (dipAngle - fcLowerBound) / (float)fcInterval )) //+1
                         | _ -> 0
                let pos' = 
                    match fcUpperBound < dipAngle with
                        | true -> numOfRangeGaps + 1
                        | _ -> pos
                     
                let currColor = 
                  if invertMapping then
                    getColor (hsvStart.H + (hStepSize * (single (pos'))))
                  else 
                    getColor (hsvEnd.H - (hStepSize * (single (pos'))))
                    
                return currColor.ToC3f().ToC4b()
            }

        let getShaderParams (model : AdaptiveFalseColorsModel) : aval<FalseColorsShaderParams> = 
            adaptive {
                let! fcInterval     = model.interval.value
                let! startColor     = model.lowerColor.c
                let! endColor       = model.upperColor.c
                let! invertMapping  = model.invertMapping
                let! fcUpperBound   = model.upperBound.value
                let! fcLowerBound   = model.lowerBound.value
                             
                let hsvStart = HSVf.FromC3f (startColor.ToC3f())
                let hsvEnd   = HSVf.FromC3f (endColor.ToC3f())

                let range = fcUpperBound - fcLowerBound
                let numOfRangeGaps = int (System.Math.Round (range / fcInterval))
                let numOfStops = if (numOfRangeGaps + 2) > 100 then 100 else (numOfRangeGaps + 2)

                let hStepSize =
                  if hsvStart.H < hsvEnd.H then 
                    (hsvEnd.H - hsvStart.H) / (single (numOfStops-1)) 
                  else 
                    ((hsvEnd.H + 1.0f) - hsvStart.H) / (single (numOfStops-1))

                return { 
                    hsvStart = V3d((float)hsvStart.H, (float)hsvStart.S, (float)hsvStart.V)
                    hsvEnd = V3d(hsvEnd.H, hsvEnd.S, hsvEnd.V)
                    interval = (float)fcInterval
                    inverted = invertMapping
                    lowerBound = fcLowerBound
                    upperBound = fcUpperBound
                    stepS = (float)hStepSize
                    numOfRG = (float)numOfRangeGaps
                }
            }
           
        let createStopps (numOfStops : int) (startColor : C4b) (endColor : C4b) (inverted : bool) =
            let inverted = (not inverted)
            let hsvStart = HSVf.FromC3f (startColor.ToC3f())
            let hsvEnd   = HSVf.FromC3f (endColor.ToC3f())
            let stepSize = 1.0 / (float numOfStops)

            let hStepSize =
              if hsvStart.H < hsvEnd.H then 
                (hsvEnd.H - hsvStart.H) / (single (numOfStops-1)) 
              else 
                ((hsvEnd.H + 1.0f) - hsvStart.H) / (single (numOfStops-1))

            let mutable stops = 
              if not inverted then 
                [(1.0f, getColor hsvStart.H)]
              else 
                [(1.0f, getColor hsvEnd.H)]

            for i = 1 to (numOfStops-1) do
                let currColor = 
                  if not inverted then
                    getColor (hsvStart.H + (hStepSize * (single (i-1))))
                  else 
                    getColor (hsvEnd.H - (hStepSize * (single (i-1))))

                let percentage = System.Math.Round ((stepSize * (float (numOfStops - i))), 5)
                let addElement = (single percentage, currColor)
                stops <- addElement :: stops
                if (numOfStops < 100) then 
                    let nextColor = 
                      if not inverted then 
                        getColor (hsvStart.H + (hStepSize * (single i)))
                      else 
                        getColor (hsvEnd.H - (hStepSize * (single i)))
                                    
                    let otherElement = (single percentage, nextColor)
                    stops <- otherElement :: stops

            let laststop = 
              if not inverted then 
                (0.0f, getColor hsvEnd.H) 
              else 
                (0.0f, getColor hsvStart.H)
            stops <- laststop :: stops
            stops
                
        let buildSvgStop (off:float32) (col:C3b) = 
                let offset = sprintf "%f%%" (off * 100.0f)
                let color = sprintf "stop-color:rgb(%i,%i,%i);stop.opacity:1" col.R col.G col.B
                //printfn "offset : %s  style : %s" offset color                

                Svg.stop ["offset" => offset; style color]
    
        let createFalseColorLegendBasics (id : string) (falseColor : AdaptiveFalseColorsModel) =
            alist { 
                let! enabled        = falseColor.useFalseColors                    
                let! fcUpperBound   = falseColor.upperBound.value
                let! fcLowerBound   = falseColor.lowerBound.value
                let! fcInterval     = falseColor.interval.value
                let! startColor     = falseColor.upperColor.c
                let! endColor       = falseColor.lowerColor.c
                let! invertMapping  = falseColor.invertMapping


                if enabled then
                    let range = (fcUpperBound - fcLowerBound) |> abs
                    let numOfRangeGaps = int (System.Math.Round (range / fcInterval))                    
                    
                    let numOfBuckets = if (numOfRangeGaps + 2) > 100 then 100 else (numOfRangeGaps + 2)
                    let numOfLabels =  
                        if numOfBuckets > 100 then 25
                        else if numOfBuckets > 25 then (numOfBuckets / 4)
                        else numOfBuckets - 2
                    
                    let stopList = createStopps numOfBuckets startColor endColor invertMapping
                    
                    let svgstopList = 
                        stopList
                        |> List.map (fun a ->
                            let (off, col) = a
                            buildSvgStop off col)
                        |> AList.ofList
                    
                    yield Svg.defs [] [
                        onBoot ("$('#__ID__').attr('id','" + id + "')") (
                            Incremental.Svg.linearGradient 
                                (AttributeMap.ofList [  "x1" => "0%"; "y1" => "0%"; 
                                                        "x2" => "0%"; "y2" => "100%";
                                                        "pointer-events" => "none";]) // x1 = x2 => vertical Gradient
                                svgstopList
                        )]
                        
                    yield Svg.rect [
                        "fill"          => "#EEEEEE";
                                    "width"         => "42px";
                        "width"         => "42px";
                        "height"        => "95%"; //95%
                        "x"             => "8px";    
                        "y"             => "1.75%";
                        "rx"            => "5";       
                        "ry"            => "5";
                        "stroke"        => "black";
                        "stroke-width"  => "1px";
                        "opacity"       => "0.5";
                    ]
                               


                    // SVG-RECT applies previously defined gradient (by #id)
                    yield Svg.rect [
                        "style"         => "fill:url(#" + id + ")";
                        "width"         => "10px";  
                        "height"        => "95%"//95%
                        "x"             => "12px";      
                        "y"             => "2.5%"; 
                        "stroke"        => "white";
                        "stroke-width"  => "1px";
                        "rx"            => "3";
                        "ry"            => "3";
                    ]
                   
                    let labelInterval = range / (float numOfLabels)
                    
                    let filteri f s = 
                        s
                        |> List.mapi (fun i v -> (i,v))
                        |> List.filter (fun v -> f (fst v) (snd v))
                        |> List.map (fun v -> snd v)
                    
                    let mapOffset (l : list<float32 * C3b>) = 
                        l |> List.map fst    
                    
                    let labelPosList = 
                        if (numOfBuckets < 26) then
                            mapOffset stopList          
                            |> filteri (fun i _ -> i % 2 = 1)
                        else 
                            [0 .. numOfLabels]
                            |> List.map (fun a -> (single a) / (single numOfLabels))
                    
                    for i in 0..numOfLabels do 
                        let a = ((List.item (numOfLabels - i) labelPosList) * 0.95f) + 0.03f
                        let output = fcLowerBound + (labelInterval * (float i))
                        let toPercent value = sprintf ("%f") value + "%"
                        let label = sprintf "%.4f" output
                      
                        yield Svg.text ([ "x" => "25px"; "y" => toPercent (a * 100.0f); "text-anchor" => "left";
                                                                                        "font-size" => "10"; 
                                                                                        "fill" => "#ffffff";
                                                                                        "pointer-events" => "none";]) label
                }

    let viewDnSLegendProperties paletteFile lifter (model : AdaptiveFalseColorsModel) = 
        UI.viewScalarMappingProperties paletteFile model |> UI.map lifter

    let viewScalarsLegendProperties paletteFile lifter (model : AdaptiveFalseColorsModel) = 
        UI.viewScalarMappingProperties paletteFile model |> UI.map lifter

    let viewDepthLegendProperties lifter (model : AdaptiveFalseColorsModel) = 
        UI.viewDepthLegendProperties  model |> UI.map lifter

   
