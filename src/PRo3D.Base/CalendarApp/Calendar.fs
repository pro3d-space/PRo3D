namespace PRo3D.Base

open System
open Adaptify
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Operators
open Aardvark.UI.Generic
open PRo3D.Base

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Calendar =
    [<ReferenceEquality; NoComparison>]
    type Thing<'a> = { value : 'a }
    let inline thing a = { value = a }
    
    let private requirements =  
        [//  Fomantic-UI 2.9.3 - Calendar
            { kind = Stylesheet; name = "calendar.css"; url = "./resources/calendar.css" }
            { kind = Script; name = "calendar.js"; url = "./resources/calendar.js" }
            { kind = Script; name = "calendarUtils.js"; url = "./resources/calendarUtils.js" }
        ]

    type CalendarAction =
        | SetDateString of string
        | SetDate of DateTime
        | SetDateJs of list<string>

    type CalendarType = 
        | Time
        | Date
        | DateTime
    
    module CalendarType =
        let string calendarType = 
            match calendarType with
            | Time     -> "time"
            | Date     -> "date"
            | DateTime -> "datetime"

    type IsoDateTime = {
        time : string
    }

    let parseDate (dateString : string) =
        let sucess, date = DateTime.TryParse dateString
        if sucess then
            date
        else
            let dateString = String.trimc '"' dateString
            let sucess, date = DateTime.TryParse dateString
            if sucess then
                date
            else
                Log.error "[Calendar] Could not parse date %s" dateString
                DateTime.Now
        
    let update (m : Calendar) (msg : CalendarAction) =
        Log.warn "[Calendar] %s" (string msg)
        match msg with
        | SetDateString str ->
            let result = DateTime.TryParse str
            match result with
            | true, date ->
                Log.line "setting date %s" str
                {m with date = date}
            | _ ->
                Log.error "[Calendar] Could not parse date %s" str
                m
        | SetDate date ->
            {m with date = date}
        | SetDateJs lst ->
            match lst with
            | [] ->
                Log.warn "[Calendar] received empty list from js."
                m
            | date::tail ->
                let date = parseDate date
                {m with date = date}

    let view (m : AdaptiveCalendar) (centre : bool) (disabled : bool) 
             (calendarType : CalendarType) =
        let bootCode = sprintf "createCalendar('__ID__', '%s')" 
                        (CalendarType.string calendarType);
                                
        let attributes =
            seq {
                yield clazz "ui inverted calendar"
                yield onEvent "ondatechange" [] (fun x -> SetDateJs x)
                if centre then
                    yield style "display: flex;align-items: center;;justify-content: center"
            } |> Seq.toList

        let divClass =
            if disabled then
                "ui inverted disabled input"
            else 
                "ui inverted input"


        //JS-Standard: YYYY-MM-DDTHH:mm:ss.sss
        let toJsString (d : DateTime) =
            let str = sprintf "%04i-%02i-%02iT%02i:%02i:%02i" 
                              d.Year d.Month d.Day 
                              d.Hour d.Minute d.Second
            {time = str}

        let updateCode = "setCalendarDate('__ID__', data);"

        require requirements (
           div [] [
                div [] [    
                    div attributes [
                        div [clazz divClass; 
                            ] [      
                            input [attribute "placeholder" "Select date"] 
                        ]
                        
                    ] 
                ] |> GuiEx.DataChannel.addDataChannel bootCode updateCode None (m.date |> AVal.map toJsString)
           ] 
        )
        
    let init =
        {
            date = DateTime.Now
            minDate = None
            maxDate = None
            label   = None
        }

    let fromDate date =
        {
            date = date
            minDate = None
            maxDate = None
            label   = None
        }

    let withMinMax date minDate maxDate =
        {
            date = date
            minDate = Some minDate
            maxDate = Some maxDate
            label   = None
        }

    let withLabel date minDate maxDate label =
        {
            date = date
            minDate = Some minDate
            maxDate = Some maxDate
            label   = label
        }