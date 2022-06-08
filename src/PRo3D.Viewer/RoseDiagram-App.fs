namespace Pro3D.AnnotationStatistics
open System
open Aardvark.UI

module RoseDiagram_App =  

    let update (m:RoseDiagramModel) (action:RoseDiagramModelAction) =
        match action with        

        | UpdateRD data -> 
            let updatedBins = RoseDiagramModel.sortRoseDiagramDataIntoBins m.bins data m.binAngle.value
            let max = BinModel.getBinMaxValue updatedBins
            let avgAngle = RoseDiagramModel.calculateAvgAngle (data |> List.map(fun (_,d) -> d))
            {m with bins = updatedBins; maxBinValue = max; avgAngle = avgAngle}

        | SetBinAngle act ->
            let ud_angle = Numeric.update m.binAngle act
            let bins = RoseDiagramModel.initRoseDiagramBins ud_angle.value
            let updatedBins = RoseDiagramModel.sortRoseDiagramDataIntoBins bins m.data ud_angle.value
            let max = BinModel.getBinMaxValue updatedBins
            {m with bins = updatedBins; maxBinValue = max; binAngle = ud_angle}

        | EnterRDBin id ->
            {m with hoveredBin = Some(id)}

        | ExitRDBin ->
            {m with hoveredBin = None}

