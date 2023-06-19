namespace PRo3D.Core

open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
open Chiron

open PRo3D.Base
open Aardvark.Rendering

#nowarn "0686"

[<ModelType>]
type FrustumModel = {
    toggleFocal             : bool
    focal                   : NumericInput
    oldFrustum              : Frustum
    frustum                 : Frustum
    }

module FrustumModel =
    let focal = {
        value   = 10.25 
        min     = 10.0
        max     = 1000.0
        step    = 0.01
        format  = "{0:0.00}"
    }
    let hfov = 2.0 * atan(11.84 /(focal.value*2.0))
    
    let init near far =
        {
            toggleFocal             = true
            focal                   = focal
            oldFrustum              = Frustum.perspective 60.0 0.1 10000.0 1.0
            frustum                 = Frustum.perspective (hfov.DegreesFromRadians()) near far 1.0 //Frustum.perspective 60.0 0.1 10000.0 1.0
        }

type FrustumModel with
    static member ToJson (x : FrustumModel) =
        json {
          do! Json.write      "toggleFocal"  x.toggleFocal
          do! Json.write      "focal"        x.focal.value      
          do! Json.writeWith  (Ext.toJson<Frustum, Ext>) "frustumOld"   x.oldFrustum
          do! Json.writeWith  (Ext.toJson<Frustum, Ext>) "frustum"      x.frustum    
        }
    static member FromJson (x : FrustumModel) =
        json {
            let! toggleFocal = Json.read "toggleFocal"
            let! focal       = Json.read "focal"      
            let! oldFrustum  = Json.readWith Ext.fromJson<Frustum, Ext> "frustumOld"
            let! frustum     = Json.readWith Ext.fromJson<Frustum, Ext> "frustum"   

            return {
                toggleFocal = toggleFocal 
                focal       = {FrustumModel.focal with value = focal}
                oldFrustum  = oldFrustum  
                frustum     = frustum     
            }
        }

