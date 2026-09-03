namespace PRo3D.Base.Annotation

open System
open MBrace.FsPickler
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open PRo3D.Base.Gis
open Chiron

open Adaptify
open Aardvark.Geometry

#nowarn "0686"

type Projection = 
| Linear = 0 
| Viewpoint = 1 
| Sky = 2
| Bookmark = 3

type Geometry =
| Point         = 0
| Line          = 1
| Polyline      = 2
| Polygon       = 3
| DnS           = 4
| TT            = 5
| Ellipse       = 6
| AxisEllipse   = 7
| Axis4PEllipse = 8

module Geometry =

    /// Whether an annotation's `points` are still the control points the user clicked, and so can
    /// be edited one vertex at a time.
    ///
    /// The ellipse tools are excluded: once the ellipse is constructed, `getFinishedAnnotation`
    /// replaces their `points` with the sampled outline, so there are no control points left to
    /// move. DnS and TT are excluded because their points carry a fitted plane's meaning rather
    /// than a free polyline's.
    let isVertexEditable (geometry : Geometry) =
        match geometry with
        | Geometry.Point | Geometry.Line | Geometry.Polyline | Geometry.Polygon -> true
        | _ -> false

    /// The projection modes that produce a well-defined annotation for this geometry.
    ///
    /// The ellipse tools are fitted in a plane through the picked points and are only
    /// meaningful under `Sky` (see docs/EllipseAnnotations.md); every other tool supports
    /// all projection modes. `addPoint` in Drawing-App.fs has no branch for the excluded
    /// combinations and would `failwith` on them.
    ///
    /// The head of the list is the default: `SetGeometry` selects it when the current
    /// projection is not in the new geometry's allowed set.
    let allowedProjections (geometry : Geometry) : list<Projection> =
        match geometry with
        | Geometry.Ellipse | Geometry.AxisEllipse | Geometry.Axis4PEllipse -> [ Projection.Sky ]
        | _ -> [ Projection.Linear; Projection.Viewpoint; Projection.Sky; Projection.Bookmark ]

    /// Geometries whose results only make sense with a real reference body selected
    /// (`Planet.None` gives an arbitrary world-axis frame: dip/strike/thickness azimuths
    /// are measured from world +X, not true north). The ellipse tools additionally rely on
    /// the Sky surface-drape, which has no valid scale without a body and produces an
    /// empty outline -> a zero-point annotation that crashes result calculation.
    /// The annotation toolbar greys these out while `Planet.None` is the reference system.
    let needsReferenceBody (geometry : Geometry) =
        match geometry with
        | Geometry.DnS | Geometry.TT
        | Geometry.Ellipse | Geometry.AxisEllipse | Geometry.Axis4PEllipse -> true
        | _ -> false

type Semantic = 
| Horizon0 = 0 
| Horizon1 = 1 
| Horizon2 = 2 
| Horizon3 = 3 
| Horizon4 = 4 
| Crossbed = 5 
| GrainSize = 6 
| None = 7

[<ModelType>]
type Segment = {
    startPoint : V3d
    endPoint   : V3d
    
    points : IndexList<V3d> 
}
with
    static member FromJson ( _ : Segment) =
        json {
        
            let! startPoint = Json.read "startPoint"
            let! endPoint = Json.read "endPoint"

            let! points = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"

            return {
                startPoint = startPoint |> V3d.Parse
                endPoint   = endPoint |> V3d.Parse
                points     = points |> IndexList.ofList
            }
        }

    static member ToJson ( x : Segment) =
        json {
            do! Json.write "startPoint" (x.startPoint.ToString())
            do! Json.write "endPoint" (x.endPoint.ToString())
            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points" (x.points |> IndexList.toList)
        }

type Style = {
    color : C4b
    thickness : NumericInput
} with
    static member color_ =
        (fun b -> b.color), (fun c (b : Style) -> { b with color = c })
    static member thickness_ =
        (fun b -> b.thickness), (fun value b -> { b with thickness = value })

