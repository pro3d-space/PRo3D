//39220194-111d-6a3e-2efd-987c7d039398
//ccc1df35-f5da-a276-3d56-b020b77460cf
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Base.Annotation

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base.Annotation
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveColorByCategoryModel(value : ColorByCategoryModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _enabled_ = FSharp.Data.Adaptive.cval(value.enabled)
    let _attribute_ = FSharp.Data.Adaptive.cval(value.attribute)
    let _numericLegend_ = PRo3D.Base.AdaptiveFalseColorsModel(value.numericLegend)
    let _categoryColors_ = FSharp.Data.Adaptive.cval(value.categoryColors)
    let _noValueColor_ = Aardvark.UI.AdaptiveColorInput(value.noValueColor)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : ColorByCategoryModel) = AdaptiveColorByCategoryModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : ColorByCategoryModel) -> AdaptiveColorByCategoryModel(value)) (fun (adaptive : AdaptiveColorByCategoryModel) (value : ColorByCategoryModel) -> adaptive.Update(value))
    member __.Update(value : ColorByCategoryModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<ColorByCategoryModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _enabled_.Value <- value.enabled
            _attribute_.Value <- value.attribute
            _numericLegend_.Update(value.numericLegend)
            _categoryColors_.Value <- value.categoryColors
            _noValueColor_.Update(value.noValueColor)
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.enabled = _enabled_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.attribute = _attribute_ :> FSharp.Data.Adaptive.aval<ColorCategoryAttribute>
    member __.numericLegend = _numericLegend_
    member __.categoryColors = _categoryColors_ :> FSharp.Data.Adaptive.aval<FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Aardvark.UI.ColorInput>>
    member __.noValueColor = _noValueColor_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module ColorByCategoryModelLenses = 
    type ColorByCategoryModel with
        static member version_ = ((fun (self : ColorByCategoryModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : ColorByCategoryModel) -> { self with version = value }))
        static member enabled_ = ((fun (self : ColorByCategoryModel) -> self.enabled), (fun (value : Microsoft.FSharp.Core.bool) (self : ColorByCategoryModel) -> { self with enabled = value }))
        static member attribute_ = ((fun (self : ColorByCategoryModel) -> self.attribute), (fun (value : ColorCategoryAttribute) (self : ColorByCategoryModel) -> { self with attribute = value }))
        static member numericLegend_ = ((fun (self : ColorByCategoryModel) -> self.numericLegend), (fun (value : PRo3D.Base.FalseColorsModel) (self : ColorByCategoryModel) -> { self with numericLegend = value }))
        static member categoryColors_ = ((fun (self : ColorByCategoryModel) -> self.categoryColors), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Aardvark.UI.ColorInput>) (self : ColorByCategoryModel) -> { self with categoryColors = value }))
        static member noValueColor_ = ((fun (self : ColorByCategoryModel) -> self.noValueColor), (fun (value : Aardvark.UI.ColorInput) (self : ColorByCategoryModel) -> { self with noValueColor = value }))

