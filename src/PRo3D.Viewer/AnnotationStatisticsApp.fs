namespace Pro3D.AnnotationStatistics

open System
open PRo3D.Base
open PRo3D.Base.Annotation
open Aardvark.Base
open Aardvark.UI
open PRo3D.Core
open FSharp.Data.Adaptive

type BinAction =
    | Update of float 

type HistogramAction =    
    | UpdateData of List<float>    
    | SetBinNumber of Numeric.Action
    | SetDomainMin of Numeric.Action
    | SetDomainMax of Numeric.Action    

//TODO
type RoseDiagramAction =
    | UpdateBinCount
    
type VisualizationAction = 
    | UpdateHistogram of HistogramAction
    | UpdateRoseDiagram of RoseDiagramAction

type PropertyAction =    
    | UpdateStats of List<float>
    | UpdateVisualization of VisualizationAction

type AnnoStatsAction =
    | SetSelected of Guid * GroupsModel
    | SetProperty of Prop
    | UpdateProperty of PropertyAction * Prop



module BinOperations =

    let update (m:Bin) (action:BinAction) =
        match action with
        | Update v -> let inside = v >= m.start && v <= m.theEnd
                      match inside with
                      | true -> {m with value = m.value + 1}
                      | false -> m

module RoseDiagramOperations = 
    let update (m:RoseDiagram) (action:RoseDiagramAction) =

        match action with

        | UpdateBinCount -> let updatedBins = AnnotationStatistics.calculateBinCount m.bins m.data m.binAngle
                            let max = AnnotationStatistics.getBinMaxValue updatedBins
                            {m with bins = updatedBins; maxBinValue = max}
    

    
    
   


        



        



module HistogramOperations =     

    let private compute (m:(Pro3D.AnnotationStatistics.Histogram)) =
        let numBins = m.numOfBins.value
        let start = m.domainStart.value
        let theEnd = m.domainEnd.value
        let width = (theEnd-start) / numBins
        let bins = AnnotationStatistics.createBins [] (int(numBins)) 0 start width m.data 
        let maxValue = AnnotationStatistics.getBinMaxValue bins
        {m with bins = bins; maxBinValue = maxValue}


    let update (m:(Pro3D.AnnotationStatistics.Histogram)) (action:HistogramAction) =
        match action with

        | UpdateData d -> let updatedBins = m.bins |> List.map (fun b -> BinOperations.update b (Update d.Head))
                          let maxValue = AnnotationStatistics.getBinMaxValue updatedBins                         
                          compute {m with data = (m.data @ d); bins = updatedBins; maxBinValue = maxValue}  

        | SetBinNumber act -> let ud_n = Numeric.update m.numOfBins act
                              let ud_hist = {m with numOfBins = ud_n}
                              compute ud_hist  
                              

        | SetDomainMin act -> let ud_min = Numeric.update m.domainStart act
                              let ud_hist = {m with domainStart = ud_min}
                              compute ud_hist  

        | SetDomainMax act -> let ud_max = Numeric.update m.domainEnd act
                              let ud_hist = {m with domainEnd = ud_max}
                              compute ud_hist 
                              
module VisualizationOperations =
    let update (v:Visualization) (act:VisualizationAction) =
        match (v,act) with
        | Histogram h, UpdateHistogram ha -> Visualization.Histogram (HistogramOperations.update h ha)
        | RoseDiagram r, UpdateRoseDiagram ra -> Visualization.RoseDiagram (RoseDiagramOperations.update r ra)
        | _ -> failwith "this is not a valid combination of visualization and vis action" //TODO there has to be a better way than this
        

module PropertyOperations =

    let private calcMinMaxAvg (l:List<float>) =
        match (l.IsEmpty) with
        | true -> (0.0, 0.0, 0.0)        
        | false -> let min = l |> List.min
                   let max = l |> List.max
                   let avg = l |> List.average
                   (min, max, avg)                   
                   
    

    let update (m:Property) (action:PropertyAction) =
        match action with       
        | UpdateStats d -> let updatedData = m.data @ d
                           let min,max,avg = calcMinMaxAvg updatedData
                           {m with data = updatedData; min = min; max = max; avg = avg}
        
        | UpdateVisualization visAction -> let updatedVis = VisualizationOperations.update m.visualization visAction
                                           {m with visualization = updatedVis}
    

