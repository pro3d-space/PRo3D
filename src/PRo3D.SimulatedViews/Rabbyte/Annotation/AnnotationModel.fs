namespace Rabbyte.Annotation

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI

open Rabbyte.Drawing

open Adaptify

type ClippingVolumeType = 
    | Direction of V3d
    | Point of V3d
    | Points of IndexList<V3d>

//[<ModelType>]
//type ExtAnnotation = 
//    {
//        annotation      : Annotation
//        style           : BrushStyle
//        clippingVolume  : ClippingVolumeType

//    }


//[<ModelType>]
//type AnnotationModel = 
//    {
//        annotations         : IndexList<Annotation>
//        annotationsGrouped  : HashMap<C4b, IndexList<Annotation>>
//        showDebug           : bool
//        extrusionOffset     : float
//    }

//type AnnotationAction = 
//    | AddAnnotation of DrawingModel*Option<ClippingVolumeType>
//    | ChangeExtrusionOffset of float
//    | ShowDebugVis
//    //| RemoveDrawing of DrawingModel
//    //| EditDrawing of DrawingModel

//[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
//module AnnotationModel = 
    
//    let initExtAnnotation =
//        {
//            annotation     = Annotation.initial
//            style          = DrawingModel.defaultStyle
//            primitiveType  = PrimitveType.
//            clippingVolume = Direction V3d.ZAxis //Points ([V3d.IOO; V3d.OIO; -V3d.OIO; -V3d.IOO] |> IndexList.ofList)//Point V3d.Zero// Direction (V3d.ZAxis)
//        }

//    let convertDrawingToAnnotation (drawingModel:DrawingModel) (clippingVolumeType:Option<ClippingVolumeType>) = 
//        let defaultClippingVolume = 
//            {initExtAnnotation with 
//                annotation = {Annotation.initial with 
//                                points = drawingModel.points
//                                segments = drawingModel.segments
//                                }
//                style = drawingModel.style
//                primitiveType = drawingModel.primitiveType
//            }

//        match clippingVolumeType with
//        | Some t -> { defaultClippingVolume with clippingVolume = t }
//        | None -> defaultClippingVolume

//    let convertAnnotationToDrawing (annotation:Annotation) = 
//        { DrawingModel.initial with
//            annotation = {
//                style = annotation.style
//                points = annotation.points
//                segments = annotation.segments
//                primitiveType = annotation.primitiveType
//            }

//        }        

//    let initial = 
//        {
//            annotations = IndexList.Empty
//            annotationsGrouped = HashMap.Empty
//            showDebug   = false
//            extrusionOffset = 10.0
//        }