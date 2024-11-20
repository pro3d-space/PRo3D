//92c93bd6-05b9-7a4d-f938-102134dbf154
//65bb462e-7231-459f-5cbc-8988f75f3fb2
#nowarn "49" // upper case patterns
#nowarn "66" // upcast is unncecessary
#nowarn "1337" // internal types
#nowarn "1182" // value is unused
namespace rec PRo3D.Base

open System
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base
[<System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
type AdaptiveCalendar(value : Calendar) =
    let _date_ = FSharp.Data.Adaptive.cval(value.date)
    let _minDate_ = FSharp.Data.Adaptive.cval(value.minDate)
    let _maxDate_ = FSharp.Data.Adaptive.cval(value.maxDate)
    let _label_ = FSharp.Data.Adaptive.cval(value.label)
    let mutable __value = value
    let __adaptive = FSharp.Data.Adaptive.AVal.custom((fun (token : FSharp.Data.Adaptive.AdaptiveToken) -> __value))
    static member Create(value : Calendar) = AdaptiveCalendar(value)
    static member Unpersist = Adaptify.Unpersist.create (fun (value : Calendar) -> AdaptiveCalendar(value)) (fun (adaptive : AdaptiveCalendar) (value : Calendar) -> adaptive.Update(value))
    member __.Update(value : Calendar) =
        if Microsoft.FSharp.Core.Operators.not((FSharp.Data.Adaptive.ShallowEqualityComparer<Calendar>.ShallowEquals(value, __value))) then
            __value <- value
            __adaptive.MarkOutdated()
            _date_.Value <- value.date
            _minDate_.Value <- value.minDate
            _maxDate_.Value <- value.maxDate
            _label_.Value <- value.label
    member __.Current = __adaptive
    member __.date = _date_ :> FSharp.Data.Adaptive.aval<System.DateTime>
    member __.minDate = _minDate_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.DateTime>>
    member __.maxDate = _maxDate_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<System.DateTime>>
    member __.label = _label_ :> FSharp.Data.Adaptive.aval<Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>>
[<AutoOpen; System.Diagnostics.CodeAnalysis.SuppressMessage("NameConventions", "*")>]
module CalendarLenses = 
    type Calendar with
        static member date_ = ((fun (self : Calendar) -> self.date), (fun (value : System.DateTime) (self : Calendar) -> { self with date = value }))
        static member minDate_ = ((fun (self : Calendar) -> self.minDate), (fun (value : Microsoft.FSharp.Core.option<System.DateTime>) (self : Calendar) -> { self with minDate = value }))
        static member maxDate_ = ((fun (self : Calendar) -> self.maxDate), (fun (value : Microsoft.FSharp.Core.option<System.DateTime>) (self : Calendar) -> { self with maxDate = value }))
        static member label_ = ((fun (self : Calendar) -> self.label), (fun (value : Microsoft.FSharp.Core.option<Microsoft.FSharp.Core.string>) (self : Calendar) -> { self with label = value }))

