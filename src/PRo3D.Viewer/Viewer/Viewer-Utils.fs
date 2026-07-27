namespace PRo3D

open System
open System.IO

open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open FShade
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.SimulatedViews

open Adaptify.FSharp.Core
open Aardvark.GeoSpatial.Opc
open PRo3D.InstrumentVisualization

module ViewerUtils =    
    type Self = Self

    let mapAttribute (f : 'msg -> 'newmsg) (a : Attribute<'msg>) =
        let (str, a) = a
        let avalue = AttributeValue.map f a
        Aardvark.UI.Attribute (str, avalue)

    //let _surfaceModelLens = Model.Lens.scene |. Scene.Lens.surfacesModel
    //let _flatSurfaces = Scene.Lens.surfacesModel |. SurfaceModel.Lens.surfaces |. GroupsModel.Lens.flat
        
    let colormap =
        let s = typeof<Self>.Assembly.GetManifestResourceStream("PRo3D.Viewer.resources.HueColorMap.png")
        let pi = PixImage.Load(s)
        PixTexture2d(PixImageMipMap [| pi |], true) :> ITexture    
    

    let addImageCorrectionParameters (surf: AdaptiveSurface)  (isg:ISg<'a>) =

        let contr    = surf.colorCorrection.contrast.value 
        let useContr = surf.colorCorrection.useContrast 
        let bright   = surf.colorCorrection.brightness.value 
        let useB     = surf.colorCorrection.useBrightn
        let gamma    = surf.colorCorrection.gamma.value 
        let useG     = surf.colorCorrection.useGamma 
        let useGray  = surf.colorCorrection.useGrayscale 
        let useColor = surf.colorCorrection.useColor 
        let color    = surf.colorCorrection.color.c 

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


    let addRadiometryParameters (surf: AdaptiveSurface)  (isg:ISg<'a>) =

        let useRad   = surf.radiometry.useRadiometry 
        let abR =
            adaptive {
                let! minR     = surf.radiometry.minR.value
                let! maxR     = surf.radiometry.maxR.value
                return V2d(minR,maxR)
            }
            
        let abG =
            adaptive {
                let! minG     = surf.radiometry.minG.value
                let! maxG     = surf.radiometry.maxG.value
                return V2d(minG,maxG)
            } 

        let abB =
            adaptive {
                let! minB     = surf.radiometry.minB.value
                let! maxB     = surf.radiometry.maxB.value
                return V2d(minB,maxB)
            } 

        isg
            |> Sg.uniform "useRadiometry"  useRad  
            |> Sg.uniform "abR"     abR
            |> Sg.uniform "abG"     abG
            |> Sg.uniform "abB"     abB


    let addAttributeFalsecolorMappingParameters (surf : AdaptiveSurface)  (isg:ISg<'a>) =
            
        let selectedScalar =
            adaptive {
                let! scalar = surf.selectedScalar
                match scalar with
                | AdaptiveSome _ -> return true
                | _ -> return false
            }  
      
        let scalar = surf.selectedScalar   
      

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

    let addDepthMappingParameters (fp : AdaptiveFootPrint)   (isg:ISg<'a>) =
        isg 
            |> Sg.uniform "falseColors"    fp.depthColorLegend.useFalseColors 
            |> Sg.uniform "startC"         ((C4b.Blue |> AVal.constant) |> AVal.map(fun x -> ((float)(HSVf.FromC3f (x.ToC3f())).H)))  
            |> Sg.uniform "endC"           ((C4b.Red |> AVal.constant)  |> AVal.map(fun x -> ((float)(HSVf.FromC3f (x.ToC3f())).H)))  
            |> Sg.uniform "interval"       fp.depthColorLegend.interval.value
            |> Sg.uniform "inverted"       (false |> AVal.constant)
            |> Sg.uniform "lowerBound"     fp.depthColorLegend.lowerBound.value //(AVal.constant(0.0)) //
            |> Sg.uniform "upperBound"     fp.depthColorLegend.upperBound.value //
            |> Sg.uniform "MinMax"         (AVal.constant(3000.0)) //(AVal.constant(V2d(0.0,1.0)))
            |> Sg.texture (Sym.ofString "ColorMapTexture") (AVal.constant colormap)

    let getLodParameters 
        (surf:aval<AdaptiveSurface>) 
        (refsys:AdaptiveReferenceSystem) 
        (observedSystem : aval<Option<SpiceReferenceSystem>>) 
        (observerSystem : aval<Option<ObserverSystem>>)
        (frustum : aval<Frustum>) =
        adaptive {
            let! s = surf
            let! frustum = frustum 
            let sizes = V2i(1024,768)
            let! quality = s.quality.value
            //let! trafo = AVal.map2(fun a b -> a * b) s.preTransform s.transformation.trafo //combine pre and current transform
            let! trafo =  TransformationApp.fullTrafo s.transformation refsys observedSystem observerSystem //SurfaceTransformations.fullTrafo surf refsys
            
            return { frustum = frustum; size = sizes; factor = quality; trafo = trafo }
        }
    
    let getLodParameters' (surf:Surface) (frustum : Frustum) =
        let sizes = V2i(1024,768)
        let quality = surf.quality.value
        let trafo = surf.preTransform //combine pre and current transform
            
        { frustum = frustum; size = sizes; factor = Math.Pow(Math.E, quality); trafo = trafo }
            
    let attributeParameters (surf:aval<AdaptiveSurface>) =
         adaptive {
            let! s = surf
            let! scalar = s.selectedScalar
            let! scalar' = 
                match scalar with
                | AdaptiveSome m -> m.label |> AVal.map Some
                | AdaptiveNone -> AVal.constant None //scalar |> Option.map(fun x -> x.index) //option<aval<int>>
            
            let! texture = s.primaryTexture 
            let attr : AttributeParameters = 
                {
                    selectedTexture = texture |> Option.map (fun x -> { texture = TextureReference.LegacyId x.index; channel = ChannelReference.NoChannelSelection })
                    selectedScalar  = scalar'//scalar  |> Option.map(fun x -> x.index |> AVal.force)
                }

            return attr
        }

    type Vertex = {
        [<Position>]        pos     : V4f
        [<Color>]           c       : V4f
        [<TexCoord>]        tc      : V2f
        [<Semantic("ViewSpacePos")>]  vp    : V4f
        [<Semantic("FootPrintProj")>] tc0   : V4f
        [<Semantic("DepthTex")>]      tc1   : V4f
    }

        
    let viewSingleSurfaceSg 
        (surface         : AdaptiveSgSurface) 
        (m               : AdaptiveModel) 
        (surfacesMap     : amap<Guid, AdaptiveLeafCase>)
        (frustum         : aval<Frustum>) 
        (selectedId      : aval<Option<Guid>>)
        (surfacePicking  : aval<bool>)
        (previewPickingEnabled : aval<bool>)
        (globalBB        : aval<Box3d>) 
        (refsys          : AdaptiveReferenceSystem)
        (observedSystem  : aval<Option<SpiceReferenceSystem>>) 
        (observerSystem  : aval<Option<ObserverSystem>>)
        (fp              : AdaptiveFootPrint) 
        (vpVisible       : aval<bool>)
        (useHighlighting : aval<bool>)
        (filterTexture   : aval<bool>)
        (allowFootprint  : bool)  
        (allowDepthview  : bool) 
        (cursorWorldSpace : aval<Option<V3d * float>>)
        (view            : aval<CameraView>) =

        adaptive {
            match! AMap.tryFind surface.surface surfacesMap with
            | Some (AdaptiveSurfaces surf) -> 

                let isSelected = 
                    adaptive {
                        let! selId = selectedId
                        let! selectedH = surf.highlightSelected
                        let! alwaysH = surf.highlightAlways

                        match selId with
                        | Some id -> return (((id = surface.surface) && selectedH) || alwaysH)
                        | None -> return false
                    }
                   
                let createSg (sg : ISg) =
                    sg 
                    |> Sg.noEvents 
                    |> Sg.cullMode(surf.cullMode)
                    |> Sg.fillMode(surf.fillMode)
                                                
                let triangleFilter = surf.triangleSize.value
                let triangleFilterEnabled = surf.filterByTriangleSize

                let trafo =
                    adaptive {
                        let! fullTrafo = TransformationApp.fullTrafo surf.transformation refsys observedSystem observerSystem
                        let! preTransform = surf.preTransform
                        let! flipZ = surf.transformation.flipZ
                        let! sketchFab = surf.transformation.isSketchFab
                        if flipZ then 
                            return Trafo3d.Scale(1.0, 1.0, -1.0) * (fullTrafo * preTransform)
                        else if sketchFab then
                            // TODO https://github.com/pro3d-space/PRo3D/issues/117
                            // i'm not sure whether swithcYZTrafo is the right one here. Firstly, i think we should change the naming (also in the UI).
                            // Secondly, do we need this as a third option: 
                            //return Trafo3d.FromOrthoNormalBasis(V3d.IOO,-V3d.OIO,-V3d.OOI)
                            // this was here before:
                            return Sg.switchYZTrafo
                        else
                            return (fullTrafo * preTransform)
                            //return Trafo3d.Scale(scaleFactor) * (fullTrafo * preTransform)
                    }

                let pickable = 
                    (globalBB, trafo)
                    ||>  AVal.map2( fun (a:Box3d) (b:Trafo3d) -> 
                        { shape = PickShape.Box (a.Transformed(b)); trafo = Trafo3d.Identity }
                    ) 
                
                let pickBox = 
                    pickable 
                    |> AVal.map(fun k ->
                        match k.shape with
                        | PickShape.Box bb -> bb
                        | _ -> Box3d.Invalid
                    )
                    

                let samplerDescription : aval<SamplerState -> SamplerState> = 
                    (AVal.constant(true)) //filterTexture 
                    |> AVal.map (fun filterTexture ->  
                        fun (x : SamplerState) -> 
                            match filterTexture with
                            | false -> { x with Filter = TextureFilter.MinLinearMagPoint }
                            | true -> { x with Filter = TextureFilter.MinMagLinear }  // HERA/MARS-DL, default for snapshots
                    )
                        
                let footprintVisible = //AVal.map2 (fun (vp:Option<AdaptiveViewPlan>) vis -> (vp.IsSome && vis)) vp, fp.isVisible
                    adaptive {
                        if not allowFootprint then 
                            return false
                        else
                            let! fpVisible = fp.isVisible
                            let! vpV = vpVisible
                            return (fpVisible && vpV)
                    }

                let depthVisible = 
                    adaptive {
                        if not allowDepthview then return false
                        else
                            let! depthVisible = fp.isDepthVisible
                            let! vpV = vpVisible
                            return (depthVisible && vpV)
                    }
                
                let footprintViewProj = 
                    adaptive {
                        let! fppm = fp.projectionMatrix
                        let! fpvm = fp.instViewMatrix
                        //let! s = surf
                        //let! ts = s.preTransform
                        //let! t = trafo
                        //return (t.Forward * fpvm * fppm) //* t.Forward 
                        return (fppm * fpvm) // * ts.Forward
                    }

                let structuralOnOff (visible : aval<bool>) (sg : ISg<_>) : ISg<_> = 
                    visible 
                    |> AVal.map (fun visible -> 
                        if visible then sg else Sg.empty
                    )
                    |> Sg.dynamic

               
                // transform the home position into view space on the CPU (double precision)
                // and hand a clean V3f to the shader; the per-vertex check then runs in the
                // numerically stable view space (vp), no planet-scale world coords on the GPU
                let homePositionViewSpace =
                    adaptive {
                        let! homePosition = surf.homePosition

                        match homePosition with
                        | Some hp ->
                            let! view' = view
                            let mv = (view' |> CameraView.viewTrafo).Forward
                            return V3f (mv.TransformPos hp.Location)
                        | None ->
                            let! bb = surface.globalBB
                            return V3f bb.Center
                    }
                    
                let filterByDistance =
                    adaptive {
                        let! homePosition = surf.homePosition 
                        
                        match homePosition with
                        | Some _ ->
                            return! surf.filterByDistance
                        | None ->
                            return false
                    }  
                    
                let cusorViewSpace = 
                    (view, cursorWorldSpace) ||> AVal.map2 (fun view p -> 
                        match p with
                        | None -> V4d.Zero
                        | Some (p, _) -> 
                            let vp = (CameraView.viewTrafo view).Forward.TransformPos p
                            V4d(vp, 1.0) // w for indication if valid
                    )

                let surfaceSg =
                    surface.sceneGraph
                    |> AVal.map createSg
                    |> Sg.dynamic
                    |> Sg.trafo trafo //(Transformations.fullTrafo surf refsys)
                    |> Sg.modifySamplerState DefaultSemantic.DiffuseColorTexture samplerDescription
                    |> Sg.applyBody (observedSystem |> AVal.map (function None -> None | Some o -> Some o.body.Value))
                    |> Sg.noEvents
                    |> Sg.uniform "selected"      (isSelected) // isSelected
                    |> Sg.uniform "selectionColor" (AVal.constant (C4b (200uy,200uy,255uy,255uy)))
                    //|> addAttributeFalsecolorMappingParameters surf
                    |> addDepthMappingParameters fp
                    |> Sg.uniform "FilterTriangleEnabled" triangleFilterEnabled
                    |> Sg.uniform "MaxTriangleSize"   triangleFilter  
                    |> Sg.uniform "HomePositionViewSpace" homePositionViewSpace
                    |> Sg.uniform "FilterByDistance" filterByDistance
                    |> Sg.uniform "FilterDistance" (surf.filterDistance.value |> AVal.map float32)


                    |> Sg.uniform "CursorViewSpace" cusorViewSpace
                    |> Sg.uniform "CursorWorldSizeSquared" (
                        cursorWorldSpace |> AVal.map (function 
                            | None -> V4f.Zero 
                            | Some (_, s) -> 
                                let r = s * 0.2
                                // radius start gradient, radius start inner cirucle, radius end gradient
                                V4f(r, r + 0.1 * r, r + 0.2 * r, r + 0.25 * r) ////(V4f(2.0, 2.1, 2.2, 2.3)  |> AVal.constant)
                        )
                    ) 
                    |> Sg.uniform "CursorShaderEnabled" (AVal.constant true)

                    |> addImageCorrectionParameters  surf
                    |> addRadiometryParameters surf
                    |> Sg.uniform "DepthVisible" depthVisible //(AVal.constant(true)) //
                    |> Sg.uniform "FootprintVisible" footprintVisible
                    |> Sg.uniform "FootprintModelViewProj" (M44d.Identity |> AVal.constant)
                    |> Sg.applyFootprint footprintViewProj
                    |> Sg.noEvents
                    |> Sg.texture (Sym.ofString "ColorMapTexture") (AVal.constant colormap)
                    |> Sg.texture (Sym.ofString "FootPrintTexture") fp.projTex
                    |> Sg.LodParameters ( getLodParameters  (AVal.constant surf) refsys observedSystem observerSystem frustum  )
                    |> Sg.AttributeParameters( attributeParameters  (AVal.constant surf) )
                    
                    |> SecondaryTexture.Sg.applySecondaryTextureId (
                            (surf.secondaryTexture, surf.secondaryTextureLayer) 
                            ||> AVal.map2 (fun texture layer ->
                                match texture, layer with
                                | Some s, Some l -> Some { texture = TextureReference.LegacyId s.index; channel = ChannelReference.ChannelWithIndex l }
                                | Some s, None -> 
                                    Some { texture = TextureReference.LegacyId s.index; channel = ChannelReference.NoChannelSelection }
                                | _ -> None
                            )
                    )
                    |> Sg.pickable' pickable

                    |> Sg.noEvents 

                    |> Sg.texture "SecondaryTextureTransferFunction" (
                        surf.transferFunction |> AVal.map (fun tf -> 
                            match tf.tf with
                            | ColorMaps.TF.Passthrough -> NullTexture.Instance
                            | ColorMaps.TF.Ramp(_,_,name) ->
                                match Map.tryFind name ColorMaps.colorMaps with
                                | None -> NullTexture.Instance
                                | Some l ->
                                    try 
                                        l.Value :> ITexture
                                    with e -> 
                                        Log.warn "SecondaryTextureTransferFunction: %A" e
                                        NullTexture.Instance
                        )
                    )
                    |> Sg.uniform "TextureCombiner" (
                        surf.transferFunction |> AVal.map (fun tf -> tf.textureCombiner)
                    )
                    |> Sg.uniform "SecondaryTextureContour"(
                        surf.contourModel.Current |> AVal.map (fun m -> 
                            V4d((if m.enabled then m.distance.value else -1.0), m.width.value, m.border.value, 0.0)
                        )
                    )
                    |> Sg.uniform "TransferFunctionMode" (
                        surf.transferFunction |> AVal.map (fun tf -> 
                            match tf.tf with
                            | ColorMaps.TF.Passthrough -> TransferFunctionMode.Passthrough
                            | ColorMaps.TF.Ramp(_,_,_) -> TransferFunctionMode.Ramp
                        )
                    )
                    |> Sg.uniform "TFRange" (
                        surf.transferFunction |> AVal.map (fun tf -> 
                            match tf.tf with
                            | ColorMaps.TF.Passthrough -> V2d.OI
                            | ColorMaps.TF.Ramp(min,max,_) -> V2d(min, max)
                        )
                    )
                    |> Sg.uniform "TFBlendFactor" (
                        surf.transferFunction |> AVal.map (fun tf -> tf.blendFactor)
                    )



                    |> Sg.withEvents [
                        if Config.previewIntersections  then
                            yield SceneEventKind.Move, (
                                fun sceneHit ->
                                    let surfacePicking = surfacePicking |> AVal.force
                                    let surfacePickingActivated = ((m.ctrlFlag |> AVal.force) <> (m.inverseFlag |> AVal.force))
                                    // only show the preview cursor while in picking mode (ctrl held,
                                    // modulo invert) - no preview while navigating the camera
                                    if previewPickingEnabled.GetValue() && surfacePicking && surfacePickingActivated then
                                        let name  = surf.name |> AVal.force
                                        true, Seq.ofList [PreviewPickSurface (sceneHit, name, true)]
                                    else
                                        true, Seq.empty
                            )
                        yield SceneEventKind.Click, (
                           fun sceneHit -> 
                                let name  = surf.name |> AVal.force        
                                let surfacePicking = surfacePicking |> AVal.force
                                let surfacePickingActivated = ((m.ctrlFlag |> AVal.force) <> (m.inverseFlag |> AVal.force))
                                if surfacePicking && surfacePickingActivated then
                                    true, Seq.ofList [PickSurface (sceneHit, name, true)]
                                else 
                                    true, Seq.ofList []
                            )
                       ]  
                    // handle surface visibility
                    |> Sg.onOff (surf.isVisible) // on off variant
                    //|> structuralOnOff  (surf |> AVal.bind(fun x -> x.isVisible)) // structural variant
                    |> Sg.andAlso (
                        (Sg.wireBox (C4b.VRVisGreen |> AVal.constant) pickBox) 
                        |> Sg.noEvents
                        |> Sg.effect [  
                            Shader.stableTrafo |> toEffect 
                            DefaultSurfaces.vertexColor |> toEffect
                        ] 
                        |> Sg.onOff isSelected
                    )
                    // pivot point
                    |> Sg.andAlso (
                        TransformationApp.Sg.viewObjectSpace surf.transformation
                        |> Sg.trafo trafo
                        //|> Sg.dynamic
                    )    
                    // surface reference system (global coords)
                    |> Sg.andAlso (
                        TransformationApp.Sg.viewTrafoRefSys surf.transformation
                        //|> Sg.trafo trafo
                        //|> Sg.dynamic
                    )    
                return surfaceSg
            | _ -> 
                return Sg.empty

        } |> Sg.dynamic

    let getSimpleSingleSurfaceSg 
        (surface         : AdaptiveSgSurface) 
        (surfacesMap     : amap<Guid, AdaptiveLeafCase>)
        (frustum         : aval<Frustum>)
        (refsys          : AdaptiveReferenceSystem)
        (observedSystem : aval<Option<SpiceReferenceSystem>>) 
        (observerSystem : aval<Option<ObserverSystem>>)=

        adaptive {
            match! AMap.tryFind surface.surface surfacesMap with
            | Some (AdaptiveSurfaces surf) -> 

                let createSg (sg : ISg) =
                        sg 
                        |> Sg.noEvents 
                        |> Sg.cullMode(surf.cullMode)
                        |> Sg.fillMode(surf.fillMode)
            
                let triangleFilter = surf.triangleSize.value

                let trafo =
                        adaptive {
                            let! fullTrafo = TransformationApp.fullTrafo surf.transformation refsys observedSystem observerSystem
                            let! preTransform = surf.preTransform
                            let! flipZ = surf.transformation.flipZ
                            let! sketchFab = surf.transformation.isSketchFab
                            if flipZ then 
                                return Trafo3d.Scale(1.0, 1.0, -1.0) * (fullTrafo * preTransform)
                            else if sketchFab then
                                // TODO https://github.com/pro3d-space/PRo3D/issues/117
                                // i'm not sure whether swithcYZTrafo is the right one here. Firstly, i think we should change the naming (also in the UI).
                                // Secondly, do we need this as a third option: 
                                //return Trafo3d.FromOrthoNormalBasis(V3d.IOO,-V3d.OIO,-V3d.OOI)
                                // this was here before:
                                return Sg.switchYZTrafo
                            else
                                return (fullTrafo * preTransform)
                                //return Trafo3d.Scale(scaleFactor) * (fullTrafo * preTransform)
                        }
            
                let test =             
                  surface.sceneGraph
                    |> AVal.map createSg
                    |> Sg.dynamic
                    |> Sg.trafo trafo 
                    |> Sg.uniform "MaxTriangleSize"   triangleFilter 
                    |> Sg.onOff (surf.isVisible)
                    |> Sg.LodParameters( getLodParameters  (AVal.constant surf) refsys  observedSystem observerSystem frustum )
                    |> Sg.noEvents 
                    |> Sg.effect [
                        Shader.TriangleFilter.triangleFilter     |> toEffect
                        Shader.stableTrafo  |> toEffect 
                        Shader.OPCFilter.improvedDiffuseTexture |> toEffect
                    ]
                return test
            | _ -> 
                return Sg.empty
        } |> Sg.dynamic

    let getObserverSystem (m : AdaptiveModel) =
        Gis.GisApp.getObserverSystemAdaptive m.scene.gisApp

    let getSimpleSurfacesSg 
        (m:AdaptiveModel) =  
        let sgGrouped = m.scene.surfacesModel.sgGrouped 
        let surfs = m.scene.surfacesModel.surfaces.flat
        let refSystem = m.scene.referenceSystem
        let observerSystem = getObserverSystem m
            
        let surfacesToSg surfaces =
            surfaces
            |> AMap.map (fun guid sf -> 
                  let observationSystem = Gis.GisApp.getSpiceReferenceSystemAdaptive m.scene.gisApp guid
                  getSimpleSingleSurfaceSg sf surfs m.frustum refSystem observationSystem observerSystem
               )
            |> AMap.toASet 
            |> ASet.map snd    
            |> Sg.set

        let grouped = 
            sgGrouped |> AList.map surfacesToSg
        let sg = grouped |> AList.toASet |> Sg.set

        sg
    
    //let getVPResolution (m:AdaptiveModel) =
    //    adaptive {
    //        let! id = m.scene.viewPlans.selectedViewPlan
    //        match id with
    //        | Some id -> 
    //            let! selectedVp = m.scene.viewPlans.viewPlans |> AMap.find id
    //            let! inst = selectedVp.selectedInstrument
    //            let width, height =
    //                match inst with
    //                | Some i -> 
    //                    let horRes = i.intrinsics.horizontalResolution/uint32(2)
    //                    let vertRes = i.intrinsics.verticalResolution/uint32(2)
    //                    int(horRes), int(vertRes)
    //                | None -> 
    //                    512, 512
    //            return V2i(width, height)
    //        | None -> return V2i(512, 512)


    //        //match id with
    //        //| Some v -> 
    //        //    let! vp = m.scene.viewPlans.viewPlans |> AMap.tryFind v
    //        //    match vp with
    //        //    | Some selVp -> 
    //        //        let width, height =
    //        //            match selVp.selectedInstrument with
    //        //            | Some i -> 
    //        //                let horRes = i.intrinsics.horizontalResolution/uint32(2)
    //        //                let vertRes = i.intrinsics.verticalResolution/uint32(2)
    //        //                int(horRes), int(vertRes)
    //        //            | None -> 
    //        //                512, 512
    //        //        return V2i(width, height)
    //        //    | None -> return V2i(512, 512)
    //        //| None -> return V2i(512, 512)
        //}

    let getDepth 
        (m:AdaptiveModel) 
        (runtime : IRuntime) = 
        //let resolution = V3i (a.resolution.X, a.resolution.Y, 1)
        //

        let resolution = V2i(512, 512) |> AVal.constant //getVPResolution m

        let depthsignature = 
            runtime.CreateFramebufferSignature ([
                DefaultSemantic.Colors, TextureFormat.Rgba8
                DefaultSemantic.DepthStencil, TextureFormat.Depth24Stencil8
            ], 8)
        
        (getSimpleSurfacesSg m)
            |> Sg.compile runtime depthsignature
            |> RenderTask.renderToDepth resolution 
        
    //let frustum (m:AdaptiveModel) =
    //    let near = m.scene.config.nearPlane.value
    //    let far = m.scene.config.farPlane.value
    //    (Navigation.UI.frustum near far)

    module Shader =

        open FShade

        type Vertex = {
            [<Position>]        pos     : V4f
            [<Color>]           c       : V4f
            [<TexCoord>]        tc      : V2f

            [<Semantic("ViewSpacePos")>]
            vp : V4f

            [<Semantic("FootPrintProj")>]
            tc0     : V4f

            [<Normal>] 
            n : V3f

            [<SourceVertexIndex>]  sourceVertexIndex : int
        }

        let fixAlpha (v : Vertex) =
            fragment {
               return V4f(v.c.X, v.c.Y,v.c.Z, 1.0f)
            }

        type UniformScope with
            // size filter stuff
            member x.MaxTriangleSize : float = x?MaxTriangleSize
            member x.FilterTriangleEnabled : bool = x?FilterTriangleEnabled

            // filter for distance to home position
            member x.FilterByDistance : bool = x?FilterByDistance
            member x.FilterDistance : float32 = x?FilterDistance
            member x.HomePositionViewSpace : V3f = x?HomePositionViewSpace


        // performs all checks in view space
        let triangleSizeFilter (input : Triangle<Vertex>) =
            triangle {
                let p0 = input.P0.vp.XYZ
                let p1 = input.P1.vp.XYZ
                let p2 = input.P2.vp.XYZ

                // TriangleSize
                let maxSize = uniform?MaxTriangleSize

                let a = (p1 - p0)
                let b = (p2 - p1)
                let c = (p0 - p2)

                let alpha = a.Length < maxSize
                let beta  = b.Length < maxSize
                let gamma = c.Length < maxSize

                let filterDistanceActive : bool = uniform.FilterByDistance
                let disabled = not uniform.FilterTriangleEnabled
                let smallTriangle = alpha && beta && gamma
                // if disabled, let all trianlges pass
                let validTriangle = disabled || smallTriangle

                if filterDistanceActive then
                    let filterRange : float32 = uniform.FilterDistance
                    let homePositionVSp : V3f = uniform.HomePositionViewSpace

                    let inRange =
                        (Vec.distance homePositionVSp p0) < filterRange &&
                        (Vec.distance homePositionVSp p1) < filterRange &&
                        (Vec.distance homePositionVSp p2) < filterRange

                    if validTriangle && inRange then
                        yield { input.P0 with sourceVertexIndex = 0 } 
                        yield { input.P1 with sourceVertexIndex = 1 } 
                        yield { input.P2 with sourceVertexIndex = 2 } 
                else
                    if validTriangle then
                        yield { input.P0 with sourceVertexIndex = 0 } 
                        yield { input.P1 with sourceVertexIndex = 1 } 
                        yield { input.P2 with sourceVertexIndex = 2 } 
            }
         

        let stableTrafo (v : Vertex) =
            vertex {
                let p = uniform.ModelViewProjTrafo * v.pos

                return
                    { v with
                        pos = p
                        c = v.c
                        vp = uniform.ModelViewTrafo * v.pos
                    }
            }

        type UniformScope with
            member x.HasNormals : bool = x?HasNormals

        let private diffuseSampler =
            sampler2d {
                texture uniform.DiffuseColorTexture
                filter Filter.Anisotropic
                maxAnisotropy 16
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
            }
       
        let textureOrLightingIfPossible (v : Vertex) =
            fragment {
                if uniform.HasDiffuseColorTexture then
                    let texColor = diffuseSampler.Sample(v.tc,-1.0f) // TODO: to why is -1 being used here as lod offset?
                    return texColor
                else
                    if uniform.HasNormals then 
                        let ambient = 0.2f
                        let lView = V3f.OOO - v.vp.XYZ |> Vec.normalize
                        let nView = uniform.ModelViewTrafo.TransformDir(v.n) |> Vec.normalize
                        let diffuse = Vec.dot nView lView |> abs
                        return V4f(v.c.XYZ * diffuse + ambient * V3f.III, 1.0f)
                    else
                        return v.c
            }

       

    module CrossSectionShader =
        open FShade

        type CrossSectionVertex = {
            [<Semantic("InsideOutsideV4")>]
            insideOutside : V4f
        }

        type UniformScope with
            member x.CrossSectionClippingEnabled : bool = x?CrossSectionClippingEnabled

        let crossSectionClip (v : CrossSectionVertex) =
            fragment {
                if uniform.CrossSectionClippingEnabled && v.insideOutside.X < 0.0f then
                    discard()
                return v
            }

    module CurtainShader =
        open FShade

        type CurtainVertex = {
            [<Position>]                     pos        : V4f
            [<TexCoord>]                     tc         : V2f
            [<Semantic("ViewPos")>]          vp         : V4f
            [<Semantic("TotalDepth")>]       totalDepth : float32
            [<Semantic("SurfaceElevation")>] surfElev   : float32
        }

        type UniformScope with
            member x.UpVS : V3f = uniform?UpVS
            member x.TextureDepth : float32 = uniform?TextureDepth
            member x.CurtainBaseColor : V4f = uniform?CurtainBaseColor
            // 1 = absolute altitude-band texture mapping, 0 = surface-relative
            member x.CurtainAbsoluteMode : int = uniform?CurtainAbsoluteMode

        let curtainVertex (v : CurtainVertex) =
            vertex {
                let vp = uniform.ModelViewTrafo * v.pos
                return { v with vp = vp }
            }

        let curtainGeometry (line : Line<CurtainVertex>) =
            triangle {
                let up     = uniform.UpVS |> Vec.normalize
                // Per-vertex extrusion depths encoded in tc.Y by the CPU
                let depth0 = line.P0.tc.Y
                let depth1 = line.P1.tc.Y
                let offset0 = V4f(up * depth0, 0.0f)
                let offset1 = V4f(up * depth1, 0.0f)
                let p0 = line.P0.vp
                let p1 = line.P1.vp
                let u0 = line.P0.tc.X
                let u1 = line.P1.tc.X
                let elev0 = line.P0.surfElev
                let elev1 = line.P1.surfElev
                // top edge (surface, v=1); bottom edge (extruded down, v=0)
                yield { line.P0 with pos = uniform.ProjTrafo * p0;             tc = V2f(u0, 1.0f); totalDepth = depth0; surfElev = elev0 }
                yield { line.P0 with pos = uniform.ProjTrafo * (p0 - offset0); tc = V2f(u0, 0.0f); totalDepth = depth0; surfElev = elev0 }
                yield { line.P1 with pos = uniform.ProjTrafo * p1;             tc = V2f(u1, 1.0f); totalDepth = depth1; surfElev = elev1 }
                yield { line.P1 with pos = uniform.ProjTrafo * (p1 - offset1); tc = V2f(u1, 0.0f); totalDepth = depth1; surfElev = elev1 }
            }

        let private curtainSampler =
            sampler2d {
                texture uniform?DiffuseColorTexture
                filter Filter.MinMagMipLinear
                addressU WrapMode.Clamp
                addressV WrapMode.Clamp
            }

        let curtainFragment (v : CurtainVertex) =
            fragment {
                let texDepth  = uniform.TextureDepth
                let baseColor = uniform.CurtainBaseColor
                let totalD    = v.totalDepth
                // v.tc.Y = 1.0 at surface, 0.0 at bottom; distFromSurface grows downward
                let distFromSurface = (1.0f - v.tc.Y) * totalD
                if uniform.CurtainAbsoluteMode = 1 then
                    // Absolute mode: texture occupies the absolute altitude band
                    // [texStartAlt - texDepth, texStartAlt]. surfElev is pre-offset on
                    // CPU: (elevation - texStartAlt), so fragRelAlt = 0 at texStartAlt.
                    let fragRelAlt = v.surfElev - distFromSurface
                    if fragRelAlt <= 0.0f && fragRelAlt >= -texDepth && texDepth > 0.0f then
                        // V=1 at top (fragRelAlt=0), V=0 at bottom (fragRelAlt=-texDepth)
                        let t = (fragRelAlt + texDepth) / texDepth
                        return curtainSampler.Sample(V2f(v.tc.X, t))
                    else
                        return baseColor
                else
                    // Surface-relative mode: texture starts on the surface and drapes
                    // down texDepth metres, independent of absolute altitude.
                    if distFromSurface <= texDepth && texDepth > 0.0f then
                        // V=1 at surface (distFromSurface=0), V=0 at texDepth down
                        let t = 1.0f - distFromSurface / texDepth
                        return curtainSampler.Sample(V2f(v.tc.X, t))
                    else
                        return baseColor
            }

    let objEffect =
        Effect.compose [
            //Shader.footprintV       |> toEffect 
            Shader.stableTrafo      |> toEffect
            Shader.triangleSizeFilter  |> toEffect

            Shader.textureOrLightingIfPossible |> toEffect

            PRo3D.Base.OPCFilter.improvedDiffuseTextureAndColor |> toEffect
            Shader.mapColorAdaption  |> toEffect
            PRo3D.Base.Shader.mapRadiometry |> toEffect
            Shader.fixAlpha          |> toEffect
        ]

    let surfaceEffect =
        Effect.compose [

            // image projection
            PRo3D.SPICE.Shaders.planetLocalLightingViewSpace   |> toEffect
            ImageProjection.Shaders.stableImageProjectionTrafo |> toEffect
            PRo3D.SPICE.Shaders.transformShadowVertices |> toEffect
            
            
            Shaders.donutVertex |> toEffect
            Shader.footprintV        |> toEffect 
            Shader.stableTrafo       |> toEffect
            Shader.triangleSizeFilter   |> toEffect
            
            ImageProjection.Shaders.generateNormal |> toEffect
           
            Shader.fixAlpha |> toEffect
            PRo3D.Base.OPCFilter.improvedDiffuseTexture |> toEffect  
            PRo3D.Base.OPCFilter.markPatchBorders |> toEffect 
           
            
            // selection coloring makes gamma correction pointless. remove if we are happy with markPatchBorders
            // Shader.selectionColor          |> toEffect
            //PRo3D.Base.Shader.differentColor   |> toEffect
                        
            OpcViewer.Base.Shader.LoDColor.LoDColor |> toEffect                             
            //PRo3D.Base.Shader.falseColorsScalars |> toEffect
            PRo3D.Base.Shader.mapColorAdaption  |> toEffect  
            PRo3D.Base.Shader.mapRadiometry |> toEffect

            Shader.secondaryTexture |> toEffect 

            Shader.contourLines |> toEffect
            Shaders.donutFragment |> toEffect

            CrossSectionShader.crossSectionClip |> toEffect

            //PRo3D.Base.Shader.depthImageF        |> toEffect
            PRo3D.Base.Shader.depthCalculation2     |> toEffect //depthImageF        |> toEffect

            PRo3D.Base.Shader.footPrintF        |> toEffect

            // TODO HERA: make this optional
            ImageProjection.Shaders.stableImageProjection |> toEffect

            if not Config.limitedShaderCapabilities then
                ImageProjection.Shaders.localImageProjections |> toEffect

            PRo3D.SPICE.Shaders.solarLighting |> toEffect
        ]
        //Effect.compose [
            
        //    Shader.stableTrafo       |> toEffect
           

        //    PRo3D.Base.OPCFilter.improvedDiffuseTexture |> toEffect  
        //    PRo3D.Base.OPCFilter.markPatchBorders |> toEffect 


        //    OpcViewer.Base.Shader.LoDColor.LoDColor |> toEffect                             
        //]

    let isViewPlanVisible (m:AdaptiveModel) =
        adaptive {
            let! id = m.scene.viewPlans.selectedViewPlan
            match id with
            | Some v -> 
                let! vp = m.scene.viewPlans.viewPlans |> AMap.tryFind v
                match vp with
                | Some selVp -> return! selVp.isVisible
                | None -> return false
            | None -> return false
        }


    //TODO TO refactor screenshot specific
    let getSurfacesScenegraphs (runtime : IRuntime) (m : AdaptiveModel) =
        let sgGrouped = m.scene.surfacesModel.sgGrouped
        
      //  let renderCommands (sgGrouped:alist<amap<Guid,AdaptiveSgSurface>>) overlayed depthTested (m:AdaptiveModel) =
        let usehighlighting = true |> AVal.constant //m.scene.config.useSurfaceHighlighting
        let selected = m.scene.surfacesModel.surfaces.singleSelectLeaf
        let refSystem = m.scene.referenceSystem
        let vpVisible = isViewPlanVisible m
        let view = m.navigation.camera.view
        let observerSystem = Gis.GisApp.getObserverSystemAdaptive m.scene.gisApp

        let grouped = 
            sgGrouped |> AList.map(
                fun x -> ( x 
                    |> AMap.map(fun guid sf -> 
                        let surfaces = m.scene.surfacesModel.surfaces.flat
                        let observationSystem = Gis.GisApp.getSpiceReferenceSystemAdaptive m.scene.gisApp guid
                        viewSingleSurfaceSg 
                            sf
                            m
                            surfaces 
                            m.frustum 
                            selected 
                            (AVal.map2 (&&) m.ctrlFlag m.inverseFlag)
                            m.scene.config.showPreviewIntersection
                            sf.globalBB 
                            refSystem 
                            observationSystem
                            observerSystem
                            m.footPrint 
                            vpVisible
                            usehighlighting m.filterTexture
                            true
                            false
                            (AVal.constant None)
                            view)
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
                        |> Sg.effect [surfaceEffect]
                        //|> Sg.uniform "LoDColor" (AVal.constant C4b.Gray)
                        |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring //()                        

                    yield  sg

                        //if i = c then //now gets rendered multiple times
                         // assign priorities globally, or for each anno and make sets
            
            }                              
        sgs

    let createGroupedSgs 
        (sgGrouped      :alist<amap<Guid,AdaptiveSgSurface>>) 
        (view           : aval<CameraView>)
        (allowFootprint : bool) 
        (allowDepthview : bool) 
        (m              : AdaptiveModel)  =

        let usehighlighting = ~~true //m.scene.config.useSurfaceHighlighting
        let filterTexture = ~~true
        //let mutable useTC = true
        //avoids kdtree intersections for certain interactions
        let surfacePicking = 
            m.interaction 
            |> AVal.map(fun x -> 
                match x with
                | Interactions.PickAnnotation | Interactions.PickLog -> false
                | _ -> true
            )

        let vpVisible = isViewPlanVisible m
        let selected = m.scene.surfacesModel.surfaces.singleSelectLeaf
        let refSystem = m.scene.referenceSystem
        //let view = m.navigation.camera.view


        let cursorWorldPos = 
            (m.surfaceIntersection, m.scene.config.previewIntersectionWorldSize.value, m.scene.config.showPreviewIntersection) 
            |||> AVal.map3 (fun hitPoint previewSize showIt -> 
                match hitPoint with
                | Some hitPoint when showIt -> Some (hitPoint.hitPoint, previewSize)
                | _ -> None
            )

        let validSurfacePriority v = 
            // only relevant in secondary pass with overlayed geometry to render "missing" traverses
            AVal.constant true

        let observerSystem = Gis.GisApp.getObserverSystemAdaptive m.scene.gisApp
                              
        let wrapGisData (surfaceId : Guid) (sg : ISg<_>) =
            let projectedTexture =  PRo3D.GIS.ProjectedImagesListAppHelper.getProjectedTexture m.scene.gisApp
            let imageProperties = PRo3D.GIS.ProjectedImagesListAppHelper.getProjectionVisualizationProperties m.scene.gisApp
            let surfaceReferenceSystem = Gis.GisApp.getSpiceReferenceSystemAdaptive m.scene.gisApp surfaceId

            sg
            |> Sg.applyProjectedImages (fun body -> 
                body 
                |> AVal.map (function 
                    | Some b -> 
                        let r = PRo3D.GIS.ProjectedImagesListAppHelper.getProjectedImageData m.scene.gisApp surfaceId "MARS"
                        r
                    | _ -> None 
                )
            )
            |> Sg.texture "ProjectedTexture" projectedTexture
            |> Sg.uniform' "ProjectedImageModelViewProjValid" (PRo3D.GIS.ProjectedImagesListAppHelper.getSelectedImage  m.scene.gisApp.projectedImageList |> AVal.map Option.isSome)
            |>  PRo3D.InstrumentVisualization.InstrumentImageVisualization.applyProperties {  imageProperties with instrumentImage = projectedTexture }
            |> Sg.noEvents


        // Compute cross-section clipping data from scene-level cross section model
        let crossSectionData : aval<Option<Sg.CrossSectionData>> =
            m.scene.crossSectionModel.crossSection
            |> AVal.bind (fun csOpt ->
                match csOpt with
                | None -> AVal.constant None
                | Some cs ->
                    m.scene.referenceSystem.planet |> AVal.map (fun planet ->
                        match cs.geometry with
                        | LineOnSurface points ->
                            if points.Length >= 2 then
                                let refPoint = cs.refPoint
                                let up = CooTransformation.getUpVector (points.[0]) planet
                                let basis = PRo3D.Base.Annotation.CrossSection.buildBasisFromUp up
                                match PRo3D.Base.Annotation.CrossSection.buildPolygon basis points refPoint with
                                | Some poly ->
                                    Some { Sg.CrossSectionData.polygon = poly; basis = basis }
                                | None -> None
                            else None
                    )
            )

        sgGrouped
        |> AList.map (fun group ->

            let surfaces =
                group
                |> AMap.map(fun guid surface ->


                    let observationSystem = Gis.GisApp.getSpiceReferenceSystemAdaptive m.scene.gisApp guid
                    let s =
                        viewSingleSurfaceSg 
                            surface 
                            m
                            m.scene.surfacesModel.surfaces.flat
                            m.frustum 
                            selected 
                            surfacePicking
                            m.scene.config.showPreviewIntersection
                            surface.globalBB
                            refSystem 
                            observationSystem
                            observerSystem
                            m.footPrint 
                            vpVisible
                            usehighlighting filterTexture
                            allowFootprint
                            allowDepthview
                            cursorWorldPos
                            view


                    let surfaceSg =
                        match surface.isObj with
                        | true ->
                            s
                            |> Sg.effect [objEffect]
                        | false ->
                            s
                            |> Sg.effect [surfaceEffect] 
                            |> Sg.uniform "LoDColor" (AVal.constant C4b.Gray)
                            |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring


                    surfaceSg
                    |> wrapGisData guid
                )

            let depthComposed = 
                group.Content
                |> AVal.map (fun surfaces -> 
                    match surfaces |> HashMap.toValueSeq |> Seq.tryHead with
                    | None -> Sg.empty
                    | Some someSurf -> 
                        let priority = 
                            AMap.tryFind someSurf.surface m.scene.surfacesModel.surfaces.flat
                            |> AVal.bind (function
                                | (Some (AdaptiveSurfaces s)) -> 
                                    s.priority.value |> AVal.map (int >> Some) 
                                | _ -> AVal.constant None
                            )
                        TraverseApp.Sg.view view m.scene.config.nearPlane.value (m.frustum |> AVal.map Frustum.horizontalFieldOfViewInDegrees) refSystem m.scene.traverses priority validSurfacePriority false
                        |> Sg.map ViewerAction.TraverseMessage
                )
                |> Sg.dynamic

            let surfaces =
                surfaces
                |> AMap.toASet
                |> ASet.map snd
                |> Sg.set
                |> Sg.uniform "CrossSectionClippingEnabled" m.scene.crossSectionModel.clippingEnabled
                |> Sg.applyCrossSection crossSectionData
                |> Sg.noEvents

            Sg.ofList [surfaces; depthComposed]
        )

    let createCurtainSg (view : aval<CameraView>) (m : AdaptiveModel) : ISg<ViewerAction> =
        let curtainSgOpt =
            AVal.custom (fun token ->
                let csm        = m.scene.crossSectionModel
                let enabled    = csm.curtainEnabled.GetValue(token)
                let texPath    = csm.curtainTexturePath.GetValue(token)
                let depth      = csm.curtainExtrusionDepth.value.GetValue(token)
                let absMode    = csm.curtainAbsoluteMode.GetValue(token)
                let targetAlt  = csm.curtainTargetAltitude.value.GetValue(token)
                let texDepth   = csm.curtainTextureDepth.value.GetValue(token)
                let texStartAlt = csm.curtainTextureStartAltitude.value.GetValue(token)
                let baseColor  = csm.curtainBaseColor.c.GetValue(token)
                let csOpt      = csm.crossSection.GetValue(token)
                let planet     = m.scene.referenceSystem.planet.GetValue(token)
                let camView    = view.GetValue(token)

                if not enabled then Sg.empty
                else
                    match texPath with
                    | None -> Sg.empty
                    | Some path when not (System.IO.File.Exists path) -> Sg.empty
                    | Some path ->
                        match csOpt with
                        | None -> Sg.empty
                        | Some cs ->
                            let ptsWS =
                                match cs.geometry with
                                | LineOnSurface pts -> pts
                            if ptsWS.Length < 2 then Sg.empty
                            else
                            let n        = ptsWS.Length
                            let modelTrafo = Trafo3d.Translation(ptsWS.[0])
                            let ptsLocal   = ptsWS |> Array.map modelTrafo.Backward.TransformPos

                            // Arc-length UV
                            let segs = ptsLocal |> Array.pairwise |> Array.map (fun (a,b) -> Vec.distance a b)
                            let cum  = Array.zeroCreate n
                            let mutable s = 0.0
                            for i in 1 .. n-1 do
                                s <- s + segs.[i-1]
                                cum.[i] <- s
                            let total = max 1e-12 s
                            let u = cum |> Array.map (fun x -> float32 (x / total))

                            // Geometry: LineList
                            let positions : V3f[] = ptsLocal |> Array.map V3f

                            // Per-vertex extrusion depth encoded in tc.Y
                            let depths : float32[] =
                                if absMode then
                                    ptsWS |> Array.map (fun p ->
                                        // failure -> depth 0 for that vertex (tryGet API
                                        // replaced the old failure-as-zero getElevation')
                                        let alt = CooTransformation.tryGetElevation planet p |> Option.defaultValue targetAlt
                                        float32 (max 0.0 (alt - targetAlt)))
                                else
                                    Array.create n (float32 depth)

                            // Per-vertex surface elevation relative to texture start altitude
                            // (offset to keep values small and avoid float32 precision issues)
                            let surfElevs : float32[] =
                                ptsWS |> Array.map (fun p ->
                                    let alt = CooTransformation.tryGetElevation planet p |> Option.defaultValue texStartAlt
                                    float32 (alt - texStartAlt))

                            let texcoords : V2f[] = Array.map2 (fun (uu : float32) (d : float32) -> V2f(uu, d)) u depths
                            let indices : int[] =
                                Array.init ((n-1)*2) (fun k ->
                                    let i = k / 2
                                    if k % 2 = 0 then i else i + 1)
                            let ig =
                                IndexedGeometry(
                                    Mode = IndexedGeometryMode.LineList,
                                    IndexedAttributes = SymDict.ofList [
                                        DefaultSemantic.Positions,               (positions :> Array)
                                        DefaultSemantic.DiffuseColorCoordinates, (texcoords :> Array)
                                        Sym.ofString "SurfaceElevation",         (surfElevs :> Array)
                                    ],
                                    IndexArray = indices
                                )

                            // Up vector in view space
                            let upWorld = CooTransformation.getUpVector ptsWS.[0] planet
                            let viewTrafo = camView |> CameraView.viewTrafo
                            let upVS = viewTrafo.TransformDir upWorld |> V3f

                            Sg.ofIndexedGeometry ig
                            |> Sg.trafo (AVal.constant modelTrafo)
                            |> Sg.shader {
                                do! CurtainShader.curtainVertex
                                do! CurtainShader.curtainGeometry
                                do! CurtainShader.curtainFragment
                            }
                            |> Sg.fileTexture DefaultSemantic.DiffuseColorTexture path true
                            |> Sg.uniform "UpVS" (AVal.constant upVS)
                            |> Sg.uniform "TextureDepth" (AVal.constant (float32 texDepth))
                            |> Sg.uniform "CurtainAbsoluteMode" (AVal.constant (if absMode then 1 else 0))
                            |> Sg.uniform "CurtainBaseColor" (AVal.constant (baseColor.ToC4f().ToV4f()))
                            |> Sg.cullMode (AVal.constant CullMode.None)
                            |> Sg.noEvents
            )
        curtainSgOpt |> Sg.dynamic

    let renderCommands
        (sgGrouped      :alist<amap<Guid,AdaptiveSgSurface>>) 
        (overlayed      : ISg<ViewerAction>)
        (depthTested    : ISg<ViewerAction>)
        (view           : aval<CameraView>)
        (allowFootprint : bool) 
        (allowDepthview : bool) 
        (runtime        : IRuntime) 
        (m              : AdaptiveModel)  =

        let grouped = createGroupedSgs sgGrouped view allowFootprint allowDepthview m



        alist {                    
            for sg in grouped do
                yield RenderCommand<_>.ClearDepth 1.0
                yield RenderCommand<_>.ClearDepth 1.0
                yield RenderCommand<_>.Render sg

            yield RenderCommand<_>.Render depthTested
            yield RenderCommand<_>.ClearDepth 1.0

            yield RenderCommand<_>.Render overlayed

        }

        //alist {
        //    for set in grouped do
        //        let sg = set|> Sg.set
        //            |> Sg.effect [surfaceEffect]
        //            |> Sg.uniform "LoDColor" (AVal.constant C4b.Gray)
        //            |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring

        //        yield RenderCommand.SceneGraph sg

        //    yield RenderCommand.SceneGraph depthTested
        //    yield Aardvark.UI.RenderCommand.Clear(None,Some (AVal.constant 1.0), None)

        //    yield RenderCommand.SceneGraph overlayed

        //}


module Jezero =
    open PRo3D.Base

    open Aether
    open Aether.Operators
    
    let galeBounds = Box2i(V2i(3,9), V2i(19,16))
    let isJezero x = x.importPath |> String.contains "Jezero"
    

    let _translation = Surface.transformation_ >-> Transformations.translation_ >-> V3dInput.value_
    let _quality = Surface.quality_ >-> NumericInput.value_

    let hack surfaces =

        let surfaces =
            surfaces                         
            |> IndexList.filter(fun x -> 
                if isJezero x then
                    let parsedPath = 
                        x.importPath 
                        |> Path.GetFileName 
                        |> String.split "_"
                        |> Array.toList
                    
                    //let gridCoord = new V2i((parsedPath.[1] |> Int32.Parse), (parsedPath.[2] |> Int32.Parse))
                    //galeBounds.Contains(gridCoord)   
                    true
                else
                    true
            )
            |> IndexList.map(fun x ->
                if isJezero x then
                    x                     
                    |> Optic.set _quality (1.0)
                else    
                    x
            )
        surfaces

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
                        |> String.split "_"
                    
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

module Keyboard =
    open Aardvark.Application
    open System.Runtime.InteropServices

    let isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)

    let (|Modifier|_|) (k : Keys) =
        if k = Keys.LeftCtrl then Some Modifier
        elif k = Keys.LeftAlt then Some Modifier
        elif int k = 70 && isMac then Some Modifier
        else None