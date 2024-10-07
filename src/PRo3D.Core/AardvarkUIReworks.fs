namespace Aardvark.UI

module NoSemUi =

    open System
    open Aardvark.Base
    open Aardvark.UI
    open Aardvark.UI.Primitives    
    open FSharp.Data.Adaptive
    open Aardvark.UI.Generic
    open Aardvark.UI.Primitives.SimplePrimitives


    [<AutoOpen>]
    module private Helpers =

        let inline pickle (v : 'a) = System.Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)

        let unpickle (v : string) =
            try
                match NumericConfigDefaults<'a>.NumType with
                | Aardvark.UI.Primitives.SimplePrimitives.NumberType.Int ->
                    match System.Double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
                    | (true, v) -> 
                        let txt = Aardvark.Base.Text(Math.Round(v).ToString())
                        let rv = Aardvark.Base.Text<'a>.Parse.Invoke(txt)
                        Some rv
                    | _ -> None
                | _ ->
                    let value = Aardvark.Base.Text<'a>.Parse.Invoke(Aardvark.Base.Text(v))
                    Some value
            with
            | _ ->
                Log.warn "[UI.Primitives] failed to parse user input"
                None

    open Aardvark.UI.Primitives

    [<ReferenceEquality; NoComparison>]
    type private Thing<'a> = { value : 'a }

    let inline private thing a = { value = a }

    let private semui =
        [
            { kind = Stylesheet; name = "semui"; url = "./resources/semantic.css" }
            { kind = Stylesheet; name = "semui-overrides"; url = "./resources/semantic-overrides.css" }
            { kind = Script; name = "semui"; url = "./resources/semantic.js" }
            { kind = Script; name = "essential"; url = "./resources/essentialstuff.js" }
        ]


    let numeric (cfg : NumericConfig<'a>) (inputType : string) (atts : AttributeMap<'msg>) (value : aval<'a>) (update : 'a -> 'msg) =

        let value = if value.IsConstant then AVal.custom (fun t -> value.GetValue t) else value

        let update (v : 'a) =
            value.MarkOutdated()
            update v

        let myAtts =
            AttributeMap.ofList [
                //"class", AttributeValue.String "ui input"
                onEvent' "data-event" [] (function (str :: _) -> str |> unpickle |> Option.toList |> Seq.map update | _ -> Seq.empty)
            ]

        let boot =
            String.concat ";" [
                "var $__ID__ = $('#__ID__');"
                "$__ID__.numeric({ changed: function(v) { aardvark.processEvent('__ID__', 'data-event', v); } });"
                "valueCh.onmessage = function(v) { $__ID__.numeric('set', v.value); };"
            ]

        let pattern =
            match NumericConfigDefaults<'a>.NumType with
            | NumberType.Int -> "[0-9]+"
            | _ -> "[0-9]*(\.[0-9]*)?"

        require semui (
            onBoot' ["valueCh", AVal.channel (AVal.map thing value)] boot (
                Incremental.div (AttributeMap.union atts myAtts) (
                    alist {
                        yield
                            input (att [

                                attribute "value" (value.GetValue() |> pickle)
                                attribute "type" inputType
                                attribute "min" (pickle cfg.min)
                                attribute "max" (pickle cfg.max)
                                attribute "step" (pickle cfg.smallStep)
                                attribute "data-largestep" (pickle cfg.largeStep)
                                //attribute "data-numtype" (NumericConfigDefaults<'a>.NumType.ToString())
                                attribute "pattern" pattern
                            ])
                    }
                )
            )
        )
        