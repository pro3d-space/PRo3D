namespace PRo3D.Base.Annotation

open System
open Aardvark.Base
open Aardvark.UI
open OpcViewer.Base
open PRo3D.Base

open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open Aardvark.Geometry

//TODO refactor: AnnotationHelpers.fs is a very unspecific file name ... divide functions better into modules

module DipAndStrike =   
      
    let projectOntoPlane (x:V3d) (n:V3d) = (x - (x * n)).Normalized
    
    let computeAzimuth (dir:V3d) (north:V3d) (up:V3d) =
        let east = north.Cross(up)
        let eastWestPlane = new Plane3d(east, 0.0) //separates west (-) from east  (+)
        let northSouthPlane = new Plane3d(north, 0.0) //separates sout (-) from north  (+)
    
        let coords = new V2d(eastWestPlane.Height(dir), northSouthPlane.Height(dir))
        let angle = Fun.Atan(coords.Y / coords.X).DegreesFromRadians().Abs()
    
        if coords.X.IsNaN() then
            Double.NaN
        else
            match coords.X.Sign() with
            | -1 -> 
                match coords.Y.Sign() with
                | 1 -> 270.0 + angle
                | _ -> 270.0 - angle 
            | _ -> 
                match coords.Y.Sign() with
                | -1 -> angle + 90.0
                | _  -> 90.0 - angle
    
    let bearing (up:V3d) (north : V3d) (dir:V3d) =
        computeAzimuth dir north up
    
    let pitch (up:V3d) (dir:V3d) =
        let ground = new Plane3d(up.Normalized, 0.0)
        Math.Asin(ground.Height(dir)).DegreesFromRadians()
    
    let computeStandardDeviation avg (input : List<float>) =
    
        let sosq = 
            input 
            |> List.map(fun (x:double) ->
                let k = (x.Abs() - avg)
                Math.Pow(k,2.0))
            |> List.sum
        
        Math.Sqrt (sosq / float (input.Length - 1))          
    
    let calculateDnsErrors (points:IndexList<V3d>) =
        let points = points |> IndexList.filter(fun x -> not x.IsNaN)
    
        if points.Count < 3 then []
        else
            let v3dArray = points.AsList |> List.toArray
            let mutable plane = PlaneFitting.planeFit(v3dArray)
                    
            let distances = 
                points 
                |> IndexList.toList
                |> List.map(fun x -> (plane.Height x))
    
            distances
      
    let signedOrientation up (plane : Plane3d) =
         let horP = new Plane3d(up, V3d.Zero)                
         horP.Height(plane.Normal).Sign()
    
    let calculateManualDipAndStrikeResults (up : V3d) (north : V3d) (annotation : Annotation) =
        Log.startTimed "[Annotation] computing manual dns"
        
        let up = up |> Vec.normalize
        let north = north |> Vec.normalize
        
        //apply dip azimuth rotation
        let dipDirection = 
            north 
            |> Trafo3d.Rotation(up, ((-annotation.manualDipAzimuth.value).RadiansFromDegrees())).Forward.TransformDir
            |> Vec.normalize
        
        //strike
        let strikeDirection = dipDirection.Cross(up) |> Vec.normalize

        //apply dip angle rotation along negative strike direction because dipping angles can only dip down
        let dipDirection = 
            dipDirection
            |> Trafo3d.Rotation(-strikeDirection, ((annotation.manualDipAngle.value).RadiansFromDegrees())).Forward.TransformDir
            |> Vec.normalize
    
        //dip plane incline .. maximum dip angle
        let v = strikeDirection.Cross(up) |> Vec.normalize

        let p0 = annotation.points.[0]
        let p1 = annotation.points.[1]
        let planeNormal = dipDirection.Cross(strikeDirection) |> Vec.normalize

        let dippingPlane = Plane3d(planeNormal, p0)

        Log.line "north %A" north
        Log.line "dip   %A" dipDirection
        Log.line "v     %A" v
        Log.line "true  %A" (dippingPlane.Height(p1))

        let alpha = -(Math.Asin (Vec.dot up dipDirection)).DegreesFromRadians()
        Log.line "alpha     %A" alpha
        
        let dns = {
            version         = DipAndStrikeResults.current
            plane           = dippingPlane
            dipAngle        = alpha
            dipDirection    = dipDirection
            strikeDirection = strikeDirection
            dipAzimuth      = computeAzimuth dipDirection north up
            strikeAzimuth   = computeAzimuth strikeDirection north up
            centerOfMass    = p0
            error           = 
                { 
                    version      = Statistics.current
                    average      = nan
                    min          = nan
                    max          = nan
                    stdev        = nan
                    sumOfSquares = nan
                }
            regressionInfo = None
        }

        Log.stop()

        Some dns

    let calculateDipAndStrikeResults (up:V3d) (north : V3d) (points:IndexList<V3d>) =
    
        let points = points |> IndexList.filter(fun x -> not x.IsNaN)
    
        match points.Count with 
        | x when x > 2 ->
    
            let v3dArray = points.AsList |> List.toArray // points.toArray not possible because of: int -> V3d[]
    
            //let p = v3dArray.[0]
           // let up = p.Normalized        
    
            let linRegression = (new LinearRegression3d(v3dArray)).TryGetRegressionInfo()

            Log.line "[AnnotationHelpers.fs] %A" linRegression

            let plane = 
                match linRegression with
                | Some lr -> lr.Plane
                | None ->
                    Log.line "[dns computation] linear regression failed, fallback to evd"
                    PlaneFitting.planeFit(v3dArray)
    
            let distances = 
                points 
                |> IndexList.toList
                |> List.map(fun x -> (plane.Height x).Abs())
    
            let sos = distances |> List.map(fun x -> x * x) |> List.sum /// (float distances.Length)
            
            let avg = distances |> List.average
            let max = distances |> List.max
            let min = distances |> List.min
         
            let std = distances |> computeStandardDeviation avg
    
            Log.line("[dipandStrike]: avg %f; max %f; min %f; std: %f; sols: %f") avg max min std sos
            
            //correct plane orientation - check if normals point in same direction           
            let planeNormal = 
                match signedOrientation up plane with
                | -1 -> -plane.Normal
                | _  -> plane.Normal
    
            //let plane = Plane3d(planeNormal, plane.Distance)
            
            //strike
            let strike = up.Cross(planeNormal).Normalized
    
            //dip vector 
            let dip = strike.Cross(planeNormal).Normalized
    
            //dip plane incline .. maximum dip angle
            let v = strike.Cross(up).Normalized
    
            //let distances2 = 
            //    points
            //    |> IndexList.toList
            //    |> List.map(fun x -> (plane.Height x).Abs())
    
            //Log.line "%A" distances2
    
            let centerOfMass = V3d.Divide(points |> IndexList.sum, (float)points.Count)

            let dns = {
                version         = DipAndStrikeResults.current
                plane           = plane
                dipAngle        = Math.Acos(v.Dot(dip)).DegreesFromRadians()
                dipDirection    = dip
                strikeDirection = strike
                dipAzimuth      = computeAzimuth v north up
                strikeAzimuth   = computeAzimuth strike north up
                centerOfMass    = centerOfMass //(new Box3d(points)).Center //[@LF] this is not the center of mass (sum over points / no of points)
                error           = 
                    { 
                        version      = Statistics.current
                        average      = avg
                        min          = min
                        max          = max
                        stdev        = std
                        sumOfSquares = sos
                    }
                regressionInfo = linRegression
            }
            Some dns        
        | _ -> 
            None
        
    let recalculateDnSAzimuth (anno:Annotation) (up:V3d) (north : V3d) =
    
        let points = anno.points |> IndexList.filter(fun x -> not x.IsNaN)
        match anno.dnsResults with
        | Some dns ->
            match points.Count with 
            | x when x > 2 ->       
                let mutable plane = 
                    points.AsArray |> PlaneFitting.planeFit
                
                //correct plane orientation - check if normals point in same direction
                let height = Plane3d(up, V3d.Zero).Height(plane.Normal)
                plane.Normal <-
                    match height.Sign() with
                    | -1 -> -plane.Normal
                    | _  -> plane.Normal                
    
                //strike
                let strike = up.Cross(plane.Normal).Normalized
    
                //dip plane incline .. maximum dip angle
                let v = strike.Cross(up).Normalized
    
                { 
                  dns with
                    dipAzimuth = computeAzimuth v north up; 
                    strikeAzimuth = computeAzimuth strike north up 
                } |> Some
                
            | _ -> None //TODO TO check if this shouldnt be none
        | _-> None

    let viewUI (model : AdaptiveAnnotation) =

        let results = AVal.map AdaptiveOption.toOption model.dnsResults
        let da   = AVal.bindOption results Double.NaN (fun a -> a.dipAngle)
        let daz  = AVal.bindOption results Double.NaN (fun a -> a.dipAzimuth)
        let staz = AVal.bindOption results Double.NaN (fun a -> a.strikeAzimuth)
        
        require GuiEx.semui (
            Html.table [ 
                Html.row "Dipping Angle:"       [Incremental.text (da   |> AVal.map  (fun d -> sprintf "%.2f deg" (d)))]
                Html.row "Dipping Orientation:" [Incremental.text (daz  |> AVal.map  (fun d -> sprintf "%.2f deg" (d)))]
                Html.row "Strike Orientation:"  [Incremental.text (staz |> AVal.map  (fun d -> sprintf "%.2f deg" (d)))]
            ]

        )