[<ModelType>]
type Statistics = {
    version      : int
    average      : float
    min          : float
    max          : float
    stdev        : float
    sumOfSquares : float
}
with 
    //version modules  
    static member current = 0
    static member private readV0 = 
        json {
            let! average      = Json.readFloat "average"
            let! min          = Json.readFloat "min"
            let! max          = Json.readFloat "max"
            let! stdev        = Json.readFloat "stdev"
            let! sumOfSquares = Json.readFloat "sumOfSquares"
            
            return {
                version      = Statistics.current
                average      = average
                min          = min
                max          = max
                stdev        = stdev
                sumOfSquares = sumOfSquares
            }
        }

    static member FromJson(_ : Statistics) = 
        json {
            let! v = Json.read "version"
            match v with            
            | 0 -> return! Statistics.readV0
            | _ -> return! v |> sprintf "don't know version %A  of Statistics" |> Json.error
        }
    static member ToJson (x : Statistics) =
        json {              
            do! Json.write      "version"      Statistics.current
            do! Json.writeFloat "sumOfSquares" x.sumOfSquares
            do! Json.writeFloat "stdev"        x.stdev
            do! Json.writeFloat "max"          x.max
            do! Json.writeFloat "min"          x.min
            do! Json.writeFloat "average"      x.average
        }

module Statistics =
    
    //initial
    let initial = 
        {
            version      = Statistics.current
            average      = Double.NaN
            min          = Double.NaN
            max          = Double.NaN
            stdev        = Double.NaN
            sumOfSquares = Double.NaN
        }
    
[<ModelType>]
type DipAndStrikeResults = {
    version         : int
    plane           : Plane3d
    dipAngle        : float
    dipDirection    : V3d
    strikeDirection : V3d
    dipAzimuth      : float
    strikeAzimuth   : float
    centerOfMass    : V3d
    error           : Statistics
    regressionInfo  : option<RegressionInfo3d>
}
with 
    static member current = 1
    static member private readV0 = 
        json {
            let! plane           = Json.read "plane"
            let! dipAngle        = Json.read "dipAngle"
            let! dipDirection    = Json.read "dipDirection"
            let! strikeDirection = Json.read "strikeDirection"
            let! dipAzimuth      = Json.read "dipAzimuth"
            let! strikeAzimuth   = Json.read "strikeAzimuth"
            let! centerOfMass    = Json.read "centerOfMass"
            let! error           = Json.read "error"
            
            return {
                version         = DipAndStrikeResults.current
                plane           = plane |> Json.parsePlane3d //plane |> Plane3d.Parse
                dipAngle        = dipAngle
                dipDirection    = dipDirection |> V3d.Parse
                strikeDirection = strikeDirection |> V3d.Parse
                dipAzimuth      = dipAzimuth
                strikeAzimuth   = strikeAzimuth
                centerOfMass    = centerOfMass |> V3d.Parse
                error           = error
                regressionInfo  = None
            }
        }

    static member private readV1 = 
        json {
            let! plane           = Json.read "plane"
            let! dipAngle        = Json.read "dipAngle"
            let! dipDirection    = Json.read "dipDirection"
            let! strikeDirection = Json.read "strikeDirection"
            let! dipAzimuth      = Json.read "dipAzimuth"
            let! strikeAzimuth   = Json.read "strikeAzimuth"
            let! centerOfMass    = Json.read "centerOfMass"
            let! error           = Json.read "error"
            let! regressionInfo  = Json.read "regressionInfo"
            
            return {
                version         = DipAndStrikeResults.current
                plane           = plane |> Json.parsePlane3d //plane |> Plane3d.Parse
                dipAngle        = dipAngle
                dipDirection    = dipDirection |> V3d.Parse
                strikeDirection = strikeDirection |> V3d.Parse
                dipAzimuth      = dipAzimuth
                strikeAzimuth   = strikeAzimuth
                centerOfMass    = centerOfMass |> V3d.Parse
                error           = error
                regressionInfo  = regressionInfo
            }
        }
    
    static member FromJson(_ : DipAndStrikeResults) = 
        json {
            let! v = Json.read "version"
            match v with            
              | 0 -> return! DipAndStrikeResults.readV0
              | 1 -> return! DipAndStrikeResults.readV1
              | _ -> return! v |> sprintf "don't know version %A  of DipAndStrikeResults" |> Json.error
        }
    static member ToJson (x : DipAndStrikeResults) =
        json {
            do! Json.write "version"          x.version                    
            do! Json.write "plane"            (x.plane.ToString())
            do! Json.write "dipAngle"         x.dipAngle          
            do! Json.write "dipDirection"     (x.dipDirection.ToString())
            do! Json.write "strikeDirection"  (x.strikeDirection.ToString())        
            do! Json.write "dipAzimuth"       x.dipAzimuth     
            do! Json.write "strikeAzimuth"    x.strikeAzimuth  
            do! Json.write "centerOfMass"     (x.centerOfMass.ToString())
            do! Json.write "error"            x.error
            do! Json.write "regressionInfo"   x.regressionInfo
        }

    static member initial =
        {
            version         = DipAndStrikeResults.current
            plane           = Plane3d.Invalid      
            dipAngle        = Double.NaN  
            dipDirection    = V3d.NaN
            strikeDirection = V3d.NaN
            dipAzimuth      = Double.NaN  
            strikeAzimuth   = Double.NaN  
            centerOfMass    = V3d.NaN  
            error           = Statistics.initial
            regressionInfo  = None
        }  


        
