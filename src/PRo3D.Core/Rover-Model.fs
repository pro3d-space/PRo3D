namespace Pro3d.Core

open Adaptify
open System
open System.IO

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph
open Aardvark.Rendering
open Aardvark.UI

open Aardvark.Data.GLTF
open Aardvark.SceneGraph.Assimp


[<ModelType>]
type RoverModel = {
    path              : string
    roverTraverse     : Option<Guid>
    roverLocation     : V3d
    roverDirection    : V3d
}

module RoverModel =
    module Shader = 
        open FShade


    let initial = {
        path = @"D:\Temp\RoverTesting\Export_YoqSothoth\Perseverance_100.gltf"
        roverTraverse  = None
        roverLocation  = V3d.NaN
        roverDirection = V3d.IOO
    } 

    module Sg = 
        module Semantic =
            let RoughnessCoordinate = Symbol.Create "RoughnessCoordinate"
            let MetallicnessCoordinate = Symbol.Create "MetallicnessCoordinate"
            let EmissiveCoordinate = Symbol.Create "EmissiveCoordinate"
            let NormalCoordinate = DefaultSemantic.NormalMapCoordinates
            let Tangent = Symbol.Create "Tangent"
        
        let loadRoverModel (path : aval<string> )= 
            let roverFile = path.GetValue()

            let model = GLTF.tryLoad roverFile

            let meshSg (m : Mesh) =
                let fvc =
                    match m.Index with
                    | Some i -> i.Length
                    | None -> m.Positions.Length

                let mutable sg =
                    Sg.render m.Mode (DrawCallInfo(FaceVertexCount = fvc, InstanceCount = 1))
                    |> Sg.vertexAttribute' DefaultSemantic.Positions m.Positions

                match m.Index with
                | Some idx -> sg <- sg |> Sg.indexArray idx
                | None -> ()

                match m.Normals with
                | Some ns -> sg <- sg |> Sg.vertexAttribute' DefaultSemantic.Normals ns
                | None -> ()

                match m.Tangents with
                | Some ns -> sg <- sg |> Sg.vertexAttribute' Semantic.Tangent ns
                | None -> ()

                match m.Colors with
                | Some cs -> sg <- sg |> Sg.vertexAttribute' DefaultSemantic.Colors cs
                | None -> sg <- sg |> Sg.vertexAttribute' DefaultSemantic.Colors (Array.create m.Positions.Length C4b.White)

                for data, sems in m.TexCoords do
                    let view = BufferView.ofArray data
                    for sem in sems do
                        let semantic =
                            match sem with
                            | TextureSemantic.BaseColor -> DefaultSemantic.DiffuseColorCoordinates
                            | TextureSemantic.Roughness -> Semantic.RoughnessCoordinate
                            | TextureSemantic.Emissive -> Semantic.EmissiveCoordinate
                            | TextureSemantic.Metallicness -> Semantic.MetallicnessCoordinate
                            | TextureSemantic.Normal -> Semantic.NormalCoordinate

                        sg <- sg |> Sg.vertexBuffer semantic view

                let uniforms =
                    UniformProvider.ofList [
                        "HasNormals", AVal.constant (Option.isSome m.Normals) :> IAdaptiveValue
                        "HasTangents", AVal.constant (Option.isSome m.Tangents)
                        "HasColors", AVal.constant (Option.isSome m.Colors)
                    ]

                Sg.UniformApplicator(uniforms, sg) :> ISg

            model 
            |> Option.map (fun scene -> 

                let textures =
                        scene.ImageData |> Map.map (fun _ data ->
                            let texture = StreamTexture(fun () -> new MemoryStream(data.Data))
                            texture :> ITexture |> AVal.constant
                        )

                let meshes =
                    scene.Meshes |> Map.map (fun _ m -> meshSg m)

                let rec traverse (node : Node) : ISg =
                    let cs =
                        match node.Children with
                        | [] -> None
                        | _ -> node.Children |> Seq.map traverse |> SgFSharp.Sg.ofSeq |> Some

                    let ms =
                        node.Meshes |> List.choose (fun mi ->
                            match Map.tryFind mi.Mesh meshes with
                            | Some mesh ->
                                match mi.Material |> Option.bind (fun mid -> Map.tryFind mid scene.Materials) with
                                | Some mat ->
                                    let uniforms =
                                        let baseColorTexture =
                                            mat.BaseColorTexture 
                                            |> Option.bind (fun id -> Map.tryFind id textures)
                                            |> Option.defaultValue (NullTexture.Instance |> AVal.constant)
                                            
                                        let roughnessTexture =
                                            mat.RoughnessTexture 
                                            |> Option.bind (fun id -> Map.tryFind id textures)
                                            |> Option.defaultValue (NullTexture.Instance |> AVal.constant)
                                            
                                        let metallicnessTexture =
                                            mat.MetallicnessTexture 
                                            |> Option.bind (fun id -> Map.tryFind id textures)
                                            |> Option.defaultValue (NullTexture.Instance |> AVal.constant)

                                        let normalTexture =
                                            mat.NormalTexture 
                                            |> Option.bind (fun id -> Map.tryFind id textures)
                                            |> Option.defaultValue (NullTexture.Instance |> AVal.constant)

                                        let emissiveTexture =
                                            mat.EmissiveTexture 
                                            |> Option.bind (fun id -> Map.tryFind id textures)
                                            |> Option.defaultValue (NullTexture.Instance |> AVal.constant)
                                            
                                        UniformProvider.ofList [
                                            "DiffuseColor", AVal.constant mat.BaseColor :> IAdaptiveValue
                                            "Roughness", AVal.constant mat.Roughness
                                            "Metallicness", AVal.constant mat.Metallicness
                                            "EmissiveColor", AVal.constant mat.EmissiveColor
                                            "NormalTextureScale", AVal.constant mat.NormalTextureScale

                                            "HasDiffuseColorTexture", AVal.constant (Option.isSome mat.BaseColorTexture)
                                            "HasRoughnessTexture", AVal.constant (Option.isSome mat.RoughnessTexture)
                                            "HasMetallicnessTexture", AVal.constant (Option.isSome mat.MetallicnessTexture)
                                            "HasEmissiveTexture", AVal.constant (Option.isSome mat.EmissiveTexture)
                                            "Has" + string DefaultSemantic.NormalMapTexture, AVal.constant (Option.isSome mat.NormalTexture)

                                            "DiffuseColorTexture", baseColorTexture
                                            "RoughnessTexture", roughnessTexture
                                            "MetallicnessTexture", metallicnessTexture
                                            "RoughnessTextureComponent", AVal.constant mat.RoughnessTextureComponent
                                            "MetallicnessTextureComponent", AVal.constant mat.MetallicnessTextureComponent
                                            string DefaultSemantic.NormalMapTexture, normalTexture
                                            "EmissiveTexture", emissiveTexture
                                        ]
                                    Some (Sg.UniformApplicator(uniforms, mesh) :> ISg)
                                | None ->
                                    Some mesh
                            | None ->
                                None
                        )                         
                        |> SgFSharp.Sg.ofList |> Some

                    let sg =
                        match cs with
                        | Some cs ->
                            ms 
                            |> Option.map(fun m -> SgFSharp.Sg.ofList [cs; m])
                            |> Option.defaultValue cs
                        | None ->
                            ms
                            |> Option.map(fun m -> m)
                            |> Option.defaultValue SgFSharp.Sg.empty
                            
                    node.Trafo
                    |> Option.map (fun t -> SgFSharp.Sg.trafo' t sg)
                    |> Option.defaultValue sg

                traverse scene.RootNode)
            |> Option.defaultValue SgFSharp.Sg.empty
            
        let defaultMaterial =
            UniformProvider.ofList [
                "DiffuseColor", AVal.constant C4f.White :> IAdaptiveValue
                "Roughness", AVal.constant 0.0
                "Metallicness", AVal.constant 0.0
                "EmissiveColor", AVal.constant C4f.Black
                "NormalTextureScale", AVal.constant 1.0

                "HasDiffuseColorTexture", AVal.constant false
                "HasRoughnessTexture", AVal.constant false
                "HasMetallicnessTexture", AVal.constant false
                "HasEmissiveTexture", AVal.constant false
                "Has" + string DefaultSemantic.NormalMapTexture, AVal.constant false

                "DiffuseColorTexture", AVal.constant NullTexture.Instance
                "RoughnessTexture", AVal.constant NullTexture.Instance
                "RoughnessTextureComponent", AVal.constant 0
                "MetallicnessTexture", AVal.constant NullTexture.Instance
                "MetallicnessTextureComponent", AVal.constant 0
                string DefaultSemantic.NormalMapTexture, AVal.constant NullTexture.Instance
                "EmissiveTexture", AVal.constant NullTexture.Instance
            ]

        let viewRover (path : aval<string>) (visible : aval<bool>) (pos : aval<V3d>) = 
            let roverModel = loadRoverModel path//Sg.box (AVal.constant(C4b.Red)) (AVal.constant(Box3d.Unit))
            
            let rM = 
                Sg.UniformApplicator(defaultMaterial, roverModel)
                |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.Colors C4b.White
                |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.Normals V3f.OOI
                |> SgFSharp.Sg.vertexBufferValue' Semantic.Tangent V4f.IOOI
                |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.DiffuseColorCoordinates V2f.Zero
                |> SgFSharp.Sg.vertexBufferValue' Semantic.RoughnessCoordinate V2f.Zero
                |> SgFSharp.Sg.vertexBufferValue' Semantic.EmissiveCoordinate V2f.Zero
                |> SgFSharp.Sg.vertexBufferValue' Semantic.MetallicnessCoordinate V2f.Zero
                |> SgFSharp.Sg.vertexBufferValue' Semantic.NormalCoordinate V2f.Zero
                |> Sg.noEvents

            rM
            |> Sg.translation pos
            |> Sg.uniform "DepthOffset" (AVal.constant 0.0000000001)
            |> Sg.blendMode (AVal.constant BlendMode.None)
            |> Sg.effect [
                toEffect Aardvark.UI.Trafos.Shader.stableTrafo
                toEffect DefaultSurfaces.vertexColor
                toEffect PRo3D.Base.Shader.DepthOffset.depthOffsetFS
            ]
            |> Sg.onOff visible

        


              
        
    

    

