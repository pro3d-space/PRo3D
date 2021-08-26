namespace PRo3D.Base

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.UI    
open OpcViewer.Base


module OutlineEffect =


    //module Shader =
    //    open FShade
       
    //    type StencilVertex = 
    //        {
    //            [<Position>] pos :  V4d
    //            [<SourceVertexIndex>] i : int
    //        }
    
    //    let lines (t : Triangle<StencilVertex>) =
    //        line {
    //            yield t.P0
    //            yield t.P1
    //            restartStrip()
                
    //            yield t.P1
    //            yield t.P2
    //            restartStrip()
    
    //            yield t.P2
    //            yield t.P0
    //            restartStrip()
    //        }

    let read a =
      { StencilMode.None with 
          Comparison = ComparisonFunction.Greater
          CompareMask = StencilMask 0xff
          Reference = a
      }
      //StencilMode(StencilOperationFunction.Keep, StencilOperationFunction.Keep, StencilOperationFunction.Keep, StencilCompareFunction.Greater, a, 0xffu)

    let write a =
      { StencilMode.None with 
          Comparison = ComparisonFunction.Greater
          CompareMask = StencilMask 0xff
          Reference = a
          Pass = StencilOperation.Replace
          DepthFail = StencilOperation.Replace
          Fail = StencilOperation.Keep
      }

    // NOTE: sg MUST only hold pure Sg without any modes or shaders!
    let createForSg (outlineGroup: int) (pass: RenderPass) (color: C4f) sg =
        let sg = sg |> Sg.uniform "Color" (AVal.constant color)
        
        let mask = 
            sg 
            |> Sg.stencilMode (AVal.constant (write outlineGroup))
            |> Sg.writeBuffers' (Set.ofList [DefaultSemantic.Stencil])
            |> Sg.depthTest (AVal.init DepthTest.None)
            |> Sg.cullMode (AVal.init CullMode.None)
            |> Sg.blendMode (AVal.init BlendMode.Blend)
            |> Sg.fillMode (AVal.init FillMode.Fill)
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
                do! DefaultSurfaces.sgColor
            }

        let outline =
            sg 
            |> Sg.stencilMode (AVal.constant (read outlineGroup))
            |> Sg.writeBuffers' (Set.ofList [DefaultSemantic.Colors])
            |> Sg.depthTest (AVal.init DepthTest.None)
            |> Sg.cullMode (AVal.init CullMode.None)
            |> Sg.blendMode (AVal.init BlendMode.Blend)
            |> Sg.fillMode (AVal.init FillMode.Fill)
            |> Sg.pass pass
            |> Sg.uniform "LineWidth" (AVal.constant 5.0)
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
                do! Shader.lines
                do! DefaultSurfaces.thickLine
                do! DefaultSurfaces.thickLineRoundCaps
                do! DefaultSurfaces.sgColor
            }
        
        [ mask; outline ] |> Sg.ofList
 
    let createForLine 
        (points: aval<V3d[]>) 
        (outlineGroup: int) 
        (trafo: aval<Trafo3d>) 
        (color: aval<C4b>) 
        (lineWidth: aval<float>)
        (outlineWidth: aval<float>)
        (pass: RenderPass)
        (outlinePass: RenderPass) =
              
        let sg = 
            Sg.edgeLines false points trafo // edgeLines applies invers trafo for stable points....
            |> Aardvark.SceneGraph.SgPrimitives.Sg.lines color
            |> Sg.noEvents
            |> Sg.trafo trafo
            
        let mask = 
            sg
            |> Sg.uniform "LineWidth" lineWidth   
            |> Sg.stencilMode (AVal.constant (write outlineGroup))
            |> Sg.writeBuffers' (Set.ofList [DefaultSemantic.Stencil])                              
            |> Sg.pass pass
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
                do! DefaultSurfaces.vertexColor
                do! DefaultSurfaces.thickLine                
            }                  
            
        let outline = 
            sg
            |> Sg.uniform "LineWidth" outlineWidth           
            |> Sg.stencilMode (AVal.constant (read outlineGroup))
            |> Sg.writeBuffers' (Set.ofList [DefaultSemantic.Colors])
            |> Sg.pass outlinePass
            |> Sg.depthTest (AVal.constant DepthTest.None)
            |> Sg.shader {
                do! DefaultSurfaces.stableTrafo
                do! DefaultSurfaces.thickLine
                do! DefaultSurfaces.thickLineRoundCaps
                do! DefaultSurfaces.vertexColor
            }
            
        [mask; outline] |> Sg.ofList 


    let screenSpaceStablePoints = 
        FShade.Effect.compose [
            toEffect Shader.ScreenSpaceScale.screenSpaceScale
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor
        ]
        
    let outlineShader = 
        FShade.Effect.compose [
            toEffect Shader.ScreenSpaceScale.screenSpaceScale
            toEffect DefaultSurfaces.stableTrafo
            toEffect Shader.lines
            toEffect DefaultSurfaces.thickLine
            toEffect DefaultSurfaces.thickLineRoundCaps
            //toEffect DefaultSurfaces.constantColor C4f.Red
            toEffect DefaultSurfaces.vertexColor
        ]

    let createForPoints
        (view : aval<CameraView>)
        (viewportSize : aval<V2i>)
        (points : aval<V3d[]>) 
        (outlineGroup: int)
        (color: aval<C4b>) 
        (pointWidth : aval<float>)
        (outlineWidth : aval<float>)
        (pass: RenderPass)
        (outlinePass: RenderPass) =
        
        // TODO ...Points-Outline is broken! (?not stable with trafo / screenSpaceScaling)

        //let sg = Sg.drawSpheres points outlineWidth (AVal.constant C4b.Red)
        let sg = Sg.drawSpheresFast view viewportSize points outlineWidth (AVal.constant C4b.Red)
        //let sgo = Sg.drawSpheres points outlineWidth (AVal.constant C4b.Red)   // using this instead -> visible broken visualization when point is on the edge of the view-frustrum

        let mask = 
            sg
            |> Sg.stencilMode (AVal.constant (write outlineGroup))
            |> Sg.writeBuffers' (Set.ofList [DefaultSemantic.Stencil;DefaultSemantic.Depth])
            |> Sg.pass pass
            |> Sg.effect [screenSpaceStablePoints]
            
        let outline = 
            sg //sgo
            |> Sg.stencilMode (AVal.constant (read outlineGroup))
            |> Sg.writeBuffers' (Set.ofList [DefaultSemantic.Colors; DefaultSemantic.Depth])
            |> Sg.pass outlinePass
            |> Sg.depthTest (AVal.constant DepthTest.None)
            |> Sg.uniform "LineWidth" outlineWidth
            |> Sg.effect [outlineShader]

        [mask; (*outline;*) sg |> Sg.pass outlinePass] |> Sg.ofList 
        
    type PointOrLine = 
        | Point
        | Line
        | Both



    let createForLineOrPoint (view : aval<CameraView>) (viewportSize : aval<V2i>) (mode: PointOrLine) (color: aval<C4b>) (width: aval<float>) (outlineWidth: float) (pass: RenderPass) (trafo: aval<Trafo3d>) (points: aval<V3d[]>) =
            
        let outlinePass = RenderPass.after "outline" RenderPassOrder.Arbitrary pass
        let outlineWidth = width |> AVal.map (fun x -> x + outlineWidth) // 3.0
        let outlineGroup = 1

        aset {
            let! ps = points
            let length = ps.Length
            match mode, length with 
            | _, 0  -> yield Sg.empty
            | _, 1
            | Point, _ -> 
                yield createForPoints view viewportSize points outlineGroup color width outlineWidth pass outlinePass
            | Line, _ ->
                yield createForLine points outlineGroup trafo color width outlineWidth pass outlinePass
            | Both, _ -> 
                yield createForPoints view  viewportSize points outlineGroup color width outlineWidth pass outlinePass
                yield createForLine points outlineGroup trafo color width outlineWidth pass outlinePass
        } 
        |> Sg.set