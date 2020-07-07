namespace PRo3D.DrawingApp

open System

open Aardvark.Base
open Aardvark.Application
open Aardvark.UI

open Aardvark.Base.Incremental
open Aardvark.Base.Rendering
open Aardvark.Application
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.Rendering.Text

open Aardvark.GeoSpatial.Opc

open Aardvark.UI
open Aardvark.UI.Primitives    
open PRo3D
open PRo3D.Base
open PRo3D.Drawing
open PRo3D.Base
open PRo3D.Base.Annotation

open FShade

module Sg =                             
      //TODO TO refactor formatting
    //open PRo3D.Surfaces.Mutable.SgSurfaceModule

    let discISg color size height trafo =
        Sg.cylinder 30 color size height              
          |> Sg.noEvents
          |> Sg.uniform "WorldPos" (trafo |> Mod.map(fun (x : Trafo3d) -> x.Forward.C3.XYZ))
          |> Sg.uniform "Size" size
          |> Sg.shader {
              //do! Shader.screenSpaceScale
              do! Shader.stableTrafo
              do! DefaultSurfaces.vertexColor
              do! Shader.stableLight
          }
          |> Sg.trafo(trafo)
    
    let coneISg color radius height trafo =  
        Sg.cone 30 color radius height
           |> Sg.noEvents         
           |> Sg.shader {                   
               do! Shader.stableTrafo
               do! DefaultSurfaces.vertexColor
               do! Shader.stableLight
           }
           |> Sg.trafo(trafo) 
           
    type innerViewConfig =
        {
            nearPlane       : IMod<float>
            hfov            : IMod<float>                
            arrowThickness  : IMod<float>
            arrowLength     : IMod<float>
            dnsPlaneSize    : IMod<float>
            offset          : IMod<float>
        }
    
    let drawTrueThicknessPlane (planeScale : IMod<float>) (dnsResults : IMod<option<MDipAndStrikeResults>>) (cl : MFalseColorsModel) =                         
        aset {                            
            let! dns = dnsResults
            match dns with
            | Some x -> 
                
                let color = FalseColorLegendApp.Draw.getColorDnS cl x.dipAngle                                                 

                let posTrafo = 
                    x.centerOfMass 
                    |> Mod.map Trafo3d.Translation
                
                // disc
                let discTrafo =
                    Mod.map2(fun (pln:Plane3d) pos -> 
                        (Trafo3d.RotateInto(V3d.ZAxis, pln.Normal) * pos)) 
                        x.plane 
                        posTrafo
                
                yield discISg color planeScale (planeScale |> Mod.map(fun d -> d * 0.01)) discTrafo
                                
            | None -> ()            
        } |> Sg.set
                                  
    let drawDns' (points : alist<V3d>) (dnsResults : IMod<option<MDipAndStrikeResults>>) (conf:innerViewConfig) (cl : MFalseColorsModel) =                         
        aset {                            
            let! dns = dnsResults
            match dns with
            | Some x -> 
                let center = points |> AList.toMod |> Mod.map (fun list -> list.[PList.count list / 2])
                
                let color = FalseColorLegendApp.Draw.getColorDnS cl x.dipAngle
                     
                let lengthFactor = 
                    points
                    |> AList.toMod 
                    |> Mod.map(fun x -> (x.AsList |> Calculations.getDistance) / 3.0)
                          
                let discRadius = conf.dnsPlaneSize |> Mod.map2 (*) lengthFactor
                let posTrafo = center |> Mod.map Trafo3d.Translation
                
                // disc
                let discTrafo =
                    Mod.map2(fun (pln:Plane3d) pos -> (Trafo3d.RotateInto(V3d.ZAxis, pln.Normal) * pos)) x.plane posTrafo
                
                yield discISg color discRadius (discRadius |> Mod.map(fun d -> d * 0.01)) discTrafo
                
                let lineLength = conf.arrowLength |> Mod.map2 (*) lengthFactor //discRadius |> Mod.map((*) 1.5)
                
                let coneHeight = lineLength |> Mod.map((*) 0.2) //lineLength
                let coneRadius = coneHeight |> Mod.map((*) 0.3)
                
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
                    Mod.map(fun s -> Trafo3d.RotateInto(V3d.ZAxis, dip) * Trafo3d.Translation(center' + dip.Normalized * s))
                
                yield coneISg color coneRadius coneHeight coneTrafo
                
                //strikes lines
                let! strike = x.strikeDirection
                let strikeLine1 =
                  alist {                                                                
                      yield center' - strike.Normalized * lineLength'
                      yield center' + strike.Normalized * lineLength'
                  }                                            
                
                //yield Sg.lines strikeLine1 (Mod.constant C4b.Red) conf.arrowThickness posTrafo anno.key
                yield Sg.drawScaledLines strikeLine1 (Mod.constant C4b.Red) conf.arrowThickness posTrafo
            | None -> ()            
        } |> Sg.set
        
    let drawDns (anno : MAnnotation) (conf:innerViewConfig) (cl : MFalseColorsModel) (cam:IMod<CameraView>) =   
        drawDns' anno.points anno.dnsResults conf cl

    let getPolylinePoints (a : MAnnotation) =
        alist {                          
            let! hasSegments = (a.segments |> AList.count) |> Mod.map(fun x -> x > 0)
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

    let pickEventsHelper (id : IMod<Guid>) (currentlyActive : IMod<bool>) (pixelWidth : IMod<float>) (model : IMod<Trafo3d>) (edges : IMod<Line3d[]>) =
        SceneEventKind.Click, (
            fun (sceneHit : SceneHit) ->
                let id = id |> Mod.force
                let currentlyActive = currentlyActive |> Mod.force
                let lines = edges |> Mod.force
                let modelTrafo = model |> Mod.force
                let pixelWidth = pixelWidth |> Mod.force                        
        
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

    let drawWorkingAnnotation (offset : IMod<float>) (anno : IMod<Option<MAnnotation>>)  = 
    
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
            
        let color     = Mod.bindOption anno C4b.VRVisGreen   (fun a -> a.color.c) 
        let thickness = Mod.bindOption anno 1.0              (fun a -> a.thickness.value)             
        let trafo     = Mod.bindOption anno Trafo3d.Identity (fun a -> a.modelTrafo)                      
        
        Sg.ofList [
            Sg.drawLines polyPoints offset color thickness trafo //polyline
            //Sg.dots polyPoints (Mod.constant C4b.VRVisGreen) // debug sample points
            //Sg.drawSpheres points thickness color                     //support points
            Sg.drawPointList 
              points 
              (C4b.VRVisGreen |> Mod.constant)//(color     |> Mod.map (fun x -> (x |> PRo3D.Sg.createSecondaryColor)))
              (thickness |> Mod.map (fun x -> x * 1.5)) 
              (offset    |> Mod.map (fun x -> x * 1.1))
        ]                                                               

    let drawText' (view : IMod<CameraView>) (conf: innerViewConfig) (text:IMod<string>)(anno : MAnnotation) = 
        let points = anno.points |> AList.toMod
        let pos = points |> Mod.map(fun a -> a |> PList.toList |> List.head)
        Sg.text view conf.nearPlane conf.hfov pos anno.modelTrafo anno.textsize.value text
    
    let drawText (view : IMod<CameraView>) (conf: innerViewConfig) (anno : MAnnotation) = 
        drawText' view conf anno.text anno
    
    let optional (sg : ISg<_>) (m : IMod<bool>) : aset<ISg<_>> =
        adaptive {
            let! m = m 
            if m then return sg
            else return Sg.empty
        } |> ASet.ofModSingle
    
    let getDotsIsg (points : alist<V3d>) (size:IMod<float>) (color : IMod<C4b>) (geometry: IMod<Geometry>) (offset : IMod<float>) =
        aset {
            let! geometry = geometry
            match geometry with
            | Geometry.Point -> 
                match points|> AList.toList |> List.tryHead with
                | Some p -> 
                    yield Sg.dot color size  (Mod.constant p)
                | _ -> 
                    yield Sg.empty
            | _ -> 
                //let color = color |> Mod.map(fun x -> (x |> createSecondaryColor))
                yield Sg.drawPointList points (C4b.VRVisGreen |> Mod.constant) size (offset |> Mod.map(fun x -> x * 1.1))
        } 
        |> Sg.set  

    let finishedAnnotation 
        (anno       : MAnnotation) 
        (c          : IMod<C4b>) 
        (conf       : innerViewConfig)
        (view       : IMod<CameraView>) 
        (showPoints : IMod<bool>) 
        (picked     : IMod<bool>)
        (pickingAllowed : IMod<bool>) =

        let points = getPolylinePoints anno      
        let dots = 
            showPoints 
            |> optional (
                getDotsIsg
                    anno.points
                    (anno.thickness.value |> Mod.map(fun x -> x+0.5)) 
                    c 
                    anno.geometry 
                    conf.offset
            )
       
        let azimuth =
            adaptive { 
                let! results = anno.dnsResults 
                let! x = 
                    match results with
                    | Some r -> r.dipAzimuth
                    | None   -> Mod.constant Double.NaN
                
                return x
            }
    
        let azimuthText = (drawText' view conf (azimuth |> Mod.map(fun x -> sprintf "%.2f" x)) anno)
          
        let texts = 
            anno.text 
            |> Mod.map (String.IsNullOrEmpty >> not) 
            |> optional (drawText view conf anno)
    
        let dotsAndText = ASet.unionMany' [dots; texts] |> Sg.set
            
        //let selectionColor = Mod.map2(fun x color -> if x then C4b.VRVisGreen else color) picked c
        let pickingAllowed = // for this particular annotation // whether should fire pick actions
            Mod.map2 (&&) pickingAllowed anno.visible

        let pickFunc = pickEventsHelper (anno.key |> Mod.constant) pickingAllowed anno.thickness.value anno.modelTrafo

        let pickingLines = Sg.pickableLine points conf.offset c anno.thickness.value anno.modelTrafo true pickFunc
             
        let selectionSg = 
            picked 
            |> Mod.map (function
                | true -> OutlineEffect.createForLineOrPoint PRo3D.Base.OutlineEffect.Both (Mod.constant C4b.VRVisGreen) anno.thickness.value 3.0  RenderPass.main anno.modelTrafo points
                | false -> Sg.empty ) 
            |> Sg.dynamic
    
        Sg.ofList [
            selectionSg
            //azimuthText
            pickingLines
            dotsAndText
        ] |> Sg.onOff anno.visible
    
    let finishedAnnotationDiscs (anno : MAnnotation) (conf:innerViewConfig) (cl : MFalseColorsModel) (cam:IMod<CameraView>) =
        anno.showDns 
        |> optional (drawDns anno conf cl cam) 
        |> Sg.set 
        |> Sg.onOff anno.visible