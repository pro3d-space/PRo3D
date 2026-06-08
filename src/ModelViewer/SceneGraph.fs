namespace Example.GLTF

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Data.GLTF
open FSharp.Data.Adaptive
open System.IO

module Skybox =
    type Marker = Marker
    let getImage (folderPath: string) (suffix: string) =
        let names = Directory.GetFiles(folderPath)|> Array.map Path.GetFullPath
        let file = names |> Array.find (fun str -> str.EndsWith suffix)
        PixImage.Load(file)

    let get (folderPath: string) =
        AVal.custom (fun _ ->
            let env =
                let trafo t (img : PixImage) =
                    img.TransformedPixImage t

                PixCube [|
                    PixImageMipMap(
                        getImage folderPath "_rt.png"
                        |> trafo ImageTrafo.Rot90
                    )
                    PixImageMipMap(
                        getImage folderPath "_lf.png"
                        |> trafo ImageTrafo.Rot270
                    )

                    PixImageMipMap(
                        getImage folderPath "_bk.png"
                    )
                    PixImageMipMap(
                        getImage folderPath "_ft.png"
                        |> trafo ImageTrafo.Rot180
                    )

                    PixImageMipMap(
                        getImage folderPath "_up.png"
                        |> trafo ImageTrafo.Rot90
                    )
                    PixImageMipMap(
                        getImage folderPath "_dn.png"
                        |> trafo ImageTrafo.Rot90
                    )
                |]
            PixTextureCube(env) :> ITexture
        )


module Semantic =
    let RoughnessCoordinate = Symbol.Create "RoughnessCoordinate"
    let MetallicnessCoordinate = Symbol.Create "MetallicnessCoordinate"
    let EmissiveCoordinate = Symbol.Create "EmissiveCoordinate"
    let NormalCoordinate = DefaultSemantic.NormalMapCoordinates
    let Tangent = Symbol.Create "Tangent"