[<ModelType>]
type AnnotationResults = {
    version           : int
    height            : float
    heightDelta       : float
    avgAltitude       : float
    length            : float
    wayLength         : float
    bearing           : float
    slope             : float
    trueThickness     : float
    verticalThickness : float
    area              : float
}
with 
    static member current = 3
    static member private readV0 =
        json {      
            let! height      = Json.readFloat "height"     
            let! heightDelta = Json.readFloat "heightDelta"
            let! avgAltitude = Json.readFloat "avgAltitude"
            let! length      = Json.readFloat "length"     
            let! wayLength   = Json.readFloat "wayLength"  
            let! bearing     = Json.readFloat "bearing"    
            let! slope       = Json.readFloat "slope"
            
            return {
                version           = AnnotationResults.current    
                height            = height     
                heightDelta       = heightDelta
                avgAltitude       = avgAltitude
                length            = length     
                wayLength         = wayLength  
                bearing           = bearing    
                slope             = slope            
                trueThickness     = Double.NaN
                verticalThickness = Double.NaN
                area              = Double.NaN
            }
        }

    static member private readV1 =
        json {      
            let! height         = Json.readFloat "height"     
            let! heightDelta    = Json.readFloat "heightDelta"
            let! avgAltitude    = Json.readFloat "avgAltitude"
            let! length         = Json.readFloat "length"     
            let! wayLength      = Json.readFloat "wayLength"  
            let! bearing        = Json.readFloat "bearing"    
            let! slope          = Json.readFloat "slope"
            let! trueThickness  = Json.readFloat "trueThickness"
            
            return {
                version           = AnnotationResults.current    
                height            = height     
                heightDelta       = heightDelta
                avgAltitude       = avgAltitude
                length            = length
                wayLength         = wayLength  
                bearing           = bearing
                slope             = slope
                trueThickness     = trueThickness
                verticalThickness = Double.NaN
                area              = Double.NaN
            }
        }

    static member private readV2 =
        json {      
            let! height             = Json.readFloat "height"     
            let! heightDelta        = Json.readFloat "heightDelta"
            let! avgAltitude        = Json.readFloat "avgAltitude"
            let! length             = Json.readFloat "length"     
            let! wayLength          = Json.readFloat "wayLength"  
            let! bearing            = Json.readFloat "bearing"    
            let! slope              = Json.readFloat "slope"
            let! trueThickness      = Json.readFloat "trueThickness"
            let! verticalThickness  = Json.readFloat "verticalThickness"
            
            return {
                version           = AnnotationResults.current    
                height            = height     
                heightDelta       = heightDelta
                avgAltitude       = avgAltitude
                length            = length
                wayLength         = wayLength  
                bearing           = bearing
                slope             = slope
                trueThickness     = trueThickness
                verticalThickness = verticalThickness
                area              = Double.NaN
            }
        }

    static member private readV3 = 
        json {      
            let! height             = Json.readFloat "height"     
            let! heightDelta        = Json.readFloat "heightDelta"
            let! avgAltitude        = Json.readFloat "avgAltitude"
            let! length             = Json.readFloat "length"     
            let! wayLength          = Json.readFloat "wayLength"  
            let! bearing            = Json.readFloat "bearing"    
            let! slope              = Json.readFloat "slope"
            let! trueThickness      = Json.readFloat "trueThickness"
            let! verticalThickness  = Json.readFloat "verticalThickness"
            let! area               = Json.readFloat "area"
            
            return {
                version           = AnnotationResults.current    
                height            = height     
                heightDelta       = heightDelta
                avgAltitude       = avgAltitude
                length            = length
                wayLength         = wayLength  
                bearing           = bearing
                slope             = slope
                trueThickness     = trueThickness
                verticalThickness = verticalThickness
                area              = area
            }
        }

    static member FromJson(_: AnnotationResults) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! AnnotationResults.readV0
            | 1 -> return! AnnotationResults.readV1
            | 2 -> return! AnnotationResults.readV2
            | 3 -> return! AnnotationResults.readV3
            | _ -> return! v |> sprintf "don't know version %A  of AnnotationResults" |> Json.error
        }
    
    static member ToJson (x : AnnotationResults) =
        json {
            do! Json.write      "version"           x.version
            do! Json.writeFloat "height"            x.height      
            do! Json.writeFloat "heightDelta"       x.heightDelta       
            do! Json.writeFloat "avgAltitude"       x.avgAltitude 
            do! Json.writeFloat "length"            x.length            
            do! Json.writeFloat "wayLength"         x.wayLength       
            do! Json.writeFloat "bearing"           x.bearing     
            do! Json.writeFloat "slope"             x.slope       
            do! Json.writeFloat "trueThickness"     x.trueThickness
            do! Json.writeFloat "verticalThickness" x.verticalThickness
            do! Json.writeFloat "area"              x.area
        }


