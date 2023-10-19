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

open Chiron

open PRo3D.Base
open PRo3D.Core

open Adaptify

#nowarn "0686"

[<ModelType>]
type ScalarLayer = {
    version      : int
    label        : string
    actualRange  : Range1d
    definedRange : Range1d
    index        : int
    colorLegend  : FalseColorsModel
}
module ScalarLayer =
    let current = 0  

    let read0 =
        json {
            let! label        = Json.read "label"
            let! actualRange  = Json.read "actualRange" 
            let! definedRange = Json.read "definedRange"
            let! index        = Json.read "index"       
            let! colorLegend  = Json.read "colorLegend"
            
            return
                {
                    version      = current
                    label        = label
                    actualRange  = actualRange  |> Range1d.Parse
                    definedRange = definedRange |> Range1d.Parse
                    index        = index
                    colorLegend  = colorLegend 
                }
        }

type ScalarLayer with 
    static member FromJson(_ : ScalarLayer) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ScalarLayer.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of ScalarLayer"
                |> Json.error
        }
    static member ToJson (x : ScalarLayer) =
        json {
            do! Json.write "version"        x.version
            do! Json.write "label"          x.label
            do! Json.write "actualRange"    (x.actualRange.ToString())
            do! Json.write "definedRange"   (x.definedRange.ToString())
            do! Json.write "index"          x.index
            do! Json.write "colorLegend"    x.colorLegend
        }

[<ModelType>]
type ColorCorrection = {
    version     : int
    contrast    : NumericInput
    useContrast : bool
    brightness  : NumericInput
    useBrightn  : bool
    gamma       : NumericInput
    useGamma    : bool
    color       : ColorInput
    useColor    : bool
    useGrayscale: bool
}

module ColorCorrection =
    let current = 0
    let read0 =
        json {            
            let! contrast     = Json.readWith Ext.fromJson<NumericInput,Ext> "contrast"    
            let! useContrast  = Json.read "useContrast" 
            let! brightness   = Json.readWith Ext.fromJson<NumericInput,Ext> "brightness"  
            let! useBrightn   = Json.read "useBrightn"  
            let! gamma        = Json.readWith Ext.fromJson<NumericInput,Ext> "gamma"       
            let! useGamma     = Json.read "useGamma"    
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"       
            let! useColor     = Json.read "useColor"
            let! useGrayscale = Json.read "useGrayscale"
            
            return {
                version      = current
                contrast     = contrast    
                useContrast  = useContrast 
                brightness   = brightness  
                useBrightn   = useBrightn  
                gamma        = gamma       
                useGamma     = useGamma    
                color        = color       
                useColor     = useColor    
                useGrayscale = useGrayscale
            }
        }

type ColorCorrection with 
    static member FromJson( _ : ColorCorrection) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ColorCorrection.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of ColorCorrection" 
                |> Json.error        
        }
    static member ToJson (x : ColorCorrection) =
        json {
             do! Json.write "version" x.version

             do! Json.write "version"      x.version      
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "contrast" x.contrast
             do! Json.write "useContrast"  x.useContrast  
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "brightness" x.brightness   
             do! Json.write "useBrightn"   x.useBrightn   
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "gamma"  x.gamma        
             do! Json.write "useGamma"     x.useGamma     
             do! Json.writeWith Ext.toJson<ColorInput,Ext> "color" x.color        
             do! Json.write "useColor"     x.useColor     
             do! Json.write "useGrayscale" x.useGrayscale 
        }

[<ModelType>]
type Radiometry = {
    version       : int
    useRadiometry : bool

    minR          : NumericInput
    maxR          : NumericInput

    minG          : NumericInput
    maxG          : NumericInput

    minB          : NumericInput
    maxB          : NumericInput
}

module Radiometry =
    let current = 0
    let read0 =
        json {  
            let! useRadiometry  = Json.read "useRadiometry" 
            let! minR   = Json.readWith Ext.fromJson<NumericInput,Ext> "minR" 
            let! maxR   = Json.readWith Ext.fromJson<NumericInput,Ext> "maxR"
            let! minG   = Json.readWith Ext.fromJson<NumericInput,Ext> "minG" 
            let! maxG   = Json.readWith Ext.fromJson<NumericInput,Ext> "maxG"
            let! minB   = Json.readWith Ext.fromJson<NumericInput,Ext> "minB" 
            let! maxB   = Json.readWith Ext.fromJson<NumericInput,Ext> "maxB"
            return {
                version      = current    
                useRadiometry  = useRadiometry 
                
                minR = minR         
                maxR = maxR         
                
                minG = minG         
                maxG = maxG        
                
                minB = minB        
                maxB = maxB 
            }
        }

