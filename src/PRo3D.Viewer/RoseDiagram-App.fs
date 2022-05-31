namespace Pro3D.AnnotationStatistics

module RoseDiagram_App =

    let update (m:RoseDiagramModel) (action:RoseDiagramModelAction) =
        match action with        

        | UpdateRD data -> 
            let updatedBins = RoseDiagramModel.sortRoseDiagramDataIntoBins m.bins data m.binAngle
            let max = BinModel.getBinMaxValue updatedBins
            let avgAngle = RoseDiagramModel.calculateAvgAngle (data |> List.map(fun (_,d) -> d))
            {m with bins = updatedBins; maxBinValue = max; avgAngle = avgAngle}

        | EnterRDBin id ->           
            {m with hoveredBin = Some(id)}

        | ExitRDBin ->
            {m with hoveredBin = None}

