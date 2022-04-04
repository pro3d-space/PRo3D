namespace Experiments

module Crazy = 

    let derive (f : 'a -> 'insight) : 'insight = 
        failwith ""

    let retarget (oldTuple : 'a * 'insight) (newInput : 'a) : 'insight = 
        failwith "" 
        

(*

let analysis =
let weather = getWeather ($tirol, $sept) ($tirol: latlon)
let daysWithStrongWind = weather |> Seq.filter $(fun day -> day.$maxGust < 30)
let windDirectionBins = makeHisto daysWithStrongWind
let prominentDirection = $getMax windDirectionBins
// juhu, föhn

varyingParameters analysis

// UI - zeit und ort als input über karte
// UI slider condition
// statistik


*)