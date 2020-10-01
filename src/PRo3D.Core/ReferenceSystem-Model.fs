namespace PRo3D.Core

open FSharp.Data.Adaptive
open Adaptify
open Aardvark.Base
open Aardvark.UI
open PRo3D
open PRo3D.Base
open Chiron

open Aether
open Aether.Operators

#nowarn "0686"

[<ModelType>]
type ReferenceSystem = {
    version       : int
    origin        : V3d
    north         : V3dInput
    noffset       : NumericInput
    northO        : V3d
    up            : V3dInput
    isVisible     : bool
    size          : NumericInput
    scaleChart    : IndexList<string>
    selectedScale : string
    planet        : PRo3D.Base.Planet
}

type ReferenceSystemConfig<'a> = {
    arrowLength    : Lens<'a,double>
    arrowThickness : Lens<'a,double>
    nearPlane      : Lens<'a,double>
}

type ReferenceSystemAction =
    | InferCoordSystem   of V3d
    | UpdateUpNorth      of V3d
    | SetUp              of Vector3d.Action
    | SetNorth           of Vector3d.Action
    | SetNOffset         of Numeric.Action
    | ToggleVisible
    | SetScale           of string
    | SetArrowSize       of double
    | SetPlanet          of Planet

type InnerConfig<'a> =
    {
        arrowLength     : Lens<'a,float>
        arrowThickness  : Lens<'a,float>            
    } 

type MInnerConfig<'ma> =
    {
        getArrowLength    : 'ma -> aval<float>
        getArrowThickness : 'ma -> aval<float>
        getNearDistance   : 'ma -> aval<float>
    }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ReferenceSystem =    
    
    let current = 0   
    let read0 =
        json {
            let! origin        = Json.read "origin"
            let! north         = Json.readWith Ext.fromJson<V3dInput,Ext> "north"
            let! noffset       = Json.readWith Ext.fromJson<NumericInput,Ext> "noffset"
            let! northO        = Json.read "northO"
            let! up            = Json.readWith Ext.fromJson<V3dInput,Ext> "up"
            let! isVisible     = Json.read "isVisible"
            let! size          = Json.readWith Ext.fromJson<NumericInput,Ext> "size"
            let! scaleChart    = Json.read "scaleChart"
            let! selectedScale = Json.read "selectedScale"
            let! planet        = Json.read "planet"

            return 
                {
                    version       = current
                    origin        = origin |> V3d.Parse
                    north         = north        
                    noffset       = noffset      
                    northO        = northO |> V3d.Parse
                    up            = up
                    isVisible     = isVisible
                    size          = size
                    scaleChart    = scaleChart |> IndexList.ofList
                    selectedScale = selectedScale
                    planet        = planet |> enum<Planet>
                }
        }

    let initNum = {
        value   = 0.0
        min     = -1.0
        max     = +1.0
        step    = 0.001
        format  = "{0:0.000}"
    }

    let setV3d (v : V3d) = {
        x = { initNum with value = v.X }
        y = { initNum with value = v.Y }
        z = { initNum with value = v.Z }
        value = v    
    }

    let initArrowSize = {
        value   = 1.0
        min     = 0.1
        max     = 10.0
        step    = 0.01
        format  = "{0:0.0}"
    }

    let initNum2 = {
        value   = 1.0
        min     = 0.0
        max     = 1000.0
        step    = 1.0
        format  = "{0:0.000}"
    }

    let initNoffset = {
        value   = float 0.0
        min     = float 0.0
        max     = float 360.0
        step    = float 1.0
        format  = "{0:0.0}"
    }

    let initial = {
        version       = current
        origin        = V3d.Zero
        north         = setV3d V3d.IOO
        noffset       = initNoffset
        northO        = V3d.IOO
        up            = setV3d V3d.OOI
        isVisible     = true              
        size          = initNum2
        scaleChart = ["100km"; "10km"; "1km";"100m";"10m";"2m";"1m";"10cm";"1cm";"1mm";"0.1mm"] |> IndexList.ofList
        selectedScale = "2m"
        planet = Planet.Mars
    }

    //open ViewConfigModelLenses

    
    //let initialConfig : ReferenceSystemConfig<ViewConfigModel> = {
    //    arrowLength    = ViewConfigModel.arrowLength_    >-> NumericInput.value_
    //    arrowThickness = ViewConfigModel.arrowThickness_ >-> NumericInput.value_
    //    nearPlane      = ViewConfigModel.nearPlane_ >-> NumericInput.value_
    //}

type ReferenceSystem with
    static member FromJson(_ : ReferenceSystem) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! ReferenceSystem.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of ReferenceSystem"
                |> Json.error
        }
    static member ToJson(x : ReferenceSystem) =
        json {
            do! Json.write "version" x.version

            do! Json.write "origin" (x.origin.ToString())
            do! Json.writeWith Ext.toJson<V3dInput,Ext> "north" x.north 
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "noffset" x.noffset
            do! Json.write "northO" (x.northO.ToString())
            do! Json.writeWith Ext.toJson<V3dInput,Ext> "up" x.up
            do! Json.write "isVisible" x.isVisible    
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "size" x.size
            do! Json.write "scaleChart" (x.scaleChart |> IndexList.toList)
            do! Json.write "selectedScale" x.selectedScale
            do! Json.write "planet" (x.planet |> int)
        }