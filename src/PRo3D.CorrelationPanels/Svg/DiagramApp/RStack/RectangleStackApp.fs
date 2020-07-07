namespace Svgplus

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI
open Svgplus.RectangleType // always import this before importing Svgplus, so correct Lenses are used //TODO
open Svgplus.RectangleStackTypes
open Svgplus

open CorrelationDrawing

type RectangleStackAction =
    | SelectBorder     of RectangleBorderId * bool //overwrite
    | DeselectBorder
    | RectangleMessage of (RectangleId * RectangleAction)
    | ResetPosition    of V2d
    | UpdatePosition   of V2d
    | UpdateColour     of (Rectangle -> Rectangle)
    | SetYScaling      of float
    | UpdateXSizes     of (float -> float)
    | FixWidthTo       of float
    | Delete
    | Nop

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module RectangleStackApp =
   
    module Lens =
        let pos =
            { new Lens<RectangleStack, Aardvark.Base.V2d>() with
                override x.Get(s) = s.pos
                override x.Set(s,v) =
                    let _rectangles = 
                      s.rectangles 
                        |> HMap.map (fun id r -> 
                            let _x = v.X
                            let _y = v.Y + r.pos.Y
                            let _v = V2d (_x, _y)
                            Rectangle.Lens.pos.Set (r, _v)
                        )
                        
                    { s with rectangles = _rectangles; pos = v }
            }
    
    let calcStackDim rectangles = 
        rectangles 
        |> HMap.values 
        |> Seq.fold (fun (s:V2d) (e:Rectangle) -> V2d(max s.X e.maxWidth, s.Y + e.dim.height)) V2d.Zero

    //TODO TO pure magic happening here, mapPrev' irre
    let stack (model : RectangleStack) =
        match model.order with
        | [] -> model  
        | _ ->
            let clean = 
                model.rectangles
                |> HMap.map (fun _ r -> Rectangle.Lens.pos.Set (r, V2d 0.0))
              
            //compute rectangle positions according to order and height
            let f (prev : Rectangle) (curr : Rectangle) =
                let cposy = prev.pos.Y + prev.dim.height
                Rectangle.Lens.posY.Set (curr, cposy)
            
            let rs = 
                DS.PList.mapPrev' (model.order |> PList.ofList) clean None f
            
            {
                model with  
                    rectangles = rs
                    stackDimensions = calcStackDim rs
            }
    
    let create id rmap order borders (nativeRange : Range1d) yScale stackType : RectangleStack =

        let rectangleStack = 
            {
                id              = id
                rectangles      = rmap
                order           = order
                borders         = borders
                selectedBorder  = None
                pos             = V2d.Zero
                yAxisMargin     = 20.0
                stackDimensions = calcStackDim rmap
                stackRange      = nativeRange
                yToSvg          = yScale
                stackType       = stackType
            }
        
        rectangleStack |> stack
    
    let resetPosition (model : RectangleStack) (v : V2d) =
        let _rectangles = 
             model.rectangles 
            |> HMap.map (fun id r -> Rectangle.Lens.pos.Set (r, v))
        {
            model with  
                rectangles     = _rectangles
                pos            = v
        } |> stack
    
    let update (model : RectangleStack) (action : RectangleStackAction) =

        let updateRect (optr : option<Rectangle>) (m : RectangleAction) =
            match optr with
            | Some r -> Svgplus.Rectangle.update r m
            | None   -> Rectangle.init (RectangleId.createNew ())    
        
        match action with 
        | ResetPosition v ->
            resetPosition model v    
        | UpdatePosition v -> Lens.pos.Set (model, v)
        | Delete           -> model
        | RectangleMessage msg ->
            let (id, m) = msg
            let _rects = 
                model.rectangles 
                |> HMap.update id (fun x -> updateRect x m)
            { 
                model with 
                    rectangles = _rects
            }
        | UpdateColour cmap ->
            let _rects =
                model.rectangles
                |> HMap.map (fun id r -> 
                    Rectangle.update r (RectangleAction.UpdateColour cmap))
            { model with rectangles = _rects}
        | SetYScaling scale ->
            let _rects =
                model.rectangles
                |> HMap.map (fun id r -> 
                    let newHeight = 
                        match r.fixedInfinityHeight with
                        | Some fixedHeight -> r.worldHeight * scale + fixedHeight
                        | None -> r.worldHeight * scale
                    Rectangle.Lens.height.Set(r, newHeight))
            { model with rectangles = _rects; stackDimensions = calcStackDim _rects; yToSvg = scale }
        | UpdateXSizes f ->
            let _rects =
                model.rectangles
                |> HMap.map (fun id r -> Rectangle.Lens.width.Update (r,f))
            
            { model with rectangles = _rects; stackDimensions = calcStackDim _rects }
        | FixWidthTo d ->
            let _rects =
                model.rectangles
                |> HMap.map (fun id r -> {r with fixedWidth = Some d})
            {model with rectangles = _rects; stackDimensions = calcStackDim _rects}
        | SelectBorder (rectangleBorderId, overwrite) ->
            match model.selectedBorder with
            | Some selected ->
                if selected = rectangleBorderId && (not overwrite) then
                    { model with selectedBorder = None } //deselect
                else
                    { model with selectedBorder = (Some (rectangleBorderId)) } //change selection
            | None ->
                { model with selectedBorder = (Some (rectangleBorderId))}
        | DeselectBorder ->
            { model with selectedBorder = None }
        | Nop ->
            model
      
    let update' (action : RectangleStackAction) (model : RectangleStack) =
        update model action

    let view (stacksMaxMinRanges : IMod<Range1d>) (flattenHorizon: IMod<option<FlattenHorizonData>>) (model : MRectangleStack) =
    
        let viewMap = 
            Svgplus.Rectangle.view >> UIMapping.mapAListId  
        
        let borders = 
            model.borders
            |> AMap.toASet
            |> ASet.collect(fun (_,x) ->
                Rectangle.viewBorder x model.rectangles false (fun _ -> SelectBorder(x.id, false))
                |> ASet.ofModSingle
            ) 

        let content =
            
            let aOrder = 
                model.order 
                |> Mod.map(fun x -> x |> PList.ofList)
                |> AList.ofMod

            alist {
                for id in aOrder do
                    let! r = AMap.find id model.rectangles 
                    yield! (viewMap r id RectangleMessage)

                let! selected = model.selectedBorder
                match selected with
                | Some borderId ->
                    let! border = model.borders |> AMap.find borderId
                    yield! Rectangle.viewBorder border model.rectangles true (fun _ -> Nop) |> AList.ofModSingle
                | None -> ()

                yield! borders |> AList.ofASet
            }

        let dependencies = 
            [
                { kind = Script; name = "d3"; url = "https://d3js.org/d3.v5.min.js" }
                { kind = Script; name = "d3_axis"; url = "d3_axis.js" }
            ]
        
        let calcMaxGrainSize = 
            model.rectangles 
            |> AMap.toMod
            |> Mod.bind (fun x ->
                Mod.custom (fun t -> 
                    x.Values 
                    |> Seq.map (fun e -> e.grainSize.GetValue(t).middleSize) 
                    |> Seq.max))

        let xAxis = 
            let updateChart = "data.onmessage = function (data) { drawScaleX('__ID__',data); };"

            let xAxisPosition = 
                adaptive {
                    let! yAxisMargin = model.yAxisMargin
                    let! stackPos = model.pos
                    let! stackDim = model.stackDimensions
                    let! maxGrainSize = calcMaxGrainSize

                    let stackHeight = stackDim.OY
                    let marginLeft = V2d(yAxisMargin,0.0)
                    let magic = V2d(47.0,0.0)                      // magic-margin?! TODO!

                    let lowerLeft = stackPos + stackHeight + marginLeft + magic

                    let minRange = max 125.0 (stackDim.X-79.0) // magic -79?? (results of "invisible labels"...( which are always 67.0 + ?? )
                    let minGrainSize = max 0.003 maxGrainSize

                    let range = V2d(0.0, minRange)    
                    let domain = V2d(1.0, minGrainSize * 1000000.0) // in μm

                    return [ lowerLeft; domain; range]
                }

            require dependencies (
                onBoot' ["data", xAxisPosition |> Mod.channel] updateChart (Incremental.Svg.g AttributeMap.empty AList.empty))
            |> AList.single

        let yAxis = 
            let updateChart = "data2.onmessage = function (data) { drawScaleY('__ID__',data); };"

            let yAxisPosition = 
                adaptive {
                    let! yAxisMargin = model.yAxisMargin
                    let! stackPos = model.pos
                    let! stackDim = model.stackDimensions
                    let! dom = model.stackRange
                    let! otherRanges = stacksMaxMinRanges
                    let infBlock = 10.0 // CAUTION FIXED VALUE model.yToSvg    // CAUTION only valid if infBlock is 1 unit!
                    let marginLeft = V2d(yAxisMargin,0.0)
                    let magic = V2d(47.0,0.0)                      // magic-margin?! TODO!
                    let shiftToSecondaryLogSide = (V2d(-15.0, 0.0)) //MAGIC
                    let lowerLeft = stackPos + marginLeft + magic + V2d(0.0, infBlock) + shiftToSecondaryLogSide
                    let range = V2d(0.0, stackDim.Y-(2.0*infBlock)) // range without first and last block -> infinity-blocks
                    let domainNormalized = V2d(dom.Min-otherRanges.Min, dom.Max-otherRanges.Min)
                    
                    let! flattenHorizon = flattenHorizon
                    let shiftedDomain =
                        match flattenHorizon with
                        | Some x -> domainNormalized - V2d(domainNormalized.Y - x.offsetFromStackTop) // shifts scale to reference correlation origin
                        | None -> domainNormalized

                    return [lowerLeft; shiftedDomain; range]
                }

            require dependencies (
                onBoot' ["data2", yAxisPosition |> Mod.channel] updateChart (Incremental.Svg.g AttributeMap.empty AList.empty))
            |> AList.single

        let lst = 
            match model.stackType with
            | Primary -> 
                content 
                |> AList.append xAxis
                |> AList.append yAxis
            | Secondary -> 
                content
         
        Incremental.Svg.g AttributeMap.empty lst