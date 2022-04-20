namespace Rabbyte.Annotation

//open Aardvark.Base
//open FSharp.Data.Adaptive
//open Aardvark.Rendering
//open Aardvark.Application
//open Aardvark.SceneGraph

//open Aardvark.UI
//open Aardvark.UI.Primitives

//open OpcViewer.Base
//open FShade.Primitives
//open PRo3D.Base.Annotation
//open PRo3D.Core.Drawing
//open PRo3D.Core

//module PolygonArea =
////    open Rabbyte.Drawing

////    let update (model: AnnotationModel) (act: AnnotationAction) =
////        match act with
////        | ChangeExtrusionOffset offset -> { model with extrusionOffset = offset }
////        | ShowDebugVis -> { model with showDebug = not model.showDebug }
////        | AddAnnotation (drawingModel, clippinVolumType) -> 
////            let annotation = AnnotationModel.convertDrawingToAnnotation drawingModel clippinVolumType
////            let updatedAnnotation = model.annotations |> IndexList.prepend annotation
            
////            let updatedAnnotationsFilledPolygon = 
////                match drawingModel.primitiveType with
////                    | Polygon -> 
////                        model.annotationsGrouped 
////                            |> HashMap.alter annotation.style.primary.c (fun x -> 
////                                match x with 
////                                    | Some y -> Some (y |> IndexList.prepend annotation)
////                                    | None -> Some (IndexList.single annotation))    
////                    | _ -> model.annotationsGrouped

////            { model with annotations = updatedAnnotation; annotationsGrouped = updatedAnnotationsFilledPolygon }

//    let drawOutlines (near: aval<float>) (far: aval<float>) (annotations : alist<Annotation>) = 
//        annotations
//        |> AList.map (fun x -> DrawingApp.drawContour x.points x.segments x.style near far |> Sg.noEvents)
//        |> AList.toASet
//        |> Sg.set

//    let viewOutlines (near: aval<float>) (far: aval<float>) (model: AdaptiveAnnotation) = 
//        model |> drawOutlines near far

//    let viewGrouped (near: aval<float>) (far: aval<float>) (startRenderPass: RenderPass) (model: AdaptiveAnnotation) : (ISg<'a> * RenderPass) = 

//        let filledSg, nextRenderPass = 
//            model |> AnnotationSg.drawAnnotationsFilledGrouped (RenderPass.after "" RenderPassOrder.Arbitrary startRenderPass)
//        let outlineSg = 
//            model |> drawOutlines near far
//            |> Sg.pass nextRenderPass

//        let sg = 
//            [ filledSg; outlineSg ] 
//            |> Sg.ofList

//        sg, nextRenderPass

//    let viewSeq (near: aval<float>) (far: aval<float>) (startRenderPass: RenderPass) (model: AdaptiveAnnotation) : (ISg<'a> * RenderPass) =
        
//        let filledSg, nextRenderPass =
//            model |> AnnotationSg.drawAnnotationsFilledSeq (RenderPass.after "" RenderPassOrder.Arbitrary startRenderPass)
//        let outlineSg = 
//            model |> drawOutlines near far
//            |> Sg.pass nextRenderPass

//        let sg = 
//            [ filledSg; outlineSg ] 
//            |> Sg.ofList

//        sg, nextRenderPass

////    let viewGui (model: AdaptiveAnnotationModel) =
////        let style' = "color: white; font-family:Consolas;"

////        table [clazz "item"] [
////            tr [] [ 
////                checkbox [clazz "ui inverted toggle checkbox"] model.showDebug ShowDebugVis "Show Debug Vis:"
////            ]
                    
////            tr[][
////                td[style style'][text "Offset:"]
////                td[style style'][numeric { min = 0.1; max = 20.0; smallStep = 0.1; largeStep= 1.0 } [clazz "ui inverted input"] model.extrusionOffset ChangeExtrusionOffset]
////            ]
////        ]