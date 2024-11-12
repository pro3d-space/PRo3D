namespace PRo3D.Core.Surface

open System
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.SceneGraph
open Aardvark.Data.Opc
open Aardvark.VRVis

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

module SurfaceProperties =        

    [<Literal>]
    let ramp = "Ramp"
    [<Literal>]
    let passthrough = "Passthrough"

    type Action =
        | SetFillMode    of FillMode
        | SetCullMode    of CullMode
        | SetName        of string
        | ToggleVisible 
        | ToggleIsActive
        | SetQuality     of Numeric.Action
        | SetTriangleSize of Numeric.Action
        | SetPriority    of Numeric.Action
        //| SetScaling     of Numeric.Action
        | SetScalarMap   of Option<ScalarLayer>

        | SetPrimaryTexture of Option<TextureLayer>
        | SetSecondaryTexture of Option<TextureLayer>
        | SetTransferFunctionMode of Option<string>
        | SetTFMin of float
        | SetTFMax of float
        | SetColorMappingName of Option<string>
        | SetTextureCombiner of TextureCombiner
        | SetBlendFactor of float
        | CountourAppMessage of ContourLineApp.Action

        | SetHomePosition //of Guid //of Option<CameraView>
        | ToggleFilterByDistance //of Guid //of Option<CameraView>
        | SetFilterDistance of Numeric.Action //of Guid //of Option<CameraView>
        | ToggleHighlightSelected
        | ToggleHighlightAlways

    let update (model : Surface) (act : Action) =
        match act with
        | CountourAppMessage act -> 
            { model with contourModel = ContourLineApp.update model.contourModel act }
        | SetFillMode mode ->
            { model with fillMode = mode }
        | SetCullMode mode ->
            { model with cullMode = mode }
        | SetName s ->
            { model with name = s }
        | ToggleVisible ->
            { model with isVisible = not model.isVisible }
        | ToggleIsActive ->
            { model with isActive = not model.isActive }
        | SetTriangleSize a ->
            { model with triangleSize = Numeric.update model.triangleSize a}
        | ToggleFilterByDistance ->
            { model with filterByDistance = not model.filterByDistance }
        | SetFilterDistance a ->
            { model with filterDistance = Numeric.update model.filterDistance a}
        | SetQuality a ->
            { model with quality = Numeric.update model.quality a }
        | SetPriority a ->
            { model with priority = Numeric.update model.priority a }
        //| SetScaling a ->
        //    { model with scaling = Numeric.update model.scaling a }
        | SetScalarMap a -> 
            match a with
            | Some s -> 
                let scs = model.scalarLayers |> HashMap.alter s.index (Option.map(fun _ -> s))
                Log.error "[SurfaceProperties] %A" scs
                { model with selectedScalar = Some s} |> Console.print //; scalarLayers = scs 
            | None -> { model with selectedScalar = None }

        | SetPrimaryTexture texture ->                
            { model with primaryTexture = texture } |> Console.print
        | SetSecondaryTexture texture ->                
            { model with secondaryTexture = texture } |> Console.print

        | SetTransferFunctionMode (Some name) -> 
            match name with
            | _ when name = ramp -> 
                match model.transferFunction with
                | { tf = ColorMaps.TF.Ramp(_,_,_); textureCombiner = _ } -> model
                | _ -> { model with transferFunction = { model.transferFunction with tf = ColorMaps.TF.Ramp(0.0, 1.0, ColorMaps.colorMaps |> Map.toSeq |> Seq.head |> fst); } }
            | _ when name = passthrough-> 
                { model with transferFunction = { model.transferFunction with tf = ColorMaps.TF.Passthrough }}
            | _ -> 
                Log.warn "unkonwn tf mode: %s" name
                model
        | SetTransferFunctionMode None -> model

        | SetTFMin min -> 
            { model with transferFunction = { model.transferFunction with tf = ColorMaps.TF.trySetMin min model.transferFunction.tf; } }
        | SetTFMax max -> 
            { model with transferFunction = { model.transferFunction with tf = ColorMaps.TF.trySetMax max model.transferFunction.tf; } }

        | SetColorMappingName None -> model
        | SetColorMappingName (Some name) -> 
            { model with transferFunction = { model.transferFunction with tf = ColorMaps.TF.trySetName name model.transferFunction.tf }  }
        | SetTextureCombiner c -> 
            { model with transferFunction = { model.transferFunction with textureCombiner = c } }
        | SetBlendFactor f -> 
            { model with transferFunction = { model.transferFunction with blendFactor = f }}


        | SetHomePosition -> model
        | ToggleHighlightSelected ->
            { model with highlightSelected = not model.highlightSelected }
        | ToggleHighlightAlways ->
            { model with highlightAlways = not model.highlightAlways }
            

    let getTextures (layers : seq<AttributeLayer>) =
        layers 
        |> Seq.choose (fun x -> match x with | TextureLayer l -> Some l | _ -> None) 
        |> Seq.mapi(fun i x -> { x with index = i}) 
        |> IndexList.ofSeq

    let getScalars (layers : seq<AttributeLayer>) =
        layers 
        |> Seq.choose (fun x -> match x with | ScalarLayer l -> Some l | _ -> None) 
        |> Seq.mapi(fun i x -> { x with index = i}) 
        |> IndexList.ofSeq

    let getScalarsHmap (layers : seq<AttributeLayer>) =
        layers 
        |> Seq.choose (fun x -> match x with | ScalarLayer l -> Some l | _ -> None) 
        |> Seq.mapi(fun i x -> { x with index = i}) 
        |> Seq.map(fun x -> x.index, x)
        |> HashMap.ofSeq

    let mapTolist (input : amap<_,'a>) : alist<'a> = 
        input |> AMap.toASet |> AList.ofASet |> AList.map snd    
    
    let scalarLayerList (m:AdaptiveSurface) = 
        (m.scalarLayers |> mapTolist)

    //let getSelectedScalar (layer:aval<Option<MScalarLayer>>) = //: Option<ScalarLayer> =
    //    adaptive {
    //            let! layer = layer
    //            match layer with
    //             | Some l ->  let! current = l.Current
    //                          return SetScalarMap (Some current)
    //             | _-> return SetScalarMap None
    //        } |> AVal.force

          
    let view (model : AdaptiveSurface) =        
      require GuiEx.semui (
        Incremental.table (AttributeMap.ofList [clazz "ui celled striped inverted table unstackable"]) <|
            alist {
                // Html.row "Path:"        [Incremental.text (model.importPath |> AVal.map (fun x -> sprintf "%A" x ))]                
                yield Html.row "Name:"        [Html.SemUi.textBox model.name SetName ]
                yield Html.row "Visible:"     [GuiEx.iconCheckBox model.isVisible ToggleVisible ]
                yield Html.row "Active:"      [GuiEx.iconCheckBox model.isActive ToggleIsActive ]
                yield Html.row "Highlight Selected:"   [GuiEx.iconCheckBox model.highlightSelected ToggleHighlightSelected ]
                yield Html.row "Highlight Always:"     [GuiEx.iconCheckBox model.highlightAlways ToggleHighlightAlways ]
                yield Html.row "Priority:"    [Numeric.view' [NumericInputType.InputBox] model.priority |> UI.map SetPriority ]       
                yield Html.row "Quality:"     [Numeric.view' [NumericInputType.Slider]   model.quality  |> UI.map SetQuality ]
                yield Html.row "TriangleFilter:" [Numeric.view' [NumericInputType.InputBox]   model.triangleSize  |> UI.map SetTriangleSize ]
                yield Html.row "DistanceFilter:" [GuiEx.iconCheckBox model.filterByDistance ToggleFilterByDistance ]
                yield Html.row "FilterDistance:" [Numeric.view' [NumericInputType.InputBox]   model.filterDistance  |> UI.map SetFilterDistance ]
                // Html.row "Scale:"       [Numeric.view' [NumericInputType.InputBox]   model.scaling  |> UI.map SetScaling ]
                yield Html.row "Fillmode:"    [Html.SemUi.dropDown model.fillMode SetFillMode]                
                yield Html.row "Scalars:"     [UI.dropDown'' (model |> scalarLayerList)  (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption model.selectedScalar)  (fun x -> SetScalarMap (x |> Option.map(fun y -> y.Current |> AVal.force)))   (fun x -> x.label |> AVal.force)]
                // Html.row "Scalars:"     [UI.dropDown'' (model |> scalarLayerList)  model.selectedScalar   (fun x -> SetScalarMap (x |> Option.map(fun y -> y.Current ))) (fun x -> x.label |> AVal.force)]
                 
                yield Html.row "Cull Faces:"  [Html.SemUi.dropDown model.cullMode SetCullMode]
                yield Html.row "Set Homeposition:"  [button [clazz "ui button tiny"; onClick (fun _ -> SetHomePosition )] []] //[text "DiscoverOpcs" ]  

                yield Html.row "OPCx Info path:"    [Incremental.text (model.opcxPath |> AVal.map (function None -> "none" | Some p -> p))]
                yield Html.row "Primary Texture:"   [UI.dropDown'' model.textureLayers model.primaryTexture  (fun x -> SetPrimaryTexture x) (fun x -> x.label)]
                
                let tfToName (tf : ColorMaps.TF) =
                    match tf with
                    | ColorMaps.TF.Ramp(_,_,_) -> ramp
                    | ColorMaps.TF.Passthrough -> passthrough
                
                
                yield Html.row "Transfer Function" [ div [] [UI.dropDown'' (AList.ofList [ramp; passthrough]) (model.transferFunction |> AVal.map (fun tf -> Some (tfToName tf.tf))) SetTransferFunctionMode (fun a -> a)]]
                yield Html.row "Secondary Texture:"   [UI.dropDown'' model.textureLayers model.secondaryTexture (fun x -> SetSecondaryTexture x) (fun x -> x.label)]
                
                let! tf = model.transferFunction

                yield Html.row "Texture Combiner" [Html.SemUi.dropDown (AVal.constant tf.textureCombiner) SetTextureCombiner]  
                        
                match tf.textureCombiner with
                | TextureCombiner.Blend -> 
                    yield 
                        Html.row "Blend Factor" [Aardvark.UI.NoSemUi.numeric { min = 0.0; max = 1.0; smallStep = 0.01; largeStep = 0.1 } "range" AttributeMap.empty (
                                tf.blendFactor |> AVal.constant 
                            ) SetBlendFactor
                        ]
                | _ -> 
                    ()

                match tf.tf with
                | ColorMaps.TF.Ramp(min,max,name) ->
                    yield Html.row "Min" [
                        yield Aardvark.UI.NoSemUi.numeric { min = 0.0; max = 1.0; smallStep = 0.01; largeStep = 0.1 } "text" AttributeMap.empty (
                            match tf.tf with 
                            | ColorMaps.TF.Ramp(min, max, name) -> min |> AVal.constant
                            | _ -> 0.0 |> AVal.constant
                        ) SetTFMin
                    ]
                    yield Html.row "Max" [
                        yield Aardvark.UI.NoSemUi.numeric { min = 0.0; max = 1.0; smallStep = 0.01; largeStep = 0.1 } "text" AttributeMap.empty (
                            match tf.tf with 
                            | ColorMaps.TF.Ramp(min, max, name) -> max |> AVal.constant
                            | _ -> 0.0 |> AVal.constant
                        ) SetTFMax
                    ]
                    let toColorMapName (tf : ColorMaps.TF) =
                        match tf with
                        | ColorMaps.TF.Ramp(_,_,s) -> Some s
                        | _ -> None

                    yield Html.row "Color Map" [
                        yield UI.dropDown'' (ColorMaps.colorMaps |> Map.toSeq |> Seq.map fst |> AList.ofSeq) (toColorMapName tf.tf |> AVal.constant) (fun x -> SetColorMappingName x) (fun s -> s)
                    ]
                | _ -> ()

                    
            }
      )

module ColorCorrectionProperties =    

    type Action =
        | SetContrast    of Numeric.Action
        | UseContrast    
        | SetBrightness  of Numeric.Action
        | UseBrightness  
        | SetGamma       of Numeric.Action
        | UseGamma       
        | SetColor       of ColorPicker.Action
        | UseColor
        | UseGrayScale

    let update (model : ColorCorrection) (act : Action) =        
      match act with
        | SetContrast c ->
            { model with contrast = Numeric.update model.contrast c }
        | UseContrast  ->
            { model with useContrast = (not model.useContrast) }
        | SetBrightness b ->
            { model with brightness = Numeric.update model.brightness b }
        | UseBrightness  ->
            { model with useBrightn = (not model.useBrightn) }
        | SetGamma g ->
            { model with gamma = Numeric.update model.gamma g }
        | UseGamma  ->
            { model with useGamma = (not model.useGamma) }
        | SetColor a ->
            { model with color = ColorPicker.update model.color a }
        | UseColor  ->
            { model with useColor = (not model.useColor) }
        | UseGrayScale  ->
            { model with useGrayscale = (not model.useGrayscale) }

    let view (paletteFile : string) (model : AdaptiveColorCorrection) =        
      require GuiEx.semui (
        Html.table [  
          Html.row ""                     []
          Html.row "use color:"           [GuiEx.iconCheckBox model.useColor UseColor ]
          Html.row "color:"               [ColorPicker.viewAdvanced ColorPicker.defaultPalette paletteFile "pro3d" true model.color |> UI.map SetColor ]
          Html.row "grayscale:"           [GuiEx.iconCheckBox model.useGrayscale UseGrayScale ]
          Html.row "use brightness:"      [GuiEx.iconCheckBox model.useBrightn UseBrightness ]
          Html.row "set brightness:"      [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.brightness  |> UI.map SetBrightness ] 
          Html.row "use contrast:"        [GuiEx.iconCheckBox model.useContrast UseContrast ] 
          Html.row "set contrast:"        [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.contrast  |> UI.map SetContrast ] 
          Html.row "use gamma:"           [GuiEx.iconCheckBox model.useGamma UseGamma ]
          Html.row "set gamma:"           [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.gamma  |> UI.map SetGamma ] 
        ]
      )


module RadiometryProperties =    

    type Action =
        | UseRadiometry
        | SetMinR    of Numeric.Action
        | SetMaxR    of Numeric.Action
        | SetMinG    of Numeric.Action
        | SetMaxG    of Numeric.Action
        | SetMinB    of Numeric.Action
        | SetMaxB    of Numeric.Action

    let update (model : Radiometry) (act : Action) =        
      match act with
        | UseRadiometry  ->
            { model with useRadiometry = (not model.useRadiometry) }
        | SetMinR minr ->
            { model with minR = Numeric.update model.minR minr }
        | SetMaxR maxr ->
            { model with maxR = Numeric.update model.maxR maxr }
        | SetMinG ming ->
            { model with minG = Numeric.update model.minG ming }
        | SetMaxG maxg ->
            { model with maxG = Numeric.update model.maxG maxg }
        | SetMinB minb ->
            { model with minB = Numeric.update model.minB minb }
        | SetMaxB maxb ->
            { model with maxB = Numeric.update model.maxB maxb }

    let view (model : AdaptiveRadiometry) =        
      require GuiEx.semui (
        Html.table [        
          Html.row "use radiometry:"      [GuiEx.iconCheckBox model.useRadiometry UseRadiometry ]
          Html.row "set min R:"      [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.minR  |> UI.map SetMinR ]
          Html.row "set max R:"      [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.maxR  |> UI.map SetMaxR ]
          Html.row "set min G:"      [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.minG  |> UI.map SetMinG ]
          Html.row "set max G:"      [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.maxG  |> UI.map SetMaxG ]
          Html.row "set min B:"      [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.minB  |> UI.map SetMinB ]
          Html.row "set max B:"      [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.maxB  |> UI.map SetMaxB ]
        ]
      )