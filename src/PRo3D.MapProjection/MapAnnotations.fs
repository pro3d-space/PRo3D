namespace PRo3D.MapProjection

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation
open PRo3D.Core
open PRo3D.Core.Drawing

/// Annotations on the map (phase 2): PRo3D's packed annotation buffers (`PackedRendering`),
/// drawn with an identity view and the map shader stages (`Shaders.annotation*`), on top of the
/// surfaces and the graticule. No depth test: an annotation on a hidden layer of a
/// non-star-shaped body still shows.
module MapAnnotations =

    type AnnotationInputs =
        {
            annotations     : aset<Guid * AdaptiveAnnotation>
            colorByCategory : AdaptiveColorByCategoryModel
            /// drawn highlighted, as in the 3D view
            selected        : aset<Guid>
        }

    let none =
        {
            annotations     = ASet.empty
            colorByCategory = ColorByCategory.disabled
            selected        = ASet.empty
        }

    /// Annotations for the standalone app: an SBMT structure file (points, ellipses, circles;
    /// SBMT lines and polygons are not imported yet) in `frame`, or a PRo3D annotation file.
    let load (frame : string) (path : string) : list<Annotation> =
        match SbmtImporter.detectStructureType path with
        | Some _ ->
            SbmtImporter.startImporter Trafo3d.Identity frame path |> IndexList.toList
        | None ->
            (DrawingUtilities.IO.loadAnnotationsFromFile path).annotations.flat
            |> HashMap.toList
            |> List.choose (fun (_, leaf) ->
                match leaf with
                | Leaf.Annotations a -> Some a
                | _ -> None)

    let ofList (annotations : list<Annotation>) : aset<Guid * AdaptiveAnnotation> =
        annotations |> List.map (fun a -> a.key, AdaptiveAnnotation a) |> ASet.ofList

    /// With an identity view the packers' `MV` is the pivot and point positions are body-centred.
    let private identityView = AVal.constant M44d.Identity
    let private noDepthOffset = AVal.constant 0.0
    let private noHover = AVal.constant -1

    let fillsPass  = RenderPass.after "map-annotation-fills"  RenderPassOrder.Arbitrary MapSg.graticulePass
    let linesPass  = RenderPass.after "map-annotation-lines"  RenderPassOrder.Arbitrary fillsPass
    let pointsPass = RenderPass.after "map-annotation-points" RenderPassOrder.Arbitrary linesPass

    let sg (inputs : AnnotationInputs) (view : MapSg.MapView) : ISg =
        let ordered = PackedRendering.orderedAnnotations inputs.annotations
        let lines, _, _ = PackedRendering.linesNoIndirect inputs.colorByCategory noDepthOffset noHover inputs.selected ordered identityView
        let fills = PackedRendering.fills inputs.colorByCategory noDepthOffset ordered identityView
        let points = PackedRendering.pointsGeometry inputs.colorByCategory inputs.selected inputs.annotations noDepthOffset identityView

        // one effect per projection kind, switched without rebuilding, like the surfaces
        let byKind (effect : MapProjectionKind -> FShade.Effect) (s : ISg) =
            s |> Sg.effectPool [| effect MapProjectionKind.Equirectangular; effect MapProjectionKind.PolarNorth |]
                               (view.kind |> AVal.map MapSg.effectIndex)

        Sg.ofList [
            fills
            |> byKind Shaders.annotationFillEffect
            |> Sg.blendMode (AVal.constant BlendMode.Blend)
            |> Sg.pass fillsPass

            lines
            |> byKind Shaders.annotationLineEffect
            |> Sg.pass linesPass

            points
            |> byKind Shaders.annotationPointEffect
            |> Sg.pass pointsPass
        ]
        |> Sg.depthTest (AVal.constant DepthTest.None)
        |> Sg.cullMode (AVal.constant CullMode.None)
        |> MapSg.withMapUniforms view

    /// The whole map: surfaces, graticule, annotations.
    let mapWithAnnotations (cfg : OpcSg.Config) (view : MapSg.MapView) (camera : aval<Option<V3d>>) (surfaces : aset<MapSg.MapSurface>) (inputs : AnnotationInputs) : ISg =
        Sg.ofList [ MapSg.map cfg view camera surfaces; sg inputs view ]
