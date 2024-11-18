namespace PRo3D.Core.Drawing

open System

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering.Text

open Aardvark.UI
open Aardvark.UI.Primitives    

open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

open FShade

open Adaptify.FSharp.Core

module Sg =                             
      //TODO TO refactor formatting
    //open PRo3D.Surfaces.Mutable.SgSurfaceModule

    let stableLight = 
        Effect.compose [
            //do! Shader.screenSpaceScale
            toEffect Shader.StableTrafo.stableTrafo
            toEffect DefaultSurfaces.vertexColor
            toEffect Shader.StableLight.stableLight
        ]

    let discISg color size thickness trafo =
        Sg.cylinder 12 color size thickness              
          |> Sg.noEvents
          |> Sg.uniform "WorldPos" (trafo |> AVal.map(fun (x : Trafo3d) -> x.Forward.C3.XYZ))
          |> Sg.uniform "Size" size
          |> Sg.effect [stableLight]
          |> Sg.trafo(trafo)


    let coneISg color radius height trafo =  
        Sg.cone 12 color radius height
           |> Sg.noEvents         
           |> Sg.effect [stableLight]
           |> Sg.trafo(trafo) 
           
    type innerViewConfig =
        {
            nearPlane        : aval<float>
            hfov             : aval<float>                
            arrowThickness   : aval<float>
            arrowLength      : aval<float>
            dnsPlaneSize     : aval<float>
            offset           : aval<float>
            pickingTolerance : aval<float>
        }
    
    let drawTrueThicknessPlane (planeScale : aval<float>) (dnsResults : aval<option<AdaptiveDipAndStrikeResults>>) (cl : AdaptiveFalseColorsModel) =                         
        aset {                            
            let! dns = dnsResults
            match dns with
            | Some x -> 
                
                let color = FalseColorLegendApp.Draw.getColorDnS cl x.dipAngle                                                 

                let posTrafo = 
                    x.centerOfMass 
                    |> AVal.map Trafo3d.Translation
                
                // disc
                let discTrafo =
                    AVal.map2(fun (pln:Plane3d) pos -> 
                        (Trafo3d.RotateInto(V3d.ZAxis, pln.Normal) * pos)) 
                        x.plane 
                        posTrafo
                
                yield discISg color planeScale (planeScale |> AVal.map(fun d -> d * 0.01)) discTrafo
                                
            | None -> ()            
        } |> Sg.set
                                  
    let drawDns' 
        (points     : alist<V3d>) 
        (dnsResults : aval<option<AdaptiveDipAndStrikeResults>>) 
        (conf       : innerViewConfig) 
        (cl         : AdaptiveFalseColorsModel) =                         
        aset {                            
            let! dns = dnsResults
            match dns with
            | Some x -> 
                let center = points |> AList.toAVal |> AVal.map (fun list -> list.[IndexList.count list / 2])
                
                let color = FalseColorLegendApp.Draw.getColorDnS cl x.dipAngle
                     
                let lengthFactor = 
                    points
                    |> AList.toAVal 
                    |> AVal.map(fun x -> (x.AsList |> Calculations.getDistance) / 3.0)
                          
                let discRadius = conf.dnsPlaneSize |> AVal.map2 (*) lengthFactor
                let posTrafo = center |> AVal.map Trafo3d.Translation
                
                // disc
                let discTrafo =
                    AVal.map2(fun (pln:Plane3d) pos -> (Trafo3d.RotateInto(V3d.ZAxis, pln.Normal) * pos)) x.plane posTrafo
                
                yield discISg color discRadius (discRadius |> AVal.map(fun d -> d * 0.01)) discTrafo
                
                let lineLength = conf.arrowLength |> AVal.map2 (*) lengthFactor //discRadius |> AVal.map((*) 1.5)
                
                let coneHeight = lineLength |> AVal.map((*) 0.2) //lineLength
                let coneRadius = coneHeight |> AVal.map((*) 0.3)
                
                // dip arrow       
                let! lineLength' = lineLength
                let! center'     = center
                
                let! dip = x.dipDirection
                let dipLine = 
                  AVal.constant [| center'; center' + dip.Normalized * lineLength' |]
                
                yield Sg.drawScaledLines dipLine color conf.arrowThickness posTrafo 
                
                let coneTrafo = 
                  lineLength |>
                    AVal.map(fun s -> Trafo3d.RotateInto(V3d.ZAxis, dip) * Trafo3d.Translation(center' + dip.Normalized * s))
                
                yield coneISg color coneRadius coneHeight coneTrafo
                
                //strikes lines
                let! strike = x.strikeDirection
                let strikeLine1 =
                    AVal.constant [| center' - strike.Normalized * lineLength'; center' + strike.Normalized * lineLength'  |]

                //yield Sg.lines strikeLine1 (AVal.constant C4b.Red) conf.arrowThickness posTrafo anno.key
                yield Sg.drawScaledLines strikeLine1 (AVal.constant C4b.Red) conf.arrowThickness posTrafo
            | None -> ()            
        } |> Sg.set
        
    let drawDns 
        (anno   : AdaptiveAnnotation) 
        (conf   : innerViewConfig) 
        (cl     : AdaptiveFalseColorsModel) 
        (cam    : aval<CameraView>) =   
        drawDns' anno.points (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption anno.dnsResults) conf cl

    let getPolylinePoints (a : AdaptiveAnnotation) =
        //a.segments.Content 
        //    |> AVal.bind (fun segments -> 
        //        if IndexList.isEmpty segments then a.points |> AList.toAVal |> AVal.map IndexList.toArray
        //        else 
        //            segments |> IndexList.map (fun s -> 
                        
        //            )
        //    )
        AVal.custom (fun t -> 
            let segments = a.segments.Content.GetValue t
            if IndexList.isEmpty segments then  
                a.points.Content.GetValue(t) |> IndexList.toArray 
            else 
                let points = System.Collections.Generic.List<V3d>()
                a.segments.Content.GetValue(t) |> IndexList.iter(fun (s : AdaptiveSegment) -> 
                    points.Add(s.startPoint.GetValue(t))
                    for s in s.points.Content.GetValue(t) do points.Add(s)
                    points.Add(s.endPoint.GetValue(t))
                )
                points.ToArray()
        )
        //alist {                          
        //    let! hasSegments = (a.segments |> AList.count) |> AVal.map(fun x -> x > 0)
        //    if hasSegments |> not then
        //        yield! a.points
        //    else
        //        for segment in a.segments do
        //            let! startPoint = segment.startPoint
        //            let! endPoint = segment.endPoint
        //            yield  startPoint
        //            yield! segment.points
        //            yield  endPoint
        //}
    
    let mutable lastHash = -1

    let pickEventsHelper 
        (id              : aval<Guid>) 
        (currentlyActive : aval<bool>) 
        (pixelWidth      : aval<float>) 
        (model           : aval<Trafo3d>) 
        (edges           : aval<Line3d[]>) =

        SceneEventKind.Click, (
            fun (sceneHit : SceneHit) ->
                let id = id |> AVal.force
                let currentlyActive = currentlyActive |> AVal.force
                let lines = edges |> AVal.force
                let modelTrafo = model |> AVal.force
                let pixelWidth = pixelWidth |> AVal.force                        
        
                Log.line "[AnnotationPicking] pickable hit"

                let rayHash = sceneHit.globalRay.Ray.Ray.GetHashCode()

                if (rayHash = lastHash) then
                    Log.warn "[AnnotationPicking] detected duplicate picking interaction (rayhash)"
                    true, Seq.empty
                else
                    if lines.Length > 0 && currentlyActive then
                        Log.line "[AnnotationPicking] Pixel picking in progress"

                        let reallyHit = 
                             // TODO hs/to real horrorshow here!
                             lines 
                             |> Array.forany (fun e -> 
                                 let m = modelTrafo * sceneHit.event.evtView *  sceneHit.event.evtProj
                                 let r = sceneHit.localRay.Ray.Ray
                                 let a = Line3d(r.Origin, r.Origin + r.Direction * 10000.0)
                                 let hit = a.GetClosestPointOn(e)
     
                                 let p = m.Forward.TransformPosProj(hit)
                                 let c = p.XY * 0.5 + V2d.Half
                                 let pixel = V2d(c.X,1.0-c.Y) * V2d sceneHit.event.evtViewport
                                 let d = Vec.length (pixel - V2d sceneHit.event.evtPixel)
                                 d < pixelWidth * 2.0 // most lines are to thin to pick properly
                             )


                        // TODO hs/to picking refactoring (search for this string in order to find connected parts)
                        if reallyHit then
                            lastHash <- rayHash
                            Log.line "[AnnotationPicking] pixel picked %A" id
                            true, Seq.ofList [ PickAnnotation (sceneHit, (id)) ]
                        else
                            Log.line "[AnnotationPicking] no pixel picking hit"
                            true, Seq.empty // if no pick continue anyways. we are no blocker geometry
                    else 
                        true, Seq.empty
        )

    let drawWorkingAnnotation (offset : aval<float>) (anno : aval<Option<AdaptiveAnnotation>>)  = 
    
        let polyPoints =
            adaptive {
                let! anno = anno
                match anno with
                | Some a -> return! getPolylinePoints a
                | None -> return [||]
            }
    
        let points = 
            alist {
                let! anno = anno
                match anno with
                | Some a -> yield! a.points
                | None -> ()
            }    
            
        let color     = AVal.bindOption anno C4b.VRVisGreen   (fun a -> a.color.c) 
        let thickness = AVal.bindOption anno 1.0              (fun a -> a.thickness.value)             
        let trafo     = AVal.bindOption anno Trafo3d.Identity (fun a -> a.modelTrafo)                      
        
        Sg.ofList [
            Sg.drawLines polyPoints offset color thickness trafo //polyline
            //Sg.dots polyPoints (AVal.constant C4b.VRVisGreen) // debug sample points
            //Sg.drawSpheres points thickness color                     //support points
            PRo3D.Base.Sg.drawPointList 
              points 
              (C4b.VRVisGreen |> AVal.constant)//(color     |> AVal.map (fun x -> (x |> PRo3D.Sg.createSecondaryColor)))
              (thickness |> AVal.map (fun x -> x * 1.5)) 
              (offset    |> AVal.map (fun x -> x * 1.1))
        ]                                                               

    let computeCenterOfMass (points : list<V3d>) =
        let sum = points.Sum()
        let length = (double)points.Length

        sum / length

    let drawText' (view : aval<CameraView>) (conf: innerViewConfig) (text:aval<string>)(anno : AdaptiveAnnotation) = 
        let points = 
            anno.points 
            |> AList.toAVal
            
        let pos = 
            points 
            |> AVal.map(fun a -> 
                a 
                |> IndexList.toList 
                |> computeCenterOfMass
            )

        Sg.text view conf.nearPlane conf.hfov pos (pos |> AVal.map Trafo3d.Translation) anno.textsize.value text
    
    let drawText 
        (view : aval<CameraView>) 
        (conf: innerViewConfig) 
        (anno : AdaptiveAnnotation) = 

        drawText' view conf anno.text anno
    
    let optionalSet (sg : ISg<_>) (m : aval<bool>) : aset<ISg<_>> =
        adaptive {
            let! m = m 
            if m then return sg
            else return Sg.empty
        } |> ASet.ofAValSingle

    let optional (m : aval<bool>) (sg : ISg<_>)  : ISg<_> =
        adaptive {
            let! m = m 
            if m then return sg
            else return Sg.empty
        } |> Sg.dynamic

    let getDotsIsg (points : alist<V3d>) (size:aval<float>) (color : aval<C4b>) (geometry: aval<Geometry>) (offset : aval<float>) =
        aset {
            let! geometry = geometry
            match geometry with
            | Geometry.Point -> 
                match points|> AList.force |> IndexList.toList |> List.tryHead with
                | Some p -> 
                    yield Sg.dot color size  (AVal.constant p)
                | _ -> 
                    yield Sg.empty
            | _ -> 
                //let color = color |> AVal.map(fun x -> (x |> createSecondaryColor))
                yield Sg.drawPointList points (C4b.VRVisGreen |> AVal.constant) size (offset |> AVal.map(fun x -> x * 1.1))
        } 
        |> Sg.set  
        
    [<ObsoleteAttribute("Old way of drawing annotations. Use finishedAnnotation instead")>]
    let finishedAnnotationOld 
        (anno             : AdaptiveAnnotation)
        (color            : aval<C4b>) 
        (config           : innerViewConfig)
        (view             : aval<CameraView>) 
        (viewportSize     : aval<V2i>)
        (showPoints       : aval<bool>)         
        (picked           : aval<bool>)
        (pickingAllowed   : aval<bool>) =

        let points = getPolylinePoints anno      
        let dots = 
            showPoints 
            |> optionalSet (
                getDotsIsg
                    anno.points
                    (anno.thickness.value |> AVal.map(fun x -> x + 0.5))
                    color
                    anno.geometry 
                    config.offset
            )
     
        let texts = 
            anno.text 
            |> AVal.map2 (fun show text -> (String.IsNullOrEmpty text) || show ) anno.showText
            |> optionalSet (drawText view config anno)
    
        let dotsAndText = ASet.union' [dots; texts] |> Sg.set
                    
        let pickingAllowed = // for this particular annotation // whether should fire pick actions
            AVal.map2 (&&) pickingAllowed anno.visible

        let pickFunc = pickEventsHelper (anno.key |> AVal.constant) pickingAllowed anno.thickness.value anno.modelTrafo

        let pickingLines = 
            Sg.pickableLine 
                points 
                config.offset 
                color
                anno.thickness.value 
                config.pickingTolerance
                anno.modelTrafo 
                true
                pickFunc

        let vm = view |> AVal.map (fun v -> (CameraView.viewTrafo v).Forward)
             
        let selectionSg = 
            picked 
            |> AVal.map (function
                | true -> 
                    OutlineEffect.createForLineOrPoint 
                        view 
                        viewportSize 
                        PRo3D.Base.OutlineEffect.Both 
                        (AVal.constant C4b.VRVisGreen) 
                        anno.thickness.value 
                        3.0  
                        RenderPass.main 
                        anno.modelTrafo 
                        points
                | false -> Sg.empty 
            )
            |> Sg.dynamic
    
        Sg.ofList [
            selectionSg
            pickingLines
            dotsAndText
        ] |> optional anno.visible

    let finishedAnnotationText
         (anno             : AdaptiveAnnotation) 
         (config           : innerViewConfig)
         (view             : aval<CameraView>) =
        
        anno.text 
        |> AVal.map3 (fun show visible text -> (String.IsNullOrEmpty text) || (show && visible) ) anno.showText anno.visible
        |> optionalSet (drawText view config anno)
        |> Sg.set

    let finishedAnnotation 
        (anno             : AdaptiveAnnotation) 
        (color            : aval<C4b>) 
        (config           : innerViewConfig)
        (view             : aval<CameraView>) 
        (viewportSize     : aval<V2i>)
        (showPoints       : aval<bool>)         
        (picked           : aval<bool>)
        (pickingAllowed   : aval<bool>) =
 
        //let dots = 
        //    showPoints 
        //    |> optionalSet (
        //        getDotsIsg
        //            anno.points
        //            (anno.thickness.value |> AVal.map(fun x -> x + 0.5))
        //            color
        //            anno.geometry 
        //            config.offset
        //    )
     
        let texts = 
            anno.text 
            |> AVal.map2 (fun show text -> (String.IsNullOrEmpty text) || show ) anno.showText
            |> optionalSet (drawText view config anno)
    
        let dotsAndText = texts |> Sg.set //ASet.union' [dots; texts] |> Sg.set
            
        //let selectionColor = AVal.map2(fun x color -> if x then C4b.VRVisGreen else color) picked c
        let pickingAllowed = // for this particular annotation // whether should fire pick actions
            AVal.map2 (&&) pickingAllowed anno.visible

        //let pickFunc = pickEventsHelper (anno.key |> AVal.constant) pickingAllowed anno.thickness.value anno.modelTrafo


        //let pickingLines = 
        //    Sg.pickableLine 
        //        points 
        //        config.offset 
        //        color
        //        anno.thickness.value 
        //        config.pickingTolerance
        //        anno.modelTrafo 
        //        true 
        //        pickFunc
             
        let vm = view |> AVal.map (fun v -> (CameraView.viewTrafo v).Forward)

        let selectionSg = 
            picked 
            |> AVal.map (function
                | true -> 
                    
                    let points = getPolylinePoints anno     
                    OutlineEffect.createForLineOrPoint view viewportSize PRo3D.Base.OutlineEffect.Both (AVal.constant C4b.VRVisGreen) anno.thickness.value 3.0  RenderPass.main anno.modelTrafo points
                | false -> Sg.empty ) 
            |> Sg.dynamic
    
        Sg.ofList [
            selectionSg
            //pickingLines
            //dotsAndText
            //(texts |> Sg.set)
        ] |> optional anno.visible
    
    let finishedAnnotationDiscs (anno : AdaptiveAnnotation) (conf:innerViewConfig) (cl : AdaptiveFalseColorsModel) (cam:aval<CameraView>) =
        optional (AVal.map2 (&&) anno.visible anno.showDns) (drawDns anno conf cl cam) 

    //cones
    let cone color radius height (pos : aval<V3d>) (dir : aval<V3d>) =
        Sg.cone' 10 color radius height 
        |> Sg.noEvents
        |> Sg.trafo (dir |> AVal.map (fun x ->  Trafo3d.RotateInto(V3d.OOI, x)))
        |> Sg.trafo (pos |> AVal.map Trafo3d.Translation)
        |> Sg.uniform "WorldPos" pos
        |> Sg.uniform "Size" ~~15.0
        |> Sg.effect [
            //toEffect <| Shaders.screenSpaceScale
            toEffect <| DefaultSurfaces.stableTrafo
            toEffect <| DefaultSurfaces.vertexColor
            //toEffect <| DefaultSurfaces.stableHeadlight
        ]

    //spheres
    let sphere' color radius (pos : aval<V3d>) =
        Sg.sphere 4 (color) (~~1.0) 
        |> Sg.noEvents        
        |> Sg.trafo (pos |> AVal.map Trafo3d.Translation)
        |> Sg.uniform "WorldPos" pos
        |> Sg.uniform "Size" radius
        |> Sg.effect [
            toEffect <| Shader.ScreenSpaceScale.screenSpaceScale
            toEffect <| DefaultSurfaces.stableTrafo
            toEffect <| DefaultSurfaces.vertexColor
            //toEffect <| DefaultSurfaces.stableHeadlight
        ]

    //lines
    let toColoredEdges (offset:V3d) (color : C4b) (points : array<V3d>) =
        points
        |> Array.map (fun x -> x-offset)
        |> Array.pairwise
        |> Array.map (fun (a,b) -> (new Line3d(a,b), color))


    let thickLine' (line : Line<OpcViewer.Base.Shader.ThickLineNew.ThickLineVertex>) =
        triangle {
            let t = uniform.LineWidth
            let sizeF = V3d(float uniform.ViewportSize.X, float uniform.ViewportSize.Y, 1.0)
    
            let mutable pp0 = line.P0.pos
            let mutable pp1 = line.P1.pos        
                            
            let add = 2.0 * V2d(t,t) / sizeF.XY
                            
            let a0 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4d( 1.0,  0.0,  0.0, -(1.0 + add.X))) &&pp0 &&pp1
            let a1 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4d(-1.0,  0.0,  0.0, -(1.0 + add.X))) &&pp0 &&pp1
            let a2 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4d( 0.0,  1.0,  0.0, -(1.0 + add.Y))) &&pp0 &&pp1
            let a3 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4d( 0.0, -1.0,  0.0, -(1.0 + add.Y))) &&pp0 &&pp1
            let a4 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4d( 0.0,  0.0,  1.0, -1.0)) &&pp0 &&pp1
            let a5 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4d( 0.0,  0.0, -1.0, -1.0)) &&pp0 &&pp1
    
            if a0 && a1 && a2 && a3 && a4 && a5 then
                let p0 = pp0.XYZ / pp0.W
                let p1 = pp1.XYZ / pp1.W
    
                let fwp = (p1.XYZ - p0.XYZ) * sizeF
    
                let fw = V3d(fwp.XY, 0.0) |> Vec.normalize
                let r = V3d(-fw.Y, fw.X, 0.0) / sizeF
                let d = fw / sizeF
                let p00 = p0 - r * t - d * t
                let p10 = p0 + r * t - d * t
                let p11 = p1 + r * t + d * t
                let p01 = p1 - r * t + d * t
    
                let rel = t / (Vec.length fwp)
    
                yield { line.P0 with i = 0; pos = V4d(p00, 1.0); lc = V2d(-1.0, -rel); w = rel }
                yield { line.P0 with i = 0; pos = V4d(p10, 1.0); lc = V2d( 1.0, -rel); w = rel }
                yield { line.P1 with i = 1; pos = V4d(p01, 1.0); lc = V2d(-1.0, 1.0 + rel); w = rel }
                yield { line.P1 with i = 1; pos = V4d(p11, 1.0); lc = V2d( 1.0, 1.0 + rel); w = rel }
        }

    let drawColoredEdges width edges = 
        edges
        |> IndexedGeometryPrimitives.lines
        |> Sg.ofIndexedGeometry
        |> Sg.uniform "LineWidth" (AVal.constant width)
        |> Sg.uniform "DepthOffset" (AVal.constant 0.0001)
        |> Sg.blendMode (AVal.constant BlendMode.None)
        |> Sg.effect [
            toEffect Aardvark.UI.Trafos.Shader.stableTrafo
            toEffect DefaultSurfaces.vertexColor
            toEffect thickLine'
            toEffect PRo3D.Base.Shader.DepthOffset.depthOffsetFS
        ]

    let lines (color : C4b) (width : double) (points : V3d[]) =
        let offset =
            match points |> Array.tryHead with
            | Some h -> h
            | None -> V3d.Zero

        points 
        |> toColoredEdges offset color        
        |> drawColoredEdges width
        |> Sg.trafo (offset |> Trafo3d.Translation |> AVal.constant)
