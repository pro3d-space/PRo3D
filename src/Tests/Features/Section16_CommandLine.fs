/// Section 16 — Command Line Interface
///   TC-16.1 (CLI — Snapshot Rendering)
///
///   The full snapshot render is a headless render loop and an external process;
///   what is pure logic — and what a broken command line would break first — is the
///   argument parsing, exercised here through the real CommandLine.parseArguments.
module PRo3D.Tests.Section16_CommandLine

open Expecto

open PRo3D.SimulatedViews                 // CommandLine, SnapshotType
open PRo3D.Tests

let tests =
    testList "Section 16 — Command Line Interface" [

        // TC-16.1 Snapshot Rendering — argument parsing

        test "TC-16.1 --opc parses a single OPC path" {
            let a = CommandLine.parseArguments [| "--opc"; "some/opc"; "--asnap"; "anim.json" |]
            Expect.equal a.opcPaths (Some [ "some/opc" ]) "the OPC path should be parsed"
        }

        test "TC-16.1 --opc with a semicolon parses several OPC paths" {
            let a = CommandLine.parseArguments [| "--opc"; "opcA;opcB" |]
            Expect.equal a.opcPaths (Some [ "opcA"; "opcB" ]) "both OPC paths should be parsed"
        }

        test "TC-16.1 --obj parses an OBJ path" {
            let a = CommandLine.parseArguments [| "--obj"; "model.obj" |]
            Expect.equal a.objPaths (Some [ "model.obj" ]) "the OBJ path should be parsed"
        }

        test "TC-16.1 --asnap sets the snapshot path and the CameraAndSurface type" {
            let a = CommandLine.parseArguments [| "--opc"; "o"; "--asnap"; "anim.json" |]
            Expect.equal a.snapshotPath (Some "anim.json") "the snapshot path should be parsed"
            Expect.equal a.snapshotType (Some SnapshotType.CameraAndSurface)
                "an animation snapshot implies the CameraAndSurface type"
        }

        test "TC-16.1 --exitOnFinish and --renderDepth flags are recognised" {
            let a = CommandLine.parseArguments [| "--opc"; "o"; "--exitOnFinish"; "--renderDepth" |]
            Expect.isTrue a.exitOnFinish "--exitOnFinish should be set"
            Expect.isTrue a.renderDepth "--renderDepth should be set"
        }

        test "TC-16.1 an empty command line is valid (interactive start)" {
            let a = CommandLine.parseArguments [||]
            Expect.isTrue a.areValid "no arguments means a normal interactive start"
        }
    ]
