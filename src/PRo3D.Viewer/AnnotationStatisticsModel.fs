namespace Pro3D.AnnotationStatistics

open Aardvark.Base
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation


[<ModelType>]
type AnnoStatsModel = {
    selectedAnnotations: alist<Annotation>
    selectedLengths:     alist<float>
}

module AnnoStats =
    let initial =
        {
            selectedAnnotations = AList.empty
            selectedLengths = AList.empty
        }
    

