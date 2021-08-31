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
open OpcViewer.Base.Shader
open PRo3D.Comparison

module ViewerUtils =    
    type Self = Self

    //let _surfaceModelLens = Model.Lens.scene |. Scene.Lens.surfacesModel
    //let _flatSurfaces = Scene.Lens.surfacesModel |. SurfaceModel.Lens.surfaces |. GroupsModel.Lens.flat
        
    let colormap = 
        let config = { wantMipMaps = false; wantSrgb = false; wantCompressed = false }
        let s = typeof<Self>.Assembly.GetManifestResourceStream("PRo3D.Viewer.resources.HueColorMap.png")
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
        
    let viewSingleSurfaceSg 
        (surface         : AdaptiveSgSurface) 
        (blarg           : amap<Guid, AdaptiveLeafCase>) // TODO v5: to get your naming right!!
        (frustum         : aval<Frustum>) 
        (selectedId      : aval<Option<Guid>>)
        (surfacePicking  : aval<bool>)
        (globalBB        : aval<Box3d>) 
        (refsys          : AdaptiveReferenceSystem)
        (fp              : AdaptiveFootPrint) 
        (vp              : aval<Option<AdaptiveViewPlan>>) 
        (useHighlighting : aval<bool>)
        (filterTexture   : aval<bool>)
        (comparisonApp   : AdaptiveComparisonApp) =

        adaptive {
            let! exists = (blarg |> AMap.keys) |> ASet.contains surface.surface
            if exists then
              
                let surf = SurfaceUtils.lookUp (surface.surface) blarg
                    //AVal.bind(fun x -> lookUp (x.surface) blarg )
                
                let isSelected = AVal.map2(fun x y ->
                    match x with
                    | Some id -> (id = surface.surface) && y
                    | None -> false) selectedId useHighlighting
                
                let createSg (sg : ISg) =
                    sg 
                    |> Sg.noEvents 
                    |> Sg.cullMode(surf |> AVal.bind(fun x -> x.cullMode))
                    |> Sg.fillMode(surf |> AVal.bind(fun x -> x.fillMode))
                
                let pickable = 
                    AVal.map2( fun (a:Box3d) (b:Trafo3d) -> 
                        { shape = PickShape.Box (a.Transformed(b)); trafo = Trafo3d.Identity }
                    ) globalBB (SurfaceTransformations.fullTrafo surf refsys)
                
                let pickBox = 
                    pickable 
                    |> AVal.map(fun k ->
                        match k.shape with
                        | PickShape.Box bb -> bb
                        | _ -> Box3d.Invalid)
                
                let triangleFilter = 
                    surf |> AVal.bind(fun s -> s.triangleSize.value)
                
                let trafo =
                    adaptive {
                        let! fullTrafo = SurfaceTransformations.fullTrafo surf refsys
                        let! s = surf
                        let! sc = s.scaling.value
                        let! t = s.preTransform
                        let! flipZ = s.transformation.flipZ
                        if flipZ then 
                            return Trafo3d.Scale(sc) * Trafo3d.Scale(1.0, 1.0, -1.0) * (fullTrafo * t)
                        else
                            return Trafo3d.Scale(sc) * (fullTrafo * t) 
                    }
                    
                let trafoObj =
                   adaptive {
                        let! s = surf
                        let! t = s.preTransform
                        return t
                    }

                
                let samplerDescription : aval<SamplerState -> SamplerState> = 
                    filterTexture |> AVal.map (fun filterTexture ->  
                        fun (x : SamplerState) -> 
                            match filterTexture with
                            | false -> { x with Filter = TextureFilter.MinLinearMagPoint }
                            | true -> { x with Filter = TextureFilter.MinMagLinear }  // HERA/MARS-DL, default for snapshots
                    )
                        
                let footprintVisible = //AVal.map2 (fun (vp:Option<AdaptiveViewPlan>) vis -> (vp.IsSome && vis)) vp, fp.isVisible
                    adaptive {
                        let! vp = vp
                        let! visible = fp.isVisible
                        let! id = fp.vpId
                        return (vp.IsSome && visible)
                    }
                
                let footprintMatrix = 
                    adaptive {
                        let! fppm = fp.projectionMatrix
                        let! fpvm = fp.instViewMatrix
                        let! s = surf
                        let! ts = s.preTransform
                        let! t = trafo
                        //return (t.Forward * fpvm * fppm) //* t.Forward 
                        return (fppm * fpvm) // * ts.Forward
                    } 

                //let measurementsSg =
                //    ComparisonApp.measurementsSg surf
                //                                 (pickBox |> AVal.map (fun x -> x.Size.[x.MajorDim]))
                //                                 trafo
                //                                 refsys
                //                                 comparisonApp

                let structuralOnOff (visible : aval<bool>) (sg : ISg<_>) : ISg<_> = 
                    visible 
                    |> AVal.map (fun visible -> 
                        if visible then sg else Sg.empty
                    )
                    |> Sg.dynamic
                
                let surfaceSg =
                    surface.sceneGraph
                    |> AVal.map createSg
                    |> Sg.dynamic
                    |> Sg.trafo trafo //(Transformations.fullTrafo surf refsys)
                    |> Sg.modifySamplerState DefaultSemantic.DiffuseColorTexture samplerDescription
                    |> Sg.uniform "selected"      (isSelected) // isSelected
                    |> Sg.uniform "selectionColor" (AVal.constant (C4b (200uy,200uy,255uy,255uy)))
                    |> addAttributeFalsecolorMappingParameters surf
                    |> Sg.uniform "TriangleSize"   triangleFilter  //triangle filter
                    |> addImageCorrectionParameters surf
                    |> Sg.uniform "footprintVisible" footprintVisible
                    |> Sg.uniform "instrumentMVP" footprintMatrix
                    |> Sg.uniform "projMVP" fp.projectionMatrix
                    |> Sg.uniform "globalToLocal" fp.globalToLocalPos
                    |> Sg.uniform "instViewMVP" fp.instViewMatrix
                    |> Sg.texture (Sym.ofString "FootPrintTexture") fp.projTex
                    |> Sg.LodParameters( getLodParameters surf refsys frustum )
                    |> Sg.AttributeParameters( attributeParameters surf )
                    |> OpcViewer.Base.Sg.pickable' pickable
                    |> Sg.noEvents 
                    |> Sg.withEvents [
                        SceneEventKind.Click, (
                           fun sceneHit -> 
                             let surfM = surf       |> AVal.force
                             let name  = surfM.name |> AVal.force        
                             let surfacePicking = surfacePicking |> AVal.force
                             //Log.warn "[SurfacePicking] spawning picksurface action %s" name //TODO remove spanwning altogether when interaction is not "PickSurface"
                             true, Seq.ofList [PickSurface (sceneHit, name, surfacePicking)])
                       ]  
                    // handle surface visibility
                    |> Sg.onOff (surf |> AVal.bind(fun x -> x.isVisible)) // on off variant
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
                    //|> Sg.andAlso (
                    //    measurementsSg
                    //)
                return surfaceSg
            else
                return Sg.empty
        } |> Sg.dynamic
        
    let frustum (m:AdaptiveModel) =
        let near = m.scene.config.nearPlane.value
        let far = m.scene.config.farPlane.value
        (Navigation.UI.frustum near far)


    let fixAlpha (v : Vertex) =
        fragment {         
           return V4d(v.c.X, v.c.Y,v.c.Z, 1.0)           
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

    let surfaceEffect =
        Effect.compose [
            Shader.stableTrafo             |> toEffect
            triangleFilterX                |> toEffect
            Shader.OPCFilter.improvedDiffuseTexture |> toEffect
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

    let groupedSceneGraphs (sgGrouped:alist<amap<Guid,AdaptiveSgSurface>>)  (m:AdaptiveModel) =
        let usehighlighting = true |> AVal.constant //m.scene.config.useSurfaceHighlighting
        let filterTexture = ~~true

        let comparisonSgAreas =  AreaSelection.sgAllAreas m.scene.comparisonApp.areas              
        let areaStatisticsSg = AreaComparison.sgAllDifferences m.scene.comparisonApp.areas

        //avoids kdtree intersections for certain interactions
        let surfacePicking = 
            m.interaction 
            |> AVal.map(fun x -> 
                match x with
                | Interactions.PickAnnotation | Interactions.PickLog -> false
                | _ -> true
            )

        let selected = m.scene.surfacesModel.surfaces.singleSelectLeaf
        let refSystem = m.scene.referenceSystem
        let grouped = 
            sgGrouped |> AList.map(
                fun x -> ( x 
                    |> AMap.map(fun _ surface ->                         
                        viewSingleSurfaceSg 
                            surface 
                            m.scene.surfacesModel.surfaces.flat
                            m.frustum 
                            selected 
                            surfacePicking
                            surface.globalBB
                            refSystem m.footPrint 
                            (AVal.map AdaptiveOption.toOption m.scene.viewPlans.selectedViewPlan) usehighlighting filterTexture
                            m.scene.comparisonApp
                       )
                    |> AMap.toASet 
                    |> ASet.map snd                     
                )                
            )
        grouped


    let renderCommands (sgGrouped:alist<amap<Guid,AdaptiveSgSurface>>) overlayed depthTested (m:AdaptiveModel) =
        let grouped = groupedSceneGraphs sgGrouped m
        let comparisonSgAreas =  AreaSelection.sgAllAreas m.scene.comparisonApp.areas              
        let areaStatisticsSg = AreaComparison.sgAllDifferences m.scene.comparisonApp.areas
        let last = grouped |> AList.tryLast
        alist {        
            for set in grouped do
                let sg = 
                    set 
                    |> Sg.set
                    |> Sg.effect [surfaceEffect]
                    |> Sg.uniform "LoDColor" (AVal.constant C4b.Gray)
                    |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring //()

                yield RenderCommand.SceneGraph sg

                //if i = c then //now gets rendered multiple times
                 // assign priorities globally, or for each anno and make sets
                let depthTested =
                    last |> AVal.map (function 
                        | Some e when System.Object.ReferenceEquals(e,set) -> depthTested 
                        | _ -> Sg.empty
                    )
                yield RenderCommand.SceneGraph (depthTested |> Sg.dynamic)
                yield RenderCommand.SceneGraph (areaStatisticsSg )
                yield RenderCommand.SceneGraph comparisonSgAreas

                yield Aardvark.UI.RenderCommand.Clear(None,Some (AVal.constant 1.0), None)

            yield RenderCommand.SceneGraph overlayed

        }

    let completeSceneGraph sgGrouped overlayed depthTested (viewportSize : V2i ) frustum (m:AdaptiveModel) =
        let grouped = groupedSceneGraphs sgGrouped m
        //let pass0 = RenderPass.main
        //let pass1 = RenderPass.after "pass1" RenderPassOrder.Arbitrary pass0

        let comparisonSgAreas =  AreaSelection.sgAllAreas m.scene.comparisonApp.areas              
        let areaStatisticsSg = AreaComparison.sgAllDifferences m.scene.comparisonApp.areas 

        let last = grouped |> AList.tryLast
        let surfaceSgs = 
          aset {
            for set in grouped do
              let sg = 
                  set 
                  |> Sg.set
                  |> Sg.effect [surfaceEffect]
                  |> Sg.uniform "LoDColor" (AVal.constant C4b.Gray)
                  |> Sg.uniform "LodVisEnabled" m.scene.config.lodColoring //()
              yield sg
          } |> Sg.set
           // |> Sg.pass pass0

        let sg = surfaceSgs
                  |> Sg.andAlso depthTested
                  |> Sg.andAlso overlayed

        let frustum = AVal.map2 (fun o f -> o |> Option.defaultValue f) m.overlayFrustum m.frustum 
        
        let camera =
            //AVal.map2 (fun v f -> Camera.create v f) m.navigation.camera.view frustum 
            AVal.map2 (fun v f -> Camera.create v f) m.scene.cameraView frustum 
        
        sg 
          |> Aardvark.SceneGraph.SgFSharp.Sg.camera camera


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