type Radiometry with 
    static member FromJson( _ : Radiometry) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Radiometry.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A of Radiometry" 
                |> Json.error        
        }
    static member ToJson (x : Radiometry) =
        json {
             do! Json.write "version" x.version
             do! Json.write "useRadiometry"  x.useRadiometry  
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "minR" x.minR
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "maxR" x.maxR  
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "minG" x.minG
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "maxG" x.maxG  
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "minB" x.minB
             do! Json.writeWith Ext.toJson<NumericInput,Ext> "maxB" x.maxB  
        }

type TextureLayer = {
    version : int
    label   : string
    index   : int
}

module TextureLayer =
    let current = 0
    let read0 = 
        json {
            let! label  = Json.read "label"
            let! index  = Json.read "index"

            return {
                version = current
                label   = label
                index   = index
            }
        }

type TextureLayer with 
    static member FromJson(_ : TextureLayer) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! TextureLayer.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of TextureLayer" 
                |> Json.error 
        }
    static member ToJson (x : TextureLayer) =
        json {
            do! Json.write "version" x.version
            do! Json.write "label"   x.label
            do! Json.write "index"   x.index
        }

type AttributeLayer = 
    | ScalarLayer  of ScalarLayer
    | TextureLayer of TextureLayer

//TODO refactor: transformations are used in many contexts, shouldn't be intertwined with SurfaceModel
//[<ModelType>]
//type Transformations = { 
//    version               : int
//    useTranslationArrows  : bool
//    translation           : V3dInput
//    yaw                   : NumericInput
//    pitch                 : NumericInput
//    roll                  : NumericInput
//    trafo                 : Trafo3d
//    pivot                 : V3d
//    flipZ                 : bool
//    isSketchFab           : bool
//} 

//module Transformations =
//    module Initial =
//        let yaw = {
//            value   = 0.0
//            min     = -180.0
//            max     = +180.0
//            step    = 0.01
//            format  = "{0:0.00}"
//        }

//        let pitch = {
//            value   = 0.0
//            min     = -180.0
//            max     = +180.0
//            step    = 0.01
//            format  = "{0:0.00}"
//        }

//        let roll = {
//            value   = 0.0
//            min     = -180.0
//            max     = +180.0
//            step    = 0.01
//            format  = "{0:0.00}"
//        }

//    let current = 3
    
//    let read0 = 
//        json {            
//            let! useTranslationArrows = Json.read "useTranslationArrows"
//            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext> "translation"
//            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"
//            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
//            let! pivot                = Json.read "pivot"            
            
//            return {
//                version              = current
//                useTranslationArrows = useTranslationArrows
//                translation          = translation
//                yaw                  = yaw
//                pitch                = Initial.pitch
//                roll                 = Initial.roll
//                trafo                = trafo               
//                pivot                = pivot |> V3d.Parse
//                flipZ                = false
//                isSketchFab          = false
//            }
//        }

//    let read1 = 
//        json {            
//            let! useTranslationArrows = Json.read "useTranslationArrows"
//            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext>     "translation"
//            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"          
//            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
//            let! pivot                = Json.read "pivot"
//            let! flipZ                = Json.read "flipZ"
            
//            return {
//                version              = current
//                useTranslationArrows = useTranslationArrows
//                translation          = translation
//                yaw                  = yaw
//                pitch                = Initial.pitch
//                roll                 = Initial.roll
//                trafo                = trafo
//                pivot                = pivot |> V3d.Parse
//                flipZ                = flipZ
//                isSketchFab          = false
//            }
//        }

//    let read2 = 
//        json {            
//            let! useTranslationArrows = Json.read "useTranslationArrows"
//            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext>     "translation"
//            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"          
//            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
//            let! pivot                = Json.read "pivot"
//            let! flipZ                = Json.read "flipZ"
//            let! isSketchFab          = Json.read "isSketchFab"
            
//            return {
//                version              = current
//                useTranslationArrows = useTranslationArrows
//                translation          = translation
//                yaw                  = yaw                 
//                pitch                = Initial.pitch
//                roll                 = Initial.roll
//                trafo                = trafo               
//                pivot                = pivot |> V3d.Parse
//                flipZ                = flipZ
//                isSketchFab          = isSketchFab
//            }
//        }

