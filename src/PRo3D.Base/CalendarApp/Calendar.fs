namespace PRo3D.Base

open System
open System.Globalization
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

    /// Observation times are UTC (SPICE epochs are), and the model holds them as
    /// DateTimeKind.Utc. That matters beyond display: DateTime equality and subtraction
    /// compare ticks and ignore the kind, so a local 16:00 and a UTC 14:00 -- the same
    /// instant in UTC+2 -- compare unequal. Local converts; Unspecified is read as UTC.
    let toUtc (d : DateTime) : DateTime =
        match d.Kind with
        | DateTimeKind.Utc -> d
        | DateTimeKind.Local -> d.ToUniversalTime()
        | _ -> DateTime.SpecifyKind(d, DateTimeKind.Utc)

    /// Parse an observation time: a string without a zone is UTC, and the result is
    /// DateTimeKind.Utc (plain AssumeUniversal would convert it to local time).
    let tryParseUtc (s : string) : Option<DateTime> =
        match DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal) with
        | (true, date) -> Some date
        | _ -> None

    let parseDate (dateString : string) : System.DateTime =
        match tryParseUtc dateString with
        | Some date ->
            date
        | None ->
            let dateString = dateString.Trim '"'
            match tryParseUtc dateString with
            | Some date ->
                date
            | None ->
                Log.error "[Calendar] Could not parse date %s" dateString
                DateTime.UtcNow

    let update (m : Calendar) (msg : CalendarAction) =
        Log.warn "[Calendar] %s" (string msg)
        match msg with
        | SetDateString str ->
            match tryParseUtc str with
            | Some date ->
                Log.line "setting date %s" str
                {m with date = date}
            | None ->
                Log.error "[Calendar] Could not parse date %s" str
                m
        | SetDate date ->
            {m with date = toUtc date}
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


        //JS-Standard: YYYY-MM-DDTHH:mm:ss.sssZ
        let toJsString (d : DateTime) =
            let d = d.ToUniversalTime()
            let str = sprintf "%04i-%02i-%02iT%02i:%02i:%02iZ" 
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
            date = DateTime.UtcNow
            minDate = None
            maxDate = None
            label   = None
        }

    let fromDate date =
        {
            date = toUtc date
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

    let withDate date  m =
        {m with date = date}