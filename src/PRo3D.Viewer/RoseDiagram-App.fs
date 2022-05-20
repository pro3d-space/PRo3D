namespace Pro3D.AnnotationStatistics

module RoseDiagram_App =

    let update (m:RoseDiagramModel) (action:RoseDiagramModelAction) =
        match action with
        | UpdateBinCount -> 
            let updatedBins = RoseDiagramModel.sortRoseDiagramDataIntoBins m.bins m.data m.binAngle
            let max = BinModel.getBinMaxValue updatedBins
            {m with bins = updatedBins; maxBinValue = max}

        | UpdateRD data -> 
            let updatedBins = RoseDiagramModel.sortRoseDiagramDataIntoBins m.bins data m.binAngle
            let max = BinModel.getBinMaxValue updatedBins
            {m with bins = updatedBins; maxBinValue = max}