//    let read3 = 
//        json {            
//            let! useTranslationArrows = Json.read "useTranslationArrows"
//            let! translation          = Json.readWith Ext.fromJson<V3dInput,Ext> "translation"
//            let! yaw                  = Json.readWith Ext.fromJson<NumericInput,Ext> "yaw"
//            let! pitch                = Json.readWith Ext.fromJson<NumericInput,Ext> "pitch"
//            let! roll                 = Json.readWith Ext.fromJson<NumericInput,Ext> "roll"
//            let! trafo                = Json.readWith Ext.fromJson<Trafo3d,Ext> "trafo"
//            let! pivot                = Json.read "pivot"
//            let! flipZ                = Json.read "flipZ"
//            let! isSketchFab          = Json.read "isSketchFab"
            
//            return {
//                version              = current
//                useTranslationArrows = useTranslationArrows
//                translation          = translation
//                yaw                  = yaw
//                pitch                = pitch
//                roll                 = roll
//                trafo                = trafo
//                pivot                = pivot |> V3d.Parse
//                flipZ                = flipZ
//                isSketchFab          = isSketchFab
//            }
//        }

//type Transformations with
    

//    static member FromJson(_ : Transformations) =
//        json {
//            let! v = Json.read "version"
//            match v with
//            | 0 -> return! Transformations.read0
//            | 1 -> return! Transformations.read1
//            | 2 -> return! Transformations.read2
//            | 3 -> return! Transformations.read3
//            | _ -> 
//                return! v 
//                |> sprintf "don't know version %A  of Transformations"
//                |> Json.error
//        }

//    static member ToJson( x : Transformations) =
//        json {            
//            do! Json.write "version" x.version
//            do! Json.write "useTranslationArrows" x.useTranslationArrows
//            do! Json.writeWith Ext.toJson<V3dInput,Ext> "translation" x.translation
//            do! Json.writeWith Ext.toJson<NumericInput,Ext> "yaw"   x.yaw
//            do! Json.writeWith Ext.toJson<NumericInput,Ext> "pitch" x.pitch
//            do! Json.writeWith Ext.toJson<NumericInput,Ext> "roll"  x.roll
//            do! Json.writeWith Ext.toJson<Trafo3d,Ext> "trafo" x.trafo
//            do! Json.write "pivot" (x.pivot.ToString())
//            do! Json.write "flipZ" x.flipZ
//            do! Json.write "isSketchFab" x.isSketchFab
//        }

type SurfaceTrafo = {
    id : string
    trafo: Trafo3d
}

type SurfaceType = 
    | SurfaceOPC = 0
    | Mesh = 1


type MeshLoaderType = 
    | Unkown    = 0
    | Assimp    = 1
    | GlTf      = 2
    | Wavefront = 3
    | Ply       = 4


