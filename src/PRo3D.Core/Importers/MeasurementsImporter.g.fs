//182eb989-2a93-999e-bed7-9bfd91876fe3
//6e101e5f-6fc1-a5ba-5111-097dd2e72bac
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Core

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Core
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveMeasurementsImporterModel(value : MeasurementsImporterModel) =
    let _annotations_ =
        let inline __arg2 (m : PRo3D.Base.Annotation.AdaptiveAnnotation) (v : PRo3D.Base.Annotation.Annotation) =
            m.Update(v)
            m
        FSharp.Data.Traceable.ChangeableModelList(value.annotations, (fun (v : PRo3D.Base.Annotation.Annotation) -> PRo3D.Base.Annotation.AdaptiveAnnotation(v)), __arg2, (fun (m : PRo3D.Base.Annotation.AdaptiveAnnotation) -> m))
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : MeasurementsImporterModel) = AdaptiveMeasurementsImporterModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : MeasurementsImporterModel) -> AdaptiveMeasurementsImporterModel(value)) (fun (adaptive : AdaptiveMeasurementsImporterModel) (value : MeasurementsImporterModel) -> adaptive.Update(value))
    member __.Update(value : MeasurementsImporterModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<MeasurementsImporterModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _annotations_.Update(value.annotations)
    member __.Current = __adaptive
    member __.annotations = _annotations_ :> FSharp.Data.Adaptive.alist<PRo3D.Base.Annotation.AdaptiveAnnotation>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module MeasurementsImporterModelLenses = 
    type MeasurementsImporterModel with
        static member annotations_ = ((fun (self : MeasurementsImporterModel) -> self.annotations), (fun (value : FSharp.Data.Adaptive.IndexList<PRo3D.Base.Annotation.Annotation>) (self : MeasurementsImporterModel) -> { self with annotations = value }))