module AnnotationStatisticsApp =     

    let private calcMinMaxAvg (l:List<float>) =
           match (l.IsEmpty) with
           | true -> (0.0, 0.0, 0.0)        
           | false -> let min = l |> List.min
                      let max = l |> List.max
                      let avg = l |> List.average
                      (min, max, avg)                      
                      
     

    let getPropData (prop:Prop) (selected:List<Annotation>) =

        match prop.kind with
            | Kind.LENGTH -> selected 
                                   |> List.map(fun a -> match a.results with
                                                           | Some r -> Some(r.length)
                                                           | None -> None
                                               )
                                   |> List.choose(fun o -> o)
                                   

            | Kind.BEARING -> selected 
                                    |> List.map(fun a -> match a.results with
                                                            | Some r -> Some(r.bearing)
                                                            | None -> None
                                                )
                                    |> List.choose(fun o -> o)
                                    
            
            | Kind.VERTICALTHICKNESS -> selected 
                                                |> List.map(fun a -> match a.results with
                                                                        | Some r -> Some(r.verticalThickness)
                                                                        | None -> None
                                                            )
                                                |> List.choose(fun o -> o)

            | Kind.DIP_AZIMUTH -> selected 
                                                |> List.map(fun a -> match a.dnsResults with
                                                                        | Some r -> Some(r.dipAzimuth)
                                                                        | None -> None
                                                            )
                                                |> List.choose(fun o -> o)

            | Kind.STRIKE_AZIMUTH -> selected 
                                                |> List.map(fun a -> match a.dnsResults with
                                                                        | Some r -> Some(r.strikeAzimuth)
                                                                        | None -> None
                                                            )
                                                |> List.choose(fun o -> o)
            

    //when a new annotation is added
    let updateAllProperties (props:HashMap<Prop, Property>) (addedAnnotation:Annotation) =   
        
        props |> HashMap.map (fun k v -> 
                                let data = getPropData k [addedAnnotation]
                                if (data.IsEmpty) then v 
                                else
                                    let p1 = PropertyOperations.update v (UpdateStats data)
                                    match k.scale with
                                    | Scale.Metric -> PropertyOperations.update p1 (UpdateVisualization (UpdateHistogram (UpdateData data)))
                                    | Scale.Angular -> PropertyOperations.update p1 (UpdateVisualization (UpdateRoseDiagram UpdateBinCount))

        )               


   

    let update (m:AnnotationStatisticsModel) (a:AnnoStatsAction) =
        match a with
        | SetSelected (id, g) ->         
            
            match (g.flat |> HashMap.tryFind id) with
            | Some l -> let anno = Leaf.toAnnotation l
                        match (m.selectedAnnotations |> HashMap.tryFind id) with
                        | Some _ -> m
                        | None -> let updatedAnnos = m.selectedAnnotations.Add (id,anno)
                                  let updatedProperties = updateAllProperties m.properties anno
                                  {m with selectedAnnotations = updatedAnnos; properties = updatedProperties}
            | None -> m

        
        | SetProperty prop ->    
                                match (m.properties.ContainsKey prop) with
                                | true -> m
                                | false -> let d = getPropData prop (m.selectedAnnotations |> HashMap.toValueList)
                                           let min, max, avg = calcMinMaxAvg d     
                                           let initialVis = 
                                            match prop.scale with
                                            | Scale.Metric -> Visualization.Histogram (AnnotationStatistics.initHistogram min max d)
                                            | Scale.Angular -> Visualization.RoseDiagram (AnnotationStatistics.initRoseDiagram d)
                                           let property = {
                                                            prop = prop
                                                            data = d 
                                                            min = min
                                                            max = max
                                                            avg = avg                                                 
                                                            visualization = initialVis
                                                           }
                                           let properties = m.properties.Add (prop, property)
                                           {m with properties = properties}

                    
        
        | UpdateProperty (act, prop) -> 
            match act with
            | UpdateStats d -> match (m.properties.TryFind prop) with
                                | Some p -> let updatedProp = PropertyOperations.update p act
                                            let updatedPropList = m.properties |> HashMap.alter prop (function None -> None | Some _ -> Some updatedProp)
                                            {m with properties = updatedPropList}                                    
                        
                                | None -> m 

            | UpdateVisualization visAction -> match (m.properties.TryFind prop) with
                                                | Some p -> let updatedVis = VisualizationOperations.update p.visualization visAction
                                                            let updatedProp = {p with visualization = updatedVis}
                                                            let updatedPropList = m.properties |> HashMap.alter prop (function None -> None | Some _ -> Some updatedProp)
                                                            {m with properties = updatedPropList}
                                                | None -> m
                
  
    
      
   
                


    let propDropdown =         

        div [ clazz "ui menu"; style "width:150px; height:20px;padding:0px; margin:0px"] [
            onBoot "$('#__ID__').dropdown('on', 'hover');" (
                div [ clazz "ui dropdown item"; style "width:100%"] [
                    text "Properties"
                    i [clazz "dropdown icon"; style "margin:0px 5px"][] 
                    div [ clazz "ui menu"] [
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetProperty (AnnotationStatistics.initProp Kind.LENGTH Scale.Metric))] [text "Length"]
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetProperty (AnnotationStatistics.initProp Kind.BEARING Scale.Metric))] [text "Bearing"]
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetProperty (AnnotationStatistics.initProp Kind.VERTICALTHICKNESS Scale.Metric))] [text "Vertical Thickness"] 
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetProperty (AnnotationStatistics.initProp Kind.DIP_AZIMUTH Scale.Angular))] [text "Dip Azimuth"] 
                        div [clazz "ui inverted item"; onMouseClick (fun _ -> SetProperty (AnnotationStatistics.initProp Kind.STRIKE_AZIMUTH Scale.Angular))] [text "Strike Azimuth"] 
                    ]
                ]
            )
        ] 

    let propListing (prop:AdaptiveProperty) = 

        require GuiEx.semui (
            Html.table [                                    
                Html.row "Minimum" [Incremental.text (prop.min |> AVal.map (fun f -> sprintf "%.2f" f))]
                Html.row "Maximum" [Incremental.text (prop.max |> AVal.map (fun f -> sprintf "%.2f" f))]
                Html.row "Average" [Incremental.text (prop.avg |> AVal.map (fun f -> sprintf "%.2f" f))]
            ]
          )
     

