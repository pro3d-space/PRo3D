namespace Pro3D.AnnotationStatistics

open System
open Aardvark.Base
open Aardvark.UI
open Adaptify
open FSharp.Data.Adaptive

[<ModelType>]
type HistogramModel = 
    {
        [<NonAdaptive>]
        id          : Guid    
        data        : List<Guid*float>
        maxBinValue : int
        numOfBins   : NumericInput
        domainStart : NumericInput
        domainEnd   : NumericInput
        bins        : List<BinModel> 
    }

type HistogramModelAction =    
    | UpdateData of List<Guid*float>    //Guid of elements will be stored in the bins to allow reconstruction
    | SetBinNumber of Numeric.Action    //the user can manually adapt the bin number and domain range
    | SetDomainMin of Numeric.Action
    | SetDomainMax of Numeric.Action   

module HistogramModel =

    //numeric input field to set the domain range
    let domainNumeric (value:float) = 
          {
              value = value
              min   = 0.01
              max   = 1000.0 
              step  = 1.00
              format = "{0:0.00}"
          }
    
    //numeric input field to set the number of bins
    let binNumeric =
          {
              value = 5.00
              min = 5.00
              max = 30.00
              step = 1.00
              format = "{0:0.00}"
          }

    let createHistogramBins (count:int) (min:float) (width:float) =
        [
            for i in 0..(count-1) do
                let start = min + (float(i) * width)
                let en = start + width                
                {                    
                    count = 0
                    range = Range1d(start,en)
                    annotationIDs = List.empty
                }
        ]

    //the data is sorted into bins together with a list of ids of the elements that were responsible for an increase of the bin counter
    //with this we can, if needed, reconstruct the elements from this ids at a later point
    let sortHistogramDataIntoBins (bins:List<BinModel>) (data:List<Guid*float>) (domain:Range1d) (width:float)= //(min:float) (width:float)=

        let grouping = 
            data 
            |> List.groupBy (fun (_,value) -> 
                let shifted = value - domain.Min                 
                int(shifted/width)
            )
            |> List.map(fun (binID, innerList) -> 
                let counter = innerList|> List.length
                let annotationIds = innerList |> List.map(fun (id,_) -> id)
                (binID, (counter, annotationIds))
            )
            |> Map.ofList //review: this looks quite smart though

        bins 
        |> List.mapi (fun i bin -> 
            match (grouping.TryFind i) with
            | Some (count,ids) -> { bin with count = count; annotationIDs = ids}
            | None -> bin
        )
    
    let setHistogramBins (data:List<Guid*float>) (domain:Range1d) (n:int) = //(min:float) (width:float) (binCount:int) =

        let binWidth = domain.Size / float(n)           
        let createBins = createHistogramBins n domain.Min binWidth
        sortHistogramDataIntoBins createBins data domain binWidth
    
    let initHistogram (domain:Range1d) (data:List<Guid*float>) =
        
        let domainStart = floor(domain.Min) 
        let domainEnd = ceil(domain.Max)    
        let roundedDomain = Range1d(domainStart,domainEnd)           
        let bins = setHistogramBins data roundedDomain (int(binNumeric.value))
        {
            id          = Guid.NewGuid()       
            numOfBins   = binNumeric 
            maxBinValue = BinModel.getBinMaxValue bins
            domainStart = domainNumeric domainStart
            domainEnd   = domainNumeric domainEnd
            data        = data
            bins        = bins 
        }
