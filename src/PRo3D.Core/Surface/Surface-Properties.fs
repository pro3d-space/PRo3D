namespace PRo3D.Core.Surface

open System
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

module SurfaceProperties =        

    type Action =
        | SetFillMode    of FillMode
        | SetCullMode    of CullMode
        | SetName        of string
        | ToggleVisible 
        | ToggleIsActive
        | SetQuality     of Numeric.Action
        | SetTriangleSize of Numeric.Action
        | SetPriority    of Numeric.Action
        | SetScaling     of Numeric.Action
        | SetScalarMap   of Option<ScalarLayer>
        | SetTexturesMap of Option<TextureLayer>
        | SetHomePosition //of Guid //of Option<CameraView>

    let update (model : Surface) (act : Action) =
        match act with
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
        | SetQuality a ->
            { model with quality = Numeric.update model.quality a }
        | SetPriority a ->
            { model with priority = Numeric.update model.priority a }
        | SetScaling a ->
            { model with scaling = Numeric.update model.scaling a }
        | SetScalarMap a -> 
            match a with
            | Some s -> 
                let scs = model.scalarLayers |> HashMap.alter s.index (Option.map(fun _ -> s))
                Log.error "[SurfaceProperties] %A" scs
                { model with selectedScalar = Some s} |> Console.print //; scalarLayers = scs 
            | None -> { model with selectedScalar = None }
        | SetTexturesMap a ->                
            { model with selectedTexture = a } |> Console.print
        | SetHomePosition -> model
            

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
        Html.table [                                            
          // Html.row "Path:"        [Incremental.text (model.importPath |> AVal.map (fun x -> sprintf "%A" x ))]                
          Html.row "Name:"        [Html.SemUi.textBox model.name SetName ]
          Html.row "Visible:"     [GuiEx.iconCheckBox model.isVisible ToggleVisible ]
          Html.row "Active:"      [GuiEx.iconCheckBox model.isActive ToggleIsActive ]
          Html.row "Priority:"    [Numeric.view' [NumericInputType.InputBox] model.priority |> UI.map SetPriority ]       
          Html.row "Quality:"     [Numeric.view' [NumericInputType.Slider]   model.quality  |> UI.map SetQuality ]
          Html.row "TriangleFilter:" [Numeric.view' [NumericInputType.InputBox]   model.triangleSize  |> UI.map SetTriangleSize ]
          Html.row "Scale:"       [Numeric.view' [NumericInputType.InputBox]   model.scaling  |> UI.map SetScaling ]
          Html.row "Fillmode:"    [Html.SemUi.dropDown model.fillMode SetFillMode]                
          Html.row "Scalars:"     [UI.dropDown'' (model |> scalarLayerList)  (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption model.selectedScalar)  (fun x -> SetScalarMap (x |> Option.map(fun y -> y.Current |> AVal.force)))   (fun x -> x.label |> AVal.force)]
          //Html.row "Scalars:"     [UI.dropDown'' (model |> scalarLayerList)  model.selectedScalar   (fun x -> SetScalarMap (x |> Option.map(fun y -> y.Current ))) (fun x -> x.label |> AVal.force)]
          Html.row "Textures:"    [UI.dropDown'' model.textureLayers model.selectedTexture  (fun x -> SetTexturesMap x) (fun x -> x.label)]
          Html.row "Cull Faces:"  [Html.SemUi.dropDown model.cullMode SetCullMode]
          Html.row "Set Homeposition:"  [button [clazz "ui button tiny"; onClick (fun _ -> SetHomePosition )][]] //[text "DiscoverOpcs" ]  
        ]
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
          Html.row "use color:"           [GuiEx.iconCheckBox model.useColor UseColor ]
          Html.row "color:"               [ColorPicker.viewAdvanced ColorPicker.defaultPalette paletteFile "pro3d" model.color |> UI.map SetColor ]
          Html.row "grayscale:"           [GuiEx.iconCheckBox model.useGrayscale UseGrayScale ]
          Html.row "use brightness:"      [GuiEx.iconCheckBox model.useBrightn UseBrightness ]
          Html.row "set brightness:"      [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.brightness  |> UI.map SetBrightness ] 
          Html.row "use contrast:"        [GuiEx.iconCheckBox model.useContrast UseContrast ] 
          Html.row "set contrast:"        [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.contrast  |> UI.map SetContrast ] 
          Html.row "use gamma:"           [GuiEx.iconCheckBox model.useGamma UseGamma ]
          Html.row "set gamma:"           [Numeric.view' [NumericInputType.Slider; NumericInputType.InputBox]   model.gamma  |> UI.map SetGamma ] 
        ]
      )