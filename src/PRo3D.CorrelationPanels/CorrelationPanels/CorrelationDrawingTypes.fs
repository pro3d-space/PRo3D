namespace CorrelationDrawing.XXX

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Base.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives

open Svgplus
open UIPlus
open UIPlus.KeyboardTypes
open Svgplus.RectangleStackTypes
open Svgplus.RectangleType
open Svgplus.CameraType
open Svgplus.DiagramItemType
open CorrelationDrawing.SemanticTypes

open PRo3D.Base.Annotation

open CorrelationDrawing.Types
open CorrelationDrawing.AnnotationTypes
open CorrelationDrawing.SemanticTypes
open CorrelationDrawing.Model

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//FLAGS
type AppFlags =
  | None                  = 0b0000000000
  | ShowDebugView         = 0b0000000001
  

type SgFlags = 
  | None                  = 0b0000000000
  | TestTerrain           = 0b0000000001
  //| Correlations          = 0b0000000010
  //| TestTerrain           = 0b0000000100
  

type SvgFlags = 
  | None                  = 0b0000000000
  | BorderColour          = 0b0000000001
  | RadialDiagrams        = 0b0000000010 
  | Histograms            = 0b0000000100 
    //| EditLogNames          = 0b0000001000
  | LogLabels             = 0b0000100000 
  | XAxis                 = 0b0001000000 
  | YAxis                 = 0b0010000000 
  | EditCorrelations      = 0b0100000000 //TODO stretchLogs?

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

type CorrelationPlotViewType = LineView | LogView | CorrelationView

type XAxisFunction           = Average | Minimum | Maximum

[<ModelType>]
type RenderingParameters = {
    fillMode : FillMode
    cullMode : CullMode
}   


[<ModelType>]
type SvgOptions = {
  logPadding       : float
  logHeight        : float
  logMaxWidth      : float
  cpWidth          : float
  secLevelWidth    : float
  xAxisScaleFactor : float
  yAxisScaleFactor : float
  xAxisPadding     : float
  yAxisPadding     : float
  yAxisStep        : float
  axisWeight       : float

  //offset           : V2d //TODO might want to put into svgOptions
  //zoom             : SvgZoom //TODO might want to put into svgOptions
  //fontSize         : FontSize

}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
[<ModelType>]
type CorrelationDrawingModel = {
    keyboard          : Keyboard<CorrelationDrawingModel>
    hoverPosition     : option<Trafo3d>
    working           : option<Contact>
    projection        : Projection 
    //geometry          : GeometryType
    exportPath        : string
   // flags             : SgFlags
} with
  member self.isDrawing =
    self.keyboard.ctrlPressed


////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
type SaveType = Annotations | Semantics
type SaveIndex =
  {
    ind : int
  } with
    member this.next : SaveIndex =
      {ind = this.ind + 1}
    member this.filename (t : SaveType) : string =
      match t with
        | SaveType.Annotations ->
          sprintf "%03i_annotations.save" this.ind
        | SaveType.Semantics ->
          sprintf "%03i_semantics.save" this.ind

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SaveIndex =
  let init : SaveIndex =
    {ind = 0}
  let findSavedIndices () =
      System.IO.Directory.GetFiles( "./", "*.save")
        |> Array.toList
        |> List.filter (fun (str : string) ->
                          let (b, _) = System.Int32.TryParse str.[2..4] //TODO hardcoded
                          b
                       )
        |> List.map (fun str -> str.[2..4])
        |> List.map int
        |> List.distinct //TODO only if sem & anno
        |> List.map (fun i -> {ind = i})

  let nextAvaible () =
    let si = findSavedIndices ()
    match si with
      | [] -> init
      | si ->
        let max = 
          si |> List.map (fun i -> i.ind)
              |> List.max
        {ind = max}.next
      

[<ModelType>]
type Pages = 
    {
        [<NonAdaptive>]
        past          : Option<Pages>

        saveIndices   : list<SaveIndex>

        keyboard      : Keyboard<Pages>
        
        [<NonAdaptive>]
        future        : Option<Pages>

        appFlags      : AppFlags
        sgFlags       : SgFlags

        camera        : CameraControllerState
        rendering     : RenderingParameters
        cullMode      : CullMode
        fill          : bool
        dockConfig    : DockConfig

        drawingApp    : CorrelationDrawingModel
        //annotationApp : AnnotationApp
        semanticApp   : SemanticsModel
        corrPlot      : CorrelationPlotModel
    }