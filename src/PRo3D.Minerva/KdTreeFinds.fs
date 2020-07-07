namespace KdTreeHelper

open System.Runtime.CompilerServices
open Aardvark.Geometry
open Aardvark.Base

[<AutoOpen>]
module private KdTreeQueryHelpers =

    module Seq = 
        open System.Collections.Generic

        type private EnumeratorEnumerable<'a>(create : unit -> IEnumerator<'a>) =
            interface System.Collections.IEnumerable with
                member x.GetEnumerator() = create() :> _

            interface IEnumerable<'a> with
                member x.GetEnumerator() = create()

        [<AbstractClass>]
        type AbstractEnumerator<'a>() =
            abstract member MoveNext : unit -> bool
            abstract member Current : 'a
            abstract member Reset : unit -> unit
            abstract member Dispose : unit -> unit

            interface System.Collections.IEnumerator with
                member x.MoveNext() = x.MoveNext()
                member x.Current = x.Current :> obj
                member x.Reset() = x.Reset()

            interface IEnumerator<'a> with
                member x.Current = x.Current
                member x.Dispose() = x.Dispose()


        let ofEnumerator (create : unit -> #IEnumerator<'a>) =
            EnumeratorEnumerable (fun () -> create() :> IEnumerator<'a>) :> seq<'a>

        let mergeSorted (cmp : 'a -> 'a -> int) (l : IEnumerable<'a>) (r : IEnumerable<'a>) =
            let newEnumerator() =
                let l = l.GetEnumerator()
                let r = r.GetEnumerator()

                let mutable initial = true
                let mutable lh = None
                let mutable rh = None
                let mutable current = Unchecked.defaultof<'a>

                { new AbstractEnumerator<'a>() with
                    member x.MoveNext() =
                        if initial then
                            initial <- false
                            lh <- if l.MoveNext() then Some l.Current else None
                            rh <- if r.MoveNext() then Some r.Current else None

                        match lh, rh with
                        | Some lv, Some rv ->
                            let c = cmp lv rv
                            if c <= 0 then
                                current <- lv
                                if l.MoveNext() then lh <- Some l.Current
                                else lh <- None
                            else
                                current <- rv
                                if r.MoveNext() then rh <- Some r.Current
                                else rh <- None
                            true
                        | Some lv, None ->
                            current <- lv
                            if l.MoveNext() then lh <- Some l.Current
                            else lh <- None
                            true
                        | None, Some rv ->
                            current <- rv
                            if r.MoveNext() then rh <- Some r.Current
                            else rh <- None
                            true
                        | None, None ->
                            false

                    member x.Reset() =
                        l.Reset()
                        r.Reset()
                        initial <- true
                        lh <- None
                        rh <- None
                        current <- Unchecked.defaultof<'a>

                    member x.Dispose() =
                        l.Dispose()
                        r.Dispose()
                        initial <- false
                        lh <- None
                        rh <- None
                        current <- Unchecked.defaultof<'a>

                    member x.Current = current

                } :> IEnumerator<_>

            ofEnumerator newEnumerator

    let toHull3d (viewProj : Trafo3d) =
        let r0 = viewProj.Forward.R0
        let r1 = viewProj.Forward.R1
        let r2 = viewProj.Forward.R2
        let r3 = viewProj.Forward.R3

        let inline toPlane (v : V4d) =
            Plane3d(-v.XYZ, v.W)

        Hull3d [|
            r3 - r0 |> toPlane  // right
            r3 + r0 |> toPlane  // left
            r3 + r1 |> toPlane  // bottom
            r3 - r1 |> toPlane  // top
            r3 + r2 |> toPlane  // near
            //r3 - r2 |> toPlane  // far
        |]

    type RegionQuery = 
        {
            viewProj : Trafo3d
            region : Region3d
            cam : V3d
        }

[<AbstractClass; Sealed; Extension>]
type KdTreeQuery () = 
     
    static let traversePointKdTree (query : RegionQuery) (t : PointKdTreeD<V3d[], V3d>) (bounds : Box3d) (positions : V3d[]) =
        let data = t.Data
     

        let inline isInner (node : int) =   
            let r = 2 * node + 1
            r < data.PermArray.Length
            
        let getScreenSpacePos (pt : V3d) =
            let pp = query.viewProj.Forward.TransformPosProj pt
            pp

        let compare (l : V3d, _, _) (r : V3d, _, _) = compare l.Z r.Z

        let rec traverse (box : Box3d) (node : int) =
            if node >= data.PermArray.Length then
                Seq.empty
            else
                let index = int data.PermArray.[node]
                let pt = positions.[index]
                let self = 
                    if Region3d.contains pt query.region then Seq.singleton (getScreenSpacePos pt, pt, index)
                    else Seq.empty

                if isInner node then
                    // inner
                    let splitAxis = data.AxisArray.[node]
                    let splitValue = pt.[splitAxis]

                    let mutable lbox = box
                    let mutable rbox = box
                    lbox.Max.[splitAxis] <- splitValue
                    rbox.Min.[splitAxis] <- splitValue

                    let il = Region3d.intersects lbox query.region
                    let ir = Region3d.intersects rbox query.region
                
                    if il && ir then
                        let left = traverse lbox (2 * node + 1)
                        let right = traverse rbox (2 * node + 2)
                        Seq.mergeSorted compare self (Seq.mergeSorted compare left right)
                    elif il then
                        let inner = traverse lbox (2 * node + 1)
                        Seq.mergeSorted compare self inner
                    elif ir then
                        let inner = traverse rbox (2 * node + 2)
                        Seq.mergeSorted compare self inner
                    else
                        self


                else
                    // leaf
                    self

        if Region3d.intersects bounds query.region then
            traverse bounds 0
        else
            Seq.empty

    static member private FindPointsInternal(self :  PointKdTreeD<(V3d[]), V3d>, worldBox : Box3d, positions : V3d[], viewProj : Trafo3d, box : Box2d) =
        let cam = viewProj.Backward.TransformPosProj(V3d(0.0, 0.0, -100000.0))

        let hull = 
            let c = box.Center
            let scale = 2.0 / box.Size
                
            let lvp = 
                viewProj *
                Trafo3d.Scale(scale.X, scale.Y, 1.0) *
                Trafo3d.Translation(-scale.X * c.X, -scale.Y * c.Y, 0.0)

            toHull3d lvp
            |> FastHull3d
                

        let region = 
            Region3d.ofIntersectable {
                new Intersectable3d() with
                    override x.Contains (pt : V3d) = hull.Hull.Contains pt
                    override x.Contains (box : Box3d) = box.ComputeCorners() |> Array.forall hull.Hull.Contains
                    override x.Intersects (box : Box3d) = hull.Intersects box
            }

        traversePointKdTree { region = region; cam = cam; viewProj = viewProj } self worldBox positions
 
    [<Extension>]
    static member FindPoints(self :  PointKdTreeD<(V3d[]), V3d>, worldBox : Box3d, positions : V3d[], viewProj : Trafo3d, box : Box2d) =
        KdTreeQuery.FindPointsInternal(self, worldBox, positions, viewProj, box)
        |> Seq.map (fun (pp,w, index) -> pp.Z, w, index)
        
    [<Extension>]
    static member FindPoints(self :  PointKdTreeD<(V3d[]), V3d>, worldBox : Box3d, positions : V3d[], viewProj : Trafo3d, ellipse : Ellipse2d) =
        let ellipse =
            let d = Vec.dot ellipse.Axis0 ellipse.Axis1 
            if Fun.IsTiny d then ellipse
            else Ellipse2d.FromConjugateDiameters(ellipse.Center, ellipse.Axis0, ellipse.Axis1)

        let box = 
            Box2d [| ellipse.Center - ellipse.Axis0; ellipse.Center + ellipse.Axis0;  ellipse.Center - ellipse.Axis1; ellipse.Center + ellipse.Axis1 |]
                
        let m = M22d.FromCols(ellipse.Axis0, ellipse.Axis1).Inverse
            
        KdTreeQuery.FindPointsInternal(self, worldBox, positions, viewProj, box)
        |> Seq.filter (fun (pp,w, index) -> Vec.lengthSquared (m * (pp.XY - ellipse.Center)) <= 1.0)
        |> Seq.map (fun (pp,w, index) -> pp.Z, w, index)

