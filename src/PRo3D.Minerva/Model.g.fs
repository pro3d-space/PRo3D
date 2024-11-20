//d3dab701-cafd-cd67-474a-c8d7e804c05e
//0a752333-7359-eb3f-d78a-eaadd7defca7
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Minerva

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Minerva
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveFeatureCollection(value : FeatureCollection) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _name_ = FSharp.Data.Adaptive.cval(value.name)
    let _typus_ = FSharp.Data.Adaptive.cval(value.typus)
    let _boundingBox_ = FSharp.Data.Adaptive.cval(value.boundingBox)
    let _features_ = FSharp.Data.Adaptive.clist(value.features)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : FeatureCollection) = AdaptiveFeatureCollection(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : FeatureCollection) -> AdaptiveFeatureCollection(value)) (fun (adaptive : AdaptiveFeatureCollection) (value : FeatureCollection) -> adaptive.Update(value))
    member __.Update(value : FeatureCollection) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<FeatureCollection>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _name_.Value <- value.name
            _typus_.Value <- value.typus
            _boundingBox_.Value <- value.boundingBox
            _features_.Value <- value.features
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.name = _name_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
    member __.typus = _typus_ :> FSharp.Data.Adaptive.aval<Typus>
    member __.boundingBox = _boundingBox_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Box2d>
    member __.features = _features_ :> FSharp.Data.Adaptive.alist<Feature>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module FeatureCollectionLenses = 
    type FeatureCollection with
        static member version_ = ((fun (self : FeatureCollection) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : FeatureCollection) -> { self with version = value }))
        static member name_ = ((fun (self : FeatureCollection) -> self.name), (fun (value : Microsoft.FSharp.Core.string) (self : FeatureCollection) -> { self with name = value }))
        static member typus_ = ((fun (self : FeatureCollection) -> self.typus), (fun (value : Typus) (self : FeatureCollection) -> { self with typus = value }))
        static member boundingBox_ = ((fun (self : FeatureCollection) -> self.boundingBox), (fun (value : Aardvark.Base.Box2d) (self : FeatureCollection) -> { self with boundingBox = value }))
        static member features_ = ((fun (self : FeatureCollection) -> self.features), (fun (value : FSharp.Data.Adaptive.IndexList<Feature>) (self : FeatureCollection) -> { self with features = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSgFeatures(value : SgFeatures) =
    let _names_ = FSharp.Data.Adaptive.cval(value.names)
    let _positions_ = FSharp.Data.Adaptive.cval(value.positions)
    let _colors_ = FSharp.Data.Adaptive.cval(value.colors)
    let _trafo_ = FSharp.Data.Adaptive.cval(value.trafo)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SgFeatures) = AdaptiveSgFeatures(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SgFeatures) -> AdaptiveSgFeatures(value)) (fun (adaptive : AdaptiveSgFeatures) (value : SgFeatures) -> adaptive.Update(value))
    member __.Update(value : SgFeatures) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SgFeatures>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _names_.Value <- value.names
            _positions_.Value <- value.positions
            _colors_.Value <- value.colors
            _trafo_.Value <- value.trafo
    member __.Current = __adaptive
    member __.names = _names_ :> FSharp.Data.Adaptive.aval<(Microsoft.FSharp.Core.string)[]>
    member __.positions = _positions_ :> FSharp.Data.Adaptive.aval<(Aardvark.Base.V3d)[]>
    member __.colors = _colors_ :> FSharp.Data.Adaptive.aval<(Aardvark.Base.C4b)[]>
    member __.trafo = _trafo_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Trafo3d>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SgFeaturesLenses = 
    type SgFeatures with
        static member names_ = ((fun (self : SgFeatures) -> self.names), (fun (value : (Microsoft.FSharp.Core.string)[]) (self : SgFeatures) -> { self with names = value }))
        static member positions_ = ((fun (self : SgFeatures) -> self.positions), (fun (value : (Aardvark.Base.V3d)[]) (self : SgFeatures) -> { self with positions = value }))
        static member colors_ = ((fun (self : SgFeatures) -> self.colors), (fun (value : (Aardvark.Base.C4b)[]) (self : SgFeatures) -> { self with colors = value }))
        static member trafo_ = ((fun (self : SgFeatures) -> self.trafo), (fun (value : Aardvark.Base.Trafo3d) (self : SgFeatures) -> { self with trafo = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveInstrumentColor(value : InstrumentColor) =
    let _mahli_ = FSharp.Data.Adaptive.cval(value.mahli)
    let _frontHazcam_ = FSharp.Data.Adaptive.cval(value.frontHazcam)
    let _mastcam_ = FSharp.Data.Adaptive.cval(value.mastcam)
    let _apxs_ = FSharp.Data.Adaptive.cval(value.apxs)
    let _frontHazcamR_ = FSharp.Data.Adaptive.cval(value.frontHazcamR)
    let _frontHazcamL_ = FSharp.Data.Adaptive.cval(value.frontHazcamL)
    let _mastcamR_ = FSharp.Data.Adaptive.cval(value.mastcamR)
    let _mastcamL_ = FSharp.Data.Adaptive.cval(value.mastcamL)
    let _chemLib_ = FSharp.Data.Adaptive.cval(value.chemLib)
    let _chemRmi_ = FSharp.Data.Adaptive.cval(value.chemRmi)
    let _color_ = Aardvark.UI.AdaptiveColorInput(value.color)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : InstrumentColor) = AdaptiveInstrumentColor(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : InstrumentColor) -> AdaptiveInstrumentColor(value)) (fun (adaptive : AdaptiveInstrumentColor) (value : InstrumentColor) -> adaptive.Update(value))
    member __.Update(value : InstrumentColor) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<InstrumentColor>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _mahli_.Value <- value.mahli
            _frontHazcam_.Value <- value.frontHazcam
            _mastcam_.Value <- value.mastcam
            _apxs_.Value <- value.apxs
            _frontHazcamR_.Value <- value.frontHazcamR
            _frontHazcamL_.Value <- value.frontHazcamL
            _mastcamR_.Value <- value.mastcamR
            _mastcamL_.Value <- value.mastcamL
            _chemLib_.Value <- value.chemLib
            _chemRmi_.Value <- value.chemRmi
            _color_.Update(value.color)
    member __.Current = __adaptive
    member __.mahli = _mahli_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.frontHazcam = _frontHazcam_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.mastcam = _mastcam_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.apxs = _apxs_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.frontHazcamR = _frontHazcamR_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.frontHazcamL = _frontHazcamL_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.mastcamR = _mastcamR_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.mastcamL = _mastcamL_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.chemLib = _chemLib_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.chemRmi = _chemRmi_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.C4b>
    member __.color = _color_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module InstrumentColorLenses = 
    type InstrumentColor with
        static member mahli_ = ((fun (self : InstrumentColor) -> self.mahli), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with mahli = value }))
        static member frontHazcam_ = ((fun (self : InstrumentColor) -> self.frontHazcam), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with frontHazcam = value }))
        static member mastcam_ = ((fun (self : InstrumentColor) -> self.mastcam), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with mastcam = value }))
        static member apxs_ = ((fun (self : InstrumentColor) -> self.apxs), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with apxs = value }))
        static member frontHazcamR_ = ((fun (self : InstrumentColor) -> self.frontHazcamR), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with frontHazcamR = value }))
        static member frontHazcamL_ = ((fun (self : InstrumentColor) -> self.frontHazcamL), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with frontHazcamL = value }))
        static member mastcamR_ = ((fun (self : InstrumentColor) -> self.mastcamR), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with mastcamR = value }))
        static member mastcamL_ = ((fun (self : InstrumentColor) -> self.mastcamL), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with mastcamL = value }))
        static member chemLib_ = ((fun (self : InstrumentColor) -> self.chemLib), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with chemLib = value }))
        static member chemRmi_ = ((fun (self : InstrumentColor) -> self.chemRmi), (fun (value : Aardvark.Base.C4b) (self : InstrumentColor) -> { self with chemRmi = value }))
        static member color_ = ((fun (self : InstrumentColor) -> self.color), (fun (value : Aardvark.UI.ColorInput) (self : InstrumentColor) -> { self with color = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveFeatureProperties(value : FeatureProperties) =
    let _pointSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.pointSize)
    let _textSize_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.textSize)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : FeatureProperties) = AdaptiveFeatureProperties(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : FeatureProperties) -> AdaptiveFeatureProperties(value)) (fun (adaptive : AdaptiveFeatureProperties) (value : FeatureProperties) -> adaptive.Update(value))
    member __.Update(value : FeatureProperties) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<FeatureProperties>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _pointSize_.Update(value.pointSize)
            _textSize_.Update(value.textSize)
    member __.Current = __adaptive
    member __.pointSize = _pointSize_
    member __.textSize = _textSize_
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module FeaturePropertiesLenses = 
    type FeatureProperties with
        static member pointSize_ = ((fun (self : FeatureProperties) -> self.pointSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : FeatureProperties) -> { self with pointSize = value }))
        static member textSize_ = ((fun (self : FeatureProperties) -> self.textSize), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : FeatureProperties) -> { self with textSize = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveQueryModel(value : QueryModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _minSol_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.minSol)
    let _maxSol_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.maxSol)
    let _distance_ = Aardvark.UI.Primitives.AdaptiveNumericInput(value.distance)
    let _filterLocation_ = FSharp.Data.Adaptive.cval(value.filterLocation)
    let _checkMAHLI_ = FSharp.Data.Adaptive.cval(value.checkMAHLI)
    let _checkFrontHazcam_ = FSharp.Data.Adaptive.cval(value.checkFrontHazcam)
    let _checkMastcam_ = FSharp.Data.Adaptive.cval(value.checkMastcam)
    let _checkAPXS_ = FSharp.Data.Adaptive.cval(value.checkAPXS)
    let _checkFrontHazcamR_ = FSharp.Data.Adaptive.cval(value.checkFrontHazcamR)
    let _checkFrontHazcamL_ = FSharp.Data.Adaptive.cval(value.checkFrontHazcamL)
    let _checkMastcamR_ = FSharp.Data.Adaptive.cval(value.checkMastcamR)
    let _checkMastcamL_ = FSharp.Data.Adaptive.cval(value.checkMastcamL)
    let _checkChemLib_ = FSharp.Data.Adaptive.cval(value.checkChemLib)
    let _checkChemRmi_ = FSharp.Data.Adaptive.cval(value.checkChemRmi)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : QueryModel) = AdaptiveQueryModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : QueryModel) -> AdaptiveQueryModel(value)) (fun (adaptive : AdaptiveQueryModel) (value : QueryModel) -> adaptive.Update(value))
    member __.Update(value : QueryModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<QueryModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _minSol_.Update(value.minSol)
            _maxSol_.Update(value.maxSol)
            _distance_.Update(value.distance)
            _filterLocation_.Value <- value.filterLocation
            _checkMAHLI_.Value <- value.checkMAHLI
            _checkFrontHazcam_.Value <- value.checkFrontHazcam
            _checkMastcam_.Value <- value.checkMastcam
            _checkAPXS_.Value <- value.checkAPXS
            _checkFrontHazcamR_.Value <- value.checkFrontHazcamR
            _checkFrontHazcamL_.Value <- value.checkFrontHazcamL
            _checkMastcamR_.Value <- value.checkMastcamR
            _checkMastcamL_.Value <- value.checkMastcamL
            _checkChemLib_.Value <- value.checkChemLib
            _checkChemRmi_.Value <- value.checkChemRmi
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.minSol = _minSol_
    member __.maxSol = _maxSol_
    member __.distance = _distance_
    member __.filterLocation = _filterLocation_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.V3d>
    member __.checkMAHLI = _checkMAHLI_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkFrontHazcam = _checkFrontHazcam_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkMastcam = _checkMastcam_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkAPXS = _checkAPXS_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkFrontHazcamR = _checkFrontHazcamR_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkFrontHazcamL = _checkFrontHazcamL_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkMastcamR = _checkMastcamR_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkMastcamL = _checkMastcamL_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkChemLib = _checkChemLib_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
    member __.checkChemRmi = _checkChemRmi_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module QueryModelLenses = 
    type QueryModel with
        static member version_ = ((fun (self : QueryModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : QueryModel) -> { self with version = value }))
        static member minSol_ = ((fun (self : QueryModel) -> self.minSol), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : QueryModel) -> { self with minSol = value }))
        static member maxSol_ = ((fun (self : QueryModel) -> self.maxSol), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : QueryModel) -> { self with maxSol = value }))
        static member distance_ = ((fun (self : QueryModel) -> self.distance), (fun (value : Aardvark.UI.Primitives.NumericInput) (self : QueryModel) -> { self with distance = value }))
        static member filterLocation_ = ((fun (self : QueryModel) -> self.filterLocation), (fun (value : Aardvark.Base.V3d) (self : QueryModel) -> { self with filterLocation = value }))
        static member checkMAHLI_ = ((fun (self : QueryModel) -> self.checkMAHLI), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkMAHLI = value }))
        static member checkFrontHazcam_ = ((fun (self : QueryModel) -> self.checkFrontHazcam), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkFrontHazcam = value }))
        static member checkMastcam_ = ((fun (self : QueryModel) -> self.checkMastcam), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkMastcam = value }))
        static member checkAPXS_ = ((fun (self : QueryModel) -> self.checkAPXS), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkAPXS = value }))
        static member checkFrontHazcamR_ = ((fun (self : QueryModel) -> self.checkFrontHazcamR), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkFrontHazcamR = value }))
        static member checkFrontHazcamL_ = ((fun (self : QueryModel) -> self.checkFrontHazcamL), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkFrontHazcamL = value }))
        static member checkMastcamR_ = ((fun (self : QueryModel) -> self.checkMastcamR), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkMastcamR = value }))
        static member checkMastcamL_ = ((fun (self : QueryModel) -> self.checkMastcamL), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkMastcamL = value }))
        static member checkChemLib_ = ((fun (self : QueryModel) -> self.checkChemLib), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkChemLib = value }))
        static member checkChemRmi_ = ((fun (self : QueryModel) -> self.checkChemRmi), (fun (value : Microsoft.FSharp.Core.bool) (self : QueryModel) -> { self with checkChemRmi = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSelectionModel(value : SelectionModel) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _selectedProducts_ = FSharp.Data.Adaptive.cset(value.selectedProducts)
    let _highlightedFrustra_ = FSharp.Data.Adaptive.cset(value.highlightedFrustra)
    let _singleSelectProduct_ = FSharp.Data.Adaptive.cval(value.singleSelectProduct)
    let _selectionMinDist_ = FSharp.Data.Adaptive.cval(value.selectionMinDist)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : SelectionModel) = AdaptiveSelectionModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : SelectionModel) -> AdaptiveSelectionModel(value)) (fun (adaptive : AdaptiveSelectionModel) (value : SelectionModel) -> adaptive.Update(value))
    member __.Update(value : SelectionModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<SelectionModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _selectedProducts_.Value <- value.selectedProducts
            _highlightedFrustra_.Value <- value.highlightedFrustra
            _singleSelectProduct_.Value <- value.singleSelectProduct
            _selectionMinDist_.Value <- value.selectionMinDist
            ()
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.selectedProducts = _selectedProducts_ :> FSharp.Data.Adaptive.aset<Microsoft.FSharp.Core.string>
    member __.highlightedFrustra = _highlightedFrustra_ :> FSharp.Data.Adaptive.aset<Microsoft.FSharp.Core.string>
    member __.singleSelectProduct = _singleSelectProduct_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
    member __.selectionMinDist = _selectionMinDist_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.float>
    member __.kdTree = __value.kdTree
    member __.flatPos = __value.flatPos
    member __.flatID = __value.flatID
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SelectionModelLenses = 
    type SelectionModel with
        static member version_ = ((fun (self : SelectionModel) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : SelectionModel) -> { self with version = value }))
        static member selectedProducts_ = ((fun (self : SelectionModel) -> self.selectedProducts), (fun (value : FSharp.Data.Adaptive.HashSet<Microsoft.FSharp.Core.string>) (self : SelectionModel) -> { self with selectedProducts = value }))
        static member highlightedFrustra_ = ((fun (self : SelectionModel) -> self.highlightedFrustra), (fun (value : FSharp.Data.Adaptive.HashSet<Microsoft.FSharp.Core.string>) (self : SelectionModel) -> { self with highlightedFrustra = value }))
        static member singleSelectProduct_ = ((fun (self : SelectionModel) -> self.singleSelectProduct), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : SelectionModel) -> { self with singleSelectProduct = value }))
        static member selectionMinDist_ = ((fun (self : SelectionModel) -> self.selectionMinDist), (fun (value : Microsoft.FSharp.Core.float) (self : SelectionModel) -> { self with selectionMinDist = value }))
        static member kdTree_ = ((fun (self : SelectionModel) -> self.kdTree), (fun (value : Aardvark.Geometry.PointKdTreeD<(Aardvark.Base.V3d)[], Aardvark.Base.V3d>) (self : SelectionModel) -> { self with kdTree = value }))
        static member flatPos_ = ((fun (self : SelectionModel) -> self.flatPos), (fun (value : Microsoft.FSharp.Core.array<Aardvark.Base.V3d>) (self : SelectionModel) -> { self with flatPos = value }))
        static member flatID_ = ((fun (self : SelectionModel) -> self.flatID), (fun (value : Microsoft.FSharp.Core.array<Microsoft.FSharp.Core.string>) (self : SelectionModel) -> { self with flatID = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveSession(value : Session) =
    let _version_ = FSharp.Data.Adaptive.cval(value.version)
    let _queryFilter_ = AdaptiveQueryModel(value.queryFilter)
    let _featureProperties_ = AdaptiveFeatureProperties(value.featureProperties)
    let _selection_ = AdaptiveSelectionModel(value.selection)
    let _queries_ = FSharp.Data.Adaptive.cval(value.queries)
    let _filteredFeatures_ = FSharp.Data.Adaptive.clist(value.filteredFeatures)
    let _dataFilePath_ = FSharp.Data.Adaptive.cval(value.dataFilePath)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Session) = AdaptiveSession(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Session) -> AdaptiveSession(value)) (fun (adaptive : AdaptiveSession) (value : Session) -> adaptive.Update(value))
    member __.Update(value : Session) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Session>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _version_.Value <- value.version
            _queryFilter_.Update(value.queryFilter)
            _featureProperties_.Update(value.featureProperties)
            _selection_.Update(value.selection)
            _queries_.Value <- value.queries
            _filteredFeatures_.Value <- value.filteredFeatures
            _dataFilePath_.Value <- value.dataFilePath
    member __.Current = __adaptive
    member __.version = _version_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.int>
    member __.queryFilter = _queryFilter_
    member __.featureProperties = _featureProperties_
    member __.selection = _selection_
    member __.queries = _queries_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>>
    member __.filteredFeatures = _filteredFeatures_ :> FSharp.Data.Adaptive.alist<Feature>
    member __.dataFilePath = _dataFilePath_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.string>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module SessionLenses = 
    type Session with
        static member version_ = ((fun (self : Session) -> self.version), (fun (value : Microsoft.FSharp.Core.int) (self : Session) -> { self with version = value }))
        static member queryFilter_ = ((fun (self : Session) -> self.queryFilter), (fun (value : QueryModel) (self : Session) -> { self with queryFilter = value }))
        static member featureProperties_ = ((fun (self : Session) -> self.featureProperties), (fun (value : FeatureProperties) (self : Session) -> { self with featureProperties = value }))
        static member selection_ = ((fun (self : Session) -> self.selection), (fun (value : SelectionModel) (self : Session) -> { self with selection = value }))
        static member queries_ = ((fun (self : Session) -> self.queries), (fun (value : Microsoft.FSharp.Collections.list<Microsoft.FSharp.Core.string>) (self : Session) -> { self with queries = value }))
        static member filteredFeatures_ = ((fun (self : Session) -> self.filteredFeatures), (fun (value : FSharp.Data.Adaptive.IndexList<Feature>) (self : Session) -> { self with filteredFeatures = value }))
        static member dataFilePath_ = ((fun (self : Session) -> self.dataFilePath), (fun (value : Microsoft.FSharp.Core.string) (self : Session) -> { self with dataFilePath = value }))
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveMinervaModel(value : MinervaModel) =
    let _session_ = AdaptiveSession(value.session)
    let _data_ = AdaptiveFeatureCollection(value.data)
    let _kdTreeBounds_ = FSharp.Data.Adaptive.cval(value.kdTreeBounds)
    let _hoveredProduct_ = FSharp.Data.Adaptive.cval(value.hoveredProduct)
    let _solLabels_ = FSharp.Data.Adaptive.cmap(value.solLabels)
    let _sgFeatures_ = AdaptiveSgFeatures(value.sgFeatures)
    let _selectedSgFeatures_ = AdaptiveSgFeatures(value.selectedSgFeatures)
    let _picking_ = FSharp.Data.Adaptive.cval(value.picking)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : MinervaModel) = AdaptiveMinervaModel(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : MinervaModel) -> AdaptiveMinervaModel(value)) (fun (adaptive : AdaptiveMinervaModel) (value : MinervaModel) -> adaptive.Update(value))
    member __.Update(value : MinervaModel) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<MinervaModel>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _session_.Update(value.session)
            _data_.Update(value.data)
            _kdTreeBounds_.Value <- value.kdTreeBounds
            _hoveredProduct_.Value <- value.hoveredProduct
            _solLabels_.Value <- value.solLabels
            _sgFeatures_.Update(value.sgFeatures)
            _selectedSgFeatures_.Update(value.selectedSgFeatures)
            _picking_.Value <- value.picking
    member __.Current = __adaptive
    member __.session = _session_
    member __.data = _data_
    member __.comm = __value.comm
    member __.vplMessages = __value.vplMessages
    member __.kdTreeBounds = _kdTreeBounds_ :> FSharp.Data.Adaptive.aval<Aardvark.Base.Box3d>
    member __.hoveredProduct = _hoveredProduct_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.Option<SelectedProduct>>
    member __.solLabels = _solLabels_ :> FSharp.Data.Adaptive.amap<Microsoft.FSharp.Core.string, Aardvark.Base.V3d>
    member __.sgFeatures = _sgFeatures_
    member __.selectedSgFeatures = _selectedSgFeatures_
    member __.picking = _picking_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.bool>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module MinervaModelLenses = 
    type MinervaModel with
        static member session_ = ((fun (self : MinervaModel) -> self.session), (fun (value : Session) (self : MinervaModel) -> { self with session = value }))
        static member data_ = ((fun (self : MinervaModel) -> self.data), (fun (value : FeatureCollection) (self : MinervaModel) -> { self with data = value }))
        static member comm_ = ((fun (self : MinervaModel) -> self.comm), (fun (value : Microsoft.FSharp.Core.option<PRo3D.Minerva.Communication.Communicator.Communicator>) (self : MinervaModel) -> { self with comm = value }))
        static member vplMessages_ = ((fun (self : MinervaModel) -> self.vplMessages), (fun (value : FSharp.Data.Adaptive.ThreadPool<MinervaAction>) (self : MinervaModel) -> { self with vplMessages = value }))
        static member kdTreeBounds_ = ((fun (self : MinervaModel) -> self.kdTreeBounds), (fun (value : Aardvark.Base.Box3d) (self : MinervaModel) -> { self with kdTreeBounds = value }))
        static member hoveredProduct_ = ((fun (self : MinervaModel) -> self.hoveredProduct), (fun (value : Microsoft.FSharp.Core.Option<SelectedProduct>) (self : MinervaModel) -> { self with hoveredProduct = value }))
        static member solLabels_ = ((fun (self : MinervaModel) -> self.solLabels), (fun (value : FSharp.Data.Adaptive.HashMap<Microsoft.FSharp.Core.string, Aardvark.Base.V3d>) (self : MinervaModel) -> { self with solLabels = value }))
        static member sgFeatures_ = ((fun (self : MinervaModel) -> self.sgFeatures), (fun (value : SgFeatures) (self : MinervaModel) -> { self with sgFeatures = value }))
        static member selectedSgFeatures_ = ((fun (self : MinervaModel) -> self.selectedSgFeatures), (fun (value : SgFeatures) (self : MinervaModel) -> { self with selectedSgFeatures = value }))
        static member picking_ = ((fun (self : MinervaModel) -> self.picking), (fun (value : Microsoft.FSharp.Core.bool) (self : MinervaModel) -> { self with picking = value }))

