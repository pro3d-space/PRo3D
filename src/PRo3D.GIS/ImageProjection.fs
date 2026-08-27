namespace PRo3D.Core

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.SceneGraph
open Aardvark.Rendering

module ImageProjection =

    module Shaders =

        open Aardvark.Base
    
        open FShade
        open Aardvark.Rendering.Effects


        type UniformScope with
            member x.ProjectedImageModelViewProjValid : bool = uniform?ProjectedImageModelViewProjValid
            member x.ProjectedImageModelViewProj : M44f = uniform?ProjectedImageModelViewProj
            member x.ProjectedImageOpacity : float32 = uniform?ProjectedImageOpacity2
            /// 1 flips generateNormal's face normal, 0 (the default when unset) leaves it.
            /// Set per OPC hierarchy from the CPU-estimated winding; see OpcSg.build.
            member x.NormalFlip : float32 = uniform?NormalFlip
            // The projection stack (multi-image projection), bottom -> top,
            // filled per patch by projectionUniformMap. Fixed-size uniform
            // arrays, NOT storage buffers: 32 * M44f = 2 KB sits far under the
            // 16 KB UBO floor of GL 4.1, so the same shader runs on macOS
            // (FShade emits SSBOs unconditionally, and macOS never got GL 4.3).
            // The CPU side hands over plain arrays; UniformWriters zero-fills
            // the tail and StackCount bounds the loop. The size type must match
            // ProjectedImages.maxCount in ProjectedImageList-Model.fs.
            member x.ProjectedStackTrafos : Arr<N<32>, M44f> = uniform?ProjectedStackTrafos
            member x.ProjectedStackMinMax : Arr<N<32>, V2f> = uniform?ProjectedStackMinMax
            member x.ProjectedStackCount : int = uniform?ProjectedStackCount
            /// InstrumentVisibilityMode.RelativeCount: tint fragments by how
            /// many stack layers cover them (projectedStackCoverage)
            member x.ProjectedStackCoverageEnabled : bool = uniform?ProjectedStackCoverageEnabled
            /// hover footprint (D5): the hovered image's projector, per patch
            member x.HoveredProjectionTrafo : M44f = uniform?HoveredProjectionTrafo
            member x.HoveredProjectionValid : bool = uniform?HoveredProjectionValid

        type Vertex = {
            [<Position>]    pos     : V4f
            [<Semantic("ProjectedImagePos")>] projectedPos : V4f
            [<Color>] c: V4f
            [<Semantic("BodyLocalPos")>] localPos : V4f
            [<Semantic("LocalNormal")>] localNormalNumericallyUnstable : V3f
            [<Normal>] n : V3f
        }

        let private projectedTexture =
            sampler2d {
                texture uniform?ProjectedTexture
                filter Filter.MinMagMipLinear
                addressU WrapMode.Border
                addressV WrapMode.Border
                borderColor C4f.White
            }


        let stableImageProjectionTrafo (v : Vertex) =
            vertex {
                return { v with projectedPos = uniform.ProjectedImageModelViewProj * v.pos; localPos = v.pos; }
            }

        /// LEGACY single-image projection -- the viewer renders the projection
        /// STACK (stableImageProjectionStack) instead; only the standalone
        /// testbeds (TestViewer, ProjectionTestbed) still compose this.
        let stableImageProjection (v : Vertex) =
            fragment {
                let p = v.projectedPos.XYZ / v.projectedPos.W
                let tc = V3f(0.5, 0.5,0.5) + V3f(0.5, 0.5, 0.5) * p.XYZ
                let inRange = Vec.allGreaterOrEqual tc V3f.OOO && Vec.allSmallerOrEqual tc.XYZ V3f.III
                let borderWidth = 0.01f 

                let normal = uniform.ProjectedImageModelViewProj.TransformDir(v.localNormalNumericallyUnstable) |> Vec.normalize

                let c = 
                    if uniform.ProjectedImageModelViewProjValid && inRange && normal.Z < 0.0f then
                        let AFC2 = V2f(tc.X, tc.Y)
                        let c = projectedTexture.Sample(AFC2).X |>  PRo3D.InstrumentVisualization.Shaders.remap
                        let xBorder = (smoothstep 0.0f borderWidth tc.X) * smoothstep 1.0f (1.0f - borderWidth) tc.X 
                        let yBorder = (smoothstep 0.0f borderWidth tc.Y) * smoothstep 1.0f (1.0f - borderWidth) tc.Y
                        let borderFactor = xBorder * yBorder
                        let borderColor = V3f(0.0, 1.0, 0.0)
                        let a = clamp 0.0f 1.0f uniform.ProjectedImageOpacity
                        let blendedProjected = c.XYZ * a + (1.0f - a) * v.c.XYZ
                        let borderImage = blendedProjected.XYZ * borderFactor + borderColor * (1.0f - borderFactor)
                        V4f(borderImage.XYZ, 1.0f) 
                    else
                        v.c
                return { v with c = c }
            }

        let private projectedStackTexture =
            sampler2dArray {
                texture uniform?ProjectedStackTextures
                filter Filter.MinMagMipLinear
                addressU WrapMode.Border
                addressV WrapMode.Border
                borderColor C4f.White
            }

        // same texture + state as ColorMapping's colormapTextureSampler; local
        // because the stack shader inlines its remap (see the NOTE there)
        let private stackColormapSampler =
            sampler2d {
                texture uniform?ColormapTexture
                filter Filter.MinMagMipLinear
                addressU WrapMode.Clamp
                addressV WrapMode.Clamp
            }

        type UniformScope with
            member x.StackUseFalseColor : bool = uniform?UseFalseColor
            member x.StackDataType : int = uniform?DataType

        /// The projection stack: layers bottom -> top, painter's order -- the
        /// TOPMOST layer that covers a fragment with a projector-facing normal
        /// wins (walked top-down with an early-out, so the common single-cover
        /// case samples once). Opaque stacking; the global opacity only blends
        /// the stack's result with the underlying terrain color. Each layer
        /// remaps its sample with its own min/max (colormap/false-color/data
        /// type are global, D2). Subsumes the old single-image projection: a
        /// stack of one behaves identically, minus the green border (the
        /// hovered layer gets an outline in a later phase instead).
        let stableImageProjectionStack (v : Vertex) =
            fragment {
                let mutable color = v.c
                let mutable covered = false
                let count = uniform.ProjectedStackCount
                for j in 0 .. count - 1 do
                    let i = count - 1 - j
                    if not covered then
                        let ndc = uniform.ProjectedStackTrafos.[i] * v.localPos
                        let p = ndc.XYZ / ndc.W
                        let tc = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * p
                        // an unresolved layer's zero matrix yields NaN here and
                        // fails the range test -- the slot simply never covers
                        let inRange = Vec.allGreaterOrEqual tc V3f.OOO && Vec.allSmallerOrEqual tc V3f.III
                        let normal = uniform.ProjectedStackTrafos.[i].TransformDir(v.localNormalNumericallyUnstable) |> Vec.normalize
                        if inRange && normal.Z < 0.0f then
                            let value = projectedStackTexture.Sample(V2f(tc.X, tc.Y), i).X
                            let minMax = uniform.ProjectedStackMinMax.[i]
                            // per-layer remap, inlined rather than shared with
                            // ColorMapping.remap: reworking that function would
                            // change the legacy effect's identity and force a
                            // full shader-cache recompile on users (see the
                            // note there)
                            let normalizedInt16 =
                                min minMax.Y ((max minMax.X (value * 65000.0f)) - minMax.X) / (minMax.Y - minMax.X)
                            let normalizedFloat =
                                (value - minMax.X) / (minMax.Y - minMax.X)
                            let normalized =
                                if uniform.StackDataType = 2 then normalizedFloat else normalizedInt16
                            let mapped =
                                if uniform.StackUseFalseColor then
                                    stackColormapSampler.Sample(V2f(normalized, 0.0f))
                                else
                                    V4f(normalized, normalized, normalized, 1.0f)
                            let a = clamp 0.0f 1.0f uniform.ProjectedImageOpacity
                            color <- V4f(mapped.XYZ * a + (1.0f - a) * v.c.XYZ, 1.0f)
                            covered <- true
                return { v with c = color }
            }

        /// Hover footprint outline (D5): a green border where the HOVERED
        /// image's projector footprint crosses the surface -- drawn for one
        /// image only, so the stack loop stays free of per-layer border work
        /// (the old single-image shader's always-on green border is subsumed
        /// by this hover-only outline).
        let hoveredProjectionOutline (v : Vertex) =
            fragment {
                if uniform.HoveredProjectionValid then
                    let ndc = uniform.HoveredProjectionTrafo * v.localPos
                    let p = ndc.XYZ / ndc.W
                    let tc = V3f(0.5f, 0.5f, 0.5f) + V3f(0.5f, 0.5f, 0.5f) * p
                    let inRange = Vec.allGreaterOrEqual tc V3f.OOO && Vec.allSmallerOrEqual tc V3f.III
                    let normal = uniform.HoveredProjectionTrafo.TransformDir(v.localNormalNumericallyUnstable) |> Vec.normalize
                    if inRange && normal.Z < 0.0f then
                        let borderWidth = 0.01f
                        let xBorder = (smoothstep 0.0f borderWidth tc.X) * smoothstep 1.0f (1.0f - borderWidth) tc.X
                        let yBorder = (smoothstep 0.0f borderWidth tc.Y) * smoothstep 1.0f (1.0f - borderWidth) tc.Y
                        let borderFactor = xBorder * yBorder
                        let borderColor = V3f(0.0f, 1.0f, 0.0f)
                        let c = v.c.XYZ * borderFactor + borderColor * (1.0f - borderFactor)
                        return { v with c = V4f(c, 1.0f) }
                    else
                        return v
                else
                    return v
            }

        [<ReflectedDefinition>]
        let isBorder (tc : V3f) =
            let borderWidth = 0.0001f 
            //let xBorder = (smoothstep 0.0f borderWidth tc.X) * smoothstep 1.0f (1.0f - borderWidth) tc.X 
            //let yBorder = (smoothstep 0.0f borderWidth tc.Y) * smoothstep 1.0f (1.0f - borderWidth) tc.Y
            //let borderFactor = xBorder * yBorder
            let borderX = tc.X < borderWidth || tc.X > 1.0f - borderWidth 
            let borderY = tc.Y < borderWidth || tc.Y > 1.0f - borderWidth
            borderX || borderY

        [<ReflectedDefinition>]
        let mapClippedProjectionsToColor (validCount : int) (totalCount : int) =
            let ratio = float32 validCount / float32 totalCount
            let color = 
                if ratio < 0.1f then V3f(0.0, 0.0, 1.0) // Blue
                elif ratio < 0.2f then V3f(0.0, 1.0, 1.0) // Cyan
                elif ratio < 0.3f then V3f(0.0, 1.0, 0.0) // Green
                elif ratio < 0.4f then V3f(1.0, 1.0, 0.0) // Yellow
                else V3f(1.0, 0.0, 0.0) // Red
            color

        [<ReflectedDefinition>]
        let mapClippedProjectionsToColor2 (validCount : int) (totalCount : int) =
            let ratio = float32 validCount / float32 totalCount
            let color = 
                if ratio < 0.1f then V3f(0.0, 0.0, 1.0) // Blue
                elif ratio < 0.2f then V3f(0.0, 0.5, 1.0) // Light Blue
                elif ratio < 0.3f then V3f(0.0, 1.0, 1.0) // Cyan
                elif ratio < 0.4f then V3f(0.0, 1.0, 0.5) // Light Green
                elif ratio < 0.5f then V3f(0.0, 1.0, 0.0) // Green
                elif ratio < 0.6f then V3f(0.5, 1.0, 0.0) // Yellow-Green
                elif ratio < 0.7f then V3f(1.0, 1.0, 0.0) // Yellow
                elif ratio < 0.8f then V3f(1.0, 0.5, 0.0) // Orange
                elif ratio < 0.9f then V3f(1.0, 0.0, 0.0) // Red
                else V3f(0.5, 0.0, 0.0) // Dark Red
            color

        /// Coverage view (InstrumentVisibilityMode.RelativeCount): tint each
        /// fragment by how many STACK layers cover it. The port of the old
        /// localImageProjections storage-buffer shader onto the bounded
        /// Arr<N<32>> uniform arrays -- same coverage test, but over the
        /// projection stack (which is what gets rendered) instead of the whole
        /// library, and no SSBO, so it runs on GL 4.1/macOS and the
        /// limitedShaderCapabilities platform split is gone.
        let projectedStackCoverage (v : Vertex) =
            fragment {
                if uniform.ProjectedStackCoverageEnabled && uniform.ProjectedStackCount > 0 then
                    let mutable clippedCount = 0
                    for i in 0 .. uniform.ProjectedStackCount - 1 do
                        let ndc = uniform.ProjectedStackTrafos.[i] * v.localPos
                        let normal = uniform.ProjectedStackTrafos.[i].TransformDir(v.localNormalNumericallyUnstable).Normalized
                        let p = ndc.XYZ / ndc.W
                        let tc = V3f(0.5, 0.5, 0.5) + V3f(0.5, 0.5, 0.5) * p.XYZ
                        // tc.Z too, else geometry behind the near plane counts as covered
                        let clipped = Vec.anyGreater tc V3f.III || Vec.anySmaller tc V3f.OOO
                        let onRightSide = normal.Z < 0.0f
                        if not onRightSide || clipped then
                            clippedCount <- clippedCount + 1

                    if clippedCount < uniform.ProjectedStackCount then
                        let color = mapClippedProjectionsToColor2 (uniform.ProjectedStackCount - clippedCount) uniform.ProjectedStackCount
                        let c = v.c.XYZ * 0.8f + color * 0.2f
                        return V4f(c, 1.0f)
                    else
                        return v.c
                else
                    return v.c
            }

        type NormalVertex = {
            [<Position>] pos : V4f
            // Body-local position, stashed before stableTrafo overwrites [<Position>]
            // with clip space. The face normal is built from this so the front-facing
            // test does not depend on the render camera.
            [<Semantic("BodyLocalPos")>] localPos : V4f
            [<Semantic("LocalNormal")>] localNormal : V3f
            [<Normal>] n : V3f
            [<SourceVertexIndex>] i : int
        }


        let generateNormal (t : Triangle<NormalVertex>) =
            triangle {
                let p0 = t.P0.localPos.XYZ
                let p1 = t.P1.localPos.XYZ
                let p2 = t.P2.localPos.XYZ

                let edge1 = p1 - p0
                let edge2 = p2 - p0

                // operand order matters: edge2 edge1 points the normal into the body
                let normal = Vec.cross edge1 edge2 |> Vec.normalize

                yield { t.P0 with localNormal = normal; i = 0 }
                yield { t.P1 with localNormal = normal; i = 1 }
                yield { t.P2 with localNormal = normal; i = 2 }
            }

        // Optional per-dataset winding correction, composed AFTER generateNormal only
        // where NormalFlip is bound (the projection testbed). NormalFlip's sign still
        // follows the source data's triangle winding, which two OPCs can disagree on; it
        // is estimated once per dataset on the CPU (OpcSg.estimateNormalFlip). Kept out of
        // generateNormal itself because that shader is in the main viewer's always-on OPC
        // effect stack, where an unbound uniform makes FShade throw.
        let applyNormalFlip (v : NormalVertex) =
            vertex {
                return { v with localNormal = if uniform.NormalFlip > 0.5f then -v.localNormal else v.localNormal }
            }

        let useVertexNormals (v : NormalVertex) =
            vertex {
                return { v with localNormal = v.n.Normalized }
            }

        let flipNormals (v : NormalVertex) =
            vertex {
                return { v with localNormal = -v.localNormal }
            }

