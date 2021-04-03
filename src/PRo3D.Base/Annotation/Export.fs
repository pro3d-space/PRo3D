namespace PRo3D.Base.Annotation


open System
open System.IO
open Microsoft.FSharp.Reflection
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI

open PRo3D.Base.Annotation.GeoJSON

module Export =
    
    type ExportDnS = {
        dipAngle      : float
        dipAzimuth    : float
        strikeAzimuth : float

        errorAvg      : float
        errorMin      : float
        errorMax      : float
        errorStd      : float
        errorSos      : float
    }
   
    type ExportAnnotation = {
        key           : Guid
        geometry      : PRo3D.Base.Annotation.Geometry
        projection    : Projection
        semantic      : Semantic
        color         : string
        thickness     : float        

        points        : int
        height        : float
        heightDelta   : float
        length        : float
        wayLength     : float        
        dipAngle      : float
        dipAzimuth    : float
        strikeAzimuth : float

        manualDip     : float
        trueThickness : float

        errorAvg      : float
        errorMin      : float
        errorMax      : float
        errorStd      : float
        sumOfSquares  : float

        text          : string
        groupName     : string
        surfaceName   : string

        x             : double
        y             : double
        z             : double
    }

    let toExportAnnotation (lookUp) (a: Annotation) : ExportAnnotation =
      
        let results = 
            match a.results with
            | Some r -> r
            | None ->  
                { 
                    version       = AnnotationResults.current
                    height        = Double.NaN
                    heightDelta   = Double.NaN
                    avgAltitude   = Double.NaN
                    length        = Double.NaN
                    wayLength     = Double.NaN
                    bearing       = Double.NaN
                    slope         = Double.NaN
                    trueThickness = Double.NaN
                }
        
        let dnsResults = 
            match a.dnsResults with
            | Some x -> 
                { 
                    dipAngle      = x.dipAngle
                    dipAzimuth    = x.dipAzimuth
                    strikeAzimuth = x.strikeAzimuth
                    errorAvg      = x.error.average
                    errorMin      = x.error.min
                    errorMax      = x.error.max
                    errorStd      = x.error.stdev
                    errorSos      = x.error.sumOfSquares
                }
            | None -> 
                { 
                    dipAngle      = Double.NaN
                    dipAzimuth    = Double.NaN
                    strikeAzimuth = Double.NaN
                    errorAvg      = Double.NaN
                    errorMin      = Double.NaN
                    errorMax      = Double.NaN
                    errorStd      = Double.NaN
                    errorSos      = Double.NaN
                }
        
        let points = 
            a.points
            //|> IndexList.map a.modelTrafo.Forward.TransformPos 
            |> IndexList.toArray
        
        let c = Box3d(points).Center
        
        {   
            key           = a.key
            geometry      = a.geometry
            projection    = a.projection
            semantic      = a.semantic
            color         = a.color.ToString()
            thickness     = a.thickness.value
            points        = a.points.Count
            
            
            height        = results.height
            heightDelta   = results.heightDelta
            length        = results.length
            wayLength     = results.wayLength
            dipAngle      = dnsResults.dipAngle
            dipAzimuth    = dnsResults.dipAzimuth
            strikeAzimuth = dnsResults.strikeAzimuth
            
            errorAvg     = dnsResults.errorAvg
            errorMin     = dnsResults.errorMin 
            errorMax     = dnsResults.errorMax
            errorStd     = dnsResults.errorStd
            sumOfSquares = dnsResults.errorSos

            manualDip     = a.manualDipAngle.value
            trueThickness = results.trueThickness
            
            text          = a.text;
            groupName     = lookUp |> HashMap.tryFind a.key |> Option.defaultValue("")
            surfaceName   = a.surfaceName
            
            x             = c.X;
            y             = c.Y;
            z             = c.Z;
        }

    //TODO TO revise csv exporter, make baselib maybe

    // Code from http://www.fssnip.net/3U/title/CSV-writer ----------------------------------------------------------------
    type Array =
        static member join delimiter xs = 
            xs 
            |> Array.map (fun x -> x.ToString())
            |> String.concat delimiter

    type Seq =
        static member write (path:string) (data:seq<'a>)  = 
            use writer = new StreamWriter(path)
            data
            |> Seq.iter writer.WriteLine

        static member csv (separator:string) (useEnclosure:bool) (headerMapping:string -> string) ( data:seq<'a>) =
            seq {
                let dataType = typeof<'a>
                let stringSeqDataType = typeof<System.Collections.Generic.IEnumerable<string>>
                let inline enclose s =
                    match useEnclosure with
                    | true -> "\"" + (string s) + "\""
                    | false -> string s

                let header = 
                    match dataType with
                    | ty when FSharpType.IsRecord ty ->
                        FSharpType.GetRecordFields dataType
                        |> Array.map (fun info -> headerMapping info.Name)                    
                    | ty when FSharpType.IsTuple ty -> 
                        FSharpType.GetTupleElements dataType
                        |> Array.mapi (fun idx info -> headerMapping(string idx) )
                    | ty when ty.IsAssignableFrom stringSeqDataType ->
                        data :?> seq<seq<string>> |> Seq.head
                        |> Seq.toArray
                    | _ -> dataType.GetProperties()
                        |> Array.map (fun info -> headerMapping info.Name)

                yield header |> Array.map enclose |> Array.join separator
                                    
                let lines =
                    match dataType with 
                    | ty when FSharpType.IsRecord ty -> 
                        data |> Seq.map FSharpValue.GetRecordFields
                    | ty when FSharpType.IsTuple ty ->
                        data |> Seq.map FSharpValue.GetTupleFields
                    | ty when ty.IsAssignableFrom stringSeqDataType ->
                        data :?> seq<seq<string>> |> Seq.tail
                        |> Seq.map (fun ss -> Seq.toArray ss |> Array.map (fun s -> s :> obj) )
                    | _ -> 
                        let props = dataType.GetProperties()
                        data 
                        |> Seq.map ( fun line -> 
                            props 
                            |> Array.map (fun prop ->
                                prop.GetValue(line, null) 
                            )
                        )
                    |> Seq.map (Array.map enclose)
                yield! lines |> Seq.map (Array.join separator)
            }

    module GeoJSON =
        let toPolygon () =
            let polygon = 
                GeoJsonGeometry.Polygon(
                    [
                        [
                            Coordinate.V2d{ X=100.0; Y=0.0 }
                            Coordinate.V2d{ X=101.0; Y=0.0 }
                            Coordinate.V2d{ X=101.0; Y=1.0 }
                            Coordinate.V2d{ X=100.0; Y=1.0 }
                            Coordinate.V2d{ X=100.0; Y=0.0 }
                        ]                        
                        [
                            Coordinate.V2d{ X=100.8; Y=0.8 }
                            Coordinate.V2d{ X=100.8; Y=0.2 }
                            Coordinate.V2d{ X=100.2; Y=0.2 }
                            Coordinate.V2d{ X=100.2; Y=0.8 }
                            Coordinate.V2d{ X=100.8; Y=0.8 }
                        ]
                    ]
                )

            ()

        let doJson (path:string) (annotations : list<ExportAnnotation>) = failwith ""

