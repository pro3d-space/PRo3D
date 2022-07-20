
namespace Pro3D.AnnotationStatistics

open System
open Adaptify
open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering
open FSharp.Data.Adaptive


[<ModelType>]
type LineSelectionModel = 
    {
        annotationLines : HashMap<Guid, V2d*V2d>    
        lineStart       : V2i
        lineEnd         : V2i
        draw            : Boolean
    }

module LineSelection =
    let initial =
        {
            annotationLines = HashMap.empty
            lineStart = V2i.OO
            lineEnd = V2i.OO
            draw = false
        }

    //let transformToPixelSpace (p:V3d) (camera:CameraControllerState) (frustum:Frustum) (viewPortSize:V2i) =
    //    let cam = Camera.create camera.view frustum                                                      
    //    let t = cam.cameraView.ViewTrafo * (cam.frustum |> Frustum.projTrafo)                            
    //    let ndc = t.Forward.TransformPosProj(p)
    //    let screenspace = ndc.XY * 0.5 + V2d.Half        
    //    let temp1 = V2d(screenspace.X,1.0-screenspace.Y)
    //    let temp2 = temp1 * V2d viewPortSize       
    //    V2i((int)temp2.X, (int)temp2.Y)
        






