namespace PRo3D

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.SimulatedViews
open Aardvark.Application

open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis
open Aardvark.Base.Rendering
open Aardvark.Base.CameraView
open PRo3D.SimulatedViews
open PRo3D.Base
open PRo3D.Core
open PRo3D.Base.Annotation
open Chiron
open SnapshotSettings
open Shading.ShadingApp

#nowarn "0044"
#nowarn "0686"



type InteractionMode = PickOrbitCenter = 0 | Draw = 1 | PlaceObject = 2
type Points = list<V3d>

type Projection = 
    | Linear    = 0 
    | Viewpoint = 1 
    | Sky       = 2

type Geometry = 
    | Point     = 0 
    | Line      = 1 
    | Polyline  = 2 
    | Polygon   = 3 
    | DnS       = 4

type Semantic = 
    | Horizon0  = 0 
    | Horizon1  = 1 
    | Horizon2  = 2 
    | Horizon3  = 3 
    | Horizon4  = 4 
    | Crossbed  = 5 
    | GrainSize = 6
    | None      = 7

type ViewerMode =
    | Standard
    | Instrument




[<ModelType>]
type Statistics = {
  average      : float
  min          : float
  max          : float
  stdev        : float
  sumOfSquares : float
}

module Statistics = 
  let init = {
    average      = Double.NaN
    min          = Double.NaN
    max          = Double.NaN
    stdev        = Double.NaN
    sumOfSquares = Double.NaN
  }



[<ModelType>]
type OrientationCubeModel = {
    camera  : CameraControllerState
}

type Style = {
    color : C4b
    thickness : NumericInput
}
    
module JsonTypes =
    type _V3d = {
        X : double
        Y : double
        Z : double
    }

    type _Points = list<_V3d>

    type _Segment = list<_V3d>

    type _Annotation = {       
        semantic : string
        geometry : _Points 
        segments : list<_Segment>
        color : string
        thickness : double        
        projection : string
        elevation : double
        distance : double
        azimuth : double
        angle   : double
        surfid  : string
    }

    let ofV3d (v:V3d) : _V3d = { X = v.X; Y = v.Y; Z = v.Z }

    let ofPolygon (p:Points) : _Points = p  |> List.map ofV3d

    let ofSegment (s:Segment) : _Segment = 
        s.points  
        |> IndexList.map ofV3d 
        |> IndexList.toList

    let ofSegment1 (s:IndexList<V3d>) : _Segment = 
        s  
        |> IndexList.map ofV3d
        |> IndexList.toList


    let rec fold f s xs =
        match xs with
        | x::xs -> 
                let r = fold f s xs
                f x r
        | [] -> s

    let sum = [ 1 .. 10 ] |> List.fold (fun s e -> s * e) 1

    let sumDistance (polyline : Points) : double =
        polyline  
        |> List.pairwise 
        |> List.fold (fun s (a,b) -> s + (b - a).LengthSquared) 0.0 
        |> Math.Sqrt
    
[<ModelType>]
type PathProxy = {
    absolutePath : option<string>
    relativePath : option<string>
}





type SurfaceShift = {
    id : string
    shift: float
}



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
    lodColoring             : bool
    drawOrientationCube     : bool
    // SnapshotViewer extensions
    showExplorationPoint    : bool
    filterTexture           : bool 
    shadingApp              : Shading.ShadingApp
    snapshotSettings        : SnapshotSettings
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

    let depthOffset = {
       min = -500.0
       max = 500.0
       value = 0.001
       step = 0.001
       format = "{0:0.000}"
    }       

    let current = 1
 
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
        //useSurfaceHighlighting = true
        showExplorationPoint  = true
        filterTexture         = false 
        shadingApp     = Shading.ShadingApp.init
        snapshotSettings      =  SimulatedViews.SnapshotSettings.init
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
                    showExplorationPoint  = true
                    filterTexture         = false 
                    shadingApp     =  Shading.ShadingApp.init
                    snapshotSettings      =  SnapshotSettings.init
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
                    showExplorationPoint  = true
                    filterTexture         = false 
                    shadingApp     =  Shading.ShadingApp.init
                    snapshotSettings      =  SnapshotSettings.init
                }
            }

    module V2 =
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
                let! (showExplorationPoint : bool) = Json.read "showExplorationPoint"                        
                let! (filterTexture : bool)        = Json.read "filterTexture"                        
                let! (shadingProperties : Shading.ShadingApp) = Json.read "shadingProperties"          
                let! (snapshotSettings : SnapshotSettings) = Json.read "snapshotSettings"
            
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
                    showExplorationPoint  = true
                    filterTexture         = false 
                    shadingApp     = shadingProperties
                    snapshotSettings      = snapshotSettings
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
            | _ -> return! v |> sprintf "don't know version %A  of ViewConfigModel" |> Json.error
        }
    static member ToJson (x : ViewConfigModel) =
        json {
            do! Json.write "drawOrientationCube" x.drawOrientationCube                       
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "importTriangleSize" x.importTriangleSize
            do! Json.write "lodColoring" x.lodColoring
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "dnsPlaneSize" x.dnsPlaneSize
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "arrowThickness" x.arrowThickness
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "arrowLength" x.arrowLength
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "navigationSensitivity" x.navigationSensitivity
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "farPlane" x.farPlane
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "nearPlane" x.nearPlane
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "offset" x.offset
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "depthOffset" x.offset
            do! Json.write "showExplorationPoint" x.showExplorationPoint                        
            do! Json.write "filterTexture"        x.filterTexture                 
            do! Json.write "shadingProperties"    x.shadingApp       
            do! Json.write "snapshotSettings"     x.snapshotSettings
            do! Json.write "version"              x.version
        }




