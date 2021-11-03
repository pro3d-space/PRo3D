namespace PRo3D.SimulatedViews

open Aardvark.Base
open Adaptify
open Aardvark.UI

type ImageFormat =
    | JPEG = 0
    | PNG = 1


[<ModelType>]
type ScreenshotApp = {
    width       : NumericInput
    height      : NumericInput
    samples     : string
    url         : string
    imageFormat : ImageFormat
    outputPath  : string
}

type ScreenshotAppAction = 
    | SetWidth of Numeric.Action
    | SetHeight of Numeric.Action
    | CreateScreenshot
    | SetImageFormat of ImageFormat
    | OpenFolder