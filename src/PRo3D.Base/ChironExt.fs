namespace PRo3D.Base

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives

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

    let parseOption (x : Json<Option<'a>>) (f : 'a -> 'b) = 
        x |> Json.map (fun x -> x |> Option.map (fun y -> f y))
    let writeOption (name : string) (x : option<'a>) =
      match x with
      | Some a ->
          Json.write name (a.ToString ())
      | None ->
          Json.writeNone name
    let writeOptionList (name : string) 
                     (x : option<List<'a>>) 
                     (f : List<'a> -> string -> Json<unit>) = //when 'a : (static member ToJson : () -> () )>>) =
      match x with
      | Some a ->
          f a name
      | None ->
          Json.writeNone name
    let writeOptionFloat (name : string) (x : option<float>) =
      match x with
      | Some a ->
          Json.write name a
      | None ->
          Json.writeNone name

    let writeOptionInt (name : string) (x : option<int>) =
      match x with
      | Some a ->
          Json.write name a
      | None ->
          Json.writeNone name

    let writeOptionBool (name : string) (x : option<bool>) =
      match x with
      | Some a ->
          Json.write name a
      | None ->
          Json.writeNone name

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
    
    let inline toJson (x: ^a)  =
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

    //Trafo3d option
    static member FromJson1 (ext : Ext, _ : Option<Trafo3d>) = 
        json {
            let! t  = Json.read "trafo"
            match t with 
            | Some trafo -> return (Some(trafo |> Trafo3d.Parse))
            | None -> return None
                      
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
    
    // Euclidean3d
    static member FromJson1 (ext : Ext, _ : Euclidean3d) = 
        json {
            let! t  = Json.read "euclidean3d"
            return (t |> Euclidean3d.Parse)            
        }

    static member ToJson1 (ext : Ext, v : Euclidean3d) = 
        json {
            do! Json.write "euclidean3d"  (v.ToString())
        }


    // Affine3d
    static member FromJson1 (ext : Ext, _ : Affine3d) = 
        json {
            let! t  = Json.read "affine3d"
            return (t |> Affine3d.Parse)            
        }

    static member ToJson1 (ext : Ext, v : Affine3d) = 
        json {
            do! Json.write "affine3d"  (v.ToString())
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


    // Frustum
    static member ToJson1 (ext : Ext, x : Frustum) =
        json {
            do! Json.write "left"      x.left   
            do! Json.write "right"     x.right   
            do! Json.write "bottom"    x.bottom
            do! Json.write "top"       x.top     
            do! Json.write "near"      x.near   
            do! Json.write "far"       x.far     
            do! Json.write "isOrtho"   x.isOrtho     
        }

    static member FromJson1(_: Ext, _ : Frustum) = 
        json {
            let! left       = Json.read "left"   
            let! right      = Json.read "right"  
            let! bottom     = Json.read "bottom" 
            let! top        = Json.read "top"    
            let! near       = Json.read "near"   
            let! far        = Json.read "far"    
            let! isOrtho    = Json.read "isOrtho"

            return {
                left       = left    
                right      = right   
                bottom     = bottom  
                top        = top     
                near       = near    
                far        = far     
                isOrtho    = isOrtho 
            }
        }