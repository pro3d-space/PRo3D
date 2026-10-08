namespace PRo3D.Composition

open System

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.UI
open FSharp.Data.Adaptive
open Adaptify.FSharp.Core
open OpcViewer.Base
open Aardvark.GeoSpatial.Opc

open PRo3D
open PRo3D.Base
open PRo3D.Base.Gis
open PRo3D.Core
open PRo3D.Core.Surface

/// Surface scene graphs a host can show without the Viewer's full effect stack (SPICE lighting,
/// image projection, shadows, footprints), plus the pick events that turn a click on a surface
/// into a host message.
module SurfaceView =

    /// When a click (or hover) on a surface becomes a message. The gates are read in the event
    /// handler, where forcing is allowed.
    type SurfacePickEvents<'msg> =
        {
            /// a drawing/picking tool owns the left button right now
            enabled : unit -> bool
            /// hover picks for the preview cursor
            preview : Option<unit -> bool>
            click   : SceneHit -> string -> 'msg
            move    : SceneHit -> string -> 'msg
        }

    /// Scene events of one surface. Tools are on the left button only: a right-drag that ends
    /// on a surface (Direct Tool Mode orbits with the right button) must not place a point.
    let pickEvents (events : SurfacePickEvents<'msg>) (name : aval<string>) : list<SceneEventKind * (SceneHit -> bool * seq<'msg>)> =
        [
            match events.preview with
            | Some previewEnabled ->
                yield SceneEventKind.Move, (fun sceneHit ->
                    if previewEnabled () && events.enabled () then
                        true, Seq.singleton (events.move sceneHit (AVal.force name))
                    else
                        true, Seq.empty
                )
            | None -> ()
            yield SceneEventKind.Click, (fun sceneHit ->
                let leftButton = (sceneHit.event.evtButtons = Aardvark.Application.MouseButtons.Left)
                if events.enabled () && leftButton then
                    true, Seq.singleton (events.click sceneHit (AVal.force name))
                else
                    true, Seq.empty
            )
        ]

    /// Level-of-detail parameters of a surface (its placement drives the LoD metric).
    let lodParameters
        (surf           : aval<AdaptiveSurface>)
        (refsys         : AdaptiveReferenceSystem)
        (observedSystem : aval<Option<SpiceReferenceSystem>>)
        (observerSystem : aval<Option<ObserverSystem>>)
        (frustum        : aval<Frustum>) =
        adaptive {
            let! s = surf
            let! frustum = frustum
            let sizes = V2i(1024,768)
            let! quality = s.quality.value
            let! trafo = TransformationApp.fullTrafo s.transformation refsys observedSystem observerSystem
            return { frustum = frustum; size = sizes; factor = quality; trafo = trafo }
        }

    /// Which texture layer and scalar the OPC patches show (the surface's *Primary Texture*
    /// and *Scalars* choice); the patch loader reads it per patch.
    let attributeParameters (surf : aval<AdaptiveSurface>) =
        adaptive {
            let! s = surf
            let! scalar = s.selectedScalar
            let! scalar' =
                match scalar with
                | AdaptiveSome m -> m.label |> AVal.map Some
                | AdaptiveNone -> AVal.constant None

            let! texture = s.primaryTexture
            let attr : AttributeParameters =
                {
                    selectedTexture = texture |> Option.map (fun x -> { texture = TextureReference.LegacyId x.index; channel = ChannelReference.NoChannelSelection })
                    selectedScalar  = scalar'
                }

            return attr
        }

    /// Model -> world of a surface: its placement, pre-transform and the flip/SketchFab
    /// conventions. Applied with `Sg.trafo`, so the geometry stays in local space and the
    /// MVP is composed on the CPU in double precision.
    let surfaceTrafo
        (surf           : AdaptiveSurface)
        (refsys         : AdaptiveReferenceSystem)
        (observedSystem : aval<Option<SpiceReferenceSystem>>)
        (observerSystem : aval<Option<ObserverSystem>>) =
        adaptive {
            let! fullTrafo = TransformationApp.fullTrafo surf.transformation refsys observedSystem observerSystem
            let! preTransform = surf.preTransform
            let! flipZ = surf.transformation.flipZ
            let! sketchFab = surf.transformation.isSketchFab
            if flipZ then
                return Trafo3d.Scale(1.0, 1.0, -1.0) * (fullTrafo * preTransform)
            else if sketchFab then
                // TODO https://github.com/pro3d-space/PRo3D/issues/117
                return Sg.switchYZTrafo
            else
                return (fullTrafo * preTransform)
        }

    /// The OPC patch nodes read six inherited attributes (OpcRenderingProperties.captureContext)
    /// that the Viewer's full surface chain sets per surface: footprint, projected images, body,
    /// cross section, lat/lon grid, secondary texture. Only some have a scene-root default, and
    /// the root default does not reach a surface rendered inside a render command (Sg.execute),
    /// so the plain surface sets all of them to "off".
    let withNeutralOpcAttributes (sg : ISg) : ISg =
        sg
        |> Sg.applyFootprint (AVal.constant M44d.Identity)
        |> SgExtensions.Sg.applyProjectedImages' (fun _ -> AVal.constant None)
        |> SgExtensions.Sg.applyBody (AVal.constant None)
        |> SgExtensions.Sg.applyCrossSection (AVal.constant None)
        |> SgExtensions.Sg.applyLatLonGrid (AVal.constant None)
        |> SecondaryTexture.Sg.applySecondaryTextureId (AVal.constant None)

    /// One OPC surface with the plain textured effect: triangle filter, CPU-composed stable
    /// MVP, diffuse texture. No events.
    let simpleSurfaceSg
        (surface        : AdaptiveSgSurface)
        (surfacesMap    : amap<Guid, AdaptiveLeafCase>)
        (frustum        : aval<Frustum>)
        (refsys         : AdaptiveReferenceSystem)
        (observedSystem : aval<Option<SpiceReferenceSystem>>)
        (observerSystem : aval<Option<ObserverSystem>>) : ISg<'msg> =

        adaptive {
            match! AMap.tryFind surface.surface surfacesMap with
            | Some (AdaptiveSurfaces surf) ->

                let createSg (sg : ISg) =
                    sg
                    |> withNeutralOpcAttributes
                    |> Sg.noEvents
                    |> Sg.cullMode(surf.cullMode)
                    |> Sg.fillMode(surf.fillMode)

                let triangleFilter = surf.triangleSize.value
                let trafo = surfaceTrafo surf refsys observedSystem observerSystem

                return
                    surface.sceneGraph
                    |> AVal.map createSg
                    |> Sg.dynamic
                    |> Sg.trafo trafo
                    |> Sg.uniform "MaxTriangleSize" triangleFilter
                    |> Sg.uniform "FilterTriangleEnabled" surf.filterByTriangleSize
                    // no distance-to-home filtering on the plain surface
                    |> Sg.uniform "FilterByDistance" (AVal.constant false)
                    |> Sg.uniform "FilterDistance" (AVal.constant 0.0f)
                    |> Sg.uniform "HomePositionViewSpace" (AVal.constant V3f.Zero)
                    |> Sg.onOff (surf.isVisible)
                    |> Sg.LodParameters( lodParameters (AVal.constant surf) refsys observedSystem observerSystem frustum )
                    |> Sg.AttributeParameters( attributeParameters (AVal.constant surf) )
                    |> Sg.noEvents
                    // the Viewer's own surface shaders (PRo3D.Shader): stable MVP composed on the
                    // CPU, triangle-size filter in view space, diffuse texture
                    |> Sg.effect [
                        Shader.stableTrafo           |> toEffect
                        Shader.triangleSizeFilter    |> toEffect
                        Shader.opcDiffuseTexture     |> toEffect
                    ]
            | _ ->
                return Sg.empty
        } |> Sg.dynamic

    /// `simpleSurfaceSg`, pickable by its bounding box, with pick events and a selection box.
    let pickableSurfaceSg
        (events         : SurfacePickEvents<'msg>)
        (selected       : aval<Option<Guid>>)
        (surface        : AdaptiveSgSurface)
        (surfacesMap    : amap<Guid, AdaptiveLeafCase>)
        (frustum        : aval<Frustum>)
        (refsys         : AdaptiveReferenceSystem)
        (observedSystem : aval<Option<SpiceReferenceSystem>>)
        (observerSystem : aval<Option<ObserverSystem>>) : ISg<'msg> =

        adaptive {
            match! AMap.tryFind surface.surface surfacesMap with
            | Some (AdaptiveSurfaces surf) ->
                let trafo = surfaceTrafo surf refsys observedSystem observerSystem

                let pickBox = (surface.globalBB, trafo) ||> AVal.map2 (fun bb t -> bb.Transformed t)

                let isSelected =
                    selected |> AVal.map (fun sel -> sel = Some surface.surface)

                let selectionBox =
                    Sg.wireBox (AVal.constant C4b.VRVisGreen) pickBox
                    |> Sg.noEvents
                    |> Sg.effect [
                        Shader.stableTrafo |> toEffect
                        DefaultSurfaces.vertexColor |> toEffect
                    ]
                    |> Sg.onOff isSelected

                return
                    simpleSurfaceSg surface surfacesMap frustum refsys observedSystem observerSystem
                    // picked by the placed bounding box; the click is then refined on the KdTrees
                    |> Aardvark.UI.SgFSharp.Sg.pickable' (pickBox |> AVal.map PickShape.Box)
                    |> Sg.withEvents (pickEvents events surf.name)
                    |> Sg.onOff surf.isVisible
                    |> Sg.andAlso selectionBox
            | _ ->
                return Sg.empty
        } |> Sg.dynamic

    /// One draw pass per surface priority group, each with fresh depth, then the depth-tested
    /// annotations, then the overlays on top.
    let renderCommands
        (surfaceGroups : alist<ISg<'msg>>)
        (overlayed     : ISg<'msg>)
        (depthTested   : ISg<'msg>) : alist<RenderCommand<'msg>> =
        alist {
            for sg in surfaceGroups do
                yield RenderCommand<_>.ClearDepth 1.0
                yield RenderCommand<_>.ClearDepth 1.0
                yield RenderCommand<_>.Render sg

            yield RenderCommand<_>.Render depthTested
            yield RenderCommand<_>.ClearDepth 1.0

            yield RenderCommand<_>.Render overlayed
        }
