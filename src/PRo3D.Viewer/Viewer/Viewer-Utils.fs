namespace PRo3D

open System
open System.IO

open Aardvark.Base
open Aardvark.Base.Geometry
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open FShade
open Aardvark.Rendering.Effects
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open Aardvark.UI.Animation
open Aardvark.Rendering.Text


open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.SgPrimitives.Sg
open Aardvark.GeoSpatial.Opc
open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Viewer
open PRo3D.SimulatedViews

open Adaptify.FSharp.Core

module ViewerUtils =    
    type Self = Self

    let mapRenderCommand rc =
        match rc with
            | SceneGraph sg -> 
                RenderCommand.SceneGraph (sg |> Sg.map ViewerMessage)
            | RenderCommand.Clear (a,b,c) -> 
                RenderCommand<ViewerAnimationAction>.Clear (a,b,c)

    let mapAttribute (f : 'msg -> 'newmsg) (a : Attribute<'msg>) =
        let (str, a) = a
        let avalue = AttributeValue.map f a
        Aardvark.UI.Attribute (str, avalue)

    //let _surfaceModelLens = Model.Lens.scene |. Scene.Lens.surfacesModel
    //let _flatSurfaces = Scene.Lens.surfacesModel |. SurfaceModel.Lens.surfaces |. GroupsModel.Lens.flat
        
    let colormap = 
        let config = { wantMipMaps = false; wantSrgb = false; wantCompressed = false }
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
            |> Sg.uniform "lowerBound"     fp.depthColorLegend.lowerBound.value 
            |> Sg.uniform "upperBound"     fp.depthColorLegend.upperBound.value
            |> Sg.uniform "MinMax"         (AVal.constant(V2d(0.0,1.0)))
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
            let! trafo =  TransformationApp.fullTrafo s.transformation refsys //SurfaceTransformations.fullTrafo surf refsys
            
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
                | None -> None
            
            let texture = s.primaryTexture 
            let attr : AttributeParameters = 
                {
                    selectedTexture = texture |> Option.map(fun x -> x.index)
                    selectedScalar  = scalar'
                }

            attr    

    type Vertex = {
        [<Position>]        pos     : V4d
        [<Color>]           c       : V4d
        [<TexCoord>]        tc      : V2d
        [<Semantic("ViewSpacePos")>]  vp    : V4d
        [<Semantic("FootPrintProj")>] tc0   : V4d
        [<Semantic("DepthTex")>]      tc1   : V4d
    }

        
    let viewSingleSurfaceSg 
        (surface         : AdaptiveSgSurface) 
        (surfacesMap     : amap<Guid, AdaptiveLeafCase>)
        (frustum         : aval<Frustum>) 
        (selectedId      : aval<Option<Guid>>)
        (surfacePicking  : aval<bool>)
        (globalBB        : aval<Box3d>) 
        (refsys          : AdaptiveReferenceSystem)
        (surfaceToGlobal : Option<AdaptiveSgSurface -> AdaptiveSurface -> AdaptiveReferenceSystem -> aval<Trafo3d>>)
        (fp              : AdaptiveFootPrint) 
        (vpVisible       : aval<bool>)
        (useHighlighting : aval<bool>)
        (filterTexture   : aval<bool>)
        (allowFootprint  : bool)  
        (allowDepthview  : bool) 
        (view            : aval<CameraView>) =

        adaptive {
            match! AMap.tryFind surface.surface surfacesMap with
            | Some (AdaptiveSurfaces surf) -> 

                let isSelected = 
                    (selectedId, useHighlighting) ||> AVal.map2(fun x y ->
                        match x with
                        | Some id -> (id = surface.surface) && y
                        | None -> false
                    )
                
                let createSg (sg : ISg) =
                    sg 
                    |> Sg.noEvents 
                    |> Sg.cullMode(surf.cullMode)
                    |> Sg.fillMode(surf.fillMode)
                                                
                let triangleFilter = surf.triangleSize.value

                let trafo =
                    adaptive {
                        let! fullTrafo = TransformationApp.fullTrafo surf.transformation refsys
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
                    filterTexture 
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

               
                let homePositionViewSpace =
                    adaptive {
                        let! homePosition = surf.homePosition
                        
                        match homePosition with
                        | Some hp -> 
                            let! view' = view
                            let mv = (view' |> CameraView.viewTrafo).Forward
                            return (mv.TransformPos hp.Location)
                        | None ->
                            let! bb = surface.globalBB
                            return bb.Center                        
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

                let surfaceToSolarSystem =
                    match surfaceToGlobal with
                    | None -> Trafo3d.Identity |> AVal.constant
                    | Some f -> 
                        f surface surf refsys

                let surfaceSg =
                    surface.sceneGraph
                    |> AVal.map createSg
                    |> Sg.dynamic
                    |> Sg.trafo trafo //(Transformations.fullTrafo surf refsys)
                    |> Sg.trafo surfaceToSolarSystem
                    |> Sg.modifySamplerState DefaultSemantic.DiffuseColorTexture samplerDescription
                    |> Sg.uniform "selected"      (isSelected) // isSelected
                    |> Sg.uniform "selectionColor" (AVal.constant (C4b (200uy,200uy,255uy,255uy)))
                    //|> addAttributeFalsecolorMappingParameters surf
                    |> addDepthMappingParameters fp
                    |> Sg.uniform "TriangleSize"   triangleFilter  //triangle filter
                    |> Sg.uniform "HomePositionViewSpace" homePositionViewSpace
                    |> Sg.uniform "FilterByDistance" filterByDistance
                    |> Sg.uniform "FilterDistance" (surf.filterDistance.value)
                    |> addImageCorrectionParameters  surf
                    |> addRadiometryParameters surf
                    |> Sg.uniform "DepthVisible" depthVisible
                    |> Sg.uniform "FootprintVisible" footprintVisible
                    |> Sg.uniform "FootprintModelViewProj" (M44d.Identity |> AVal.constant)
                    |> Sg.applyFootprint footprintViewProj
                    |> Sg.noEvents
                    |> Sg.texture (Sym.ofString "ColorMapTexture") (AVal.constant colormap)
                    |> Sg.texture (Sym.ofString "FootPrintTexture") fp.projTex
                    |> Sg.LodParameters( getLodParameters  (AVal.constant surf) refsys frustum )
                    |> Sg.AttributeParameters( attributeParameters  (AVal.constant surf) )
                    
                    |> SecondaryTexture.Sg.applySecondaryTextureId (
                            surf.secondaryTexture 
                            |> AVal.map (function
                                | None -> -1
                                | Some s -> s.index
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
                        SceneEventKind.Click, (
                           fun sceneHit -> 
                             let name  = surf.name |> AVal.force        
                             let surfacePicking = surfacePicking |> AVal.force
                             //Log.warn "[SurfacePicking] spawning picksurface action %s" name //TODO remove spanwning altogether when interaction is not "PickSurface"
                             true, Seq.ofList [PickSurface (sceneHit, name, surfacePicking)])
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
                        surf.transformation |> TransformationApp.Sg.view
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
        (refsys          : AdaptiveReferenceSystem) =

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
                            let! fullTrafo = TransformationApp.fullTrafo surf.transformation refsys
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

                let triangleFilterX (input : Triangle<Vertex>) =
                    triangle {
                        let p0 = input.P0.pos.XYZ
                        let p1 = input.P1.pos.XYZ
                        let p2 = input.P2.pos.XYZ

                        let maxSize = uniform?TriangleSize

                        let a = (p1 - p0)
                        let b = (p2 - p1)
                        let c = (p0 - p2)

                        let alpha = a.Length < maxSize
                        let beta  = b.Length < maxSize
                        let gamma = c.Length < maxSize

                        let check = (alpha && beta && gamma)
                        if check then
                            yield input.P0 
                            yield input.P1
                            yield input.P2
                    }
            
                let test =             
                  surface.sceneGraph
                    |> AVal.map createSg
                    |> Sg.dynamic
                    |> Sg.trafo trafo 
                    |> Sg.uniform "TriangleSize"   triangleFilter 
                    |> Sg.onOff (surf.isVisible)
                    |> Sg.LodParameters( getLodParameters  (AVal.constant surf) refsys frustum )
                    |> Sg.noEvents 
                    |> Sg.effect [
                        triangleFilterX     |> toEffect
                        Shader.stableTrafo  |> toEffect 
                        Shader.OPCFilter.improvedDiffuseTexture |> toEffect
                    ]
                return test
            | _ -> 
                return Sg.empty
        } |> Sg.dynamic

    let getSimpleSurfacesSg 
        (m:AdaptiveModel) =  
        let sgGrouped = m.scene.surfacesModel.sgGrouped 
        let surfs = m.scene.surfacesModel.surfaces.flat
        let refSystem = m.scene.referenceSystem
            
        let surfacesToSg surfaces =
            surfaces
              |> AMap.map (fun guid sf -> getSimpleSingleSurfaceSg sf surfs m.frustum refSystem)
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
            [<FShade.InstrinsicAttributes.Position>]        pos     : V4d
            [<WorldPosition>]   wp      : V4d
            [<Color>]           c       : V4d
            [<TexCoord>]        tc      : V2d

            [<Semantic("ViewSpacePos")>] 
            vp : V4d

            [<Semantic("FootPrintProj")>] 
            tc0     : V4d

            [<Normal>] n : V3d
            //[<SourceVertexIndex>]  sv      : int
        }

        //let stableTrafo (v : Vertex) =
        //      vertex {
        //          let mvp : M44d = uniform?MVP?ModelViewTrafo
        //          let vp = mvp * v.pos

        //          return 
        //              { v with
        //                  pos = uniform.ProjTrafo * vp
        //                  c = v.c
        //                  vp = uniform.ModelViewTrafo * v.pos
        //              }
        //      }

        let fixAlpha (v : Vertex) =
            fragment {         
               return V4d(v.c.X, v.c.Y,v.c.Z, 1.0)           
            }

        let triangleFilterX (input : Triangle<Vertex>) =
            triangle {
                let p0 = input.P0.vp.XYZ
                let p1 = input.P1.vp.XYZ
                let p2 = input.P2.vp.XYZ

                let maxSize = uniform?TriangleSize

                let a = (p1 - p0)
                let b = (p2 - p1)
                let c = (p0 - p2)

                let alpha = a.Length < maxSize
                let beta  = b.Length < maxSize
                let gamma = c.Length < maxSize

                let filterDistanceActive : bool = uniform?FilterByDistance
                let triangleSizeCheck = (alpha && beta && gamma)

                if filterDistanceActive then
                    let filterRange : float = uniform?FilterDistance
                    let homePositionVSp : V3d = uniform?HomePositionViewSpace

                    let inRange = 
                        (Vec.Distance(homePositionVSp, p0)) < filterRange &&
                        (Vec.Distance(homePositionVSp, p1)) < filterRange &&
                        (Vec.Distance(homePositionVSp, p2)) < filterRange

                    if triangleSizeCheck && inRange then
                        yield input.P0 
                        yield input.P1
                        yield input.P2
                else
                    if triangleSizeCheck then
                        yield input.P0 
                        yield input.P1
                        yield input.P2
            }
         

        let stableTrafo (v : Vertex) =
            vertex {
                let p = uniform.ModelViewProjTrafo * v.pos
                let wp = uniform.ModelTrafo * v.pos

                return 
                    { v with
                        pos = p
                        c = v.c
                        vp = uniform.ModelViewTrafo * v.pos
                        wp = wp
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
                    let texColor = diffuseSampler.Sample(v.tc,-1.0) // TODO: to why is -1 being used here as lod offset?
                    return texColor
                else
                    if uniform.HasNormals then 
                        let ambient = 0.2
                        let lView = V3d.OOO - v.vp.XYZ |> Vec.normalize
                        let nView = uniform.ModelViewTrafo.TransformDir(v.n) |> Vec.normalize
                        let diffuse = Vec.dot nView lView |> abs
                        return V4d(v.c.XYZ * diffuse + ambient * V3d.III, 1.0)
                    else
                        return v.c
            }

       

    let objEffect =
        Effect.compose [
            //Shader.footprintV       |> toEffect 
            Shader.stableTrafo      |> toEffect
            Shader.triangleFilterX  |> toEffect

            Shader.textureOrLightingIfPossible |> toEffect

            PRo3D.Base.OPCFilter.improvedDiffuseTextureAndColor |> toEffect
            Shader.mapColorAdaption  |> toEffect   
            PRo3D.Base.Shader.mapRadiometry |> toEffect
            Shader.fixAlpha          |> toEffect
        ]

    let surfaceEffect =
        Effect.compose [
            
            Shader.footprintV        |> toEffect 
            Shader.stableTrafo       |> toEffect
            Shader.triangleFilterX   |> toEffect
           
           
            Shader.fixAlpha |> toEffect
            PRo3D.Base.OPCFilter.improvedDiffuseTexture |> toEffect  
            PRo3D.Base.OPCFilter.markPatchBorders |> toEffect 

           
            
            // selection coloring makes gamma correction pointless. remove if we are happy with markPatchBorders
            // Shader.selectionColor          |> toEffect
            //PRo3D.Base.Shader.differentColor   |> toEffect
                        
            OpcViewer.Base.Shader.LoDColor.LoDColor |> toEffect                             
            //PRo3D.Base.Shader.falseColorLegend2 |> toEffect
            PRo3D.Base.Shader.mapColorAdaption  |> toEffect  
            PRo3D.Base.Shader.mapRadiometry |> toEffect

            Shader.secondaryTexture |> toEffect 
            Shader.contourLines |> toEffect

            //PRo3D.Base.Shader.depthImageF        |> toEffect
            PRo3D.Base.Shader.depthCalculation2     |> toEffect //depthImageF        |> toEffect

            PRo3D.Base.Shader.footPrintF        |> toEffect
        ]

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
    let getSurfacesScenegraphs (runtime : IRuntime) (m:AdaptiveModel) =
        let sgGrouped = m.scene.surfacesModel.sgGrouped
        
      //  let renderCommands (sgGrouped:alist<amap<Guid,AdaptiveSgSurface>>) overlayed depthTested (m:AdaptiveModel) =
        let usehighlighting = true |> AVal.constant //m.scene.config.useSurfaceHighlighting
        let selected = m.scene.surfacesModel.surfaces.singleSelectLeaf
        let refSystem = m.scene.referenceSystem
        let vpVisible = isViewPlanVisible m
        let view = m.navigation.camera.view

        let surfaceToSunSystem (sg : AdaptiveSgSurface) (surface : AdaptiveSurface) (r : AdaptiveReferenceSystem) =
            //Gis.GisApp.computeSurfaceToViewerTrafo sg.
            AVal.constant Trafo3d.Identity

        let grouped = 
            sgGrouped |> AList.map(
                fun x -> ( x 
                    |> AMap.map(fun _ sf -> 
                        let surfaces = m.scene.surfacesModel.surfaces.flat

                        viewSingleSurfaceSg 
                            sf 
                            surfaces 
                            m.frustum 
                            selected 
                            m.ctrlFlag 
                            sf.globalBB 
                            refSystem 
                            (Some surfaceToSunSystem)
                            m.footPrint 
                            vpVisible
                            usehighlighting m.filterTexture
                            true
                            false
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
  
    //TODO TO refactor screenshot specific
    let getSurfacesSgWithCamera (runtime : IRuntime) (m : AdaptiveModel) =
        let sgs = getSurfacesScenegraphs runtime m
        let camera =
            AVal.map2 (fun v f -> Camera.create v f) m.scene.cameraView m.frustum 
        sgs 
            |> ASet.ofAList
            |> Sg.set
            |> (camera |> Sg.camera)

    let renderCommands 
        (sgGrouped:alist<amap<Guid,AdaptiveSgSurface>>) 
        overlayed 
        depthTested 
        (allowFootprint : bool) 
        (allowDepthview : bool) 
        (runtime : IRuntime) 
        (m:AdaptiveModel)  =

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
        let view = m.navigation.camera.view
        let grouped = 
            sgGrouped |> AList.map(
                fun x -> ( x 
                    |> AMap.map(fun _ surface ->   
                        let s =
                            viewSingleSurfaceSg 
                                surface 
                                m.scene.surfacesModel.surfaces.flat
                                m.frustum 
                                selected 
                                surfacePicking
                                surface.globalBB
                                refSystem 
                                m.footPrint 
                                vpVisible
                                usehighlighting filterTexture
                                allowFootprint
                                allowDepthview
                                view

                        match surface.isObj with
                        | true -> 
                            s 
                            |> Sg.effect [
                                objEffect
                            ] 
                        | false -> 
                            s
                            |> Sg.effect [surfaceEffect] 
                            |> Sg.uniform "LoDColor" (AVal.constant C4b.Gray)
                            |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring
                       )
                    |> AMap.toASet 
                    |> ASet.map snd                     
                )                 
            )

        //grouped   
        let last = grouped |> AList.tryLast
        
        alist {                    
            for set in grouped do  
                let sg = set|> Sg.set
                    //|> Sg.effect [surfaceEffect] 
                    //|> Sg.uniform "LoDColor" (AVal.constant C4b.Gray)
                    //|> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring

                yield RenderCommand.SceneGraph (sg)

                //if i = c then //now gets rendered multiple times
                 // assign priorities globally, or for each anno and make sets
                let depthTested =
                    last 
                    |> AVal.map (function 
                        | Some e when System.Object.ReferenceEquals(e,set) -> depthTested 
                        | _ -> Sg.empty
                    )
                yield RenderCommand.SceneGraph (depthTested |> Sg.dynamic)

                yield Aardvark.UI.RenderCommand.Clear(None,Some (AVal.constant 1.0), None)

            yield RenderCommand.SceneGraph overlayed

        }


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
                        |> String.split('_')
                    
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

module Keyboard =
    open Aardvark.Application
    open System.Runtime.InteropServices

    let isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)

    let (|Modifier|_|) (k : Keys) =
        if k = Keys.LeftCtrl then Some Modifier
        elif k = Keys.LeftAlt then Some Modifier
        elif int k = 70 && isMac then Some Modifier
        else None