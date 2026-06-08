namespace PRo3D.Viewer

open PRo3D.Core
open PRo3D.Core.Rover3DModel
open FSharp.Data.Adaptive
open Aardvark.SceneGraph
open Aardvark.Rendering
open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open Aardvark.UI.Primitives

module Rover3DApp = 
    //TODO: Start File Dialog to ask user to define RoverModel
    let getRoverIfNotDefined (model : Rover3DModel) =
        model
    
    let update (model : Rover3DModel) (action : Rover3DAction) : Rover3DModel = 
        match action with
        | SetRoverTrafo t -> 
            { model with trafo = Some(t); upVector = model.refSystem.up.value; lightDir = -model.refSystem.up.value}
            //|> showAfterUpdate
        | RemoveRover -> 
            { model with trafo = None }
        | SetRoverPath newPath -> 
            if newPath.IsEmptyOrNull() then model
            else { model with path = newPath.Head }
        | SetRoverPoints p -> 
            model.setPoint
            |> Option.map(fun pos -> 
                let forward = V3f((p - pos).Normalized)
                let upVector = model.refSystem.up.value
                let completeTrafo = TrafoHelper.initialPlacementTrafo' (V3f(pos)) forward (upVector.ToV3f())
                
                { model with trafo = Some(Trafo3d(completeTrafo)); setPoint = None; upVector = upVector; lightDir = -upVector})
                //|> showAfterUpdate)
            |> Option.defaultValue { model with setPoint = Some(p)}

    let jsSetRoverModelFileDialog = 
        "top.aardvark.dialog.showOpenDialog({ title: 'SelectRoverModel', filters: [{ name: 'Graphics Library Transmission Format (*.gltf)', extensions: ['gltf']},], properties: ['openFile']}).then(result => {aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

    let view = 
        require GuiEx.semui (
            div [                
            ] [
                button [clazz "ui icon button"; onClick (fun _ -> RemoveRover)] [
                    i [clazz "car icon red"] [] ] 
                button [ 
                    clazz "ui icon button"; 
                    Dialogs.onChooseFiles SetRoverPath;
                    clientEvent "onclick" jsSetRoverModelFileDialog;
                ] [ i [clazz "car icon green"] [] ]
            ]
        )
        
            

    let viewRover (runtime : IRuntime) (rover3DModel : AdaptiveRover3DModel) = 
        let trafo = TrafoHelper.usableTrafo rover3DModel.trafo
        
        let roverScene = 
            Sg.loadRoverModel rover3DModel.path
            |> Sg.trafo trafo

        let visible = 
            rover3DModel.trafo
            |> AVal.map(fun t -> t.IsSome)
    
        //let roverScene = rM |> Sg.trafo trafo

        let shadowMap = 
            roverScene |> Shadows.createShadowMap trafo runtime rover3DModel.upVector rover3DModel.lightDir

        let roverScene = 
            roverScene
            |> Sg.uniform "DepthOffset" (AVal.constant 0.0000000001)
            |> Sg.uniform "LightViewProjRover" (Shadows.lightViewProj trafo rover3DModel.upVector rover3DModel.lightDir)
            |> Sg.uniform "LightDirectionRover" rover3DModel.lightDir
            |> Sg.texture "ShadowTexture" shadowMap
            |> Sg.blendMode (AVal.constant BlendMode.None)
            //|> Sg.effect [
            //    toEffect Aardvark.UI.Trafos.Shader.stableTrafo
            //    toEffect PRo3D.Base.Shader.DepthOffset.depthOffsetFS
            //    toEffect Shader.lighting
            //]
            |> Sg.onOff visible

        roverScene, shadowMap