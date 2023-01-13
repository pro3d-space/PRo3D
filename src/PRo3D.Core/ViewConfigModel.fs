namespace PRo3D.Core

open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
open Chiron
open PRo3D.Base

#nowarn "0686"

[<ModelType>]
type ViewConfigModel = {
    [<NonAdaptive>]
    version                 : int
    nearPlane               : NumericInput
    farPlane                : NumericInput
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

    let current = 2
 
    let initial = {
        version = current
        nearPlane             = initNearPlane
        farPlane              = initFarPlane
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
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "offset"                x.offset
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "depthOffset"           x.offset
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "pickingTolerance"      x.pickingTolerance
            do! Json.write "version" x.version
        }