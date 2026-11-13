module PRo3D.Tests.InstrumentFovTests

open System
open System.IO
open System.Text.RegularExpressions
open Aardvark.Base
open Aardvark.UI.Primitives
open Chiron
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Core.Gis
open PRo3D.ImageMapping
open PRo3D.SPICE
// last: an earlier open shadows Expecto's `test`
open Expecto

/// PRO3D_SPICE_KERNELS' ik directory, if set (it may name the tree or its `kernels` dir).
let private ikDir () =
    match Environment.GetEnvironmentVariable "PRO3D_SPICE_KERNELS" with
    | null | "" -> None
    | k ->
        [ Path.Combine(k, "ik"); Path.Combine(k, "kernels", "ik") ]
        |> List.tryFind Directory.Exists

/// INS-<id>_<key> = ( <number> ) from an IK's data section.
let private ikNumber (file : string) (id : int) (key : string) =
    let m = Regex.Match(File.ReadAllText file, sprintf @"INS-%d_%s\s*=\s*\(\s*([-+0-9.eE]+)\s*\)" id key)
    if m.Success then Some (Double.Parse(m.Groups.[1].Value, Globalization.CultureInfo.InvariantCulture)) else None

let private fovOf name =
    match Map.tryFind name InstrumentProjection.defaultFovs with
    | Some f -> f
    | None -> failtestf "no default for %s" name

let private roundtrip (m : GisApp) =
    let s = m |> Json.serialize |> Json.formatWith JsonFormattingOptions.SingleLine
    s, (s |> Json.parse |> Json.deserialize : GisApp)

let private withFovs (fovs : HashMap<string, InstrumentFov>) (m : GisApp) =
    { m with projectedImageList = { m.projectedImageList with instrumentFovs = fovs } }

let private set (v : float) = Numeric.Action.SetValue v

[<Tests>]
let tests =
    testList "Instrument field of view (#882)" [

        test "the defaults are the instrument kernels' FOV definitions" {
            match ikDir () with
            | None -> skiptest "PRO3D_SPICE_KERNELS not set"
            | Some ik ->
                // (frame, NAIF id, width taken from REF (true) or CROSS (false)).
                // REF_VECTOR is +X in all; AFC and TIRI have the image width along X,
                // HSH has its width (the long axis) along Y; its id is the paraxial field.
                let iks = Directory.GetFiles(ik, "*.ti")
                for name, id, widthIsRef in
                        [ "HERA_AFC-1", 91110, true
                          "HERA_AFC-2", 91120, true
                          "HERA_HSH",   91401, false
                          "HERA_TIRI",  91200, true ] do
                    let key = sprintf "INS-%d_FOV_REF_ANGLE" id
                    let path =
                        match iks |> Array.tryFind (fun f -> File.ReadAllText(f).Contains key) with
                        | Some p -> p
                        | None -> failtestf "no IK under %s defines %s" ik key
                    match ikNumber path id "FOV_REF_ANGLE", ikNumber path id "FOV_CROSS_ANGLE" with
                    | Some r, Some c ->
                        let w, h = if widthIsRef then r, c else c, r
                        let f = fovOf name
                        Expect.floatClose Accuracy.high f.horizontal (2.0 * w) (sprintf "%s width" name)
                        Expect.floatClose Accuracy.high f.vertical (2.0 * h) (sprintf "%s height" name)
                    | _ -> failtestf "%s: FOV angles of INS-%d not found" path id
        }

        test "a frustum spans exactly its horizontal and vertical field" {
            let fov : InstrumentProjection.Fov = { horizontal = 15.5; vertical = 8.3 }
            let f = InstrumentProjection.frustumOf fov 1.0 10.0
            Expect.floatClose Accuracy.high f.right (tan (Conversion.RadiansFromDegrees 7.75)) "half width"
            Expect.floatClose Accuracy.high f.top (tan (Conversion.RadiansFromDegrees 4.15)) "half height"
            Expect.floatClose Accuracy.high f.left (-f.right) "centred"
        }

        test "scene values override the kernel per instrument, the rest stay" {
            let over = Map.ofList [ "HERA_AFC-1", ({ horizontal = 5.509; vertical = 5.509 } : InstrumentProjection.Fov) ]
            let d = InstrumentProjection.instruments 1.0 10.0
            let o = InstrumentProjection.instrumentsWith over 1.0 10.0
            match Map.tryFind "HERA_AFC-1" o, Map.tryFind "HERA_AFC-2" o, Map.tryFind "HERA_AFC-2" d with
            | Some a1, Some a2, Some a2d ->
                Expect.floatClose Accuracy.high a1.right (tan (Conversion.RadiansFromDegrees (5.509 / 2.0))) "AFC-1 overridden"
                Expect.equal a2 a2d "AFC-2 untouched"
            | _ -> failtest "frusta missing"
            Expect.equal (InstrumentProjection.instrumentsWith Map.empty 1.0 10.0) d "no overrides = the defaults"
        }

        test "editing and resetting an instrument" {
            let fovs = InstrumentFov.defaults
            let edited = InstrumentFovApp.update fovs (SetHorizontal ("HERA_HSH", set 15.9))
            let edited = InstrumentFovApp.update edited (SetVertical ("HERA_HSH", set 9.9))
            match HashMap.tryFind "HERA_HSH" edited with
            | Some f ->
                Expect.equal (InstrumentFov.toFov f) { horizontal = 15.9; vertical = 9.9 } "edited"
                Expect.isTrue (InstrumentFov.isOverride f) "differs from the kernel"
            | None -> failtest "HSH gone"
            let reset = InstrumentFovApp.update edited (Reset "HERA_HSH")
            Expect.equal (InstrumentFov.toMap reset) (InstrumentFov.toMap fovs) "back to the kernel"
            Expect.equal (InstrumentFovApp.update fovs (SetHorizontal ("NO_SUCH", set 1.0))) fovs "unknown instrument: no-op"
        }

        test "only changed instruments are saved, under their own key, and read back" {
            let m = GisApp.initial None
            let unset, restoredUnset = roundtrip m
            Expect.isFalse (unset.Contains "instrumentFovs") "nothing changed: not written (old scenes look the same)"
            Expect.equal (InstrumentFov.toMap restoredUnset.projectedImageList.instrumentFovs) InstrumentProjection.defaultFovs "kernel defaults"

            let changed = InstrumentFovApp.update InstrumentFov.defaults (SetHorizontal ("HERA_AFC-1", set 5.509))
            let s, restored = roundtrip (withFovs changed m)
            Expect.stringContains s "instrumentFovs" "own key"
            Expect.isFalse (s.Contains "HERA_AFC-2") "unchanged instruments are not written"
            Expect.equal (InstrumentFov.toMap restored.projectedImageList.instrumentFovs) (InstrumentFov.toMap changed) "read back"
        }

        test "unusable saved angles fall back to the kernel" {
            let m = GisApp.initial None
            let s, _ = roundtrip m
            let bad =
                s.TrimEnd('}') + ",\"instrumentFovs\":[{\"instrument\":\"HERA_AFC-1\",\"horizontal\":null,\"vertical\":5.0},{\"instrument\":\"HERA_TIRI\",\"horizontal\":0,\"vertical\":5.0}]}"
            let restored : GisApp = bad |> Json.parse |> Json.deserialize
            Expect.equal (InstrumentFov.toMap restored.projectedImageList.instrumentFovs) InstrumentProjection.defaultFovs "defaults kept"
        }
    ]
