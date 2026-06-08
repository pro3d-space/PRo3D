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
            member x.ProjectedImagesLocalTrafos : M44f[] = uniform?StorageBuffer?ProjectedImagesLocalTrafos
            member x.ProjectedImagesCount : int = uniform?ProjectedImagesLocalTrafosCount
            member x.ProjectedImageOpacity : float32 = uniform?ProjectedImageOpacity2

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

        let localImageProjections (v : Vertex) = 
            fragment {
                let mutable clippedCount = 0
                for i in 0 .. uniform.ProjectedImagesCount - 1 do
                    let ndc = uniform.ProjectedImagesLocalTrafos[i] * v.localPos
                    let normal = uniform.ProjectedImagesLocalTrafos[i].TransformDir(v.localNormalNumericallyUnstable).Normalized
                    let p = ndc.XYZ / ndc.W
                    let tc = V3f(0.5, 0.5, 0.5) + V3f(0.5, 0.5, 0.5) * p.XYZ
                    let clipped = Vec.anyGreater tc.XY V2f.II || Vec.anySmaller tc.XY V2f.OO
                    let onRightSide =  normal.Z < 0.0f
                    if not onRightSide || clipped then
                        clippedCount <- clippedCount + 1

                if clippedCount < uniform.ProjectedImagesCount then 
                    let color = mapClippedProjectionsToColor2 (uniform.ProjectedImagesCount  - clippedCount) uniform.ProjectedImagesCount 
                    let c = v.c.XYZ * 0.8f + color * 0.2f
                    return V4f(c, 1.0f)
                else 
                    return v.c
            }

        type NormalVertex = {
            [<Position>] pos : V4f
            [<Semantic("LocalNormal")>] localNormal : V3f
            [<Normal>] n : V3f
            [<SourceVertexIndex>] i : int
        }


        let generateNormal (t : Triangle<NormalVertex>) =
            triangle {
                let p0 = t.P0.pos.XYZ
                let p1 = t.P1.pos.XYZ
                let p2 = t.P2.pos.XYZ

                let edge1 = p1 - p0
                let edge2 = p2 - p0

                let normal = Vec.cross edge2 edge1 |> Vec.normalize

                yield { t.P0 with localNormal = normal; i = 0 }
                yield { t.P1 with localNormal = normal; i = 1 }
                yield { t.P2 with localNormal = normal; i = 2 }
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