[<ReflectedDefinition>]
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

    type UniformScope with
        member x.DiffuseColor : V4f = uniform?Material?DiffuseColor
        member x.Roughness : float32 = uniform?Material?Roughness
        member x.Metallicness : float32 = uniform?Material?Metallicness
        member x.EmissiveColor : V4f = uniform?Material?EmissiveColor
        member x.NormalTextureScale : float32 = uniform?Material?NormalTextureScale

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
            [<Position>]                pos             : V4f
            [<ViewPosition>]            viewPos         : V4f
            [<Normal>]                  normal          : V3f
            [<TexCoord>]                texCoord        : V2f
            [<RoughnessCoordinate>]     roughCoord      : V2f
            [<MetallicnessCoordinate>]  metalCoord      : V2f
            [<EmissiveCoordinate>]      emissCoord      : V2f
            [<NormalCoordinate>]        normCoord       : V2f
            [<Tangent>]                 tangent         : V4f
            [<ViewTangent>]             viewTangent     : V3f
            [<ViewBiTangent>]           viewBiTangent   : V3f
            [<ViewLightDirection>]      viewLightDir    : V3f
        }

    let trafo (v : Vertex) =
        vertex {

            let vp = uniform.ModelViewTrafo * v.pos
            let vld = (uniform.ViewTrafo * V4f(uniform.LightLocation, 1.0f) - vp).XYZ |> Vec.normalize
            let vn = uniform.ModelViewTrafoInv.Transposed.TransformDir v.normal |> Vec.normalize
            let vt = uniform.ModelViewTrafoInv.Transposed.TransformDir v.tangent.XYZ |> Vec.normalize
            let vb = v.tangent.W * Vec.cross vn vt

            return
                { v with
                    pos = uniform.ProjTrafo * vp
                    viewPos = vp
                    normal = uniform.ModelViewTrafoInv.Transposed.TransformDir v.normal |> Vec.normalize
                    viewTangent = vt
                    viewBiTangent = vb
                    viewLightDir = vld
                }
        }

    let samples24 =
        [|
            V2f( -0.4612850228120782f, -0.8824263018037591f )
            V2f( 0.2033539719528926f, 0.9766070232577696f )
            V2f( 0.8622755945065503f, -0.4990552917715807f )
            V2f( -0.8458406529500018f, 0.4340626564690164f )
            V2f( 0.9145341241356336f, 0.40187426079092753f )
            V2f( -0.8095919285224212f, -0.2476471278659192f )
            V2f( 0.2443597793708885f, -0.8210571365841042f )
            V2f( -0.29522102954593127f, 0.6411496844366571f )
            V2f( 0.4013698454531175f, 0.47134750051312063f )
            V2f( -0.1573158341083741f, -0.48548502348882533f )
            V2f( 0.5674301785250454f, -0.1052346781436156f )
            V2f( -0.4929375319230899f, 0.09422383038685558f )
            V2f( 0.967785465127825f, -0.06868225365333279f )
            V2f( 0.2267967507441493f, -0.40237871966279687f )
            V2f( -0.7200979001122771f, -0.6248240905561527f )
            V2f( -0.015195608523765971f, 0.35623701723070667f )
            V2f( -0.11428925675805125f, -0.963723441683084f )
            V2f( 0.5482105069441386f, 0.781847612911249f )
            V2f( -0.6515264455787967f, 0.7473765703131305f )
            V2f( 0.5826875031269089f, -0.6956573112908789f )
            V2f( -0.8496230198638387f, 0.09209564840857346f )
            V2f( 0.38289808661249414f, 0.15269522898022844f )
            V2f( -0.4951171173546325f, -0.2654758742352245f )
        |]

    let linearToSrgb (v : V4f) =
        let e = 1.0f / 2.2f
        V4f(v.X ** e, v.Y ** e, v.Z ** e, v.W)

    [<ReflectedDefinition>]
    let srgbToLinear (v : V4f) =
        let e = 2.2f
        V4f(v.X ** e, v.Y ** e, v.Z ** e, v.W)

    let trowbridgeReitzNDF (roughness : float32) (nDotH : float32) =
        let a = roughness * roughness
        let a2 = a * a
        let nDotH2 = nDotH * nDotH
        let denom = nDotH2 * (a2 - 1.0f) + 1.0f
        a2 / (ConstantF.Pi * denom * denom)

    let fresnel (f0 : V3f) (nv : float32) (roughness : float32) =
        let a = V3f.III * (1.0f - roughness)
        f0 + (max f0 a - f0) * nv ** 5.0f

    let schlickBeckmannGAF (d : float32) (roughness : float32) =
        let a = roughness * roughness
        let k = a * 0.797884560803f
        d / (d * (1.0f - k) + k)

    let sampleEnvDiffuse (viewDir : V3f) =
        let worldDir = uniform.ViewTrafoInv.TransformDir viewDir |> Vec.normalize
        skyboxDiffuse.Sample(worldDir) |> srgbToLinear |> Vec.xyz

    let sampleEnv (viewDir : V3f) (roughness : float32) =
        let worldDir = uniform.ViewTrafoInv.TransformDir viewDir |> Vec.normalize
        skyboxSpecular.SampleLevel(worldDir, roughness * float32 (uniform.LevelCount - 1)) |> srgbToLinear |> Vec.xyz

    [<ReflectedDefinition>] [<Inline>]
    let getR0 (reflectivity : float32, metalness : float32, baseColor : V3f) =
        V3f.III * reflectivity * (1.0f - metalness) + baseColor * metalness

    let shade (v : Vertex) =
        fragment {
            let eps = 0.00001f

            let baseColor =
                if uniform.HasDiffuseColorTexture then
                    let tex = diffuseColorTex.Sample(v.texCoord) |> srgbToLinear
                    tex * uniform.DiffuseColor
                else
                    uniform.DiffuseColor

            let roughness =
                if uniform.HasRoughnessTexture then
                    let tv = roughnessTexture.Sample(v.roughCoord).[uniform.RoughnessTextureComponent]
                    let uv = eps + uniform.Roughness
                    uv * tv |> saturate
                else
                    uniform.Roughness + eps |> clamp 0.0f 0.99f

            let metalness =
                if uniform.HasMetallicnessTexture then
                    let tv = metallicnessTexture.Sample(v.metalCoord).[uniform.MetallicnessTextureComponent]
                    let uv = eps + uniform.Metallicness
                    uv * tv |> saturate
                else
                    uniform.Metallicness + eps |> saturate

            let occlusion = 1.0f

            if baseColor.W < 0.01f then discard()

            let vn = v.normal |> Vec.normalize
            let vld = v.viewLightDir |> Vec.normalize
            let vcd = -v.viewPos.XYZ |> Vec.normalize

            let half =
                let v = vld + vcd
                let l = Vec.Length v
                if l > eps then v / l
                else V3f.Zero

            let vn =
                if uniform.HasNormalTexture then
                    let vt = Vec.normalize v.viewTangent
                    let vb = Vec.normalize v.viewBiTangent

                    let v = normalTexture.Sample(v.normCoord).XYZ
                    let nn = (v * 2.0f - 1.0f) * V3f(V2f.II * uniform.NormalTextureScale * 0.5f, 1.0f)

                    let newNormal = vn * nn.Z + vt * nn.X + vb * nn.Y |> Vec.normalize
                    if newNormal.Z < 0.0f then vn
                    else newNormal
                else
                    vn


            let refl = -Vec.reflect vn vcd

            let nl = Vec.dot vn vld |> max 0.0f
            let nh = Vec.dot vn half |> max 0.0f
            //let hv = Vec.dot half vcd |> max 0.0f
            let nv = Vec.dot vn vcd |> max 0.0f



            let f0 = getR0(0.04f, metalness, V3f.III) * baseColor.XYZ
            let d = trowbridgeReitzNDF nh roughness

            let f = fresnel f0 nv roughness
            let g = schlickBeckmannGAF nv roughness * schlickBeckmannGAF nl roughness

            let lambert = nl * 3.0f
            let dr = V3f.III * occlusion

            let diffuseIrradiance = sampleEnvDiffuse vn * occlusion
            let specularIrradiance = sampleEnv refl roughness * occlusion

            let diffuseDirectTerm = (baseColor.XYZ / ConstantF.Pi) * (V3f.III - f) * (1.0f - metalness)

            let specularDirectTerm =
                (f * g * d) / (4.0f * nl * nv + eps)

            let brdfDirectOutput = (diffuseDirectTerm + specularDirectTerm) * lambert * dr
            let ambientDiffuse = diffuseIrradiance * (baseColor.XYZ / ConstantF.Pi) * (1.0f - f) * (1.0f - metalness)

            let ambientSpecular = specularIrradiance * f

            let color = brdfDirectOutput + ambientDiffuse + ambientSpecular

            return V4f(saturate color, 1.0f) |> linearToSrgb
        }

    let environment (v : Effects.Vertex) =
        fragment {
            let ndc = v.pos.XY / v.pos.W

            let p04 = uniform.ProjTrafoInv * V4f(ndc, -1.0f, 1.0f)
            let p14 = uniform.ProjTrafoInv * V4f(ndc, -0.8f, 1.0f)

            let dir = p14.XYZ / p14.W - p04.XYZ / p04.W

            let res = V4f(sampleEnv dir 0.2f, 1.0f)
            return linearToSrgb res
            //return skybox.Sample(dir)
        }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SceneSg =

    let private white =
        let img = PixImage<byte>(Col.Format.RGBA, V2i.II)
        img.GetMatrix<C4b>().Set(C4b.White) |> ignore
        PixTexture2d(img, false) :> ITexture |> AVal.constant


    let private meshSg (m : Mesh) =
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
        | None -> ()

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

    let toSimpleSg (runtime : IRuntime) (scenes : seq<Scene>) (environmentPath: string) =

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

                "DiffuseColorTexture", white
                "RoughnessTexture", white
                "RoughnessTextureComponent", AVal.constant 0
                "MetallicnessTexture", white
                "MetallicnessTextureComponent", AVal.constant 0
                string DefaultSemantic.NormalMapTexture, white
                "EmissiveTexture", white
            ]

        let sceneSgs =
            scenes |> Seq.toList |> List.map (fun scene ->
                let textures =
                    scene.ImageData |> Map.map (fun _ data ->
                        let texture = StreamTexture(fun () -> new MemoryStream(data.Data))
                        texture :> ITexture |> AVal.constant
                    )

                let meshes =
                    scene.Meshes |> Map.map (fun _ m -> meshSg m)



                let rec traverse (node : Node) =


                    let cs =
                        match node.Children with
                        | [] -> None
                        | _ -> node.Children |> Seq.map traverse |> Sg.ofSeq |> Some

                    let ms =
                        node.Meshes |> List.choose (fun mi ->
                            match Map.tryFind mi.Mesh meshes with
                            | Some mesh ->
                                match mi.Material |> Option.bind (fun mid -> Map.tryFind mid scene.Materials) with
                                | Some mat ->
                                    let uniforms =
                                        let baseColorTexture =
                                            match mat.BaseColorTexture |> Option.bind (fun id -> Map.tryFind id textures) with
                                            | Some t -> t
                                            | None -> white

                                        let roughnessTexture =
                                            match mat.RoughnessTexture |> Option.bind (fun id -> Map.tryFind id textures) with
                                            | Some t -> t
                                            | None -> white

                                        let metallicnessTexture =
                                            match mat.MetallicnessTexture |> Option.bind (fun id -> Map.tryFind id textures) with
                                            | Some t -> t
                                            | None -> white

                                        let normalTexture =
                                            match mat.NormalTexture |> Option.bind (fun id -> Map.tryFind id textures) with
                                            | Some t -> t
                                            | None -> white

                                        let emissiveTexture =
                                            match mat.EmissiveTexture |> Option.bind (fun id -> Map.tryFind id textures) with
                                            | Some t -> t
                                            | None -> white

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
                        ) |> Sg.ofList |> Some

                    let sg =
                        match cs with
                        | Some cs ->
                            match ms with
                            | Some ms -> Sg.ofList [cs; ms]
                            | None -> cs
                        | None ->
                            match ms with
                            | Some ms -> ms
                            | None -> Sg.empty

                    match node.Trafo with
                    | Some t -> Sg.trafo' t sg
                    | None -> sg

                traverse scene.RootNode
            )

        let specular, diffuse =
            Skybox.get environmentPath
            |> AVal.force
            |> EnvironmentMap.prepare runtime

        let specular = specular :> ITexture |> AVal.constant
        let diffuse = diffuse :> ITexture |> AVal.constant

        Sg.ofList [
            Sg.UniformApplicator(defaultMaterial, Sg.ofList sceneSgs)
            |> Sg.vertexBufferValue' DefaultSemantic.Normals V3f.OOI
            |> Sg.vertexBufferValue' Semantic.Tangent V4f.IOOI
            |> Sg.vertexBufferValue' DefaultSemantic.DiffuseColorCoordinates V2f.Zero
            |> Sg.vertexBufferValue' Semantic.RoughnessCoordinate V2f.Zero
            |> Sg.vertexBufferValue' Semantic.EmissiveCoordinate V2f.Zero
            |> Sg.vertexBufferValue' Semantic.MetallicnessCoordinate V2f.Zero
            |> Sg.vertexBufferValue' Semantic.NormalCoordinate V2f.Zero
            |> Sg.texture "SkyboxSpecular" specular
            |> Sg.texture "SkyboxDiffuse" diffuse

            Sg.farPlaneQuad
            |> Sg.texture "SkyboxSpecular" specular
            |> Sg.texture "SkyboxDiffuse" diffuse
            |> Sg.shader {
                do! Shader.environment
            }


        ]
        |> Sg.uniform' "LevelCount" EnvironmentMap.levelCount