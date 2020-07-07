namespace PRo3D.Align

open System

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives

type AlignmentActions =
    | AddPoint of PickSurfacePair
    | Finish

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Alignment =

    let initModel = {
        pickedPoints = plist.Empty 
        alignment    = None
        resultTrafo  = None
    }

    let align (al : Alignment) : Trafo3d =
        Trafo3d.Identity

module AlignmentApp =
    open PRo3D 
    
    let update (m : AlignmentModel) (a : AlignmentActions) : AlignmentModel =
        match a with
          | AddPoint k ->
            let points = m.pickedPoints |> PList.prepend k
            Log.line "%A" points
            
            match points.Count with
              | 0 -> m
              | 1 -> { m with pickedPoints = points }
              | 2 ->
                let red  = points.[1]
                let blue = points.[0]

                let al = {    
                    red        = red.surfaceName
                    blue       = blue.surfaceName
                    redPoints  = [red.point]  |> PList.ofList
                    bluePoints = [blue.point] |> PList.ofList
                }

                // reject picked point of surface names are not unique
                { m with alignment = Some al; pickedPoints = points }
              | _ -> 
                match (m.pickedPoints |> PList.tryHead, m.alignment) with
                  | Some p, Some kk -> 
                    
                    let al' =
                        if p.surfaceName = kk.red then
                            { kk with redPoints = kk.redPoints |> PList.prepend p.point }
                        else
                            { kk with bluePoints = kk.bluePoints |> PList.prepend p.point }

                    { m with alignment = Some al'; pickedPoints = points }
                  | _ -> m                                
                                                        
          | Finish -> m
            //match m.alignment with
            //  | Some al when al.bluePoints.Count = al.redPoints.Count ->
            //    let source = al.redPoints |> PList.toArray
            //    let dest   = al.bluePoints |> PList.toArray
                
            //    let c = Aardvark.VRVis.Approx.PoseTrafoEstimation.Config.SimilarityTrafoDefault

            //    let t = Aardvark.VRVis.Approx.PoseTrafoEstimation.SimilarityTrafo(source, dest, c).Value
            //    { m with resultTrafo = Some t }
            //  | _ -> m

    let view (m : MAlignmentModel) (view : IMod<CameraView>) : ISg<AlignmentActions> =
                
        //aset {
        //    //let! align = m.alignment
            

        //    //match align with
        //    //  | Some a -> 
        //    //    let! countB = a.bluePoints |> AList.count
        //    //    let! countR = a.redPoints |> AList.count

        //    //    yield Sg.dots a.redPoints  (Mod.constant C4b.Red)  view
        //    //    yield Sg.dots a.bluePoints (Mod.constant C4b.Blue) view
        //    //  | None ->
        //    let points = m.pickedPoints |> AList.map(fun x -> x.point)
        //    yield Sg.dots points (Mod.constant 1.0) (Mod.constant C4b.VRVisGreen)      // TODO THOMAS: maybe use Sg.indexedGeometryDots instead of Sg.dots?
        //} |> Sg.set                                 

        failwith ""