module ImageProjectionTrafoSceneGraph =
    open Aardvark.Base.Ag
    open Aardvark.SceneGraph.Semantics.TrafoExtensions

    type PlanetApplicator(child : ISg, planet : string) =
        inherit Sg.AbstractApplicator(child)
        member x.Planet = planet
        
    [<Rule>]
    type PlanetSemantics() =
        member x.Planet(app : PlanetApplicator, scope : Ag.Scope) =
            app.Child?Planet <- app.Planet
        

    type ProjectedImageApplicator(child : ISg, viewProjection : string -> aval<Option<Trafo3d>>) =
        inherit Sg.AbstractApplicator(child)
        member x.ViewProjection = viewProjection

    [<Rule>]
    type ProjectedImageSemantics() =
        member x.ProjectedImageModelViewProj(app : ProjectedImageApplicator, scope : Ag.Scope) =
            let planet : string = scope?Planet
            let projectionTrafo = app.ViewProjection planet
            let modelTrafo = scope.ModelTrafo 
            let possiblyTrafo = 
                projectionTrafo |> AVal.bind (function
                    | None -> AVal.constant None
                    | Some vp -> 
                        AVal.map (fun m -> m *  vp |> Some) modelTrafo
                )
            let trafo = possiblyTrafo |> AVal.map (Option.defaultValue Trafo3d.Identity)
            app.Child?ProjectedImageModelViewProj <- trafo

     
module Sg = 
    open ImageProjectionTrafoSceneGraph

    let applyPlanet (planet : string) (sg : ISg) =
        PlanetApplicator(sg, planet)

    let applyProjectedImage (viewProjTrafo : string -> aval<Option<Trafo3d>>) (sg : ISg) =
        ProjectedImageApplicator(sg, viewProjTrafo) :> ISg