module Calculations =

    //let getHeightDelta (p:V3d) (upVec:V3d) = (p * upVec).Length
    
    let verticalDistance (points:list<V3d>) (up:V3d) = 
        match points.Length with
        | 1 -> 0.0
        | _ -> 
            let a = points |> List.head
            let b = points |> List.last
            let v = (b - a)

            (v |> Vec.dot up.Normalized)

    let horizontalDistance (points:list<V3d>) (up:V3d) = 
        match points.Length with
        | 1 -> 0.0
        | _ -> 
            let a = points |> List.head
            let b = points |> List.last
            let v = (a - b)
            let vertical = (v |> Vec.dot up.Normalized)

            (v.LengthSquared - (vertical |> Fun.Square)) |> Fun.Sqrt

    let getHeightDelta2 (p:V3d) (upVec:V3d) (planet:Planet) = 
        CooTransformation.getHeight p upVec planet
    
    let calcResultsPoint (model:Annotation) (upVec:V3d) (planet:Planet) : AnnotationResults =            
        { 
            version       = AnnotationResults.current
            height        = Double.NaN
            heightDelta   = Double.NaN
            avgAltitude   = CooTransformation.getAltitude model.points.[0] upVec planet
            length        = Double.NaN
            wayLength     = Double.NaN
            bearing       = Double.NaN
            slope         = Double.NaN
            trueThickness = Double.NaN
        }
    
    let getDistance (points:list<V3d>) = 
        points
        |> List.pairwise
        |> List.map (fun (a,b) -> Vec.Distance(a,b))
        |> List.sum

    let getSegmentDistance (s:Segment) = 
        getDistance
            [
                yield s.startPoint
                for p in s.points do
                    yield p
                yield s.endPoint 
            ] 
    
    let computeWayLength (segments:IndexList<Segment>) = 
        [ 
            for s in segments do
                yield getSegmentDistance s
        ] 
        |> List.sum
                                                               
    let calcResultsLine (annotation : Annotation) (upVec:V3d) (northVec:V3d) (planet:Planet) : AnnotationResults =
        let count = annotation.points.Count
        let dist = Vec.Distance(annotation.points.[0], annotation.points.[count-1])
        let wayLength =
            if not annotation.segments.IsEmpty then
                computeWayLength annotation.segments
            else
                annotation.points 
                |> IndexList.toList 
                |> getDistance
    
        let heights = 
            annotation.points 
            // |> IndexList.map(fun x -> model.modelTrafo.Forward.TransformPos(x))
            |> IndexList.map(fun p -> getHeightDelta2 p upVec planet ) 
            |> IndexList.toList
    
        let hcount = heights.Length
    
        let line    = new Line3d(annotation.points.[0], annotation.points.[count-1])
        let bearing = DipAndStrike.bearing upVec northVec line.Direction.Normalized
        let slope   = DipAndStrike.pitch upVec line.Direction.Normalized
    
        let verticalThickness = (heights |> List.max) - (heights |> List.min)

        let trueThickness =
            match (annotation.geometry, annotation.dnsResults) with
            | (Geometry.TT, Some dns) when (annotation.manualDipAngle.value.IsNaN()) |> not ->
                let p1 = annotation.points.[1]
                (dns.plane.Height(p1))
            | _ -> Double.NaN

        {   
            version       = AnnotationResults.current
            height        = verticalThickness
            heightDelta   = Fun.Abs (heights.[hcount-1] - heights.[0])
            avgAltitude   = (heights |> List.average)
            length        = dist
            wayLength     = wayLength
            bearing       = bearing
            slope         = slope
            trueThickness = trueThickness
        }
    
    let calculateAnnotationResults (model:Annotation) (upVec:V3d) (northVec:V3d) (planet:Planet) : AnnotationResults =
        match model.points.Count with
        | x when x > 1 -> calcResultsLine model upVec northVec planet
        | _ -> calcResultsPoint model upVec planet
    
    let recalcBearing (model:Annotation) (upVec:V3d) (northVec:V3d) = 
        match model.results with 
        | Some r ->
            let count = model.points.Count
            match count with
            | x when x > 1 ->
                let line = new Line3d(model.points.[0], model.points.[count-1])
                let bearing = DipAndStrike.bearing upVec northVec line.Direction.Normalized
                Some {r with bearing = bearing }
            | _ -> Some r
        | None -> None

