namespace CorrelationDrawing

open System

module Time =

  let getTimestamp = 
    let now = DateTime.Now
    sprintf "%04i%03i%02i%02i%04i" 
        now.Year 
        now.DayOfYear 
        now.Hour 
        now.Minute 
        now.Millisecond