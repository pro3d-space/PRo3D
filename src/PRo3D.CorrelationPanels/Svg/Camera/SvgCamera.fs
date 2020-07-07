namespace Svgplus
  open Svgplus.CameraType
  open Aardvark.Base
  open Aardvark.Application
  open Aardvark.Base.Incremental
  open Aardvark.UI
  

  [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
  module SvgCamera =

    type Action =
      | MouseDown     of (MouseButtons * V2i)
      | MouseUp       of (MouseButtons * V2i)
      | MouseMove     of V2d


     
    let init : SvgCamera =
      {
        zoomFactorX         = Zoom.init 1.0
        zoomFactorY         = Zoom.init 1.0
        dragging            = false
        zooming             = false
        lockAspectRatio     = true
        lastMousePos        = V2d(0.0)
        offset              = V2d(0.0)
        fontSize            = FontSize.init 10
        transformedMousePos = V2d(0.0)
      }

    let mousePosition (model : SvgCamera) (origMousePosition : V2d) =
      let _x = origMousePosition.X / model.zoomFactorX.factor
      let _y = origMousePosition.Y / model.zoomFactorY.factor
      V2d(_x, _y)

    let zoomIn (model : SvgCamera) (mousepos : V2d) = 
      let diff = mousepos - model.lastMousePos
      let factorY = diff.OY.Length * 0.01 //TODO hardcoded zoom speed
      let factorX = diff.XO.Length * 0.01 //TODO hardcoded zoom speed
      let signumY =
        match diff.Y with
          | a when a <= 0.0  -> -1.0
          | b when b >  0.0  -> 1.0
          | _                -> 1.0
      let signumX =
        match diff.X with
          | a when a <= 0.0  -> -1.0
          | b when b >  0.0  -> 1.0
          | _                -> 1.0
      let deltaZoomX = factorX * signumX
      let deltaZoomY = factorY * signumY
      let zoomX = (model.zoomFactorX + deltaZoomX)
      let zoomY = (model.zoomFactorY + deltaZoomY)
      let zoom = (zoomX + zoomY) * 0.5

      let (zoomX, zoomY) = 
        match model.lockAspectRatio with
          | false -> (zoomX, zoomY)
          | true -> (zoom, zoom)
      let _fontSize = 
        match zoom with
          | z when z.factor < 1.0 -> 
            FontSize.defaultSize.fontSize + (int (System.Math.Round ((1.0 - z.factor) * 10.0)))
          | z when z.factor > 1.0 -> 
            FontSize.defaultSize.fontSize - int (System.Math.Round z.factor)
          | _ -> FontSize.defaultSize.fontSize

      {model with 
        zoomFactorX = zoomX
        zoomFactorY = zoomY
        fontSize = FontSize.init _fontSize
        lastMousePos = mousepos
      }

    let update (model : SvgCamera) (action : Action) =
      match action with
        | MouseDown (b,p) ->
          let p = V2d p
          match b with
            | MouseButtons.Left   -> {model with  dragging = true
                                                  lastMousePos = p}
            | MouseButtons.Middle -> model
            | MouseButtons.Right  -> {model with  zooming      = true
                                                  lastMousePos = p}
            | _ -> model
          
        | MouseUp (b,p) -> 
          let p = V2d p
          match b with
            | MouseButtons.Left   -> {model with  dragging = false
                                                  lastMousePos = p}
            | MouseButtons.Middle ->  model
            | MouseButtons.Right  -> {model with  zooming      = false
                                                  lastMousePos = p}
            | _ -> model
        | MouseMove p ->
          let _model = 
            {model with
              transformedMousePos = mousePosition model p
            }

          match model.dragging, model.zooming with //TODO refactor
            | true, false ->
              let _offset = model.offset + V2d(p - model.lastMousePos)
              {_model with 
                lastMousePos = p
                offset       = _offset
                
              }            

            | false, true -> 
              zoomIn _model p

            | false, false -> 
              _model
            | true, true -> 
              {
                _model with dragging = false
                            zooming  = false
              }

    let transformationAttributes (model : MSvgCamera) =
      let atts =
        amap {
          let! zfx = model.zoomFactorX
          let! zfy = model.zoomFactorY
          let! offset = model.offset
          let! fs = model.fontSize
          let transform = sprintf 
                              "scale(%f,%f) translate(%f %f)" 
                              zfx.factor 
                              zfy.factor 
                              offset.X 
                              offset.Y
          yield attribute "transform" transform
          yield attribute "font-size" (sprintf "%ipx" fs.fontSize)
        }
      atts