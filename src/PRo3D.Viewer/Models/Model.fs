namespace PRo3D



open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI.Mutable
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Application

open Aardvark.SceneGraph
open Aardvark.SceneGraph.Opc
open Aardvark.VRVis
open Aardvark.Base.Rendering
open Aardvark.Base.CameraView

open PRo3D.Base
open PRo3D.Base.Annotation
open Chiron

#nowarn "0044"
#nowarn "0686"


module List =
    let rec updateIf (p : 'a -> bool) (f : 'a -> 'a) (xs : list<'a>) = 
        match xs with
        | x :: xs ->
            if(p x) then (f x) :: updateIf p f xs
            else x :: updateIf p f xs
        | [] -> []

    let addWithoutDup e f list =
        match List.exists f list with
        | true -> list
        | false -> e :: list

module PList =
    let append' (a : plist<_>) (b : plist<_>) =
        let rec doIt xs =
            match xs with
            | x::xs -> PList.prepend x (doIt xs)
            | [] -> b
        doIt (PList.toList a)

    let tryHead (a: plist<_>) =
        a |> PList.tryAt 0

    let rev (a: plist<_>) =
        a |> PList.toList |> List.rev |> PList.ofList

    let applyNonEmpty (func : plist<_> -> plist<_>) (a : plist<_>) =
        if PList.count a > 0 then (a |> func) else a

    let remove' (v : 'a) (list: plist<'a>) : plist<'a> =
        list |> PList.filter(fun x -> x <> v)

module Option = 
    let fromBool v b =
        match b with 
        | true  -> Some v
        | false -> None

type NavigationMode = FreeFly = 0 | ArcBall = 1
type InteractionMode = PickOrbitCenter = 0 | Draw = 1 | PlaceObject = 2
type Points = list<V3d>

type Projection = Linear = 0 | Viewpoint = 1 | Sky = 2
type Geometry = Point = 0 | Line = 1 | Polyline = 2 | Polygon = 3 | DnS = 4
type Semantic = 
    | Horizon0 = 0 
    | Horizon1 = 1 
    | Horizon2 = 2 
    | Horizon3 = 3 
    | Horizon4 = 4 
    | Crossbed = 5 
    | GrainSize = 6 
    | None = 7

type ViewerMode =
    | Standard
    | Instrument

type TTAlignment =
    | Top    = 0
    | Right  = 1
    | Bottom = 2
    | Left   = 3
    
type SnapshotType = 
    | Camera
    | CameraAndSurface
    | CameraSurfaceMask

type GuiMode =
  | NoGui
  | RenderViewOnly
  | CoreGui
  | CompleteGui


type StartupArgs = {
    opcPaths              : option<list<string>>
    objPaths              : option<list<string>>
    snapshotPath          : option<string>
    outFolder             : string
    snapshotType          : option<SnapshotType>
    guiMode               : GuiMode
    showExplorationPoint  : bool
    showReferenceSystem   : bool
    renderDepth           : bool
    exitOnFinish          : bool
    areValid              : bool
    verbose               : bool
    startEmpty            : bool
    useAsyncLoading       : bool
    magnificationFilter   : bool
} with 
    member args.hasValidAnimationArgs =
        (args.opcPaths.IsSome || args.objPaths.IsSome)
            && args.snapshotType.IsSome && args.areValid  
    static member initArgs =
      {
          opcPaths              = None
          objPaths              = None
          snapshotPath          = None
          snapshotType          = None
          guiMode               = GuiMode.CompleteGui
          showExplorationPoint  = true
          showReferenceSystem   = true
          renderDepth           = false
          exitOnFinish          = false
          areValid              = true
          verbose               = false
          startEmpty            = false
          useAsyncLoading       = false
          magnificationFilter   = false
          outFolder             = ""
      }


[<DomainType>]
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

[<DomainType>]
type FalseColorsModel = {
    version         : int
    useFalseColors  : bool
    lowerBound      : NumericInput
    upperBound      : NumericInput
    interval        : NumericInput
    invertMapping   : bool
    lowerColor      : ColorInput //C4b
    upperColor      : ColorInput //C4b    
}

type FalseColorsShaderParams = {
    hsvStart   : V3d //(h, s, v)
    hsvEnd     : V3d //(h, s, v)
    interval   : float
    inverted   : bool
    lowerBound : float
    upperBound : float
    stepS      : float
    numOfRG    : float
}

module FalseColorsModel =
    
    let current = 0
    let read0 =
        json {
            let! useFalseColors = Json.read "useFalseColors"
            let! lowerBound     = Json.readWith Ext.fromJson<NumericInput,Ext> "lowerBound"
            let! upperBound     = Json.readWith Ext.fromJson<NumericInput,Ext> "upperBound"
            let! interval       = Json.readWith Ext.fromJson<NumericInput,Ext> "interval"
            let! invertMapping  = Json.read "invertMapping"
            let! lowerColor     = Json.readWith Ext.fromJson<ColorInput,Ext> "lowerColor"
            let! upperColor     = Json.readWith Ext.fromJson<ColorInput,Ext> "upperColor"

            return 
                {
                    version        = current
                    useFalseColors = useFalseColors
                    lowerBound     = lowerBound
                    upperBound     = upperBound
                    interval       = interval
                    invertMapping  = invertMapping
                    lowerColor     = lowerColor
                    upperColor     = upperColor
                }
        }
        //TODO TO rename inits
    let dnSInterv  = {
        value   = 5.0
        min     = 0.0
        max     = 90.0
        step    = 0.1
        format = "{0:0.00}"
    } 
    let initMinAngle = {
        value   = 0.0
        min     = 0.0
        max     = 90.0
        step    = 1.0
        format  = "{0:0.0}"
    }
    let initMaxAngle = {
        value   = 45.0
        min     = 1.0
        max     = 90.0
        step    = 1.0
        format  = "{0:0.0}"
    }

    let initDnSLegend = 
        {
            version         = current
            useFalseColors  = false
            lowerBound      = initMinAngle
            upperBound      = initMaxAngle
            interval        = dnSInterv
            invertMapping   = false
            lowerColor      = { c = C4b.Blue }
            upperColor      = { c = C4b.Red }
        }
   
    let scalarsInterv  = {
        value   = 5.0
        min     = 0.0
        max     = 90.0
        step    = 0.0001
        format  = "{0:0.0000}"
    } 

    let initlb (range: Range1d) = {
        value   = range.Min
        min     = range.Min
        max     = range.Max
        step    = 0.0001
        format  = "{0:0.0000}"
    }

    let initub (range: Range1d) = {
        value   = range.Max
        min     = range.Min
        max     = range.Max
        step    = 0.0001
        format  = "{0:0.0000}"
    }

    let initDefinedScalarsLegend (range: Range1d) = {
        version         = current
        useFalseColors  = false
        lowerBound      = initlb range
        upperBound      = initub range 
        interval        = scalarsInterv 
        invertMapping   = false
        lowerColor      = { c = C4b.Blue }
        upperColor      = { c = C4b.Red }
    }
    
    let initShaderParams = 
        {
            hsvStart = V3d(0.0, 0.0, 0.0) 
            hsvEnd   = V3d(0.0, 0.0, 0.0) 
            interval = 1.0
            inverted = false
            lowerBound = 0.0
            upperBound = 1.0
            stepS     = 1.0
            numOfRG  = 1.0
        }

type FalseColorsModel with
    static member FromJson(_ : FalseColorsModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! FalseColorsModel.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of FalseColorsModel"
                |> Json.error
        }
    static member ToJson (x : FalseColorsModel) =
        json {
            do! Json.write "version"         x.version
            do! Json.write "useFalseColors"  x.useFalseColors
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "lowerBound" x.lowerBound
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "upperBound" x.upperBound
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "interval"   x.interval
            do! Json.write "invertMapping"   x.invertMapping 
            do! Json.writeWith Ext.toJson<ColorInput,Ext> "lowerColor"   x.lowerColor
            do! Json.writeWith Ext.toJson<ColorInput,Ext> "upperColor"   x.upperColor
    
        }    

[<DomainType>]
type OrientationCubeModel =
  {
    camera  : CameraControllerState
  }

//[<DomainType>]
//type DipAndStrikeResults = {
//    plane           : Plane3d
//    dipAngle        : float
//    dipDirection    : V3d
//    strikeDirection : V3d
//    dipAzimuth      : float
//    strikeAzimuth   : float
//    centerOfMass    : V3d
//    error           : Statistics
//}

//[<DomainType>]
//type AnnotationResults = {
//    height      : float
//    heightDelta : float
//    avgAltitude : float
//    length      : float
//    wayLength   : float
//    bearing     : float
//    slope       : float
//}

//[<DomainType>]
//type Segment = {
//    startPoint : V3d
//    endPoint   : V3d
    
//    points : plist<V3d> 
//}

//[<DomainType; Obsolete("use Annotation' instead")>]
//type Annotation = {
//    [<PrimaryKey; NonIncremental>]
//    key         : Guid

//    modelTrafo  : Trafo3d

//    geometry    : Geometry
//    projection  : Projection
//    semantic    : Semantic

//    points      : plist<V3d>
//    segments    : plist<Segment> 
//    color       : ColorInput
//    thickness   : NumericInput
//    results     : Option<AnnotationResults>
//    dnsResults  : Option<DipAndStrikeResults>

//    visible     : bool
//    showDns     : bool
//    text        : string
//    textsize    : NumericInput

//    surfaceName : string
//    view        : CameraView
//}


type Style = {
    color : C4b
    thickness : NumericInput
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Annotation =
    open Aardvark.Base.IndexedGeometryPrimitives

    

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

    let ofSegment (s:Segment) : _Segment = s.points  |> PList.map ofV3d 
                                                     |> PList.toList

    let ofSegment1 (s:plist<V3d>) : _Segment = s  |> PList.map ofV3d
                                                  |> PList.toList


    let rec fold f s xs =
        match xs with
            | x::xs -> 
                    let r = fold f s xs
                    f x r
            | [] -> s

    let sum = [ 1 .. 10 ] |> List.fold (fun s e -> s * e) 1

    let sumDistance (polyline : Points) : double =
        polyline  |> List.pairwise |> List.fold (fun s (a,b) -> s + (b - a).LengthSquared) 0.0 |> Math.Sqrt

    //let ofAnnotation (a:Annotation) : _Annotation =
    //    let polygon = ofPolygon (a.points |> PList.toList)
    //    let avgHeight = (polygon |> List.map (fun v -> v.Z ) |> List.sum) / double polygon.Length
    //    let distance = sumDistance (a.points |> PList.toList)
    //    let angle, azimuth =
    //        match a.dnsResults with
    //            | Some dns -> 
    //                dns.dipAngle, dns.dipAzimuth
    //            | None -> -1.0, -1.0

    //    {            
    //        semantic = a.semantic.ToString()
    //        geometry = polygon
    //        segments = a.segments |> PList.map (fun x -> ofSegment1 x.points) |> PList.toList //|> List.map (fun x -> ofSegment x)
    //        color = a.color.ToString()
    //        thickness = a.thickness.value
            
    //        projection = a.projection.ToString()
    //        elevation = avgHeight
    //        distance = distance

    //        azimuth = azimuth
    //        angle = angle
    //        surfid = a.surfaceName
    //    }  

    //let ofDrawing (m : Drawing) : list<_Annotation> =
    //    m.finished.AsList |> List.map ofAnnotation

[<DomainType>]
type PathProxy = {
    absolutePath : option<string>
    relativePath : option<string>
}

[<DomainType>]
type MeasurementsImporterModel = {
    annotations : plist<Annotation>
}

type SurfaceTrafo = {
    id : string
    trafo: Trafo3d
}

type SurfaceShift = {
    id : string
    shift: float
}

[<DomainType>]
type SurfaceTrafoImporterModel = {
    trafos : plist<SurfaceTrafo>
}

[<DomainType>]
type ViewConfigModel = {
    [<NonIncremental>]
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
    //useSurfaceHighlighting  : bool
    //showExplorationPoint    : bool
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
        offset = depthOffset
        //useSurfaceHighlighting = true
        //showExplorationPoint = true
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
                }
            }

type ViewConfigModel with 
    static member FromJson(_ : ViewConfigModel) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ViewConfigModel.V0.read
            | 1 -> return! ViewConfigModel.V1.read
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
            do! Json.write "version" x.version
        }




