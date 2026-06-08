namespace PRo3D.Core

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
type Rover3DModel = {
    path              : string
    refSystem         : ReferenceSystem
    trafo             : Option<Trafo3d>    
    setPoint          : Option<V3d>
    lightDir          : V3d
    upVector          : V3d
}

type Rover3DAction =
    | SetRoverTrafo  of Trafo3d
    | RemoveRover
    | SetRoverPath   of List<string>
    | SetRoverPoints of V3d

module Rover3DModel =
    module Shader = 
        open FShade
        open Aardvark.Rendering.Effects

        type ViewPositionAttribute() = inherit SemanticAttribute("ViewPosition")
        type RoughnessCoordinateAttribute() = inherit SemanticAttribute("RoughnessCoordinate")
        type MetallicnessCoordinateAttribute() = inherit SemanticAttribute("MetallicnessCoordinate")
        type EmissiveCoordinateAttribute() = inherit SemanticAttribute("EmissiveCoordinate")
        type NormalCoordinateAttribute() = inherit SemanticAttribute(string DefaultSemantic.NormalMapCoordinates)
        type TangentAttribute() = inherit SemanticAttribute("Tangent")
        type ViewTangentAttribute() = inherit SemanticAttribute("ViewTangent")
        type ViewBiTangentAttribute() = inherit SemanticAttribute("ViewBiTangent")
        type ViewLightDirectionAttribute() = inherit SemanticAttribute("ViewLightDirection")

        let linearToSrgb (v : V4f) =
            let e = 1.0f / 2.2f
            V4d(v.X ** e, v.Y ** e, v.Z ** e, v.W)

        [<ReflectedDefinition>]
        let srgbToLinear (v : V4f) =
            let e = 2.2f
            V4f(v.X ** e, v.Y ** e, v.Z ** e, v.W)

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
            member x.DiffuseColor       : V4f = uniform?Material?DiffuseColor
            member x.Roughness          : float = uniform?Material?Roughness
            member x.Metallicness       : float = uniform?Material?Metallicness
            member x.EmissiveColor      : V4f = uniform?Material?EmissiveColor
            member x.NormalTextureScale : float = uniform?Material?NormalTextureScale
       
            member x.HasDiffuseColorTexture : bool = uniform?Material?HasDiffuseColorTexture
            member x.HasRoughnessTexture    : bool = uniform?Material?HasRoughnessTexture
            member x.HasMetallicnessTexture : bool = uniform?Material?HasMetallicnessTexture
            member x.HasEmissiveTexture     : bool = uniform?Material?HasEmissiveTexture
            member x.HasNormalTexture       : bool = uniform?Material?("Has" + string DefaultSemantic.NormalMapTexture)
       
            member x.RoughnessTextureComponent    : int = uniform?Material?RoughnessTextureComponent
            member x.MetallicnessTextureComponent : int = uniform?Material?MetallicnessTextureComponent
       
            member x.HasNormals  : bool = uniform?Mesh?HasNormals
            member x.HasTangents : bool = uniform?Mesh?HasTangents
            member x.HasColors   : bool = uniform?Mesh?HasColors
            member x.LevelCount  : int = uniform?LevelCount

            member x.LightViewProjRover       : M44f = uniform?LightViewProjRover
            member x.LightDirectionRover      : V3f = uniform?LightDirectionRover
            

        let diffuseColorTex =
            sampler2d {
                texture uniform?DiffuseColorTexture
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                filter Filter.MinMagMipLinear
            }

        let private shadowSampler =
            sampler2dShadow {
                texture uniform?ShadowTexture
                filter Filter.MinMagLinear
                addressU WrapMode.Border
                addressV WrapMode.Border
                borderColor C4f.White
                comparison ComparisonFunction.LessOrEqual
            }

        let private poissonDisk =
             [|
                 V2f( -0.94201624f,  -0.39906216f )
                 V2f(  0.94558609f,  -0.76890725f )
                 V2f( -0.094184101f, -0.92938870f )
                 V2f(  0.34495938f,   0.29387760f )
                 V2f( -0.91588581f,   0.45771432f )
                 V2f( -0.81544232f,  -0.87912464f )
                 V2f( -0.38277543f,   0.27676845f )
                 V2f(  0.97484398f,   0.75648379f )
                 V2f(  0.44323325f,  -0.97511554f )
                 V2f(  0.53742981f,  -0.47373420f )
                 V2f( -0.26496911f,  -0.41893023f )
                 V2f(  0.79197514f,   0.19090188f )
                 V2f( -0.24188840f,   0.99706507f )
                 V2f( -0.81409955f,   0.91437590f )
                 V2f(  0.19984126f,   0.78641367f )
                 V2f(  0.14383161f,  -0.14100790f )
             |]

        [<ReflectedDefinition>]
        let private getShadow (wp : V4f) =
            let lightSpace = uniform.LightViewProjRover * wp
            let div = lightSpace.XYZ / lightSpace.W
            let tc = V3f.Half + V3f.Half * div.XYZ

            // PCF using offset disk from
            // http://developer.download.nvidia.com/whitepapers/2008/PCSS_Integration.pdf
            let mutable sum = 0.0f
            for i = 0 to 15 do
                let offset = poissonDisk.[i] * (1.0f / 4096.0f)
                sum <- sum + shadowSampler.Sample(tc.XY + offset, tc.Z - 0.01f)

            0.1f + sum / 16.0f

        let lighting (v : Vertex) =
            fragment {
                //let n = v.n |> Vec.normalize
                //let l = uniform.LightDirection |> Vec.normalize

                let ambient = 0.1f
                //let NdotL = Vec.dot n l
                let diffuse = getShadow v.pos
                    //if NdotL > 0.0f then
                    //    NdotL * getShadow v.wp
                    //else
                    //    0.0f


                return V4f(v.c.XYZ * diffuse, v.c.W)
            }
            
            //fragment {
            //    //let n = v.normal |> Vec.normalize
            //    //let l = uniform.LightDirection |> Vec.normalize

            //    let vp = v.pos

            //    let ambient = 0.1f
            //    //let NdotL = Vec.dot n l
            //    let diffuse = getShadow vp
            //        //if NdotL > 0.0f then
            //        //    NdotL * getShadow v.pos
            //        //else
            //        //    255.0f
                 
            //    let lightSpace = uniform.LightViewProj * vp
            //    let div = lightSpace.XYZ / lightSpace.W
            //    let tc = V3f.Half + V3f.Half * div.XYZ

            //    //return V4f(v.color.XYZ * diffuse, v.color.W)
            //    //return V4f(v.color.XYZ * diffuse + ambient, v.color.W)
            //    return V4f(v.color.XYZ * tc, v.color.W)
            //}

        //let roughnessTexture =
        //    sampler2d {
        //        texture uniform?RoughnessTexture
        //        addressU WrapMode.Wrap
        //        addressV WrapMode.Wrap
        //        filter Filter.MinMagMipLinear
        //    }

        //let metallicnessTexture =
        //    sampler2d {
        //        texture uniform?MetallicnessTexture
        //        addressU WrapMode.Wrap
        //        addressV WrapMode.Wrap
        //        filter Filter.MinMagMipLinear
        //    }

        //let emissiveTexture =
        //    sampler2d {
        //        texture uniform?EmissiveTexture
        //        addressU WrapMode.Wrap
        //        addressV WrapMode.Wrap
        //        filter Filter.MinMagMipLinear
        //    }

        //let normalTexture =
        //    sampler2d {
        //        texture uniform?(string DefaultSemantic.NormalMapTexture)
        //        addressU WrapMode.Wrap
        //        addressV WrapMode.Wrap
        //        filter Filter.MinMagMipLinear
        //    }
        
        let trafo (v : Vertex) =
            vertex {

                let vp = uniform.ModelViewTrafo * v.pos
                //let vld = (uniform.ViewTrafo * V4d(uniform.LightLocation, 1.0) - vp).XYZ |> Vec.normalize
                //let vn = uniform.ModelViewTrafoInv.Transposed.TransformDir v.normal |> Vec.normalize
                //let vt = uniform.ModelViewTrafoInv.Transposed.TransformDir v.tangent.XYZ |> Vec.normalize
                //let vb = v.tangent.W * Vec.cross vn vt

                return
                    { v with
                        pos     = uniform.ProjTrafo * vp
                        //vp = vp
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
                        let tex = diffuseColorTex.Sample(v.tc) |> srgbToLinear
                        tex * uniform.DiffuseColor
                    else
                        uniform.DiffuseColor

                return V4f(baseColor)
            }

    module Shadows = 
        let lightBox = (Box3d(-3.0, -3.0, -5.0, 3.0, 3.0, 80.0))

        let lightView (trafo : aval<Trafo3d>) (sky : aval<V3d>) (lightDir : aval<V3d>) =
            adaptive {
                let! lD = lightDir
                let! t  = trafo   
                let! s  = sky
                let lightLocation = t.TransformPos(-lD.Normalized * 10.0)

                return CameraView.look lightLocation lD s |> CameraView.viewTrafo            
            }                        
        
        let lightViewProj (roverTrafo : aval<Trafo3d>) (sky : aval<V3d>) (lightDir : aval<V3d>) =            
            adaptive {
                let! rT = roverTrafo
                let lightProj = lightBox.Transformed(rT) |> Frustum.ortho |> Frustum.projTrafo

                //let lightProjTransformed = rT * lightProj
                let! view = lightDir |> lightView roverTrafo sky
                return (view * lightProj)
            }            
        
        let computeShadowMap (trafo : aval<Trafo3d>) (signature : IFramebufferSignature) (lightDir : aval<V3d>) (sky : aval<V3d>) (sg : ISg<'Msg>) =
            let runtime = signature.Runtime :?> IRuntime
            let shadowMapSize = V2i(4096, 4096) |> AVal.constant
            let lightProjBox = trafo |> AVal.map lightBox.Transformed
            let lightProj = lightProjBox |> AVal.map(fun b -> Frustum.ortho(b) |> Frustum.projTrafo)

            sg
            |> Sg.viewTrafo (lightView trafo sky lightDir)
            |> Sg.projTrafo lightProj
            |> Sg.compile runtime signature
            |> RenderTask.renderToDepth shadowMapSize

        let createShadowMap (roverTrafo : aval<Trafo3d>) (runtime : IRuntime) (lightDir : aval<V3d>) (sky : aval<V3d>) (sg : ISg<'Msg>) = 
            let signature =
                runtime.CreateFramebufferSignature [
                    DefaultSemantic.DepthStencil, TextureFormat.DepthComponent32
                ]

            computeShadowMap roverTrafo signature lightDir sky sg


    let initial = {
        path = ""//Path.combine [PRo3D.Config.besideExecuteable;"resources";"RoverModel";"Perseverance.gltf"]
        trafo            = None
        refSystem        = ReferenceSystem.initial
        setPoint         = None
        lightDir         = -V3d.OOI
        upVector         = V3d.OOI
    } 

    module TrafoHelper = 
        let trafoFromTranslatedBase
            (position   : V3f) 
            (tilt       : V3f) 
            (forward    : V3f) 
            (right      : V3f) 
            : Trafo3f =

            let rotTrafo =  Trafo3f.FromOrthoNormalBasis(forward.Normalized, right.Normalized, tilt.Normalized)
            (rotTrafo * Trafo3f.Translation(position))
               
        let initialPlacementTrafo' 
            (position:V3f) 
            (forward : V3f) 
            (up:V3f) : Trafo3f =
            
            let forward = forward.Normalized
            let up = up.Normalized

            let n = Vec.Cross(forward, up.Normalized).Normalized
            let tilt = Vec.Cross(n, forward).Normalized
            let right = Vec.Cross(tilt, forward).Normalized        

            trafoFromTranslatedBase position tilt forward right

        let initialPlacementTrafo 
            (position:V3f) 
            (lookAt:V3f) 
            (up:V3f) : Trafo3f =
            let forward = (lookAt - position).Normalized

            initialPlacementTrafo' position forward up  

        let usableTrafo (trafo : aval<Option<Trafo3d>>) =
            trafo |> AVal.map(fun t -> t |> Option.map(fun tr -> Trafo3d(tr)) |> Option.defaultValue Trafo3d.Identity)    
            


    
    let showAfterUpdate (m : Rover3DModel) = 
        let trafo = m.trafo |> Option.map(fun tr -> Trafo3d(tr)) |> Option.defaultValue Trafo3d.Identity

        let lightProjBox = trafo |> Shadows.lightBox.Transformed
        let lightProj = Frustum.ortho(lightProjBox) |> Frustum.projTrafo

        Log.warn "Trafo:%A\nlightProj: %A\n" trafo lightProj

        let lightLocation = trafo.TransformPos (-m.lightDir.Normalized * 10.0)
        let camera = (CameraView.look lightLocation m.lightDir m.upVector) |> CameraView.viewTrafo  

        Log.error "RoverLocation: %A\nLightLocation: %A,\n direction: %A\n upVector: %A\n Camera: %A " (trafo.TransformPos(V3d.OOO)) lightLocation m.lightDir m.upVector camera 

        m


    module Sg = 
        let defaultMaterial =
            UniformProvider.ofList [
                "DiffuseColor", AVal.constant C4f.White :> IAdaptiveValue                
                "HasDiffuseColorTexture", AVal.constant false               
                "DiffuseColorTexture", AVal.constant NullTexture.Instance                
            ]        
            


        let loadRoverModel (path : aval<string> )= 
            aset {
                let! roverFile = path

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
                                | _ -> Symbol.Empty

                            sg <- sg |> Sg.vertexBuffer semantic view

                    let uniforms =
                        UniformProvider.ofList [
                            "HasColors", AVal.constant (Option.isSome m.Colors) :> IAdaptiveValue
                        ]

                    Sg.UniformApplicator(uniforms, sg) :> ISg

                let roverSGModel = 
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


                Sg.UniformApplicator(defaultMaterial, roverSGModel)
                |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.Colors C4b.White
                |> SgFSharp.Sg.vertexBufferValue' DefaultSemantic.DiffuseColorTexture V2f.Zero  
                             
                |> SgFSharp.Sg.shader {
                    do! Shader.trafo
                    do! Shader.shade
                }
                |> Sg.noEvents
            

            } 
            |> Sg.set
            
        

        


              
        
    

    

