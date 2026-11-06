/// #834: saving a scene must never fail, and a non-finite number must never enter the
/// model in the first place. These tests pin both: the NaN-safe writers (also for older
/// releases' readers) and the sources that used to produce NaN.
module SaveRobustnessTests

open System
open System.IO
open System.Threading
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive
open Chiron
open Expecto

open PRo3D
open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Viewer

let private nan = Double.NaN

let private nanStatistics : Statistics =
    { version = Statistics.current; average = nan; min = nan; max = nan; stdev = nan; sumOfSquares = nan }

/// What the manual dip of a polygon used to produce: every angle NaN.
let private nanDns : DipAndStrikeResults =
    {
        version         = DipAndStrikeResults.current
        plane           = Plane3d(V3d.NaN, nan)
        dipAngle        = nan
        dipDirection    = V3d.NaN
        strikeDirection = V3d.NaN
        dipAzimuth      = nan
        strikeAzimuth   = nan
        centerOfMass    = V3d.OOO
        error           = nanStatistics
        regressionInfo  = None
    }

let private polygon () =
    let a = Annotation.make Projection.Linear None Geometry.Polygon None { c = C4b.White } Annotation.Initial.thickness ""
    { a with points = IndexList.ofList [ V3d(0.0, 0.0, 0.0); V3d(1.0, 0.0, 0.0); V3d(1.0, 1.0, 0.0) ] }

