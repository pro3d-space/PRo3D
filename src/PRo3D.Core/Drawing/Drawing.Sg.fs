namespace PRo3D.Core.Drawing

open System

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.Data.Opc
open Aardvark.Rendering.Text

open Aardvark.UI

open Aardvark.UI    

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

    /// World-space distance below which two *consecutive* polyline points count as identical.
    /// Comfortably above double-precision noise at planetary magnitudes (~1e-9 m at 1e7 m) and
    /// far below any meaningful annotation detail (sampling distance defaults to 1 m).
    let private polylinePointEps = 1e-6

    /// Drops points coinciding with their immediate predecessor.
    ///
    /// Only *consecutive* duplicates go. A closing point repeating the first one is not adjacent
    /// to it and is kept, so closed rings — ellipses emit samples+1 points — keep their closing
    /// edge.
    let private dedupeConsecutive (ps : V3d[]) =
        if ps.Length < 2 then ps
        else
            let res = System.Collections.Generic.List<V3d>(ps.Length)
            res.Add ps.[0]
            for i in 1 .. ps.Length - 1 do
                if Vec.Distance(res.[res.Count - 1], ps.[i]) > polylinePointEps then
                    res.Add ps.[i]
            res.ToArray()

    /// Concatenates an annotation's segments — or its raw points, when it has none — into a
    /// polyline.
    ///
    /// Segments share their end points by construction: segment i's endPoint is segment i+1's
    /// startPoint (Drawing-App.fs:161), and the last interior sample can land on the segment end
    /// (Drawing-App.fs:171-179). The raw concatenation therefore contains doubled and tripled
    /// vertices, which become zero-length line segments — and the thick-line geometry shader
    /// expands those by normalizing (p1 - p0), i.e. a zero vector. Dropped here rather than at
    /// each of the call sites.
    /// The flattening itself, evaluated against the caller's token.
    ///
    /// Anything already inside an AVal.custom must call this rather than getPolylinePoints.
    /// Building an adaptive value inside another one's evaluation creates a node that nothing
    /// holds a strong reference to, and adaptive outputs are weak: once it is collected, the
    /// invalidation chain from `a.points` to the enclosing computation is gone and the geometry
    /// silently stops updating until something unrelated marks it dirty. That is what made an
    /// edited annotation only redraw once the next annotation was drawn.
    let getPolylinePointsAt (a : AdaptiveAnnotation) (t : AdaptiveToken) : V3d[] =
        let segments = a.segments.Content.GetValue t
        if IndexList.isEmpty segments then
            a.points.Content.GetValue(t) |> IndexList.toArray |> dedupeConsecutive
        else
            let points = System.Collections.Generic.List<V3d>()
            segments |> IndexList.iter(fun (s : AdaptiveSegment) ->
                points.Add(s.startPoint.GetValue(t))
                for p in s.points.Content.GetValue(t) do points.Add(p)
                points.Add(s.endPoint.GetValue(t))
            )
            points.ToArray() |> dedupeConsecutive

    let getPolylinePoints (a : AdaptiveAnnotation) =
        AVal.custom (getPolylinePointsAt a)
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
                             |> Array.exists (fun e -> 
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

        Sg.text view conf.nearPlane conf.hfov pos (pos |> AVal.map Trafo3d.Translation) anno.textsize.value text (AVal.constant C4b.White)
    
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


    let shouldTextBeRendered (anno : AdaptiveAnnotation) =
         (anno.text, anno.visible, anno.showText) 
         |||> AVal.map3 (fun text visible show -> show && visible && not (String.IsNullOrEmpty text))


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

    // composed once (like stableLight above), not per sphere' call
    let private sphereEffect =
        Effect.compose [
            toEffect Shader.ScreenSpaceScale.screenSpaceScale
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor
        ]

    //spheres
    let sphere' color radius (pos : aval<V3d>) =
        Sg.sphere 4 (color) (~~1.0)
        |> Sg.noEvents
        |> Sg.trafo (pos |> AVal.map Trafo3d.Translation)
        |> Sg.uniform "WorldPos" pos
        |> Sg.uniform "Size" radius
        |> Sg.effect [sphereEffect]

    //lines
    let toColoredEdges (offset:V3d) (color : C4b) (points : array<V3d>) =
        points
        |> Array.map (fun x -> x-offset)
        |> Array.pairwise
        |> Array.map (fun (a,b) -> (new Line3d(a,b), color))


    let thickLine' (line : Line<OpcViewer.Base.Shader.ThickLineNew.ThickLineVertex>) =
        triangle {
            let t = uniform.LineWidth
            let sizeF = V3f(float32 uniform.ViewportSize.X, float32 uniform.ViewportSize.Y, 1.0f)
    
            let mutable pp0 = line.P0.pos
            let mutable pp1 = line.P1.pos        
                            
            let add = 2.0f * V2f(t,t) / sizeF.XY
                            
            let a0 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4f( 1.0f,  0.0f,  0.0f, -(1.0f + add.X))) &&pp0 &&pp1
            let a1 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4f(-1.0f,  0.0f,  0.0f, -(1.0f + add.X))) &&pp0 &&pp1
            let a2 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4f( 0.0f,  1.0f,  0.0f, -(1.0f + add.Y))) &&pp0 &&pp1
            let a3 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4f( 0.0f, -1.0f,  0.0f, -(1.0f + add.Y))) &&pp0 &&pp1
            let a4 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4f( 0.0f,  0.0f,  1.0f, -1.0f)) &&pp0 &&pp1
            let a5 = OpcViewer.Base.Shader.ThickLineNew.clipLine (V4f( 0.0f,  0.0f, -1.0f, -1.0f)) &&pp0 &&pp1
    
            if a0 && a1 && a2 && a3 && a4 && a5 then
                let p0 = pp0.XYZ / pp0.W
                let p1 = pp1.XYZ / pp1.W
    
                let fwp = (p1.XYZ - p0.XYZ) * sizeF
    
                let fw = V3f(fwp.XY, 0.0f) |> Vec.normalize
                let r = V3f(-fw.Y, fw.X, 0.0f) / sizeF
                let d = fw / sizeF
                let p00 = p0 - r * t - d * t
                let p10 = p0 + r * t - d * t
                let p11 = p1 + r * t + d * t
                let p01 = p1 - r * t + d * t
    
                let rel = t / (Vec.length fwp)
    
                yield { line.P0 with i = 0; pos = V4f(p00, 1.0f); lc = V2f(-1.0f, -rel); w = rel }
                yield { line.P0 with i = 0; pos = V4f(p10, 1.0f); lc = V2f( 1.0f, -rel); w = rel }
                yield { line.P1 with i = 1; pos = V4f(p01, 1.0f); lc = V2f(-1.0f, 1.0f + rel); w = rel }
                yield { line.P1 with i = 1; pos = V4f(p11, 1.0f); lc = V2f( 1.0f, 1.0f + rel); w = rel }
        }

    let drawColoredEdges width edges = 
        edges
        |> IndexedGeometryPrimitives.lines
        |> Sg.ofIndexedGeometry
        |> Sg.uniform "LineWidth" (AVal.constant width)
        |> Sg.uniform "DepthOffset" (AVal.constant 0.0000000001)
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
