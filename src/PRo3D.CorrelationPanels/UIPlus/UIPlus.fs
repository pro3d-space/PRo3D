namespace UIPlus

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open CorrelationDrawing
open Chiron

type ArrowButtonId = {
  id        : string 
} with
  member this.isValid = (this.id <> "")

module ArrowButtonId = 
  let invalid = {id = ""}
  let newId () : ArrowButtonId  = 
    let id = System.Guid.NewGuid ()
    {id = id.ToString () }

[<ModelType>]
type ArrowButton = {
    [<NonAdaptive>]
    id            : ArrowButtonId
    
    direction     : Direction
    size          : Size
}

[<ModelType>]
type TextInput = {
    version   : int
    text      : string
    disabled  : bool
    bgColor   : C4b  
    size      : option<int>
} 
with 
    static member current = 0
    static member private readV0 =
        json {
            let! text     = Json.read "text"
            let! disabled = Json.read "disabled"
            let! bgColor  = Json.read "bgColor"
            let! size     = Json.read "size"

            return {                
                version  = TextInput.current
                text     = text
                disabled = disabled
                bgColor  = bgColor |> C4b.Parse
                size     = size
            }
        }
    static member FromJson (_:TextInput) =
       json {
           let! v = Json.read "version"
           match v with
           | 0 -> return! TextInput.readV0
           | _ -> return! v |> sprintf "don't know version %d of TextInput" |> Json.error
       }
    static member ToJson (x : TextInput) =
        json {
            do! Json.write "version" x.version             
            do! Json.write "text" x.text
            do! Json.write "disabled" x.disabled
            do! Json.write "bgColor" (x.bgColor.ToString())
            do! Json.write "size" x.size             
        }
        
type GrainType =
    | Boulder 
    | Cobble  
    | VcGravel
    | CGravel 
    | MGravel 
    | FGravel 
    | SandStone
    | VfGravel
    | VcSand  
    | CSand   
    | MSand   
    | FSand   
    | VfSand  
    | Silt    
    | Paleosol    
    | Clay    
    | Colloid 
    | Unknown 
with 
    member this.toString =
        match this with
        | Boulder  -> "boulder"           
        | Cobble   -> "cobble"            
        | VcGravel -> "very coarse gravel"
        | CGravel  -> "coarse gravel"     
        | MGravel  -> "medium gravel"     
        | FGravel  -> "fine gravel"       
        | SandStone  -> "sandstone"
        | VfGravel -> "very fine gravel"  
        | VcSand   -> "very coarse sand"  
        | CSand    -> "coarse sand"       
        | MSand    -> "medium sand"       
        | FSand    -> "fine sand"         
        | VfSand   -> "very fine sand"    
        | Silt     -> "silt"              
        | Paleosol -> "paleosol"
        | Clay     -> "clay"              
        | Colloid  -> "colloid"                 
        | Unknown  -> "unknown"
    static member fromString input =
        match input with
        | "boulder"            -> Boulder 
        | "cobble"             -> Cobble  
        | "very coarse gravel" -> VcGravel
        | "coarse gravel"      -> CGravel 
        | "medium gravel"      -> MGravel 
        | "fine gravel"        -> FGravel 
        | "sandstone"          -> SandStone
        | "very fine gravel"   -> VfGravel
        | "very coarse sand"   -> VcSand  
        | "coarse sand"        -> CSand   
        | "medium sand"        -> MSand   
        | "fine sand"          -> FSand   
        | "very fine sand"     -> VfSand  
        | "silt"               -> Silt    
        | "paleosol"           -> Paleosol
        | "clay"               -> Clay    
        | "colloid"            -> Colloid      
        | "unknown"            -> Unknown 
        | _ -> Unknown

type GrainSizeInfo = {
    grainType    : GrainType
    middleSize   : float
    displayWidth : float
}

[<ModelType>]
type ColourMapItem = {
    [<NonAdaptive>]
    id                  : GrainType
    [<NonAdaptive>]
    order               : int
                       
    upper               : float
    defaultMiddle       : float
    lower               : float
    upperStr            : string
    colour              : ColorInput
    label               : string
}

[<ModelType>]
type ColourMap = {
    mappings     : HashMap<GrainType, ColourMapItem>
    defaultValue : GrainType
    selected     : option<GrainType>
}