namespace PRo3D.Viewer

open PRo3D.Core
open PRo3D.Core.Rover3DModel
open FSharp.Data.Adaptive
open Aardvark.SceneGraph
open Aardvark.Rendering
open Aardvark.Base
open Aardvark.UI

module Rover3DApp = 
    
    let update (model : Rover3DModel) (action : Rover3DAction) : Rover3DModel = 
        match action with
        | SetRoverTrafo t -> 
            { model with trafo = Some(t) }
        | RemoveRover -> 
            { model with trafo = None }
        | SetRoverPath newPath -> 
            { model with path = newPath }
    
    let viewRover (rover3DModel : AdaptiveRover3DModel) = 
        let roverSGModel = Sg.loadRoverModel rover3DModel.path
        let visible = 
            rover3DModel.trafo
            |> AVal.map(fun t -> t.IsSome)

        let trafo = 
            rover3DModel.trafo
            |> AVal.map(fun t -> 
                t |> Option.defaultValue Trafo3d.Identity)        
        
        let rM = 
            Sg.UniformApplicator(Sg.defaultMaterial, roverSGModel)
            |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.Colors C4b.White
            |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.DiffuseColorTexture V2f.Zero  
                         
            |> SgFSharp.Sg.shader {
                do! Shader.trafo
                do! Shader.shade
            }
            |> Sg.noEvents
        
        rM  
        |> Sg.trafo trafo
        //|> Sg.trafo translationTrafo
        |> Sg.uniform "DepthOffset" (AVal.constant 0.0000000001)
        |> Sg.blendMode (AVal.constant BlendMode.None)
        |> Sg.effect [
            toEffect Aardvark.UI.Trafos.Shader.stableTrafo
            toEffect PRo3D.Base.Shader.DepthOffset.depthOffsetFS
        ]            
        |> Sg.onOff visible