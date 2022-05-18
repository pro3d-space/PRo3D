namespace Pro3D.AnnotationStatistics

open Aardvark.Base
open Aardvark.UI



module Histogram_App =

    let private compute (m:HistogramModel) =
     
           let numBins = m.numOfBins.value
           let domain = Range1d(m.domainStart.value,m.domainEnd.value)        
           let bins = HistogramModel.setHistogramBins m.data domain (int(numBins))
           let maxValue = BinModel.getBinMaxValue bins
           {m with bins = bins; maxBinValue = maxValue}

    let update (m:HistogramModel) (action:HistogramModelAction) =

        match action with
        | UpdateData data ->      
            //calculate the new min and max values
            let domMin, domMax =
                let values = data |> List.map (fun (_,value) -> value)
                let min = floor(values |> List.min)
                let max = ceil(values |> List.max)
                min,max
            //update the Numeric input fields
            let ud_min = Numeric.update m.domainStart (Numeric.Action.SetValue domMin)
            let ud_max = Numeric.update m.domainEnd (Numeric.Action.SetValue domMax)

            let domain = Range1d(domMin,domMax)
            let numBins = m.numOfBins.value
            let width = domain.Size / numBins 
            let updatedBins = HistogramModel.sortHistogramDataIntoBins m.bins data domain width
            let maxValue = BinModel.getBinMaxValue updatedBins                         
            {m with data = data; domainStart = ud_min; domainEnd = ud_max; bins = updatedBins; maxBinValue = maxValue}            
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

        | EnterBin id ->
            {m with hoveredBin = Some(id)}

        | ExitBin ->
            {m with hoveredBin = None}
    

