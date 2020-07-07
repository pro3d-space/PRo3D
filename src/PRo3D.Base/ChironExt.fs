namespace PRo3D.Base

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI

open PRo3D
open Chiron

#nowarn "0686"

type Ext = Ext
 
module Json = 
  let writeFloat name (floatValue : double)  = 
    json {
      if floatValue.IsNaN() then      
        do! Json.writeNone name
      else
        do! Json.write name floatValue
    }

  let readFloat name : Json<double> = 
    json {
      let! v = Json.tryRead name
      return 
        match v with
        | Some k -> k
        | None -> Double.NaN      
    }

  let parsePlane3d (s:string) =        

    let x = s.NestedBracketSplitLevelOne() |> Seq.toArray
    let n = x.[0] |> V3d.Parse
    let p = float(x.[1]) //|> Double.Parse
    Plane3d(n, p)

module Ext =
    let inline fromJsonDefaults (a: ^a, _: ^b) =
        ((^a or ^b or ^e) : (static member FromJson1: ^e * ^a -> ^a Json)(Unchecked.defaultof<_>,a))
    
    let inline fromJson x =
        fst (fromJsonDefaults (Unchecked.defaultof<'a>, FromJsonDefaults) x)
    
    let inline toJsonDefaults (a: ^a, _: ^b) =
        ((^a or ^b or ^e) : (static member ToJson1: ^e * ^a -> unit Json)(Unchecked.defaultof<_>,a))
    
    let inline toJson (x: 'a) =
        snd (toJsonDefaults (x, ToJsonDefaults) (Object (Map.empty)))

type Ext with
    //NumericInput
    static member FromJson1 (ext : Ext, _ : NumericInput) = 
        json {
            let! value  = Json.readFloat "value"
            let! min    = Json.readFloat "min"
            let! max    = Json.readFloat "max"
            let! step   = Json.readFloat "step"
            let! format = Json.read "format"
    
            return {
                value   = value
                min     = min
                max     = max
                step    = step
                format  = format
            }
        }
    static member ToJson1 (ext : Ext, v : NumericInput) = 
        json {
            do! Json.writeFloat "value"  v.value
            do! Json.writeFloat "min"    v.min
            do! Json.writeFloat "max"    v.max
            do! Json.writeFloat "step"   v.step
            do! Json.write "format" v.format
        }
    
    //ColorInput
    static member FromJson1 (ext : Ext, _ : ColorInput) = 
        json {
            let! c  = Json.read "color"                  
            return {
                c = c |> C4b.Parse
            }
        }
    static member ToJson1 (ext : Ext, v : ColorInput) = 
        json {
            do! Json.write "color"  (v.c.ToString())
        }
    
    //Trafo3d
    static member FromJson1 (ext : Ext, _ : Trafo3d) = 
        json {
            let! t  = Json.read "trafo"
            return (t |> Trafo3d.Parse)            
        }

    static member ToJson1 (ext : Ext, v : Trafo3d) = 
        json {
            do! Json.write "trafo"  (v.ToString())
        }
    
    //VectorInput
    static member FromJson1 (ext : Ext, _ : V3dInput) = 
        json {
            let! x  = Json.readWith Ext.fromJson<NumericInput,Ext> "x"
            let! y  = Json.readWith Ext.fromJson<NumericInput,Ext> "y"
            let! z  = Json.readWith Ext.fromJson<NumericInput,Ext> "z"
            let! value  = Json.read "value"
    
            return {
                 x = x
                 y = y
                 z = z
                 value = value |> V3d.Parse
            }
        }
    static member ToJson1 (ext : Ext, v : V3dInput) = 
        json {             
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "x" v.x
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "y" v.y
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "z" v.z
    
            do! Json.write "value" (v.ToString())
        }
    
    //CameraView
    static member FromJson1 (_ : Ext, _ : CameraView) = 
        json {
            let! (cameraView : list<string>) = Json.read "view"
            let cameraView = cameraView |> List.map V3d.Parse
            return CameraView(
                cameraView.[0],
                cameraView.[1],
                cameraView.[2],
                cameraView.[3], 
                cameraView.[4]
            )
        }
    
    static member ToJson1 (_ : Ext, x : CameraView) =
        json {             
            let camView = [
                x.Sky
                x.Location
                x.Forward 
                x.Up
                x.Right
            ]
            
            do! Json.write "view" (camView |> List.map(fun x -> x.ToString()))
        }

    //V3d
    static member FromJson1 (_ : Ext, _ : list<V3d>) = 
        json {
            let! (points : list<string>) = Json.read "points"
            return points |> List.map V3d.Parse
        }
    
    static member ToJson1 (_ : Ext, x : list<V3d>) =
        json {                                     
            do! Json.write "points" (x |> List.map(fun x -> x.ToString()))
        }
      
       
    //int
    static member FromJson1 (ext : Ext, v : int) = 
        json {       
            return 1
        }
