namespace Pro3D.AnnotationStatistics

open Aardvark.Base
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open PRo3D.Base.Annotation


[<ModelType>]
type AnnoStatsModel = {
    selectedAnnotations: IndexList<Annotation>
    selectedLengths:     IndexList<float>
}

module AnnoStats =
    let initial =
        {
            selectedAnnotations = IndexList.empty
            selectedLengths = IndexList.empty
        }
    

