namespace PRo3D.Comparison

open Adaptify
open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive
open PRo3D.Base
open Aardvark.UI
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Base
open Aardvark.Rendering
open Adaptify.FSharp.Core
open Aardvark.GeoSpatial




type AreaSelectionAction =
    | DimensionsMessage of Aardvark.UI.Vector3d.Action
    | SetLocation of V3d
    | ToggleVisible


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AreaSelection =
    let update (m : AreaSelection) (action : AreaSelectionAction) =
        match action with
        | DimensionsMessage msg -> m
        | SetLocation location -> m
        | ToggleVisible ->
            {m with visible = not m.visible}

    let view (area : aval<Box3d>) =
        (Sg.wireBox (C4b.VRVisGreen |> AVal.constant) area) 
        |> Sg.noEvents
        |> Sg.effect [     
            Shader.stableTrafo' |> toEffect
            //Shader.stableTrafo |> toEffect 
            DefaultSurfaces.vertexColor |> toEffect
        ] 