type EllipticAnnotationResult = 
    {
        geographicalEllipse      : Ellipse2d
        geographicalEllipseAssym : Option<Ellipse2d>
    }
    with
        static let version = 0

        static member private readV0 =
            json {      
                let! center             = Json.read "center"     
                let! major              = Json.read "major"
                let! minor              = Json.read "minor"

                let! center2            = Json.tryRead "centerAssim"
                let! major2             = Json.tryRead "majorAssim"
                let! minor2             = Json.tryRead "minorAssim"

                let assymEllipse = 
                    match center2, major2, minor2 with
                    | Some c, Some ma, Some mi -> 
                        Some(Ellipse2d(V2d.Parse(c), V2d.Parse(ma), V2d.Parse(mi)))
                    | _ -> 
                        None
            
                return {
                    geographicalEllipse      = Ellipse2d(V2d.Parse(center), V2d.Parse(major), V2d.Parse(minor))
                    geographicalEllipseAssym = assymEllipse
                }
            }

        static member FromJson(_: EllipticAnnotationResult) =
            json {
                let! v = Json.read "version"
                match v with 
                | 0 -> return! EllipticAnnotationResult.readV0
                | _ -> return! v |> sprintf "don't know version %A  of AnnotationResults" |> Json.error
            }
    
        static member ToJson (x : EllipticAnnotationResult) =
            json {
                do! Json.write   "version"  version
                do! Json.write   "center"   (string x.geographicalEllipse.Center)
                do! Json.write   "major"    (string x.geographicalEllipse.Axis0)
                do! Json.write   "minor"    (string x.geographicalEllipse.Axis1)
                
                if x.geographicalEllipseAssym.IsSome then
                    do! Json.write "centerAssim" (string x.geographicalEllipseAssym.Value.Center)
                    do! Json.write "majorAssim"  (string x.geographicalEllipseAssym.Value.Axis0)
                    do! Json.write "minorAssim"  (string x.geographicalEllipseAssym.Value.Axis1)
    

            }
    

module AnnotationResults =    
    
    let initial = 
        {
            version           = AnnotationResults.current
            height            = Double.NaN
            heightDelta       = Double.NaN
            avgAltitude       = Double.NaN
            length            = Double.NaN
            wayLength         = Double.NaN
            bearing           = Double.NaN
            slope             = Double.NaN
            trueThickness     = Double.NaN
            verticalThickness = Double.NaN
            area              = Double.NaN
        }  

type SemanticId = SemanticId of string
type SemanticType = Metric = 0 | Angular = 1 | Hierarchical = 2 | Undefined = 3

