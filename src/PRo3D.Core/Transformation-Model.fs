namespace PRo3D.Core.Surface

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.SceneGraph.IO.Loader
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Trafos
open Aardvark.VRVis
open Aardvark.VRVis.Opc

open DevILSharp.ILU
open Chiron

open PRo3D.Base
open PRo3D.Core

open Adaptify


[<ModelType>]
type Transformations = { 
    version               : int
    useTranslationArrows  : bool
    translation           : V3dInput
    yaw                   : NumericInput
    pitch                 : NumericInput
    roll                  : NumericInput
    trafo                 : Trafo3d
    pivot                 : V3dInput 
    oldPivot              : V3d
    showPivot             : bool
    pivotChanged          : bool
    flipZ                 : bool
    isSketchFab           : bool
    scaling               : NumericInput
    trafoChanged          : bool
    firstChangeAfterNewPivot : bool
} 


module Transformations =
    module Initial =
        let scaling = {
            value  = 1.000
            min    = 0.001
            max    = 50.000
            step   = 0.001
            format = "{0:0.000}"
        }

        let yaw = {
            value   = 0.000
            min     = -180.000
            max     = +180.000
            step    = 0.001
            format  = "{0:0.000}"
        }

        let pitch = {
            value   = 0.000
            min     = -180.000
            max     = +180.000
            step    = 0.001
            format  = "{0:0.000}"
        }

        let roll = {
            value   = 0.000
            min     = -180.000
            max     = +180.000
            step    = 0.001
            format  = "{0:0.000}"
        }

        let initNum = {
            value   = 0.000
            min     = -10000000.000
            max     = +10000000.000
            step    = 0.001
            format  = "{0:0.000}"
        }

        let setV3d (v : V3d) = {
            x = { initNum with value = v.X }
            y = { initNum with value = v.Y }
            z = { initNum with value = v.Z }
            value = v    
        }

    let current = 5 //4 //21.12.2022 laura
    
    let read0 = 
        json {            
            let! useTranslationArrows = Json.read "useTranslationArrows"
            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext> "translation"
            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"
            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
            let! pivot                = Json.read "pivot"
            let pivot'                = pivot |> V3d.Parse
            
            
            return {
                version              = current
                useTranslationArrows = useTranslationArrows
                translation          = translation
                yaw                  = yaw
                pitch                = Initial.pitch
                roll                 = Initial.roll
                trafo                = trafo               
                pivot                = Initial.setV3d( pivot')
                oldPivot             = pivot'
                showPivot            = true
                pivotChanged         = false
                flipZ                = false
                isSketchFab          = false
                scaling              = Initial.scaling
                trafoChanged         = false
                firstChangeAfterNewPivot = false
            }
        }

    let read1 = 
        json {            
            let! useTranslationArrows = Json.read "useTranslationArrows"
            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext>     "translation"
            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"          
            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
            let! pivot                = Json.read "pivot"
            let pivot'                = pivot |> V3d.Parse
            let! flipZ                = Json.read "flipZ"
            
            return {
                version              = current
                useTranslationArrows = useTranslationArrows
                translation          = translation
                yaw                  = yaw
                pitch                = Initial.pitch
                roll                 = Initial.roll
                trafo                = trafo
                pivot                = Initial.setV3d(pivot')
                oldPivot             = pivot'
                showPivot            = true
                pivotChanged         = false
                flipZ                = flipZ
                isSketchFab          = false
                scaling              = Initial.scaling
                trafoChanged         = false
                firstChangeAfterNewPivot = false
            }
        }

    let read2 = 
        json {            
            let! useTranslationArrows = Json.read "useTranslationArrows"
            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext>     "translation"
            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"          
            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
            let! pivot                = Json.read "pivot"
            let pivot'                = pivot |> V3d.Parse
            let! flipZ                = Json.read "flipZ"
            let! isSketchFab          = Json.read "isSketchFab"
            
            return {
                version              = current
                useTranslationArrows = useTranslationArrows
                translation          = translation
                yaw                  = yaw                 
                pitch                = Initial.pitch
                roll                 = Initial.roll
                trafo                = trafo               
                pivot                = Initial.setV3d( pivot' )
                oldPivot             = pivot'
                showPivot            = true
                pivotChanged         = false
                flipZ                = flipZ
                isSketchFab          = isSketchFab
                scaling              = Initial.scaling
                trafoChanged         = false
                firstChangeAfterNewPivot = false
            }
        }


    let read3 = 
        json {            
            let! useTranslationArrows = Json.read "useTranslationArrows"
            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext> "translation"
            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"
            let! pitch                = Json.readWith Ext.fromJson<NumericInput,Ext> "pitch"
            let! roll                 = Json.readWith Ext.fromJson<NumericInput,Ext> "roll"
            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
            let! pivot                = Json.read "pivot"
            let pivot'                = pivot |> V3d.Parse
            let! flipZ                = Json.read "flipZ"
            let! isSketchFab          = Json.read "isSketchFab"
            
            return {
                version              = current
                useTranslationArrows = useTranslationArrows
                translation          = translation
                yaw                  = yaw
                pitch                = pitch
                roll                 = roll
                trafo                = trafo
                pivot                = Initial.setV3d( pivot')
                oldPivot             = pivot'
                showPivot            = true
                pivotChanged         = false
                flipZ                = flipZ
                isSketchFab          = isSketchFab
                scaling              = Initial.scaling
                trafoChanged         = false
                firstChangeAfterNewPivot = false
            }
        }


    let read4 = 
        json {            
            let! useTranslationArrows = Json.read "useTranslationArrows"
            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext> "translation"
            //let! scaling              = Json.readWith Ext.fromJson<NumericInput,Ext> "scaling"
            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"
            let! pitch                = Json.readWith Ext.fromJson<NumericInput,Ext> "pitch"
            let! roll                 = Json.readWith Ext.fromJson<NumericInput,Ext> "roll"
            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
            let! pivot                = Json.readWith Ext.fromJson<V3dInput,Ext> "pivot"
            let! showPivot            = Json.read "showPivot"
            let! flipZ                = Json.read "flipZ"
            let! isSketchFab          = Json.read "isSketchFab"
            
            return {
                version              = current
                useTranslationArrows = useTranslationArrows
                translation          = translation
                yaw                  = yaw
                pitch                = pitch
                roll                 = roll
                trafo                = trafo
                pivot                = pivot
                oldPivot             = pivot.value
                showPivot            = showPivot
                pivotChanged         = false
                flipZ                = flipZ
                isSketchFab          = isSketchFab
                scaling              = Initial.scaling //scaling
                trafoChanged         = false
                firstChangeAfterNewPivot = false
            }
        }

    let read5 = 
        json {            
            let! useTranslationArrows = Json.read "useTranslationArrows"
            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext> "translation"
            let! scaling              = Json.readWith Ext.fromJson<NumericInput,Ext> "scaling"
            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"
            let! pitch                = Json.readWith Ext.fromJson<NumericInput,Ext> "pitch"
            let! roll                 = Json.readWith Ext.fromJson<NumericInput,Ext> "roll"
            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
            let! pivot                = Json.readWith Ext.fromJson<V3dInput,Ext> "pivot"
            let! showPivot            = Json.read "showPivot"
            let! flipZ                = Json.read "flipZ"
            let! isSketchFab          = Json.read "isSketchFab"
            
            return {
                version              = current
                useTranslationArrows = useTranslationArrows
                translation          = translation
                yaw                  = yaw
                pitch                = pitch
                roll                 = roll
                trafo                = trafo
                pivot                = pivot
                oldPivot             = pivot.value
                showPivot            = showPivot
                pivotChanged         = false
                flipZ                = flipZ
                isSketchFab          = isSketchFab
                scaling              = scaling
                trafoChanged         = false
                firstChangeAfterNewPivot = false
            }
        }

type Transformations with
    

    static member FromJson(_ : Transformations) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Transformations.read0
            | 1 -> return! Transformations.read1
            | 2 -> return! Transformations.read2
            | 3 -> return! Transformations.read3
            | 4 -> return! Transformations.read4
            | 5 -> return! Transformations.read5
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of Transformations"
                |> Json.error
        }

    static member ToJson( x : Transformations) =
        json {            
            do! Json.write "version" x.version
            do! Json.write "useTranslationArrows" x.useTranslationArrows
            do! Json.writeWith Ext.toJson<V3dInput,Ext> "translation" x.translation
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "scaling" x.scaling
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "yaw"   x.yaw
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "pitch" x.pitch
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "roll"  x.roll
            do! Json.writeWith Ext.toJson<Trafo3d,Ext> "trafo" x.trafo
            do! Json.writeWith Ext.toJson<V3dInput,Ext> "pivot" x.pivot
            do! Json.write "showPivot" x.showPivot
            do! Json.write "flipZ" x.flipZ
            do! Json.write "isSketchFab" x.isSketchFab
        }


     


//module Init =
//    open MBrace.FsPickler
//    open MBrace.FsPickler.Combinators    
//    open Aardvark.Geometry

    
//    let scaling = {
//        value  = 1.00
//        min    = 0.01
//        max    = 50.00
//        step   = 0.01
//        format = "{0:0.00}"
//    }

//    let translationInput = {
//        value   = 0.0
//        min     = -10000000.0
//        max     = 10000000.0
//        step    = 0.01
//        format  = "{0:0.00}"
//    }
    
//    let initTranslation (v : V3d) = {
//        x     = { translationInput with value = v.X }
//        y     = { translationInput with value = v.Y }
//        z     = { translationInput with value = v.Z }
//        value = v    
//    }
//    let transformations = {
//        version              = Transformations.current
//        useTranslationArrows = false
//        translation          = initTranslation (V3d.OOO)
//        trafo                = Trafo3d.Identity
//        yaw                  = Transformations.Initial.yaw
//        pitch                = Transformations.Initial.pitch
//        roll                 = Transformations.Initial.roll
//        pivot                = initTranslation (V3d.OOO)
//        oldPivot             = V3d.OOO
//        showPivot            = false
//        pivotChanged         = false
//        flipZ                = false
//        isSketchFab          = false
//    }
    
