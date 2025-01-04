namespace PRo3D.Core

open System
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D
open PRo3D.Base
open PRo3D.Core.Surface

open Chiron

open Aether
open Aether.Operators

#nowarn "0686"

type Orientation = 
| Horizontal_cam    = 0 // right direction of camera view
| Vertical_cam      = 1 // up direction of camera view
| Sky_cam           = 2 // camera sky vector
| Horizontal_planet = 3 // reference systen plane parallel to right camera view
| Sky_planet        = 4 // up direction of reference system


type Pivot = //alignment
| Left   = 0
| Middle = 1
| Right  = 2

type Unit =
| Undefined = 0
| mm        = 1
| cm        = 2
| m         = 3
| km        = 4

[<ModelType>]
type scSegment = {
    startPoint : V3d
    endPoint   : V3d
    color      : C4b
}
//with
//    static member FromJson ( _ : scSegment) =
//        json {

//            let! startPoint = Json.read "startPoint"
//            let! endPoint = Json.read "endPoint"
//            let! color  = Json.read "color"

//            return {
//                startPoint = startPoint |> V3d.Parse
//                endPoint   = endPoint |> V3d.Parse
//                color      = color |> C4b.Parse
//            }
//        }

//    static member ToJson ( x : scSegment) =
//        json {
//            do! Json.write "startPoint" (x.startPoint.ToString())
//            do! Json.write "endPoint" (x.endPoint.ToString())
//            do! Json.write "color" (x.color.ToString())
//        }

//[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module scSegment = 

    let read0 =
        json {
            
            let! startPoint = Json.read "startPoint"
            let! endPoint = Json.read "endPoint"
            let! color  = Json.read "color"

            return {
                startPoint = startPoint |> V3d.Parse
                endPoint   = endPoint |> V3d.Parse
                color      = color |> C4b.Parse
            }
        }

type scSegment with
    static member FromJson ( _ : scSegment) =
        json {
            return! scSegment.read0
        }

    static member ToJson ( x : scSegment) =
        json {
            do! Json.write "startPoint" (x.startPoint.ToString())
            do! Json.write "endPoint" (x.endPoint.ToString())
            do! Json.write "color" (x.color.ToString())
        }

type ScaleRepresentation =
    | ScaleBar = 0
    | CoordinateFrame = 1

[<ModelType>]
type ScaleVisualization = {
    version         : int
    guid            : System.Guid
    name            : string

    text           : string
    textsize       : NumericInput
    textVisible    : bool
   
    isVisible       : bool
    position        : V3d    
    scSegments      : IndexList<scSegment>
    orientation     : Orientation
    alignment       : Pivot
    thickness       : NumericInput
    length          : NumericInput
    unit            : Unit //Formatting.Len //
    subdivisions    : NumericInput

    view            : CameraView
    transformation  : Transformations
    preTransform    : Trafo3d
    //direction       : V3d

    representation : ScaleRepresentation
}