[<ModelType>]
type Annotation = {
    version        : int
    
    [<NonAdaptive>]
    key            : Guid
                   
    modelTrafo     : Trafo3d

    referenceSystem : Option<SpiceReferenceSystem>
                   
    geometry       : Geometry
    projection     : Projection         
    bookmarkId     : option<System.Guid>
    semantic       : Semantic
                   
    points         : IndexList<V3d>
    segments       : IndexList<Segment>
                   
    color          : ColorInput
    thickness      : NumericInput
                   
    results        : Option<AnnotationResults>
    dnsResults     : Option<DipAndStrikeResults>
    ellipticResults : Option<EllipticAnnotationResult>
                   
    visible          : bool
    showDns          : bool
    text             : string
    textsize         : NumericInput
    showText         : bool
    manualDipAngle   : NumericInput
    manualDipAzimuth : NumericInput
                 
    surfaceName    : string
    view           : CameraView
                   
    semanticId     : SemanticId
    semanticType   : SemanticType

    crossSectionClipping : bool
    crossSectionRefPoint : Option<V3d>

    /// fills the interior of closed geometries (polygon, ellipses); ignored for open ones
    showFill   : bool
    fillColor  : ColorInput
    fillAlpha  : NumericInput
}
with
    static member current = 5
    static member initialManualDipAngle = {
        value   = Double.NaN
        min     = 0.0
        max     = 90.0
        step    = 0.1
        format  = "{0:0.0}"
    }

    static member initialmanualDipAzimuth = {
        value   = Double.NaN
        min     = 0.0
        max     = 360.0
        step    = 0.1
        format  = "{0:0.0}"
    }

    // lives on the type rather than in module Annotation.Initial because the version readers
    // below need it, and the module is defined after the type
    static member initialFillAlpha = {
        value   = 0.35
        min     = 0.0
        max     = 1.0
        step    = 0.05
        format  = "{0:0.00}"
    }
        
    static member private readV0 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            
            let! points       = Json.read "points"
            let! segments     = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible      = Json.read "visible"
            let! showDns      = Json.read "showDns"
            
            let! text         = Json.read "text"
            
            let! textSize     = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"            
            
            let! surfaceName  = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> enum<Projection>
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> Serialization.jsonSerializer.UnPickleOfString
                segments         = segments      |> Serialization.jsonSerializer.UnPickleOfString
                color            = color
                thickness        = thickness      
                results          = results    
                dnsResults       = dnsResults         
                visible          = visible 
                showDns          = showDns   
                text             = text      
                textsize         = textSize
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = SemanticId ""
                semanticType     = SemanticType.Undefined
                manualDipAngle   = Annotation.initialManualDipAngle
                manualDipAzimuth = Annotation.initialmanualDipAzimuth
                bookmarkId       = None
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
            }
        }

    static member private readV1 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            
            let! points   = Json.read "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> enum<Projection>
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> Serialization.jsonSerializer.UnPickleOfString
                segments         = segments      |> Serialization.jsonSerializer.UnPickleOfString
                color            = color
                thickness        = thickness      
                results          = results    
                dnsResults       = dnsResults         
                visible          = visible 
                showDns          = showDns   
                text             = text      
                textsize         = textSize         
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView 
                semanticId       = semanticId |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = Annotation.initialManualDipAngle
                manualDipAzimuth = Annotation.initialmanualDipAzimuth
                bookmarkId       = None
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
            }
        }

    static member private readV2 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            
            let! points   = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> enum<Projection>
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> IndexList.ofList
                segments         = segments      |> IndexList.ofList
                color            = color
                thickness        = thickness
                results          = results
                dnsResults       = dnsResults
                visible          = visible
                showDns          = showDns
                text             = text
                textsize         = textSize
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = semanticId   |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = Annotation.initialManualDipAngle
                manualDipAzimuth = Annotation.initialmanualDipAzimuth
                bookmarkId       = None
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
            }
        }

    static member private readV3 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! bookmarkId   = Json.tryRead "bookmarkId"
            let! semantic     = Json.read "semantic"
            
            let! points   = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"

            let! manualDipAngle = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAngle"
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> enum<Projection>
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> IndexList.ofList
                segments         = segments      |> IndexList.ofList
                color            = color
                thickness        = thickness
                results          = results
                dnsResults       = dnsResults
                visible          = visible
                showDns          = showDns
                text             = text
                textsize         = textSize
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = semanticId   |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = manualDipAngle
                manualDipAzimuth = Annotation.initialmanualDipAzimuth
                bookmarkId       = bookmarkId
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
            }
        }

    static member private readV4 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            let! bookmarkId   = Json.tryRead "bookmarkId"
            
            let! points   = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"
    
            let! manualDipAngle = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAngle"
            let! manualDipAzimuth = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAzimuth"
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> enum<Projection>
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> IndexList.ofList
                segments         = segments      |> IndexList.ofList
                color            = color
                thickness        = thickness
                results          = results
                dnsResults       = dnsResults
                visible          = visible
                showDns          = showDns
                text             = text
                textsize         = textSize
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = semanticId   |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = manualDipAngle
                manualDipAzimuth = manualDipAzimuth
                bookmarkId       = bookmarkId
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
            }
        }

    static member private readV5 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            let! bookmarkId   = Json.tryRead "bookmarkId"
            
            let! points   = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            let! showText = Json.read "showText"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"
    
            let! manualDipAngle = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAngle"
            let! manualDipAzimuth = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAzimuth"

            let! ellipseProperties = Json.tryRead "ellipseResults"

            let! crossSectionClipping = Json.tryRead "crossSectionClipping"
            let! crossSectionRefPoint = Json.tryRead "crossSectionRefPoint"
            let crossSectionRefPoint : Option<V3d> =
                crossSectionRefPoint |> Option.map V3d.Parse

            // optional, no version bump - same approach as crossSectionClipping above.
            // primitives rather than ColorInput/NumericInput, so the input records can be
            // rebuilt with current min/max/step instead of pinning stale bounds in the file.
            let! showFill  = Json.tryRead "showFill"
            let! fillColor = Json.tryRead "fillColor"
            let! fillAlpha = Json.tryRead "fillAlpha"

            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> enum<Projection>
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> IndexList.ofList
                segments         = segments      |> IndexList.ofList
                color            = color
                thickness        = thickness
                results          = results
                dnsResults       = dnsResults
                visible          = visible
                showDns          = showDns
                text             = text
                textsize         = textSize
                showText         = showText
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = semanticId   |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = manualDipAngle
                manualDipAzimuth = manualDipAzimuth
                bookmarkId       = bookmarkId
                referenceSystem  = None
                ellipticResults  = ellipseProperties
                crossSectionClipping = crossSectionClipping |> Option.defaultValue false
                crossSectionRefPoint = crossSectionRefPoint
                showFill             = showFill |> Option.defaultValue false
                fillColor            =
                    fillColor
                    |> Option.map (fun (s : string) -> { c = C4b.Parse s })
                    |> Option.defaultValue color
                fillAlpha            =
                    { Annotation.initialFillAlpha with
                        value = fillAlpha |> Option.defaultValue Annotation.initialFillAlpha.value }
            }
        }

    static member FromJson(_:Annotation) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Annotation.readV0
            | 1 -> return! Annotation.readV1
            | 2 -> return! Annotation.readV2
            | 3 -> return! Annotation.readV3
            | 4 -> return! Annotation.readV4
            | 5 -> return! Annotation.readV5
            | _ -> return! v |> sprintf "don't know version %A of Annotation" |> Json.error
        }
    
    static member ToJson (x : Annotation) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "key"        (x.key.ToString())
            do! Json.write "modelTrafo" (x.modelTrafo.ToString())
            do! Json.write "geometry"   (x.geometry |> int)
            do! Json.write "projection" (x.projection |> int)
            if x.bookmarkId.IsSome then
                do! Json.write "bookmarkId" (x.bookmarkId.Value.ToString())
            do! Json.write "semantic"   (x.semantic |> int)
            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points" (x.points |> IndexList.toList)        
            do! Json.write "segments"   (x.segments |> IndexList.toList)
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>) "color" x.color
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "thickness" x.thickness
            do! Json.write "results"     x.results
            do! Json.write "dnsResults"  x.dnsResults
            do! Json.write "visible"     x.visible
            do! Json.write "showDns"     x.showDns
            do! Json.write "text"        x.text
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "textsize" x.textsize
            do! Json.write "showText"    x.showText
            do! Json.write "surfaceName" x.surfaceName
            
            let camView = x.view
            let camView = 
                [camView.Sky; camView.Location; camView.Forward; camView.Up ; camView.Right] 
                |> List.map(fun x -> x.ToString())

            do! Json.write "view" camView
            do! Json.write "semanticId" (x.semanticId.ToString())
            do! Json.write "semanticType" (x.semanticType |> int)

            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "manualDipAngle" (x.manualDipAngle)
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "manualDipAzimuth" (x.manualDipAzimuth)    
            
            match x.ellipticResults with
            | None -> ()
            | Some e ->
                do! Json.write "ellipseResults" x.ellipticResults

            do! Json.write "crossSectionClipping" x.crossSectionClipping
            match x.crossSectionRefPoint with
            | Some rp -> do! Json.write "crossSectionRefPoint" (rp.ToString())
            | None -> ()

            // written unconditionally: gating on showFill would discard a configured fill
            // colour and alpha as soon as the user switches the fill off and saves
            do! Json.write "showFill"  x.showFill
            do! Json.write "fillColor" (x.fillColor.c.ToString())
            do! Json.write "fillAlpha" x.fillAlpha.value
        }

