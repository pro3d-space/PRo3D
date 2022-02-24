namespace PRo3D.Provenance.Abstraction.Presentation

open System

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive

open Adaptify

open PRo3D
open PRo3D.Core
open PRo3D.Core.Surface

type RenderingParams = ViewConfigModel

type OSurface = Surface

[<ModelType>]
type Surface = {
    fillMode        : FillMode
    cullMode        : CullMode
    quality         : float
    triangleSize    : float
    scaling         : float
    scalarLayer     : ScalarLayer option
    textureLayer    : TextureLayer option
    colorCorrection : ColorCorrection
}

type OSurfaceParams = GroupsModel
type SurfaceParams = HashMap<Guid, Surface>

[<ModelType>]
type PresentationParams = {
    rendering : RenderingParams
    surfaces : SurfaceParams
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Surface =
    
    let create (s : OSurface) : Surface = {
        fillMode        = s.fillMode
        cullMode        = s.cullMode
        quality         = s.quality.value
        triangleSize    = s.triangleSize.value
        scaling         = s.scaling.value
        scalarLayer     = s.selectedScalar
        textureLayer    = s.selectedTexture
        colorCorrection = s.colorCorrection
    }

    let restore (orig : OSurface) (s : Surface) : OSurface = {
        orig with 
            fillMode          = s.fillMode
            cullMode          = s.cullMode
            quality           = { orig.quality with value = s.quality }
            triangleSize      = { orig.triangleSize with value = s.triangleSize }
            scaling           = { orig.scaling with value = s.scaling }
            selectedScalar    = s.scalarLayer
            selectedTexture   = s.textureLayer
            colorCorrection   = s.colorCorrection
    }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SurfaceParams =
    
    let create (s : OSurfaceParams) : SurfaceParams =
        s.flat 
        |> HashMap.map (fun _ v ->
            v |> Leaf.toSurface |> Surface.create
        )

    let restore (current : OSurfaceParams) (surfaces : SurfaceParams) : OSurfaceParams =
        let f = 
            current.flat 
            |> HashMap.map (fun k s ->
                match surfaces |> HashMap.tryFind k with
                | None   -> s
                | Some c ->
                    c 
                    |> Surface.restore (Leaf.toSurface s) 
                    |> Surfaces
            )

        { current with flat = f }