[<ModelType>]
type ScaleBarDrawing = {
    orientation     : Orientation
    alignment       : Pivot
    thickness       : NumericInput
    length          : NumericInput
    unit            : Unit //Formatting.Len //
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ScaleBar =    
    
    let getDirectionVec 
        (orientation : Orientation) 
        (view : CameraView) =  

        match orientation with
        | Orientation.Horizontal_cam    -> view.Right
        | Orientation.Vertical_cam      -> view.Up
        | Orientation.Sky_cam           -> view.Sky
        |_                       -> view.Right

    let current = 0   
    let read0 =
        json {
            let! guid            = Json.read "guid"
            let! name            = Json.read "name"

            let! text           = Json.read "text"
            let! textSize       = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            let! textVisible    = Json.read "textVisible"

            let! isVisible       = Json.read "isVisible"
            let! position        = Json.read "position"
            //let! scSegments      = Json.read "scSegments"

            let! orientation     = Json.read "orientation"
            let! alignment       = Json.read "alignment"
            let! thickness       = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            let! length          = Json.readWith Ext.fromJson<NumericInput,Ext> "length"
            let! unit            = Json.read "unit"
            let! subdivisions    = Json.readWith Ext.fromJson<NumericInput,Ext> "subdivisions"
            
            let! (view : list<string>) = Json.read "view"
            
            let view = view |> List.map V3d.Parse
            let view = CameraView(view.[0],view.[1],view.[2],view.[3], view.[4])

            let! transformation  = Json.read "transformation"
            let! preTransform    = Json.read "preTransform"

            let orientation = orientation |> enum<Orientation>

            let! representation = 
                Json.tryRead "representation" 


            //let! direction        = Json.tryRead "direction"

            return 
                {
                    version         = current
                    guid            = guid |> Guid
                    name            = name
                    
                    text            = text
                    textsize        = textSize  
                    textVisible     = textVisible

                    isVisible       = isVisible
                    position        = position |> V3d.Parse
                    scSegments      = IndexList.empty //scSegments  |> Serialization.jsonSerializer.UnPickleOfString
                    orientation     = orientation
                    alignment       = alignment |> enum<Pivot>
                    thickness       = thickness     
                    length          = length   
                    unit            = unit |> enum<Unit> //Unit
                    subdivisions    = subdivisions

                    view            = view
                    transformation  = transformation
                    preTransform    = preTransform |> Trafo3d.Parse
                    representation  = representation |> Option.map enum<ScaleRepresentation> |> Option.defaultValue ScaleRepresentation.ScaleBar 
                }
        }

type ScaleVisualization with
    static member FromJson(_ : ScaleVisualization) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! ScaleBar.read0
            | _ -> 
                return! v 
                |> sprintf "don't know version %A  of ScaleBar"
                |> Json.error
        }
    static member ToJson(x : ScaleVisualization) =
        json {
            do! Json.write "version" x.version
            do! Json.write "guid" x.guid
            do! Json.write "name" x.name

            do! Json.write "text"    x.text
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "textsize" x.textsize
            do! Json.write "textVisible" x.textVisible    

            do! Json.write "isVisible" x.isVisible    
            do! Json.write "position" (x.position.ToString())
            //do! Json.write "scSegments"  (x.scSegments |> IndexList.toList )
            do! Json.write "orientation" (x.orientation |> int)
            do! Json.write "alignment" (x.alignment |> int)
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "thickness" x.thickness
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "length" x.length
            do! Json.write "unit" (x.unit |> int)
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "subdivisions" x.subdivisions

            let camView = x.view
            let camView = [camView.Sky; camView.Location; camView.Forward; camView.Up ; camView.Right] |> List.map(fun x -> x.ToString())      
            do! Json.write "view" camView
            do! Json.write "transformation" x.transformation  
            do! Json.write "preTransform" (x.preTransform.ToString())
            do! Json.write "representation" (int x.representation)
            //do! Json.write "direction" (x.direction.ToString())
        }


[<ModelType>]
type ScaleBarsModel = {
    version          : int
    scaleBars        : HashMap<Guid,ScaleVisualization>
    selectedScaleBar : Option<Guid> 
}

module ScaleBarsModel =
    
    let current = 0    
    let read0 = 
        json {
            let! scaleBars = Json.read "scaleBars"
            let scaleBars = scaleBars |> List.map(fun (a : ScaleVisualization) -> (a.guid, a)) |> HashMap.ofList

            let! selected     = Json.read "selectedScaleBar"
            return 
                {
                    version          = current
                    scaleBars        = scaleBars
                    selectedScaleBar = selected
                }
        }  
        
    let initial =
        {
            version          = current
            scaleBars        = HashMap.empty
            selectedScaleBar = None
        }

 
    
type ScaleBarsModel with
    static member FromJson (_ : ScaleBarsModel) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! ScaleBarsModel.read0
            | _ ->
                return! v
                |> sprintf "don't know version %A  of ScaleBarsModel"
                |> Json.error
        }

    static member ToJson (x : ScaleBarsModel) =
        json {
            do! Json.write "version"           x.version
            do! Json.write "scaleBars"        (x.scaleBars |> HashMap.toList |> List.map snd)
            do! Json.write "selectedScaleBar"  x.selectedScaleBar
        }

module InitScaleBarsParams =

    let translationInput = {
        value   = 0.000
        min     = -10000000.001
        max     = 10000000.000
        step    = 0.001
        format  = "{0:0.000}"
    }

    let yaw = {
        value   = 0.000
        min     = -180.001
        max     = +180.000
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
        yaw                  = yaw
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
        eulerMode            = EulerMode.defaultMode
    }

    let thickness = {
        value   = 0.03
        min     = 0.001
        max     = 10.0
        step    = 0.001
        format  = "{0:0.000}"
    }

    let length = {
        value   = 1.0
        min     = 0.0
        max     = 10000.0
        step    = 1.0
        format  = "{0:0}"
    }
    
    let text = {
        value   = 0.05
        min     = 0.001
        max     = 5.0
        step    = 0.001
        format  = "{0:0.000}"
    }

    let subdivisions = {
        value   = 5.0
        min     = 1.0
        max     = 1000.0
        step    = 1.0
        format  = "{0:0}"
    }

    let initialScaleBarDrawing = {
        orientation     = Orientation.Horizontal_cam
        alignment       = Pivot.Left
        thickness       = thickness
        length          = length
        unit            = Unit.m //Formatting.Len length.value
    }

   


   