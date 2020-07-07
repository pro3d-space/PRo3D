namespace PRo3D.Base.Annotation

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI

open Chiron
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base

#nowarn "0686"

type Projection = 
| Linear = 0 
| Viewpoint = 1 
| Sky = 2

type Geometry = 
| Point = 0 
| Line = 1 
| Polyline = 2 
| Polygon = 3 
| DnS = 4

type Semantic = 
| Horizon0 = 0 
| Horizon1 = 1 
| Horizon2 = 2 
| Horizon3 = 3 
| Horizon4 = 4 
| Crossbed = 5 
| GrainSize = 6 
| None = 7

[<DomainType>]
type Segment = {
    startPoint : V3d
    endPoint   : V3d
    
    points : plist<V3d> 
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
                points     = points |> PList.ofList
            }
        }

    static member ToJson ( x : Segment) =
        json {
            do! Json.write "startPoint" (x.startPoint.ToString())
            do! Json.write "endPoint" (x.endPoint.ToString())
            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points" (x.points |> PList.toList)
        }

type Style = {
    color : C4b
    thickness : NumericInput
}

[<DomainType>]
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
            let! average      = Json.read "average"
            let! min          = Json.read "min"
            let! max          = Json.read "max"
            let! stdev        = Json.read "stdev"
            let! sumOfSquares = Json.read "sumOfSquares"
            
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
            do! Json.write "sumOfSquares" x.sumOfSquares            
            do! Json.write "stdev" x.stdev            
            do! Json.write "max" x.max            
            do! Json.write "min" x.min                                 
            do! Json.write "average" x.average             
            do! Json.write "version" x.version
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
    
[<DomainType>]
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
}
with 
    static member current = 0
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
          }
        }
    
    static member FromJson(_ : DipAndStrikeResults) = 
        json {
            let! v = Json.read "version"
            match v with            
              | 0 -> return! DipAndStrikeResults.readV0
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
        }

module DipAndStrikeResults =  
    
    //initial
    let initial = 
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
        }  
        
[<DomainType>]
type AnnotationResults = {
    version     : int
    height      : float
    heightDelta : float
    avgAltitude : float
    length      : float
    wayLength   : float
    bearing     : float
    slope       : float
}
with 
    static member current = 0
    static member private readV0 =
        json {      
            let! height     = Json.readFloat "height"     
            let! heightDelta= Json.readFloat "heightDelta"
            let! avgAltitude= Json.readFloat "avgAltitude"
            let! length     = Json.readFloat "length"     
            let! wayLength  = Json.readFloat "wayLength"  
            let! bearing    = Json.readFloat "bearing"    
            let! slope      = Json.readFloat "slope"
            
            return {
              version     = AnnotationResults.current    
              height      = height     
              heightDelta = heightDelta
              avgAltitude = avgAltitude
              length      = length     
              wayLength   = wayLength  
              bearing     = bearing    
              slope       = slope            
            }
        }
    static member FromJson(_: AnnotationResults) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! AnnotationResults.readV0
            | _ -> return! v |> sprintf "don't know version %A  of AnnotationResults" |> Json.error
        }
    
    static member ToJson (x : AnnotationResults) =
        json {
            do! Json.write      "version"     x.version     
            do! Json.writeFloat "height"      x.height      
            do! Json.writeFloat "heightDelta" x.heightDelta       
            do! Json.writeFloat "avgAltitude" x.avgAltitude 
            do! Json.writeFloat "length" x.length            
            do! Json.writeFloat "wayLength" x.wayLength       
            do! Json.writeFloat "bearing"     x.bearing     
            do! Json.writeFloat "slope"       x.slope       
        }

module AnnotationResults =
    let current = 0
    //conversion
    
    let initial = 
        {
            version     = current
            height      = Double.NaN
            heightDelta = Double.NaN
            avgAltitude = Double.NaN
            length      = Double.NaN
            wayLength   = Double.NaN
            bearing     = Double.NaN
            slope       = Double.NaN
        }  

type SemanticId = SemanticId of string
type SemanticType = Metric = 0 | Angular = 1 | Hierarchical = 2 | Undefined = 3

