namespace Svgplus.Correlations2

open System
open Aardvark.Base
open Svgplus.RectangleType
open Aardvark.UI
open Aardvark.Base.Incremental

module CorrelationsApp =
    let update (model : CorrelationsModel) (action : CorrelationsAction) =
        match action with
        | CorrelationsAction.Create borders ->
            Log.line "[Correlations] creating new correlation from %d borders" borders.Count

            let corr =
                {
                    version  = Correlation.current
                    id       = CorrelationId.create()
                    contacts = borders
                }

            { model with correlations = model.correlations |> HMap.add corr.id corr }
        | CorrelationsAction.Edit (id,borders) ->
            let corr =
                model.correlations 
                |> HMap.alter id (fun x ->
                    match x with 
                    | Some corr -> { corr with contacts = borders } |> Some
                    | None -> None)
                                
            { model with correlations = corr }
        | CorrelationsAction.Delete id -> 
            let model =
                match model.selectedCorrelation with
                | Some selectedId when selectedId = id -> { model with selectedCorrelation = None }
                | _ -> model
            let model =
                match model.alignedBy with
                | Some alignedId when alignedId = id -> { model with alignedBy = None }
                | _ -> model
            let correlations = (HMap.remove id model.correlations)
            { model with correlations = correlations }
        | CorrelationsAction.Select id -> 
            match model.selectedCorrelation with
            | Some corr when corr = id ->
                { model with selectedCorrelation = None }
            | _ -> 
                { model with selectedCorrelation = Some id }    
        | CorrelationsAction.FlattenHorizon id -> 
            { model with alignedBy = Some id }    
        | CorrelationsAction.DefaultHorizon ->
           { model with alignedBy = None }    

    let viewCorrelationsSVG (model: MCorrelationsModel) (bordersTable: amap<RectangleBorderId, MRectangleBorder>) (rectanglesTable: amap<RectangleId, MRectangle>) = 
        let yMargin = 67.0 // MAGIC y-offset (incl. static lable width)

        let mod2Pair (a,b) = Mod.map2(fun x y -> x,y) a b

        let correlationLines =  
            model.correlations 
            |> AMap.toASet
            |> ASet.collect(fun (_,correlation) -> 
                let isSelected =
                    model.selectedCorrelation 
                    |> Mod.map (fun oSel -> 
                        oSel 
                        |> Option.map (fun selectedId -> selectedId = correlation.id) 
                        |> Option.defaultValue false
                    )

                correlation.contacts 
                |> AMap.chooseM(fun rectBorderId borderContactId ->
                    bordersTable |> AMap.tryFind rectBorderId
                )
                |> AMap.chooseM(fun rectBorderId rectBorder ->

                    let lower = rectanglesTable |> AMap.tryFind rectBorder.lowerRectangle
                    let upper = rectanglesTable |> AMap.tryFind rectBorder.upperRectangle
                    let lowerUpper = (lower, upper) |> mod2Pair

                    Mod.map2(fun (l,u) color -> Option.map2(fun a b -> (a, b, color)) l u) lowerUpper rectBorder.color                    
                )
                |> AMap.mapM(fun _ (l,u,color) ->
                    Mod.map2(fun pos dims -> pos, dims, color) l.pos ((l.dim, u.dim)|> mod2Pair))
                |> AMap.toASet
                |> ASet.toAList 
                |> AList.sortBy(fun (_,(pos,_,_)) -> pos.X)
                |> AList.toMod 
                |> Mod.map2(fun isSelected x -> 
                    x 
                    |> PList.toList 
                    |> List.pairwise
                    |> List.map(fun ((_,b),(_,d)) -> (b,d))
                    |> List.map(fun ((leftPos , (leftLowerSize, leftUpperSize), leftColor) , (rightPos,_,_)   ) -> 
                        // main connection between two stacks
                        Line2d(
                            leftPos  + V2d.IO * yMargin + V2d.IO * (max leftLowerSize.X leftUpperSize.X),
                            rightPos + V2d.IO * (yMargin-15.0) // MAGIC 30,0 width of secondary-stack
                        ), correlation.id, if isSelected then C4b.VRVisGreen else leftColor
                    )
                    |> PList.ofList
                ) isSelected 
                |> AList.ofMod
                |> AList.toASet
            ) 
            |> ASet.map(fun (x, correlationId, color) -> 
                let fOnClick = fun _ -> CorrelationsAction.Select correlationId
                Svgplus.Paths.drawClickableCubicBezierCurve x.P0 x.P1 color 2.0 true 3.0 3.0 fOnClick)
            |> ASet.toAList

        Incremental.Svg.g AttributeMap.empty correlationLines

    let viewCorrelations (model : MCorrelationsModel): DomNode<CorrelationsAction> =       
        let toStyleColor color = (sprintf "color: %s;" (Html.ofC4b color))
        
        let getColor id isHeader = 
            model.selectedCorrelation
            |> Mod.map(fun x ->
                match x with
                | Some y when y = id ->
                    C4b.VRVisGreen |> toStyleColor
                | _ when isHeader ->
                    C4b.Gray |> toStyleColor
                | _ ->
                    C4b.White |> toStyleColor
            )

        let iconAttributes id =
            amap {
                yield clazz "ui circle inverted middle aligned icon"

                let! color = getColor id false
                yield style color
                yield onClick(fun _ -> Select id)
            } |> AttributeMap.ofAMap

        let headerAttributes id = 
            amap {
                yield clazz "header"
                let! color = getColor id true
                yield style color
                yield onClick(fun _ -> Select id)
            } |> AttributeMap.ofAMap

        let listOfCorrelations =            
            alist {
                let! corrs = model.correlations |> AMap.toMod
                let nums = corrs |> HMap.count |> string                
                
                for c in corrs.Values do
                    yield div [clazz "item"; style "margin: 0px 5px 0px 10px"][
                        Incremental.i (iconAttributes c.id) AList.empty
                        div [clazz "content"] [
                            Incremental.div 
                                (headerAttributes c.id) 
                                ([text (c.id |> string)] |> AList.ofList)                            
                            div [clazz "description"; style (toStyleColor C4b.White)] [
                                text (c.id |> string)
                            ]
                        ]
                        hr [style ((C4b.White |> toStyleColor) + "margin: 5px 0px 0px 0px")]
                    ]
            }
            
        Incremental.div 
            ([clazz "ui list"] |> AttributeMap.ofList) 
            listOfCorrelations

    let viewCorrelationActions (model : MCorrelationsModel) =
        
        let flattenAttr = 
            amap { 
                let! selectedId = model.selectedCorrelation
                let! alignedId = model.alignedBy
                match selectedId, alignedId with
                | Some correlationId, Some x when x <> correlationId ->
                    yield onMouseClick (fun _ -> FlattenHorizon correlationId)
                    yield clazz "ui icon button"
                | Some correlationId, None ->
                    yield onMouseClick (fun _ -> FlattenHorizon correlationId)
                    yield clazz "ui icon button"
                | _ -> 
                    yield clazz "ui icon disabled button"
            } |> AttributeMap.ofAMap
        
        alist {
            let! selected = model.selectedCorrelation 
            match selected with
            | Some correlationId ->                    
                yield div [clazz "ui buttons inverted"] [                    
                    button [
                        clazz "ui icon button"
                        onMouseClick (fun _ -> Delete correlationId)
                    ] [
                        i [clazz "remove icon red"] []
                    ]

                    Incremental.button flattenAttr <| alist {
                        yield i [clazz "exchange icon black"] []
                    }
                ]  
            | None -> ()

            let alignAttr = 
                amap {
                    yield onMouseClick (fun _ -> DefaultHorizon)
                        
                    let! isDefaultAligned = model.alignedBy
                    let! empty = model.correlations |> AMap.keys |> ASet.count |> Mod.map (fun x -> x < 1)
                    match empty, isDefaultAligned with
                    | true, _
                    | false, Some _ -> yield clazz "ui icon button"
                    | false, None ->  yield clazz "ui icon disabled button"
                } |> AttributeMap.ofAMap

            yield div [clazz "ui buttons inverted"] [                    
                Incremental.button alignAttr <| alist {
                    yield i [clazz "random icon black"][]
                    }
            ]
        }                                     