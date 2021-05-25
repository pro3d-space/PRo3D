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

type AreaComparisonAction =
    | UpdateStatistics
    | AreaSelectionMessage of AreaSelectionAction

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AreaComparison =
    let update (m : AreaComparison) (action : AreaComparisonAction) =
        match action with
        | UpdateStatistics -> m
        | AreaSelectionMessage msg -> 
            let area = AreaSelection.update m.area msg
            {m with area = area}
