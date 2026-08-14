namespace PRo3D.GeometryLab

open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open PRo3D.Base.Geometry.RegionOps

(*
A 2D bench for the annotation boolean operations.

It exists so cut and merge can be *felt* before they reach the viewer, and so a case found by
clicking becomes a regression test rather than a bug report. The geometry is the real thing -
PRo3D.Base.Geometry.RegionOps - not a 2D reimplementation; only terrain, picking and the 3D
renderer are absent. See plans/booleanOperations.md.
*)

type Tool =
    | Draw
    | Cut
    | Select

/// A region plus the identity the UI needs. Regions carry multiple contours and holes natively,
/// which is the point: it lets the lab show cases PRo3D's single-ring Annotation cannot store.
type Shape =
    {
        id       : int
        region   : Region
        selected : bool
    }

[<ModelType>]
type Model =
    {
        /// Shape is not a [<ModelType>], so Adaptify treats each element as a value and the list
        /// becomes an alist<Shape> - per-shape change tracking, with the *real* region type kept
        /// in the model. (An earlier [<TreatAsValue>] here was a workaround for adaptify failing
        /// on the then-unrestored project; it was never needed.)
        shapes    : IndexList<Shape>
        nextId    : int

        tool      : Tool

        /// ring under construction, in draw mode
        drawing   : IndexList<V2d>
        cursor    : Option<V2d>

        /// stroke being dragged, in cut mode
        cutFrom   : Option<V2d>

        status    : string

        [<TreatAsValue>]
        past      : Option<Model>
        [<TreatAsValue>]
        future    : Option<Model>
    }

type Message =
    | SetTool       of Tool
    /// One mousedown message; update dispatches on the active tool. Two separate messages on the
    /// same event were silently collapsed to one by the attribute map - only one handler per
    /// event kind survives on a node.
    | MouseDown     of V2d
    | MouseUp       of V2d
    | ClosePolygon
    | MoveCursor    of V2d
    | ToggleSelect  of int
    | MergeSelected
    | DeleteSelected
    | ExportFixture
    | Undo
    | Redo
