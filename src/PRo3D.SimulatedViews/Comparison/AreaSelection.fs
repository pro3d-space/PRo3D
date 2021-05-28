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
    | UpdateStatistics


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AreaSelection =
    let init guid : AreaSelection = 
        {
            id = guid
            dimensions = V3d.III * 3.0
            location   = V3d.OOO
            visible    = true
            selectedVertices = IndexList.empty
            statistics = None
        }

    let update (m : AreaSelection) (action : AreaSelectionAction) =
        match action with
        | DimensionsMessage msg -> m
        | SetLocation location -> m
        | ToggleVisible ->
            {m with visible = not m.visible}
        | UpdateStatistics -> 
            m


    let sg (m : AdaptiveAreaSelection) =
        let box = AVal.map2 (fun c s -> Box3d.FromCenterAndSize (c,s))
                            m.location m.dimensions 
         
        Sg.wireBox (C4b.VRVisGreen |> AVal.constant) box
          |> Sg.andAlso (Sg.drawPointList m.selectedVertices (C4b.Red |> AVal.constant) (4.0 |> AVal.constant) (0.0 |> AVal.constant))
          



