namespace PRo3D.Core

open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators

open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI

open PRo3D.Base

module Sg =

    type MarkerStyle = {
        position  : aval<V3d>
        direction : aval<V3d>
        color     : aval<C4b>
        size      : aval<float>
        thickness : aval<float>
        hasArrow  : aval<bool>
        text      : aval<option<string>>
        fix       : aval<bool>
    }

    //let coneISg color radius size trafo =  
    //    Sg.cone 30 color radius size
    //            |> Sg.noEvents
    //            |> Aardvark.UI.FShadeSceneGraph.Sg.shader {
    //                do! Aardvark.GeoSpatial.Opc.Shader.stableTrafo
    //                do! DefaultSurfaces.vertexColor
    //               // do! DefaultSurfaces.simpleLighting
    //            }
    //            |> Sg.trafo(trafo)

    //TODO refactor: confusing use of variable names and transformations, seems very complicated for a line with a label
    let directionMarker (near:aval<float>) (cam:aval<CameraView>) (style : MarkerStyle) =
        aset{
           
            //let! dirs = style.direction
            
            let lineLengthScale = style.size                    
            let scaledLength = 
                AVal.map2(fun (str:V3d) s -> str.Normalized * s) style.direction lineLengthScale

            //let scaledLength = dir * length
            let al = 
                (style.position, scaledLength) ||> AVal.map2 (fun p length -> [| p; p + length |])

            let posTrafo =
                style.position |> AVal.map(fun d -> Trafo3d.Translation(d))

            //let coneTrafo = 
            //    AVal.map2(fun p s -> Trafo3d.RotateInto(V3d.ZAxis, dirs.Normalized) * Trafo3d.Translation(p + s)) style.position scaledLength

            //let radius = length * 0.1
            //let scale =  DrawingApp.Sg.computeInvariantScale cam p 5.0
            //let radius = lineLengthScale |> AVal.map(fun d -> d * 0.04)
            //let coneSize = lineLengthScale |> AVal.map(fun d -> d * 0.3)

            let nLabelPos = AVal.map2(fun l r -> l + r) style.position scaledLength
            let nPosTrafo =
                nLabelPos |> AVal.map(fun d -> Trafo3d.Translation(d))

            
            let label = 
                style.text 
                  |> AVal.map(fun x ->
                    match x with 
                      | Some text -> Sg.text cam near ~~60.0 nLabelPos nPosTrafo ~~0.05 ~~text
                      | None -> Sg.empty)

            yield Sg.drawLines al (AVal.constant 0.0)style.color style.thickness posTrafo
            yield label |> Sg.dynamic                                   
             
        } |> Sg.set 

       
    let point (pos:aval<V3d>) (color:aval<C4b>) (cam:aval<CameraView>) = 
        Sg.dot color (AVal.constant 3.0)  pos

