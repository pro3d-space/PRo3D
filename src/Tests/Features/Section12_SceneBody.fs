/// Section 12 — GIS View, the scene body at viewer level (#758, docs/SceneBody.md)
///   TC-12.5 the planet and the GIS observation stay one setting through the real
///   ViewerApp.updateViewer, scene load (SceneBodySync.reconcileOnLoad) and bookmark
///   playback (ViewerLenses._bookmark).
///
///   None of this touches the renderer, so the model is built headless with
///   Viewer.initial and updateViewer gets a placeholder runtime, as in Section 18.
module PRo3D.Tests.Section12_SceneBody

open System
open System.Collections.Concurrent
open System.Threading

open Aardvark.Base
open Aardvark.Rendering

open Expecto

open PRo3D
open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Core
open PRo3D.Core.Gis
open PRo3D.Core.SequencedBookmarks
open PRo3D.Viewer
open PRo3D.Tests

module private Head =

    let make () =
        let cts       = new CancellationTokenSource()
        let mailbox   = MailboxProcessor.Start(Viewer.initMessageLoop cts, cts.Token)
        let sendQueue = new BlockingCollection<string>()
        let model =
            Viewer.initial mailbox StartupArgs.initArgs "" 1 "." ViewerLenses._animator "tests"
        let update (m : Model) (msg : ViewerAction) =
            ViewerApp.updateViewer
                (Unchecked.defaultof<IRuntime>) (Unchecked.defaultof<IFramebufferSignature>)
                sendQueue mailbox m msg
        model, update

    let setPlanet p = ViewerAction.ReferenceSystemMessage (ReferenceSystemAction.SetPlanet p)
    let observe body = ViewerAction.GisAppMessage (GisAppAction.ObservationInfoMessage (ObservationInfoAction.SetObserver body))
    let frame f = ViewerAction.GisAppMessage (GisAppAction.ObservationInfoMessage (ObservationInfoAction.SetReferenceFrame f))

    let withObservation (observer : Option<string>) (frame : Option<string>) (planet : Planet) (m : Model) =
        let info =
            { m.scene.gisApp.defaultObservationInfo with
                observer       = observer |> Option.map EntitySpiceName
                referenceFrame = frame |> Option.map FrameSpiceName }
        { m with
            scene =
                { m.scene with
                    gisApp = { m.scene.gisApp with defaultObservationInfo = info }
                    referenceSystem = { m.scene.referenceSystem with planet = planet } } }

    let observation (m : Model) =
        let info = m.scene.gisApp.defaultObservationInfo
        info.observer |> Option.map (fun o -> o.Value), info.referenceFrame |> Option.map (fun f -> f.Value)

let private dimorphos = Some "Dimorphos", Some "DIMORPHOS_FIXED"