module Init =
    open MBrace.FsPickler
    open MBrace.FsPickler.Combinators    
    open Aardvark.Geometry

    let initInCoreKdTree a : KdTrees.InCoreKdTree =
        {
            kdTree = ConcreteKdIntersectionTree()
            boundingBox = a
        }

    let incorePickler : Pickler<KdTrees.InCoreKdTree> =
        Pickler.product initInCoreKdTree
        ^. Pickler.field (fun s -> s.boundingBox)     Pickler.auto<Box3d>

        
    let quality = {
        value = 1.0
        min =  -2.0
        max = 5.0
        step = 0.1
        format = "{0:0.0}"
    }

    let triangleSize = {
        value = 1.0
        min = 0.0
        max = 10000.0
        step = 0.01
        format = "{0:0.000}"
    }

    let priority = {
        value  = 0.0
        min    = 0.0
        max    = 10.0
        step   = 1.0
        format = "{0:0}"
    }

    //let scaling = {
    //    value  = 1.00
    //    min    = 0.01
    //    max    = 50.00
    //    step   = 0.01
    //    format = "{0:0.00}"
    //}

    // colorCorrection
    let contrast = {
        value = 0.0
        min =  -255.0
        max = 255.0
        step = 1.0
        format = "{0:0}"
    }

    let brightness = {
        value = 0.0
        min =  -255.0
        max = 255.0
        step = 1.0
        format = "{0:0}"
    }

    let gamma = {
        value = 1.0
        min =  0.01
        max = 10.0
        step = 0.01
        format = "{0:0.00}"
    }
   

    let initColorCorrection = {
        version      = ColorCorrection.current
        contrast     = contrast
        useContrast  = false
        brightness   = brightness
        useBrightn   = false
        gamma        = gamma
        useGamma     = false
        color        = { c = C4b.Magenta }
        useColor     = false
        useGrayscale = false
    } 

    let minR = {
        value = 0.0
        min =  0.00
        max = 255.0
        step = 1.0
        format = "{0:0}"
    }

    let maxR = {
        value = 255.0
        min =  0.00
        max = 255.0
        step = 1.0
        format = "{0:0}"
    }

    let minG = {
        value = 0.0
        min =  0.00
        max = 255.0
        step = 1.0
        format = "{0:0}"
    }

    let maxG = {
        value = 255.0
        min =  0.00
        max = 255.0
        step = 1.0
        format = "{0:0}"
    }

    let minB = {
        value = 0.0
        min =  0.00
        max = 255.0
        step = 1.0
        format = "{0:0}"
    }

    let maxB = {
        value = 255.0
        min =  0.00
        max = 255.0
        step = 1.0
        format = "{0:0}"
    }

    let initRadiometry = {
        version      = Radiometry.current
        useRadiometry = false
        minR = minR
        maxR = maxR
        minG = minG
        maxG = maxG
        minB = minB
        maxB = maxB
        }

    let translationInput = {
        value   = 0.0
        min     = -10000000.0
        max     = 10000000.0
        step    = 0.001
        format  = "{0:0.000}"
    }
    
    let initTranslation (v : V3d) = {
        x     = { translationInput with value = v.X }
        y     = { translationInput with value = v.Y }
        z     = { translationInput with value = v.Z }
        value = v    
    }
    let transformations = {
        version              = Transformations.current
        useTranslationArrows = false
        translation          = initTranslation (V3d.OOO)
        trafo                = Trafo3d.Identity
        yaw                  = Transformations.Initial.yaw
        pitch                = Transformations.Initial.pitch
        roll                 = Transformations.Initial.roll
        pivot                = initTranslation (V3d.OOO)
        oldPivot             = V3d.OOO
        showPivot            = false
        pivotChanged         = false
        flipZ                = false
        isSketchFab          = false
        scaling              = Transformations.Initial.scaling
        trafoChanged         = false
        usePivot             = false
        pivotSize            = Transformations.Initial.initPivotSize 0.4
    }
    


type TransferFunction =
    {
        tf : ColorMaps.TF
        textureCombiner : TextureCombiner
        blendFactor : float
    }

module TransferFunction =
    let empty = { tf = ColorMaps.TF.Passthrough; textureCombiner = TextureCombiner.Primary; blendFactor = 1.0 }


[<ModelType>]
type Surface = {
    
    version         : int

    guid            : System.Guid
    
    name            : string
    importPath      : string
    opcNames        : list<string>
    opcPaths        : list<string>    
    relativePaths   : bool
                    
    fillMode        : FillMode
    cullMode        : CullMode
    isVisible       : bool
    isActive        : bool
    quality         : NumericInput
    priority        : NumericInput
                    
    triangleSize    : NumericInput
    scaling         : NumericInput

    filterByDistance : bool
    filterDistance   : NumericInput
                    
    preTransform    : Trafo3d
    
    scalarLayers    : HashMap<int, ScalarLayer> 
    selectedScalar  : option<ScalarLayer>

    textureLayers   : IndexList<TextureLayer>
    primaryTexture     : Option<TextureLayer>
    secondaryTexture   : Option<TextureLayer>
    transferFunction   : TransferFunction
    opcxPath        : Option<string>

    [<NonAdaptiveAttribute>]
    surfaceType     : SurfaceType     
    preferredLoader : MeshLoaderType

    colorCorrection : ColorCorrection
    homePosition    : Option<CameraView>
    transformation  : Transformations
    radiometry      : Radiometry
}

