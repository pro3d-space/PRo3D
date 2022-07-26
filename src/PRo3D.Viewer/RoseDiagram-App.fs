namespace Pro3D.AnnotationStatistics
open System
open Aardvark.UI

module RoseDiagram_App =  

    let update (m:RoseDiagramModel) (action:RoseDiagramModelAction) =
        match action with        

        | UpdateRD data -> 
            let updatedBins = RoseDiagramModel.sortRoseDiagramDataIntoBins m.bins data m.binAngle
            let max = BinModel.getBinMaxValue updatedBins
            let avgAngle = RoseDiagramModel.calculateAvgAngle (data |> List.map(fun (_,d) -> d))
            {m with data = data; bins = updatedBins; maxBinValue = max; avgAngle = avgAngle}

        | SetBinAngle angle ->            
            let bins = RoseDiagramModel.initRoseDiagramBins angle
            let updatedBins = RoseDiagramModel.sortRoseDiagramDataIntoBins bins m.data angle
            let max = BinModel.getBinMaxValue updatedBins
            {m with bins = updatedBins; maxBinValue = max; binAngle = angle}

        | EnterRDBin id ->           
            {m with hoveredBin = Some(id)}

        | ExitRDBin ->
            {m with hoveredBin = None}

        | PeekRDBinStart value ->
            let bin = RoseDiagramModel.computeBinAffiliation value (m.binAngle/2.0) m.binAngle            
            {m with peekItem = Some(bin,value)}

        | PeekRDBinEnd ->
           {m with peekItem = None}

