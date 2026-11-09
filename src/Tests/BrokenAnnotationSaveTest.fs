/// #834: an annotation that cannot be serialized must not stop the save of all others,
/// and the user must be told which one was left out.
module BrokenAnnotationSaveTest

open System
open System.IO
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive
open Expecto

open PRo3D
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

let private annotation (text : string) =
    let a = Annotation.make Projection.Linear None Geometry.Polygon None { c = C4b.White } Annotation.Initial.thickness ""
    { a with text = text; points = IndexList.ofList [ V3d.OOO; V3d.IOO; V3d.IIO ] }

/// What the manual dip of a polygon could produce: NaN angles, which Chiron cannot write.
let private broken () =
    let nan = Double.NaN
    let dns =
        {
            version         = DipAndStrikeResults.current
            plane           = Plane3d(V3d.OOI, 0.0)
            dipAngle        = nan
            dipDirection    = V3d.OOI
            strikeDirection = V3d.IOO
            dipAzimuth      = nan
            strikeAzimuth   = nan
            centerOfMass    = V3d.OOO
            error           = { version = Statistics.current; average = nan; min = nan; max = nan; stdev = nan; sumOfSquares = nan }
            regressionInfo  = None
        }
    { annotation "broken" with dnsResults = Some dns }

let tests () =
    testList "broken annotations do not stop the save (#834)" [
        test "the broken annotation is left out and named, the others are saved" {
            let dir = Path.Combine(Path.GetTempPath(), "pro3d-834-" + string (Guid.NewGuid()))
            Directory.CreateDirectory dir |> ignore
            try
                let good = annotation "good"
                let bad = broken ()
                let groups =
                    GroupsModel.initial
                    |> GroupsApp.addLeafToActiveGroup (Leaf.Annotations good) false
                    |> GroupsApp.addLeafToActiveGroup (Leaf.Annotations bad) false
                let drawing = { DrawingModel.initialdrawing with annotations = groups }
                let paths = ViewerIO.ScenePaths.create (Path.Combine(dir, "scene.pro3d"))

                let kept, unsaved = ViewerIO.saveAnnotations drawing paths

                Expect.equal unsaved [ "'broken'" ] "the broken annotation is named"
                Expect.equal (kept.annotations.flat.Count) 2 "the session keeps both"
                match ViewerIO.tryLoadAnnotations paths.annotations with
                | Some loaded ->
                    Expect.isTrue (loaded.annotations.flat.ContainsKey good.key) "the good one is in the file"
                    Expect.isFalse (loaded.annotations.flat.ContainsKey bad.key) "the broken one is not"
                | None -> failtest "the annotation file does not load"
            finally
                try Directory.Delete(dir, true) with _ -> ()
        }
    ]