module Surface =
    let current = 1 //16.06.2023 Laura: radiometry   

    module Initial =
        let triangleSize = {
            value = 1.0
            min = 0.0
            max = 10000.0
            step = 0.01
            format = "{0:0.000}"
        }

        let filterDistance (dist:float) = {
            value = dist
            min = 0.0
            max = 100000.0
            step = 0.01
            format = "{0:0.000}"
        }

    let read0 =
        json {
            let! guid            = Json.read "guid"
            let! name            = Json.read "name"
            let! importPath      = Json.read "importPath"  
            let! opcNames        = Json.read "opcNames"       
            let! opcPaths        = Json.read "opcPaths"       
            let! relativePaths   = Json.read "relativePaths"
            let! fillMode        = Json.read "fillMode"       
            let! cullMode        = Json.read "cullMode"       
            let! isVisible       = Json.read "isVisible"      
            let! isActive        = Json.read "isActive"       
            let! quality         = Json.readWith Ext.fromJson<NumericInput,Ext> "quality"
            let! priority        = Json.readWith Ext.fromJson<NumericInput,Ext> "priority"
            let! triangleSize    = Json.readWith Ext.fromJson<NumericInput,Ext> "triangleSize"   
            let! scaling         = Json.readWith Ext.fromJson<NumericInput,Ext> "scaling"
            let! preTransform    = Json.read "preTransform"
            let! scalarLayers    = Json.read "scalarLayers"  
            let! selectedScalar  = Json.read "selectedScalar"
            let! textureLayers   = Json.read "textureLayers"

            let! selectedTexture = Json.read "selectedTexture"

            let! surfaceType     = Json.read "surfaceType"    
            let! colorCorrection = Json.read "colorCorrection"
            let! transformation  = Json.read "transformation"

            let! preferredLoader = Json.readOrDefault "preferredMeshLoader" 0

            let! (cameraView : list<string>) = Json.read "homePosition"
            let cameraView = cameraView |> List.map V3d.Parse
            let view = 
                match cameraView.Length with
                | 5 -> 
                    CameraView(
                        cameraView.[0],
                        cameraView.[1],
                        cameraView.[2],
                        cameraView.[3], 
                        cameraView.[4]
                    ) |> Some
                | _ -> None

            let scalarLayers  = scalarLayers  |> HashMap.ofList
            let textureLayers = textureLayers |> IndexList.ofList


            return 
                {
                    version         = current
                    guid            = guid |> Guid
                    name            = name
                    importPath      = importPath
                    opcNames        = opcNames
                    opcPaths        = opcPaths
                    relativePaths   = relativePaths
                    fillMode        = fillMode |> enum<FillMode>
                    cullMode        = cullMode |> enum<CullMode>
                    isVisible       = isVisible
                    isActive        = isActive
                    quality         = quality
                    priority        = priority
                    triangleSize    = triangleSize
                    scaling         = scaling
                    preTransform    = preTransform |> Trafo3d.Parse
                    scalarLayers    = scalarLayers
                    selectedScalar  = selectedScalar
                    textureLayers   = textureLayers
                    primaryTexture   = selectedTexture
                    secondaryTexture = None
                    transferFunction = TransferFunction.empty
                    surfaceType     = surfaceType     |> enum<SurfaceType>
                    preferredLoader = preferredLoader |> enum<MeshLoaderType>
                    colorCorrection = colorCorrection
                    homePosition    = view
                    transformation  = transformation
                    radiometry      = Init.initRadiometry
                    opcxPath        = None

                    filterByDistance = false
                    filterDistance   = Initial.filterDistance 10.0
                }
        }

    let read1 =
        json {
            let! guid            = Json.read "guid"
            let! name            = Json.read "name"
            let! importPath      = Json.read "importPath"  
            let! opcNames        = Json.read "opcNames"       
            let! opcPaths        = Json.read "opcPaths"       
            let! relativePaths   = Json.read "relativePaths"
            let! fillMode        = Json.read "fillMode"       
            let! cullMode        = Json.read "cullMode"       
            let! isVisible       = Json.read "isVisible"      
            let! isActive        = Json.read "isActive"       
            let! quality         = Json.readWith Ext.fromJson<NumericInput,Ext> "quality"
            let! priority        = Json.readWith Ext.fromJson<NumericInput,Ext> "priority"
            let! triangleSize    = Json.readWith Ext.fromJson<NumericInput,Ext> "triangleSize"   

            let! filterByDistance = Json.tryRead "filterByDistance" 
            let! filterDistance   = Json.tryRead "filterDistance"

            let! scaling         = Json.readWith Ext.fromJson<NumericInput,Ext> "scaling"
            let! preTransform    = Json.read "preTransform"
            let! scalarLayers    = Json.read "scalarLayers"  
            let! selectedScalar  = Json.read "selectedScalar"
            let! textureLayers   = Json.read "textureLayers"
            let! selectedTexture = Json.read "selectedTexture"
            let! surfaceType     = Json.read "surfaceType"    
            let! colorCorrection = Json.read "colorCorrection"
            let! transformation  = Json.read "transformation"
            let! radiometry      = Json.read "radiometry"

            let! preferredLoader = Json.readOrDefault "preferredMeshLoader" 0

            let! (cameraView : list<string>) = Json.read "homePosition"
            let cameraView = cameraView |> List.map V3d.Parse
            let view = 
                match cameraView.Length with
                | 5 -> 
                    CameraView(
                        cameraView.[0],
                        cameraView.[1],
                        cameraView.[2],
                        cameraView.[3], 
                        cameraView.[4]
                    ) |> Some
                | _ -> None

            let scalarLayers  = scalarLayers  |> HashMap.ofList
            let textureLayers = textureLayers |> IndexList.ofList

            return 
                {
                    version         = current
                    guid            = guid |> Guid
                    name            = name
                    importPath      = importPath
                    opcNames        = opcNames
                    opcPaths        = opcPaths
                    relativePaths   = relativePaths
                    fillMode        = fillMode |> enum<FillMode>
                    cullMode        = cullMode |> enum<CullMode>
                    isVisible       = isVisible
                    isActive        = isActive
                    quality         = quality
                    priority        = priority
                    triangleSize    = triangleSize
                    scaling         = scaling
                    preTransform    = preTransform |> Trafo3d.Parse
                    scalarLayers    = scalarLayers
                    selectedScalar  = selectedScalar
                    textureLayers   = textureLayers
                    primaryTexture   = selectedTexture
                    secondaryTexture = None
                    transferFunction = TransferFunction.empty
                    opcxPath        = None
                    surfaceType     = surfaceType     |> enum<SurfaceType>
                    preferredLoader = preferredLoader |> enum<MeshLoaderType>
                    colorCorrection = colorCorrection
                    homePosition    = view
                    transformation  = transformation
                    radiometry      = radiometry

                    filterByDistance = match filterByDistance with |Some v -> v |None -> false
                    filterDistance   = match filterDistance with |Some d -> Initial.filterDistance d |None -> Initial.filterDistance 10.0
                }
        }
     
