/// Rung 2 of the map projection testing ladder (#772): the map effects generate GLSL, with
/// no GL context. FShade decompiles shader bodies at runtime, so a construct that type-checks
/// (a loop in a geometry stage, a helper reading a uniform) can still fail only when the
/// panel is first opened.
module MapProjectionShaderTest

open Expecto

open Aardvark.Rendering
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
            // GeometrySourceVertexIndex is FShade's own (SourceVertexIndex pass-through), not an attribute
            let provided = Set.ofList [ "Positions"; "DiffuseColorCoordinates"; "GeometrySourceVertexIndex" ]
            for name, effect in effects do
                let inputs = effect.Inputs |> Map.keys |> Set.ofSeq
                Expect.isEmpty (Set.difference inputs provided) (sprintf "%s reads only %A, not %A" name provided (Set.difference inputs provided))
        }

        test "the annotation effects generate GLSL and read only what the packed buffers provide" {
            // PackedRendering.linesNoIndirect / fills / pointsGeometry attributes, plus FShade's own
            // (PointCoord is the gl_PointCoord built-in of point sprites)
            let provided = Set.ofList [ "Positions"; "Colors"; "LineWidth"; "ObjId"; "PickingTolerance"; "Sizes"; "GeometrySourceVertexIndex"; "PointCoord" ]
            for kind in [ MapProjectionKind.Equirectangular; MapProjectionKind.PolarNorth ] do
                for name, effect, hasGeometry in
                        [ "fills",  Shaders.annotationFillEffect kind,  true
                          "lines",  Shaders.annotationLineEffect kind,  true
                          "points", Shaders.annotationPointEffect kind, false ] do
                    let label = sprintf "annotation %s, %A" name kind
                    let code = OutcropTraceShaderTest.compile label effect
                    printfn "map projection, %s: %d lines of GLSL" label (code.Split(char 10).Length)
                    Expect.equal (code.Contains "#ifdef Geometry") hasGeometry (sprintf "%s: geometry stage" label)
                    let inputs = effect.Inputs |> Map.keys |> Set.ofSeq
                    // Effect.Inputs is FShade's pre-link view; compare with what PRo3D's own packed
                    // line pass reports for the same buffers, so only genuinely new inputs fail
                    let viewerLines =
                        FShade.Effect.compose [
                            toEffect PRo3D.Core.PackedRendering.LineShader.noIndirectLineVertex
                            toEffect PRo3D.Core.PackedRendering.LineShader.thickLine
                            toEffect PRo3D.Base.Shader.DepthOffset.depthOffsetFS ]
                    let known = Set.union provided (viewerLines.Inputs |> Map.keys |> Set.ofSeq)
                    Expect.isEmpty (Set.difference inputs known)
                        (sprintf "%s reads no attribute beyond the packed buffers: %A" label (Set.difference inputs known))
        }

        test "the graticule effect generates GLSL" {
            OutcropTraceShaderTest.compile "graticule" Shaders.graticuleEffect |> ignore
        }
    ]