let private roundTrip (x : 'a) : 'a =
    x |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty |> Json.parse |> Json.deserialize

let private finite (d : DipAndStrikeResults) = DipAndStrikeResults.isFinite d

let private withValue (n : NumericInput) v = { n with value = v }

type FrustumProbe = { f : Frustum } with
    static member ToJson (x : FrustumProbe) = Json.writeWith Ext.toJson<Frustum, Ext> "f" x.f
    static member FromJson (_ : FrustumProbe) =
        json {
            let! f = Json.readWith Ext.fromJson<Frustum, Ext> "f"
            return { f = f }
        }

type Probe = { v : float } with
    static member ToJson (x : Probe) =
        json {
            do! Json.writeFloatOr 7.0 "or" x.v
            do! Json.writeFloat "float" x.v
        }

let tests () =
    testList "save robustness (#834)" [

        testList "serialization never throws" [

            test "an annotation with a NaN dip & strike saves, and loads with no dip & strike" {
                let a = { polygon () with dnsResults = Some nanDns; fillAlpha = withValue (polygon ()).fillAlpha nan }
                let loaded : Annotation = roundTrip a
                Expect.isNone loaded.dnsResults "a dip & strike with NaN angles is undefined and saved as none"
                Expect.isTrue (Double.IsNaN loaded.fillAlpha.value || Double.IsFinite loaded.fillAlpha.value) "fill alpha loads"
            }

            test "a NaN dip & strike written on its own stays readable by plain Json.read" {
                // older releases read these with Json.read: a null would fail the whole file
                let json = nanDns |> Json.serialize |> Json.format
                match Json.parse json with
                | Json.Object m ->
                    for key in [ "dipAngle"; "dipAzimuth"; "strikeAzimuth" ] do
                        match m |> Map.tryFind key with
                        | Some (Json.Number _) -> ()
                        | other -> failtestf "%s must be a number for plain Json.read, was %A" key other
                | other -> failtestf "%A" other
                let _ : DipAndStrikeResults = json |> Json.parse |> Json.deserialize
                ()
            }

            test "non-finite and huge floats are written as finite numbers or null" {
                for v in [ nan; Double.PositiveInfinity; Double.NegativeInfinity; Double.MaxValue; 1e300; -1e300 ] do
                    match Json.serialize { Probe.v = v } |> Json.format |> Json.parse with
                    | Json.Object m ->
                        match m |> Map.tryFind "or", m |> Map.tryFind "float" with
                        | Some (Json.Number a), Some (Json.Number b) ->
                            Expect.isTrue (abs (float a) <= Json.maxWritableFloat) "writeFloatOr clamps"
                            Expect.isTrue (abs (float b) <= Json.maxWritableFloat) "writeFloat clamps"
                        | Some (Json.Number a), Some (Json.Null _) ->
                            Expect.isFalse (Double.IsFinite v) "null only for non-finite"
                            Expect.equal (float a) 7.0 "writeFloatOr falls back"
                        | other -> failtestf "%f wrote %A" v other
                    | other -> failtestf "%A" other
            }

            test "a NaN frustum writes as a finite default" {
                let f = { Frustum.perspective 60.0 0.1 100.0 1.0 with top = nan; bottom = Double.PositiveInfinity }
                let json = Json.serialize { FrustumProbe.f = f } |> Json.format
                Expect.isFalse (json.Contains "null") json
                let back = (json |> Json.parse |> Json.deserialize : FrustumProbe).f
                Expect.isTrue (Double.IsFinite back.top && Double.IsFinite back.bottom) "finite after the round trip"
            }
        ]

        testList "NaN sources" [

            test "manual dip on a polygon whose azimuth was never set gives no dip & strike" {
                let a = { polygon () with manualDipAngle = withValue (polygon ()).manualDipAngle 30.0 }
                Expect.isTrue (Double.IsNaN a.manualDipAzimuth.value) "precondition: azimuth starts as NaN"
                Expect.isNone (DipAndStrike.calculateManualDipAndStrikeResults V3d.OOI V3d.OIO a) "no NaN dip & strike"
            }

            test "a manual dip of exactly 90 degrees is finite for any up vector" {
                let rnd = RandomSystem(834)
                let dir () = V3d(rnd.UniformDouble() - 0.5, rnd.UniformDouble() - 0.5, rnd.UniformDouble() - 0.5).Normalized
                for _ in 1 .. 2000 do
                    let up = dir ()
                    let north = (up.Cross(dir ())).Normalized
                    let a = { polygon () with
                                geometry         = Geometry.TT
                                manualDipAngle   = withValue (polygon ()).manualDipAngle 90.0
                                manualDipAzimuth = withValue (polygon ()).manualDipAzimuth (rnd.UniformDouble() * 360.0) }
                    match DipAndStrike.calculateManualDipAndStrikeResults up north a with
                    | Some d -> Expect.isTrue (finite d) (sprintf "up %A: %A" up d)
                    | None -> failtestf "a valid manual dip gave none for up %A" up
            }

            test "a horizontal plane dips 0 with finite azimuths" {
                let att = DipAndStrike.attitudeFromNormal V3d.OOI V3d.OIO V3d.OOI
                Expect.floatClose Accuracy.medium att.dipAngle 0.0 "dip"
                Expect.isTrue (Double.IsFinite att.dipAzimuth && Double.IsFinite att.strikeAzimuth) (sprintf "%A" att)
            }

            test "editing up through (0,0,0) keeps the last direction" {
                let rs = ReferenceSystem.initial
                // drive every component to zero, one at a time, as the UI does
                let step (m : ReferenceSystem) act =
                    ReferenceSystemApp.update ViewConfigModel.initial LenseConfigs.referenceSystemConfig m act |> fst
                let m =
                    [ Vector3d.Action.SetX (Numeric.Action.SetValue 0.0)
                      Vector3d.Action.SetY (Numeric.Action.SetValue 0.0)
                      Vector3d.Action.SetZ (Numeric.Action.SetValue 0.0) ]
                    |> List.fold (fun m a -> step m (ReferenceSystemAction.SetUp a)) rs
                Expect.isFalse (m.up.value.AnyNaN) "up never becomes NaN"
                Expect.isTrue (m.up.value.Length > 0.5) (sprintf "up stays a direction: %A" m.up.value)
            }
        ]

        testList "the save itself" [

            test "Save As without extension writes <name>.pro3d next to its bookmark folder, nothing left half-written" {
                let dir = Path.Combine(Path.GetTempPath(), "pro3d-834-" + string (Guid.NewGuid()))
                Directory.CreateDirectory dir |> ignore
                try
                    let cts = new CancellationTokenSource()
                    let mailbox = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)
                    let m =
                        Viewer.initial mailbox StartupArgs.initArgs "" 1 dir ViewerLenses._animator "tests"
                    let a = { polygon () with dnsResults = Some nanDns }
                    let annotations = GroupsApp.addLeafToActiveGroup (Leaf.Annotations a) true m.drawing.annotations
                    let m = { m with drawing = { m.drawing with annotations = annotations } }

                    let target = Path.Combine(dir, "noextension")
                    match ViewerIO.saveEverything target m with
                    | Result.Error e -> failtestf "save failed: %s" e
                    | Result.Ok (saved, outcome) ->
                        Expect.equal outcome.path (target + ".pro3d") "extension added"
                        Expect.isEmpty outcome.problems "nothing left out"
                        Expect.isTrue (File.Exists (target + ".pro3d")) "scene file"
                        Expect.isTrue (File.Exists (target + ".pro3d.ann")) "annotation file"
                        Expect.isTrue (Directory.Exists target) "bookmark folder beside it"
                        Expect.isEmpty (Directory.GetFiles(dir, "*.saving")) "no temp files left"
                        Expect.equal saved.scene.scenePath (Some (target + ".pro3d")) "model points to the saved scene"
                        match ViewerIO.tryLoadAnnotations (target + ".pro3d.ann") with
                        | Some loaded ->
                            let back = loaded.annotations.flat |> HashMap.tryFind a.key
                            match back with
                            | Some (Leaf.Annotations b) -> Expect.isNone b.dnsResults "NaN dip & strike loads as none"
                            | other -> failtestf "annotation missing after load: %A" other
                        | None -> failtest "annotations did not load"
                    cts.Cancel()
                finally
                    try Directory.Delete(dir, true) with _ -> ()
            }
        ]
    ]
