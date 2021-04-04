namespace PRo3D.Base

open FSharp.Data.Adaptive

open Aardvark.Base

open PRo3D.Base
open PRo3D.Base.Annotation


module AnnotationRendering = 
    
    let bakeAnnotations (annotations : aset<AdaptiveAnnotation>) = 
        AVal.custom (fun token -> 
            let annotations = annotations.Content.GetValue(token)
            for a in annotations do
                failwith ""
        )