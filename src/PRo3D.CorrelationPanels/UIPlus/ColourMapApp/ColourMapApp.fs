namespace UIPlus

open Aardvark.UI
open Aardvark.Base
open FSharp.Data.Adaptive
open UIPlus
open UIPlus.Tables

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ColourMap =

    type Action =
        | ItemMessage       of GrainType * ColourMapItem.Action
        | SelectItem        of GrainType

    let initColorMapItem grainType colour upper middle lower upperStr order : ColourMapItem =
        {
            id              = grainType
            upper           = upper
            defaultMiddle   = middle
            lower           = lower
            upperStr        = upperStr
            colour          = {ColorPicker.init with c = colour}
            label           = grainType.toString
            order            = order
        }


    let inCorrMappings =
        [
            initColorMapItem GrainType.Unknown  (new C4b(255,255,255)) -2.0            0.003          0.0              "   -    "  // like vfGravel
            initColorMapItem GrainType.Boulder  (new C4b(217,95,14))     1.0            0.628          0.256            "< -8    "  
            initColorMapItem GrainType.Cobble   (new C4b(217,95,14))    0.256          0.16           0.064            "-6 to -8"  
            initColorMapItem GrainType.VcGravel (new C4b(217,95,14))     0.064          0.048          0.032            "-5 to -6"  
            initColorMapItem GrainType.CGravel  (new C4b(217,95,14))    0.032          0.024          0.016            "-4 to -5"  
            initColorMapItem GrainType.MGravel  (new C4b(217,95,14))   0.016          0.012          0.008            "-3 to -4"  
            initColorMapItem GrainType.FGravel  (new C4b(217,95,14))   0.008          0.006          0.004            "-2 to -3"  
            initColorMapItem GrainType.VfGravel (new C4b(217,95,14))   0.004          0.003          0.002            "-1 to -2"
            initColorMapItem GrainType.VcSand   (new C4b(254,196,79))   0.002          0.0015         0.001            " 0 to -1"
            initColorMapItem GrainType.CSand    (new C4b(254,196,79))   0.001          0.00075        0.0005           " 1 to 0 "
            initColorMapItem GrainType.MSand    (new C4b(254,196,79))   0.0005         0.000375       0.00025          " 2 to 1 "
            initColorMapItem GrainType.FSand    (new C4b(254,196,79))  0.00025        0.0001875      0.000125         " 3 to 2 "
            initColorMapItem GrainType.VfSand   (new C4b(254,196,79))  0.000125       7e-05          0.000015         " 4 to 3 "
            initColorMapItem GrainType.Silt     (new C4b(255,247,188))  0.000015       9.4e-06        0.0000038        " 8 to 4 "
            initColorMapItem GrainType.Clay     (new C4b(99,99,99))     0.0000038      2.39e-06       0.00000098       " 10 to 8 "
            initColorMapItem GrainType.Colloid  (new C4b(99,99,99))     0.00000098     1.195e-06      3.8e-08          " 20 to 10 "   
        ] 

    let dqMappings =
           [
               initColorMapItem GrainType.Unknown  (new C4b(255,255,255)) -2.0            0.003          0.0              "   -    "  // like vfGravel
               initColorMapItem GrainType.Boulder  (new C4b(205,102,49))     1.0            0.628          0.256            "< -8    "  
               initColorMapItem GrainType.Cobble   (new C4b(205,102,49))    0.256          0.16           0.064            "-6 to -8"  
               initColorMapItem GrainType.VcGravel (new C4b(205,102,49))     0.064          0.048          0.032            "-5 to -6"  
               initColorMapItem GrainType.CGravel  (new C4b(205,102,49))    0.032          0.024          0.016            "-4 to -5"  
               initColorMapItem GrainType.MGravel  (new C4b(205,102,49))   0.016          0.012          0.008            "-3 to -4"  
               initColorMapItem GrainType.FGravel  (new C4b(205,102,49))   0.008          0.006          0.004            "-2 to -3"  
               initColorMapItem GrainType.SandStone  (new C4b(246,237,108))   0.008          0.006          0.004            "-2 to -3"  
               initColorMapItem GrainType.VfGravel (new C4b(205,102,49))   0.004          0.003          0.002            "-1 to -2"
               initColorMapItem GrainType.VcSand   (new C4b(246,237,108))   0.002          0.0015         0.001            " 0 to -1"
               initColorMapItem GrainType.CSand    (new C4b(246,237,108))   0.001          0.00075        0.0005           " 1 to 0 "
               initColorMapItem GrainType.MSand    (new C4b(246,237,108))   0.0005         0.000375       0.00025          " 2 to 1 "
               initColorMapItem GrainType.FSand    (new C4b(246,237,108))  0.00025        0.0001875      0.000125         " 3 to 2 "
               initColorMapItem GrainType.VfSand   (new C4b(246,237,108))  0.000125       7e-05          0.000015         " 4 to 3 "
               initColorMapItem GrainType.Silt     (new C4b(250,248,208))  0.000015       9.4e-06        0.0000038        " 8 to 4 "
               initColorMapItem GrainType.Paleosol (new C4b(205,102,49))  0.000015       9.4e-06        0.0000038        " 8 to 4 "
               initColorMapItem GrainType.Clay     (new C4b(109,109,109))     0.0000038      2.39e-06       0.00000098       " 10 to 8 "
               initColorMapItem GrainType.Colloid  (new C4b(109,109,109))     0.00000098     1.195e-06      3.8e-08          " 20 to 10 "   
           ] 

    let initial: ColourMap = 

        let mappings = 
            //dqMappings
            inCorrMappings
            |> List.mapi (fun index x -> 
                let orderedItem = x index
                orderedItem.id, orderedItem)
            |> HashMap.ofList

        {
            mappings     = mappings
            defaultValue = GrainType.Unknown
            selected     = None
        }

    let update (model : ColourMap) (action : Action) =
        match action with
        | ItemMessage (id, colorAction) ->
            let updatedMap = 
                model.mappings
                |> HashMap.alter id (Option.map (fun x -> ColourMapItem.update x colorAction))
            {model with mappings = updatedMap}
        | SelectItem id ->
            {model with selected = Some id}

    let view (model : MColourMap) =
        let tableview = 
            let domList =
                model.mappings
                |> AMap.toASet
                |> ASet.sortBy (fun (k,i) -> i.order)
                |> AList.map (fun (k, m) -> 
                    let mapper = UI.map (fun a -> Action.ItemMessage (m.id, a) ) 
                    let nodes = (ColourMapItem.view m) |> List.map mapper
                    model.selected
                    |> AList.bind (fun x -> 
                        match x with 
                        | Some id when id = m.id -> intoActiveTr (Action.SelectItem m.id) nodes |> AList.single
                        | _ -> intoTrOnClick (Action.SelectItem m.id) nodes |> AList.single)
                )
                |> AList.concat

            toTableView (div[][]) domList ["Grain size";"Colour";"φ-scale"]

        div [] [
            Incremental.div (AttributeMap.ofList [clazz "ui inverted segment"]) (AList.single tableview)               
        ]   