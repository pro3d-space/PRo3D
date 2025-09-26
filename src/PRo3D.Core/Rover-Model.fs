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
open PRo3D.Core


[<ModelType>]
type RoverModel = {
    path              : string
    roverTraverse     : Option<Guid>
    refSystem         : ReferenceSystem
    translationTrafo  : Trafo3d
    rotationTrafo     : Trafo3d
    roverDirection    : V3d
}

module RoverModel =
    module Shader = 
        open FShade
        type ViewPositionAttribute() = inherit SemanticAttribute("ViewPosition")
        type RoughnessCoordinateAttribute() = inherit SemanticAttribute("RoughnessCoordinate")
        type MetallicnessCoordinateAttribute() = inherit SemanticAttribute("MetallicnessCoordinate")
        type EmissiveCoordinateAttribute() = inherit SemanticAttribute("EmissiveCoordinate")
        type NormalCoordinateAttribute() = inherit SemanticAttribute(string DefaultSemantic.NormalMapCoordinates)
        type TangentAttribute() = inherit SemanticAttribute("Tangent")
        type ViewTangentAttribute() = inherit SemanticAttribute("ViewTangent")
        type ViewBiTangentAttribute() = inherit SemanticAttribute("ViewBiTangent")
        type ViewLightDirectionAttribute() = inherit SemanticAttribute("ViewLightDirection")

        let linearToSrgb (v : V4d) =
            let e = 1.0 / 2.2
            V4d(v.X ** e, v.Y ** e, v.Z ** e, v.W)

        [<ReflectedDefinition>]
        let srgbToLinear (v : V4d) =
            let e = 2.2
            V4d(v.X ** e, v.Y ** e, v.Z ** e, v.W)

        let trowbridgeReitzNDF (roughness : float) (nDotH : float) =
            let a = roughness * roughness
            let a2 = a * a
            let nDotH2 = nDotH * nDotH
            let denom = nDotH2 * (a2 - 1.0) + 1.0
            a2 / (Constant.Pi * denom * denom)

        let fresnel (f0 : V3d) (nv : float) (roughness : float) =
            let a = V3d.III * (1.0 - roughness)
            f0 + (max f0 a - f0) * nv ** 5.0

        let schlickBeckmannGAF (d : float) (roughness : float) =
            let a = roughness * roughness
            let k = a * 0.797884560803
            d / (d * (1.0 - k) + k)
                   

        [<ReflectedDefinition>] [<Inline>]
        let getR0 (reflectivity : float, metalness : float, baseColor : V3d) =
            V3d.III * reflectivity * (1.0 - metalness) + baseColor * metalness

        type UniformScope with
            member x.DiffuseColor : V4d = uniform?Material?DiffuseColor
            member x.Roughness : float = uniform?Material?Roughness
            member x.Metallicness : float = uniform?Material?Metallicness
            member x.EmissiveColor : V4d = uniform?Material?EmissiveColor
            member x.NormalTextureScale : float = uniform?Material?NormalTextureScale
       
            member x.HasDiffuseColorTexture : bool = uniform?Material?HasDiffuseColorTexture
            member x.HasRoughnessTexture : bool = uniform?Material?HasRoughnessTexture
            member x.HasMetallicnessTexture : bool = uniform?Material?HasMetallicnessTexture
            member x.HasEmissiveTexture : bool = uniform?Material?HasEmissiveTexture
            member x.HasNormalTexture : bool = uniform?Material?("Has" + string DefaultSemantic.NormalMapTexture)
       
            member x.RoughnessTextureComponent : int = uniform?Material?RoughnessTextureComponent
            member x.MetallicnessTextureComponent : int = uniform?Material?MetallicnessTextureComponent
       
            member x.HasNormals : bool = uniform?Mesh?HasNormals
            member x.HasTangents : bool = uniform?Mesh?HasTangents
            member x.HasColors : bool = uniform?Mesh?HasColors
            member x.LevelCount : int = uniform?LevelCount
       
        type Vertex =
            {
                [<Position>]                pos             : V4d
                [<ViewPosition>]            viewPos         : V4d
                [<Normal>]                  normal          : V3d
                [<TexCoord>]                texCoord        : V2d
                [<RoughnessCoordinate>]     roughCoord      : V2d
                [<MetallicnessCoordinate>]  metalCoord      : V2d
                [<EmissiveCoordinate>]      emissCoord      : V2d
                [<NormalCoordinate>]        normCoord       : V2d
                [<Tangent>]                 tangent         : V4d
                [<ViewTangent>]             viewTangent     : V3d
                [<ViewBiTangent>]           viewBiTangent   : V3d
                [<ViewLightDirection>]      viewLightDir    : V3d
            }

        let skyboxSpecular =
            samplerCube {
                texture uniform?SkyboxSpecular
                filter Filter.MinMagMipLinear
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                addressW WrapMode.Wrap
            }

        let skyboxDiffuse =
            samplerCube {
                texture uniform?SkyboxDiffuse
                filter Filter.MinMagMipLinear
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                addressW WrapMode.Wrap
            }

        let diffuseColorTex =
            sampler2d {
                texture uniform?DiffuseColorTexture
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                filter Filter.MinMagMipLinear
            }

        let roughnessTexture =
            sampler2d {
                texture uniform?RoughnessTexture
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                filter Filter.MinMagMipLinear
            }

        let metallicnessTexture =
            sampler2d {
                texture uniform?MetallicnessTexture
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                filter Filter.MinMagMipLinear
            }

        let emissiveTexture =
            sampler2d {
                texture uniform?EmissiveTexture
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                filter Filter.MinMagMipLinear
            }

        let normalTexture =
            sampler2d {
                texture uniform?(string DefaultSemantic.NormalMapTexture)
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                filter Filter.MinMagMipLinear
            }
        
        let trafo (v : Vertex) =
            vertex {

                let vp = uniform.ModelViewTrafo * v.pos
                //let vld = (uniform.ViewTrafo * V4d(uniform.LightLocation, 1.0) - vp).XYZ |> Vec.normalize
                //let vn = uniform.ModelViewTrafoInv.Transposed.TransformDir v.normal |> Vec.normalize
                //let vt = uniform.ModelViewTrafoInv.Transposed.TransformDir v.tangent.XYZ |> Vec.normalize
                //let vb = v.tangent.W * Vec.cross vn vt

                return
                    { v with
                        pos = uniform.ProjTrafo * vp
                        viewPos = vp
                        //normal = uniform.ModelViewTrafoInv.Transposed.TransformDir v.normal |> Vec.normalize
                        //viewTangent = vt
                        //viewBiTangent = vb
                        //viewLightDir = vld
                    }
            }

        let shade (v : Vertex) =
            fragment {
                let eps = 0.00001

                let baseColor =
                    if uniform.HasDiffuseColorTexture then
                        let tex = diffuseColorTex.Sample(v.texCoord) |> srgbToLinear
                        tex * uniform.DiffuseColor
                    else
                        uniform.DiffuseColor

                return baseColor

                //let roughness =
                //    if uniform.HasRoughnessTexture then
                //        let tv = roughnessTexture.Sample(v.roughCoord).[uniform.RoughnessTextureComponent]
                //        let uv = eps + uniform.Roughness
                //        uv * tv |> saturate
                //    else
                //        uniform.Roughness + eps |> clamp 0.0 0.99

                //let metalness =
                //    if uniform.HasMetallicnessTexture then
                //        let tv = metallicnessTexture.Sample(v.metalCoord).[uniform.MetallicnessTextureComponent]
                //        let uv = eps + uniform.Metallicness
                //        uv * tv |> saturate
                //    else
                //        uniform.Metallicness + eps |> saturate

                //let occlusion = 1.0

                //if baseColor.W < 0.01 then discard()

                //let vn = v.normal |> Vec.normalize
                //let vld = v.viewLightDir |> Vec.normalize
                //let vcd = -v.viewPos.XYZ |> Vec.normalize

                //let half =
                //    let v = vld + vcd
                //    let l = Vec.Length v
                //    if l > eps then v / l
                //    else V3d.Zero

                //let vn =
                //    if uniform.HasNormalTexture then
                //        let vt = Vec.normalize v.viewTangent
                //        let vb = Vec.normalize v.viewBiTangent

                //        let v = normalTexture.Sample(v.normCoord).XYZ
                //        let nn = (v * 2.0 - 1.0) * V3d(V2d.II * uniform.NormalTextureScale * 0.5, 1.0)

                //        let newNormal = vn * nn.Z + vt * nn.X + vb * nn.Y |> Vec.normalize
                //        if newNormal.Z < 0.0 then vn
                //        else newNormal
                //    else
                //        vn


                //let refl = -Vec.reflect vn vcd

                //let nl = Vec.dot vn vld |> max 0.0
                //let nh = Vec.dot vn half |> max 0.0
                ////let hv = Vec.dot half vcd |> max 0.0
                //let nv = Vec.dot vn vcd |> max 0.0



                //let f0 = getR0(0.04, metalness, V3d.III) * baseColor.XYZ
                //let d = trowbridgeReitzNDF nh roughness

                //let f = fresnel f0 nv roughness
                //let g = schlickBeckmannGAF nv roughness * schlickBeckmannGAF nl roughness

                //let lambert = nl * 3.0
                //let dr = V3d.III * occlusion

                //let diffuseDirectTerm = (baseColor.XYZ / Constant.Pi) * (V3d.III - f) * (1.0 - metalness)

                //let specularDirectTerm =
                //    (f * g * d) / (4.0 * nl * nv + eps)

                //let brdfDirectOutput = (diffuseDirectTerm + specularDirectTerm) * lambert * dr
                //let ambientDiffuse = (baseColor.XYZ / Constant.Pi) * (1.0 - f) * (1.0 - metalness)

                //let ambientSpecular = f

                //let color = brdfDirectOutput + ambientDiffuse + ambientSpecular

                //return V4d(saturate color, 1.0) |> linearToSrgb
            }


    let initial = {
        path = @"D:\Temp\RoverTesting\Export_YoqSothoth\Perseverance_100.gltf"
        roverTraverse    = None
        translationTrafo = Trafo3d.Translation(V3d.NaN)
        rotationTrafo    = Trafo3d.Identity
        refSystem        = ReferenceSystem.initial
        roverDirection   = V3d.IOO
    } 

    module TrafoHelper = 
        let trafoFromTranslatedBase
            (position   : V3d) 
            (tilt       : V3d) 
            (forward    : V3d) 
            (right      : V3d) 
            : Trafo3d =

            let rotTrafo =  Trafo3d.FromOrthoNormalBasis(forward.Normalized, right.Normalized, tilt.Normalized)
            (rotTrafo * Trafo3d.Translation(position))
               
        let initialPlacementTrafo' 
            (position:V3d) 
            (forward : V3d) 
            (up:V3d) : Trafo3d =
            
            let forward = forward.Normalized
            let up = up.Normalized

            let n = Vec.Cross(forward, up.Normalized).Normalized
            let tilt = Vec.Cross(n, forward).Normalized
            let right = Vec.Cross(tilt, forward).Normalized        

            trafoFromTranslatedBase position tilt forward right

        let initialPlacementTrafo 
            (position:V3d) 
            (lookAt:V3d) 
            (up:V3d) : Trafo3d =
            let forward = (lookAt - position).Normalized

            initialPlacementTrafo' position forward up  

    module Sg = 
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
                
                match m.Colors with
                | Some cs -> sg <- sg |> Sg.vertexAttribute' DefaultSemantic.Colors cs
                | None -> sg <- sg |> Sg.vertexAttribute' DefaultSemantic.Colors (Array.create m.Positions.Length C4b.White)

                for data, sems in m.TexCoords do
                    let view = BufferView.ofArray data
                    for sem in sems do
                        let semantic =
                            match sem with
                            | TextureSemantic.BaseColor -> DefaultSemantic.DiffuseColorCoordinates  
                            
                            

                        sg <- sg |> Sg.vertexBuffer semantic view

                let uniforms =
                    UniformProvider.ofList [
                        "HasColors", AVal.constant (Option.isSome m.Colors) :> IAdaptiveValue
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

                let rec traverse (node : Aardvark.Data.GLTF.Node) : ISg =
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
                                        
                                            
                                        UniformProvider.ofList [
                                            "DiffuseColor", AVal.constant mat.BaseColor :> IAdaptiveValue                                            
                                            "NormalTextureScale", AVal.constant mat.NormalTextureScale
                                            "HasDiffuseColorTexture", AVal.constant (Option.isSome mat.BaseColorTexture)
                                            "DiffuseColorTexture", baseColorTexture

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
                "HasDiffuseColorTexture", AVal.constant false               
                "DiffuseColorTexture", AVal.constant NullTexture.Instance                
            ]

        let viewRover (path : aval<string>) (visible : aval<bool>) (translationTrafo : aval<Trafo3d>) (rotationTrafo : aval<Trafo3d>)= 
            let roverModel = loadRoverModel path//Sg.box (AVal.constant(C4b.Red)) (AVal.constant(Box3d.Unit))
            
            let rM = 
                Sg.UniformApplicator(defaultMaterial, roverModel)
                |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.Colors C4b.White
                |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.DiffuseColorTexture V2f.Zero  
                //|> SgFSharp.Sg.trafo rotationTrafo
                //|> SgFSharp.Sg.trafo translationTrafo                                
                |> SgFSharp.Sg.shader {
                    do! Shader.trafo
                    do! Shader.shade
                }
                |> Sg.noEvents

            Sg.ofList [rM; (Sg.cylinder 16 (C4b.Orange |> AVal.constant) (1.0 |> AVal.constant) (2.0 |> AVal.constant))]  
            |> Sg.trafo rotationTrafo
            |> Sg.trafo translationTrafo
            |> Sg.uniform "DepthOffset" (AVal.constant 0.0000000001)
            |> Sg.blendMode (AVal.constant BlendMode.None)
            |> Sg.effect [
                toEffect Aardvark.UI.Trafos.Shader.stableTrafo
                toEffect PRo3D.Base.Shader.DepthOffset.depthOffsetFS
            ]            
            |> Sg.onOff visible
            

        


              
        
    

    

