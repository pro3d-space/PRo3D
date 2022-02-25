namespace PRo3D.Provenance

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI.Primitives

open FSharp.Data.Adaptive

open Adaptify

open PRo3D.Viewer
open PRo3D.Provenance.Abstraction

type Action =
    | AppAction             of ViewerAction
    | ProvenanceAction      of ProvenanceAction    
    | UpdateConfig          of DockConfig
    | NodeClick             of NodeId            
    | KeyDown               of Keys
    | KeyUp                 of Keys
    | Click
    | RenderControlResized  of V2i

[<ModelType>]
type InnerModel = {
    current : PRo3D.Viewer.Model          // The current state of the inner application
    //preview : Preview option    // A preview or temporary state
    //output : AppModel           // The state that is displayed (may be a preview)
}

[<ModelType>]
type Model = {
    inner               : InnerModel
    //view : View
    dockConfig          : DockConfig    
    provenance          : Provenance
    renderControlSize   : V2i
}

module AppModel =

    let setCamera (camera : CameraView) (model : PRo3D.Viewer.Model) =
    
        let c = { model.navigation.camera with view = CameraView.restore camera }

        { model with navigation = { model.navigation with camera = c }}

    let getCamera (model : PRo3D.Viewer.Model) =
        model.navigation.camera.view |> CameraView.create

    let setRendering (rendering  : RenderingParams) (model : PRo3D.Viewer.Model) =
        { model with scene = { model.scene with config = rendering } }

    let getRendering (model : AppModel) =
        model.scene.config

    let setSurfaceParams (surfaces : SurfaceParams) (model : PRo3D.Viewer.Model) =
        let s = SurfaceParams.restore model.scene.surfacesModel.surfaces surfaces
        { model with scene = { model.scene with surfacesModel = { model.scene.surfacesModel with surfaces = s } } }

    let getSurfaceParams (model : PRo3D.Viewer.Model) : SurfaceParams =
        SurfaceParams.create model.scene.surfacesModel.surfaces

    //let setPresentation (presentation : PresentationParams) (model : PRo3D.Viewer.Model) =
    //    model 
    //    |> setRendering presentation.rendering
    //    |> setSurfaceParams presentation.surfaces

    //let getPresentation (model : PRo3D.Viewer.Model) =
    //    { rendering = getRendering model 
    //      surfaces = getSurfaceParams model }

    let setViewParams (view : ViewParams) (model : PRo3D.Viewer.Model) =
        model 
        |> setCamera view.camera
        //|> setPresentation view.presentation

    let getViewParams (model : PRo3D.Viewer.Model) =
        { 
            camera = model |> getCamera
         //   presentation = model |> getPresentation 
        }    

    let getSceneHit (model : PRo3D.Viewer.Model) =
        let t = getTargeting model
        t.lastHit

    let getFrustum (model : PRo3D.Viewer.Model) =
        model.frustum

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module InnerModel =

    // Gets the active encapsulated model according to the given mode.
    // E.g. when a preview of a camera position is ongoing, the preview model is returned
    // when calling this function for mode = View
    let getActiveModel (mode : Mode) (model : InnerModel) =
        model.preview |> Option.filter (Preview.is mode)
                      |> Option.map (fun p -> p.model)
                      |> Option.defaultValue model.current

    let getCurrentModel (model : InnerModel) =
        model.current

    let setCamera (camera : CameraView) (model : InnerModel) =
        { model with current = model.current |> AppModel.setCamera camera }

    let getCamera (model : InnerModel) =
        model.current |> AppModel.getCamera

    let setRendering (rendering  : RenderingParams) (model : InnerModel) =
        { model with current = model.current |> AppModel.setRendering rendering }

    let getRendering (model : InnerModel) =
        model.current |> AppModel.getRendering

    let setSurfaceParams (surfaces  : SurfaceParams) (model : InnerModel) =
        { model with current = model.current |> AppModel.setSurfaceParams surfaces }

    let getSurfaceParams (model : InnerModel) =
        model.current |> AppModel.getSurfaceParams

    let setPresentation (presentation : PresentationParams) (model : InnerModel) =
        { model with current = model.current |> AppModel.setPresentation presentation }

    let getPresentation (model : InnerModel) =
        model.current |> AppModel.getPresentation

    let setViewParams (view : ViewParams) (model : InnerModel) =
        { model with current = model.current |> AppModel.setViewParams view }

    let setActiveViewParams (view : ViewParams) (model : InnerModel) =
        match model.preview with
            | Some p when p |> Preview.is View ->
                { model with preview = Some { p with model = p.model |> AppModel.setViewParams view } }
            | _ ->
                { model with current = model.current |> AppModel.setViewParams view }

    let getViewParams (model : InnerModel) =
        model.current |> AppModel.getViewParams

    let getActiveViewParams (model : InnerModel) =
        model.preview |> Option.filter (Preview.is View)
                      |> Option.map (fun p -> p.model)
                      |> Option.defaultValue model.current
                      |> AppModel.getViewParams

    let getActivePresentation (model : InnerModel) =
        model |> getActiveViewParams
              |> fun v -> v.presentation

    let setTargeting (enabled : bool) (model : InnerModel) =
        { model with current = model.current |> AppModel.setTargeting enabled }

    let getTargeting (model : InnerModel) =
        model.current |> AppModel.getTargeting

    let getSceneHit (model : InnerModel) =
        model.current |> AppModel.getSceneHit

    let getFrustum (model : InnerModel) =
        model.current |> AppModel.getFrustum

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Model =

    let getActiveModel (mode : Mode) (model : Model) =
        model.inner |> InnerModel.getActiveModel mode

    let getCurrentModel (model : Model) =
        model.inner |> InnerModel.getCurrentModel

    let setCamera (camera : CameraView) (model : Model) =
        { model with inner = model.inner |> InnerModel.setCamera camera }

    let getCamera (model : Model) =
        model.inner |> InnerModel.getCamera

    let setRendering (rendering  : RenderingParams) (model : Model) =
        { model with inner = model.inner |> InnerModel.setRendering rendering }

    let getRendering (model : Model) =
        model.inner |> InnerModel.getRendering

    let setSurfaceParams (surfaces  : SurfaceParams) (model : Model) =
        { model with inner = model.inner |> InnerModel.setSurfaceParams surfaces }

    let getSurfaceParams (model : Model) =
        model.inner |> InnerModel.getSurfaceParams

    //let setPresentation (presentation : PresentationParams) (model : Model) =
    //    { model with inner = model.inner |> InnerModel.setPresentation presentation }

    //let getPresentation (model : Model) =
    //    model.inner |> InnerModel.getPresentation

    let getActivePresentation (model : Model) =
        model.inner |> InnerModel.getActivePresentation

    let setViewParams (view : ViewParams) (model : Model) =
        { model with inner = model.inner |> InnerModel.setViewParams view }

    let setActiveViewParams (view : ViewParams) (model : Model) =
        { model with inner = model.inner |> InnerModel.setActiveViewParams view }

    let getViewParams (model : Model) =
        model.inner |> InnerModel.getViewParams

    let getActiveViewParams (model : Model) =
        model.inner |> InnerModel.getActiveViewParams

    let setTargeting (enabled : bool) (model : Model) =
        { model with inner = model.inner |> InnerModel.setTargeting enabled }

    let getTargeting (model : Model) =
        model.inner |> InnerModel.getTargeting

    let getSceneHit (model : Model) =
        model.inner |> InnerModel.getSceneHit

    let getFrustum (model : Model) =
        model.inner |> InnerModel.getFrustum

    //let isAnimating (model : Model) =
    //    model.view.animation |> Animation.isAnimating

    //let isPreview (model : Model) =
    //    model.inner.preview |> Option.isSome

    //let isPreviewMode (mode : Mode) (model : Model) =
    //    model.inner.preview |> Option.exists (Preview.is mode)