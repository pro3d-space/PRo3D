namespace PRo3D

open System
open System.Xml.Linq
open System.Xml

open Aardvark.Base
open Aardvark.UI

open PRo3D.Base.Annotation

module MeasurementsImporter = 
    open Aardvark.UI.Primitives
   
    let xname s = XName.Get(s)

    let thickness (a:XElement) = {
        value = (float)(a.Element(xname "object").Element(xname "LineThickness").Value)
        min = 0.5
        max = 10.0
        step = 0.1
        format = ""
        }

    let textsize  = {
        value = 0.05
        min = 0.01
        max = 5.0
        step = 0.01
        format = ""
        }

    let getStyle (a:XElement) = {
        color = C4b.Parse(a.Element(xname "object").Element(xname "Color").Value)
        thickness = thickness a
        }

    let getTrafo (t:XElement) = 
        let strafo = 
          t.Value.NestedBracketSplitLevelOne()
            |> Seq.map M44d.Parse
            |> List.ofSeq

        Trafo3d(strafo.[0], strafo.[1])        

    let parsePointsLevelOne (points:XElement) = 
        points.Value.NestedBracketSplitLevelOne()
            |> Seq.map V3d.Parse
            |> Seq.toArray
      
    let parsePointsLevelZero (points:XElement) = 
        points.Value.NestedBracketSplit(0) 
            |> Seq.map V3d.Parse
            |> Seq.toArray
           
    let getPoints (points:XElement) (aType:string) =
      match (points,aType) with
        | (null,_) -> Array.empty
        | (_, "Exomars.Base.Geology.Point") -> points.Value |> V3d.Parse |> Array.singleton
        | (_, "Exomars.Base.Geology.Line")  -> parsePointsLevelOne points
        | (_,_) -> parsePointsLevelZero points
            
      
    let segmentOfArray (points : array<_>) = 
      if points.IsEmpty() then None
      else
        Some { startPoint = points.[0]; endPoint = points.[points.Length - 1]; points = PList.ofArray points }
                        
    let parseSegments (segments:XElement) = 
      segments.Elements(xname "V3d_Array")
        |> Seq.map (fun x -> x |> parsePointsLevelZero |> segmentOfArray)
        |> Seq.toList

    let parseSegment (seg:XElement) = 
        parsePointsLevelZero seg |> segmentOfArray 
        
    let getSegments (m:XElement, aType:string) = 
      let segments = 
        match aType with
          |"Exomars.Base.Geology.Line" ->
              let segment = m.Element(xname "Segment")
              match segment.FirstAttribute with
                  | null -> []
                  | _ -> [parseSegment segment]
          | "Exomars.Base.Geology.Polyline"  | "Exomars.Base.Geology.DipAndStrike" -> 
              let segments = m.Element(xname "Segments")
              match segments with
                  | null -> []
                  | _ -> parseSegments segments
          | _ -> []
      List.choose id segments // drop all empty segments (segments being None)


    let getGeometry (aType:string, closed:bool) = 
      let geometry = 
        if closed then 
          Geometry.Polygon
        else 
          match aType with
            |"Exomars.Base.Geology.Point"           -> Geometry.Point
            |"Exomars.Base.Geology.Line"            -> Geometry.Line
            |"Exomars.Base.Geology.Polyline"        -> Geometry.Polyline
            |"Exomars.Base.Geology.DipAndStrike"    -> Geometry.DnS
            |_ -> Geometry.Point
      geometry

    let getAnnotation (t: Trafo3d) (m:XElement) = 
        let anType = (m.Attribute(xname "type").Value.ToString().Split ',').[0]
        let closed = 
          match m.Element(xname "Closed") with
            | null -> false
            | _ -> m.Element(xname "Closed").Value.ToBool()

        let style = (getStyle (m.Element(xname "LineAttributes")))
        let color = new C4b(style.color.R, style.color.G, style.color.B)
        let id    = (m.Element(xname "Id")).Value |> Guid.Parse
        let trafo = getTrafo (m.Element(xname "LocalToGlobal")) * t

        let points = 
          getPoints (m.Element(xname "Points")) anType 
            |> PList.ofArray 
            |> PList.map trafo.Forward.TransformPos

        let segments = 
          getSegments(m, anType)
            |> PList.ofList 
            |> PList.map (
                fun s -> 
                    let points     = s.points     |> PList.map trafo.Forward.TransformPos
                    let startPoint = s.startPoint |> trafo.Forward.TransformPos
                    let endPoint   = s.endPoint   |> trafo.Forward.TransformPos
                    {s with startPoint = startPoint; endPoint = endPoint; points = points}
            )

        Log.line "TrafoImporter: Found %A in xml" id

        let an = {
                version = Annotation.current
                key = id
                geometry = getGeometry (anType, closed)
                projection = Projection.Linear
                semantic = Semantic.Horizon0
                points = points
                segments = segments
                color = { c = color }
                thickness = style.thickness
                results = None
                dnsResults = None
                modelTrafo = trafo 
                visible = true 
                showDns = false
                text = ""
                textsize = textsize
                surfaceName = ""
                view = FreeFlyController.initial.view
                semanticId = SemanticId ""
                semanticType = SemanticType.Undefined
        }
        an
        

    type XmlReader with
    /// Returns a lazy sequence of XElements matching a given name.
        member reader.StreamElements(name, ?namespaceURI) =
            let readOp =
                match namespaceURI with
                | None    -> fun () -> reader.ReadToFollowing(name)
                | Some ns -> fun () -> reader.ReadToFollowing(name, ns)
            seq {
                while readOp() do
                    match XElement.ReadFrom reader with
                    | :? XElement as el -> yield el
                    | _ -> ()
            }

    let startImporter (path:string) : plist<Annotation> =
        let reader = XmlReader.Create path
        let measurements = reader.StreamElements("Measurements").Elements(xname "object")
        let annotations = 
            measurements 
            |> Seq.map (fun x -> getAnnotation Trafo3d.Identity x)
            |> PList.ofSeq  
        annotations


    type Action =
        | Import    of string
        
    let update (model : MeasurementsImporterModel) (act : Action) =
        match act with
        | Import s         -> { model with annotations = startImporter s }