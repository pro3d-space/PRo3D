/// Rung 2 of the map projection testing ladder (#772): the map effects generate GLSL, with
/// no GL context. FShade decompiles shader bodies at runtime, so a construct that type-checks
/// (a loop in a geometry stage, a helper reading a uniform) can still fail only when the
/// panel is first opened.
module MapProjectionShaderTest

open Expecto

open PRo3D.MapProjection

let private effects =
    [
        "surface equirectangular", Shaders.surfaceEffect MapProjectionKind.Equirectangular
        "surface polar",           Shaders.surfaceEffect MapProjectionKind.PolarNorth
        "position equirectangular", Shaders.bodyPositionEffect MapProjectionKind.Equirectangular
        "position polar",           Shaders.bodyPositionEffect MapProjectionKind.PolarNorth
    ]

let tests () =
    testList "map projection shaders (#772)" [

        test "every map effect generates GLSL with its geometry stage" {
            for name, effect in effects do
                let code = OutcropTraceShaderTest.compile name effect
                let lines = code.Split('\n').Length
                printfn "map projection, %s: %d lines of GLSL" name lines
                Expect.isTrue (code.Contains "#ifdef Geometry") (sprintf "%s has a geometry stage" name)
                Expect.isLessThan lines 1500 (sprintf "%s: %d lines of GLSL" name lines)
        }

        test "the map effects read only attributes an OPC patch provides" {
            // an extra input would become a vertex attribute PatchNode does not have, which
            // renders garbage on some drivers instead of failing
            let provided = Set.ofList [ "Positions"; "DiffuseColorCoordinates" ]
            for name, effect in effects do
                let inputs = effect.Inputs |> Map.keys |> Set.ofSeq
                Expect.isEmpty (Set.difference inputs provided) (sprintf "%s reads only %A" name provided)
        }

        test "the graticule effect generates GLSL" {
            OutcropTraceShaderTest.compile "graticule" Shaders.graticuleEffect |> ignore
        }
    ]
