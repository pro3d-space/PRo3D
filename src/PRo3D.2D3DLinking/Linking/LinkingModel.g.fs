//924fa8f3-80e2-5e42-4791-7b19f269b110
//029ccd70-f150-f5a2-b247-64d41766f295
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Linking

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Linking
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveLinkingModel(value : LinkingModel) =
    let _frustums_ = FSharp.Data.Adaptive.cval(value.frustums)
    let _instrumentParameter_ = FSharp.Data.Adaptive.cmap(value.instrumentParameter)
    let _trafo_ = FSharp.Data.Adaptive.cval(value.trafo)
    let _pickingPos_ = FSharp.Data.Adaptive.cval(value.pickingPos)
    let _filterProducts_ = FSharp.Data.Adaptive.cmap(value.filterProducts)
    let _overlayFeature_ = FSharp.Data.Adaptive.cval(value.overlayFeature)
    let _frustumOpacity_ = FSharp.Data.Adaptive.cval(value.frustumOpacity)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : LinkingModel) = AdaptiveLinkingModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : LinkingModel) -> AdaptiveLinkingModel(value)) (fun (adaptive : AdaptiveLinkingModel) (value : LinkingModel) -> adaptive.Update(value))
    member __.Update(value : LinkingModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<LinkingModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _frustums_.Value <- value.frustums
            _instrumentParameter_.Value <- value.instrumentParameter
            _trafo_.Value <- value.trafo
            _pickingPos_.Value <- value.pickingPos
            _filterProducts_.Value <- value.filterProducts
            _overlayFeature_.Value <- value.overlayFeature
            _frustumOpacity_.Value <- value.frustumOpacity
    member __.Current = __adaptive
    member __.frustums = _frustums_ :> FSharp.Data.Adaptive.aval<FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, LinkingFeature>>
    member __.instrumentParameter = _instrumentParameter_ :> FSharp.Data.Adaptive.amap<PRo3D.Minerva.Instrument, InstrumentParameter>
    member __.trafo = _trafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
    member __.pickingPos = _pickingPos_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<Aardvark.Base.V3d>>
    member __.filterProducts = _filterProducts_ :> FSharp.Data.Adaptive.amap<PRo3D.Minerva.Instrument, Microsoft.FSharp.Core.bool>
    member __.overlayFeature = _overlayFeature_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<LinkingFeatureDisplay>>
    member __.frustumOpacity = _frustumOpacity_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module LinkingModelLenses = 
    type LinkingModel with
        static member frustums_ = ((fun (self : LinkingModel) -> self.frustums), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, LinkingFeature>) (self : LinkingModel) -> { self with frustums = value }))
        static member instrumentParameter_ = ((fun (self : LinkingModel) -> self.instrumentParameter), (fun (value : FSharp.Data.Adaptive.HashMap<PRo3D.Minerva.Instrument, InstrumentParameter>) (self : LinkingModel) -> { self with instrumentParameter = value }))
        static member trafo_ = ((fun (self : LinkingModel) -> self.trafo), (fun (value : Aardvark.Base.Trafo3d) (self : LinkingModel) -> { self with trafo = value }))
        static member pickingPos_ = ((fun (self : LinkingModel) -> self.pickingPos), (fun (value : Microsoft.FSharp.Core.Option<Aardvark.Base.V3d>) (self : LinkingModel) -> { self with pickingPos = value }))
        static member filterProducts_ = ((fun (self : LinkingModel) -> self.filterProducts), (fun (value : FSharp.Data.Adaptive.HashMap<PRo3D.Minerva.Instrument, Microsoft.FSharp.Core.bool>) (self : LinkingModel) -> { self with filterProducts = value }))
        static member overlayFeature_ = ((fun (self : LinkingModel) -> self.overlayFeature), (fun (value : Microsoft.FSharp.Core.Option<LinkingFeatureDisplay>) (self : LinkingModel) -> { self with overlayFeature = value }))
        static member frustumOpacity_ = ((fun (self : LinkingModel) -> self.frustumOpacity), (fun (value : Microsoft.FSharp.Core.float) (self : LinkingModel) -> { self with frustumOpacity = value }))