let tests =
    testList "Section 12 — GIS View: scene body (viewer)" [

        test "TC-12.5 picking the planet points the GIS at its body in its fixed frame" {
            let m, update = Head.make ()
            let m = update m (Head.setPlanet Planet.Dimorphos)
            Expect.equal m.scene.referenceSystem.planet Planet.Dimorphos "planet"
            Expect.equal (Head.observation m) dimorphos "observation"
        }

        test "TC-12.5 a planet that is no body ends a body-fixed observation" {
            let m, update = Head.make ()
            let m = update m (Head.setPlanet Planet.Dimorphos)
            let m = update m (Head.setPlanet Planet.ENU)
            Expect.equal m.scene.referenceSystem.planet Planet.ENU "planet"
            Expect.equal (Head.observation m) (None, None) "no observation"
        }

        test "TC-12.5 ...but leaves an observation that is not body-fixed alone" {
            let m, update = Head.make ()
            let legacy = m |> Head.withObservation (Some "Dimorphos") (Some "J2000") Planet.None
            let m = update legacy (Head.setPlanet Planet.ENU)
            Expect.equal (Head.observation m) (Some "Dimorphos", Some "J2000") "a J2000 scene keeps its observation"
            let hera = m |> Head.withObservation (Some "HERA") (Some "HERA_SPACECRAFT") Planet.ENU
            let m = update hera (Head.setPlanet Planet.JPL)
            Expect.equal (Head.observation m) (Some "HERA", Some "HERA_SPACECRAFT") "a spacecraft observation too"
        }

        test "TC-12.5 picking the observed body snaps the frame and sets the planet" {
            let m, update = Head.make ()
            let m = update m (Head.observe (Some (EntitySpiceName "Dimorphos")))
            Expect.equal (Head.observation m) dimorphos "observation"
            Expect.equal m.scene.referenceSystem.planet Planet.Dimorphos "planet follows"
        }

        test "TC-12.5 observing a spacecraft leaves no planet; clearing the observation keeps it" {
            let m, update = Head.make ()
            let m = update m (Head.observe (Some (EntitySpiceName "Dimorphos")))
            let cleared = update m (Head.observe None)
            Expect.equal cleared.scene.referenceSystem.planet Planet.Dimorphos "clearing the GIS keeps the planet"
            let hera = update m (Head.observe (Some (EntitySpiceName "HERA")))
            Expect.equal hera.scene.referenceSystem.planet Planet.None "a spacecraft is no planet"
        }

        test "TC-12.5 the legacy switch (frame := the body's fixed frame) makes the scene body-fixed" {
            let m, update = Head.make ()
            let legacy = m |> Head.withObservation (Some "Dimorphos") (Some "J2000") Planet.None
            let m = update legacy (Head.frame (Some (FrameSpiceName "DIMORPHOS_FIXED")))
            Expect.equal m.scene.referenceSystem.planet Planet.Dimorphos "planet follows the switch"
        }

        test "TC-12.5 scene load fills in the planet of a body-fixed observation, and nothing else" {
            let m, _ = Head.make ()
            let gisOnly = m |> Head.withObservation (Some "Dimorphos") (Some "DIMORPHOS_FIXED") Planet.None
            Expect.equal (SceneBodySync.reconcileOnLoad gisOnly).scene.referenceSystem.planet Planet.Dimorphos "filled in"
            let j2000 = m |> Head.withObservation (Some "Dimorphos") (Some "J2000") Planet.Mars
            Expect.equal (SceneBodySync.reconcileOnLoad j2000).scene.referenceSystem.planet Planet.Mars "a J2000 scene loads as saved"
            let plain = m |> Head.withObservation None None Planet.Mars
            let reconciled = SceneBodySync.reconcileOnLoad plain
            Expect.equal reconciled.scene.referenceSystem.planet Planet.Mars "a plain Mars scene stays"
            Expect.equal (Head.observation reconciled) (None, None) "and gets no observation"
        }

        test "TC-12.5 a bookmark contributes time and camera source, never the observed body or frame" {
            let m, update = Head.make ()
            let m = update m (Head.setPlanet Planet.Dimorphos)
            let t = DateTime(2027, 3, 21, 17, 0, 0, DateTimeKind.Utc)
            let bookmark : Bookmark =
                { version = Bookmark.current; key = Guid.NewGuid(); name = "gis"
                  cameraView = CameraView.lookAt (V3d(0.0, -500.0, 0.0)) V3d.Zero V3d.OOI
                  exploreCenter = V3d.Zero; navigationMode = NavigationMode.FreeFly }
            let info =
                { ObservationInfo.initial with
                    observer = Some (EntitySpiceName "HERA")
                    referenceFrame = Some (FrameSpiceName "J2000")
                    target = None
                    time = { ObservationInfo.initial.time with date = t } }
            let bm = { SequencedBookmarkModel.init bookmark with observationInfo = Some info }
            let m = (snd ViewerLenses._bookmark) (SequencedBookmark.LoadedBookmark bm) m
            Expect.equal (Head.observation m) dimorphos "the scene body stays the scene's"
            Expect.equal m.scene.gisApp.defaultObservationInfo.time.date t "the bookmark's time"
            Expect.equal m.navigation.camera.view.Location bookmark.cameraView.Location "no camera source: the bookmark's camera stands"
        }

        test "TC-12.5 the mission time slider re-aims the camera from the camera source, unless off" {
            let kernel = IO.Path.Combine(HeraSpiceTests.mkDir, "hera_plan.tm")
            if not (IO.File.Exists kernel) then
                skiptest (sprintf "HERA planning kernel not found at %s" kernel)
            HeraSpiceTests.ensureKernelAt [ kernel ]
            let m, update = Head.make ()
            let m = update m (Head.setPlanet Planet.Dimorphos)
            let m = update m (ViewerAction.GisAppMessage (GisAppAction.ObservationInfoMessage (ObservationInfoAction.SetTarget (Some (EntitySpiceName "HERA")))))
            let row =
                m.scene.gisApp.missionTimesEntries
                |> Option.bind (fun entries ->
                    entries |> FSharp.Data.Adaptive.IndexList.toSeqIndexed |> Seq.tryFind (fun (_, e) -> e.name = "Didymos Orbital Insertion"))
            match row with
            | None -> failtest "no Didymos mission time entry"
            | Some (idx, entry) ->
                let slide v = ViewerAction.GisAppMessage (GisAppAction.SetTime (entry, idx, v))
                let a = update m (slide 0.2)
                let b = update a (slide 0.8)
                Expect.isGreaterThan (Vec.distance a.navigation.camera.view.Location b.navigation.camera.view.Location) 1e-3
                    "sliding the time moves the camera with the camera source body"
                let expected = GisApp.lookAtObserver b.scene.gisApp |> Option.map (fun c -> c.Location)
                Expect.equal (Some b.navigation.camera.view.Location) expected "the camera looks from HERA at Dimorphos"

                let off = update b (ViewerAction.GisAppMessage GisAppAction.ToggleCameraFollowsSource)
                let c = update off (slide 0.3)
                Expect.notEqual c.scene.gisApp.defaultObservationInfo.time.date b.scene.gisApp.defaultObservationInfo.time.date "the time still moves"
                Expect.equal c.navigation.camera.view.Location b.navigation.camera.view.Location "off: the camera stays"

                let r = update c (ViewerAction.GisAppMessage (GisAppAction.ObservationInfoMessage ObservationInfoAction.Reset))
                let expected = GisApp.lookAtObserver r.scene.gisApp |> Option.map (fun c -> c.Location)
                Expect.equal (Some r.navigation.camera.view.Location) expected "Re-use settings above re-aims even when off"
        }

        test "TC-12.5 a camera source equal to the observed body does not move the camera" {
            let m, update = Head.make ()
            let m = update m (Head.setPlanet Planet.Dimorphos)
            let before = m.navigation.camera.view
            let m = update m (ViewerAction.GisAppMessage (GisAppAction.ObservationInfoMessage (ObservationInfoAction.SetTarget (Some (EntitySpiceName "DIMORPHOS")))))
            Expect.equal m.navigation.camera.view.Location before.Location "no look-at from the body at itself"
        }
    ]
