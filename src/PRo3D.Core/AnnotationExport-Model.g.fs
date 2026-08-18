//97a0f710-da16-c757-3073-bd9ebc53f0af
//a7192677-a9e7-038c-c7e2-e5d64f330d1b
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
type AdaptiveAnnotationExportModel(value : AnnotationExportModel) =
    let _isOpen_ = FSharp.Data.Adaptive.cval(value.isOpen)
    let _preset_ = FSharp.Data.Adaptive.cval(value.preset)
    let _format_ = FSharp.Data.Adaptive.cval(value.format)
    let _granularity_ = FSharp.Data.Adaptive.cval(value.granularity)
    let _scope_ = FSharp.Data.Adaptive.cval(value.scope)
    let _coordinates_ = FSharp.Data.Adaptive.cval(value.coordinates)
    let _longitude_ = FSharp.Data.Adaptive.cval(value.longitude)
    let _signedLongitude_ = FSharp.Data.Adaptive.cval(value.signedLongitude)
    let _useSampledPoints_ = FSharp.Data.Adaptive.cval(value.useSampledPoints)
    let _annotationFields_ = FSharp.Data.Adaptive.cset(value.annotationFields)
    let _pointFields_ = FSharp.Data.Adaptive.cset(value.pointFields)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : AnnotationExportModel) = AdaptiveAnnotationExportModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : AnnotationExportModel) -> AdaptiveAnnotationExportModel(value)) (fun (adaptive : AdaptiveAnnotationExportModel) (value : AnnotationExportModel) -> adaptive.Update(value))
    member __.Update(value : AnnotationExportModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<AnnotationExportModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _isOpen_.Value <- value.isOpen
            _preset_.Value <- value.preset
            _format_.Value <- value.format
            _granularity_.Value <- value.granularity
            _scope_.Value <- value.scope
            _coordinates_.Value <- value.coordinates
            _longitude_.Value <- value.longitude
            _signedLongitude_.Value <- value.signedLongitude
            _useSampledPoints_.Value <- value.useSampledPoints
            _annotationFields_.Value <- value.annotationFields
            _pointFields_.Value <- value.pointFields
    member __.Current = __adaptive
    member __.isOpen = _isOpen_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.preset = _preset_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.ExportPreset>
    member __.format = _format_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.ExportFormat>
    member __.granularity = _granularity_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.ExportGranularity>
    member __.scope = _scope_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.ExportScope>
    member __.coordinates = _coordinates_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.CoordinateMode>
    member __.longitude = _longitude_ :> FSharp.Data.Adaptive.aval<PRo3D.Base.Annotation.LongitudeConvention>
    member __.signedLongitude = _signedLongitude_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.useSampledPoints = _useSampledPoints_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.annotationFields = _annotationFields_ :> FSharp.Data.Adaptive.aset<PRo3D.Base.Annotation.AnnotationField>
    member __.pointFields = _pointFields_ :> FSharp.Data.Adaptive.aset<PRo3D.Base.Annotation.PointField>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module AnnotationExportModelLenses = 
    type AnnotationExportModel with
        static member isOpen_ = ((fun (self : AnnotationExportModel) -> self.isOpen), (fun (value : Microsoft.FSharp.Core.bool) (self : AnnotationExportModel) -> { self with isOpen = value }))
        static member preset_ = ((fun (self : AnnotationExportModel) -> self.preset), (fun (value : PRo3D.Base.Annotation.ExportPreset) (self : AnnotationExportModel) -> { self with preset = value }))
        static member format_ = ((fun (self : AnnotationExportModel) -> self.format), (fun (value : PRo3D.Base.Annotation.ExportFormat) (self : AnnotationExportModel) -> { self with format = value }))
        static member granularity_ = ((fun (self : AnnotationExportModel) -> self.granularity), (fun (value : PRo3D.Base.Annotation.ExportGranularity) (self : AnnotationExportModel) -> { self with granularity = value }))
        static member scope_ = ((fun (self : AnnotationExportModel) -> self.scope), (fun (value : PRo3D.Base.Annotation.ExportScope) (self : AnnotationExportModel) -> { self with scope = value }))
        static member coordinates_ = ((fun (self : AnnotationExportModel) -> self.coordinates), (fun (value : PRo3D.Base.Annotation.CoordinateMode) (self : AnnotationExportModel) -> { self with coordinates = value }))
        static member longitude_ = ((fun (self : AnnotationExportModel) -> self.longitude), (fun (value : PRo3D.Base.Annotation.LongitudeConvention) (self : AnnotationExportModel) -> { self with longitude = value }))
        static member signedLongitude_ = ((fun (self : AnnotationExportModel) -> self.signedLongitude), (fun (value : Microsoft.FSharp.Core.bool) (self : AnnotationExportModel) -> { self with signedLongitude = value }))
        static member useSampledPoints_ = ((fun (self : AnnotationExportModel) -> self.useSampledPoints), (fun (value : Microsoft.FSharp.Core.bool) (self : AnnotationExportModel) -> { self with useSampledPoints = value }))
        static member annotationFields_ = ((fun (self : AnnotationExportModel) -> self.annotationFields), (fun (value : FSharp.Data.Adaptive.HashSet<PRo3D.Base.Annotation.AnnotationField>) (self : AnnotationExportModel) -> { self with annotationFields = value }))
        static member pointFields_ = ((fun (self : AnnotationExportModel) -> self.pointFields), (fun (value : FSharp.Data.Adaptive.HashSet<PRo3D.Base.Annotation.PointField>) (self : AnnotationExportModel) -> { self with pointFields = value }))

