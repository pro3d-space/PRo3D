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

module Calculations =

    //let getHeightDelta (p:V3d) (upVec:V3d) = (p * upVec).Length
    
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

    //computes the distance between the first and the last point projected onto the upvector
    let verticalDelta (points:list<V3d>) (up:V3d) = 
        match points.Length with
        | 1 -> 0.0
        | _ -> 
            let a = points |> List.head
            let b = points |> List.last
            let v = (b - a)

            (v |> Vec.dot up.Normalized)

    //computes the distance between the first and the last point in the horizontal plane
    let horizontalDelta (points:list<V3d>) (up:V3d) = 
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
        { AnnotationResults.initial with avgAltitude = CooTransformation.getAltitude model.points.[0] upVec planet }       
    
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
        let bearing = bearing upVec northVec line.Direction.Normalized
        let slope   = pitch upVec line.Direction.Normalized
    
        let height = (heights |> List.max) - (heights |> List.min)

        let trueThickness, verticalThickness =
            match (annotation.geometry, annotation.dnsResults) with
            | (Geometry.TT, Some dns) when (annotation.manualDipAngle.value.IsNaN()) |> not ->
                let p1 = annotation.points.[1]
                let planeHeight = dns.plane.Height(p1)

                let rayUp, rayDown = Ray3d(p1, upVec), Ray3d(p1, upVec)

                let pointOnPlaneUp = rayUp.Intersect(dns.plane)
                let pointOnPlaneDown = rayDown.Intersect(dns.plane)
                let verticalDistance = (min (Vec.distance p1 pointOnPlaneUp) (Vec.distance p1 pointOnPlaneDown)) * (planeHeight.Sign() |> float)

                planeHeight, verticalDistance
            | _ -> Double.NaN, Double.NaN

        {   
            version           = AnnotationResults.current
            height            = height
            heightDelta       = Fun.Abs (heights.[hcount-1] - heights.[0])
            avgAltitude       = (heights |> List.average)
            length            = dist
            wayLength         = wayLength
            bearing           = bearing
            slope             = slope
            trueThickness     = trueThickness
            verticalThickness = verticalThickness
        }
    
    let calculateAnnotationResults (model:Annotation) (upVec:V3d) (northVec:V3d) (planet:Planet) : AnnotationResults =
        match model.points.Count with
        | x when x > 1 -> calcResultsLine model upVec northVec planet
        | _ -> calcResultsPoint model upVec planet
    
    let reCalcBearing (model:Annotation) (upVec:V3d) (northVec:V3d) = 
        match model.results with 
        | Some r ->
            let count = model.points.Count
            match count with
            | x when x > 1 ->
                let line = new Line3d(model.points.[0], model.points.[count-1])
                let bearing = bearing upVec northVec line.Direction.Normalized
                Some {r with bearing = bearing }
            | _ -> Some r
        | None -> None

module DipAndStrike =   
      
    let projectOntoPlane (x:V3d) (n:V3d) = (x - (x * n)).Normalized
    
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
        
        let planeNormal = dipDirection.Cross(strikeDirection) |> Vec.normalize

        let dippingPlane = Plane3d(planeNormal, p0)        

        //reconstructing dip angle from dot product (must equal manualDipAngle)
        let alpha = -(Math.Asin (Vec.dot up dipDirection)).DegreesFromRadians()        
        
        let dns = {
            version         = DipAndStrikeResults.current
            plane           = dippingPlane
            dipAngle        = alpha
            dipDirection    = dipDirection
            strikeDirection = strikeDirection
            dipAzimuth      = Calculations.computeAzimuth dipDirection north up
            strikeAzimuth   = Calculations.computeAzimuth strikeDirection north up
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
            
            //strike
            let strike = up.Cross(planeNormal).Normalized
    
            //dip vector 
            let dip = strike.Cross(planeNormal).Normalized
    
            //dip plane incline .. maximum dip angle
            let v = strike.Cross(up).Normalized                       
    
            let centerOfMass = V3d.Divide(points |> IndexList.sum, (float)points.Count)

            let dns = {
                version         = DipAndStrikeResults.current
                plane           = plane
                dipAngle        = Math.Acos(v.Dot(dip)).DegreesFromRadians()
                dipDirection    = dip
                strikeDirection = strike
                dipAzimuth      = Calculations.computeAzimuth v north up
                strikeAzimuth   = Calculations.computeAzimuth strike north up
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
    
    let reCalculateDipAndStrikeResults (up : V3d) (north : V3d) (annotation : Annotation) =        
        match (annotation.geometry, annotation.dnsResults) with
        | Geometry.DnS, Some dnsResults ->
            let up = up |> Vec.normalize
            let north = north |> Vec.normalize
        
            let plane = dnsResults.plane

            //correct plane orientation - check if normals point in same direction           
            let planeNormal = 
                match signedOrientation up plane with
                | -1 -> -plane.Normal
                | _  -> plane.Normal
            
            //strike
            let strike = up.Cross(planeNormal).Normalized
    
            //dip vector 
            let dip = strike.Cross(planeNormal).Normalized
    
            //dip plane incline .. maximum dip angle
            let v = strike.Cross(up).Normalized                                       

            let dns = 
                {
                    dnsResults with
                        dipAngle        = Math.Acos(v.Dot(dip)).DegreesFromRadians()
                        dipDirection    = dip
                        strikeDirection = strike
                        dipAzimuth      = Calculations.computeAzimuth v north up
                        strikeAzimuth   = Calculations.computeAzimuth strike north up
                }
            
            Some dns 
        | Geometry.TT, Some _ ->
            calculateManualDipAndStrikeResults up north annotation
        | _ ->
            None
            
    let reCalculateDnSAzimuth (anno:Annotation) (up:V3d) (north : V3d) =
        
        let points = anno.points |> IndexList.filter(fun x -> not x.IsNaN)
        match anno.dnsResults with
        | Some dns ->
            match points.Count with 
            | x when x > 2 ->       
                let plane = dns.plane
                
                //correct plane orientation - check if normals point in same direction
                let height = Plane3d(up, V3d.Zero).Height(plane.Normal)
                let planeNormal =
                    match height.Sign() with
                    | -1 -> -plane.Normal
                    | _  -> plane.Normal                
        
                //strike
                let strike = up.Cross(planeNormal).Normalized
        
                //dip plane incline .. maximum dip angle
                let v = strike.Cross(up).Normalized
        
                { 
                    dns with
                        dipAzimuth = Calculations.computeAzimuth v north up; 
                        strikeAzimuth = Calculations.computeAzimuth strike north up 
                } |> Some
                
            | _ -> None //TODO TO check if this shouldnt be none
        | _-> None

    let viewUI (model : AdaptiveAnnotation) =

        let results       = AVal.map AdaptiveOption.toOption model.dnsResults
        let dipAngle      = AVal.bindOption results Double.NaN (fun a -> a.dipAngle)
        let dipAzimuth    = AVal.bindOption results Double.NaN (fun a -> a.dipAzimuth)
        let strikeAzimuth = AVal.bindOption results Double.NaN (fun a -> a.strikeAzimuth)
        
        require GuiEx.semui (
            Html.table [ 
                Html.row "Dipping Angle:"       [Incremental.text (dipAngle      |> AVal.map  (fun d -> sprintf "%.2f deg" (d)))]
                Html.row "Dipping Orientation:" [Incremental.text (dipAzimuth    |> AVal.map  (fun d -> sprintf "%.2f deg" (d)))]
                Html.row "Strike Orientation:"  [Incremental.text (strikeAzimuth |> AVal.map  (fun d -> sprintf "%.2f deg" (d)))]
            ]

        )



