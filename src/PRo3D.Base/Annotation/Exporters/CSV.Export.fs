namespace PRo3D.Base.Annotation

open System
open System.IO
open Microsoft.FSharp.Reflection
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI

open PRo3D.Base

module CSVExport =
    
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
        key                : Guid
        geometry           : PRo3D.Base.Annotation.Geometry
        projection         : Projection
        semantic           : Semantic
        color              : string
        thickness          : float        

        points             : int
        height             : float
        heightDelta        : float
        length             : float
        wayLength          : float        
        dipAngle           : float
        dipAzimuth         : float
        strikeAzimuth      : float

        manualDip          : float
        trueThickness      : float
        bearing            : float
        slope              : float
        verticalDistance   : float
        horizontalDistance : float

        errorAvg           : float
        errorMin           : float
        errorMax           : float
        errorStd           : float
        sumOfSquares       : float

        text               : string
        groupName          : string
        surfaceName        : string

        x                  : double
        y                  : double
        z                  : double
    }

    let toExportAnnotation (lookUp) upVector (a: Annotation) : ExportAnnotation =
      
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
        
        let verticalDistance = 
            Calculations.verticalDistance (points |> Array.toList) upVector

        let horizontalDistance = 
            Calculations.horizontalDistance (points |> Array.toList) upVector

        let c = Box3d(points).Center
        
        {   
            //non-measurement
            key           = a.key
            geometry      = a.geometry
            projection    = a.projection
            semantic      = a.semantic
            color         = a.color.ToString()
            thickness     = a.thickness.value
            points        = a.points.Count
            manualDip     = a.manualDipAngle.value
            text          = a.text;
            groupName     = lookUp |> HashMap.tryFind a.key |> Option.defaultValue("")
            surfaceName   = a.surfaceName
                        
            //distances and orientations
            height        = results.height //
            heightDelta   = results.heightDelta //
            length        = results.length //
            wayLength     = results.wayLength //
            trueThickness = results.trueThickness //
            bearing       = results.bearing //
            slope         = results.slope //

            horizontalDistance = horizontalDistance //
            verticalDistance   = verticalDistance //
            
            //dns
            dipAngle      = dnsResults.dipAngle //
            dipAzimuth    = dnsResults.dipAzimuth //
            strikeAzimuth = dnsResults.strikeAzimuth //
            
            //error measures
            errorAvg     = dnsResults.errorAvg
            errorMin     = dnsResults.errorMin 
            errorMax     = dnsResults.errorMax
            errorStd     = dnsResults.errorStd
            sumOfSquares = dnsResults.errorSos
            
            //position
            x             = c.X;
            y             = c.Y;
            z             = c.Z;
        }

    let writeCSV 
        lookUp 
        upVector
        (path : string) 
        (annotations : list<Annotation>) =

        let csvTable = 
            annotations 
            |> List.map (toExportAnnotation lookUp upVector)
            |> CSV.Seq.csv "," true id

        if path.IsEmptyOrNull() |> not then 
            csvTable |> CSV.Seq.write path

    