[<DomainType>]
type Annotation = {
    version      : int
    
    [<PrimaryKey; NonIncremental>]
    key          : Guid
                 
    modelTrafo   : Trafo3d
                 
    geometry     : Geometry
    projection   : Projection

    semantic     : Semantic
                 
    points       : plist<V3d>
    segments     : plist<Segment>
                 
    color        : ColorInput
    thickness    : NumericInput
                 
    results      : Option<AnnotationResults>
    dnsResults   : Option<DipAndStrikeResults>
                 
    visible      : bool
    showDns      : bool
    text         : string
    textsize     : NumericInput
                 
    surfaceName  : string
    view         : CameraView
    
    semanticId   : SemanticId
    semanticType : SemanticType
}
with 
    static member current = 2
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
              version      = Annotation.current
              key          = key           |> Guid.Parse
              modelTrafo   = modelTrafo    |> Trafo3d.Parse        
              geometry     = geometry      |> enum<Geometry>
              projection   = projection    |> enum<Projection>
              semantic     = semantic      |> enum<Semantic>
              points       = points        |> Serialization.jsonSerializer.UnPickleOfString
              segments     = segments      |> Serialization.jsonSerializer.UnPickleOfString
              color        = color
              thickness    = thickness      
              results      = results    
              dnsResults   = dnsResults         
              visible      = visible 
              showDns      = showDns   
              text         = text      
              textsize     = textSize         
              surfaceName  = surfaceName
              view         = cameraView
              semanticId   = SemanticId ""
              semanticType = SemanticType.Undefined
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
                version      = Annotation.current
                key          = key           |> Guid.Parse
                modelTrafo   = modelTrafo    |> Trafo3d.Parse        
                geometry     = geometry      |> enum<Geometry>
                projection   = projection    |> enum<Projection>
                semantic     = semantic      |> enum<Semantic>
                points       = points        |> Serialization.jsonSerializer.UnPickleOfString
                segments     = segments      |> Serialization.jsonSerializer.UnPickleOfString
                color        = color
                thickness    = thickness      
                results      = results    
                dnsResults   = dnsResults         
                visible      = visible 
                showDns      = showDns   
                text         = text      
                textsize     = textSize         
                surfaceName  = surfaceName
                view         = cameraView 
                semanticId   = semanticId |> SemanticId
                semanticType = semanticType |> enum<SemanticType>
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
                version      = Annotation.current
                key          = key           |> Guid.Parse
                modelTrafo   = modelTrafo    |> Trafo3d.Parse        
                geometry     = geometry      |> enum<Geometry>
                projection   = projection    |> enum<Projection>
                semantic     = semantic      |> enum<Semantic>
                points       = points        |> PList.ofList
                segments     = segments      |> PList.ofList
                color        = color
                thickness    = thickness
                results      = results
                dnsResults   = dnsResults
                visible      = visible
                showDns      = showDns
                text         = text
                textsize     = textSize
                surfaceName  = surfaceName
                view         = cameraView
                semanticId   = semanticId   |> SemanticId
                semanticType = semanticType |> enum<SemanticType>
            }
        }
    static member FromJson(_:Annotation) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Annotation.readV0
            | 1 -> return! Annotation.readV1
            | 2 -> return! Annotation.readV2
            | _ -> return! v |> sprintf "don't know version %A of Annotation" |> Json.error
        }
    
    static member ToJson (x : Annotation) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "key"        (x.key.ToString())
            do! Json.write "modelTrafo" (x.modelTrafo.ToString())
            do! Json.write "geometry"   (x.geometry |> int)
            do! Json.write "projection" (x.projection |> int)
            do! Json.write "semantic"   (x.semantic |> int)
            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points" (x.points |> PList.toList)        
            do! Json.write "segments"   (x.segments |> PList.toList)
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>) "color" x.color
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "thickness" x.thickness
            do! Json.write "results"       x.results
            do! Json.write "dnsResults"    x.dnsResults
            do! Json.write "visible"    x.visible
            do! Json.write "showDns"    x.showDns
            do! Json.write "text"    x.text
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "textsize" x.textsize
            do! Json.write "surfaceName"    x.surfaceName
            
            let camView = x.view
            let camView = [camView.Sky; camView.Location; camView.Forward; camView.Up ; camView.Right] |> List.map(fun x -> x.ToString())      
            do! Json.write "view" camView
            do! Json.write "semanticId" (x.semanticId.ToString())
            do! Json.write "semanticType" (x.semanticType |> int)
          
        }

module Annotation =
         
    let thickn = {
        value   = 3.0
        min     = 1.0
        max     = 8.0
        step    = 1.0
        format  = "{0:0}"
    }
    
    let texts = {
        value   = 0.05
        min     = 0.01
        max     = 5.0
        step    = 0.01
        format  = "{0:0.00}"
    }
                
    let mk projection geometry color thickness surfaceName : Annotation =
        {
             version     = Annotation.current
             key         = Guid.NewGuid()
             geometry    = geometry
             semantic    = Semantic.None
             points      = plist.Empty
             segments    = plist.Empty //[]
             color       = color
             thickness   = thickness
             results     = None
             dnsResults  = None            
             projection  = projection
             visible     = true
             text        = ""
             textsize    = texts
             modelTrafo  = Trafo3d.Identity
             showDns     = 
                 match geometry with 
                 | Geometry.DnS -> true 
                 | _ -> false 
             surfaceName  = surfaceName
             semanticId   = SemanticId ""
             semanticType = SemanticType.Undefined
             view         = FreeFlyController.initial.view
        }

    let initial =
        mk Projection.Viewpoint Geometry.Polyline { c = C4b.Magenta } thickn ""
      
    let thickness = [1.0; 2.0; 3.0; 4.0; 5.0; 1.0; 1.0]
    let color = [new C4b(241,238,246); new C4b(189,201,225); new C4b(116,169,207); new C4b(43,140,190); new C4b(4,90,141); new C4b(241,163,64); new C4b(153,142,195) ]
    
    let make (projection) (geometry) (color) (thickness) (surfName) : Annotation  =       
        {
            version     = Annotation.current
            key         = Guid.NewGuid()
            geometry    = geometry
            semantic    = Semantic.None
            points      = plist.Empty
            segments    = plist.Empty //[]
            color       = color
            thickness   = thickness
            results     = None
            dnsResults  = None
            projection  = projection
            visible     = true
            text        = ""
            textsize    = texts
            modelTrafo  = Trafo3d.Identity
            showDns     = 
                match geometry with 
                | Geometry.DnS -> true 
                | _ -> false
            surfaceName = surfName
            view         = FreeFlyController.initial.view
            semanticId   = SemanticId ""
            semanticType = SemanticType.Undefined
        }