module Annotation =
         
    module Initial =
        let samplingAmount = {
            value   = 1.0
            min     = 0.001
            max     = 1000.0
            step    = 0.001
            format  = "{0:0.000}"
        }

        let thickness = {
            value   = 3.0
            min     = 1.0
            max     = 8.0
            step    = 1.0
            format  = "{0:0}"
        }
        
        let textSize = {
            value   = 0.05
            min     = 0.01
            max     = 5.0
            step    = 0.01
            format  = "{0:0.00}"
        }        
          
    let thickness = [1.0; 2.0; 3.0; 4.0; 5.0; 1.0; 1.0]
    let color = 
        [
            new C4b(241,238,246); 
            new C4b(189,201,225); 
            new C4b(116,169,207); 
            new C4b(43,140,190); 
            new C4b(4,90,141); 
            new C4b(241,163,64); 
            new C4b(153,142,195) 
        ]
    
    let make 
        (projection : Projection) 
        (bookmarkId : Option<Guid>)
        (geometry : Geometry) 
        (referenceSystem : Option<SpiceReferenceSystem>)
        (color : ColorInput) 
        (thickness : NumericInput) 
        (surfName : string) 
        : Annotation  =

        {
            version          = Annotation.current
            key              = Guid.NewGuid()
            geometry         = geometry
            semantic         = Semantic.None
            points           = IndexList.Empty
            segments         = IndexList.Empty //[]
            color            = color
            thickness        = thickness
            results          = None
            dnsResults       = None
            projection       = projection
            visible          = true
            text             = ""
            textsize         = Initial.textSize
            showText         = true
            modelTrafo       = Trafo3d.Identity
            showDns          = 
                match geometry with 
                | Geometry.DnS | Geometry.TT -> true                 
                | _ -> false
            surfaceName      = surfName
            view             = FreeFlyController.initial.view
            semanticId       = SemanticId ""
            semanticType     = SemanticType.Undefined
            manualDipAngle   = Annotation.initialManualDipAngle
            manualDipAzimuth = Annotation.initialmanualDipAzimuth 
            bookmarkId       = bookmarkId
            referenceSystem  = referenceSystem
            ellipticResults  = None
            crossSectionClipping = false
            crossSectionRefPoint = None
            showFill             = false
            fillColor            = color
            fillAlpha            = Annotation.initialFillAlpha
        }

    let initial =
        make Projection.Viewpoint None Geometry.Polyline None { c = C4b.Magenta } Initial.thickness ""

    let retrievePoints (a : Annotation) =
        let points = 
            if a.segments.Count = 0 then
                a.points |> IndexList.toSeq
            else
                a.segments 
                |> IndexList.toSeq 
                |> Seq.map(fun x -> 
                    seq {
                        yield x.startPoint
                        yield! (x.points |> IndexList.toSeq)
                        yield x.endPoint
                    }
                ) 
                |> Seq.concat

        points |> Seq.toList