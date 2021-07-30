namespace PRo3D.Base.Annotation

open System
open System.IO
open Microsoft.FSharp.Reflection
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI

open Chiron
open PRo3D.Base
open Aardvark.Geometry

module AttitudeExport =
    let toJson 
        (dns     : DipAndStrikeResults) 
        (name    : option<string>) 
        (uid     : option<string>) 
        (up      : V3d) 
        (points  : option<seq<V3d>>)
        (regInfo : RegressionInfo3d) =

        let center = regInfo.Center

        let inline n v =
            Json.Number (decimal v)

        let x1 = regInfo.Normal |> Vec.cross up
        let rake = x1 |> Vec.dot regInfo.Axis1 |> acos

        //let dip = (regInfo.Normal.Z / regInfo.Normal.Length) |> acos
        //let strike = ((regInfo.Normal.X / regInfo.Normal.Y) - (Math.PI / 2.0)) |> atan
        //Log.line "dip export reg %A std %A" (dip |> degrees) dns.dipAngle
        //Log.line "strike export reg %A std %A" (strike |> degrees) dns.strikeAzimuth

        //let strike = regInfo.Normal.X / regInfo.Normal.Y

        Json.Object (
            Map.ofList [
                match uid with
                | Some uid -> "uid", Json.String uid
                | _ -> ()
                match name with
                | Some name -> "name", Json.String name
                | _ -> ()

                "axes", Json.Array [
                    Json.Array [ n regInfo.Axis0.X;  n regInfo.Axis0.Y;  n regInfo.Axis0.Z ]
                    Json.Array [ n regInfo.Axis1.X;  n regInfo.Axis1.Y;  n regInfo.Axis1.Z ]
                    Json.Array [ n regInfo.Normal.X; n regInfo.Normal.Y; n regInfo.Normal.Z ]
                ]

                "hyperbolic_axes", Json.Array [ 
                    n regInfo.HyperbolicAxes.X; n regInfo.HyperbolicAxes.Y; n regInfo.HyperbolicAxes.Z 
                ]
                "max_angular_error", n (Constant.DegreesPerRadian * regInfo.AngularErrors.Y)
                "min_angular_error", n (Constant.DegreesPerRadian * regInfo.AngularErrors.X)
                "center", Json.Array [ n center.X; n center.Y; n center.Z]

                "strike", n dns.strikeAzimuth
                "dip", n dns.dipAngle
                "rake", n rake

                "disabled", Json.Bool false

                match points with
                | Some pts ->
                    "centered_array", Json.Array [
                        for p in pts do
                            let c = p - center
                            Json.Array [ n c.X; n c.Y; n c.Z ]
                    ]
                | None -> ()

            ]
        )   

    let tryToJson (dns : option<DipAndStrikeResults>) (name : option<string>) (uid : string) (up : V3d) (points : seq<V3d>) =
        match dns with
        | Some d -> 
            match d.regressionInfo with 
            | Some r -> 
                (toJson d (name) (Some uid) up (Some (points)) r) |> Some
            | None -> 
                let linRegression = points |> Seq.toArray |> LinearRegression3d.create 

                Log.warn "[Attitude] annotation %A does not have regression info, recomputing" uid
                let d' = { d with regressionInfo = linRegression }
                match d'.regressionInfo with 
                | Some r1 -> 
                    (toJson d' (name) (Some uid) up (Some (points)) r1) |> Some                
                | None ->
                    Log.warn "[Attitude] recomputation failed %A" uid
                    None
        | None -> 
            Log.warn "[Attitude] annotation %A does not have dns results" uid
            None

    let writeAttitudeJson (path:string) (up : V3d) (annotations : list<Annotation>) : unit = 

        if path.IsEmpty() then ()
        
        let attitudePlanes =
            annotations
            |> List.choose (fun x -> 
                tryToJson x.dnsResults (Some x.text) (x.key.ToString()) up (x.points |> IndexList.toSeq)
            )
            //|> List.map(fun x ->
             
            //    match x.dnsResults with
            //    | Some dns ->
                    
            //        toJson dns (Some "blurg") (Some (x.key.ToString())) (Some (x.points |> IndexList.toSeq)) dns.regressionInfo
            //    | None -> 
            //        failwith "[Attitude.Export] impossible"
            //)            
        
        attitudePlanes
        |> Json.serialize 
        |> Json.formatWith JsonFormattingOptions.Pretty 
        |> Serialization.writeToFile path
        
        ()