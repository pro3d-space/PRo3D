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
                {m with date = parseDate date}

    let view (m : AdaptiveCalendar) (centre : bool) (disabled : bool) 
             (calendarType : CalendarType) =
        let bootCode = 
            String.concat ";" [
                yield // could extend to deal with different date formats
                    sprintf "$('#__ID__').calendar({
                                type:'%s',
                                className: {table: 'ui inverted celled center aligned unstackable table'},
                                onChange: function(newDate){
                                                  console.log(newDate);
                                                  var oldDate = $(this).calendar('get date');
                                                  if(!oldDate || oldDate != newDate) {
                                                    aardvark.processEvent('__ID__', 'onDateChange', newDate.toLocaleDateString());
                                                  }
                                          },
                                formatter: {
                                    cellTime: 'H:mm',
                                    date: 'YYYY-MM-DD',
                                    datetime: 'YYYY-MM-DD, H:mm',
                                    dayHeader: 'MMMM YYYY',
                                    hourHeader: 'MMMM D, YYYY',
                                    minuteHeader: 'MMMM D, YYYY',
                                    month: 'MMMM YYYY',
                                    monthHeader: 'YYYY',
                                    time: 'H:mm',
                                    year: 'YYYY'
                                }
                            });" (CalendarType.string calendarType)
            ] 
                                
        let attributes =
            seq {
                yield clazz "ui inverted calendar"
                yield onEvent "onDateChange" [] (fun x -> SetDateJs x)
                if centre then
                    yield style "display: flex;align-items: center;;justify-content: center"
            } |> Seq.toList

        let divClass =
            if disabled then
                "ui inverted disabled input"
            else 
                "ui inverted input"
        
        require requirements (
            div [] [
                onBoot bootCode (
                    div attributes [
                        div [clazz divClass; 
                            ] [
                            //i [clazz "inverted calendar icon"] [] // calendar icon missing          
                            input [attribute "placeholder" "Select date"] 
                        ]
                    ]
                )
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
