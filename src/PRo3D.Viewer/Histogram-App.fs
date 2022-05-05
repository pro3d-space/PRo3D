namespace Pro3D.AnnotationStatistics


open Aardvark.UI



module Histogram_App =

    let private compute (m:HistogramModel) =

           let numBins = m.numOfBins.value
           let start = m.domainStart.value  //review: range1d
           let theEnd = m.domainEnd.value
           let width = (theEnd-start) / numBins       
           let bins = HistogramModel.setHistogramBins m.data start width (int(numBins))
           let maxValue = BinModel.getBinMaxValue bins
           {m with bins = bins; maxBinValue = maxValue}

    let update (m:HistogramModel) (action:HistogramModelAction) =

        match action with
        | UpdateData data ->                                 
            let numBins = m.numOfBins.value
            let start = m.domainStart.value  
            let theEnd = m.domainEnd.value
            let width = (theEnd-start) / numBins 
            let updatedBins = HistogramModel.sortHistogramDataIntoBins m.bins data start width
            let maxValue = BinModel.getBinMaxValue updatedBins                         
            {m with data = data; bins = updatedBins; maxBinValue = maxValue}            
        | SetBinNumber act -> 
            let ud_n = Numeric.update m.numOfBins act
            let ud_hist = {m with numOfBins = ud_n}
            compute ud_hist               
        | SetDomainMin act -> 
            let ud_min = Numeric.update m.domainStart act
            let ud_hist = {m with domainStart = ud_min}
            compute ud_hist  
        | SetDomainMax act -> 
            let ud_max = Numeric.update m.domainEnd act
            let ud_hist = {m with domainEnd = ud_max}
            compute ud_hist 
    

