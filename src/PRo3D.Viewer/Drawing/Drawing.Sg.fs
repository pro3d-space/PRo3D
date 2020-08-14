namespace PRo3D.DrawingApp

open System

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open FSharp.Data.Adaptive
open Aardvark.Base.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering.Text

open Aardvark.UI
open Aardvark.UI.Primitives    

open OpcViewer.Base

open PRo3D
open PRo3D.Base
open PRo3D.Drawing
open PRo3D.Base
open PRo3D.Base.Annotation

open FShade

open Adaptify.FSharp.Core

module Sg =                             
      //TODO TO refactor formatting
    //open PRo3D.Surfaces.Mutable.SgSurfaceModule

    let discISg color size height trafo =
        Sg.cylinder 30 color size height              
          |> Sg.noEvents
          |> Sg.uniform "WorldPos" (trafo |> AVal.map(fun (x : Trafo3d) -> x.Forward.C3.XYZ))
          |> Sg.uniform "Size" size
          |> Sg.shader {
              //do! Shader.screenSpaceScale
              do! Shader.StableTrafo.stableTrafo
              do! DefaultSurfaces.vertexColor
              do! Shader.StableLight.stableLight
          }
          |> Sg.trafo(trafo)
    
    let coneISg color radius height trafo =  
        Sg.cone 30 color radius height
           |> Sg.noEvents         
           |> Sg.shader {                   
               do! Shader.StableTrafo.stableTrafo
               do! DefaultSurfaces.vertexColor
               do! Shader.StableLight.stableLight
           }
           |> Sg.trafo(trafo) 
           
    type innerViewConfig =
        {
            nearPlane       : aval<float>
            hfov            : aval<float>                
            arrowThickness  : aval<float>
            arrowLength     : aval<float>
            dnsPlaneSize    : aval<float>
            offset          : aval<float>
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
                                  
    let drawDns' (points : alist<V3d>) (dnsResults : aval<option<AdaptiveDipAndStrikeResults>>) (conf:innerViewConfig) (cl : AdaptiveFalseColorsModel) =                         
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
                  alist {
                      yield center'
                      yield center' + dip.Normalized * lineLength'
                  }
                
                yield Sg.drawScaledLines dipLine color conf.arrowThickness posTrafo 
                
                let coneTrafo = 
                  lineLength |>
                    AVal.map(fun s -> Trafo3d.RotateInto(V3d.ZAxis, dip) * Trafo3d.Translation(center' + dip.Normalized * s))
                
                yield coneISg color coneRadius coneHeight coneTrafo
                
                //strikes lines
                let! strike = x.strikeDirection
                let strikeLine1 =
                  alist {                                                                
                      yield center' - strike.Normalized * lineLength'
                      yield center' + strike.Normalized * lineLength'
                  }                                            
                
                //yield Sg.lines strikeLine1 (AVal.constant C4b.Red) conf.arrowThickness posTrafo anno.key
                yield Sg.drawScaledLines strikeLine1 (AVal.constant C4b.Red) conf.arrowThickness posTrafo
            | None -> ()            
        } |> Sg.set
        
    let drawDns (anno : AdaptiveAnnotation) (conf:innerViewConfig) (cl : AdaptiveFalseColorsModel) (cam:aval<CameraView>) =   
        drawDns' anno.points (AVal.map Adaptify.FSharp.Core.Missing.AdaptiveOption.toOption anno.dnsResults) conf cl

    let getPolylinePoints (a : AdaptiveAnnotation) =
        alist {                          
            let! hasSegments = (a.segments |> AList.count) |> AVal.map(fun x -> x > 0)
            if hasSegments |> not then
                yield! a.points
            else
                for segment in a.segments do
                    let! startPoint = segment.startPoint
                    let! endPoint = segment.endPoint
                    yield  startPoint
                    yield! segment.points
                    yield  endPoint
        }
    
    let mutable lastHash = -1

    let pickEventsHelper (id : aval<Guid>) (currentlyActive : aval<bool>) (pixelWidth : aval<float>) (model : aval<Trafo3d>) (edges : aval<Line3d[]>) =
        SceneEventKind.Click, (
            fun (sceneHit : SceneHit) ->
                let id = id |> AVal.force
                let currentlyActive = currentlyActive |> AVal.force
                let lines = edges |> AVal.force
                let modelTrafo = model |> AVal.force
                let pixelWidth = pixelWidth |> AVal.force                        
        
                let rayHash = sceneHit.globalRay.Ray.Ray.GetHashCode()

                if (rayHash = lastHash) then
                    Log.warn "rayHash took over"
                    true, Seq.empty
                else

                    if lines.Length > 0 && currentlyActive then
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
                            Log.warn "[AnnotationPicking] picked %A" id
                            true, Seq.ofList [ PickAnnotation (sceneHit, (id)) ]
                        else 
                            true, Seq.empty // if no pick continue anyways. we are no blocker geometry
                    else 
                        true, Seq.empty
        )

    let drawWorkingAnnotation (offset : aval<float>) (anno : aval<Option<AdaptiveAnnotation>>)  = 
    
        let polyPoints =
            alist {
                let! anno = anno
                match anno with
                | Some a -> yield! getPolylinePoints a
                | None -> ()
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
            Sg.drawPointList 
              points 
              (C4b.VRVisGreen |> AVal.constant)//(color     |> AVal.map (fun x -> (x |> PRo3D.Sg.createSecondaryColor)))
              (thickness |> AVal.map (fun x -> x * 1.5)) 
              (offset    |> AVal.map (fun x -> x * 1.1))
        ]                                                               

    let drawText' (view : aval<CameraView>) (conf: innerViewConfig) (text:aval<string>)(anno : AdaptiveAnnotation) = 
        let points = anno.points |> AList.toAVal
        let pos = points |> AVal.map(fun a -> a |> IndexList.toList |> List.head)
        Sg.text view conf.nearPlane conf.hfov pos anno.modelTrafo anno.textsize.value text
    
    let drawText (view : aval<CameraView>) (conf: innerViewConfig) (anno : AdaptiveAnnotation) = 
        drawText' view conf anno.text anno
    
    let optional (sg : ISg<_>) (m : aval<bool>) : aset<ISg<_>> =
        adaptive {
            let! m = m 
            if m then return sg
            else return Sg.empty
        } |> ASet.ofAValSingle
    
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

    let finishedAnnotation 
        (anno       : AdaptiveAnnotation) 
        (c          : aval<C4b>) 
        (conf       : innerViewConfig)
        (view       : aval<CameraView>) 
        (showPoints : aval<bool>) 
        (picked     : aval<bool>)
        (pickingAllowed : aval<bool>) =

        let points = getPolylinePoints anno      
        let dots = 
            showPoints 
            |> optional (
                getDotsIsg
                    anno.points
                    (anno.thickness.value |> AVal.map(fun x -> x+0.5)) 
                    c 
                    anno.geometry 
                    conf.offset
            )
       
        let azimuth =
            adaptive { 
                let! results = anno.dnsResults 
                let! x = 
                    match results with
                    | AdaptiveSome r -> r.dipAzimuth
                    | AdaptiveNone   -> AVal.constant Double.NaN
                
                return x
            }
    
        let azimuthText = (drawText' view conf (azimuth |> AVal.map(fun x -> sprintf "%.2f" x)) anno)
          
        let texts = 
            anno.text 
            |> AVal.map (String.IsNullOrEmpty >> not) 
            |> optional (drawText view conf anno)
    
        let dotsAndText = ASet.union' [dots; texts] |> Sg.set
            
        //let selectionColor = AVal.map2(fun x color -> if x then C4b.VRVisGreen else color) picked c
        let pickingAllowed = // for this particular annotation // whether should fire pick actions
            AVal.map2 (&&) pickingAllowed anno.visible

        let pickFunc = pickEventsHelper (anno.key |> AVal.constant) pickingAllowed anno.thickness.value anno.modelTrafo

        let pickingLines = Sg.pickableLine points conf.offset c anno.thickness.value anno.modelTrafo true pickFunc
             
        let selectionSg = 
            picked 
            |> AVal.map (function
                | true -> OutlineEffect.createForLineOrPoint PRo3D.Base.OutlineEffect.Both (AVal.constant C4b.VRVisGreen) anno.thickness.value 3.0  RenderPass.main anno.modelTrafo points
                | false -> Sg.empty ) 
            |> Sg.dynamic
    
        Sg.ofList [
            selectionSg
            //azimuthText
            pickingLines
            dotsAndText
        ] |> Sg.onOff anno.visible
    
    let finishedAnnotationDiscs (anno : AdaptiveAnnotation) (conf:innerViewConfig) (cl : AdaptiveFalseColorsModel) (cam:aval<CameraView>) =
        anno.showDns 
        |> optional (drawDns anno conf cl cam) 
        |> Sg.set 
        |> Sg.onOff anno.visible