//["1km";"100m";"10m";"1m";"10cm";"1cm";"1mm";"0.1mm"] 
    let scaleToSize (a:string) =
        match a with
          | "100km" -> 100000.0
          | "10km"  ->  10000.0
          | "1km"   ->   1000.0
          | "100m"  ->    100.0
          | "10m"   ->     10.0
          | "2m"    ->      2.0
          | "1m"    ->      1.0
          | "10cm"  ->      0.1
          | "1cm"   ->      0.01
          | "1mm"   ->      0.001
          | "0.1mm" ->      0.0001
          | _       ->      1.0

    let getOrientationSystem (mbigConfig : 'ma) (minnerConfig : MInnerConfig<'ma>) (model:AdaptiveReferenceSystem) (cam:aval<CameraView>) =
        let thickness = AVal.constant 2.0
        let near      = minnerConfig.getNearDistance   mbigConfig

        let east = AVal.map2(fun (l:V3d) (r:V3d) -> r.Cross(l) ) model.up.value model.north.value

        let size = AVal.constant 2.0
        let posTrafo = AVal.constant (Trafo3d.Translation(V3d.OOO))
        let position = AVal.constant V3d.OIO

        let styleUp : MarkerStyle = {
            position  = position
            direction = model.up.value
            color     = AVal.constant C4b.Magenta
            size      = size
            thickness = thickness
            hasArrow  = AVal.constant false
            text      = AVal.constant None
            fix       = AVal.constant false
        }


        let upV = 
            (model.up.value, position) ||> AVal.map2 (fun udir position -> [| position; position + udir.Normalized |])

                
        let northV = 
            (model.north.value, position) ||> AVal.map2 (fun ndir position -> [| position; position + ndir.Normalized |])

        let eastV = 
            (east, position) ||> AVal.map2 (fun edir position -> [| position; position + edir.Normalized |])


        let nLabelPos = AVal.map2(fun l r -> l + r) position model.north.value
        let nPosTrafo = nLabelPos |> AVal.map(fun d -> Trafo3d.Translation(d))
        let label = Sg.text cam near (AVal.constant 60.0) nLabelPos nPosTrafo (AVal.constant 0.05) (AVal.constant "N")

        Sg.ofList [
            point model.origin (AVal.constant C4b.Red) cam
            //sizeText
            Sg.drawLines upV (AVal.constant 0.0)(AVal.constant C4b.Blue) thickness posTrafo 
            styleUp |> directionMarker near cam 
            Sg.drawLines northV (AVal.constant 0.0)(AVal.constant C4b.Red) thickness posTrafo 
            Sg.drawLines eastV (AVal.constant 0.0)(AVal.constant C4b.Green) thickness posTrafo
            label                     
        ]   |> Sg.onOff(model.isVisible)       
        
    //TODO move to less generic place than Sg
    let view<'ma> (mbigConfig : 'ma) (minnerConfig : MInnerConfig<'ma>) (model:AdaptiveReferenceSystem) (cam:aval<CameraView>)  : ISg<ReferenceSystemAction> =
                   
        let length    = minnerConfig.getArrowLength    mbigConfig
        let thickness = minnerConfig.getArrowThickness mbigConfig
        let near      = minnerConfig.getNearDistance   mbigConfig

        let east = AVal.map2(fun (l:V3d) (r:V3d) -> r.Cross(l) ) model.up.value model.northO // model.north.value

        let size = model.selectedScale |> AVal.map scaleToSize

        let styleUp : MarkerStyle = {
            position  = model.origin
            direction = model.up.value
            color     = AVal.constant C4b.Blue
            size      = size
            thickness = thickness
            hasArrow  = AVal.constant false
            text      = AVal.constant None
            fix       = AVal.constant false
        }

        let styleNorth : MarkerStyle = {
            position  = model.origin
            direction = model.northO //model.north.value
            color     = AVal.constant C4b.Red
            size      = size
            thickness = thickness
            hasArrow  = AVal.constant true
            text      = AVal.constant (Some "N")
            fix       = AVal.constant false
        }

        let styleEast : MarkerStyle = {
            position  = model.origin
            direction = east
            color     = AVal.constant C4b.Green
            size      = size
            thickness = thickness
            hasArrow  = AVal.constant false
            text      = AVal.constant None
            fix       = AVal.constant false
        }

        let styleX : MarkerStyle = {
            position  = model.origin
            direction = AVal.constant V3d.IOO
            color     = AVal.constant C4b.Magenta
            size      = size
            thickness = thickness
            hasArrow  = AVal.constant false
            text      = AVal.constant (Some "X")
            fix       = AVal.constant false
        }

        let styleY : MarkerStyle = {
            position  = model.origin
            direction = AVal.constant V3d.OIO
            color     = AVal.constant C4b.Cyan
            size      = size
            thickness = thickness
            hasArrow  = AVal.constant false
            text      = AVal.constant (Some "Y")
            fix       = AVal.constant false
        }

        let styleZ : MarkerStyle = {
            position  = model.origin
            direction = AVal.constant V3d.OOI
            color     = AVal.constant C4b.Yellow
            size      = size
            thickness = thickness
            hasArrow  = AVal.constant false
            text      = AVal.constant (Some "Z")
            fix       = AVal.constant false
        }

        //test
        //let n90 =
        //    adaptive {
        //        let! up = model.up.value
        //        let! n = model.north.value
        //        let! o = model.origin
        //        return updateVectorInDegree up n o 90.0
        //    }
                 
        //let styleNorth90 : MarkerStyle = {
        //    position  = model.origin
        //    direction =  n90 //AVal.constant(V3d(19417.0692,323595.2722,-323129.0951))//
        //    color     = AVal.constant C4b.Yellow
        //    size      = size
        //    thickness = thickness
        //    hasArrow  = AVal.constant true
        //    text      = AVal.constant (Some "90°")
        //    fix       = AVal.constant false
        //}

        let refSysTrafo2 =
          adaptive {
              let! north = model.northO
              let! up = model.up.value
              let east = north.Normalized.Cross(up.Normalized)
              
              return Trafo3d.FromOrthoNormalBasis(north.Normalized,east,up.Normalized)
          }

        
        let sizeText = 
            Sg.text 
                cam 
                near 
                (AVal.constant 60.0) 
                model.origin 
                (model.origin |> AVal.map Trafo3d.Translation) 
                (AVal.constant 0.05)
                model.selectedScale 

        let refsystem = 
          Sg.ofList [
              point model.origin (AVal.constant C4b.Red) cam
              sizeText
              styleUp    |> directionMarker near cam  
              styleNorth |> directionMarker near cam
              //styleNorth90 |> directionMarker near cam
              styleEast  |> directionMarker near cam
          ]
        
        let translation = styleX.position |> AVal.map Trafo3d.Translation
        let inv (t:aval<Trafo3d>) = t |> AVal.map(fun x -> x.Inverse)

        let xyzSystem = 
          Sg.ofList [                
            styleX  |> directionMarker near cam  
            styleY  |> directionMarker near cam  
            styleZ  |> directionMarker near cam
          ] 
            //|> Sg.trafo (translation |> inv)
            //|> Sg.trafo refSysTrafo2   
            //|> Sg.trafo translation

        [refsystem] |> Sg.ofList |> Sg.onOff(model.isVisible)  
            
