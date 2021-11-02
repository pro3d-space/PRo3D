namespace PRo3D.Core

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Rendering    

open Aardvark.Application    
open Aardvark.UI

open PRo3D.Base
open PRo3D.Base.Annotation

open Aether
open Aether.Operators
open Aardvark.UI.Primitives
open PRo3D

module ReferenceSystemApp =

    //open Aardvark.UI.ChoiceModule
   
    

    let updateVectorInDegree (up:V3d) (point:V3d) (origin:V3d) (theta:float) =
        //https://sites.google.com/site/glennmurray/Home/rotation-matrices-and-formulas
        
        let xyPlane = new Plane3d(up.Normalized, origin) 

        let u = xyPlane.Normal.X //up.X
        let v = xyPlane.Normal.Y //up.Y
        let w = xyPlane.Normal.Z //up.Z
        let a = xyPlane.Point.X //pos.X
        let b = xyPlane.Point.Y //pos.Y
        let c = xyPlane.Point.Z //pos.Z
        let x = point.X
        let y = point.Y
        let z = point.Z
        
        let t = theta.RadiansFromDegrees()

        let u2 = u*u
        let v2 = v*v
        let w2 = w*w
        let cosT = Math.Cos(t)
        let oneMinusCosT = 1.0-cosT
        let sinT = Math.Sin(t)
        
        // Build the matrix entries element by element.
        let m11 = u2 + (v2 + w2) * cosT
        let m12 = u*v * oneMinusCosT - w*sinT
        let m13 = u*w * oneMinusCosT + v*sinT
        let m14 = (a*(v2 + w2) - u*(b*v + c*w))*oneMinusCosT
                + (b*w - c*v)*sinT

        let m21 = u*v * oneMinusCosT + w*sinT
        let m22 = v2 + (u2 + w2) * cosT
        let m23 = v*w * oneMinusCosT - u*sinT
        let m24 = (b*(u2 + w2) - v*(a*u + c*w))*oneMinusCosT
                + (c*u - a*w)*sinT

        let m31 = u*w * oneMinusCosT - v*sinT
        let m32 = v*w * oneMinusCosT + u*sinT
        let m33 = w2 + (u2 + v2) * cosT
        let m34 = (c*(u2 + v2) - w*(a*u + b*v))*oneMinusCosT
                + (a*v - b*u)*sinT
                
        let px = m11*x + m12*y + m13*z + m14
        let py = m21*x + m22*y + m23*z + m24
        let pz = m31*x + m32*y + m33*z + m34

        V3d(px, py, pz)
    
   
    let upVector (point:V3d) (planet) = 
        CooTransformation.getUpVector point planet //point.Normalized
    
    let northVector (up:V3d) =
        let east = V3d.OOI.Cross(up)
        up.Cross(east)

    let inferCoordinateSystem (p : V3d) = //TODO rno
        // earth radius min max [6,357; 6,378]
        // mars equatorial radius [3396] 
        let earthLower = 5500000.0
        let earthUpper = 7000000.0
        let marsLower  = 2500000.0
        let marsUpper  = 4000000.0

        let distanceToOrigin = p.Length
        let coordinateSystem = 
            match distanceToOrigin with
            | d when d > marsLower && d < marsUpper -> Planet.Mars
            | d when d > earthLower && d < earthUpper -> Planet.Earth
            | _ -> Planet.None

        Log.warn "[ReferenceSystem] Inferred Coordinate System: %s" (coordinateSystem.ToString ())
        coordinateSystem

    let updateCoordSystem (p:V3d) (planet:Planet) (model : ReferenceSystem) = 
        let up = upVector p planet
        let n  = 
            match planet with 
            | Planet.None | Planet.JPL -> V3d.IOO 
            | _ -> northVector up

        let no = Rot3d.Rotation(up, model.noffset.value |> Double.radiansFromDegrees).Transform(n) //updateVectorInDegree up n model.origin model.noffset.value 
        { model with north = ReferenceSystem.setV3d n
                     up = ReferenceSystem.setV3d up
                     northO = no }

    let update<'a> 
        (bigConfig : 'a) 
        (config : ReferenceSystemConfig<'a>) 
        (model : ReferenceSystem) 
        (act : ReferenceSystemAction) =
        match act with
        | InferCoordSystem p ->
            let planet = inferCoordinateSystem p
            let m = updateCoordSystem p planet model
            {m with origin = p 
                    planet = planet}, bigConfig
        | UpdateUpNorth p ->
            updateCoordSystem p model.planet model, bigConfig
        | SetUp up ->    
            let up = Vector3d.update model.up up
            let up = ReferenceSystem.setV3d up.value.Normalized
            { model with up =  up}, bigConfig
        | SetNorth  n     -> 
            let n = Vector3d.update model.north n
            let n = ReferenceSystem.setV3d n.value//.Normalized
            
            { model with north = n }, bigConfig
        | SetNOffset o ->
            let noffset = Numeric.update model.noffset o
            let no = 
              Rot3d.Rotation(model.up.value, noffset.value |> Double.radiansFromDegrees)
                .Transform(model.north.value) |> Vec.normalize                

            { model with noffset = noffset; northO = no }, bigConfig 
        | ToggleVisible   -> 
            { model with isVisible = not model.isVisible}, bigConfig
        | SetArrowSize d  ->
            let big' = Optic.set config.arrowLength d bigConfig
            model, big'          
        | SetScale s ->
            { model with selectedScale = s }, bigConfig
        | SetPlanet p ->      
            let m' = updateCoordSystem model.origin p model
            { m' with planet = p }, bigConfig

    
    module UI =

        let view (model:AdaptiveReferenceSystem)  =
            
            let sphericalCoo = 
                adaptive {
                    let! pos = model.origin
                    let! planet = model.planet
                    //let! up = model.up.value
                    return CooTransformation.getLatLonAlt planet pos
                }

            require GuiEx.semui (
                Html.table [                                                
                    Html.row "Pos:"         [Incremental.text (model.origin     |> AVal.map (fun x -> x.ToString("0.00")))]
                    Html.row "Up:"          [Vector3d.view model.up |> UI.map SetUp ]
                    Html.row "North:"       [Incremental.text (model.north.value |> AVal.map (fun x -> x.ToString("0.00")))]
                    Html.row "NorthO:"      [Incremental.text (model.northO |> AVal.map (fun x -> x.ToString("0.00")))]
                    Html.row "N-Offset:"    [Numeric.view' [InputBox] model.noffset |> UI.map SetNOffset ] 
                    Html.row "Longitude:"   [Incremental.text (sphericalCoo |> AVal.map (fun x -> x.longitude.ToString("0.00")))]
                    Html.row "Latitude:"    [Incremental.text (sphericalCoo |> AVal.map (fun x -> x.latitude.ToString("0.00")))]
                    Html.row "Altitude:"    [Incremental.text (sphericalCoo |> AVal.map (fun x -> x.altitude.ToString("0.00")))]
                    Html.row "Visible:"     [GuiEx.iconCheckBox model.isVisible ToggleVisible ]
                      
                ]
            )


        //TODO ??? where is the sg view