type Surface with
    static member FromJson( _ : Surface) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! Surface.read0
            | 1 -> return! Surface.read1
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of Surface" 
                |> Json.error         
        }

    static member ToJson (x : Surface) =
        json {
            do! Json.write "version" x.version
            do! Json.write "guid" x.guid
            do! Json.write "name" x.name
            do! Json.write "importPath" x.importPath
            do! Json.write "opcNames" x.opcNames
            do! Json.write "opcPaths" x.opcPaths
            do! Json.write "relativePaths" x.relativePaths
            do! Json.write "fillMode" (x.fillMode |> int)
            do! Json.write "cullMode" (x.cullMode |> int)
            do! Json.write "isVisible" x.isVisible
            do! Json.write "isActive" x.isActive
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "quality" x.quality
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "priority" x.priority
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "triangleSize" x.triangleSize
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "scaling" x.scaling
            do! Json.write "preTransform" (x.preTransform.ToString())
            do! Json.write "scalarLayers" (x.scalarLayers |> HashMap.toList)
            do! Json.write "selectedScalar" x.selectedScalar
            do! Json.write "textureLayers" (x.textureLayers |> IndexList.toList)
            do! Json.write "selectedTexture" x.primaryTexture
            do! Json.write "surfaceType" (x.surfaceType |> int)
            do! Json.write "colorCorrection" x.colorCorrection
            do! Json.write "preferredMeshLoader" (x.preferredLoader |> int)
            do! Json.write "radiometry" x.radiometry

            let home =
                match x.homePosition with
                | None -> []
                | Some h -> 
                    [
                        h.Sky
                        h.Location
                        h.Forward 
                        h.Up
                        h.Right
                    ] |> List.map(fun x -> x.ToString())

            do! Json.write "homePosition" home
            do! Json.write "transformation" x.transformation  
            
            do! Json.write "filterByDistance" x.filterByDistance
            do! Json.write "filterDistance" x.filterDistance.value
        }

type Picking =
| PickMesh of ISg
| KdTree   of HashMap<Box3d,KdTrees.Level0KdTree>
| NoPicking

[<ModelType>]
type SgSurface = {    
    [<NonAdaptive>]
    surface     : Guid    
    trafo       : Transformation
    globalBB    : Box3d
    sceneGraph  : ISg
    picking     : Picking
    opcScene    : Option<Aardvark.GeoSpatial.Opc.Configurations.OpcScene>

    [<NonAdaptive>]
    isObj       : bool
}




