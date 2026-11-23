namespace PRo3D.InstrumentVisualization

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.SceneGraph
open Aardvark.Rendering
open PRo3D.ImageMapping


/// The layout of the projection stack's colour-map texture (stackColormapAtlas),
/// shared by the shader (ImageProjection) that looks it up.
module ColormapAtlas =
    /// the number of ColorMap values (0 .. count - 1)
    [<Literal>]
    let count = 6
    /// texels per colour map
    [<Literal>]
    let segmentWidth = 640

type VisualizationProperties = 
    {
        visualizationRange : aval<Range1d>
        // TODO sophie
        dataType : aval<DataType>
        instrumentImage : aval<ITexture>
        colorMapping : aval<Option<ITexture>>
        projectionOpacity : aval<float>
        /// Run projected samples through the colour map and the min/max remap
        /// (true), or paint the image's own RGB untouched (false).
        useTransferFunction : aval<bool>
    }

module VisualizationProperties =
    let empty = 
        {
            visualizationRange = AVal.constant Range1d.Unit
            dataType = AVal.constant DataType.Float
            instrumentImage = DefaultTextures.checkerboard
            colorMapping = AVal.constant None
            projectionOpacity = AVal.constant 1.0
            useTransferFunction = AVal.constant true
        }

[<AutoOpen>]
module InstrumentImageVisualization =
    type Self = Self
    
    
    let getResourceStream (resourceName: string) () =
        let assembly = typeof<Self>.Assembly
        let resourcePath = assembly.GetName().Name + ".resources." + resourceName
        let s = assembly.GetManifestResourceStream(resourcePath)
        if isNull s then
            let names = assembly.GetManifestResourceNames()
            Log.warn "could not find resource, embeded names are: %A" names
            failwithf "could not find resource: %s" resourcePath
        else
            s

    let getColorMapTexture (name : string) = 
        StreamTexture(getResourceStream name) :> ITexture

    /// Every colour map side by side in one row texture, for the projection stack:
    /// each layer looks its own map up (ProjectedStackColorMap) instead of all of
    /// them sharing the selected image's. Segment i is the map whose ColorMap value
    /// is i, resampled to `ColormapAtlas.segmentWidth` texels. Laid out along X, so
    /// the lookup does not depend on how the backend orients image rows; one texel
    /// high and without mip maps, so neither filter blends neighbouring maps.
    let stackColormapAtlas : Lazy<ITexture> =
        lazy (
            let count = ColormapAtlas.count
            let width = ColormapAtlas.segmentWidth
            let atlas = PixImage<byte>(Col.Format.RGBA, V2i(width * count, 1))
            let mutable target = atlas.GetMatrix<C4b>()
            for i in 0 .. count - 1 do
                let name = ColorMap.getColorMapFileName (enum<ColorMap> i)
                use stream = getResourceStream name ()
                let source = PixImage.Load(stream).ToPixImage<byte>(Col.Format.RGBA)
                let pixels = source.GetMatrix<C4b>()
                let w = source.Size.X
                let y = int64 (source.Size.Y / 2)
                for x in 0 .. width - 1 do
                    // nearest source texel of this segment texel's centre
                    let sx = min (w - 1) (int ((float x + 0.5) * float w / float width))
                    target.[int64 (i * width + x), 0L] <- pixels.[int64 sx, y]
            PixTexture2d(PixImageMipMap [| atlas :> PixImage |], false) :> ITexture
        )

    let applyProperties (p : VisualizationProperties) (sg : ISg) = 
        sg 
        |> Sg.uniform "MinValue" (p.visualizationRange |> AVal.map _.Min)
        |> Sg.uniform "MaxValue" (p.visualizationRange |> AVal.map _.Max)
        |> Sg.uniform "UseFalseColor" (p.colorMapping |> AVal.map Option.isSome)
        |> Sg.texture "InstrumentImage" p.instrumentImage
        |> Sg.uniform "DataType" (p.dataType |> AVal.map int)
        |> Sg.uniform "ProjectedImageOpacity2" (p.projectionOpacity |> AVal.map (fun v -> v))
        |> Sg.uniform "ProjectedUseTransferFunction" p.useTransferFunction
        |> Sg.texture "ProjectedStackColormaps" (AVal.constant stackColormapAtlas.Value)
        |> Sg.texture "ColormapTexture" (
            p.colorMapping |> AVal.bind (function
                | None -> DefaultTextures.blackTex 
                | Some t -> AVal.constant t)
        )


module Shaders = 
    open FShade
    open Aardvark.Rendering.Effects

    let instrumentSampler = 
        sampler2d {
            texture uniform?InstrumentImage
            filter Filter.MinMagMipLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let colormapTextureSampler =
        sampler2d {
            texture uniform?ColormapTexture
            filter Filter.MinMagMipLinear
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
        }

    type UniformScope with
        member x.MinValue : float32 = uniform?MinValue
        member x.MaxValue : float32 = uniform?MaxValue
        member x.UseFalseColor : bool = uniform?UseFalseColor
        member x.DataType : int = uniform?DataType

    // Kept self-contained on purpose: any textual change to this function
    // changes the composed surface effect's identity, which invalidates the
    // on-disk shader cache -- every user then pays a full recompile of the
    // ~300 KB surface program on next start (which looks like surfaces
    // hanging for tens of seconds). The projection-stack shader inlines the
    // same remap with per-layer ranges (ImageProjection.fs) instead of
    // sharing a parameterized helper from here.
    [<ReflectedDefinition>]
    let remap (v : float32) =
        let remappedClampedNormalizedXInt16 =
            min uniform.MaxValue ((max uniform.MinValue (v * 65000.0f)) - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
        let remappedClampedNormalizedXFloat =
            (v - uniform.MinValue) / (uniform.MaxValue - uniform.MinValue)
        let remapClampNormalize =
            if uniform.UseFalseColor then
                colormapTextureSampler.Sample(V2f ((if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16), 0.0f))
            else
                V4f(
                    (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                    (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                    (if (uniform.DataType = 2) then remappedClampedNormalizedXFloat else remappedClampedNormalizedXInt16),
                    1.0f
                )
        remapClampNormalize

    let remapInstrumentImage (v : Vertex)  = 
        fragment {
            let instrumentValue = instrumentSampler.Sample(v.tc).X 
            return remap instrumentValue
        }



