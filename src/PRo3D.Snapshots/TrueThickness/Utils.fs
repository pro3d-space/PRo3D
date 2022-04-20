namespace Utils

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.UI

module Debug =
    let plane() =
        Box3d(V3d(-5.0, -5.0, -0.01), V3d(5.0, 5.0, 0.0))
        |> Sg.box' C4b.DarkRed
        |> Sg.requirePicking
        |> Sg.noEvents

module Html =
    
    let textInputColorable (str : aval<string>) (textColor : C4b) changeAction =
        Incremental.div
            (AttributeMap.ofList[])
                (
                    alist {
                        let! t = str
                        let col = sprintf "color:%s" (Html.ofC4b textColor)

                        yield
                            Incremental.input
                                (AttributeMap.ofList[
                                    style col
                                    attribute "type" "text"
                                    attribute "value" t
                                    onChange changeAction
                                ])
                    }
                )
    
    let toggleButton (switch : aval<bool>) (offString : string) (onString : string) (action : 'msg) =
        Incremental.div
            (AttributeMap.ofList[])
                (
                    alist {
                        let! sw = switch
                        if sw
                        then yield button [clazz "ui button orange"; onClick (fun _ -> action)][text onString]
                        else yield button [clazz "ui button blue";   onClick (fun _ -> action)][text offString]
                    }
                )

module Picking =
    
    module PickPoint =
        
        let setup (pos : V3d) =
            {
                pos = pos
                id  = System.Guid.NewGuid().ToString()
            }
    
    module PickPoints =
        
        let initial =
            {
                points   = IndexList.empty
                preTrafo = None
            }
        
        let addPoint (pos : V3d) (m : PickPointsModel) =
            let p      = PickPoint.setup pos
            let points = m.points |> IndexList.append p
            {m with points = points}
        
        let removePoint (id : string) (m : PickPointsModel) =
            let idx =
                m.points
                |> IndexList.toList
                |> List.findIndex ( fun x -> x.id = id )
            
            let points =
                m.points
                |> IndexList.removeAt idx

            {m with points = points}

    type Action =
        | AddPoint    of V3d
        | RemovePoint of string
        | Reset
    
    let update (m : PickPointsModel) (act : Action) =
        match act with
        | AddPoint    pos -> 
            let preTrafo = 
                match m.preTrafo with
                    | None -> Trafo3d.Translation pos
                    | Some t -> t
            let localPoint = preTrafo.Backward.TransformPos pos
            let pointsModel = m |> PickPoints.addPoint localPoint
            { pointsModel with preTrafo = Some preTrafo }
        | RemovePoint id  -> m |> PickPoints.removePoint id
        | Reset           -> PickPoints.initial
    
    let mkSg (m : MPickPointsModel) (view : aval<CameraView>) (liftMessage : Action -> 'msg) =
        aset {
            for p in m.points |> AList.toASet do
                

                let pos = 
                    adaptive {
                        let! preTrafo = m.preTrafo
                        let! pos = p.pos
                        match preTrafo with 
                            | Some t -> return t.Forward.TransformPos(pos)
                            | _ -> return pos
                    }

                let trans = pos |> AVal.map Trafo3d.Translation

                printfn "%A" (trans.GetValue())

                let scale =
                    AVal.map2 ( fun (v : CameraView) (p : V3d) ->
                        let d = V3d.Distance(v.Location, p)
                        let s = 0.008 * d
                        Trafo3d.Scale(s)
                    ) view pos

                yield
                    Sg.sphere' 5 C4b.VRVisGreen 1.0
                    |> Sg.requirePicking
                    |> Sg.noEvents
                    |> Sg.withEvents [
                        Sg.onDoubleClick (fun _ -> p.id |> RemovePoint |> liftMessage)
                    ]
                    |> Sg.trafo scale
                    |> Sg.trafo trans
        }
        |> Sg.set
        |> Sg.effect [
            DefaultSurfaces.stableTrafo       |> toEffect
            DefaultSurfaces.vertexColor |> toEffect
        ]
    
    let initial = PickPoints.initial