[<ModelType>]
type ViewConfigModel = {
    [<NonAdaptive>]
    version                 : int
    nearPlane               : NumericInput
    farPlane                : NumericInput
    frustumModel            : FrustumModel    
    navigationSensitivity   : NumericInput
    importTriangleSize      : NumericInput
    arrowLength             : NumericInput
    arrowThickness          : NumericInput
    dnsPlaneSize            : NumericInput
    offset                  : NumericInput
    pickingTolerance        : NumericInput
    lodColoring             : bool
    drawOrientationCube     : bool
    filterTexture           : bool
    //useSurfaceHighlighting  : bool
    showExplorationPointGui : bool
    }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ViewConfigModel =
    let initNearPlane = {
        value   = 0.1
        min     = 0.01
        max     = 1000.0
        step    = 0.01
        format  = "{0:0.00}"
    }
    let initFarPlane = {
        value   = 500000.0
        min     = 1.0
        max     = 10000000.0
        step    = 10.0
        format  = "{0:0.0}"
    }
    let initNavSens = {
        value   = 2.0
        min     = -1.0
        max     = 8.0
        step    = 0.25
        format  = "{0:0.00}"
    }
    let initArrowLength = {
        value   = 1.00
        min     = 0.00
        max     = 10.0
        step    = 0.05
        format  = "{0:0.00}"
    }
    let initArrowThickness = {
        value   = 3.0
        min     = 0.0
        max     = 10.0
        step    = 0.5
        format  = "{0:0.0}"
    }
    let initPlaneSize = {
        value   = 0.5
        min     = 0.0
        max     = 10.0
        step    = 0.05
        format  = "{0:0.00}"
    }

    let initImportTriangleSize = {
        value = 1000.0
        min = 0.0
        max = 1000.0
        step = 0.01
        format = "{0:0.000}"
    }

    let initPickingTolerance = {
        value  = 0.1
        min    = 0.01
        max    = 300.0
        step   = 0.01
        format = "{0:0.00}"
    }

    let depthOffset = {
       min = -500.0
       max = 500.0
       value = 0.001
       step = 0.001
       format = "{0:0.000}"
    }       

    let current = 4
 
    let initial = {
        version = current
        nearPlane             = initNearPlane
        farPlane              = initFarPlane
        frustumModel         = FrustumModel.init 0.1 10000.0
        navigationSensitivity = initNavSens
        arrowLength         = initArrowLength
        arrowThickness      = initArrowThickness
        dnsPlaneSize        = initPlaneSize
        lodColoring         = false
        importTriangleSize  = initImportTriangleSize        
        drawOrientationCube = false
        offset              = depthOffset
        pickingTolerance    = initPickingTolerance
        filterTexture       = false
        //useSurfaceHighlighting = true
        showExplorationPointGui = true
    }
       
    module V0 =
        let read = 
            json {
                let! nearPlane                    = Json.readWith Ext.fromJson<NumericInput,Ext> "nearPlane"
                let! farPlane                     = Json.readWith Ext.fromJson<NumericInput,Ext> "farPlane"
                let! navigationSensitivity        = Json.readWith Ext.fromJson<NumericInput,Ext> "navigationSensitivity"
                let! arrowLength                  = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowLength"
                let! arrowThickness               = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowThickness"
                let! dnsPlaneSize                 = Json.readWith Ext.fromJson<NumericInput,Ext> "dnsPlaneSize"
                let! (lodColoring : bool)         = Json.read "lodColoring"
                let! importTriangleSize           = Json.readWith Ext.fromJson<NumericInput,Ext> "importTriangleSize"
                let! (drawOrientationCube : bool) = Json.read "drawOrientationCube"                        
                
                //return initial
                
                return {            
                    version               = current
                    nearPlane             = nearPlane
                    farPlane              = farPlane
                    frustumModel          = FrustumModel.init 0.1 10000.0
                    navigationSensitivity = navigationSensitivity
                    arrowLength           = arrowLength
                    arrowThickness        = arrowThickness
                    dnsPlaneSize          = dnsPlaneSize
                    lodColoring           = lodColoring
                    importTriangleSize    = importTriangleSize      
                    drawOrientationCube   = drawOrientationCube
                    offset                = depthOffset
                    pickingTolerance      = initPickingTolerance
                    filterTexture         = false
                    showExplorationPointGui = true
                }
            }
    module V1 =
        let read = 
            json {
                let! nearPlane                    = Json.readWith Ext.fromJson<NumericInput,Ext> "nearPlane"
                let! farPlane                     = Json.readWith Ext.fromJson<NumericInput,Ext> "farPlane"
                let! navigationSensitivity        = Json.readWith Ext.fromJson<NumericInput,Ext> "navigationSensitivity"
                let! arrowLength                  = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowLength"
                let! arrowThickness               = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowThickness"
                let! dnsPlaneSize                 = Json.readWith Ext.fromJson<NumericInput,Ext> "dnsPlaneSize"
                let! (lodColoring : bool)         = Json.read "lodColoring"
                let! importTriangleSize           = Json.readWith Ext.fromJson<NumericInput,Ext> "importTriangleSize"
                let! (drawOrientationCube : bool) = Json.read "drawOrientationCube"                        
                let! depthoffset                  = Json.readWith Ext.fromJson<NumericInput,Ext> "depthOffset"
                
                //return initial
                
                return {            
                    version               = current
                    nearPlane             = nearPlane
                    farPlane              = farPlane
                    frustumModel          = FrustumModel.init 0.1 10000.0
                    navigationSensitivity = navigationSensitivity
                    arrowLength           = arrowLength
                    arrowThickness        = arrowThickness
                    dnsPlaneSize          = dnsPlaneSize
                    lodColoring           = lodColoring
                    importTriangleSize    = importTriangleSize      
                    drawOrientationCube   = drawOrientationCube
                    offset                = depthoffset
                    pickingTolerance      = initPickingTolerance
                    filterTexture         = false
                    showExplorationPointGui = true
                }
            }

    module V2 =
        let read = 
            json {
                let! nearPlane                    = Json.readWith Ext.fromJson<NumericInput,Ext> "nearPlane"
                let! farPlane                     = Json.readWith Ext.fromJson<NumericInput,Ext> "farPlane"
                let! navigationSensitivity        = Json.readWith Ext.fromJson<NumericInput,Ext> "navigationSensitivity"
                let! arrowLength                  = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowLength"
                let! arrowThickness               = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowThickness"
                let! dnsPlaneSize                 = Json.readWith Ext.fromJson<NumericInput,Ext> "dnsPlaneSize"
                let! (lodColoring : bool)         = Json.read "lodColoring"
                let! importTriangleSize           = Json.readWith Ext.fromJson<NumericInput,Ext> "importTriangleSize"
                let! (drawOrientationCube : bool) = Json.read "drawOrientationCube"                        
                let! depthoffset                  = Json.readWith Ext.fromJson<NumericInput,Ext> "depthOffset"
                let! pickingTolerance             = Json.readWith Ext.fromJson<NumericInput,Ext> "pickingTolerance"
                
                //return initial
                
                return {            
                    version               = current
                    nearPlane             = nearPlane
                    farPlane              = farPlane
                    frustumModel          = FrustumModel.init 0.1 10000.0
                    navigationSensitivity = navigationSensitivity
                    arrowLength           = arrowLength
                    arrowThickness        = arrowThickness
                    dnsPlaneSize          = dnsPlaneSize
                    lodColoring           = lodColoring
                    importTriangleSize    = importTriangleSize      
                    drawOrientationCube   = drawOrientationCube
                    offset                = depthoffset
                    pickingTolerance      = pickingTolerance
                    filterTexture         = false
                    showExplorationPointGui = true
                }
            }

    module V3 = //Snapshot Viewer Scene Compatibility
        let read = 
            json {
                let! nearPlane                     = Json.readWith Ext.fromJson<NumericInput,Ext> "nearPlane"
                let! farPlane                      = Json.readWith Ext.fromJson<NumericInput,Ext> "farPlane"
                let! navigationSensitivity         = Json.readWith Ext.fromJson<NumericInput,Ext> "navigationSensitivity"
                let! arrowLength                   = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowLength"
                let! arrowThickness                = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowThickness"
                let! dnsPlaneSize                  = Json.readWith Ext.fromJson<NumericInput,Ext> "dnsPlaneSize"
                let! (lodColoring : bool)          = Json.read "lodColoring"
                let! importTriangleSize            = Json.readWith Ext.fromJson<NumericInput,Ext> "importTriangleSize"
                let! (drawOrientationCube : bool)  = Json.read "drawOrientationCube"                        
                let! depthoffset                   = Json.readWith Ext.fromJson<NumericInput,Ext> "depthOffset"
                let! (filterTexture : bool)        = Json.read "filterTexture"                        
        
                return {            
                    version               = current
                    nearPlane             = nearPlane
                    farPlane              = farPlane
                    frustumModel          = FrustumModel.init 0.1 10000.0
                    navigationSensitivity = navigationSensitivity
                    arrowLength           = arrowLength
                    arrowThickness        = arrowThickness
                    dnsPlaneSize          = dnsPlaneSize
                    lodColoring           = lodColoring
                    importTriangleSize    = importTriangleSize      
                    drawOrientationCube   = drawOrientationCube
                    offset                = depthoffset
                    pickingTolerance      = initPickingTolerance
                    filterTexture         = filterTexture 
                    showExplorationPointGui = true
                }
            }

    module V4 = //moved Frustum model to ViewConfigModel for screen space scaling with focal length 
        let read = 
            json {
                let! nearPlane                     = Json.readWith Ext.fromJson<NumericInput,Ext> "nearPlane"
                let! farPlane                      = Json.readWith Ext.fromJson<NumericInput,Ext> "farPlane"
                let! frustumModel                  = Json.read "frustumModel"
                let! navigationSensitivity         = Json.readWith Ext.fromJson<NumericInput,Ext> "navigationSensitivity"
                let! arrowLength                   = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowLength"
                let! arrowThickness                = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowThickness"
                let! dnsPlaneSize                  = Json.readWith Ext.fromJson<NumericInput,Ext> "dnsPlaneSize"
                let! (lodColoring : bool)          = Json.read "lodColoring"
                let! importTriangleSize            = Json.readWith Ext.fromJson<NumericInput,Ext> "importTriangleSize"
                let! (drawOrientationCube : bool)  = Json.read "drawOrientationCube"                        
                let! depthoffset                   = Json.readWith Ext.fromJson<NumericInput,Ext> "depthOffset"
                let! (filterTexture : bool)        = Json.read "filterTexture"                        
        
                return {            
                    version               = current
                    nearPlane             = nearPlane
                    farPlane              = farPlane
                    frustumModel          = frustumModel
                    navigationSensitivity = navigationSensitivity
                    arrowLength           = arrowLength
                    arrowThickness        = arrowThickness
                    dnsPlaneSize          = dnsPlaneSize
                    lodColoring           = lodColoring
                    importTriangleSize    = importTriangleSize      
                    drawOrientationCube   = drawOrientationCube
                    offset                = depthoffset
                    pickingTolerance      = initPickingTolerance
                    filterTexture         = filterTexture 
                    showExplorationPointGui = true
                }
            }

type ViewConfigModel with 
    static member FromJson(_ : ViewConfigModel) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ViewConfigModel.V0.read
            | 1 -> return! ViewConfigModel.V1.read
            | 2 -> return! ViewConfigModel.V2.read
            | 3 -> return! ViewConfigModel.V3.read
            | 4 -> return! ViewConfigModel.V4.read
            | _ -> return! v |> sprintf "don't know version %A  of ViewConfigModel" |> Json.error
        }
    static member ToJson (x : ViewConfigModel) =
        json {
            do! Json.write "drawOrientationCube" x.drawOrientationCube                       
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "importTriangleSize"    x.importTriangleSize
            do! Json.write "lodColoring" x.lodColoring
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "dnsPlaneSize"          x.dnsPlaneSize
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "arrowThickness"        x.arrowThickness
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "arrowLength"           x.arrowLength
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "navigationSensitivity" x.navigationSensitivity
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "farPlane"              x.farPlane
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "nearPlane"             x.nearPlane
            do! Json.write                                    "frustumModel"          x.frustumModel
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "offset"                x.offset
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "depthOffset"           x.offset
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "pickingTolerance"      x.pickingTolerance
            do! Json.write "filterTexture" x.filterTexture
            do! Json.write "version" x.version
        }