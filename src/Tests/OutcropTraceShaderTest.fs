/// Does the outcrop-trace effect actually generate GLSL?
///
/// F# compiling is not enough: FShade decompiles the shader body and emits GLSL at runtime,
/// so a construct that type-checks can still fail when the effect is first used - which in
/// the app means a blank render, far from this code. This runs the real code generation for
/// the whole surface effect stack, with no GL context and no window.
module OutcropTraceShaderTest

open Expecto

open Aardvark.Base
open Aardvark.Rendering
open FShade

open PRo3D

let private glslBackend =
    FShade.GLSL.Backend.Create {
        version                     = FShade.GLSL.GLSLVersion(4, 1, 0)
        enabledExtensions           = Set.empty
        availableExtensions         = Map.empty
        createUniformBuffers        = true
        pushConstants               = false
        bindingMode                 = FShade.GLSL.BindingMode.None
        createDescriptorSets        = false
        stepDescriptorSets          = false
        createInputLocations        = true
        createOutputLocations       = true
        createPassingLocations      = true
        createPerStageUniforms      = false
        reverseMatrixLogic          = true
        reverseTessellationWinding  = false
        separateTexturesAndSamplers = false
        depthWriteMode              = false
        useInOut                    = true
    }

let private compile (name : string) (effect : Effect) =
    let signature =
        effect
        |> Effect.toModule {
            EffectConfig.empty with
                outputs = Map.ofList [ "Colors", (typeof<V4f>, 0) ]
        }
    let glsl = ModuleCompiler.compileGLSL glslBackend signature
    Expect.isGreaterThan glsl.code.Length 0 (sprintf "%s should produce GLSL" name)
    glsl.code

let tests () =
    testList "outcrop trace shader" [

        test "the outcrop trace fragment shader generates GLSL" {
            let code = compile "outcropTrace" (Effect.ofFunction ViewerUtils.OutcropTraceShader.outcropTrace)
            // the screen-space derivatives are the part most likely to fail codegen
            Expect.stringContains code "dFdx" "the antialiasing derivative should reach the GLSL"
        }

        test "the full surface effect stack generates GLSL" {
            // catches an incompatible varying or a duplicated semantic introduced by adding
            // a stage to the stack, which is otherwise only visible as a blank render
            compile "surfaceEffect" ViewerUtils.surfaceEffect |> ignore
        }

        test "the obj effect stack generates GLSL" {
            compile "objEffect" ViewerUtils.objEffect |> ignore
        }
    ]
