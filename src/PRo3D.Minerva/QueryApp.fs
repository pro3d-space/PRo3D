namespace PRo3D.Minerva

open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Events

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.Rendering.Text 

open PRo3D.Base
open PRo3D.Minerva.Communication
open OpcViewer.Base

module QueryApp =
                   
    let filterByDistance (location:V3d) (dist:float) (features:IndexList<Feature>) =
        features        
        |> IndexList.filter(fun f -> Vec.Distance(location, f.geometry.positions.Head) < dist)                          

    let filterBySol (minsol:float) (maxsol:float) (features:IndexList<Feature>) =
        let range = Range1i([minsol |> int; maxsol |> int]) // int[] creates bounding range! Range1i(min,max) creates range from min to max!! [could also be negative]
        features |> IndexList.filter (fun x -> range.Contains(x.sol))
       
    let filterByInstrument (model : QueryModel) (features:IndexList<Feature>) =
        let fl = 
            features
            |> IndexList.choose(fun f -> 
                match f.instrument with
                | Instrument.MAHLI -> if model.checkMAHLI then Some f else None        
                | Instrument.FrontHazcam -> if model.checkFrontHazcam then Some f else None     
                | Instrument.Mastcam -> if model.checkMastcam then Some f else None         
                | Instrument.APXS -> if model.checkAPXS then Some f else None             
                | Instrument.FrontHazcamR -> if model.checkFrontHazcamR then Some f else None     
                | Instrument.FrontHazcamL -> if model.checkFrontHazcamL then Some f else None   
                | Instrument.MastcamR -> if model.checkMastcamR then Some f else None      
                | Instrument.MastcamL -> if model.checkMastcamL then Some f else None        
                | Instrument.ChemLib -> if model.checkChemLib then Some f else None         
                | Instrument.ChemRmi -> if model.checkChemRmi then Some f else None  
                | _ -> None
            )
        fl
                    
    let applyFilterQueries (features:IndexList<Feature>) (qModel : QueryModel) : IndexList<Feature> =        
        let filtered = 
            features
            |> filterByDistance qModel.filterLocation qModel.distance.value
            |> filterBySol qModel.minSol.value qModel.maxSol.value
            |> filterByInstrument qModel
            //|> updateFeaturesForRendering qModel
        Log.line "[Minerva] filtered set %d products" filtered.Count
        filtered
    
    let update (model : QueryModel) (msg : QueryAction) : QueryModel =
        match msg with
        | SetMinSol ms ->
            let minsol = Numeric.update model.minSol ms
            if model.maxSol < minsol then
                {model with minSol = minsol; maxSol = minsol }
            else 
                { model with minSol = minsol }        
        | SetMaxSol ms ->
            let maxsol = Numeric.update model.maxSol ms
            if model.minSol > maxsol then
                {model with minSol = maxsol; maxSol = maxsol}
            else
                { model with maxSol = maxsol }        
        | SetDistance d ->
            let dist = Numeric.update model.distance d
            { model with distance = dist }        
        | SetFilterLocation p ->
            { model with filterLocation = p }
        | CheckMAHLI -> 
            { model with checkMAHLI = not model.checkMAHLI }        
        | CheckFrontHazcam -> 
            { model with checkFrontHazcam = not model.checkFrontHazcam }        
        | CheckMastcam -> 
            { model with checkMastcam = not model.checkMastcam }        
        | CheckAPXS -> 
            { model with checkAPXS = not model.checkAPXS }        
        | CheckFrontHazcamR -> 
            { model with checkFrontHazcamR = not model.checkFrontHazcamR }        
        | CheckFrontHazcamL -> 
            { model with checkFrontHazcamL = not model.checkFrontHazcamL }        
        | CheckMastcamR -> 
            { model with checkMastcamR = not model.checkMastcamR }        
        | CheckMastcamL -> 
            { model with checkMastcamL = not model.checkMastcamL }        
        | CheckChemLib -> 
            { model with checkChemLib = not model.checkChemLib }        
        | CheckChemRmi -> 
            { model with checkChemRmi = not model.checkChemRmi }        

    let iconToggle (dings : aval<bool>) onIcon offIcon action =
        let toggleIcon = dings |> AVal.map(fun isOn -> if isOn then onIcon else offIcon)

        let attributes = 
            amap {
                let! icon = toggleIcon
                yield clazz icon
                yield onClick (fun _ -> action)
            } 
            |> AttributeMap.ofAMap

        Incremental.i attributes AList.empty

    let mkColAttributeMap icon instrument = //action
        amap {
            yield style (sprintf "color: %s" (Html.ofC4b (instrument |> MinervaModel.instrumentColor)))
            yield clazz icon
            //yield onClick (fun _ -> action)
        } 
        |> AttributeMap.ofAMap

    let coloredCircle instrument = 
        Incremental.i 
            (mkColAttributeMap "circle icon" instrument)
            AList.empty

    let iconCheckBox (dings : aval<bool>) action =
        iconToggle dings "check square outline icon" "square icon" action
        
    let instrumentCountText (instrumentCounts : amap<Instrument,int>) (instrument : Instrument) =
        instrumentCounts 
        |> AMap.tryFind instrument 
        |> AVal.map(function
            | Some a -> "(" + a.ToString() + ")"
            | None -> "(0)"
        )

    let viewQueryInstruments (grouped : amap<Instrument, list<Feature>>) (model : AdaptiveQueryModel) =
        let counts = 
            grouped 
            |> AMap.map(fun _ groups -> groups.Length)

        require Html.semui (
            Html.table [  
                Html.row ("MAHLI") [
                    coloredCircle Instrument.MAHLI;                    
                    iconCheckBox model.checkMAHLI CheckMAHLI
                    Incremental.text (Instrument.MAHLI |> instrumentCountText counts)
                ]   
                Html.row ("APXS") [
                    coloredCircle Instrument.APXS;
                    iconCheckBox model.checkAPXS CheckAPXS
                    Incremental.text (Instrument.APXS |> instrumentCountText counts)
                ]
                Html.row ("FrontHazcamR") [
                    coloredCircle Instrument.FrontHazcamR;
                    iconCheckBox model.checkFrontHazcamR CheckFrontHazcamR
                    Incremental.text (Instrument.FrontHazcamR |> instrumentCountText counts)
                ]
                Html.row ("FrontHazcamL") [
                    coloredCircle Instrument.FrontHazcamL;
                    iconCheckBox model.checkFrontHazcamL CheckFrontHazcamL
                    Incremental.text (Instrument.FrontHazcamL |> instrumentCountText counts)
                ]
                Html.row ("MastcamR") [
                    coloredCircle Instrument.MastcamR;
                    iconCheckBox model.checkMastcamR CheckMastcamR
                    Incremental.text (Instrument.MastcamR |> instrumentCountText counts)
                ]
                Html.row ("MastcamL") [
                    coloredCircle Instrument.MastcamL;
                    iconCheckBox model.checkMastcamL CheckMastcamL
                    Incremental.text (Instrument.MastcamL |> instrumentCountText counts)
                ]
                Html.row ("ChemLib") [
                    coloredCircle Instrument.ChemLib;
                    iconCheckBox model.checkChemLib CheckChemLib
                    Incremental.text (Instrument.ChemLib |> instrumentCountText counts)
                ]
                Html.row ("ChemRmi") [
                    coloredCircle Instrument.ChemRmi;
                    iconCheckBox model.checkChemRmi CheckChemRmi
                    Incremental.text (Instrument.ChemRmi |> instrumentCountText counts)
                ]
            ]
        )
    
    let viewQueryFilters (grouped : amap<Instrument, list<Feature>>) (model : AdaptiveQueryModel) =
      require Html.semui ( 
        Html.table [                 
            Html.row "min sol:"  [Numeric.view' [NumericInputType.InputBox] model.minSol |> UI.map (fun x -> SetMinSol x)]
            Html.row "max sol:"  [Numeric.view' [NumericInputType.InputBox] model.maxSol |> UI.map (fun x -> SetMaxSol x)]
            Html.row "instruments:" [
                viewQueryInstruments grouped model
            ]
            Html.row "distance:" [Numeric.view' [NumericInputType.InputBox] model.distance |> UI.map (fun x -> SetDistance x)]
           // Html.row "100 nearest" [button [clazz "ui button"; onClick (fun _ -> UpdateDistance)][text "ok"]] 
        ]        
      ) 