module AnnotationStatisticsDrawings =

    //for rose diagram
    let drawCircle (center:V2d) (radius:float) =
        Svg.circle(
            [
                style "stroke:white;stroke-width:2"
                attribute "cx" (sprintf "%f" center.X)      
                attribute "cy" (sprintf "%f" center.Y) 
                attribute "r" (sprintf "%f" radius)                 

            ]
        )

    let pointFromAngle (start : V2d) (angle : float) (length : float) =
           let endX = start.X + Math.Cos(angle) * length
           let endY = start.Y + Math.Sin(angle) * length
           new V2d (endX, endY)

    let drawRoseDiagramSection (startAngle:float) (endAngle:float) (center:V2d) (innerRad:float) (outerRad:float) =
        //polygon points
        let innerStart = pointFromAngle center startAngle innerRad
        let innerEnd = pointFromAngle center endAngle innerRad
        let outerStart = pointFromAngle center startAngle outerRad
        let outerEnd = pointFromAngle center endAngle outerRad

        //polygon path
        //two lines and two circle segments
        let p1 = new V2i(int(innerStart.X), int(innerStart.Y))
        let p2 = new V2i(int(outerStart.X), int(outerStart.Y))
        let p3 = new V2i(int(outerEnd.X), int(outerEnd.Y))
        let p4 = new V2i(int(innerEnd.X), int(innerEnd.Y))
        let c1 = int(outerRad)
        let c2 = int(innerRad)

        Svg.path [
            attribute "d" (sprintf "M %d %d L %d %d A %d %d 0 0 1 %d %d L %d %d A %d %d 0 0 0 %d %d"
                 p1.X p1.Y p2.X p2.Y c1 c1 p3.X p3.Y p4.X p4.Y c2 c2 p1.X p1.Y 
            )
            attribute "fill" "green"
        ]


    let drawRoseDiagram (r:AdaptiveRoseDiagram) =
           //two circles as outline
           //individual bin sections

           let attrSVG =
                         [   
                             style "position:relative"                     
                             attribute "width" "100%" 
                             attribute "height" "200px"               
                             
                         ]|> AttributeMap.ofList


           let sect = alist{
                            let! center = r.center
                            let! innerRad = r.innerRad
                            let! outerRad = r.outerRad
                            yield drawCircle center innerRad
                            yield drawCircle center outerRad

                            let! bins = r.bins
                            let areaInner = Constant.Pi * Fun.PowerOfTwo(innerRad)
                            let areaOuter = Constant.Pi * Fun.PowerOfTwo(outerRad)
                            let areaTotal = areaOuter - areaInner
                            let! maxBinValue = r.maxBinValue

                            let sections = bins |> List.map(fun b -> 
                                    let subArea = (areaTotal / ((float(maxBinValue))/(float(b.value)))) + areaInner
                                    let subOuterRadius = Fun.Sqrt(subArea / Constant.Pi)
                                    drawRoseDiagramSection b.start b.theEnd center innerRad subOuterRadius
                            )
                            yield! sections


                        }

           Incremental.Svg.svg attrSVG sect
          



    let drawHistogram (h: AdaptiveHistogram) (width:int) = 
        
        let height = 10
        let offSetY = 100
        let padLR = 10
        let pad = 5
        let xStart = 15
         

        let attrRects (bin:Bin) (idx:int)= 
            amap{
                let! n = h.numOfBins.value
                let binV = bin.value
                let w = (width-padLR*2) / (int(n))                
                
                yield style "fill:green;fill-opacity:1.0"                
                yield attribute "x" (sprintf "%ipx" (xStart + idx * (w+pad)))
                yield attribute "y" (sprintf "%ipx" ((height-(binV*10)+offSetY)))
                yield attribute "width" (sprintf "%ipx" w) 
                yield attribute "height" (sprintf "%ipx" (binV*10)) 
            } |> AttributeMap.ofAMap       

        let attrText (bin:Bin) (idx:int)= 
            amap{
                let! n = h.numOfBins.value                
                let w = (width-padLR*2) / (int(n))     
                let x = (xStart + idx * (w+pad))
                let y = height+(offSetY+40)
                let strRot = "rotate(-55," + x.ToString() + "," + y.ToString() + ")"
                yield style "font-size:8px; fill:white; position:center"
                yield attribute "x" (sprintf "%ipx" x)
                yield attribute "y" (sprintf "%ipx" y)
                yield attribute "transform" strRot
            } |> AttributeMap.ofAMap 

        let attrSVG =
            [   
                style "position:relative"                     
                attribute "width" "100%" 
                attribute "height" "200px"               
                
            ]|> AttributeMap.ofList

       
        let attrLine =
            amap{
                    let! maxBinValue = h.maxBinValue
                    yield style "stroke:white;stroke-width:2"
                    yield attribute "x1" "10px"
                    yield attribute "y1" (sprintf "%ipx" ((height+offSetY)))
                    yield attribute "x2" "10px"
                    yield attribute "y2" (sprintf "%ipx" ((height-(maxBinValue*10)+offSetY)))

            }|> AttributeMap.ofAMap 

        let attrTickLine (y:int) =
            amap{
                yield style "stroke:green; stroke-opacity:0.3"               
                yield attribute "x1" "10px"
                yield attribute "y1" (sprintf "%ipx" y)
                yield attribute "x2" (sprintf "%ipx" (xStart+width+pad))
                yield attribute "y2" (sprintf "%ipx" y)

            }|> AttributeMap.ofAMap 

        let attrTickLabel (y:int) =
            amap{
                yield style "font-size:8px; fill:white"
                yield attribute "x" "0px"
                yield attribute "y" (sprintf "%ipx" y)                
            } |> AttributeMap.ofAMap 

       
        let rectangles =             

            alist{               

                let! bins = h.bins

                //bins as rectangles + labels
                for i in 0..(bins.Length-1) do
                    let bin = bins.Item i
                    let label = sprintf "%i-%i" (int(bin.start)) (int(bin.theEnd))
                    yield Incremental.Svg.rect (attrRects bin i)
                    yield Incremental.Svg.text (attrText bin i) (AVal.constant label)
                
                //y axis
                yield Incremental.Svg.line attrLine

                //y axis ticks + labels
                let! maxBinValue = h.maxBinValue
                let y1 = height+offSetY
                let y2 = height-(maxBinValue*10)+offSetY
                let gap = (y2 - y1) / maxBinValue                
                for j in 0..maxBinValue do                                    
                    yield Incremental.Svg.line (attrTickLine (y1 + (j*gap)))
                    yield Incremental.Svg.text (attrTickLabel (y1 + (j*gap))) (AVal.constant (j.ToString()))

          
            }
        
        Incremental.Svg.svg attrSVG rectangles
    
    let histogramSettings (hist:AdaptiveHistogram) (p:AdaptiveProperty) =
           div [style "width:100%; margin: 0 0 5 0"][                
               text "Histogram Settings"
               Html.table[
                   Html.row "domain min" [Numeric.view' [InputBox] hist.domainStart |> UI.map SetDomainMin |> UI.map UpdateHistogram |> UI.map UpdateVisualization|> UI.map (fun a -> UpdateProperty (a, p.prop))]
                   Html.row "domain max" [Numeric.view' [InputBox] hist.domainEnd |> UI.map SetDomainMax |> UI.map UpdateHistogram |> UI.map UpdateVisualization |> UI.map (fun a -> UpdateProperty (a, p.prop))]
                   Html.row "number of bins" [Numeric.view' [InputBox] hist.numOfBins |> UI.map SetBinNumber |> UI.map UpdateHistogram |> UI.map UpdateVisualization |> UI.map (fun a -> UpdateProperty (a, p.prop))]
               ]
           ]
       
    let drawVisualization (p:AdaptiveProperty) =

             let v = 
                 alist{
                     let! vis = p.visualization
                     match vis with
                     | AdaptiveHistogram h -> yield histogramSettings h p
                                              yield drawHistogram h 200
                     | AdaptiveRoseDiagram r -> yield drawRoseDiagram r
                 }

             Incremental.div AttributeMap.empty v
           

          
        
       
                  
        
        
                   
        


        



        

    

            
            
           
                                                                        
           
        
        
       
            
            
           


        




                                  
                                                               
                                                               

        
                                  
            
            
            




