namespace PRo3D.SimulatedViews

open Aardvark.Base
open Adaptify
open Aardvark.UI

open Chiron
open PRo3D.Base

type ImageFormat =
    | JPEG = 0
    | PNG = 1


[<ModelType>]
type ScreenshotModel = {
    version         : int
    width           : NumericInput
    height          : NumericInput
    backgroundColor : ColorInput
    imageFormat     : ImageFormat
    //outputPath      : string
    //url             : string
    //samples         : int
}

module ScreenshotModel =
    let current = 0 //20211611 ... added traverse and sequenced bookmarks and comparison app
    
    let private initNumeric = 
        {
            min = 1.0
            max = 16000.0
            step = 1.0
            value = 500.0
            format = "{0:0}"
        }

    let initial =
        {
            version         = current
            width           = initNumeric
            height          = initNumeric
            imageFormat     = ImageFormat.PNG
            backgroundColor = { ColorInput.c = C4b.White }            
        }    

    let read0  =
        json {
            let! width           = Json.readWith Ext.fromJson<NumericInput,Ext> "width"
            let! height          = Json.readWith Ext.fromJson<NumericInput,Ext> "height"
            let! imageFormat     = Json.read "imageFormat"
            let! backgroundColor = Json.readWith Ext.fromJson<ColorInput,Ext> "backgroundColor"
            
            return { 
                version         = current
                width           = width
                height          = height
                imageFormat     = imageFormat |> enum<ImageFormat>
                backgroundColor = backgroundColor                
            }            
        }
        
        
type ScreenshotModel with
    static member FromJson (_ : ScreenshotModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ScreenshotModel.read0
            | _ ->
                return! v 
                |> sprintf "don't know version %A  of ScreenshotModel"
                |> Json.error
        }
    static member ToJson (x : ScreenshotModel) =
        json {
            do! Json.write "version" x.version
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "width"  x.width
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "height" x.height
            do! Json.write "imageFormat" (x.imageFormat |> int)
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>)   "backgroundColor" x.backgroundColor
        }

type ScreenshotAction = 
    | SetWidth           of Numeric.Action
    | SetHeight          of Numeric.Action
    | SetBackgroundColor of ColorPicker.Action
    | CreateScreenshot
    | SetImageFormat     of ImageFormat
    | OpenFolder