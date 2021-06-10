namespace PRo3D

open System
open System.IO

open Aardvark.Base
open FSharp.Data.Adaptive
open FShade
open Aardvark.Rendering.Effects
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Trafos
open Aardvark.Rendering.Text

open Aardvark.GeoSpatial.Opc
open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.SimulatedViews
open PRo3D.Comparison
open PRo3D.Shading
open Adaptify.FSharp.Core

module ViewerUtils =    
    type Self = Self

    //let _surfaceModelLens = Model.Lens.scene |. Scene.Lens.surfacesModel
    //let _flatSurfaces = Scene.Lens.surfacesModel |. SurfaceModel.Lens.surfaces |. GroupsModel.Lens.flat
        
    let colormap = 
        let config = { wantMipMaps = false; wantSrgb = false; wantCompressed = false }
        let s = typeof<Self>.Assembly.GetManifestResourceStream("PRo3D.SnapshotViewer.resources.HueColorMap.png")
        let pi = PixImage.Create(s)
        PixTexture2d(PixImageMipMap [| pi |], true) :> ITexture  
    
    let addImageCorrectionParameters (surf:aval<AdaptiveSurface>)  (isg:ISg<'a>) =
        
            //AVal.bind(fun x -> lookUp (x.surface) blarg )
        let contr    = surf |> AVal.bind( fun x -> x.colorCorrection.contrast.value )
        let useContr = surf |> AVal.bind( fun x -> x.colorCorrection.useContrast )
        let bright   = surf |> AVal.bind( fun x -> x.colorCorrection.brightness.value )
        let useB     = surf |> AVal.bind( fun x -> x.colorCorrection.useBrightn) 
        let gamma    = surf |> AVal.bind( fun x -> x.colorCorrection.gamma.value )
        let useG     = surf |> AVal.bind( fun x -> x.colorCorrection.useGamma )
        let useGray  = surf |> AVal.bind( fun x -> x.colorCorrection.useGrayscale )
        let useColor = surf |> AVal.bind( fun x -> x.colorCorrection.useColor )
        let color    = surf |> AVal.bind( fun x -> x.colorCorrection.color.c )

        isg
            |> Sg.uniform "useContrastS"   useContr  //image correction
            |> Sg.uniform "contrastS"      contr
            |> Sg.uniform "useBrightnS"    useB
            |> Sg.uniform "brightnessS"    bright
            |> Sg.uniform "useGammaS"      useG 
            |> Sg.uniform "gammaS"         gamma
            |> Sg.uniform "useGrayS"       useGray
            |> Sg.uniform "useColorS"      useColor
            |> Sg.uniform "colorS"         color

    let addAttributeFalsecolorMappingParameters (surf:aval<AdaptiveSurface>)  (isg:ISg<'a>) =
            
        let selectedScalar =
            adaptive {
                let! s = surf
                let! scalar = s.selectedScalar
                match scalar with
                | AdaptiveSome _ -> return true
                | _ -> return false
            }  
      
        let scalar = surf |> AVal.bind( fun x -> x.selectedScalar )        
      

        let interval = scalar |> AVal.bind ( fun x ->
                        match x with 
                            | AdaptiveSome s -> s.colorLegend.interval.value
                            | AdaptiveNone   -> AVal.constant(1.0)
                        )

        let inverted = scalar |> AVal.bind ( fun x ->
                        match x with 
                            | AdaptiveSome s -> s.colorLegend.invertMapping
                            | AdaptiveNone   -> AVal.constant(false)
                        )     
        
        let upperB = scalar |> AVal.bind ( fun x ->
                        match x with 
                            | AdaptiveSome s -> s.colorLegend.upperBound.value
                            | AdaptiveNone   -> AVal.constant(1.0)
                        )

        let lowerB = scalar |> AVal.bind ( fun x ->
                        match x with 
                            | AdaptiveSome s -> s.colorLegend.lowerBound.value
                            | AdaptiveNone   -> AVal.constant(1.0)
                        )

        let upperC = 
          scalar 
            |> AVal.bind (fun x ->
               match x with 
                 | AdaptiveSome s -> 
                   s.colorLegend.upperColor.c 
                     |> AVal.map(fun x -> 
                       let t = x.ToC3f()
                       let t1 = HSVf.FromC3f(t)
                       let t2 = (float)t1.H
                       t2)
                 | AdaptiveNone -> AVal.constant(1.0)
               )
        let lowerC = 
          scalar 
            |> AVal.bind ( fun x ->
              match x with 
                | AdaptiveSome s -> 
                  s.colorLegend.lowerColor.c 
                    |> AVal.map(fun x -> ((float)(HSVf.FromC3f (x.ToC3f())).H))
                | AdaptiveNone   -> AVal.constant(0.0)
              )

        let rangeToMinMax = 
          scalar 
            |> AVal.bind (fun x ->
              match x with 
                  | AdaptiveSome s -> s.definedRange |> AVal.map(fun y -> V2d(y.Min, y.Max))
                  | AdaptiveNone   -> AVal.constant(V2d(0.0,1.0))
              )
        isg
            |> Sg.uniform "falseColors"    selectedScalar
            |> Sg.uniform "startC"         lowerC  
            |> Sg.uniform "endC"           upperC
            |> Sg.uniform "interval"       interval
            |> Sg.uniform "inverted"       inverted
            |> Sg.uniform "lowerBound"     lowerB
            |> Sg.uniform "upperBound"     upperB
            |> Sg.uniform "MinMax"         rangeToMinMax
            |> Sg.texture (Sym.ofString "ColorMapTexture") (AVal.constant colormap)

    let getLodParameters 
        (surf:aval<AdaptiveSurface>) 
        (refsys:AdaptiveReferenceSystem) 
        (frustum : aval<Frustum>) =
        adaptive {
            let! s = surf
            let! frustum = frustum 
            let sizes = V2i(1024,768)
            let! quality = s.quality.value
            //let! trafo = AVal.map2(fun a b -> a * b) s.preTransform s.transformation.trafo //combine pre and current transform
            let! trafo = SurfaceTransformations.fullTrafo surf refsys
            
            return { frustum = frustum; size = sizes; factor = quality; trafo = trafo }
        }
   
            
    let attributeParameters (surf:aval<AdaptiveSurface>) =
         adaptive {
            let! s = surf
            let! scalar = s.selectedScalar
            let! scalar' = 
                match scalar with
                | AdaptiveSome m -> m.label |> AVal.map Some
                | AdaptiveNone -> AVal.constant None //scalar |> Option.map(fun x -> x.index) //option<aval<int>>
            
            let! texture = s.selectedTexture 
            let attr : AttributeParameters = 
                {
                    selectedTexture = texture |> Option.map(fun x -> x.index)
                    selectedScalar  = scalar'//scalar  |> Option.map(fun x -> x.index |> AVal.force)
                }

            return attr
        }

    let attributeParameters' (surf:Surface) =
            let s = surf
            let scalar = s.selectedScalar
            let scalar' = 
                match scalar with
                | Some m -> m.label |> Some
                | None -> None //scalar |> Option.map(fun x -> x.index) //option<aval<int>>
            
            let texture = s.selectedTexture 
            let attr : AttributeParameters = 
                {
                    selectedTexture = texture |> Option.map(fun x -> x.index)
                    selectedScalar  = scalar'//scalar  |> Option.map(fun x -> x.index |> AVal.force)
                }

            attr

    let getTextureSamplerDescription (filterTexture : aval<bool>) =
        filterTexture |> AVal.map (fun filterTexture ->  
            fun (x : SamplerState) -> 
                match filterTexture with
                | false -> { x with Filter = TextureFilter.MinLinearMagPoint }
                | true -> { x with Filter = TextureFilter.MinMagLinear }  // HERA/MARS-DL, default for snapshots
        )

    let addCullFillMode (surf : aval<AdaptiveSurface>) (sg : ISg) =
        sg 
        |> Sg.noEvents 
        |> Sg.cullMode(surf |> AVal.bind(fun x -> x.cullMode))
        |> Sg.fillMode(surf |> AVal.bind(fun x -> x.fillMode))

    let getModelTrafo surface referenceSystem =
        adaptive {
            let! fullTrafo = SurfaceTransformations.fullTrafo surface referenceSystem
            let! s = surface
            let! sc = s.scaling.value
            let! t = s.preTransform
            return Trafo3d.Scale(sc) * (t * fullTrafo )
        }

    //let frustum (m:AdaptiveModel) =
    //    let near = m.scene.config.nearPlane.value
    //    let far = m.scene.config.farPlane.value
    //    (Navigation.UI.frustum near far)

    let fixAlpha (v : Vertex) =
        fragment {         
           return V4d(v.c.X, v.c.Y,v.c.Z, 1.0)           
        }

    //let triangleFilterX (input : Triangle<Vertex>) =
    //    triangle {
    //        let p0 = input.P0.pos.XYZ
    //        let p1 = input.P1.pos.XYZ
    //        let p2 = input.P2.pos.XYZ

    //        let maxSize = uniform?TriangleSize

    //        let a = (p1 - p0)
    //        let b = (p2 - p1)
    //        let c = (p0 - p2)

    //        let alpha = a.Length < maxSize
    //        let beta  = b.Length < maxSize
    //        let gamma = c.Length < maxSize

    //        let check = (alpha && beta && gamma)
    //        if check then
    //            yield input.P0 
    //            yield input.P1
    //            yield input.P2
    //    }

    let getFragmentShader (surface : aval<AdaptiveSurface>) =
        //Shader.OPCFilter.improvedDiffuseTexture |> toEffect
        let fragmentShader =
            let surface = surface.GetValue ()
            let isOpc = surface.surfaceType = SurfaceType.SurfaceOPC
            if isOpc then 
              Shader.dispatchOPCShader|> toEffect
            else Shader.dispatchOBJShader |> toEffect
        fragmentShader

    let getSurfaceEffects surf =
        Effect.compose [
            Shader.stableTrafo             |> toEffect
            Shader.shadowShaderV           |> toEffect
            //triangleFilterX                |> toEffect
            getFragmentShader surf
            //Shader.OPCFilter.improvedDiffuseTexture |> toEffect

            fixAlpha |> toEffect
        
            // selection coloring makes gamma correction pointless. remove if we are happy with markPatchBorders
            // Shader.selectionColor          |> toEffect
            PRo3D.Base.Shader.markPatchBorders |> toEffect
            //PRo3D.Base.Shader.differentColor   |> toEffect
                    
            OpcViewer.Base.Shader.LoDColor.LoDColor |> toEffect                             
         //   PRo3D.Base.Shader.falseColorLegend2 |> toEffect
            PRo3D.Base.Shader.mapColorAdaption  |> toEffect            
            //PRo3D.Base.OtherShader.Shader.footprintV        |> toEffect //TODO reactivate viewplanner
            //PRo3D.Base.OtherShader.Shader.footPrintF        |> toEffect
        ]

    let viewSingleSurfaceSg  (sgSurface       : AdaptiveSgSurface) 
                             (frustum         : aval<Frustum>) 
                             (surfacePicking  : aval<bool>) 
                             (useHighlighting : aval<bool>)
                             (scene           : AdaptiveScene)
                             (comparisonApp   : AdaptiveComparisonApp) 
                             lightViewProj 
                             shadowDepth =
        let surf = Scene.lookUp sgSurface.surface scene
        let placement = 
            adaptive {
                let! s = surf
                let! n = s.name
                return! (scene.objectPlacements |> AMap.tryFind n)
            }
        let maskColor = placement |> AVal.bind (fun p -> match p with 
                                                         | Some p -> p.maskColor.c
                                                         | None -> C4b.Green |> AVal.constant
                                              )
        let isSelected = AVal.map2(fun x y ->
            match x with
            | Some id -> (id = sgSurface.surface) && y
            | None -> false) scene.surfacesModel.surfaces.singleSelectLeaf useHighlighting
                
        let pickable = 
            AVal.map2( fun (a:Box3d) (b:Trafo3d) -> 
                { shape = PickShape.Box (a.Transformed(b)); trafo = Trafo3d.Identity }
            ) sgSurface.globalBB (SurfaceTransformations.fullTrafo surf scene.referenceSystem)
                
        let pickBox = 
            pickable 
            |> AVal.map(fun k ->
                match k.shape with
                | PickShape.Box bb -> bb
                | _ -> Box3d.Invalid)
                
        let triangleFilter = 
            surf |> AVal.bind(fun s -> s.triangleSize.value)
               
                    
        let trafoObj =
            adaptive {
                let! s = surf
                let! t = s.preTransform
                return t
            }
                    
        let samplerDescription = getTextureSamplerDescription scene.config.filterTexture 
                                    
                       
        //let structuralOnOff (visible : aval<bool>) (sg : ISg<_>) : ISg<_> = 
        //    visible 
        //    |> AVal.map (fun visible -> 
        //        if visible then sg else Sg.empty
        //    )
        //    |> Sg.dynamic

        let isOPC =
            surf |> AVal.map (fun x -> x.surfaceType = SurfaceType.SurfaceOPC)

        let drawShadows =
            AVal.map2 (fun x y -> x && y) isOPC scene.config.shadingApp.useShadows 

        let trafo = getModelTrafo surf scene.referenceSystem

        let measurementsSg =
            ComparisonApp.measurementsSg surf
                                         (pickBox |> AVal.map (fun x -> x.Size.[x.MajorDim]))
                                         trafo
                                         scene.referenceSystem
                                         comparisonApp

        //let maskColor = placement |> Mod.bind (fun p -> match p with 
        //                                                 | Some p -> p.maskColor.c
        //                                                 | None -> C4b.Green |> Mod.constant
        //                                      )
        Log.line "[ViewerUtils] Building SceneGraph"
        let sg =             
            sgSurface.sceneGraph
            |> AVal.map (addCullFillMode surf)
            |> Sg.dynamic
            |> Sg.trafo trafo //(Transformations.fullTrafo surf refsys)
            |> Sg.modifySamplerState DefaultSemantic.DiffuseColorTexture samplerDescription 
            |> Sg.uniform "selected"      (isSelected) // isSelected
            |> Sg.uniform "selectionColor" (AVal.constant (C4b (200uy,200uy,255uy,255uy)))
            |> Sg.uniform "LightDirection" scene.config.shadingApp.lightDirection.value
            |> Sg.uniform "useLighting" scene.config.shadingApp.useLighting
            |> Sg.uniform "useMask" scene.config.shadingApp.useMask
            |> Sg.uniform "maskColor" maskColor
            |> Sg.uniform "drawShadows" drawShadows
            |> Sg.uniform "Ambient" scene.config.shadingApp.ambient.value
            |> Sg.uniform "AmbientShadow" scene.config.shadingApp.ambientShadow.value
            |> ShadowSg.applyLightViewProj lightViewProj 
            |> Sg.texture (Sym.ofString "ShadowTexture") shadowDepth 
            |> addAttributeFalsecolorMappingParameters surf
            |> Sg.uniform "TriangleSize"   triangleFilter  //triangle filter
            |> addImageCorrectionParameters surf
            |> Sg.LodParameters( getLodParameters surf scene.referenceSystem frustum )
            |> Sg.AttributeParameters( attributeParameters surf )
            |> OpcViewer.Base.Sg.pickable' pickable
            |> Sg.noEvents 
            |> Sg.effect [getSurfaceEffects surf]
            |> Sg.withEvents [
                SceneEventKind.Click, (
                    fun sceneHit -> 
                      let surfM = surf       |> AVal.force
                      let name  = surfM.name |> AVal.force       
                      let surfacePicking = surfacePicking |> AVal.force
                      //Log.warn "spawning picksurface %s" name
                      true, Seq.ofList [PickSurface (sceneHit, name, surfacePicking)])
                ]  
            // handle surface visibility
            |> Sg.onOff (surf |> AVal.bind(fun x -> x.isVisible)) // on off variant
            //|> structuralOnOff  (surf |> AVal.bind(fun x -> x.isVisible)) // structural variant
            //|> Sg.effect [surfaceEffect]
            |> Sg.andAlso (
                (Sg.wireBox (C4b.VRVisGreen |> AVal.constant) pickBox) 
                |> Sg.noEvents
                |> Sg.effect [              
                    Shader.stableTrafo |> toEffect 
                    DefaultSurfaces.vertexColor |> toEffect
                ] 
                |> Sg.onOff isSelected 
            )
            |> Sg.andAlso (
                measurementsSg
            )
        sg
       



    let getSimpleSingleSurfaceSg (surface : AdaptiveSgSurface) (m : AdaptiveModel) =
        let refsys = m.scene.referenceSystem
        let frustum = m.frustum
        let surf = Scene.lookUp (surface.surface) m.scene
        let sg =             
            surface.sceneGraph
            |> AVal.map (fun sg -> addCullFillMode surf sg)
            |> Sg.dynamic
            |> Sg.trafo (getModelTrafo surf refsys) 
            |> Sg.onOff(surf |> AVal.bind(fun x -> x.isVisible))
            |> Sg.LodParameters( getLodParameters surf refsys frustum )
            |> Sg.noEvents 
            |> Sg.effect [
                Shader.stableTrafo  |> toEffect 
                Shader.OPCFilter.improvedDiffuseTexture |> toEffect
                //Shader.constantColor V4d.OOOO |> toEffect
            ]
        sg

    let getShadowDepthSg (m : AdaptiveModel) sceneBB =        
        let sgGrouped = m.scene.surfacesModel.sgGrouped
        let surfacesToSg surfaces =
            surfaces
              |> AMap.filterA (fun guid sf -> Scene.isVisibleSurfaceObj guid m.scene)
              |> AMap.map (fun guid sf -> getSimpleSingleSurfaceSg sf m)
              |> AMap.toASet 
              |> ASet.map snd    
              |> Sg.set
        let grouped = sgGrouped |> AList.map surfacesToSg
        let sg = grouped |> AList.toASet |> Sg.set
        sg
            |> Sg.viewTrafo (Shading.ShadingApp.lightView m.scene.config.shadingApp 
                                                          sceneBB m.scene.referenceSystem.up.value) 
            |> Sg.projTrafo (Shading.ShadingApp.lightProj m.scene.config.shadingApp 
                                                          sceneBB m.scene.referenceSystem.up.value) 

    //TODO TO refactor screenshot specific
    let getSurfacesScenegraphs (m:AdaptiveModel) runtime =
        //avoids kdtree intersections for certain interactions
        let surfacePicking = 
            m.interaction 
            |> AVal.map(fun x -> 
                match x with
                | Interactions.PickAnnotation | Interactions.PickLog -> false
                | _ -> true
            )
        let exists id = (m.scene.surfacesModel.surfaces.flat |> AMap.keys) |> ASet.contains id
        let sgGrouped = m.scene.surfacesModel.sgGrouped
        let usehighlighting = true |> AVal.constant //m.scene.config.useSurfaceHighlighting
        let sceneBB = Scene.calculateSceneBoundingBox m.scene true // OPCs do not throw shadows
        let lightViewProj = Shading.ShadingApp.lightViewProjection 
                                m.scene.config.shadingApp sceneBB m.scene.referenceSystem.up.value
        let shadowDepth =      
            (getShadowDepthSg m sceneBB)
                |> Sg.compile runtime 
                              (Shading.ShadingApp.shadowDepthsignature runtime)
                |> RenderTask.renderToDepth Shading.ShadingApp.shadowMapSize   
        let grouped = 
            sgGrouped |> AList.map(
                fun x -> ( x 
                    |> AMap.filterA (fun x s -> exists x)
                    |> AMap.map(fun _ sf -> 
                                  viewSingleSurfaceSg sf m.frustum surfacePicking 
                                                      usehighlighting 
                                                      m.scene m.comparisonApp 
                                                      lightViewProj shadowDepth)
                    |> AMap.toASet 
                    |> ASet.map snd                     
                )                
            )


        //grouped   
        let sgs =
            alist {                     
                let mutable i = 0
                for set in grouped do
                    i <- i + 1
                    let sg = 
                        set 
                        |> Sg.set
                        |> Sg.uniform "LoDColor" (AVal.constant C4b.Gray) 
                        |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring //()                        

                    yield  sg
                
                //comparison areas

                                

                        //if i = c then //now gets rendered multiple times
                         // assign priorities globally, or for each anno and make sets

            }                              
        sgs
  
    //TODO TO refactor screenshot specific
    let getSurfacesSgWithCamera (m : AdaptiveModel) runtime =
        let sgs = getSurfacesScenegraphs m runtime
        let camera =
            AVal.map2 (fun v f -> Camera.create v f) m.scene.cameraView m.frustum 
        sgs 
            |> ASet.ofAList
            |> Sg.set
            |> (camera |> Sg.camera)

    let getFrustumDebugSg (m : AdaptiveModel) =
        let sceneBB = Scene.calculateSceneBoundingBox m.scene true // OPCs do not throw shadows
        let lightViewProj = Shading.ShadingApp.lightViewProjection 
                                       m.scene.config.shadingApp sceneBB m.scene.referenceSystem.up.value
        let lightTrafo = 
           adaptive {
              let! lv = ShadingApp.lightView m.scene.config.shadingApp 
                                             sceneBB 
                                             m.scene.referenceSystem.up.value
              return lv.Inverse
           }            
        let frustumTrafo =
           adaptive {
              let! lp = ShadingApp.lightProj m.scene.config.shadingApp 
                                             sceneBB 
                                             m.scene.referenceSystem.up.value
              let! lv = ShadingApp.lightView m.scene.config.shadingApp 
                                             sceneBB 
                                             m.scene.referenceSystem.up.value
              return (lp.Inverse * lv.Inverse)
           }
            
        Sg.box' C4b.Cyan (Box3d.FromCenterAndSize(V3d.OOO, (V3d(0.2,0.2,0.04))))
        |> Sg.noEvents
        |> Sg.trafo lightTrafo
        |> Sg.effect [ 
              Shader.stableTrafo |> toEffect 
              Shader.StableLight.Effect
            ]
        |> Sg.andAlso 
          (Sg.box' C4b.Red (Box3d.FromCenterAndSize (V3d.OOO,V3d.III * 2.0))
            |> Sg.noEvents
            |> Sg.trafo frustumTrafo
            |> Sg.effect [ 
                Shader.stableTrafo |> toEffect 
                Shader.StableLight.Effect
              ])
        |> Sg.andAlso 
          (Sg.wireBox (C4b.BlueViolet |> AVal.constant) sceneBB
            |> Sg.noEvents
            |> Sg.effect [ 
                Shader.stableTrafo |> toEffect 
                Shader.StableLight.Effect
              ])

    let debugSimpleSg (m:AdaptiveModel) (runtime : IRuntime) =
        let sceneBB = Scene.calculateSceneBoundingBox m.scene true // OPCs do not throw shadows
        let sg = getShadowDepthSg m sceneBB
        alist {
            yield RenderCommand.SceneGraph sg
        }

    let renderCommands (sgGrouped:alist<amap<Guid,AdaptiveSgSurface>>) 
                       overlayed depthTested (m:AdaptiveModel)
                       runtime =
        let comparisonSgAreas =  AreaSelection.sgAllAreas m.comparisonApp.areas              
        let areaStatisticsSg = AreaComparison.sgAllDifferences m.comparisonApp.areas

        let sgs = (getSurfacesScenegraphs m runtime)
        let debugSg = (getFrustumDebugSg m)
        //grouped   
        let mutable renderedComparison = false
        alist {        
            for sg in sgs do
                yield RenderCommand.SceneGraph sg
                yield RenderCommand.SceneGraph depthTested
                if not renderedComparison then
                  yield RenderCommand.SceneGraph (areaStatisticsSg )
                  yield RenderCommand.SceneGraph comparisonSgAreas
                  renderedComparison <- true
                yield Aardvark.UI.RenderCommand.Clear(None,Some (AVal.constant 1.0), None)
            yield RenderCommand.SceneGraph overlayed
            let! debug = m.scene.config.shadingApp.debug
            if debug then yield RenderCommand.SceneGraph debugSg 
        }  

    //let renderScreenshot (runtime : IRuntime) (size : V2i) (sg : ISg<ViewerAction>) = 
    //    let col = runtime.CreateTexture(size, TextureFormat.Rgba8, 1, 1);
    //    let signature = 
    //        runtime.CreateFramebufferSignature [
    //            DefaultSemantic.Colors, { format = RenderbufferFormat.Rgba8; samples = 1 }
    //        ]

    //    let fbo = 
    //        runtime.CreateFramebuffer(
    //            signature, 
    //            Map.ofList [
    //                DefaultSemantic.Colors, col.GetOutputView()
    //            ]
    //        )

    //    let taskclear = runtime.CompileClear(signature,AVal.constant C4f.Black,AVal.constant 1.0)
        
    //    let task = runtime.CompileRender(signature, sg)

    //    taskclear.Run(null, fbo |> OutputDescription.ofFramebuffer) |> ignore
    //    task.Run(null, fbo |> OutputDescription.ofFramebuffer) |> ignore
    //    let colorImage = runtime.Download(col)
    //    colorImage

module GaleCrater =
    open PRo3D.Base

    open Aether
    open Aether.Operators
    
    let galeBounds = Box2i(V2i(3,9), V2i(19,16))
    let isGale x = x.importPath |> String.contains "MslGaleDem"
    let galeTrafo = V3d(0.0,0.0,-560.92)

    let _translation = Surface.transformation_ >-> Transformations.translation_ >-> V3dInput.value_
    let _quality = Surface.quality_ >-> NumericInput.value_

    let hack surfaces =

        let surfaces =
            surfaces                         
            |> IndexList.filter(fun x -> 
                if isGale x then
                    let parsedPath = 
                        x.importPath 
                        |> Path.GetFileName 
                        |> String.split('_')
                    
                    //let gridCoord = new V2i((parsedPath.[1] |> Int32.Parse), (parsedPath.[2] |> Int32.Parse))
                    //galeBounds.Contains(gridCoord)   
                    true
                else
                    true
            )
            |> IndexList.map(fun x ->
                if isGale x then
                    x 
                    |> Optic.set _translation galeTrafo
                    |> Optic.set _quality (0.1)
                else    
                    x
            )
        surfaces

